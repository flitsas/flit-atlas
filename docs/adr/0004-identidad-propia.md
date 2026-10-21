---
id: ATLAS-ADR-0004
titulo: Identidad propia de Atlas — ASP.NET Core Identity, Entra ID, cuentas locales por invitación y OpenIddict
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 D-04 (login con core-identity)", "plan 03 D3 (login con core-identity)", "plan 00 §18 P-06"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, §6, §7, §9 y §15, plan 00 D-04, plan 03 D3]
---

# ATLAS-ADR-0004 · Identidad propia de Atlas

## Contexto y problema

Los planes 00 (D-04) y 03 (D3) daban el login de Atlas a **core-identity**, el proveedor OIDC de FLIT 2.0. Pero core-identity **no soporta federación**, así que no puede ofrecer «Iniciar sesión con Microsoft» a los empleados. Tampoco tiene un modelo de invitación de externos sin acceso a productos (lo que P-06 pedía verificar). Atlas necesita tres cosas: (a) empleados que entren con su cuenta corporativa de Microsoft 365, (b) externos (clientes, asesores, proveedores) invitados con alcance y vigencia, con MFA obligatorio, y (c) tokens para clientes que no son el navegador (MCP, CLI, el widget en otras apps, servicios internos).

## Factores de decisión

- **No implementar criptografía propia**: hashing, firma de tokens, TOTP y WebAuthn deben salir de librerías maduras.
- MFA fuerte para externos; para empleados, el MFA de Entra ID (acceso condicional).
- No tocar el repo `flit` para la identidad.
- OWASP ASVS nivel 2 (capítulos de autenticación V2 y sesión V3).
- Revocación inmediata al cambiar roles, suspender o vencer un acceso.
- Licencias abiertas y versiones soportadas en .NET 10.

## Opciones consideradas

1. **Identidad propia dentro de `atlas-api`**: ASP.NET Core Identity como almacén, Microsoft.Identity.Web u OIDC de ASP.NET Core contra Entra ID para empleados, cuentas locales por invitación con passkeys y MFA para externos, y **OpenIddict** (servidor) para emitir tokens a clientes que no son el navegador.
2. core-identity como proveedor (D-04), agregándole federación e invitaciones.
3. Solo Entra ID: empleados como miembros y externos como **invitados B2B** del tenant de FLIT.
4. Un proveedor externo: Keycloak autoalojado, o un servicio administrado (Entra External ID, Auth0).
5. Duende IdentityServer en lugar de OpenIddict.

## Decisión

Opción elegida: **1**. El proyecto es `services/api/src/Flit.Atlas.Identidad/`, con tablas `identidad_*` en el esquema `atlas`. Versiones de referencia verificadas el 21/10/2026, que se fijan al adoptarlas en F1: .NET **10.0.x** (LTS, GA el 11/11/2025), **OpenIddict 7.7.x** (Apache-2.0, soporta .NET 10), **Microsoft.Identity.Web 4.x** (MIT). El repo `flit` ya usa OpenIddict 7.7.1 en core-identity: hay experiencia en el equipo.

### 1. Piezas

