# Revisión de seguridad de F4: alcance y checklist

> **Estado:** preparación (HU 4.10, parte 1), 24/10/2026. Este documento **no** es la revisión ni declara hallazgos: deja
> el alcance, el checklist y dónde está cada control para que la revisión dedicada (y la prueba de penetración del
> módulo de identidad, §6 y §15 del prompt maestro) los verifique. La columna «Estado» la llena quien revisa.
> Los números de requisito son los de **OWASP ASVS 5.0** nivel 2, por sección; **[por verificar]** el número exacto de
> cada requisito contra la versión 5.0.0 publicada al empezar la revisión.

## 1. Alcance

| Área | Qué entra | Dónde vive | Decisiones |
|---|---|---|---|
| Registro por invitación | Invitación de un solo uso (72 h, token solo como hash), aceptación con passkey o contraseña + TOTP, términos (Ley 1581), administrador de la organización | `services/api/src/Flit.Atlas.Identidad/Externos/` | ATLAS-ADR-0025 |
| MFA y passkeys | WebAuthn de ASP.NET Core Identity (.NET 10), TOTP, códigos de recuperación con hash, segundo factor obligatorio para externos | `Externos/FactoresExternos.cs`, `Externos/CuentasExternas.cs`, `Externos/AlmacenUsuarios.cs` | ATLAS-ADR-0025 |
| Recuperación | Enlace de un solo uso por correo y nuevo registro del factor; restablecimiento forzado por un administrador | `Externos/EndpointsCuentasExternas.cs`, `Externos/TokensEnlace.cs`, `Externos/EndpointsAdministracionExternos.cs` | ATLAS-ADR-0025 |
| Empleados | OIDC con PKCE contra Entra ID, tenant único (`tid`), aprovisionamiento just-in-time, mapeo de grupos a roles | `Identidad/Entra/`, `Identidad/Aprovisionamiento/` | ATLAS-ADR-0004 |
| Sesiones | Cookie `__Host-atlas` (HttpOnly, Secure, SameSite=Strict) con solo el id; sesión en el servidor con sello de seguridad comparado en cada petición; sesión nueva en cada ingreso; dispositivos conectados; reautenticación (10 min); sesión más corta para externos | `Identidad/Sesiones/`, `Identidad/EndpointsSesiones.cs`, `Identidad/Seguridad/Reautenticacion.cs`, `Identidad/Seguridad/InvalidadorSesiones.cs` | ATLAS-ADR-0008, ATLAS-ADR-0022 |
| Tokens OpenIddict | Código con PKCE (atlas-cli, MCP), `client_credentials` (servicios), refresh con rotación y detección de reutilización, audiencias y scopes, JWKS, certificados en SealedSecret | `Identidad/Tokens/` | ATLAS-ADR-0024, ATLAS-ADR-0044 |
| Cuenta de emergencia | Activación por `kubectl exec` con enlace de un solo uso, MFA local, eventos de alta severidad | `Identidad/Emergencia/` | ATLAS-ADR-0027 |
| Portal y aislamiento | `contratos_comparticion` con RLS como única puerta, `AccesoPortal` en cada petición, spec podado, auditoría de cada vista | `Flit.Atlas.Modulos.Contratos/Portal/`, `Comparticiones/`, `Poda/` | ATLAS-ADR-0038, ATLAS-ADR-0043 |
| Exportaciones y URL firmadas | Cola, entrada en el S3, URL SigV4 de 15 min, retención, tope en curso, auditoría | `Flit.Atlas.Modulos.Contratos/Exportaciones/`, `Flit.Atlas.Plataforma/Almacenamiento/`, `services/docs/` | ATLAS-ADR-0042, ATLAS-ADR-0048 |
| Publicación OIDC desde CI | Token de GitHub Actions validado con JWKS, audiencia por ambiente, lista blanca por repo, rama y workflow reutilizable fijado | `Flit.Atlas.Plataforma/Publicacion/`, `Flit.Atlas.Modulos.Contratos/EndpointsPublicacion.cs`, `.github/workflows/atlas-publicar.yml` | ATLAS-ADR-0009, ATLAS-ADR-0035 |
| atlas-docs con JWT | Token `client_credentials` de OpenIddict con audiencia `atlas-docs` validado con el JWKS; usuario de RabbitMQ por servicio | `services/docs/src/atlas_docs/autenticacion.py`, `cola.py` | ATLAS-ADR-0044 |
| Endurecimiento | CSP y cabeceras (API, PWA, visor), informes de violaciones, límites de tasa | `Flit.Atlas.Api/CabecerasSeguridad.cs`, `Identidad/Seguridad/LimitacionTasa.cs`, `EndpointsReportesCsp.cs`, `apps/web/deploy/` | ATLAS-ADR-0039, ATLAS-ADR-0048 |

