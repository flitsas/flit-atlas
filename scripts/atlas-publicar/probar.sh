#!/usr/bin/env bash
# Prueba reproducible del workflow reutilizable .github/workflows/atlas-publicar.yml (HU 3.3, ATLAS-ADR-0035 §7).
#
# 1. Corre el bloque de decisión del workflow (entre «# >>> decision» y «# <<< decision») TAL CUAL, sobre las specs
#    ficticias de scripts/atlas-publicar/fixtures/, y comprueba el criterio de aceptación: un breaking sin subir la
#    MAJOR falla; subiendo la MAJOR pasa (y el workflow sigue a publicar).
# 2. Comprueba que las copias de los rulesets de vacuum embebidas en el workflow coinciden con contracts/vacuum/.
#
# Requiere bash, node y oasdiff (la misma versión del CI, 1.33.0) en el PATH. Lo corre el job «contratos» de ci.yml.
# Uso: scripts/atlas-publicar/probar.sh
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflow="$raiz/.github/workflows/atlas-publicar.yml"
fixtures="$raiz/scripts/atlas-publicar/fixtures"
trabajo="$(mktemp -d)"
trap 'rm -rf "$trabajo"' EXIT

for herramienta in node oasdiff; do
  command -v "$herramienta" > /dev/null || { echo "Falta $herramienta en el PATH" >&2; exit 2; }
done

# Quita la sangría del marcador a cada línea del bloque (YAML la quita igual al leer el «run: |»).
extraer() {
  awk -v desde="$1" -v hasta="$2" '
    index($0, desde) && !dentro { dentro = 1; match($0, /^ */); sangria = RLENGTH; next }
    index($0, hasta) && dentro { exit }
    dentro { print (length($0) >= sangria ? substr($0, sangria + 1) : "") }
  ' "$workflow"
}

extraer '# >>> decision' '# <<< decision' > "$trabajo/decision.sh"
[ -s "$trabajo/decision.sh" ] || { echo "No se encontró el bloque de decisión en $workflow" >&2; exit 1; }

fallos=0
casos=0

# caso <nombre> <base o ""> <nuevo> <modo> <rama destino> <salida esperada> <decisión esperada>
caso() {
  local nombre="$1" base="$2" nuevo="$3" modo="$4" destino="$5" salida_esperada="$6" decision_esperada="$7"
  local dir="$trabajo/caso-$casos" estado decision
  casos=$((casos + 1))
  mkdir -p "$dir"
  set +e
  (
    cd "$dir"
    BASE="${base:+$fixtures/$base}" NUEVO="$fixtures/$nuevo" MODO_BREAKING="$modo" DESTINO="$destino" RAMAS_ESTABLES="release" \
      GITHUB_OUTPUT="$dir/salida" GITHUB_STEP_SUMMARY="$dir/resumen.md" bash "$trabajo/decision.sh" > "$dir/log" 2>&1
  )
  estado=$?
  set -e
  decision="$(sed -n 's/^decision=//p' "$dir/salida" 2> /dev/null | tail -n 1)"
  if [ "$estado" = "$salida_esperada" ] && [ "$decision" = "$decision_esperada" ]; then
    echo "ok   $nombre (salida $estado, $decision)"
  else
    echo "FALLA $nombre: salida $estado (se esperaba $salida_esperada), decisión «$decision» (se esperaba «$decision_esperada»)"
    sed 's/^/      /' "$dir/log"
    fallos=$((fallos + 1))
  fi
}

caso "sin base: primera publicación" "" compatible.yaml error develop 0 sin_base
caso "cambio compatible (MINOR)" base.yaml compatible.yaml error develop 0 compatible
caso "breaking sin subir la MAJOR falla" base.yaml breaking-sin-major.yaml error develop 1 breaking_sin_major
caso "breaking sin subir la MAJOR falla también hacia release" base.yaml breaking-sin-major.yaml error release 1 breaking_sin_major
caso "breaking con la MAJOR subida pasa" base.yaml breaking-con-major.yaml error develop 0 breaking_con_major
caso "breaking bajando la versión falla" base.yaml breaking-version-menor.yaml error develop 1 breaking_sin_major
caso "breaking sin MAJOR en modo advertencia pasa avisando" base.yaml breaking-sin-major.yaml advertencia develop 0 breaking_aceptado
caso "misma versión con otro contenido hacia release falla" base.yaml misma-version.yaml error release 1 misma_version
caso "misma versión con otro contenido hacia develop pasa (lleva prerelease)" base.yaml misma-version.yaml error develop 0 compatible
caso "el mismo spec no tiene cambios" base.yaml base.yaml error release 0 compatible

# El changelog que se publica es la salida JSON de oasdiff (un arreglo) y trae el breaking.
if ! node -e 'const c = JSON.parse(require("node:fs").readFileSync(process.argv[1], "utf8")); if (!Array.isArray(c) || !c.some((x) => x.level === 3)) process.exit(1);' \
  "$trabajo/caso-2/changelog.json"; then
  echo "FALLA el changelog del breaking no es un arreglo con un cambio de nivel 3"
  fallos=$((fallos + 1))
else
  echo "ok   changelog JSON de oasdiff con el breaking"
fi

# Copias de los rulesets embebidas en el workflow = los originales de contracts/vacuum/.
for par in "COMPARTIBLE:openapi-compartible.yaml" "INTERNO:openapi.yaml" "ASYNCAPI:asyncapi.yaml"; do
  marca="${par%%:*}"
  original="$raiz/contracts/vacuum/${par#*:}"
  extraer "<<'REGLAS_${marca}'" "REGLAS_${marca}" > "$trabajo/regla.yaml"
  if diff -q <(tr -d '\r' < "$original") "$trabajo/regla.yaml" > /dev/null; then
    echo "ok   ruleset embebido ${par#*:}"
  else
    echo "FALLA el ruleset embebido REGLAS_${marca} no coincide con contracts/vacuum/${par#*:}:"
    diff <(tr -d '\r' < "$original") "$trabajo/regla.yaml" | head -20 | sed 's/^/      /'
    fallos=$((fallos + 1))
  fi
done

echo
if [ "$fallos" -gt 0 ]; then
  echo "$fallos fallas."
  exit 1
fi
echo "Todo bien."
