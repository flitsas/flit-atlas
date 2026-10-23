---
id: ATLAS-ADR-0024
titulo: Servidor de tokens — OpenIddict embebido, clientes por configuración, scopes por módulo, llaves en certificados montados y conformidad con pruebas propias
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, §6, §6.3, §9 y §14, HU 1.3c, ATLAS-ADR-0004, ATLAS-ADR-0008, ATLAS-ADR-0021, ATLAS-ADR-0022, ATLAS-ADR-0023]
---

# ATLAS-ADR-0024 · Servidor de tokens: OpenIddict embebido, clientes por configuración, scopes por módulo y llaves en certificados montados

## Contexto y problema

ATLAS-ADR-0004 §7 decidió un servidor OIDC embebido (OpenIddict) para los clientes que no son el navegador: `atlas-cli`, el servidor MCP y el widget (F7) y los servicios internos (`client_credentials`). La PWA sigue con la cookie del BFF (ATLAS-ADR-0008). La HU 1.3c lo construye y pide, como criterios de aceptación: que un servicio obtenga un token con `client_credentials` y llame a un endpoint protegido por scope; que reutilizar un refresh ya usado revoque la cadena completa; y pruebas con la suite de conformidad básica de OIDC o un equivalente **[verificar herramienta]**.

Faltaba decidir: versión de OpenIddict, cómo se registran los clientes y sus secretos, cómo se declaran los scopes sin duplicar el manifiesto de permisos (ATLAS-ADR-0023), cómo convive el Bearer con la cookie y la antifalsificación, de dónde salen las llaves y cómo se rotan, qué va al contrato OpenAPI y qué herramienta prueba la conformidad.

## Factores de decisión

- Nada de criptografía propia (A-03): firma, cifrado, PKCE, rotación de refresh y detección de reutilización son de OpenIddict y del framework.
- Tokens de acceso ≤ 15 min, refresh con rotación y detección de reutilización, audiencias por cliente, scopes por módulo (§6.3).
- Secretos solo en SealedSecret; nada en el repo (§1 regla 5, §12).
- La cookie del BFF nunca debe bastar para un endpoint con scope, y un Bearer nunca debe abrir la sesión del BFF.
- Suspender o cambiar los roles de una persona corta sus accesos de inmediato, también los de sus tokens (ATLAS-ADR-0022).
- Equipo pequeño: configuración declarativa, pocas piezas nuevas que operar, sin exponer el servicio para probarlo.

## Opciones consideradas

1. **OpenIddict 7.7.1 (servidor y validación, EF Core sobre `IdentidadDbContext`) con clientes declarados en configuración y sembrados al arrancar, scopes declarados por cada módulo, llaves en certificados montados desde SealedSecret y conformidad con pruebas propias de integración.**
2. OpenIddict con clientes administrados desde una pantalla de Atlas (CRUD en la base).
3. Llaves generadas y guardadas por la app (Data Protection o en la base) en vez de certificados montados.
4. Correr la suite de conformidad de la OpenID Foundation en CI.

## Decisión

Opción elegida: **1**.

### 1. Versión y licencia

`OpenIddict.AspNetCore` y `OpenIddict.EntityFrameworkCore` **7.7.1** (17/09/2026, Apache-2.0, `net10.0`). Trae el arreglo de GHSA-925x-4h4v-2792 (audiencia de las aserciones de cliente, corregido en 7.7). La 7.7.2 (10/10/2026) no cumplía las 2 semanas el 22/10/2026 y solo cambia la integración con Quartz y un proveedor del cliente web; se sube cuando la cumpla.

### 2. Endpoints

| Ruta | Quién responde | Uso |
|---|---|---|
| `/.well-known/openid-configuration`, `/.well-known/jwks` | OpenIddict | Descubrimiento (RFC 8414 / OIDC Discovery) y JWKS: solo llaves públicas de firma |
| `/connect/authorize` (GET y POST) | OpenIddict + Atlas (passthrough) | Código con **PKCE obligatorio, solo S256** (`plain` se retira del descubrimiento). El usuario que autoriza es el de la **sesión del BFF**: sin sesión (o con `prompt=login` o `max_age` vencido) se va a «Iniciar sesión con Microsoft» y se vuelve; con `prompt=none` responde `login_required` |
| `/connect/continuar` | Atlas | Rebote tras el ingreso: página sin scripts que navega a `/connect/authorize` con la misma consulta. Hace falta porque la cookie `SameSite=Strict` no viaja en la redirección que viene de Microsoft (la cadena empezó en otro sitio); la navegación que inicia una página de Atlas sí la lleva. Destino fijo: no es una redirección abierta |
| `/connect/token` | OpenIddict + Atlas (passthrough) | `authorization_code`, `refresh_token` y `client_credentials` |
| `/connect/revoke`, `/connect/introspect` | OpenIddict | Revocación (todos los clientes) e introspección (solo confidenciales) |
| `/connect/userinfo` | OpenIddict + Atlas | Mínimo: `sub`; `name` con `profile`; `email` con `email` |
| `GET /api/v1/identidad/token/yo` | Atlas (Minimal API) | Endpoint protegido por scope: a quién representa el token, con qué scopes y audiencias. Es el que prueba el CA |

