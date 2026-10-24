---
id: ATLAS-ADR-0042
titulo: Exportaciones de paquetes — generador puro en atlas-docs (Jinja2, WeasyPrint, python-docx, openpyxl, Postman propio) con salida determinista y hash del contenido; de punta a punta por la cola de RabbitMQ con la entrada en el S3, URL firmada de 15 minutos, retención de 7 días, auditoría y aviso; compartir desde el móvil con la hoja nativa y solo enlaces internos
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-02, A-06, §5.1, §5.2, §6.3, §7.4, §8.4, §8.5 (exportación), §8.9, §11, §12 y §15, plan 00 §6.2, §6.3, §8.4, §8.7 y §12, HU 4.5, HU 4.6, ATLAS-ADR-0003, ATLAS-ADR-0014, ATLAS-ADR-0018, ATLAS-ADR-0020, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0024, ATLAS-ADR-0026, ATLAS-ADR-0037, ATLAS-ADR-0038, ATLAS-ADR-0039, ATLAS-ADR-0040, ATLAS-ADR-0041, ATLAS-ADR-0043, ATLAS-ADR-0044]
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

La parte 1 de la HU (ola 1) es el generador. La parte 2 (ola 2, sección «Decisión — parte 2») lo conecta de punta a
punta: la tabla de exportaciones en atlas-api, la cola, el S3 con URL firmada, la retención, la auditoría, el aviso, la
PWA y el CD.

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

## Decisión — parte 2: de punta a punta (ola 2)

Hechos verificados al empezar la ola 2:

- atlas-api publicaba eventos con su outbox, pero **no consumía ninguna cola**; atlas-docs no tenía cliente de AMQP; ni
  atlas-api ni atlas-render usaban token de servicio (solo la `NetworkPolicy`).
- atlas-api no usaba S3 (solo atlas-docs, con boto3).
- `ExtensionesModulos.Validar` exige que los eventos que publica un módulo empiecen por `atlas.<id>.`: los nombres
  propuestos en la parte 1 (`atlas.exportacion.*`) no pasan. Se renombran como `atlas.contratos.version_publicada`.
- Las HU 4.3 y 4.4 (ATLAS-ADR-0043) ya dejan en Plataforma la reautenticación para los módulos
  (`RequiereReautenticacion`), el grupo `/api/v1/portal` por módulo (`IModuloAtlas.MapearEndpointsPortal`), el acceso del
  portal (`AccesoPortal.PuedeExportarAsync`: compartición vigente con `puede_exportar`, nivel técnico y el permiso o el
  extra `exportar` de la invitación) y el spec podado del portal con caché (`SpecsDelPortal`).
- En la matriz, `atlas.exportaciones.solicitar` lo tienen el líder técnico, desarrollador, negocio y asesor comercial;
  `atlas.contratos.ver_internos`, el líder técnico, desarrollador y soporte; `ext.integrador` tiene `atlas.portal.ver`
  pero no `atlas.exportaciones.solicitar`.

### Cola de RabbitMQ y no HTTP desde un trabajo de atlas-api

Se consideró que atlas-api llamara a `POST /exportes` desde un trabajo en segundo plano. No es mucho más simple: exige
que atlas-api se pida un token a sí mismo, una llamada HTTP de minutos que se corta si el pod se reinicia, un
arrendamiento para retomarla y una prueba de punta a punta con el contenedor alcanzando el JWKS del host. La cola es lo
que piden §5.1, plan 00 §8.7 y §12 y ATLAS-ADR-0018 (el ejemplo de cola es justamente `atlas.docs.exportar`): durable,
sin llamadas largas, con reintentos del broker y autenticada por usuario de servicio (ATLAS-ADR-0044). La petición del
usuario responde 202 sin esperar la generación.

### Flujo