| Pieza | Librería | Responsabilidad |
|---|---|---|
| Almacén de usuarios | ASP.NET Core Identity (EF Core), con `Stores.SchemaVersion = IdentitySchemaVersions.Version3` (requisito para las passkeys) | Usuarios internos y externos, logins externos, passkeys, TOTP, códigos de recuperación, bloqueo, sellos de seguridad |
| Empleados | Manejador OIDC (Microsoft.Identity.Web) contra Entra ID, flujo de código con PKCE, tenant único | Login «Iniciar sesión con Microsoft» y aprovisionamiento JIT |
| Externos | Identity: passkeys nativas de .NET 10 (`SignInManager.MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `MakePasskeyRequestOptionsAsync`, `PasskeySignInAsync`; `IdentityPasskeyOptions.ServerDomain` = host del ambiente), contraseña y TOTP como alternativa | Cuenta local creada solo por invitación |
| Servidor de tokens | OpenIddict Server + Validation, embebido en `atlas-api` | Tokens para MCP, `atlas-cli`, el widget (F7) y servicios internos. **La PWA no usa estos tokens: usa la cookie del BFF** (ATLAS-ADR-0008) |

### 2. Modelo de datos (`identidad_*`)

| Tabla | Origen | Notas |
|---|---|---|
| `identidad_usuario`, `identidad_rol_identity`, `identidad_usuario_rol_identity`, `identidad_usuario_claim`, `identidad_usuario_login`, `identidad_usuario_token`, `identidad_usuario_passkey` | ASP.NET Core Identity v3, renombradas a `snake_case` con prefijo | El rol de Identity no se usa para autorizar (los roles viven en `plataforma_*`) |
| `identidad_oidc_aplicacion`, `identidad_oidc_autorizacion`, `identidad_oidc_scope`, `identidad_oidc_token` | OpenIddict EF Core | Clientes registrados: `atlas-mcp`, `atlas-cli`, `atlas-widget-*`, `svc-atlas-*` |
| `identidad_sesion` | Propia | Sesión del BFF por dispositivo: `id`, `usuario_id`, `creada_en`, `ultimo_uso_en`, `expira_en`, `agente_resumen`, `ip_hash`, `sello_seguridad`, `revocada_en`, `motivo_revocacion` |
| `identidad_evento_seguridad` | Propia, solo inserción | Inicio, fallo, bloqueo, MFA, cambio de factor, invitación, aceptación, revocación, restablecimiento, reautenticación |

Las invitaciones, los términos, las personas, las organizaciones y las asignaciones de rol viven en `plataforma_*` (prompt maestro §9). `identidad_usuario.persona_id` enlaza con `plataforma_persona`. Las columnas personales llevan `@pii`.

### 3. Claims de la sesión

El BFF construye el `ClaimsPrincipal` en el servidor. La cookie solo lleva el id de sesión cifrado; los claims se resuelven en el servidor con caché corta.

| Claim | Valor |
|---|---|
| `sub` | Id estable del usuario de Atlas (`identidad_usuario.id`, uuid v7). Nunca el `oid` de Entra |
| `persona_id` | `plataforma_persona.id` |
| `tipo_principal` | `empleado` \| `externo` \| `servicio` |
| `organizacion_id` | Organización interna de FLIT para empleados; la organización cliente para externos |
| `roles` | Roles vigentes de Atlas (`atlas.*` / `ext.*`): asignaciones manuales más grupos de Entra mapeados |
| `alcances` | Para externos: aplicativos, paquetes y documentos concedidos, con vencimiento; se resuelven en el servidor y no viajan completos en tokens largos |
| `sid` | Id de `identidad_sesion` |
| `amr` | Métodos usados (`mfa`, `pwd`, `otp`, `hwk`/`passkey`, `fed`), para exigir reautenticación |
| `auth_time` | Momento de la última autenticación fuerte |

Los tokens de OpenIddict llevan `sub`, `tipo_principal`, `organizacion_id`, `aud` por cliente y `scope` por módulo (`atlas.mcp`, `atlas.publicar`, `atlas.widget`), pero **no** la lista de roles ni de alcances: el recurso los evalúa en el servidor en cada petición.

### 4. Empleados (Entra ID)

- Aplicación «Flit Atlas» registrada por ambiente en el tenant de FLIT (F0.4). Autoridad de tenant único; se valida `tid` y se rechazan las cuentas personales de Microsoft y las de otros tenants.
- El MFA lo exige Entra ID; Atlas no pide un segundo factor a los empleados. Sí verifica que el claim `amr` del token de Entra incluya `mfa` **[por verificar: que la política de acceso condicional lo emita siempre]**.
- Aprovisionamiento **JIT**: en el primer ingreso se crea o enlaza la `Persona` por `oid` y correo verificado. Foto, cargo y área se leen de Microsoft Graph.
- Mapeo configurable de grupos de Entra a roles (`plataforma_mapeo_grupo_entra`). Las asignaciones manuales se suman y quedan auditadas. Si el usuario tiene demasiados grupos para el token (overage), se consulta Graph.
- Sincronización diaria con Graph: una cuenta desactivada en Entra marca la persona como inactiva y revoca sus sesiones. En cada refresco de sesión se comprueba el estado.

### 5. Externos (invitación, passkeys y MFA)

- Solo invita quien tiene `atlas.invitaciones.gestionar`, o `ext.admin_organizacion` dentro del alcance de su organización.
- La invitación fija organización, rol externo, alcance, vencimiento (obligatorio para proveedores; por defecto 90 días, máximo configurable) y permisos extra (exportar, sandbox).
- Enlace de un solo uso que vence en 72 h. El token es aleatorio de 256 bits y en la base **solo se guarda su hash** (SHA-256). Se envía por una abstracción `IEnviadorCorreo` (Graph `sendMail` desde el buzón compartido «Invitaciones Atlas», o SMTP) **[por decidir con el líder: core-notificaciones solo si él lo pide]**.
- Al aceptar: aviso de privacidad y términos (Ley 1581), con versión guardada como evidencia, y luego el registro del factor. **Passkey preferida**. La contraseña es alternativa: mínimo 12 caracteres, revisada contra listas de contraseñas filtradas (k-anonimato, sin enviar la contraseña) y con TOTP obligatorio. Se entregan 10 códigos de recuperación de un solo uso (Identity los guarda con hash).
- Las passkeys cuentan como factor fuerte (verificación de usuario requerida). Atlas no valida attestation (la librería no lo hace por defecto, y no hace falta para ASVS L2).
- Recuperación: enlace por correo de un solo uso (vence en 30 min) que obliga a registrar un factor nuevo e invalida las sesiones. Un administrador puede forzar el restablecimiento (auditado).
- Revocar, suspender, renovar y reenviar invitación. Al vencer la asignación, el acceso se corta solo (sello de seguridad y job de vencimiento).
- **Opcional, detrás de la bandera `identidad.externos_microsoft`:** vincular la cuenta Microsoft del tenant **del invitado** (Entra multi-tenant solo para externos invitados, validando que el correo coincida con la invitación y que el `tid` no sea el de FLIT). **Recomendación: no habilitarlo en el MVP**. Agrega la validación de emisores dinámicos y el MFA no lo controla FLIT. Se evalúa después de F4.
- Nunca cuentas compartidas.

### 6. Ciclo de vida de la sesión del BFF

1. El login (Entra o local) crea una fila en `identidad_sesion` y emite la cookie `__Host-atlas`, con solo el id cifrado (Data Protection, llaves compartidas).
2. Expiración deslizante: 8 h para empleados y 2 h para externos (configurables), con tope absoluto de 7 días y de 24 h respectivamente.
3. En cada petición se valida la sesión (caché de 60 s) y el **sello de seguridad**. Cambiar roles, suspender, vencer o restablecer cambia el sello e invalida todo de inmediato.
4. **Reautenticación** (`auth_time` reciente, ≤ 10 min) para acciones sensibles: exportar paquetes, cambiar roles, compartir, cambiar factores.
5. «Dispositivos conectados»: el usuario lista y cierra sus sesiones; un administrador cierra todas las de una persona.
6. El cierre de sesión revoca la fila, borra la cookie y le indica a la PWA que limpie IndexedDB, las cachés del service worker y la suscripción push del dispositivo.

### 7. Tokens de OpenIddict, llaves y rotación

- Flujos: código con PKCE (MCP y CLI con loopback o device flow), `client_credentials` (servicios internos), refresh token y **token exchange RFC 8693** (nativo desde OpenIddict 7.0, con `AllowTokenExchangeFlow()`) para el widget de F7.
- Tokens de acceso de ≤ 15 min, firmados (JWT, RS256 o ES256) y cifrados cuando la audiencia es Atlas mismo. Refresh tokens rodantes (activos por defecto en OpenIddict) con **detección de reuso**: si se presenta un refresh token ya canjeado, OpenIddict revoca toda la cadena de esa autorización. Margen de reuso corto para peticiones concurrentes.
- Audiencias por cliente; scopes por módulo; consentimiento explícito para clientes de terceros.
- **Llaves**: certificados X.509 de firma y de cifrado por ambiente, en SealedSecret (montados como archivos y nunca generados en la app en qa ni pdn). Endpoint de descubrimiento y JWKS publicados. **Rotación** cada 6 meses con solapamiento: se publica la nueva llave en el JWKS ≥ 24 h antes de empezar a firmar con ella, y la vieja se retira cuando vence el último token firmado con ella. Procedimiento en el manual de operación (F1).
- Data Protection (cookies, antifalsificación y estados de OIDC) con llaves persistidas en Redis o en la base y cifradas con un certificado propio.

### 8. core-identity (solo para el widget de F7)

core-identity **no participa en el login de Atlas**. En F7, el widget del asistente dentro de las apps de la Suite enviará el token de core-identity del usuario al endpoint de token de Atlas (token exchange, `subject_token_type = urn:ietf:params:oauth:token-type:jwt`). Atlas:

- confía en core-identity como **emisor adicional** configurado (emisor, JWKS y audiencia `atlas` fijos);
- vincula al usuario solo por **correo verificado** contra una `Persona` existente y activa (nunca crea usuarios a partir de ese token ni al revés);
- emite un token de Atlas de vida corta, con scope `atlas.widget` y los permisos de la persona en Atlas (no los de core-identity).

Esto requiere que core-identity emita tokens con audiencia `atlas` y `email_verified`, lo que es un cambio en el repo `flit` **[por coordinar en F7, no ahora]**.

### 9. Amenazas (STRIDE) y controles

| Amenaza | Ejemplo | Control |
|---|---|---|
| **S**uplantación | Robo de credenciales de un externo; phishing | Passkeys (resistentes a phishing); MFA obligatorio; bloqueo progresivo; limitación de tasa por IP y por cuenta; validación de `tid` en Entra |
| **S**uplantación | Reutilizar el enlace de una invitación | Un solo uso, 72 h, hash en la base, ligado al correo invitado |
| **T**ampering | Alterar claims o alcances en el cliente | Claims resueltos en el servidor; cookie cifrada y autenticada; tokens firmados; contexto firmado hacia `atlas-ia` |
| **R**epudio | Negar una exportación o un cambio de roles | `identidad_evento_seguridad` y auditoría de plataforma solo de inserción, con persona, sesión, IP y hora |
| **I**nformation disclosure | Enumeración de usuarios en login o recuperación | Respuestas y tiempos uniformes; mensajes genéricos |
| **I**nformation disclosure | Tokens en el navegador | BFF: ningún token en `localStorage` (ATLAS-ADR-0008) |
| **D**enegación de servicio | Fuerza bruta contra el login | Limitación de tasa en todos los endpoints de identidad; bloqueo con retroceso exponencial; CAPTCHA no requerido en el MVP |
| **E**levación de privilegios | Externo que se asigna roles; reuso de refresh token | Permisos separados (`atlas.roles.gestionar`); `ext.admin_organizacion` no puede ampliar alcances; detección de reuso; sello de seguridad al cambiar roles; reautenticación |

### 10. Controles ASVS nivel 2 (capítulos V2 y V3) que se prueban en F1 y F4

- V2.1: contraseñas de ≥ 12 caracteres, sin reglas de composición, revisadas contra listas filtradas y con pegado permitido.
- V2.2: limitación de tasa y bloqueo; notificación al usuario de los cambios de factor.
- V2.5: recuperación sin preguntas secretas, con enlaces de un solo uso y vida corta.
- V2.7 y V2.8: TOTP compatible con RFC 6238 y passkeys (WebAuthn) mediante la librería.
- V3.2 a V3.4: cookie `__Host-`, `Secure`, `HttpOnly` y `SameSite=Strict`; id de sesión aleatorio, que se renueva al autenticarse.
- V3.3: cierre de sesión e invalidación en el servidor; expiración deslizante y absoluta.
- V3.7: reautenticación para acciones sensibles.
- La revisión de seguridad de F4 incluye una prueba de penetración específica de este módulo.

## Consecuencias

### Positivas

- Atlas controla su ciclo de usuarios, el MFA de los externos y la revocación inmediata, sin cambios en `flit`.
- Empleados con SSO corporativo y externos sin cuentas en el tenant de FLIT.
- Todo lo criptográfico sale de librerías mantenidas (Identity, OpenIddict, Data Protection).

### Negativas

- Superficie de ataque nueva: identidad propia que hay que endurecer, probar y operar (rotación de llaves y certificados por ambiente).
- La misma persona puede tener identidad en Atlas y en core-identity; se mitiga vinculando por correo verificado y sin aprovisionamiento cruzado.
- Las passkeys en .NET 10 son una API nueva (GA hace 11 meses); hay menos experiencia publicada.
- Requiere en F0.4: el registro de la app en Entra ID por ambiente, el buzón de invitaciones, los certificados de OpenIddict y los permisos de Graph con consentimiento de administrador.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| core-identity (D-04) | No soporta federación; agregarla y sumar invitaciones de externos es un proyecto en otro repo (público) que bloquearía F1 |
| Solo Entra ID con invitados B2B | Los externos quedarían en el directorio de FLIT, con acceso potencial a otros recursos del tenant y gestión en el portal de Azure; las licencias y el ciclo de vida no los controla Atlas |
| Keycloak | Otro servicio Java que operar en VPS con capacidad limitada; duplica lo que Identity y OpenIddict dan dentro de la API |
| Entra External ID, Auth0 | Costo por usuario activo, datos de externos fuera de la infraestructura y menor control del flujo de invitación |
| Duende IdentityServer | Licencia comercial (el repo `flit` ya lo descartó por eso); OpenIddict es Apache-2.0 |

## Más información

- Prompt maestro §6 (identidad), §7 (roles), §12 (seguridad) y §15 (riesgos).
- Documentación de passkeys en ASP.NET Core: <https://learn.microsoft.com/aspnet/core/security/authentication/passkeys>.
- OpenIddict 7 (token exchange): <https://documentation.openiddict.com/guides/migration/60-to-70>.
- Pendientes: `amr=mfa` desde Entra; buzón de invitaciones; plan de rotación de certificados; cambio en core-identity para F7.