Sin flujo implícito, híbrido ni de contraseña. Sin pantalla de consentimiento: los clientes son propios de FLIT (`consent_type = implicit`); uno de terceros, si llega, la necesitará (ATLAS-ADR-0004 §7).

### 3. Tokens

- Acceso: **15 min** por defecto (`Identidad:Tokens:DuracionAcceso`; el arranque falla si supera 15 min), JWT firmado (RS256) y **cifrado** (JWE): su audiencia es atlas-api. Cuando un recurso distinto de atlas-api necesite validar tokens localmente, se decide si se le emiten sin cifrar o si usa introspección.
- Claims (ATLAS-ADR-0004 §5): `sub` (id del usuario de Atlas; para un servicio, su `client_id`), `persona_id`, `tipo_principal` (`empleado`, `externo` o `servicio`), `organizacion_id`, `name`, `aud` y `scope`. **Nunca roles ni alcances**: el recurso los evalúa en el servidor en cada petición (ATLAS-ADR-0023).
- Refresh: solo con `offline_access`; **rotación** en cada canje (7 días de vida). Reutilizar uno ya canjeado fuera del margen (10 s por defecto, para peticiones concurrentes del mismo cliente) hace que OpenIddict **revoque todos los tokens de esa autorización**, incluido el refresh vigente y los tokens de acceso; queda `identidad.refresh_reutilizado`. Tope de la concesión: 30 días desde la autorización (`DuracionMaximaConcesion`), aunque se rote a tiempo.
- En cada canje del refresh Atlas comprueba que el usuario exista, no esté bloqueado, su persona esté activa y su **sello de seguridad** sea el mismo con que se autorizó; si no, revoca la autorización y responde `invalid_grant`. `IInvalidadorSesiones` (suspensión, cambio de roles, cierre administrativo) también revoca las autorizaciones y los tokens de OpenIddict de la persona.
- Validación (`OpenIddict.Validation` con el servidor local): audiencia `atlas-api` y, en cada petición, que el token y su autorización sigan vigentes en la base (`EnableTokenEntryValidation` y `EnableAuthorizationEntryValidation`): revocar corta el acceso de inmediato, como con las sesiones.

### 4. Clientes por configuración

Sección `Identidad:Tokens:Clientes:<client_id>` con `Nombre`, `Tipo` (`Publico` o `Confidencial`), `Flujos`, `RedirectUris`, `Scopes`, `Audiencias` (por defecto `atlas-api`) y `Secreto` (solo confidenciales, ≥ 32 caracteres). Al arrancar, `SincronizadorClientes` deja `identidad_oidc_aplicacion` igual a la configuración: crea, actualiza y **borra** lo que ya no está (con sus autorizaciones y tokens). Es idempotente y usa un candado de PostgreSQL para que varias réplicas no choquen; si la base no responde, reintenta en segundo plano. El secreto se guarda derivado por OpenIddict (PBKDF2), nunca en claro. El arranque falla con todos los errores a la vez (cliente confidencial sin secreto nombrando su variable, scope que nadie declara, duración de acceso mayor a 15 min…).

- `atlas-cli` y `atlas-mcp` van en el `appsettings.json` de atlas-api: públicos, código con PKCE y redirect de loopback **sin puerto** (`http://127.0.0.1/callback`): OpenIddict acepta cualquier puerto en loopback para aplicaciones nativas (RFC 8252 §7.3).
- Los servicios internos (confidenciales, `client_credentials`) se declaran por ambiente: la definición en el ConfigMap de flit-gitops y el secreto en un SealedSecret (`Identidad__Tokens__Clientes__<id>__Secreto`). En local, en los user secrets del AppHost.