1. **Solicitar** (atlas-api). Valida, revisa permisos, arma el «paquete a exportar» y lo sube a
   `exportaciones/<id>/entrada.json`; después guarda en **una sola transacción** la fila `pendiente`, el evento
   `atlas.contratos.exportacion_solicitada` en la outbox y la auditoría. Responde **202** con la exportación y
   `Location`. Si la base falla después de subir, la entrada queda huérfana en el S3 (sin nadie que la lea).
   - **Paquete:** la revisión pedida o, sin ella, la vigente (o la última si nunca se publicó). Por cada contrato con
     operaciones, el spec de la versión fijada **podado** a su selección con `Podadores` (ATLAS-ADR-0038) al ambiente
     `Contratos:Exportaciones:Ambiente` (`qa` por defecto) con las URL públicas configuradas
     (sin URL, sin `servers`). Si la poda falla, nada se exporta: **409 `no_exportable`**
     con el detalle (en el portal, sin detalle). Las guías son las revisiones fijadas en el paquete. El changelog se
     **genera** de las definiciones de las revisiones hasta la exportada (contratos que entran o salen, versión que
     cambia, operaciones y guías agregadas o quitadas); la nota de cada revisión no sale (texto libre interno que puede
     traer datos personales). Topes de atlas-docs: 30 specs, 50 guías y 500 entradas de changelog. Las URL públicas son
     las del portal (`Contratos:Portal:Servidores`).
   - **Paquete desde el portal:** exactamente lo que la organización ve hoy (`AccesoPortal`): la revisión de su
     compartición, los contratos técnicos y disponibles con el spec de `SpecsDelPortal` (el mismo que muestra el portal,
     al ambiente `Contratos:Portal:Ambiente`) y las guías visibles.
   - **Versión** (solo internos): el spec completo tal como se publicó, las guías publicadas que le aplican y el
     changelog de oasdiff de la CI. proto no se exporta (409).
   - **Organización y fecha:** la del externo en el portal (va en la marca de agua); «FLIT S.A.S. · uso interno» en las
     internas. Fecha de Bogotá.
2. **Generar** (atlas-docs). Cola `atlas.docs.exportaciones` en `flit.atlas` (llave
   `atlas.contratos.exportacion_solicitada`), prefetch 1 (concurrencia 1 por pod, el mismo `CapacityLimiter` del
   endpoint HTTP). Solo lee la clave `exportaciones/<exportacionId>/entrada.json` del mensaje; publica
   `atlas.contratos.exportacion_iniciada`, genera con `exportar(...)`, sube cada archivo bajo `exportaciones/<id>/` y
   publica `atlas.contratos.exportacion_lista` (hash del contenido y `[{clave, ruta, formato, bytes, sha256, tipo}]`) o
   `atlas.contratos.exportacion_fallida` (`entrada_invalida`, `spec_invalido`, `pdf_no_disponible`, `interno`, con una
   frase sin contenido del spec) en **su** exchange `flit.atlas.docs`, productor `atlas-docs`. Un fallo de
   infraestructura se reintenta por `atlas.docs.exportaciones.reintentos` (TTL 30 s) y a la quinta va a
   `atlas.docs.exportaciones.errores`. Idempotente: la salida es determinista, así que una reentrega regenera los
   mismos bytes en las mismas claves.
3. **Resultado** (atlas-api). Cola `atlas.api.exportaciones` en `flit.atlas.docs`, con la misma topología de reintentos y
   errores, en un consumidor genérico nuevo de Plataforma (`IManejadorEventos` y `ConsumidorEventos`, apagable con
   `Plataforma:Mensajeria:Consumo:Habilitado`). La inbox va en la misma transacción que el cambio; además un resultado
   para una exportación terminada se ignora. `iniciada` → `procesando`; `lista` → guarda las claves (solo las del prefijo
   de la exportación; sin ZIP, con claves ajenas o sin hash, queda `fallida` `interno`), fija la retención y **avisa**
   («Tu exportación está lista», bandeja y Web Push con el tipo `contratos.exportacion`, ATLAS-ADR-0020); `fallida` →
   guarda el error, lo **audita** y avisa. El aviso es de mejor esfuerzo: si falla, el resultado queda igual.
4. **Descargar.** `GET .../{id}/descarga[?archivo=<ruta>]` (el ZIP por defecto) revisa que sea de la persona y esté
   `lista` (409 `no_lista`; 410 `vencida`), **audita** y responde `{url, nombre, bytes, venceEn}` con una **URL
   firmada (SigV4) de 15 minutos** (`Contratos:Exportaciones:DuracionUrl`, tope 15 min) que pide al S3 la respuesta como
   adjunto. JSON y no 302: la PWA abre la URL en la misma pestaña (iOS descarga bien así y no hay `blob:`). La firma usa
   `Plataforma:Almacenamiento:EndpointPublico` (el host que alcanza el navegador; la firma incluye el host).
5. **Retención.** `VencedorExportaciones` corre cada hora: borra todo bajo `exportaciones/<id>/` de las listas o fallidas
   cuya retención pasó (**7 días** por defecto, `Contratos:Exportaciones:Retencion`; 1 día para las fallidas, que solo
   tienen la entrada) y las deja `vencida`; las `pendiente` o `procesando` de más de 2 horas
   (`Contratos:Exportaciones:TiempoMaximo`) quedan `fallida` `tiempo_agotado`, auditadas y avisadas. Cada cambio es
   condicional (seguro con varias réplicas).

