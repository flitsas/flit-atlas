# Propuesta para el repo flit (FLIT 2)

Contrato de `services/core-ict` enriquecido con el Excel de contratos ICT (HU 3.8, ATLAS-ADR-0037). **Generado**:
no se edita a mano; se regenera con el script junto con el resto de `contracts/ict/`. El PR en `flit` va en la ola 3
con la HU 3.10; nada de aquí se aplica solo.

## Qué cambia en flit

1. `contracts/openapi/core-ict.v1.yaml`: se reemplaza por `core-ict.v1.yaml` de esta carpeta (versión 1.0.0 →
   1.1.0). Es el mismo contenido que `contracts/ict/core-ict.enriquecido.v1.yaml` con una cabecera de origen.
2. Opcional: agregar a `.github/workflows/contracts.yml` de `flit` un paso que valide el contrato de core-ict (hoy
   ningún paso lo valida), igual que los demás:
   `npx -y @redocly/cli@latest lint contracts/openapi/core-ict.v1.yaml`.

## Qué trae frente al contrato actual

- 10 operaciones públicas, 2 internas y 1 webhook; 5 salen del código de core-ict porque el contrato
  no las declaraba (rutas de compatibilidad v1: adjuntos, secretarías, pausa y anulación, y el webhook de estado).
- `operationId` en todas las operaciones, descripciones, longitudes, ejemplos y códigos de respuesta del Excel.
- Tablas de referencia como `enum` con `x-enum-descriptions`, la obligatoriedad por tipo de trámite con `oneOf` y
  `x-flit-obligatoriedad`, y el changelog inicial en `info.x-flit-changelog`.
- Una sola URL base por ambiente (`https://{host}` con el host público de core-ict del Gateway) en lugar de
  `api.flit.co` y el Gateway local.
- Las correcciones verificadas en el código (respuesta del login, 403, 400 del registro y cuerpo del registro como
  arreglo) que lista el informe.

## OpenAPI 3.1 y no 3.0.3

El contrato actual es 3.0.3, como los demás de `flit/contracts/openapi/`. La propuesta queda en 3.1 porque:

- `contracts.yml` de `flit` no valida `core-ict.v1.yaml` y ningún otro archivo de `flit` lo lee, así que el CI no
  exige 3.0.3; Redocly, el linter de `flit`, acepta 3.1 y la propuesta pasa `redocly lint` (configuración
  recomendada) sin errores.
- Lo que el Excel aporta usa 3.1: la sección `webhooks` (3.0.3 no la tiene; habría que pasarla a `callbacks` de la
  operación de registro), los tipos con `null` (en 3.0.3 son `nullable: true`), `examples` como lista en los
  esquemas (en 3.0.3 es un solo `example`), `const` en las variantes por tipo de trámite (en 3.0.3 es un `enum` de
  un valor) y `contentMediaType` del archivo adjunto (en 3.0.3 es `format: binary`).

Si `flit` decide quedarse en 3.0.3, esos son los cambios a mano; el resto del contrato es igual.

## Pendientes

- [por verificar] Que `ict.qa.flitsas.com` responda en QA: sale de la ruta `ict-host-route` del Gateway; el
  ingress de QA no está en `flit-gitops/services/flit2-qa`.
- [por verificar] Si core-ict debe enviar un token o una firma en el webhook (hoy no envía `Authorization`).
