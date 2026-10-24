#!/usr/bin/env bash
# Prueba reproducible del workflow reutilizable .github/workflows/atlas-publicar.yml (HU 3.3, ATLAS-ADR-0035 §7).
#
# 1. Corre el bloque de decisión del workflow (entre «# >>> decision» y «# <<< decision») TAL CUAL, sobre las specs
#    ficticias de scripts/atlas-publicar/fixtures/, y comprueba el criterio de aceptación: un breaking sin subir la
#    MAJOR falla; subiendo la MAJOR pasa (y el workflow sigue a publicar).
# 2. Corre el bloque que resuelve las $ref a otros archivos de AsyncAPI («# >>> empaquetar», HU 3.10) sobre las specs
#    ficticias de scripts/atlas-publicar/fixtures/asyncapi/: resuelve las externas, deja las locales, no toca un spec sin
#    externas y falla con una $ref rota.
# 3. Comprueba que las copias de los rulesets de vacuum embebidas en el workflow coinciden con contracts/vacuum/.
#
# Requiere bash, node, yq (mikefarah/yq v4, el de los runners de GitHub) y oasdiff (la misma versión del CI, 1.33.0) en el
# PATH. Lo corre el job «contratos» de ci.yml.
# Uso: scripts/atlas-publicar/probar.sh
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflow="$raiz/.github/workflows/atlas-publicar.yml"
fixtures="$raiz/scripts/atlas-publicar/fixtures"
trabajo="$(mktemp -d)"
trap 'rm -rf "$trabajo"' EXIT

for herramienta in node oasdiff yq; do
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

# Resolver las $ref a otros archivos (AsyncAPI): corre el bloque del workflow desde la raíz del repo, como en la CI.
extraer '# >>> empaquetar' '# <<< empaquetar' > "$trabajo/empaquetar.sh"
[ -s "$trabajo/empaquetar.sh" ] || { echo "No se encontró el bloque empaquetar en $workflow" >&2; exit 1; }

# empaquetar <nombre> <tipo> <ruta relativa a la raíz> <salida esperada>; deja SPEC en $spec_resultante.
empaquetar() {
  local nombre="$1" tipo="$2" ruta="$3" salida_esperada="$4" dir="$trabajo/empaquetar-$casos" estado
  casos=$((casos + 1))
  mkdir -p "$dir/temp"
  set +e
  (cd "$raiz" && TIPO="$tipo" RUTA="$ruta" RUNNER_TEMP="$dir/temp" GITHUB_ENV="$dir/entorno" bash "$trabajo/empaquetar.sh" > "$dir/log" 2>&1)
  estado=$?
  set -e
  spec_resultante="$(sed -n 's/^SPEC=//p' "$dir/entorno" 2> /dev/null | tail -n 1)"
  if [ "$estado" != "$salida_esperada" ]; then
    echo "FALLA $nombre: salida $estado (se esperaba $salida_esperada)"
    sed 's/^/      /' "$dir/log"
    fallos=$((fallos + 1))
    return 1
  fi
}

# afirmar <nombre> <condición en bash>
afirmar() {
  if eval "$2"; then
    echo "ok   $1"
  else
    echo "FALLA $1"
    fallos=$((fallos + 1))
  fi
}

rel_fixtures="scripts/atlas-publicar/fixtures"
if empaquetar "AsyncAPI con \$ref a otro archivo" asyncapi "$rel_fixtures/asyncapi/con-ref.yaml" 0; then
  # Lo resuelto no trae $ref a otros archivos, trae el contenido del sobre (y su $ref local, #/Fecha, resuelta en él)
  # y conserva las $ref locales del spec.
  externas_que_quedan() { yq '[.. | select(has("$ref")) | .["$ref"]] | map(select(test("^#") | not)) | length' "$spec_resultante"; }
  afirmar "AsyncAPI con \$ref a otro archivo: se publica un solo documento" \
    '[ "$spec_resultante" != "$rel_fixtures/asyncapi/con-ref.yaml" ] && [ -s "$spec_resultante" ] && [ "$(externas_que_quedan)" = 0 ]'
  afirmar "AsyncAPI con \$ref a otro archivo: trae el sobre con su \$ref local resuelta" \
    '[ "$(yq ".components.messages.TramiteCreado.payload.allOf[0].properties.occurredAt.format" "$spec_resultante")" = "date-time" ]'
  afirmar "AsyncAPI con \$ref a otro archivo: conserva las \$ref locales y la versión" \
    '[ "$(yq ".channels[\"demo.tramite.creado\"].subscribe.message[\"\$ref\"]" "$spec_resultante")" = "#/components/messages/TramiteCreado" ] && [ "$(yq ".asyncapi" "$spec_resultante")" = "2.6.0" ]'
fi
if empaquetar "AsyncAPI sin \$ref a otros archivos" asyncapi "$rel_fixtures/asyncapi/sin-ref.yaml" 0; then
  afirmar "AsyncAPI sin \$ref a otros archivos: se publica tal cual" '[ "$spec_resultante" = "$rel_fixtures/asyncapi/sin-ref.yaml" ]'
fi
if empaquetar "OpenAPI" openapi "$rel_fixtures/base.yaml" 0; then
  afirmar "OpenAPI: se publica tal cual" '[ "$spec_resultante" = "$rel_fixtures/base.yaml" ]'
fi
if empaquetar "AsyncAPI con una \$ref rota falla" asyncapi "$rel_fixtures/asyncapi/ref-rota.yaml" 1; then
  echo "ok   AsyncAPI con una \$ref rota falla"
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
