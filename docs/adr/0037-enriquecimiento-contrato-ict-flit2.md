---
id: ATLAS-ADR-0037
titulo: Enriquecimiento del contrato de ICT de FLIT 2 con el Excel — core-ict.v1.yaml como base, un solo OpenAPI 3.1 con URL base única por ambiente en contracts/ict/ y reglas de vacuum para lo compartible
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §8.4 y §12, plan 00 §2.2, §8.1, §8.6, §8.8 y §8.10, HU 3.8, decisión del líder del 24/10/2026, ATLAS-ADR-0009, ATLAS-ADR-0010, ATLAS-ADR-0021]
---

# ATLAS-ADR-0037 · Enriquecimiento del contrato de ICT de FLIT 2 con el Excel

## Contexto y problema

Los clientes integran contra la API de Integración con Terceros (ICT). El §8.4 la describe como los
`BackApiExternal*` de FLIT 1, pero esa API ya está en FLIT 2: el servicio `services/core-ict` del repo `flit` los
reemplazó con las mismas rutas de compatibilidad v1 (`/api/v1/auth/login`, `/api/v1/external-transaction/register`,
`/api/v1/status-process/byId/{id}`…) y las rutas v2 (`/api/v1/ict/...`). Lo que cambió para el cliente es que las
varias URL base de FLIT 1 pasaron a una sola por ambiente.

El contrato vive en dos lugares que no coinciden: `contracts/openapi/core-ict.v1.yaml` de `flit` (OpenAPI 3.0.3, sin
`operationId`, con esquemas abiertos y sin las rutas de adjuntos, secretarías, pausa, anulación ni el webhook) y el
Excel `Contratos_Integraciones_31Jul2025.xlsx` (descripciones, longitudes, ejemplos, tablas de referencia y
obligatoriedad por tipo de trámite, con hosts de FLIT 1 y datos sensibles en los ejemplos). El §8.4 pide un OpenAPI
3.1 enriquecido, en git, saneado y publicable por CI; Atlas no edita contratos.

La primera versión de esta decisión convertía el swagger de tsoa de cada `BackApiExternal*` a un spec por repo en
`contracts/legado/`. El 24/10/2026 el líder decidió que la base es core-ict y que no se leen ni se proponen cambios a
los repos de FLIT 1.

Hechos verificados (solo lectura de `flit` y `flit-gitops`):

- `core-ict.v1.yaml` declara los servidores `api.flit.co` y el Gateway local; ninguno es la ruta que el Gateway YARP
  enruta a core-ict. La ruta `ict-host-route` del Gateway (`services/core-api/src/Flit.Gateway/appsettings.json`)
  atiende `ict.qa.flitsas.com`, `ict.dev.flitsas.com` e `ict.flitsas.com`; en PDN el ingress de
  `flit-gitops/services/flit2-pdn` sirve `ict.flitsas.com` (y su alias `ict.flitsas.online`) hacia core-ict.
- El registro lee un arreglo JSON de filas snake_case (`RegisterRowInput`), no el objeto `{ transactions }` que
  declara el contrato, y no lee `company_manager_id` (la compañía sale del token).
- Adjuntos, secretarías, pausa y anulación están en `services/core-ict/src/Flit.Ict.Api/Endpoints/` con sus rutas v1;
  el webhook de estado sale de `WebhookNotificationJob` con campos v2 y v1 y sin cabecera `Authorization`.
- SearchPlate, StatusPlate y WebHookPlate no tienen equivalente en core-ict; RequestSign y StatusSign están en el
  índice del Excel y no tienen hoja.

## Factores de decisión

- Git es la fuente de verdad (§2) y el resultado tiene que ser reproducible sin editar a mano.
- Nada sensible llega a git ni a un cliente: el Excel no se versiona y el spec no trae tokens, correos reales, rutas
  locales, IP privadas, hosts internos ni nombres de personas.
- No adivinar: lo que el contrato, el código de core-ict y el Excel no confirman va al informe, no al spec.
- Lo compartible tiene que servir al sandbox (Microcks usa los ejemplos, plan 00 §8.8) y a la poda (plan 00 §8.6).
- No tocar otros repos sin permiso del líder (§1, regla 4c): el PR a `flit` va en la ola 3 con la HU 3.10.

## Opciones consideradas

### Base del spec

1. `core-ict.v1.yaml` de FLIT 2 como base y el Excel como enriquecimiento; lo que core-ict implementa y su contrato
   no declara se agrega desde el mapeo, verificado en el código, y queda en el informe.
