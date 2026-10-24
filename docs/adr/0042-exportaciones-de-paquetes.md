---
id: ATLAS-ADR-0042
titulo: Exportaciones de paquetes — generador puro en atlas-docs con Jinja2, WeasyPrint, python-docx y openpyxl, Postman propio, salida determinista con hash del contenido y endpoint interno previo a la cola
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-02, §5.1, §5.2, §8.4, §8.5 (exportación), §12 y §15, plan 00 §8.7 y §12, HU 4.5, ATLAS-ADR-0003, ATLAS-ADR-0014, ATLAS-ADR-0018, ATLAS-ADR-0037, ATLAS-ADR-0039, ATLAS-ADR-0040]
---

# ATLAS-ADR-0042 · Exportaciones de paquetes

## Contexto y problema

La HU 4.5 pide exportar un paquete de contratos (specs ya podados por la HU 4.2, guías y changelog) en siete
formatos: OpenAPI YAML y JSON, colección Postman, HTML autocontenido, PDF, Word, Excel con el formato que los
clientes ya conocen y un ZIP con todo. Cada uno lleva portada, índice, changelog, marca de agua (organización +
fecha) y hash del contenido; la descarga es por URL firmada de 15 minutos y el usuario recibe un aviso push.

El plan 00 §8.7 lo resolvía con un worker Node (`openapi-to-postmanv2`, `redocly build-docs` y Chromium con
Playwright). A-02 lo reemplaza: los exportes viven en el servicio Python `atlas-docs` (WeasyPrint, python-docx,
openpyxl, Jinja2) y Node no corre en producción.

Esta decisión cubre la parte 1 de la HU (el generador). La cola, el S3 con URL firmada, la auditoría y el aviso
push se integran en la ola 2 desde atlas-api.

Hechos verificados:

- `services/docs` ya tenía FastAPI, openpyxl, PyYAML, boto3 y el script del contrato ICT (ATLAS-ADR-0037); no tenía
  Dockerfile ni la publica `cd.yml`.
- El contrato ICT enriquecido trae `x-flit-obligatoriedad` en dos formas (`campos` en `SolicitudRegistroTramite` y
  `documentos` en `DocumentoAdjunto`) y las tablas de referencia como esquemas con `enum`, `x-enum-descriptions` y
  `x-flit-tabla`. La PWA ya las interpreta en `apps/web/src/modulos/contratos/spec/referencia.ts` (HU 3.9).
- El Excel de contratos (`docs/atlas/insumos/`, fuera de git) tiene una hoja índice («Version», «Fecha
  actualizacion», «Listado de servicios integrados») y una hoja por servicio con «Ambiente URL» (DEV, QA, PDN),
  «Version», «Fecha» y «Colaborador» a la derecha, «Header», «Metodo», «Parametros» (Campo, Descripción, Tipo de
  Dato, Obligatoriedad, o una columna «OBLIGATORIO <tipo de trámite>» por tipo), «Ejemplo», «Salida», códigos de
  respuesta y las tablas de referencia a la derecha. Encabezados en Arial 11 negrita blanca sobre `#4E00EB`, bordes
  finos y texto ajustado. Se leyó solo la estructura; ningún dato del Excel pasó al código ni a las pruebas.
- En Windows sin GTK, WeasyPrint no carga (`OSError` al abrir `libgobject`). En Debian y Ubuntu basta
  `libpango-1.0-0`, `libpangoft2-1.0-0`, `libharfbuzz-subset0` y una fuente.
- `contracts/asyncapi/atlas-events.v1.yaml` no declara eventos de exportación, y `ContratoAsyncApiTests` falla si
  el AsyncAPI declara un evento que el código .NET no tiene. Agregarlo hoy rompería el CI.

## Factores de decisión

- A-02 y §6 de las reglas: librerías maduras de Python, nada de Node ni Chromium en producción.
- Seguridad (§12): el HTML no puede traer scripts ni cargar recursos remotos; todo texto del spec, de las guías o de
  los metadatos se escapa. El generador no usa red.
- La misma entrada tiene que dar el mismo hash (y, mejor, los mismos bytes): sirve para auditar lo que se entregó
  a cada organización y para cachear.