Fuera de alcance: el asistente (F7, OWASP Top 10 para LLM), los legados y FLIT 1, la infraestructura de las VPS (salvo lo
que el checklist marca como dependencia de `flit-gitops`).

### Para la prueba de penetración de identidad

Ambiente: qa (`qa.atlas.flitsas.com`) con una organización de prueba («Organismo Demo»), dos externos por rol y un
empleado de prueba; nunca pdn. Casos mínimos: enumeración de cuentas en ingreso, recuperación e invitación (respuestas
y tiempos iguales); reutilización y vencimiento de enlaces (invitación 72 h, recuperación, emergencia 1 h); fuerza bruta
contra contraseña y TOTP (bloqueo progresivo y límites por IP); fijación y robo de sesión (cookie anterior tras
reingreso, sello tras suspender o cambiar roles, cierre remoto); CSRF (cabecera propia y `Origin`); reautenticación
en acciones sensibles; PKCE, `redirect_uri`, reutilización de código y de refresh, audiencias y scopes en OpenIddict;
suite de la OpenID Foundation contra qa desde la red de FLIT (ATLAS-ADR-0024 **[por verificar]**); IDOR entre
organizaciones en el portal, exportaciones y sandbox; firma y vencimiento de URL del S3; publicación con tokens de otro
repo, rama, evento (`pull_request`) o workflow.

## 2. Checklist ASVS 5.0 nivel 2

### V6 · Autenticación

| Sección | Control en Atlas | Dónde | Pruebas que ya lo cubren | Estado |
|---|---|---|---|---|
| V6.1 Documentación | Métodos por tipo de cuenta, límites y bloqueo documentados | ATLAS-ADR-0004, 0022, 0025, 0027 | — | |
| V6.2 Contraseñas | Mínimo 12, sin reglas de composición, revisión contra filtraciones (lista local y consulta por rango a Have I Been Pwned), hash de ASP.NET Core Identity (PBKDF2), solo para externos | `Externos/ValidadorContrasenaFiltrada.cs`, `Datos/IdentidadDbContext.cs` (`ConfigurarIdentity`) | `CuentasExternasTests` | |
| V6.3 Seguridad general | Bloqueo tras 10 fallos con duración progresiva; límite de tasa en toda ruta de identidad (por IP, por usuario, más estricto para externos); mensajes genéricos que no revelan si la cuenta existe | `Seguridad/VerificadorContrasena.cs`, `Seguridad/LimitacionTasa.cs`, `Externos/EndpointsCuentasExternas.cs` | `SeguridadTests`, `CuentasExternasTests` | |
| V6.4 Ciclo de vida y recuperación | Invitación y recuperación con enlace de un solo uso (hash, vencimiento), nuevo registro del factor, códigos de recuperación con hash | `Externos/TokensEnlace.cs`, `Externos/ServicioInvitaciones.cs`, `Externos/CorreosExternos.cs` | `CuentasExternasTests` | |
| V6.5 MFA | Externos: passkey (factor fuerte) o contraseña + TOTP obligatorio; empleados: MFA de Entra (acceso condicional) | `Externos/FactoresExternos.cs`, `Externos/SesionExterna.cs` | `CuentasExternasTests` | |
| V6.8 Proveedor de identidad | OIDC con PKCE, `state`, `nonce`, tenant validado (`tid`), cuentas personales rechazadas, `prompt=login` para reautenticar | `Entra/IngresoEntra.cs`, `Entra/ValidadorCuentaEntra.cs` | `ValidadorCuentaEntraTests`, `IdentidadTests`, `SeguridadTests` | |
| Cuenta de emergencia | Sin contraseña por defecto, activación solo con acceso al clúster, MFA local, eventos de alta severidad | `Emergencia/` | `EmergenciaTests` | |