### Modelo, endpoints y permisos

- **`contratos_exportacion`** (módulo de contratos, ATLAS-ADR-0026; ya no `doc_exportacion`): quién (`solicitada_por`),
  `organizacion_id` (solo si es externa, con FK al núcleo, **predicado obligatorio y RLS forzada** con
  `plataforma_organizacion_visible`), `desde_portal`, origen (`paquete` con `paquete_id` y `revision`, o `version` con
  `version_id`), nombre, formatos (`text[]`, siempre con `zip`), estado (`pendiente | procesando | lista | fallida |
  vencida`), clave y SHA-256 de la entrada, objeto (el ZIP), hash del contenido, archivos (`jsonb`), error y fechas
  (solicitada, iniciada, terminada, vence, vencida). Restricciones: origen coherente, una externa solo de un paquete y
  desde el portal, una lista con objeto, hash y vencimiento. Nunca guarda el spec.
- **Internos** en `/api/v1/contratos/exportaciones`: `POST` (permiso `atlas.exportaciones.solicitar` más
  `atlas.contratos.ver_internos` dentro, y **reautenticación**, prompt maestro §6.3), `GET` (las últimas 20 de la
  persona, filtrables por paquete o versión), `GET /{id}` y `GET /{id}/descarga` (`atlas.exportaciones.solicitar`).
- **Portal** en `/api/v1/portal/exportaciones` (montado con `MapearEndpointsPortal`): `POST` (paquete y formatos;
  `atlas.portal.ver`, reautenticación y `AccesoPortal.PuedeExportarAsync`: `no_encontrado` → 404 sin revelar el
  paquete; `solo_resumen`, `no_permitido_por_comparticion` o `sin_permiso` → 403), `GET /{id}` y `GET /{id}/descarga`
  (que vuelve a consultar el acceso: una compartición revocada corta también la descarga). Cada persona ve solo lo suyo:
  interno con interno y portal con portal, y una persona de otra organización recibe 404 (sus casos están en
  `AislamientoPortalTests` y en `ExportacionesTests`).
- **Auditoría** (`contratos.exportacion_solicitada`, `_descargada`, `_fallida`): ids, origen, formatos, SHA-256 de la
  entrada, contratos y versiones, archivo, bytes y hash del contenido; nunca el contenido del spec, la guía ni la URL.

### Almacenamiento en atlas-api

`IAlmacenObjetos` en Plataforma con el SDK de AWS para S3 (`AWSSDK.S3` 4.0.104.2, del 08/10/2026, Apache-2.0; las
4.0.104.3 a .6 tienen menos de dos semanas), rutas por bucket, sumas de verificación solo cuando se requieren
(compatibles con RustFS y MinIO) y dos clientes: el interno sube, lista y borra; el público solo firma. Sección
`Plataforma:Almacenamiento` (`Endpoint`, `EndpointPublico`, `Usuario`, `Clave`, `Bucket`, `Region`, `CrearBucket` solo
en local). Sin configuración, atlas-api arranca igual y exportar responde **503** `almacenamiento_no_disponible` (un
ambiente sin S3 no tumba el despliegue).

### Contratos, Aspire, CD y PWA

- **AsyncAPI** (`contracts/asyncapi/atlas-events.v1.yaml`): `atlas.contratos.exportacion_solicitada` en `flit.atlas`
  (publicar) y el canal nuevo `flit.atlas.docs` con `_iniciada`, `_lista` y `_fallida` (operación de recibir);
  `producer` admite `atlas` o `atlas-docs` y cada mensaje fija el suyo. Los cuatro son `TipoEvento` del módulo
  (`ContratoAsyncApiTests`).
- **Aspire:** atlas-docs recibe `ATLAS_DOCS_RABBITMQ_URL` y espera a RabbitMQ; atlas-api recibe el S3 local (el mismo
  `localhost` sirve para firmar).