- Que el cliente que hoy usa el Excel no sienta el cambio.
- Capacidad de las VPS (§15): exportar es de CPU y memoria y va fuera de la petición HTTP.
- Licencias compatibles y versiones estables con al menos dos semanas de publicadas.

## Opciones consideradas

### Colección Postman

1. Una librería Python que convierta OpenAPI a Postman.
2. `openapi-to-postmanv2` (Node) en un contenedor aparte.
3. Un conversor mínimo propio sobre el modelo neutro del documento.

### PDF

1. WeasyPrint desde el mismo HTML de Jinja2 con una hoja de impresión.
2. Chromium headless (Playwright).
3. ReportLab con un diseño propio.

### Hash

1. SHA-256 del contenido exportado en JSON canónico (metadatos, specs normalizados y guías), calculado antes de
   generar los formatos.
2. SHA-256 de cada archivo generado.

### Cómo lo invoca atlas-api en esta ola

1. Endpoint interno `POST /exportes` que devuelve el ZIP o deja los archivos en el S3.
2. Solo el consumidor de la cola.

## Decisión

- **Paquete `atlas_docs.exportes`** con una función pura `exportar(paquete, formatos)`: recibe el «paquete a
  exportar» (modelo Pydantic: metadatos con nombre, revisión, organización, fecha de Bogotá y changelog; specs
  OpenAPI o AsyncAPI como texto con su contrato y versión; guías en Markdown ya resueltas) y devuelve los archivos
  en memoria. No lee disco, no usa red ni reloj. La organización y la fecha llegan resueltas desde atlas-api.
- **Modelo neutro** (`contenido.py`): contratos, ambientes (servidores con sus variables por defecto), operaciones
  (parámetros, cuerpo aplanado hasta cuatro niveles con nombres `padre/hijo`, ejemplos, respuestas), la matriz de
  obligatoriedad y las tablas de referencia con las mismas reglas que la PWA. HTML, PDF, Word y Excel salen de ese
  modelo; AsyncAPI 2 y 3 entra como operaciones de mensaje.
- **Formatos y librerías:**

  | Formato | Cómo |
  |---|---|
  | OpenAPI YAML y JSON | El spec podado tal cual, leído con un `SafeLoader` sin timestamps (las fechas siguen siendo texto) y escrito en YAML legible y JSON con sangría. No se le agrega nada: el hash va en el manifiesto. |
  | Postman v2.1 | Opción 3: una colección por spec OpenAPI, carpeta por etiqueta, variables `{{baseUrl}}` (QA si se reconoce) y `{{token}}` (vacía), Bearer `{{token}}` en la colección y `noauth` en `security: []`, variables de ruta `:id`, cuerpo de ejemplo, sin scripts. Ids `uuid5` deterministas. Los webhooks no van (los envía FLIT). |
  | HTML | Jinja2 con autoescape y `StrictUndefined`, CSS embebido con la marca FLIT (azul `#557eff`, tinta `#476bd9`, azul noche `#162744`), sin JS y con `Content-Security-Policy: default-src 'none'` en un `<meta>`. Markdown con markdown-it-py en modo CommonMark con el HTML crudo apagado; un enlace externo pierde el `href` y su dirección queda como texto; una imagen queda como su texto alternativo. |
  | PDF | Opción 1: WeasyPrint sobre el mismo HTML con `@page` A4, índice con número de página (`target-counter`), marca de agua diagonal fija en cada página y en el encabezado, hash en el pie. `url_fetcher` que rechaza todo recurso. |
  | Word | python-docx: portada, índice (lista de secciones; python-docx no pagina), cambios, guías (Markdown a párrafos, listas, código y tablas), contratos y tablas; marca de agua en el encabezado y hash en el pie de cada página. |
  | Excel | openpyxl con la forma del Excel de contratos (estructura y estilos, no datos): «Portada», «Índice», «Cambios», una hoja por servicio con el orden del original y las tablas de referencia a la derecha, «Tablas de referencia» y «Guías». Con matriz, una columna «OBLIGATORIO <tipo>» por tipo de trámite con S, N o N/A y la condición en «Observaciones». Marca de agua en el encabezado de impresión. |
  | ZIP | Todo lo anterior bajo una carpeta, más `LEEME.md` y `manifiesto.json` (hash del contenido y SHA-256, tamaño y formato de cada archivo). |

