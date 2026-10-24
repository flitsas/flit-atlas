---
id: ATLAS-ADR-0048
titulo: Endurecimiento de F4 — CSP con informe de violaciones y Trusted Types en modo informe, cabeceras de seguridad en la API y en atlas-web, límites de tasa por persona, organización, IP y llave de sandbox, y tope de exportaciones en curso
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §6, §12, §13, §14 y §15, HU 4.10, ATLAS-ADR-0008, ATLAS-ADR-0020, ATLAS-ADR-0022, ATLAS-ADR-0039, ATLAS-ADR-0042, ATLAS-ADR-0043]
---

# ATLAS-ADR-0048 · Endurecimiento de F4

## Contexto y problema

La HU 4.10 pide, antes de abrir el acceso a externos en pdn (§12): CSP estricta, cabeceras de seguridad, limitación de
tasa por usuario y por organización (más estricta para externos), una prueba de carga (20 personas y 2 exportaciones
simultáneas sin degradar) y el terreno listo para la revisión de seguridad dedicada.

Hechos verificados en el código el 24/10/2026:

- La CSP de la PWA la pone el nginx de atlas-web (`apps/web/deploy/atlas-seguridad.conf`): `script-src 'self'; style-src
  'self'` sin excepciones (ATLAS-ADR-0020), con `frame-ancestors 'none'`, `base-uri 'self'`, `form-action 'self'` y
  `object-src 'none'`, sin informe de violaciones, sin HSTS ni `Cross-Origin-Resource-Policy`. El visor de Scalar tiene su
  propia CSP con `style-src 'unsafe-inline'` y `sandbox` (ATLAS-ADR-0039).
- atlas-api no ponía ninguna cabecera de seguridad salvo en dos respuestas (la página del servidor de tokens y el feed
  `.ics`). Detrás de atlas-web, las rutas `/api`, `/connect`, `/.well-known`, `/hubs` y `/health` llegan sin las
  cabeceras de la PWA: las de la API son las que ve el navegador.
- La limitación de tasa (ATLAS-ADR-0022) solo cubría las rutas de identidad y el reporte de errores de la PWA; el resto
  de la API, el portal y las exportaciones no tenían límite. atlas-docs genera de a una exportación por pod
  (ATLAS-ADR-0042): nada impedía que una organización llenara la cola.
- La PWA usa `dangerouslySetInnerHTML` (guías en Markdown, ya sanitizadas con DOMPurify) y el registro del service
  worker (Workbox), dos sumideros de Trusted Types; Mermaid aún no está (F6).
- La HU 4.6 baja el archivo exportado con `fetch` a la URL firmada del S3, que `connect-src 'self'` bloquea; la HU 4.7
  agrega un proxy del sandbox sin sesión, autenticado con una llave por compartición.

## Factores de decisión

- No debilitar la CSP del shell (§12, ATLAS-ADR-0020) y saber cuándo algo la viola en qa y pdn, sin datos personales en
  el log (§13).
- Que los límites no estorben a una persona que navega rápido y sí corten a un script, más estrictos para externos, y
  que una organización no pueda acaparar recursos compartidos (cola de exportaciones, portal).
- Librerías del framework, sin paquetes nuevos (`Microsoft.AspNetCore.RateLimiting`, como ATLAS-ADR-0022).
- Valores en configuración, con buenos valores por defecto.
- Una prueba de carga reproducible por cualquiera, con el generador de exportaciones real.

## Opciones consideradas

### Trusted Types

1. **Exigirlos** (`require-trusted-types-for 'script'`) en la CSP de la PWA.
2. **Solo en modo informe** (`Content-Security-Policy-Report-Only`) con las políticas que se esperan (`atlas-markdown`,
   `dompurify`), hasta medir qué rompe.
3. No usarlos.

### Dónde viven los límites del resto de la API

1. **El mismo limitador global de la identidad, con particiones nuevas** (por persona, por organización, por IP, por
   llave de sandbox) y una marca de endpoint (`ConLimiteDeExportacion`) para lo que no es una ruta fija.