### V7 · Sesión

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V7.1 Documentación | Vidas: empleados 8 h deslizante; externos 2 h deslizante y 24 h absoluta; reautenticación 10 min | `Sesiones/AlmacenSesiones.cs` (`OpcionesSesion`), ATLAS-ADR-0022 | — | |
| V7.2 Fundamentos | Id de sesión aleatorio en el servidor, cookie cifrada con Data Protection (llaves compartidas y protegidas con certificado), sesión nueva en cada ingreso | `Sesiones/AlmacenSesiones.cs`, `Sesiones/RenovacionAlIngresar.cs`, `ExtensionesIdentidad.cs` (`ConfigurarCookie`, `ConfigurarProteccionDeDatos`) | `SesionesTests`, `IdentidadTests` | |
| V7.3 Vencimiento | Deslizante y absoluto validados en el servidor en cada petición | `AlmacenSesiones.RetrieveAsync` | `SesionesTests` | |
| V7.4 Terminación | Cerrar sesión (con `Clear-Site-Data`), cerrar otras, cierre remoto por el administrador, sello que corta al suspender, cambiar roles o vencer | `EndpointsSesiones.cs`, `Seguridad/InvalidadorSesiones.cs`, `Autorizacion/VigilanteVencimientos.cs` | `SesionesTests`, `RolesTests` | |
| V7.5 Abuso de sesión | Dispositivos conectados visibles; reautenticación para exportar, compartir, cambiar roles y factores | `Seguridad/Reautenticacion.cs`, `Plataforma/Permisos/Reautenticacion.cs` | `SesionesTests`, `ExportacionesTests`, `PortalContratosTests` | |
| V7.6 Reautenticación federada | `prompt=login` a Entra; `auth_time` del token **[por verificar el claim opcional en cada app de Entra]** | `Entra/IngresoEntra.cs` | `SeguridadTests` | |
| CSRF (relacionado, V3.5) | `SameSite=Strict`, cabecera `X-Atlas-Csrf` y `Origin` del mismo host en toda mutación bajo `/api` | `EndpointsIdentidad.UseAntifalsificacionApi` | `IdentidadTests`, `ProxyTests` | |

### V8 · Control de acceso

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V8.1 Documentación | Matriz rol × permiso generada desde el código | `docs/permisos/matriz-rol-permiso.md`, `Plataforma/Permisos/` | `MatrizPermisosTests` | |
| V8.2 Diseño | Permiso por endpoint (`RequierePermiso`), predicado obligatorio por organización en EF Core y RLS en las tablas con organización | `Plataforma/Permisos/Autorizacion.cs`, `Plataforma/Datos/` | `AutorizacionTests`, `Flit.Atlas.Plataforma.Tests/AislamientoTests` | |
| V8.3 Por operación | El portal parte de las comparticiones de la organización de la sesión en cada petición (`AccesoPortal`); lo ajeno es 404; spec podado y verificado; exportaciones y descargas revisan el acceso otra vez | `Contratos/Portal/AccesoPortal.cs`, `Portal/SpecsDelPortal.cs`, `Exportaciones/EndpointsExportaciones.cs` | `AislamientoPortalTests` (cada endpoint del portal tiene su caso; bloquea el release), `PortalContratosTests`, `ExportacionesTests`, `Flit.Atlas.Modulos.Contratos.Tests/PortalTests` | |
| V8.4 Otros | Vencimientos de asignaciones y comparticiones sin intervención; auditoría de solo inserción de cada vista, exportación y acceso negado de un externo | `Autorizacion/VigilanteVencimientos.cs`, `Plataforma/Entidades` (auditoría) | `RolesTests`, `Flit.Atlas.Plataforma.Tests/AuditoriaTests` | |

