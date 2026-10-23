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
- **Administración** (provisional: rol `atlas.superadmin` en la política `PoliticasIdentidad.Administrar` hasta la HU 1.4): `POST /api/v1/identidad/admin/personas/{personaId}/sesiones/cerrar`, `…/suspender` y `…/reactivar`. Una persona suspendida no vuelve a entrar.
- **Reautenticación**: `.RequiereReautenticacion()` exige un ingreso de menos de 10 minutos; si no, 401 problem+json con `codigo = "reautenticacion_requerida"`. La PWA lleva a `GET /api/v1/identidad/entrar/microsoft?reautenticar=true&volver=<ruta>` (Entra pide las credenciales con `prompt=login`). Se aplica a cerrar otros dispositivos y a toda la administración.
- **Sesión nueva en cada ingreso**: volver a entrar desde un navegador con sesión crea otra y revoca la anterior (`reemplazada_por_ingreso`).
- **Bloqueo progresivo** (cuentas locales, HU 1.5): `VerificadorContrasena.VerificarAsync` usa el bloqueo de Identity: 10 fallos seguidos bloquean 15 min, y cada bloqueo en 24 h duplica la duración (hasta 24 h). Una cuenta bloqueada tampoco entra por Microsoft.
- **Límite de tasa** en `/api/v1/identidad/**`, `/api/auth/**` y `/api/v1/plataforma/yo`, por IP del cliente (tras los proxies confiables) y por usuario: 429 con `Retry-After`. Por réplica y en memoria. Variables: `Identidad__LimiteTasa__IngresoPorIp` (20), `Identidad__LimiteTasa__PorIp` (120), `Identidad__LimiteTasa__PorUsuario` (60), `Identidad__LimiteTasa__Ventana` (`00:01:00`).
- **Eventos de seguridad**: `atlas.identidad_evento_seguridad` (solo inserción) y, en la misma transacción, `atlas.plataforma_auditoria` como `identidad.<tipo>`: inicio, rechazo, fallo, bloqueo, cierre, cierre remoto, invalidación, suspensión, reactivación, reautenticación (pedida y hecha) y límite de tasa.
- Las sesiones abiertas antes de la migración `SesionesYSeguridad` no tienen sello: cada persona entra de nuevo una vez.

## Contratos (HU 1.7)

- `contracts/openapi/atlas.v1.yaml` se genera desde los endpoints con `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1, `ContratoOpenApi.cs`) y `ContratoOpenApiTests` falla si no coincide con el código. Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests` (detalles en [`contracts/openapi/README.md`](../../contracts/openapi/README.md)).
- `GET /openapi/v1.yaml` solo existe en local y dev; en qa y pdn no se sirve.
- Los eventos se declaran como `TipoEvento` y se encolan con `EncolarEvento(TipoEvento, …)`; `ContratoAsyncApiTests` los compara con `contracts/asyncapi/atlas-events.v1.yaml` ([`contracts/asyncapi/README.md`](../../contracts/asyncapi/README.md)).

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