2. Spec escrito desde el Excel, sin el contrato.
3. Seguir con el swagger de tsoa de los `BackApiExternal*`.

### Dónde vive el resultado

1. Un solo OpenAPI 3.1 en `contracts/ict/` de flit-atlas, más la propuesta para `flit` como archivo.
2. Directamente en `flit`.

### Servidores

1. Una URL base por ambiente publicado (`https://{host}` con el host público de core-ict que enruta el Gateway).
2. Una entrada de `servers` por servicio, como en FLIT 1.

### Versión de OpenAPI de la propuesta para `flit`

1. 3.1, igual que el spec de Atlas.
2. 3.0.3, como los demás contratos de `flit`.

## Decisión

- **Base:** opción 1. El script `atlas_docs.ict` (en `services/docs`, Python con openpyxl y PyYAML) recibe la ruta del
  repo `flit` (`--base-flit`) y lee solo `core-ict.v1.yaml` y el `appsettings.json` del Gateway. Lee todas las hojas
  del Excel sin depender de sus nombres y aplica un mapeo explícito hoja → operación de core-ict (`mapeo.py`), con las
  operaciones de compatibilidad v1 como destino. Cada decisión que no sale del contrato ni del Excel (alias, cuerpo
  del registro tomado del Excel, operaciones y webhook agregados desde el código, correcciones verificadas en el
  código, campos que core-ict no lee) tiene su motivo en el mapeo y en el informe. El contrato manda sobre el Excel
  en tipos y obligatoriedad; el Excel no inventa códigos de respuesta que core-ict no declara.
- **Qué aporta el Excel:** descripciones; longitudes y rangos (`String(12)` → `maxLength`, `number(2)` → entero de 0
  a 99, `number(17,2)` → máximo y `x-flit-decimales`); ejemplos saneados y ajustados al esquema (claves con la
  capitalización del contrato); códigos de respuesta; tablas de referencia como componentes `enum` con
  `x-enum-descriptions` y `x-flit-tabla`; el webhook en la sección `webhooks`; `info.x-flit-changelog` con la versión,
  la fecha y la hoja de cada hoja más la versión enriquecida (sube la menor del contrato: 1.0.0 → 1.1.0).
- **Obligatoriedad:** `oneOf` de variantes (`allOf` del esquema base con `const` en `transaction_type`) y la matriz
  completa en `x-flit-obligatoriedad`. El `discriminator` no es viable: `transaction_type` es numérico y la tabla de
  tipos de trámite tiene más valores que las cuatro columnas de obligatoriedad. Las reglas condicionales quedan como
  texto en la extensión.
- **Visibilidad:** público (`x-public`) lo que documenta el Excel y lo que el contrato protege con el token del
  cliente ICT (`ictClientToken`); `x-internal` lo que usa el JWT de plataforma (logs y alertas), que la poda quita.
- **Servidores:** opción 1. Una entrada por ambiente publicado (QA y PDN) con `https://{host}`, tomada del Gateway de
  FLIT 2; dev es de uso interno y no se publica. Los hosts mezclados y las URL vacías del Excel dejan de ser un
  problema del contrato: el informe los lista como resueltos.
- **Saneamiento:** por patrón (JWT, valor después de `Bearer`, correos que no son `@ejemplo.com`, rutas locales, IP
  privadas, hosts internos) y por nombre de campo en los ejemplos. Una última pasada recorre todo el árbol y la
  escritura falla si algo sobrevive. El informe cuenta lo sensible del Excel sin copiarlo; el changelog dice «Equipo
  de integraciones de FLIT» en lugar del nombre de la persona (Ley 1581, minimización del §12).
- **Lenguaje funcional y procedencia** (revisión de seguridad de la F4, F4-SEC-09): el spec se comparte podado y la
  poda no reescribe textos (ATLAS-ADR-0038), así que `description`, `summary`, `title` y `x-enum-descriptions` salen
  del script en lenguaje funcional, sin rutas de repo, archivos, clases, proyectos, números de feature o ticket,
  comandos, nombres de servicios internos, hosts ni IP internos, ADR ni notas del equipo («[por verificar]», el Excel
  y sus hojas). La procedencia que sirve al equipo **no va en las descripciones**: va en extensiones `x-flit-*` que
  la poda quita (`info.x-flit-fuente` con la descripción original del contrato y el generador, `x-flit-origen`,
  `x-flit-hoja-excel`, `x-flit-descripcion-contrato` junto a cada texto del contrato reescrito) o en el informe y la
  trazabilidad. Los textos del contrato de `flit` que nombran piezas internas se reescriben desde
  `mapeo.TEXTOS_FUNCIONALES` y cada cambio queda en el informe; la conversión falla si un texto de cara al cliente
  conserva una pieza interna (`lenguaje.py`), igual que con los datos sensibles.