2. Políticas por endpoint (`RequireRateLimiting`): un endpoint admite una sola política, y una exportación del portal
   necesita a la vez la del portal, la de exportaciones por persona y la de su organización.
3. Un limitador distribuido en Redis.

### Tope de exportaciones

1. **Contar las pendientes o en proceso** de la persona y de su organización externa, con un candado transaccional
   (`pg_advisory_xact_lock`) al crear.
2. Solo el límite de tasa de solicitudes por minuto.

## Decisión

Trusted Types: **opción 2**. Límites: **opción 1**. Exportaciones: **opción 1**.

### CSP y cabeceras de atlas-web (`apps/web/deploy`)

| Cabecera | PWA | Visor (`/visor/`) |
|---|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' $ATLAS_S3_PUBLICO; manifest-src 'self'; worker-src 'self'; frame-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'; report-uri /api/v1/plataforma/csp-reportes; report-to csp` | la de ATLAS-ADR-0039 más `report-uri` y `report-to` |
| `Content-Security-Policy-Report-Only` | `require-trusted-types-for 'script'; trusted-types atlas-markdown dompurify; report-uri …; report-to csp` | — |
| `Reporting-Endpoints` | `csp="/api/v1/plataforma/csp-reportes"` | igual |
| `Strict-Transport-Security` | `$ATLAS_HSTS` (la imagen trae `max-age=31536000; includeSubDomains`; vacío = sin la cabecera) | igual |
| `X-Content-Type-Options` | `nosniff` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | `no-referrer` |
| `Permissions-Policy` | `accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(self), payment=(), usb=()` (micrófono para el dictado de F7) | igual, con `microphone=()` |
| `Cross-Origin-Opener-Policy` | `same-origin` | — (el visor es un iframe) |
| `Cross-Origin-Resource-Policy` | `same-origin` | `cross-origin` (archivos estáticos públicos pedidos desde un origen opaco) |

- `base-uri` pasa de `'self'` a `'none'` (la PWA no usa `<base>`). `connect-src` suma el origen público del S3 que firma
  las descargas (variable `ATLAS_S3_PUBLICO`, el mismo `Plataforma__Almacenamiento__EndpointPublico` de atlas-api), para
  la HU 4.6; vacío, queda `'self'`. El bucket además necesita CORS `GET` desde el origen de Atlas (runbook).
- `vite preview` sirve las mismas cabeceras, leídas de los mismos archivos de `deploy/`: las pruebas E2E con la API
  simulada (`modulos.spec.ts`, `portal.spec.ts`) vigilan la CSP sin construir la imagen, y `cabeceras.spec.ts` revisa
  cada cabecera (contra `vite preview` o contra la imagen con `ATLAS_E2E_URL`). Las violaciones de solo informe no fallan
  las pruebas: quedan como anotación.

### Cabeceras de atlas-api (`CabecerasSeguridad`)

En cada respuesta, salvo que la respuesta ya traiga la suya: `Content-Security-Policy: default-src 'none';
frame-ancestors 'none'; base-uri 'none'; form-action 'none'; report-uri …; report-to csp`, `Reporting-Endpoints`,
`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, la misma `Permissions-Policy`,
`Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin` (no afecta las peticiones con
CORS de los clientes OIDC) y, fuera de `local` y solo en HTTPS (el esquema que deja `UseForwardedHeaders`),
`Strict-Transport-Security: max-age=31536000; includeSubDomains`. Sin `preload`: es una decisión del dominio.

### Informe de violaciones (`POST /api/v1/plataforma/csp-reportes`)

- Sin sesión ni cabecera antifalsificación (el navegador no las manda; desde el visor llega con `Origin: null`): se
  exceptúa de `UseAntifalsificacionApi` y, en vacuum, de `owasp-protection-global-unsafe` (en
  `contracts/vacuum/openapi-excepciones.yaml`). Acepta `application/csp-report` (report-uri) y `application/reports+json`
  (Reporting API, hasta 10 informes por petición).