- **Hash:** opción 1, y además el SHA-256 de cada archivo en el manifiesto. El del contenido va en la portada de
  cada formato (y en la descripción de la colección Postman); no cambia si el spec llega en YAML o en JSON.
- **Salida determinista:** los ZIP (también los `.docx` y `.xlsx`, que son ZIP) se reescriben con la fecha del
  paquete a las 00:00 y permisos fijos; las propiedades de Word y Excel llevan esa fecha y «Flit Atlas» como autor
  (el Excel se guarda con `ExcelWriter` porque `save_workbook` sella la hora actual); el PDF lleva un
  identificador derivado del hash y sin fecha de creación. Probado: dos exportaciones iguales dan los mismos bytes
  en los siete formatos.
- **Endpoint interno:** opción 1 como paso previo a la cola. `POST /exportes` recibe el paquete; con
  `?formatos=` elige formatos; sin `exportacionId` responde el ZIP con `X-Atlas-Hash-Contenido`; con
  `exportacionId` (uuid) sube cada archivo a `exportaciones/<exportacionId>/<ruta>` con `almacenamiento.subir` (el
  SHA-256 va en los metadatos del objeto) y responde las claves. Una exportación a la vez por pod
  (`CapacityLimiter(1)`), en un hilo. 422 si un spec no es OpenAPI ni AsyncAPI; 503 si falta Pango.
- **Mensaje de cola propuesto para la ola 2** (no se agrega todavía al AsyncAPI para no romper
  `ContratoAsyncApiTests`; entra junto con el `TipoEvento` en .NET). Ambos van en el `SobreEvento` común
  (ATLAS-ADR-0018), con la organización destino en `tenantId`:

  ```yaml
  atlas.exportacion.solicitada:   # productor atlas-api, consumidor atlas-docs (cola atlas.docs.exportaciones)
    data:
      exportacionId: uuid v7      # la crea atlas-api; idempotencia del consumidor por exportacionId
      formatos: [openapi, postman, html, pdf, word, excel, zip]
      paquete: { id: uuid, revision: int }
      paqueteClave: exportaciones/<exportacionId>/entrada.json   # el PaqueteExportar en el S3 (los specs pesan)
  atlas.exportacion.lista:        # productor atlas-docs, consumidor atlas-api
    data:
      exportacionId: uuid
      hashContenido: sha256 hex
      archivos: [{ clave, formato, bytes, sha256, tipo }]
      error: null | { codigo: spec_invalido | pdf_no_disponible | interno, mensaje }
  ```

  atlas-api guarda la exportación (`doc_exportacion` o la tabla del módulo de contratos), firma la URL de 15 minutos
  solo al descargar, audita la solicitud y la descarga, y envía el push. El paquete viaja por el S3 y no dentro del
  mensaje porque el contrato de Atlas pesa ~0,5 MB en YAML.
- **Dependencias** (fijadas en `pyproject.toml` y `uv.lock`; todas con más de dos semanas de publicadas al
  24/10/2026): WeasyPrint 70.0 (08/09/2026, BSD-3-Clause), python-docx 1.2.0 (16/06/2025, MIT), Jinja2 3.1.6
  (05/03/2025, BSD-3-Clause), markdown-it-py 4.2.0 (07/05/2026, MIT); en pruebas, pypdf 6.19.0 (16/09/2026,
  BSD-3-Clause) y jsonschema 4.26.0 (07/01/2026, MIT). Seis transitivas de WeasyPrint tenían una versión de menos de
  dos semanas y se topan en `[tool.uv] constraint-dependencies` a la estable anterior: Pillow 12.3.0, fontTools
  4.66.1, pydyf 0.12.1, Brotli 1.2.0, brotlicffi 1.2.0.2 y pycparser 3.11. Pyphen (MPL 1.1 / LGPL / GPL, a
  elección) se usa sin modificar, como librería dinámica de Python.
- **Esquema de Postman en las pruebas:** el oficial
  (`https://schema.getpostman.com/json/collection/v2.1.0/collection.json`, JSON Schema draft-04) en
  `services/docs/tests/exportes/esquemas/` con la licencia Apache-2.0 del repo `postmanlabs/schemas`.