- **Dónde vive:** opción 1. `contracts/ict/core-ict.enriquecido.v1.yaml`, `informe.md`, `trazabilidad.json` y
  `propuesta-flit/` (el contrato listo para reemplazar a `core-ict.v1.yaml` y su README). Se retira
  `contracts/legado/` con sus specs por repo, su informe, su trazabilidad y los PR propuestos a los `BackApiExternal*`.
- **Versión de la propuesta:** opción 1. El CI de `flit` (`contracts.yml`) no valida `core-ict.v1.yaml` y ningún
  archivo de `flit` lo lee, así que no exige 3.0.3; Redocly acepta 3.1. El README de la propuesta lista lo que habría
  que cambiar a mano para 3.0.3 (`webhooks` → `callbacks`, tipos con `null`, `examples`, `const` y
  `contentMediaType`).
- **Lo compartible en vacuum:** `contracts/vacuum/openapi-compartible.yaml` extiende las reglas de los contratos
  propios y agrega ejemplos obligatorios, versión semántica, título propio, visibilidad declarada, servidores sin
  hosts internos y seguridad en toda operación pública salvo la que entrega el token. El job `contratos` lo corre con
  vacuum 0.32.0 sobre el spec y la propuesta; el job `python` corre las pruebas del script con fixtures ficticias (un
  core-ict mínimo y un xlsx generado) y la prueba que recorre `contracts/ict/` buscando datos sensibles, piezas
  internas en los textos de cara al cliente, una sola URL base por ambiente y la trazabilidad. En la API,
  `PodaIctDescripcionesTests` poda el ICT real hacia pdn y revisa los mismos patrones en el resultado.

## Consecuencias

### Positivas

- El contrato que usan los clientes queda en git, con una sola URL base por ambiente, validado y listo para publicar.
- Cada campo del Excel tiene destino comprobable (`trazabilidad.json`): un puntero en el spec o un motivo en el
  informe.
- `flit` recibe un contrato más completo que el suyo (operaciones v1 que faltaban, `operationId`, webhook, ejemplos)
  sin que Atlas lo edite.

### Negativas

- El spec se regenera desde una copia local del Excel y del repo `flit`: el CI no puede comprobar que esté al día,
  solo que lo versionado es válido y está saneado.
- Las operaciones y correcciones tomadas del código de core-ict están escritas en el mapeo: si cambia el código,
  hay que actualizar el mapeo hasta que el contrato de `flit` las declare.
- Hasta que `flit` adopte la propuesta, su contrato y el de Atlas difieren.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Spec escrito desde el Excel | Pierde lo que el contrato sí declara (rutas v2, seguridad, esquemas) y no se puede regenerar desde `flit` |
| Swagger de tsoa de los `BackApiExternal*` | FLIT 1 ya no atiende ICT; el líder decidió no leer ni proponer cambios a esos repos |
| Directo en `flit` | Cambia otro repo; el PR va en la ola 3 con la HU 3.10 |
| Un servidor por servicio | Es la forma de FLIT 1; core-ict recibe todo por una sola URL base |
| Propuesta en 3.0.3 | El CI de `flit` no lo exige y perdería `webhooks`, `const` y los tipos con `null` |
| `discriminator` sobre `transaction_type` | Propiedad numérica y tabla compartida con otros trámites; los generadores lo manejan mal |

## Más información

- Informe: [`contracts/ict/informe.md`](../../contracts/ict/informe.md).
- Propuesta para `flit`: [`contracts/ict/propuesta-flit/README.md`](../../contracts/ict/propuesta-flit/README.md).
- [por verificar] Que `ict.qa.flitsas.com` responda en QA (sale del Gateway; el ingress de QA no está en
  `flit-gitops`); si SearchPlate, StatusPlate, WebHookPlate, RequestSign y StatusSign se implementan en core-ict; qué
  autenticación debe enviar core-ict en el webhook.
- Relacionados: ATLAS-ADR-0009 (publicación con OIDC de GitHub), ATLAS-ADR-0010 (oasdiff y vacuum), ATLAS-ADR-0021
  (contratos propios).