- Solo escribe en el log (categoría `Flit.Atlas.Csp`): advertencia `Violación de CSP (bloqueada)` si la política bloqueó,
  informativo `Violación de CSP (solo informe)` si no. Registra la directiva, el origen de lo bloqueado (o su palabra
  clave), la ruta del documento y de la fuente sin consulta ni fragmento (con los tachados de los errores de la PWA), la
  línea y la columna. Nunca la muestra de código, el referer, el navegador ni la IP.
- Topes: cuerpo de 16 KB y 30 informes por minuto por IP y 300 en total por réplica.

### Límites de tasa (sección `Identidad:LimiteTasa`, ventana fija de 1 minuto por réplica)

Se suman a los de ATLAS-ADR-0022 (ingreso 20/IP, identidad 120/IP y 60/usuario, tokens 30/IP, externos 10/IP, errores de
la PWA 10/persona y 60/IP), que no cambian.

| Partición | Alcance | Por defecto |
|---|---|---|
| `PorEmpleado` | `/api/v1/**` y `/hubs/**` por persona con sesión de empleado | 600 |
| `PorExterno` | lo mismo por persona externa | 240 |
| `PortalPorOrganizacion` | `/api/v1/portal/**` de todas las personas de una organización externa | 600 |
| `ExportacionesPorPersona` | solicitudes de exportación (`ConLimiteDeExportacion`), internas o del portal | 5 |
| `ExportacionesPorOrganizacion` | solicitudes de exportación del portal por organización externa | 10 |
| `AnonimoPorIp` | `/api/v1/**` sin sesión de la PWA (rutas públicas, Bearer de OpenIddict y de GitHub Actions), fuera de identidad | 300 |
| `SandboxPorLlave` | proxy del sandbox (`/api/v1/contratos/sandbox/**`, HU 4.7) por SHA-256 de la llave Bearer | 60 |
| `SandboxPorIp` | el mismo proxy por IP, con o sin llave | 120 |
| `ReportesCspPorIp` / `ReportesCspTotal` | informes de la CSP | 30 / 300 |

- Para partir por persona y por organización, el limitador (antes de `UseAuthentication`) lee la sesión de la PWA en
  todas las rutas de la API salvo las que inician un ingreso y los informes de la CSP; el manejador de la cookie guarda
  el resultado y `UseAuthentication` no vuelve a consultar la base.
- Respuesta: **429** con `Retry-After` y problem+json en español (`title` «Demasiadas peticiones. Intenta de nuevo en un
  momento.», `codigo = "limite_de_tasa"`, `reintentarEnSegundos`), y el evento `identidad.limite_tasa` una vez por
  cliente y ventana. El contrato OpenAPI declara el 429 en **toda** operación bajo `/api/` (tres respuestas reutilizables
  en `components.responses`) y vacuum exige ahora `owasp-define-error-responses-429` como error.

### Exportaciones en curso (sección `Contratos:Exportaciones`)

- `MaximoEnCursoPorPersona` (2) y `MaximoEnCursoPorOrganizacion` (2, solo externos): pendientes o en proceso. La
  siguiente responde **429** `codigo = "exportaciones_en_curso"` con `Retry-After` (`EsperaEnCurso`, 30 s) y un título que
  la PWA muestra.
- Se revisa antes de armar la entrada (para no subirla en vano) y otra vez dentro de la transacción que crea la
  exportación, con `pg_advisory_xact_lock` por organización (o por persona): solicitudes simultáneas no pasan del tope.
  Si la segunda revisión rechaza, la entrada subida queda huérfana en el S3, como cuando falla la base (ATLAS-ADR-0042).
- Una exportación atascada cuenta hasta que `VencedorExportaciones` la corta (2 h): es el precio de no tener un
  contador aparte.

### Prueba de carga (`tests/carga/`)

