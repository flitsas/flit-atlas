#!/usr/bin/env bash
# Prueba reproducible del workflow reutilizable .github/workflows/atlas-publicar-docs.yml (HU 5.2, ATLAS-ADR-0053 §4): corre el
# bloque de validación («# >>> validar») TAL CUAL sobre archivos ficticios y comprueba que deja pasar lo válido, que falla con
# lo inválido y que salta las plantillas y lo que no es Markdown. Requiere bash y node. Lo corre el job «contratos» de ci.yml.
# Uso: scripts/atlas-publicar/probar-docs.sh
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflow="$raiz/.github/workflows/atlas-publicar-docs.yml"
trabajo="$(mktemp -d)"
trap 'rm -rf "$trabajo"' EXIT

command -v node > /dev/null || { echo "Falta node en el PATH" >&2; exit 2; }

# Quita la sangría del marcador a cada línea del bloque (YAML la quita igual al leer el «run: |»).
awk '
  index($0, "# >>> validar") && !dentro { dentro = 1; match($0, /^ */); sangria = RLENGTH; next }
  index($0, "# <<< validar") && dentro { exit }
  dentro { print (length($0) >= sangria ? substr($0, sangria + 1) : "") }
' "$workflow" > "$trabajo/validar.sh"
[ -s "$trabajo/validar.sh" ] || { echo "No se encontró el bloque de validación en $workflow" >&2; exit 1; }

frente=$'---\naplicativo: api-demo\ntipo: guia_funcional\ntitulo: Guía\nvisibilidad: interno_negocio\naudiencias: [Todos]\nresponsable: Persona Demo\n---\n\n'
fallos=0

# caso <nombre> <salida esperada> <contenido del archivo>
caso() {
  local nombre="$1" esperada="$2" contenido="$3" dir estado=0
  dir="$trabajo/$nombre"
  mkdir -p "$dir/docs/funcional"
  printf '%s' "$contenido" > "$dir/docs/funcional/doc.md"
  (cd "$dir" && RUNNER_TEMP=tmp CARPETAS="docs/funcional docs/no-existe" bash "$trabajo/validar.sh") > "$dir/salida.txt" 2>&1 || estado=$?
  if [ "$estado" -ne "$esperada" ]; then
    echo "FALLA $nombre: salió con $estado y se esperaba $esperada"
    cat "$dir/salida.txt"
    fallos=$((fallos + 1))
  else
    echo "ok $nombre"
  fi
}

caso valido 0 "${frente}# Título"$'\n\n<Contrato id="api" />\n\n<Alerta tipo="aviso">\nTexto\n</Alerta>\n\n[enlace](https://ejemplo.com) y `{codigo}`\n\n```js\nimport a from "b"; const c = {};\n```\n'
caso sin_frente 1 "# Título"
caso falta_campo 1 $'---\naplicativo: api-demo\n---\n\nHola\n'
caso expresion 1 "${frente}Valor: {1 + 1}"
caso importacion 1 "${frente}import x from 'y'"
caso html 1 "${frente}<script>alert(1)</script>"
caso menor_suelto 1 "${frente}a < b"
caso enlace 1 "${frente}[x](javascript:alert(1))"

# Las plantillas (carpetas o archivos con «_») y lo que no es Markdown no se revisan ni se publican.
dir="$trabajo/recorrido"
mkdir -p "$dir/docs/funcional/_plantillas" "$dir/docs/funcional/flito"
printf '%s' "# Sin front-matter" > "$dir/docs/funcional/_plantillas/ficha.md"
printf '%s' "# Sin front-matter" > "$dir/docs/funcional/_borrador.md"
printf '%s' "{no es markdown}" > "$dir/docs/funcional/flito/datos.json"
printf '%s' "${frente}Hola" > "$dir/docs/funcional/flito/b.md"
printf '%s' "${frente}Hola" > "$dir/docs/funcional/flito/a.mdx"
(cd "$dir" && RUNNER_TEMP=tmp CARPETAS="docs/funcional" bash "$trabajo/validar.sh") > "$dir/salida.txt" 2>&1 || { echo "FALLA recorrido"; cat "$dir/salida.txt"; fallos=$((fallos + 1)); }
rutas="$(cd "$dir" && node -p "JSON.stringify(JSON.parse(require('node:fs').readFileSync('tmp/atlas-docs/rutas.json','utf8')).rutas)")"
if [ "$rutas" = '["docs/funcional/flito/a.mdx","docs/funcional/flito/b.md"]' ]; then
  echo "ok recorrido"
else
  echo "FALLA recorrido: $rutas"
  fallos=$((fallos + 1))
fi

if [ "$fallos" -gt 0 ]; then
  echo "$fallos casos fallaron" >&2
  exit 1
fi
echo "Todos los casos pasaron."
