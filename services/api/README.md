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
- Endpoints: `GET /api/v1/identidad/proveedores`, `GET /api/v1/identidad/entrar/microsoft`, `POST /api/v1/identidad/salir`, `GET /api/v1/plataforma/yo` y, solo en local y dev, `POST /api/v1/identidad/simulado/entrar`.

```bash
dotnet build Flit.Atlas.slnx
dotnet test --solution Flit.Atlas.slnx      # las pruebas de Plataforma necesitan Docker
```

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
