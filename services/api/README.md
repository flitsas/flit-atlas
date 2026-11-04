# services/api · atlas-api (.NET 10)

Única pieza expuesta a internet. Hace de **BFF** para la PWA (ATLAS-ADR-0008), es dueña de la identidad (ATLAS-ADR-0004), aplica permisos y enruta por YARP interno hacia Go y Python.

| Proyecto | Responsabilidad |
|---|---|
| `src/Flit.Atlas.Api/` | Host, BFF, autenticación, YARP interno, registro de módulos |
| `src/Flit.Atlas.Identidad/` | ASP.NET Core Identity, Entra ID, invitaciones, OpenIddict |
| `src/Flit.Atlas.Plataforma/` | Permisos, auditoría, notificaciones, búsqueda, organizaciones |
| `src/Flit.Atlas.Modulos.<X>/` | Un proyecto por módulo funcional (dominio, endpoints, EF Core) |
| `tests/` | xUnit + Testcontainers |

Proyectos ya creados:

| Proyecto | Contenido |
|---|---|
| `src/Flit.Atlas.ServiceDefaults/` | Telemetría, sondas `/health/live` y `/health/ready`, y `ConfiguracionRequerida` |
| `src/Flit.Atlas.Plataforma/` | `PlataformaDbContext` (tablas `plataforma_*` del §9), auditoría de solo inserción, outbox e inbox (ATLAS-ADR-0018) |
| `src/Flit.Atlas.Identidad/` | `IdentidadDbContext` (ASP.NET Core Identity v3, tablas `identidad_*`), ingreso con Entra ID, aprovisionamiento de empleados y sesiones en el servidor |
| `src/Flit.Atlas.Migrador/` | Aplica las migraciones (núcleo y módulos), crea las banderas de los módulos nuevos, aplica la matriz y termina; corre antes de `atlas-api` |
| `src/Flit.Atlas.Composicion/` | La lista única de módulos (`ModulosAtlas.Registrados`) que usan atlas-api y el migrador (ATLAS-ADR-0026) |
| `src/Flit.Atlas.Modulos.Hola/` | Módulo de ejemplo «hola», apagado por bandera: la plantilla de [`docs/como-crear-un-modulo.md`](../../docs/como-crear-un-modulo.md) |
| `tests/Flit.Atlas.Modulos.Hola.Tests/` | Contrato, migración, RLS y aislamiento del módulo de ejemplo (Testcontainers, usuario `flit_atlas`) |
| `tests/Flit.Atlas.Plataforma.Tests/` | Integración con Postgres y RabbitMQ reales (Testcontainers; requiere Docker) |
| `tests/Flit.Atlas.Api.Tests/` | Arranque y flujo de identidad contra Postgres y Redis reales (Testcontainers) |

En `src/Flit.Atlas.ServiceDefaults/` está: telemetría, sondas `/health/live` y `/health/ready`, y `ConfiguracionRequerida`, que falla el arranque nombrando todas las variables que faltan.

## Configuración obligatoria

| Variable | Uso |
|---|---|
| `ConnectionStrings__atlas` | PostgreSQL (esquema `atlas`) |
| `ConnectionStrings__redis` | Redis |
| `ConnectionStrings__rabbitmq` | RabbitMQ |
| `Atlas__Ambiente` | `local`, `dev`, `qa` o `pdn` |
| `Identidad__Entra__TenantId`, `Identidad__Entra__ClientId` | Registro de Entra ID del ambiente; obligatorios en qa y pdn |
| `Identidad__Entra__ClientCredentials__0__*` | Credencial de cliente (certificado en qa y pdn, F0.4 ítem 5.2) |
| `Identidad__ProteccionDatos__Certificado` | Certificado que cifra las llaves de Data Protection en Redis; obligatorio en qa y pdn |
| `Identidad__Simulado__Habilitado` | Proveedor simulado; solo se acepta en `local` y `dev` |
| `Identidad__Tokens__Certificados__Firma`, `Identidad__Tokens__Certificados__Cifrado`, `Identidad__Tokens__Certificados__Clave` | Certificados `.pfx` del servidor de tokens (rutas montadas desde un SealedSecret, varias separadas por «;») y su clave; obligatorios en qa y pdn (ATLAS-ADR-0024) |
| `Identidad__Tokens__Clientes__<id>__*` | Clientes de OpenIddict; el `Secreto` de los confidenciales llega desde un SealedSecret |
| `Atlas__Proxy__RedesConfiables` | Redes CIDR (separadas por coma) de los proxies cuyo `X-Forwarded-*` se acepta; en el clúster, la red de pods. Sin valor, solo loopback |
| `Atlas__Proxy__Limite` | Proxies delante de la API (por defecto 2: nginx del host y `atlas-web`) |
| `Plataforma__Push__ClavePublica`, `Plataforma__Push__ClavePrivada` | Llaves VAPID de Web Push en base64url (las genera `node scripts/generar-vapid.mjs`). Son secreto: SealedSecret en qa y pdn. Sin ellas, push queda deshabilitado en qa y pdn; en local y dev se generan unas efímeras |
| `Plataforma__Push__Asunto` | Contacto para los servicios de push (`mailto:` o `https://`); por defecto `mailto:atlas@ejemplo.com` |
| `Plataforma__Push__HostsPermitidos` | Servicios de push aceptados en las suscripciones, separados por coma (por defecto FCM, Mozilla, Apple y WNS) |