- **CD:** `cd.yml` publica `atlas-docs` (objetivo `docs` del Dockerfile) y fija su digest en flit-gitops como las demás.
- **PWA:** botón «Exportar» en el detalle del paquete (con `atlas.exportaciones.solicitar`): hoja inferior con los
  formatos, estado en vivo por sondeo (cada 2 s y, pasados 30 s, cada 5 s), «Tu exportación está lista» con el ZIP y
  los archivos sueltos, y descarga en la misma pestaña. El componente `ExportarPaquete` recibe la base
  (`/api/v1/contratos` o `/api/v1/portal`); en el portal, `ExportacionDelPortal` lo conecta con
  `conectarAccionPortal('exportar', …)` y abre la hoja desde el botón del paquete. Los avisos enlazan a
  `/contratos/paquetes?paquete=<id>&exportacion=<id>` y `/portal/paquetes/<id>?exportacion=<id>`.

### Pruebas (criterio de aceptación)

- **De punta a punta, sin simular el generador:** `ExportacionesPuntaAPuntaTests` levanta RustFS y RabbitMQ en una red
  de Docker y atlas-docs **construido desde su Dockerfile** (con Pango, así corre igual en Windows y en el CI); atlas-api
  con su outbox y su cola exporta el paquete ICT en los siete formatos, descarga el ZIP y el Excel (que abre con el SDK
  de Open XML), comprueba que la URL deja de servir (con `DuracionUrl` de 3 segundos el S3 responde 403) y que la
  solicitud y las descargas quedan auditadas y el aviso enviado.
- Que la URL por defecto es de 15 minutos (`X-Amz-Expires=900`), permisos, reautenticación, validación, poda en la
  entrada, portal (sin `puede_exportar`, 403; un paquete de otra organización, 404; con la compartición, la marca de su
  organización y nadie de otra la ve ni la descarga), resultados repetidos o
  ajenos, retención y corte por tiempo: `ExportacionesTests` (atlas-api con RustFS). RLS de la tabla con el usuario del
  servicio: `Flit.Atlas.Modulos.Contratos.Tests`. Reintentos y cola de errores del consumidor con RabbitMQ real:
  `ConsumoTests`. El consumidor de atlas-docs y el CA de los siete formatos por la cola: `tests/exportes/test_cola.py`
  (con Pango en el CI y en `--target pruebas`).

## Decisión — parte 3: compartir desde el móvil (HU 4.6)

Hechos verificados al empezar la HU 4.6:

- La descarga de la HU 4.5 abre la URL firmada en la misma pestaña (`descarga.ts`); la URL es de otro origen (el S3) y
  responde con `Content-Disposition: attachment`.
- La CSP de la PWA (`deploy/atlas-seguridad.conf`) tiene `connect-src 'self'`: un `fetch` a la URL firmada del S3 queda
  bloqueado en la imagen nginx, y el bucket tampoco declara CORS.
- La PWA no tiene telemetría de cliente; los módulos no pueden importarse entre sí (el servicio va en el shell).

### Qué se comparte

- **Un enlace interno**, nunca una URL firmada ni un enlace anónimo (prompt maestro §8.5). `src/pwa/compartir.ts`
  (`enlaceInterno`) solo acepta rutas del propio origen; quien lo abre necesita sesión y permisos, y abrirlo es una vista
  que el servidor ya filtra y audita. Un externo solo ve botones con rutas del portal (`/portal/…`).
- **Un archivo exportado**, con la hoja nativa (`navigator.share({ files })`) si el dispositivo comparte ese tipo
  (`canShare` con un `File` vacío del mismo nombre y tipo: Chrome en Android no comparte ZIP; el escritorio sin hoja,
  nada). El archivo se baja de la URL firmada con `fetch` (sin credenciales ni referer) a memoria, con un tope de 50 MB.
  No pasa por atlas-api ni hay endpoint nuevo: la descarga ya queda en `contratos.exportacion_descargada`.
- **Dos toques** para el archivo («Preparar» y «Compartir»): Safari exige llamar `navigator.share` dentro del gesto, y
  la descarga previa lo consumiría (`NotAllowedError`). Si aun así pasa, se pide volver a tocar.

### Respaldo

Sin hoja nativa (o si falla, salvo `AbortError`, que es cancelar y no se muestra), una hoja inferior con el enlace en un
campo de solo lectura, «Copiar enlace» con aviso para lectores de pantalla, «Abrir en correo» (`mailto:` con asunto y
el enlace; el cuerpo no lleva datos del contrato, la organización ni la persona) y, solo para empleados, «Abrir en Teams»
(`teams.microsoft.com/share?href=…&preview=false`, para que Teams no intente leer una página que exige sesión).

### Descargas en iOS

