#!/usr/bin/env bash
# Genera los certificados de firma y de cifrado del servidor de tokens de Atlas (HU 1.3c, ATLAS-ADR-0024) para un
# ambiente, como .pfx protegidos con una clave aleatoria. Los archivos y la clave van a un SealedSecret de flit-gitops;
# nunca a este repo. Requiere openssl 3.
#
# Uso:  scripts/generar-certificados-tokens.sh <ambiente> <carpeta-de-salida> [dias-de-vigencia] [inicio]
#   ambiente           qa o pdn (va en el nombre del certificado)
#   carpeta-de-salida  fuera del repo, p. ej. una carpeta temporal que borras después de sellar
#   dias-de-vigencia   por defecto 400 (rotación cada 6 meses con margen)
#   inicio             opcional, AAAAMMDDhhmmssZ: el certificado no vale antes (para publicarlo en el JWKS antes de
#                      que empiece a firmar; requiere openssl 3.4 o posterior)
set -euo pipefail
# En Git Bash (Windows), evita que MSYS convierta «/CN=…» en una ruta.
export MSYS_NO_PATHCONV=1

ambiente="${1:?Indica el ambiente (qa o pdn)}"
salida="${2:?Indica la carpeta de salida (fuera del repo)}"
dias="${3:-400}"
inicio="${4:-}"

case "$ambiente" in qa|pdn) ;; *) echo "Ambiente inválido: $ambiente (qa o pdn)" >&2; exit 1 ;; esac
raiz="$(git rev-parse --show-toplevel 2>/dev/null || true)"
destino="$(mkdir -p "$salida" && cd "$salida" && pwd)"
if [[ -n "$raiz" && "$destino" == "$raiz"* ]]; then
  echo "La carpeta de salida está dentro del repo: elige otra." >&2
  exit 1
fi
if command -v cygpath >/dev/null 2>&1; then
  # Git Bash: openssl es un binario de Windows y necesita la ruta en su forma (C:/…).
  destino="$(cygpath -m "$destino")"
fi

sello="$(date -u +%Y%m%d)"
umask 077
clave="$(openssl rand -hex 32)"
printf '%s' "$clave" > "$destino/clave-certificados-tokens.txt"

generar() {
  local uso="$1" extension="$2"
  local nombre="tokens-${uso}-${ambiente}-${sello}"
  local inicio_args=()
  if [[ -n "$inicio" ]]; then
    inicio_args=(-not_before "$inicio")
  fi
  openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days "$dias" "${inicio_args[@]}" \
    -subj "/CN=Flit Atlas tokens ${uso} ${ambiente}/O=FLIT S.A.S." \
    -addext "keyUsage=critical,${extension}" -addext "basicConstraints=critical,CA:FALSE" \
    -keyout "$destino/$nombre.key" -out "$destino/$nombre.crt" 2>/dev/null \
    || { echo "openssl req falló para el certificado de ${uso}." >&2; exit 1; }
  openssl pkcs12 -export -inkey "$destino/$nombre.key" -in "$destino/$nombre.crt" \
    -out "$destino/$nombre.pfx" -passout "file:$destino/clave-certificados-tokens.txt"
  rm -f "$destino/$nombre.key"
  echo "  $nombre.pfx (huella SHA-256: $(openssl x509 -in "$destino/$nombre.crt" -noout -fingerprint -sha256 | cut -d= -f2))"
}

echo "Certificados en $destino:"
generar firma digitalSignature
generar cifrado keyEncipherment
echo "Clave de los .pfx: $destino/clave-certificados-tokens.txt"
echo "Siguiente paso: sellar los .pfx y la clave con kubeseal en flit-gitops y borrar esta carpeta (ver ATLAS-ADR-0024)."