### V1 y V2 · Codificación, validación y lógica de negocio

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V1 Codificación y sanitización | Markdown de las guías sin HTML (escapado) + DOMPurify con lista blanca; Mermaid estricto (F6); SVG sin scripts (render); páginas HTML de la API codificadas | `apps/web/src/modulos/contratos/guias/Markdown.tsx`, `Tokens/EndpointsTokens.cs` | pruebas de Vitest de `Markdown`, `TokensTests` | |
| V2.2 Validación de entrada | DTO validados en cada endpoint (ValidationProblem en español); topes de tamaño de cuerpo (Kestrel y lectura acotada); specs validados y con tope de 5 MB; alias de YAML acotados | endpoints de cada módulo, `Contratos/Specs/AnalizadorSpec.cs`, `EndpointsErroresCliente.cs`, `EndpointsReportesCsp.cs` | pruebas de cada módulo, `ErroresClienteTests`, `EndurecimientoTests` | |
| V2.3 Lógica de negocio | Reglas de paquetes y comparticiones (solo publicados, proveedores con vencimiento, revisión publicable); exportaciones con estados y resultados idempotentes | `Paquetes/ReglasPaquetes.cs`, `Comparticiones/ReglasComparticiones.cs`, `Exportaciones/ManejadorResultadosExportacion.cs` | `PaquetesContratosTests`, `ExportacionesTests` | |
| V2.4 Anti-automatización | Límites de tasa por persona, organización, IP y llave de sandbox; tope de exportaciones en curso con candado | `Seguridad/LimitacionTasa.cs`, `Exportaciones/ServicioExportaciones.cs` | `SeguridadTests`, `EndurecimientoTests`, `ExportacionesTests`, prueba de carga (`tests/carga/`) | |

### V4 · API y servicios web

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V4.1 General | Contrato OpenAPI 3.1 generado y versionado, lint con vacuum (OWASP) en el CI; 429 declarado en toda operación; `Content-Type` correcto y `nosniff`; la cookie no sirve en endpoints con scope ni el Bearer en los de la PWA | `contracts/openapi/atlas.v1.yaml`, `contracts/vacuum/`, `Flit.Atlas.Api/ContratoOpenApi.cs` | `ContratoOpenApiTests`, `TokensTests` | |
| V4.2 Estructura de mensajes | Encabezados de proxy solo desde redes confiables (`Atlas:Proxy:RedesConfiables`, límite de saltos) | `Flit.Atlas.Api/Program.cs` | `ProxyTests` | |
| V4.4 WebSocket | SignalR bajo `/hubs` con la sesión de la PWA y los límites por persona | `Flit.Atlas.Plataforma` | — **[por verificar]** | |
| Servicio a servicio | atlas-docs valida el JWT (firma con el JWKS, emisor, audiencia, scope, vencimiento); RabbitMQ con usuario por servicio | `services/docs/src/atlas_docs/autenticacion.py` | `services/docs/tests/test_autenticacion.py` | |
| Publicación desde CI | JWKS de GitHub con caché y espera mínima, audiencia por ambiente, sin `pull_request`, workflow reutilizable fijado, lista blanca | `Plataforma/Publicacion/` | `Flit.Atlas.Plataforma.Tests/PublicacionCiTests`, `ModuloContratosTests` | |