- **Imagen y CI:** `services/docs/Dockerfile` nuevo (`python:3.13.15-slim-trixie`, publicada el 25/09/2026, y uv
  0.11.16, publicada el 21/05/2026, ambas fijadas por digest y con más de dos semanas de publicadas, Pango, HarfBuzz, DejaVu y tzdata, usuario sin privilegios, puerto `ATLAS_DOCS_PORT` o 4035) con un
  objetivo `pruebas` que corre pytest con Pango. El job `python` instala esas librerías para `docs` y exporta
  `ATLAS_DOCS_EXIGIR_PDF=1`: sin Pango, las pruebas del PDF fallan en el CI y en Windows se saltan. El job
  `imagenes` construye `atlas-docs`; `cd.yml` la publicará en la ola 2.

## Consecuencias

### Positivas

- Sin Node ni Chromium: la imagen pesa y consume mucho menos que la del plan 00 (WeasyPrint no lanza un navegador).
- Un solo modelo para cuatro formatos: el Excel, el Word, el HTML y el PDF dicen lo mismo, y la matriz y las tablas
  se interpretan igual que en la PWA.
- Lo entregado a una organización es verificable: el hash del contenido identifica qué se exportó y los bytes se
  pueden regenerar.
- El HTML se puede abrir sin conexión y no ejecuta nada.

### Negativas

- El conversor de Postman es propio: no cubre lo que `openapi-to-postmanv2` sí (ejemplos generados desde el
  esquema, `callbacks`, autenticaciones OAuth con flujo). Si un cliente lo necesita, se amplía o se evalúa de nuevo.
- La marca de agua de Word es texto en el encabezado y el pie, no un sello diagonal (python-docx no lo expone sin
  VML a mano); la del Excel está en el encabezado de impresión y en la portada.
- El índice de Word no tiene números de página.
- Exportar el contrato de Atlas (171 operaciones) tarda entre uno y dos minutos con el PDF en un contenedor de
  desarrollo (el PDF es casi todo):
  tiene que ir por la cola y con concurrencia 1.
- En Windows sin GTK el PDF no se genera en local (503); el resto sí.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Librería Python para Postman | En PyPI no hay un conversor OpenAPI → Postman mantenido (el 24/10/2026 se buscaron `openapi2postman`, `openapi-to-postman`, `swagger2postman` y similares: no existen; `postmanparser` solo lee colecciones). |
| `openapi-to-postmanv2` en Node | A-02: Node no corre en producción; otro contenedor solo para esto. |
| Chromium headless | A-02 y la capacidad de las VPS (512 MiB a 1 GiB por pod en el plan 00). |
| ReportLab | Habría que diseñar el PDF dos veces (HTML y PDF); WeasyPrint reutiliza la plantilla. |
| Hash solo por archivo | El PDF y el Word cambian si cambia la librería; el hash del contenido identifica lo exportado sin depender del formato. |
| Solo la cola | Sin un endpoint no se puede probar ni usar el generador hasta la ola 2, y la API necesita una vía síncrona para paquetes pequeños si el líder la habilita. |

## Más información

- Código: `services/docs/src/atlas_docs/exportes/`; pruebas: `services/docs/tests/exportes/` (exportan el contrato
  ICT enriquecido y `contracts/openapi/atlas.v1.yaml`).
- [por verificar] Autenticación del endpoint interno: hoy solo lo protege la `NetworkPolicy` (solo atlas-api entra
  a atlas-docs); en la ola 2 se agrega el token de servicio de OpenIddict o mTLS (ATLAS-ADR-0008).
- [por verificar] Tope de memoria del pod de atlas-docs en la 169 con WeasyPrint y el contrato más grande.
- [por verificar] Con el líder: si el Excel debe conservar el morado `#4E00EB` del Excel actual (se conservó para
  que el cliente lo reconozca) o pasar al azul de la marca.
- [por verificar] Nombre y forma de la tabla de exportaciones en atlas-api (`doc_exportacion` del §9 o una tabla del
  módulo de contratos) y la retención de los archivos en el S3.