k6 2.3.0 (binario Go, del 21/09/2026, AGPL-3.0, solo como herramienta: no se distribuye) con `atlas.js`: 20 personas con
sesión navegando inicio, catálogo, contratos y portal con pausas de 1 a 3 s, y desde el minuto 1 dos exportaciones
simultáneas del paquete en los siete formatos. Umbrales: p95 de las lecturas < 500 ms en total, antes de las
exportaciones y durante ellas; lecturas con error < 1 %; exportaciones listas 100 %; checks > 99 %. La corrida local es
una prueba de xUnit que se omite sin `ATLAS_CARGA_K6` (`PruebaCargaTests`): atlas-api en Kestrel con Postgres, Redis,
RabbitMQ, RustFS y atlas-docs real, el contrato ICT en un paquete compartido con «Organismo Demo», 15 empleados y 5
externos. Contra qa, las sesiones van por variables de entorno (README).

## Consecuencias

### Positivas

- Toda violación de la CSP en qa y pdn queda en Loki sin datos personales, con una alerta posible sobre la advertencia.
- Se sabrá qué rompería exigir Trusted Types antes de hacerlo (F6 con Mermaid).
- Toda la API tiene un límite; los externos y sus organizaciones, uno más estricto; la cola de exportaciones no la
  acapara nadie; el sandbox, sin sesión, tiene el suyo por llave y por IP.
- El contrato declara el 429 en cada operación, y vacuum lo exige.

### Negativas

- Los contadores siguen en memoria de cada réplica: con dos réplicas el límite efectivo es el doble
  (ATLAS-ADR-0022). Un limitador distribuido queda pendiente **[por decidir]**.
- Cada petición autenticada a la API lee la sesión antes del limitador (la misma consulta que ya hacía
  `UseAuthentication`, que ya no se repite).
- El contrato OpenAPI crece en una referencia 429 por operación.
- Trusted Types en modo informe genera informes en cada carga del shell mientras no se adopten políticas (informativos;
  la alerta es sobre las advertencias).
- `Permissions-Policy` con `microphone=(self)` deja el micrófono al propio origen desde ya, aunque el dictado llegue en F7.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Exigir Trusted Types ya | El registro del service worker y el Markdown de las guías lo violan hoy; habría que agregar políticas (o una `default`, que lo debilita) sin datos de qué más rompe |
| Políticas de límite por endpoint | Una sola política por endpoint; las exportaciones del portal necesitan tres particiones a la vez |
| Limitador en Redis | Paquete nuevo y una dependencia más en el camino de cada petición; se reevalúa con más réplicas |
| Solo límite por minuto para exportaciones | No impide que una organización deje la cola llena con pedidos espaciados |
| Nonces de CSP | La PWA no tiene nada en línea; los nonces complican el service worker (ATLAS-ADR-0039) |

## Más información

- Código: `services/api/src/Flit.Atlas.Api/CabecerasSeguridad.cs`, `Flit.Atlas.Identidad/Seguridad/LimitacionTasa.cs` y
  `EndpointsReportesCsp.cs`, `Flit.Atlas.Plataforma/Modulos/LimitesDeTasa.cs`,
  `Flit.Atlas.Modulos.Contratos/Exportaciones/` y `apps/web/deploy/`. Pruebas: `EndurecimientoTests`,
  `ExportacionesTests`, `apps/web/tests/e2e/cabeceras.spec.ts` y `csp.spec.ts`, `PruebaCargaTests`.
- Runbook: `deploy/runbook-atlas.md`. Revisión de seguridad: `docs/seguridad/revision-f4.md`.
- [por verificar] En flit-gitops: `ATLAS_S3_PUBLICO` en el ConfigMap de atlas-web y CORS `GET` del bucket desde el origen
  de Atlas; que el nginx del host **reemplace** `X-Forwarded-For` (no lo encadene con el del cliente), para que la IP de
  los límites no se pueda falsear.
- [por verificar] En Safari real: el WebKit de Playwright informa `style-src-attr inline` al crear el `<input readOnly>` del
  feed `.ics` (`/soporte/microsoft-365`, pila en el `createElement` de React; la PWA no pone estilos en línea). La prueba
  lo anota en el perfil iPhone en lugar de fallar.
- El proxy del sandbox de la HU 4.7 (ATLAS-ADR-0045) vive en `/api/v1/contratos/sandbox/{ruta}/{contrato}/{**resto}`: lo
  cubren `SandboxPorLlave` y `SandboxPorIp` (verificado al integrar).