### V3 · Seguridad del frontend web (cabeceras)

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V3.4 Cabeceras | CSP sin `unsafe-inline` en scripts ni estilos (salvo estilos del visor aislado), `frame-ancestors`, `base-uri`, `form-action`, `object-src 'none'`, informes; Trusted Types en modo informe; HSTS fuera de local; `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP y CORP | `apps/web/deploy/`, `Flit.Atlas.Api/CabecerasSeguridad.cs` | `apps/web/tests/e2e/cabeceras.spec.ts`, `csp.spec.ts`, `modulos.spec.ts`, `portal.spec.ts`, `EndurecimientoTests` | |
| Iframe del visor | `sandbox` sin `allow-same-origin`, `postMessage` validado por origen y forma, «Probar» solo a QA y sandbox | `apps/web/src/modulos/contratos/visor/` | pruebas de Vitest del visor, `modulos.spec.ts` | |

### Datos, URL firmadas y registros (V14, V16)

| Control | Dónde | Pruebas | Estado |
|---|---|---|---|
| URL firmadas SigV4 de 15 min (tope en configuración), un archivo por URL, como adjunto; descarga auditada | `Plataforma/Almacenamiento/AlmacenObjetos.cs`, `Exportaciones/EndpointsExportaciones.cs` | `ExportacionesTests`, `ExportacionesPuntaAPuntaTests` | |
| Retención de 7 días y borrado | `Exportaciones/VencedorExportaciones.cs` | `ExportacionesTests` | |
| Logs sin datos personales (errores de la PWA y CSP tachados; IP solo en tablas `@pii`) | `EndpointsErroresCliente.cs`, `EndpointsReportesCsp.cs` | `ErroresClienteTests`, `EndurecimientoTests` | |
| Eventos de seguridad de solo inserción, con auditoría en la misma transacción | `Seguridad/RegistroSeguridad.cs` | `SeguridadTests` | |

## 3. Lo que ya cubren las pruebas automatizadas

- **.NET** (`dotnet test Flit.Atlas.slnx`, Postgres y Redis reales con Testcontainers): identidad, sesiones, cuentas
  externas, emergencia, tokens (suite propia de OIDC), autorización parametrizada por rol, matriz, aislamiento del
  portal (bloqueante), RLS en SQL directo, exportaciones de punta a punta con atlas-docs real, publicación desde CI,
  cabeceras, informes de CSP y límites de tasa.
- **PWA** (Vitest y Playwright en escritorio, Pixel 7 e iPhone 14): sanitización del Markdown, visor aislado, CSP y
  cabeceras contra `vite preview` (las mismas de nginx) o la imagen.
- **atlas-docs** (pytest): validación del JWT y del consumidor de la cola.
- **Carga** (`tests/carga/`): 20 personas y 2 exportaciones con los límites de siempre.

## 4. Pendientes que la revisión debe mirar **[por verificar]**

- Que el nginx del host **reemplace** `X-Forwarded-For` (no lo encadene con el del cliente): si no, la IP de los límites
  y de la auditoría se puede falsear.
- Límites de tasa en memoria por réplica: con varias réplicas, el efectivo se multiplica (ATLAS-ADR-0022 y 0048).
- `kubectl exec` al namespace limitado por RBAC a quienes deben usar la cuenta de emergencia (ATLAS-ADR-0027).
- Claim opcional `auth_time` en las apps de Entra de qa y pdn.
- `ATLAS_S3_PUBLICO` y CORS del bucket en `flit-gitops` (HU 4.6); regla de ciclo de vida del bucket.
- Usuarios de RabbitMQ por servicio y `NetworkPolicy` de atlas-docs (ATLAS-ADR-0044).
- Suite oficial de la OpenID Foundation contra qa.
- Trusted Types: revisar los informes de qa y decidir cuándo exigirlos (ATLAS-ADR-0048).
- Política de retención de IPs (Ley 1581, ATLAS-ADR-0022).