Se descarta la opción 2 (pantalla de clientes): hoy los clientes son pocos y propios, y la configuración versionada en flit-gitops deja revisión y auditoría en el PR.

### 5. Scopes por módulo

Cada módulo declara sus scopes con `servicios.AgregarScopes(IProveedorScopes)` (`ScopeAtlas(nombre, modulo, descripcion)`), sin depender de Identidad más que de ese registro. Formato `atlas.<modulo>[.<accion>]`; el registro rechaza nombres inválidos y duplicados entre módulos. Base del §6.3: `atlas.mcp` (asistente), `atlas.publicar` (contratos) y `atlas.widget` (asistente). El arranque falla si el módulo de un scope no tiene `ManifiestoPermisos` (ATLAS-ADR-0023): así el registro de scopes queda atado al de permisos **sin duplicarlo**. La diferencia: el **permiso** dice qué puede hacer la persona; el **scope**, qué parte de la API puede usar el cliente con su token. Un endpoint con Bearer que actúe por una persona exige ambos.

Los scopes se siembran en `identidad_oidc_scope` y se publican en el descubrimiento.

### 6. Bearer y cookie

- `.RequiereScope(...)` autentica **solo** con el esquema de validación de OpenIddict y exige al menos uno de los scopes: sin token, 401; con token sin el scope, 403; con la cookie, 401.
- El esquema por defecto sigue siendo la cookie: un Bearer no abre la sesión del BFF ni vale en los endpoints de sesión.
- La antifalsificación de `/api` (ATLAS-ADR-0008) no se exige a las peticiones con `Authorization: Bearer`: esa cabecera prueba lo mismo que la propia (un sitio ajeno no puede ponerla sin CORS) y el token no es una credencial que el navegador mande solo.
- La prueba de autorización por rol (ATLAS-ADR-0023) reconoce los endpoints con scope: con la cookie de cualquier rol deben responder 401.

### 7. Llaves y rotación

- **qa y pdn**: certificados X.509 `.pfx` de **firma** y de **cifrado**, montados como archivos desde un SealedSecret; rutas en `Identidad__Tokens__Certificados__Firma` y `Identidad__Tokens__Certificados__Cifrado` (una o más, separadas por «;») y clave en `Identidad__Tokens__Certificados__Clave`. Son **obligatorios**: sin ellos el arranque falla nombrando las variables. La llave privada se carga solo en memoria (`EphemeralKeySet`). Se generan con `scripts/generar-certificados-tokens.sh` (openssl 3, RSA 3072, `keyUsage` de firma o de cifrado).
- **local y dev**: llaves efímeras si no hay certificados (los tokens dejan de valer al reiniciar); se pueden usar certificados igual que en qa.
- **Rotación de la firma** (cada 6 meses, sin corte): (1) generar el certificado nuevo con inicio en el futuro (`inicio` del script, ≥ 24 h) y agregarlo a la lista: OpenIddict publica **ambos** en el JWKS pero no firma con uno que aún no vale; (2) cuando empieza su vigencia, OpenIddict firma con el vigente que **vence más tarde** (el nuevo); (3) retirar el viejo cuando haya vencido el último token firmado con él: 15 min para acceso e identidad, pero **7 días** (la vida de un refresh) para los refresh, así que el viejo se queda al menos una semana después del cambio. Sin openssl 3.4 (sin `-not_before`), agregar el nuevo cambia la firma de inmediato: sirve igual para atlas-api, que valida con su propia lista.
- **Rotación del cifrado**: agregar el nuevo **primero** en la lista (cifra el primero y descifra con todos) y retirar el viejo pasada la vida máxima de un refresh (7 días) más el margen de despliegue.

### 8. Eventos y límite de tasa

Tipos nuevos en `identidad_evento_seguridad` y `plataforma_auditoria` (ATLAS-ADR-0022): `autorizacion_concedida`, `autorizacion_rechazada`, `token_emitido`, `token_rechazado`, `cliente_rechazado` y `refresh_reutilizado`, con cliente, flujo, scopes y código de error de OpenIddict; **nunca** tokens, códigos ni secretos. Límite propio en `/connect/token` y `/connect/authorize` (`Identidad__LimiteTasa__TokensPorIp`, 30 por minuto) además del general por IP de las rutas de identidad, que ahora incluye `/connect/*` y `/.well-known/*`.

### 9. Contrato OpenAPI

