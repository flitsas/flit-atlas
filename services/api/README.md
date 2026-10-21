# services/api · atlas-api (.NET 10)

Única pieza expuesta a internet. Hace de **BFF** para la PWA (ATLAS-ADR-0008), es dueña de la identidad (ATLAS-ADR-0004), aplica permisos y enruta por YARP interno hacia Go y Python.

| Proyecto | Responsabilidad |
|---|---|
| `src/Flit.Atlas.Api/` | Host, BFF, autenticación, YARP interno, registro de módulos |
| `src/Flit.Atlas.Identidad/` | ASP.NET Core Identity, Entra ID, invitaciones, OpenIddict (se crea en F1) |
| `src/Flit.Atlas.Plataforma/` | Permisos, auditoría, notificaciones, búsqueda, organizaciones |
| `src/Flit.Atlas.Modulos.<X>/` | Un proyecto por módulo funcional (dominio, endpoints, EF Core) |
| `tests/` | xUnit + Testcontainers |

Proyectos ya creados:

| Proyecto | Contenido |
|---|---|
| `src/Flit.Atlas.ServiceDefaults/` | Telemetría, sondas `/health/live` y `/health/ready`, y `ConfiguracionRequerida` |
| `src/Flit.Atlas.Plataforma/` | `PlataformaDbContext` (tablas `plataforma_*` del §9), auditoría de solo inserción, outbox e inbox (ATLAS-ADR-0018) |
| `src/Flit.Atlas.Migrador/` | Aplica las migraciones y termina; corre antes de `atlas-api` |
| `tests/Flit.Atlas.Plataforma.Tests/` | Integración con Postgres y RabbitMQ reales (Testcontainers; requiere Docker) |

En `src/Flit.Atlas.ServiceDefaults/` está: telemetría, sondas `/health/live` y `/health/ready`, y `ConfiguracionRequerida`, que falla el arranque nombrando todas las variables que faltan.

## Configuración obligatoria

| Variable | Uso |
|---|---|
| `ConnectionStrings__atlas` | PostgreSQL (esquema `atlas`) |
| `ConnectionStrings__redis` | Redis |
| `ConnectionStrings__rabbitmq` | RabbitMQ |

```bash
dotnet build Flit.Atlas.slnx
dotnet test --solution Flit.Atlas.slnx      # las pruebas de Plataforma necesitan Docker
```

## Persistencia

- Esquema `atlas`, `snake_case` (EFCore.NamingConventions), claves uuid v7 generadas en la app y fechas en `timestamptz`. Las columnas con datos personales llevan el comentario `@pii`.
- Cada `DbContext` tiene su historial: `atlas.__ef_migraciones_plataforma` (ATLAS-ADR-0016).
- `atlas.plataforma_auditoria` es de solo inserción: la migración inicial revoca `UPDATE`, `DELETE` y `TRUNCATE`, y un trigger los rechaza incluso a un superusuario. Ver la pregunta N-19.
- En dev, qa y pdn, el administrador crea el usuario `flit_atlas` y el esquema con `flit/deploy/postgres/servicio-con-esquema-propio.sql -v servicio=atlas`; el migrador crea las tablas con ese usuario.

Nueva migración:

```bash
dotnet tool restore
dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Plataforma -o Datos/Migraciones
```

Una prueba falla si el modelo cambia sin su migración.