## Identidad de empleados (HU 1.3)

- «Iniciar sesión con Microsoft»: OIDC con código y PKCE (Microsoft.Identity.Web), tenant único. Se rechazan las cuentas personales, las de otros tenants y los invitados externos; el rechazo queda en la auditoría como `identidad.inicio_rechazado`.
- Primer ingreso (JIT): crea o reutiliza la persona por el `oid` de Entra, la enlaza a la organización interna y asigna los roles según los grupos de Entra (`plataforma_mapeo_grupo_entra`). Cargo, área y grupos se leen de Graph con permiso de aplicación (F0.4 ítem 5.4); si Graph falla, el ingreso sigue.
- Sesión: cookie `__Host-atlas` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`) que solo lleva el id cifrado; el ticket vive en `atlas.identidad_sesion` (8 h deslizantes, 7 días como tope). Las llaves de Data Protection se guardan en Redis.
- Las peticiones que cambian estado exigen la cabecera `X-Atlas-Csrf` y el mismo origen. Cerrar sesión revoca la sesión en el servidor y envía `Clear-Site-Data`.
- Endpoints: `GET /api/v1/identidad/proveedores`, `GET /api/v1/identidad/entrar/microsoft`, `POST /api/v1/identidad/salir`, `GET /api/v1/plataforma/yo` y, solo en local y dev, `POST /api/v1/identidad/simulado/entrar` (fuera del contrato OpenAPI, ATLAS-ADR-0021).

```bash
dotnet build Flit.Atlas.slnx
dotnet test --solution Flit.Atlas.slnx      # las pruebas de Plataforma necesitan Docker
```

## Sesiones y seguridad (HU 1.3b, ATLAS-ADR-0022)

- **Dispositivos conectados**: cada sesión guarda el navegador resumido, la última IP (`inet`, `@pii`, nunca en los logs), cuándo se creó y su último uso. `GET /api/v1/identidad/sesiones` las lista y marca la actual; `DELETE /api/v1/identidad/sesiones/{id}` cierra una (la actual equivale a salir); `POST /api/v1/identidad/sesiones/cerrar-otras` cierra las demás.
- **Sello de seguridad**: cada sesión guarda el `SecurityStamp` del usuario y se compara en cada petición; si cambió, la sesión se revoca. Para cambiar roles o vencer accesos (HU 1.4) se llama a `IInvalidadorSesiones.InvalidarSesionesAsync(personaId, MotivosInvalidacion.CambioDeRoles, actorPersonaId)` **después** de guardar el cambio.
- **Administración** (permiso `atlas.identidad.administrar` en la política `PoliticasIdentidad.Administrar`, HU 1.4): `POST /api/v1/identidad/admin/personas/{personaId}/sesiones/cerrar`, `…/suspender` y `…/reactivar`. Una persona suspendida no vuelve a entrar.
- **Reautenticación**: `.RequiereReautenticacion()` exige un ingreso de menos de 10 minutos; si no, 401 problem+json con `codigo = "reautenticacion_requerida"`. La PWA lleva a `GET /api/v1/identidad/entrar/microsoft?reautenticar=true&volver=<ruta>` (Entra pide las credenciales con `prompt=login`). Se aplica a cerrar otros dispositivos y a toda la administración.
- **Sesión nueva en cada ingreso**: volver a entrar desde un navegador con sesión crea otra y revoca la anterior (`reemplazada_por_ingreso`).
- **Bloqueo progresivo** (cuentas locales, HU 1.5): `VerificadorContrasena.VerificarAsync` (y `VerificarTotpAsync` y `CanjearCodigoRecuperacionAsync`, que suman al mismo conteo) usa el bloqueo de Identity: 10 fallos seguidos bloquean 15 min, y cada bloqueo en 24 h duplica la duración (hasta 24 h). Una cuenta bloqueada tampoco entra por Microsoft.
- **Límite de tasa** en `/api/v1/identidad/**`, `/api/auth/**` y `/api/v1/plataforma/yo`, por IP del cliente (tras los proxies confiables) y por usuario: 429 con `Retry-After`. Por réplica y en memoria. Variables: `Identidad__LimiteTasa__IngresoPorIp` (20), `Identidad__LimiteTasa__PorIp` (120), `Identidad__LimiteTasa__PorUsuario` (60), `Identidad__LimiteTasa__Ventana` (`00:01:00`).
- **Eventos de seguridad**: `atlas.identidad_evento_seguridad` (solo inserción) y, en la misma transacción, `atlas.plataforma_auditoria` como `identidad.<tipo>`: inicio, rechazo, fallo, bloqueo, cierre, cierre remoto, invalidación, suspensión, reactivación, reautenticación (pedida y hecha) y límite de tasa.
- Las sesiones abiertas antes de la migración `SesionesYSeguridad` no tienen sello: cada persona entra de nuevo una vez.

## Roles y permisos (HU 1.4, ATLAS-ADR-0023)

- **Fuente única en el código**: cada módulo declara un `ManifiestoPermisos` con sus permisos `atlas.<modulo>.<accion>` y los roles que reciben cada uno; los roles del §7 están en `RolesAtlas`. La lista base del §7.4 está en `ManifiestosNucleo` hasta que llega su módulo, que la muda a su proyecto (el catálogo ya lo hizo, ATLAS-ADR-0028). `atlas.superadmin` recibe todo; todo empleado activo tiene `atlas.lector`. El migrador aplica roles y matriz a `plataforma_rol`, `plataforma_permiso` y `plataforma_rol_permiso` (idempotente).
- **Matriz**: `GET /api/v1/plataforma/permisos/matriz` (requiere `atlas.roles.ver`) y [`docs/permisos/matriz-rol-permiso.md`](../../docs/permisos/matriz-rol-permiso.md), generado; `MatrizPermisosTests` falla si no coincide. Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests`.
- **Permisos efectivos**: se calculan en el servidor en cada petición con las asignaciones vigentes (hora actual), nunca en la cookie. `GET /api/v1/plataforma/yo` trae `roles` y `permisos` (listas ordenadas de códigos); la PWA filtra la navegación con `permisos`. Un rol vencido deja de valer en la petición siguiente; el `VigilanteVencimientos` (cada minuto, `Identidad__Vencimientos__Intervalo`) lo audita e invalida las sesiones.
- **Asignaciones** con organización, alcance (`global`, `organizacion`, `ambiente` y los que agreguen los manifiestos: `aplicativo`, `paquete`, `documento`) y vigencia; manuales, de grupos de Entra (se suman) o de invitación (HU 1.5). Un rol externo exige persona externa de la misma organización y vencimiento dentro del máximo configurable (`Identidad__Externos__VigenciaMaximaAcceso`, 90 días).
- **Administración** (`atlas.roles.ver` para leer; `atlas.roles.gestionar` y reautenticación para cambiar; todo auditado): `GET /api/v1/plataforma/roles`, `GET|POST /api/v1/plataforma/personas/{personaId}/asignaciones`, `DELETE /api/v1/plataforma/asignaciones/{id}` (invalida las sesiones de la persona; las de grupo responden 409), `GET /api/v1/plataforma/personas?q=&tipo=&organizacion=&limite=` (buscador por nombre o correo sin distinguir mayúsculas ni tildes con `translate`, o por id; mínimo 2 caracteres, hasta 50 resultados por nombre con `hayMas`, y los roles vigentes de cada una; el predicado de organización y RLS limitan a un externo a la suya), `GET|POST /api/v1/plataforma/mapeos-grupos-entra`, `DELETE /api/v1/plataforma/mapeos-grupos-entra/{id}`. La PWA los usa en «Asignación de roles» (`/administracion/roles`).
- **Aislamiento**: predicado obligatorio (filtro global de EF Core en toda entidad con `OrganizacionId`) y RLS forzada en `plataforma_organizacion`, `plataforma_persona`, `plataforma_asignacion_rol`, `plataforma_invitacion` y `plataforma_auditoria`, con las variables `atlas.alcance_total` y `atlas.organizaciones` que fija `InterceptorAlcanceDatos` en cada conexión. Un externo ve solo su organización. Sin las variables (p. ej. `psql` como `flit_atlas`) no se ve ninguna fila: fija antes `SELECT set_config('atlas.alcance_total', 'on', false);`.

Un módulo nuevo declara sus permisos así (el manifiesto lo registran atlas-api y el migrador desde la lista única de `Flit.Atlas.Composicion`, ATLAS-ADR-0026):

```csharp
public static class PermisosHola
{
    public const string Ver = "atlas.hola.ver";

    public static ManifiestoPermisos Manifiesto { get; } = new("hola", "Hola",
    [
        new(Ver, "Ver los saludos.", RolesAtlas.Lector, RolesAtlas.Integrador),
    ], tiposAlcance: ["saludo"]);
}

public ManifiestoPermisos Permisos => PermisosHola.Manifiesto;            // en su IModuloAtlas
saludos.MapGet("", ...).RequierePermiso(PermisosHola.Ver);              // en cada endpoint, bajo /api/v1/hola
```

Sus tablas con `organizacion_id` llaman a `AplicarPredicadoDeOrganizacion` en `OnModelCreating` y, en su migración, activan RLS con `FORCE` y la política `atlas.plataforma_organizacion_visible(organizacion_id)`. `AutorizacionTests` falla si un endpoint nuevo no declara permiso (salvo que se agregue a la lista de públicos o de solo sesión).

## Servidor de tokens (HU 1.3c, ATLAS-ADR-0024)

OpenIddict 7.7.1 embebido para los clientes que no son el navegador (`atlas-cli`, MCP, widget y servicios internos). La PWA no usa estos tokens.

- **Endpoints** (fuera de `/api` y del contrato; su contrato es el descubrimiento): `/.well-known/openid-configuration`, `/.well-known/jwks`, `/connect/authorize` (código con PKCE S256 obligatorio; el usuario es el de la sesión del BFF y, sin sesión, se va a Microsoft y se vuelve por `/connect/continuar`), `/connect/token` (`authorization_code`, `refresh_token`, `client_credentials`), `/connect/revoke`, `/connect/introspect` (confidenciales) y `/connect/userinfo`.
- **Tokens**: acceso de 15 min (JWT firmado y cifrado, audiencia por cliente, por defecto `atlas-api`), con `sub`, `persona_id`, `tipo_principal`, `organizacion_id` y `scope`, sin roles. Refresh con rotación (7 días; tope de la concesión, 30 días): reutilizar uno ya canjeado revoca toda la cadena. Cada canje revisa la persona y el sello de seguridad; `IInvalidadorSesiones` también revoca los tokens.
- **Clientes** en `Identidad:Tokens:Clientes:<client_id>` (`Tipo`, `Flujos`, `RedirectUris`, `Scopes`, `Audiencias`, `Secreto`). `atlas-cli` y `atlas-mcp` vienen en `appsettings.json` con redirect de loopback sin puerto. Al arrancar se sincronizan con `identidad_oidc_aplicacion`: lo que sale de la configuración se borra con sus tokens.
- **Scopes por módulo**: `servicios.AgregarScopes(IProveedorScopes)` con `ScopeAtlas(nombre, modulo, descripcion)`; el módulo debe tener manifiesto de permisos. Base: `atlas.mcp`, `atlas.publicar`, `atlas.widget`.
- **Endpoints con Bearer**: `.RequiereScope("atlas.publicar")` (al menos uno de los scopes; solo el esquema de OpenIddict, nunca la cookie). Sin token 401, sin el scope 403. Ejemplo: `GET /api/v1/identidad/token/yo`.
- **Llaves**: en qa y pdn, `.pfx` de firma y de cifrado montados (los genera `scripts/generar-certificados-tokens.sh`); en local, efímeras si no hay. Rotación en ATLAS-ADR-0024 §7.
- Otras variables (opcionales): `Identidad__Tokens__Emisor` (emisor fijo, `https://…/`), `Identidad__Tokens__DuracionAcceso` (≤ `00:15:00`), `Identidad__Tokens__DuracionRefresh` (`7.00:00:00`), `Identidad__Tokens__DuracionMaximaConcesion` (`30.00:00:00`), `Identidad__Tokens__MargenReutilizacionRefresh` (`00:00:10`), `Identidad__LimiteTasa__TokensPorIp` (30).
- Eventos: `identidad.autorizacion_concedida`, `autorizacion_rechazada`, `token_emitido`, `token_rechazado`, `cliente_rechazado` y `refresh_reutilizado` (nunca el token).

Un cliente de servicio (con su secreto en una variable, nunca en el código):

```bash
curl -s https://qa.atlas.flitsas.com/connect/token -d grant_type=client_credentials -d client_id=svc-ejemplo -d "client_secret=$SECRETO" -d scope=atlas.publicar
curl -s https://qa.atlas.flitsas.com/api/v1/identidad/token/yo -H "Authorization: Bearer $TOKEN"
```

## Organizaciones, invitaciones y cuentas de externos (HU 1.5, ATLAS-ADR-0025)

- **Organizaciones** (`atlas.organizaciones.gestionar`, reautenticación para cambiar): `GET|POST /api/v1/plataforma/organizaciones`, `PUT …/{id}`, `POST …/{id}/suspender`, `…/reactivar` y `DELETE …/{id}` (solo vacías). Suspender corta el acceso de toda la organización. `adminOrganizacionHabilitado` decide si `ext.admin_organizacion` concede permisos.
- **Invitaciones** (`atlas.invitaciones.gestionar`): `GET /api/v1/plataforma/invitaciones`, `GET …/opciones`, `POST …` (organización, rol externo, alcance, vencimiento, permisos extra), `POST …/{id}/reenviar`, `…/suspender`, `…/renovar` y `…/revocar`. Estado calculado al leer. Un administrador de organización solo actúa en la suya, sin invitar administradores y sin ampliar su alcance, su vencimiento ni sus permisos extra (403 `fuera_de_tu_alcance`).
- **Enlace**: `https://<host>/invitacion#<token>` (el fragmento no llega a ningún servidor); 256 bits, en la base solo su SHA-256, vence a las 72 h de cada envío, un solo uso.
- **Cuenta local** (públicos, bajo `/api/v1/identidad`): `invitaciones/consultar`, `invitaciones/passkey/opciones`, `invitaciones/totp/clave` e `invitaciones/aceptar` crean la cuenta en una transacción (persona, usuario, factor, diez códigos de recuperación, evidencia de términos y asignaciones con `OrigenAsignacion.Invitacion`) y abren la sesión. Passkey con `IPasskeyHandler<UsuarioAtlas>` de Identity (verificación de usuario y credencial descubrible obligatorias) o contraseña de 12+ revisada contra Have I Been Pwned por rango y una lista local, con TOTP obligatorio. `AlmacenUsuarios` cifra la clave TOTP (Data Protection) y guarda los códigos de recuperación con hash.
- **Ingreso de externos**: `entrar/local` (contraseña, con bloqueo progresivo; nunca abre sesión sola), `entrar/local/segundo-factor` (TOTP de un solo uso o código de recuperación), `entrar/passkey/opciones`, `entrar/passkey` y `entrar/terminos` (versiones nuevas de los documentos, 409 `terminos_pendientes`). Sin MFA, 403 `mfa_requerido`. Solo entra con un rol vigente: al vencer, el acceso se corta solo. Sesión de 2 h deslizantes y 24 h de tope.
- **Recuperación**: `recuperacion` (202 siempre; enlace `/recuperar#…` de un solo uso y 30 min), `recuperacion/consultar`, `…/passkey/opciones`, `…/totp/clave` y `…/completar` (quita los factores anteriores, registra el nuevo y cierra las sesiones). Restablecimiento forzado: `POST /api/v1/identidad/admin/personas/{personaId}/restablecer` (`atlas.identidad.administrar`).
- **Términos y aviso de privacidad**: `GET /api/v1/identidad/terminos`; textos en `src/Flit.Atlas.Identidad/Externos/Legal/` y versiones en `DocumentosLegales` (**borrador [por verificar]**). Evidencia en `plataforma_aceptacion_terminos` con el hash del texto.
- **Correo**: `IEnviadorCorreo` (Plataforma) con `EnviadorGraph` (`sendMail` desde el buzón compartido) o `EnviadorSmtp` (MailKit). Se envía después de guardar; si falla, `correoEnviado = false` y se reenvía. El token nunca va a los logs.
- **Límite de tasa** propio de estas rutas públicas, más estricto que el de empleados: `Identidad__LimiteTasa__ExternosPorIp` (10 por minuto).

| Variable | Uso |
|---|---|
| `Atlas__UrlPublica` | URL pública con la que se arman los enlaces de los correos; obligatoria en qa y pdn |
| `Plataforma__Correo__Proveedor` | `graph` o `smtp`; obligatorio en qa y pdn (en local y dev, sin él no se envía nada) |
| `Plataforma__Correo__Remitente`, `Plataforma__Correo__NombreRemitente` | Buzón remitente (el buzón compartido) y nombre visible (por defecto «Flit Atlas») |
| `Plataforma__Correo__Smtp__Host`, `__Puerto`, `__Seguridad`, `__Usuario`, `__Clave` | Con SMTP: servidor, puerto (587), `starttls`, `ssl` o `ninguna`, y credenciales (SealedSecret) |
| `Identidad__Externos__VigenciaMaximaAcceso` | Vencimiento máximo del acceso de un externo (`90.00:00:00`) |
| `Identidad__Externos__VigenciaEnlace`, `__VigenciaRecuperacion`, `__VigenciaPaso` | 72 h, 30 min y 5 min |
| `Identidad__Externos__ConsultarContrasenasFiltradas` | `false` para no consultar HIBP (queda la lista local) |
| `Identidad__Sesion__DeslizanteExternos`, `__AbsolutaExternos` | 2 h y 24 h |
| `Identidad__Passkeys__Dominio` | Dominio de las passkeys si no es el host de la petición |
| `Identidad__Simulado__GrupoAdministracion` | Solo local y dev: grupo simulado que da `atlas.superadmin` (casilla «Con rol de administración» del ingreso simulado) |

## Cuenta de administrador de emergencia (ATLAS-ADR-0027)

- Una sola cuenta local por ambiente, de la organización interna, con `atlas.superadmin` sin vencimiento (`OrigenAsignacion.Emergencia`): para arrancar un ambiente (crear los primeros mapeos de grupos de Entra) y para entrar si Entra no responde. No tiene contraseña por defecto.
- **Comando** (dentro de la imagen de atlas-api): `dotnet Flit.Atlas.Api.dll emergencia activar`. Crea la cuenta si falta, le quita los factores, cierra sus sesiones e imprime en su salida (nunca en el log) un enlace `/emergencia#…` de un solo uso que vence en 1 h. Volver a correrlo invalida el enlace anterior. En local: `dotnet run --project services/api/src/Flit.Atlas.Api -- emergencia activar` con las mismas variables que usa el AppHost.
- **Activación** (públicos): `emergencia/consultar`, `emergencia/passkey/opciones`, `emergencia/totp/clave` y `emergencia/activar`, como la recuperación de una cuenta externa. **Ingreso** por el de cuentas locales, con MFA. `/yo` devuelve `emergencia = true` y `proveedor = emergencia`.
- No se retira su rol ni se suspende desde Atlas (409 `cuenta_de_emergencia`). Cada creación, enlace, activación e ingreso es un evento de alta severidad (`identidad.emergencia.*`).

| Variable | Uso |
|---|---|
| `Identidad__Emergencia__Correo` | Correo ficticio de la cuenta, solo al crearla (`admin.emergencia@atlas.local`) |
| `Identidad__Emergencia__Nombre` | Nombre visible («Administrador de emergencia») |
| `Identidad__Emergencia__VigenciaEnlace` | Vigencia del enlace de activación (`01:00:00`) |

## Sincronización de personas (HU 2.2, ATLAS-ADR-0030)

- Trabajo diario en atlas-api (solo con Entra configurado) que lee de Graph, con el token de aplicación de siempre (`User.Read.All` y `GroupMember.Read.All`), nombre, cargo, área, teléfono, foto y `accountEnabled` de los empleados. Una cuenta deshabilitada o borrada en Entra deja la persona `inactiva` (`inactiva_por_entra`) y le cierra sesiones y tokens; al rehabilitarse vuelve. Nunca toca externos, la cuenta de emergencia ni a una persona suspendida.
- Un solo nodo a la vez (candado consultivo de PostgreSQL); reintenta con esperas si Graph o la base no responden; freno si una corrida fuera a deshabilitar a más de la mitad. Un evento `identidad.personas_sincronizadas` por corrida con conteos (sin datos personales) y métricas `atlas.personas.sincronizacion.*`.
- Foto en `plataforma_persona_foto` (tope de tamaño), servida con ETag y caché privada en `GET /api/v1/plataforma/personas/{id}/foto`. Directorio: `GET /api/v1/plataforma/directorio[/{id}]` con `atlas.equipo.ver`; el teléfono solo con `atlas.equipo.ver_contacto`. «Sincronizar ahora»: `GET|POST /api/v1/plataforma/directorio/sincronizacion` con `atlas.identidad.administrar`.

| Variable | Por defecto | Uso |
|---|---|---|
| `Identidad__Personas__Origen` | `enlazadas` | `enlazadas` (solo quien ya entró; no crea personas), `grupo` o `tenant` (miembros, nunca invitados) |
| `Identidad__Personas__GrupoEmpleados` | — | Object id del grupo de Entra; obligatorio con `grupo` |
| `Identidad__Personas__Hora` | `05:00` | Hora del día, en la de Bogotá |
| `Identidad__Personas__Habilitada` | `true` | `false` apaga la corrida diaria (el botón sigue) |
| `Identidad__Personas__Reintentos`, `Identidad__Personas__EsperaReintento` | `4`, `00:02:00` | Reintentos con espera que se duplica |
| `Identidad__Personas__TamanoMaximoFoto` | `102400` | Bytes |
| `Identidad__Personas__FraccionMaximaDeshabilitadas`, `Identidad__Personas__MinimoFreno` | `0.5`, `3` | Freno de seguridad |

## Responsables, CODEOWNERS y «¿A quién llamo?» (HU 2.3 a 2.5, ATLAS-ADR-0031)

- Módulo `Flit.Atlas.Modulos.Equipo` (`/api/v1/equipo`, tablas `equipo_*`): reparto principal/secundario por aplicativo del catálogo (por id, leído con `ILectorAplicativos`), importado del documento de responsables con vista previa y huella; historial versionado (cada cambio, una versión con autor y nota; restaurar crea otra igual); traspaso con checklist; revisión trimestral con recordatorio en el centro de notificaciones; mapa de carga; CODEOWNERS sugerido y ficha en Markdown; «¿A quién llamo?» con el soporte de turno (`ISoporteDeTurno`, del módulo de soporte).
- Rol implícito: el responsable vigente recibe `atlas.responsable` con alcance `aplicativo:<id>` (`IProveedorRolesImplicitos`); el catálogo ya evalúa `atlas.catalogo.editar` sobre el aplicativo y sus ancestros.
- Permisos: `atlas.equipo.ver` (leer), `atlas.equipo.gestionar` (importar, cambiar, restaurar, traspasos, revisiones, usuarios de GitHub y comparar con GitHub), `atlas.equipo.ver_contacto` (teléfonos).

| Variable | Por defecto | Uso |
|---|---|---|
| `Plataforma__GitHub__Token` | — | Token de solo lectura (contenidos) para leer los CODEOWNERS reales. Secreto: SealedSecret |
| `Plataforma__GitHub__AppId`, `Plataforma__GitHub__InstalacionId`, `Plataforma__GitHub__LlavePrivada` | — | En lugar del token, la GitHub App (llave privada PEM en SealedSecret; permiso «Contents: read») |
| `Plataforma__GitHub__UrlApi` | `https://api.github.com/` | Solo para GitHub Enterprise |
| `Equipo__Revision__DiasAnticipacion` | `7` | Días antes de la revisión trimestral en que sale el recordatorio |
| `Equipo__Revision__Habilitada` | `true` | `false` apaga el trabajo del recordatorio |

Sin GitHub configurado, la pantalla de CODEOWNERS muestra el bloque sugerido y dice que no puede comparar.

## Soporte en Microsoft 365 (HU 2.10, ATLAS-ADR-0033)

- **Calendario compartido**: cada turno vigente de hoy en adelante es un evento del buzón compartido con la persona como asistente (Outlook y Teams). Cada cambio de un turno deja su fila de `soporte_evento_calendario` pendiente en la misma transacción; `TrabajoM365Soporte` (todas las réplicas, candado consultivo) la sincroniza por Graph con el token de aplicación (`IAccesoGraph`), con reintentos y `transactionId`. Se despierta con cada cambio de la réplica y revisa cada 15 s.
- **Teams**: `INotificadorTeams` (Plataforma) publica tarjetas adaptables en el webhook de un flujo de Workflows; el soporte encola los avisos (`ColaAvisosTeams`, tabla `soporte_aviso_teams`) al confirmar una generación y al aprobar un cambio. Los recordatorios del sábado (HU 2.11) usan la misma cola. No hay aviso personal por Teams: Workflows publica en un canal.
- **Feeds .ics**: `GET|POST /api/v1/soporte/feeds` y `DELETE /api/v1/soporte/feeds/{id}` con `atlas.soporte.ver`; `GET /api/v1/soporte/ics/{token}.ics` es público (el token, de 256 bits, se guarda como SHA-256). Estado: `GET /api/v1/soporte/microsoft-365`.

| Variable | Por defecto | Uso |
|---|---|---|
| `Soporte__M365__Buzon` | — | UPN del buzón compartido; sin él (o sin Entra), el calendario queda apagado |
| `Soporte__M365__CalendarioHabilitado` | `true` | `false` apaga el calendario |
| `Soporte__M365__NombreCalendario` | `Soporte TI sábados` | Nombre en la pantalla y en los feeds |
| `Soporte__M365__CanalTeams` | `soporte` | Canal de `Plataforma__Teams__Canales__*` donde salen los avisos |
| `Soporte__M365__Intervalo` | `00:00:15` | Revisión periódica de lo pendiente |
| `Soporte__M365__EsperaReintento`, `Soporte__M365__EsperaMaxima` | `00:00:30`, `00:30:00` | Espera que se duplica tras cada fallo |
| `Soporte__M365__DiasFeedAtras`, `Soporte__M365__DiasFeedAdelante` | `90`, `400` | Ventana de los feeds .ics |
| `Plataforma__Teams__Canales__<nombre>` | — | URL HTTPS del disparador del flujo de Workflows de cada canal. Secreto: SealedSecret |

Permiso de Graph: `Calendars.ReadWrite` **solo** con RBAC para aplicaciones de Exchange Online sobre el buzón (pasos en `deploy/README.md`), nunca consentido en Entra.

## Recordatorios de turno de soporte (HU 2.11, ATLAS-ADR-0029)

- `TrabajoRecordatorios` (módulo de soporte) mira cada 5 minutos si en Bogotá ya pasó la hora de los recordatorios (`horaRecordatorios` de `PUT /api/v1/soporte/configuracion`, 07:00 por defecto) y si falta la corrida del día; si la réplica arranca tarde, la recupera. Un candado consultivo de PostgreSQL deja correr un nodo a la vez.
- Manda `diasAnticipacionRecordatorio` días antes del sábado a la persona de turno y, el mismo sábado, a cada persona de turno por la bandeja, push y correo (`IEnviadorCorreo`, SMTP en qa) y un aviso por equipo en el canal de Teams del soporte (`ColaAvisosTeams` de la HU 2.10). Cada persona elige sus canales en las preferencias del centro de notificaciones (tipo `soporte.recordatorio_turno`).
- Idempotente: cada envío por canal queda en `soporte_recordatorio` (clave única) antes de salir; las corridas, en `soporte_recordatorio_corrida`. Lo fallido se reintenta en la corrida siguiente (hasta 3 veces).
- `GET /api/v1/soporte/recordatorios` (estado y últimas corridas) y `POST /api/v1/soporte/recordatorios/ejecucion` («Enviar ahora», 202), con `atlas.soporte.gestionar`.

| Variable | Por defecto | Uso |
|---|---|---|
| `Soporte__Recordatorios__Habilitados` | `true` | `false` apaga la corrida diaria («Enviar ahora» sigue) |
| `Soporte__Recordatorios__Intervalo` | `00:05:00` | Cada cuánto revisa si ya es la hora |
| `Atlas__UrlPublica` | — | URL del botón del correo (la misma de las invitaciones) |

## Contratos (HU 1.7)

- `contracts/openapi/atlas.v1.yaml` se genera desde los endpoints con `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1, `ContratoOpenApi.cs`) y `ContratoOpenApiTests` falla si no coincide con el código. Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests` (detalles en [`contracts/openapi/README.md`](../../contracts/openapi/README.md)).
- `GET /openapi/v1.yaml` solo existe en local y dev; en qa y pdn no se sirve.
- Los eventos se declaran como `TipoEvento` y se encolan con `EncolarEvento(TipoEvento, …)`; `ContratoAsyncApiTests` los compara con `contracts/asyncapi/atlas-events.v1.yaml` ([`contracts/asyncapi/README.md`](../../contracts/asyncapi/README.md)).

## Publicación de contratos desde la CI (HU 3.1 y 3.2, ATLAS-ADR-0035)

- `POST /api/v1/contratos/publicaciones` solo acepta el token OIDC de GitHub Actions (esquema `GitHubActions` de Plataforma, `.RequierePublicacionCi()`); la cookie de la PWA recibe 401. Lista blanca en el catálogo: `GET|POST /api/v1/catalogo/fuentes-publicacion`, `PUT|DELETE …/{id}` con `atlas.catalogo.gestionar`, auditada.
- Lectura con `atlas.contratos.ver_internos`: `GET /api/v1/contratos`, `…/{id}/versiones`, `…/{id}/versiones/{version}/spec` y `…/operaciones`.

| Variable | Uso |
|---|---|
| `Plataforma__Publicacion__Audiencia` | Audiencia del token; vacía = `Atlas__UrlPublica` sin la barra final |
| `Plataforma__Publicacion__Propietarios__0` | Dueños de repo aceptados (por defecto `flitsas`) |
| `Plataforma__Publicacion__Eventos__0` | Eventos que publican (por defecto `push` y `workflow_dispatch`) |
| `Plataforma__Publicacion__WorkflowsReutilizables__0` | `job_workflow_ref` aceptados, con `*` (por defecto `flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v*`) |
| `Plataforma__Publicacion__RamasEstables__0`, `Plataforma__Publicacion__PrefijosPrerelease__<rama>` | `release` publica estables; `develop` → `dev.`, `staging` → `rc.` |
| `Plataforma__Publicacion__MargenReloj`, `__VidaMaximaToken`, `__RefrescoLlaves`, `__EsperaMinimaLlaves` | 1 min, 1 h, 6 h y 30 s |

## Shell de la PWA: banderas, notificaciones y Web Push (HU 1.6)

Endpoints bajo `/api/v1/plataforma`, todos con sesión y solo sobre los datos de la persona (ATLAS-ADR-0020):

| Endpoint | Uso |
|---|---|
| `GET /banderas` | `modulo.<id>` → activo según `plataforma_modulo` y la organización de la persona |
| `GET /notificaciones?limite=` | Bandeja (más recientes primero, máximo 100) y total sin leer |
| `POST /notificaciones/{id}/leida`, `POST /notificaciones/leidas` | Marcar una o todas como leídas |
| `GET` y `PUT /notificaciones/preferencias` | Canales por tipo de notificación; `app` no se apaga, correo según el tipo y el proveedor; Teams nunca (no hay aviso personal por Teams) |
| `GET /push/clave` | Llave pública VAPID (`habilitado: false` si el ambiente no tiene llaves) |
| `POST /push/suscripciones`, `POST /push/suscripciones/baja` | Registrar o quitar el dispositivo (`plataforma_suscripcion_push`); solo endpoints https de servicios de push conocidos (contra SSRF) |
| `POST /push/prueba` | Notificación de prueba en la bandeja y por push a los dispositivos |

- El envío usa Lib.Net.Http.WebPush (VAPID y aes128gcm de la librería). Un 400, 401, 403, 404 o 410 del servicio de push borra la suscripción; cinco fallos temporales seguidos, también.
- `ServicioNotificaciones.NotificarAsync` es la entrada para los módulos: guarda en la bandeja y envía push si la preferencia lo permite.
- **Errores de la PWA**: `POST /api/v1/plataforma/errores-cliente` exige la sesión del BFF (empleado, externo o cuenta de emergencia; un token Bearer no sirve) y responde 401 sin ella: los errores antes de entrar (invitación, emergencia) no se reportan. Lleva la cabecera antifalsificación, cuerpo de máximo 8 KB y límites propios por persona (`Identidad__LimiteTasa__ErroresClientePorPersona`, 10 por minuto) y por IP (`Identidad__LimiteTasa__ErroresClientePorIp`, 60 por minuto). Escribe una advertencia en el log (categoría `Flit.Atlas.ErroresCliente`, evento 7001) con la referencia que la persona ve en «Algo salió mal» y el id de la persona (nunca su correo); no guarda nada en la base. El servidor recorta los campos, deja la ruta sin consulta ni fragmento y tacha correos, números de seis o más dígitos y cadenas con forma de token.

## Persistencia

- Esquema `atlas`, `snake_case` (EFCore.NamingConventions), claves uuid v7 generadas en la app y fechas en `timestamptz`. Las columnas con datos personales llevan el comentario `@pii`.
- Cada `DbContext` tiene su historial: `atlas.__ef_migraciones_plataforma` y `atlas.__ef_migraciones_identidad` (ATLAS-ADR-0016).
- `atlas.plataforma_auditoria` es de solo inserción: la migración inicial revoca `UPDATE`, `DELETE` y `TRUNCATE`, y un trigger los rechaza incluso a un superusuario. Ver la pregunta N-19.
- En dev, qa y pdn, el administrador crea el usuario `flit_atlas` y el esquema con `flit/deploy/postgres/servicio-con-esquema-propio.sql -v servicio=atlas`; el migrador crea las tablas con ese usuario.

Nueva migración:

```bash
dotnet tool restore
dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Plataforma -o Datos/Migraciones
dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Identidad -o Datos/Migraciones
```

Una prueba falla si el modelo cambia sin su migración.

## Módulos (HU 1.9, ATLAS-ADR-0026)

Cada `Flit.Atlas.Modulos.<X>` implementa `IModuloAtlas` (`Flit.Atlas.Plataforma.Modulos`) y se registra con una línea en `Flit.Atlas.Composicion/ModulosAtlas.cs`. atlas-api lo monta en `/api/v1/<id>` con sesión y su bandera `modulo.<id>` (404 si está apagada para la organización de la persona); el migrador migra su contexto (historial `__ef_migraciones_<id>`), crea su bandera con `ActivoPorDefecto` sin tocar las existentes y aplica sus permisos. `ArquitecturaModulosTests` verifica que un módulo solo dependa de `Flit.Atlas.Plataforma` y que nadie más lo referencie. Paso a paso: [`docs/como-crear-un-modulo.md`](../../docs/como-crear-un-modulo.md).

```bash
dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.<X> -o Datos/Migraciones
```