Los endpoints de `/connect/*` y `/.well-known/*` **no** van en `atlas.v1.yaml`: son el protocolo OAuth 2.0/OIDC, su contrato es el documento de descubrimiento (que genera OpenIddict) y documentarlos como operaciones REST duplicaría una norma externa y fallaría ante cualquier cambio de OpenIddict. El contrato sí declara el esquema de seguridad `tokenAtlas` (OAuth 2.0 con los flujos `authorizationCode` y `clientCredentials`, URL relativas y los scopes del registro) y lo exige en las operaciones con `RequiereScope` (hoy `ObtenerPortadorDelToken`).

### 10. Conformidad

La suite de la OpenID Foundation (perfiles *Basic OP* y *Config OP*) necesita alcanzar el servidor desde su propio despliegue (Java + MongoDB) y registrar clientes con flujos que Atlas no ofrece (implícito, híbrido, `form_post`), así que no se puede correr en CI sin exponer el servicio. Se decide una **suite propia** en `TokensTests` (Testcontainers, sin red) que recorre lo que exigen OIDC Discovery 1.0, OAuth 2.0 Security BCP y RFC 8252 para lo que Atlas ofrece: campos obligatorios del descubrimiento, solo flujos y métodos PKCE permitidos, JWKS sin partes privadas, firma del `id_token` validada contra el JWKS con emisor, audiencia y `nonce`, PKCE obligatorio y verificado, código de un solo uso, redirect URI exacta (y loopback con cualquier puerto), `prompt=none`, rotación y reutilización de refresh, revocación, audiencia por cliente, scope por endpoint, separación cookie/Bearer, rotación de certificados y arranque estricto en qa y pdn. Correr la suite oficial contra qa, desde la red de FLIT, queda para la revisión de seguridad de F4 **[por verificar]**.

## Consecuencias

### Positivas

- Clientes no navegador con estándares (OAuth 2.0 + PKCE, OIDC) y sin criptografía propia.
- Revocar es inmediato y consistente con las sesiones: suspensión, cambio de roles, reutilización de un refresh o cliente retirado.
- Los clientes y sus permisos quedan versionados en configuración; los secretos, en SealedSecret.
- Los módulos agregan scopes sin tocar Identidad y sin duplicar el manifiesto de permisos.

### Negativas

- Cada petición con Bearer consulta la base (token y autorización). Aceptable con los volúmenes de Atlas; si pesa, se cachea con vida corta.
- Borrar un cliente de la configuración revoca sus tokens al siguiente despliegue: un error de configuración corta a ese cliente (se corrige con otro despliegue).
- Las llaves efímeras de local invalidan los tokens de la CLI al reiniciar la API.
- El rebote `/connect/continuar` depende de que el navegador trate como mismo sitio la navegación por `meta refresh` **[por verificar en qa con Chrome, Edge, Firefox y Safari]**.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Clientes administrados desde Atlas (2) | Más superficie (pantallas, permisos, auditoría propia) para pocos clientes propios; la configuración en flit-gitops ya deja revisión |
| Llaves generadas por la app (3) | ATLAS-ADR-0004 §7 pide certificados en SealedSecret y nunca generados en qa ni pdn; con varias réplicas, llaves propias exigirían compartirlas y rotarlas a mano |
| Suite oficial en CI (4) | Requiere exponer el servicio o desplegar la suite con su base; ofrece flujos que Atlas no habilita. Se deja para F4 contra qa |
| Duende IdentityServer | Licencia comercial (ATLAS-ADR-0004) |

## Más información

- OpenIddict 7.7.1: <https://github.com/openiddict/openiddict-core/releases/tag/7.7.1>; GHSA-925x-4h4v-2792.
- RFC 6749 §10.5 (reutilización de códigos), RFC 7636 (PKCE), RFC 8252 §7.3 (loopback), RFC 8414, RFC 8725 (JWT BCP), OAuth 2.0 Security BCP (rotación de refresh).
- Pendientes **[por verificar]**: el rebote con cookie `SameSite=Strict` en navegadores reales en qa; correr la suite de la OpenID Foundation contra qa en F4; definir qué servicios internos reciben cliente (y sus scopes) cuando llegue YARP interno; consentimiento para clientes de terceros si llegan; si `atlas-mcp` necesita registro dinámico de clientes (F7).
- Relacionados: ATLAS-ADR-0004 (identidad), ATLAS-ADR-0008 (BFF), ATLAS-ADR-0021 (contratos), ATLAS-ADR-0022 (sesiones y seguridad), ATLAS-ADR-0023 (permisos).
