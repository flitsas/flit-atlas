# services/api · atlas-api (.NET 10)

Única pieza expuesta a internet. Hace de **BFF** para la PWA (ATLAS-ADR-0008), es dueña de la identidad (ATLAS-ADR-0004), aplica permisos y enruta por YARP interno hacia Go y Python.

| Proyecto | Responsabilidad |
|---|---|
| `src/Flit.Atlas.Api/` | Host, BFF, autenticación, YARP interno, registro de módulos |
| `src/Flit.Atlas.Identidad/` | ASP.NET Core Identity, Entra ID, invitaciones, OpenIddict (se crea en F1) |
| `src/Flit.Atlas.Plataforma/` | Permisos, auditoría, notificaciones, búsqueda, organizaciones |
| `src/Flit.Atlas.Modulos.<X>/` | Un proyecto por módulo funcional (dominio, endpoints, EF Core) |
| `tests/` | xUnit + Testcontainers |

También está `src/Flit.Atlas.ServiceDefaults/`: telemetría, sondas `/health/live` y `/health/ready`, y `ConfiguracionRequerida`, que falla el arranque nombrando todas las variables que faltan.

## Configuración obligatoria

| Variable | Uso |
|---|---|
| `ConnectionStrings__atlas` | PostgreSQL (esquema `atlas`) |
| `ConnectionStrings__redis` | Redis |
| `ConnectionStrings__rabbitmq` | RabbitMQ |

```bash
dotnet build Flit.Atlas.slnx
dotnet test --solution Flit.Atlas.slnx
```