Se mantiene `location.assign(url firmada)`. `window.open` (con o sin `blob:`) lo bloquea Safari tras un `await`;
`<a download>` no aplica a otro origen y con `blob:` falla en algunas versiones de la PWA instalada. En Safari y en la
PWA instalada, la URL fuera del `scope` se abre en la vista de Safari dentro de la app y descarga; en Android descarga
sin salir. El detalle está en `apps/web/src/modulos/contratos/exportaciones/descarga.ts`. En iOS, compartir el archivo
además ofrece «Guardar en Archivos».

### Dónde está

`BotonCompartir` (`src/app/ui/Compartir.tsx`) en el contrato interno, la guía y el paquete, y en el paquete, el
contrato y la guía del portal; `CompartirExportacion` en el resultado de la exportación (interna y del portal). Objetivos de 44 px y hoja
inferior en el móvil (`Hoja`).

### Auditoría y telemetría

No se registra «compartido». No hay telemetría de cliente, el destino (correo, Teams, WhatsApp…) no se conoce y no
aporta a la trazabilidad: lo que sale del servidor ya está auditado (la vista del enlace al abrirlo y la descarga del
archivo). Agregarla sería un endpoint nuevo sin un uso claro.

### Pruebas

Vitest con `navigator.share` y `canShare` simulados (`src/pwa/compartir.test.ts`, `src/app/ui/Compartir.test.tsx`,
`exportaciones/compartir.test.tsx`): con y sin soporte de archivos, cancelación sin error, falta del gesto, CSP o CORS
que impiden bajar el archivo, y que nunca se comparte una URL que no sea de Atlas. Playwright en escritorio, Pixel 7 e
iPhone 14 (`tests/e2e/compartir.spec.ts` y el portal en `portal.spec.ts`): el botón aparece, el respaldo copia el
enlace interno, el PDF llega a la hoja como `File`, axe sin errores críticos y sin scroll horizontal a 360 px.

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
- Autenticación del endpoint interno: resuelta en ATLAS-ADR-0044 (token `client_credentials` de OpenIddict con
  audiencia `atlas-docs`, firmado sin cifrar y validado con el JWKS; la cola, con un usuario de RabbitMQ por servicio).
- [por verificar] Tope de memoria del pod de atlas-docs en la 169 con WeasyPrint y el contrato más grande.
- [por verificar] Con el líder: si el Excel debe conservar el morado `#4E00EB` del Excel actual (se conservó para
  que el cliente lo reconozca) o pasar al azul de la marca.
- Tabla y retención: resueltas en la parte 2 (`contratos_exportacion`, 7 días).
- [por verificar] flit-gitops: declarar la imagen `ghcr.io/flitsas/flit-atlas/atlas-docs` en `services/atlas-qa` y
  `services/atlas-pdn` **antes** de llevar esto a `staging` (si no, el paso de digests de `cd.yml` falla, como con las
  demás), el Deployment de atlas-docs (concurrencia 1, memoria), sus variables (`ATLAS_DOCS_RABBITMQ_URL`, `ATLAS_DOCS_S3_*`,
  `ATLAS_DOCS_AUTH_*`), las de atlas-api (`Plataforma__Almacenamiento__*` y `EndpointPublico`), los usuarios de RabbitMQ
  y la `NetworkPolicy`.
- [por verificar] El host público del S3 para firmar (`EndpointPublico`): en pdn el de Contabo; en qa, MinIO o RustFS
  necesita un ingress propio. Y una regla de ciclo de vida del bucket para entradas huérfanas.
- [por verificar] Con el líder: `ext.integrador` no tiene `atlas.exportaciones.solicitar` en la matriz (§7.3 dice que
  exporta si la compartición lo permite); hoy exporta solo con el extra `exportar` de su invitación (ATLAS-ADR-0043). No
  se cambió la matriz.
- [por verificar] Con el líder (HU 4.6): para compartir el **archivo** en qa y pdn hace falta agregar el host público
  del S3 a `connect-src` de la CSP de la PWA y declarar CORS (`GET` desde el origen de Atlas) en el bucket. Sin eso,
  «Preparar» responde «No pudimos preparar el archivo» y se ofrece descargar o compartir el enlace. No se cambió la CSP:
  ampliarla es una decisión de seguridad.
- [por verificar] Probar en un iPhone real (Safari y la PWA instalada, iOS 17 y 18) y en un Android de gama media la
  descarga en la misma pestaña y la hoja con archivos; Playwright emula WebKit, no iOS.
- [por verificar] Las URL públicas por ambiente y contrato para los `servers` del spec podado
  (`Contratos:Portal:Servidores`); sin ellas, el spec exportado sale sin `servers` y Postman con `{{baseUrl}}`
  vacío.
