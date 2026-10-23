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
| `src/Flit.Atlas.Migrador/` | Aplica las migraciones y termina; corre antes de `atlas-api` |
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

## Contratos (HU 1.7)

- `contracts/openapi/atlas.v1.yaml` se genera desde los endpoints con `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1, `ContratoOpenApi.cs`) y `ContratoOpenApiTests` falla si no coincide con el código. Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests` (detalles en [`contracts/openapi/README.md`](../../contracts/openapi/README.md)).
- `GET /openapi/v1.yaml` solo existe en local y dev; en qa y pdn no se sirve.
- Los eventos se declaran como `TipoEvento` y se encolan con `EncolarEvento(TipoEvento, …)`; `ContratoAsyncApiTests` los compara con `contracts/asyncapi/atlas-events.v1.yaml` ([`contracts/asyncapi/README.md`](../../contracts/asyncapi/README.md)).

## Shell de la PWA: banderas, notificaciones y Web Push (HU 1.6)

Endpoints bajo `/api/v1/plataforma`, todos con sesión y solo sobre los datos de la persona (ATLAS-ADR-0020):

| Endpoint | Uso |
|---|---|
| `GET /banderas` | `modulo.<id>` → activo según `plataforma_modulo` y la organización de la persona |
| `GET /notificaciones?limite=` | Bandeja (más recientes primero, máximo 100) y total sin leer |
| `POST /notificaciones/{id}/leida`, `POST /notificaciones/leidas` | Marcar una o todas como leídas |
| `GET` y `PUT /notificaciones/preferencias` | Canales por tipo de notificación; `app` no se apaga, correo y Teams aún no están disponibles |
| `GET /push/clave` | Llave pública VAPID (`habilitado: false` si el ambiente no tiene llaves) |
| `POST /push/suscripciones`, `POST /push/suscripciones/baja` | Registrar o quitar el dispositivo (`plataforma_suscripcion_push`); solo endpoints https de servicios de push conocidos (contra SSRF) |
| `POST /push/prueba` | Notificación de prueba en la bandeja y por push a los dispositivos |

- El envío usa Lib.Net.Http.WebPush (VAPID y aes128gcm de la librería). Un 400, 401, 403, 404 o 410 del servicio de push borra la suscripción; cinco fallos temporales seguidos, también.
- `ServicioNotificaciones.NotificarAsync` es la entrada para los módulos: guarda en la bandeja y envía push si la preferencia lo permite.

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
