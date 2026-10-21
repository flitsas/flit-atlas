# aspire · Orquestación local

AppHost de **.NET Aspire 13.6** que levanta en desarrollo los cinco servicios de Atlas y sus dependencias: Postgres 16 con pgvector, Redis, RabbitMQ y un S3 local.

## Requisitos

| Herramienta | Versión |
|---|---|
| .NET SDK | 10.0.400 (fijado en `global.json`) |
| Docker | Desktop o Engine en ejecución |
| Go | 1.26 |
| Python y uv | 3.13 y uv 0.11 |
| Node y pnpm | 22 y pnpm 11 |

## Arranque

```bash
dotnet run --project aspire/Flit.Atlas.AppHost
```

El panel de Aspire queda en `http://localhost:15031` (el enlace con el token sale en la consola).

| Recurso | Puerto local | Salud |
|---|---|---|
| `migrador` | — | Aplica las migraciones y termina; `api` arranca solo si terminó bien |
| `api` (atlas-api) | 4031 | `/health/ready`: Postgres, Redis y RabbitMQ |
| `web` (PWA, Vite) | 4032 | `/`; hace de proxy de `/api` hacia `api` (mismo origen) |
| `render` (atlas-render) | 4033 | `/health/ready` |
| `ia` (atlas-ia) | 4034 | `/health/ready`: Postgres con la extensión `vector` |
| `docs` (atlas-docs) | 4035 | `/health/ready`: bucket `atlas` en el S3 local |
| `postgres`, `redis`, `rabbitmq`, `s3` | dinámicos | Los asigna Aspire, para no chocar con el Postgres de FLIT 2.0 |

Los puertos de los servicios son los de la propuesta F0.3 (`docs/preparacion/03-puertos-y-namespaces.md`).

## Notas

- Postgres usa la imagen `pgvector/pgvector` con Postgres 16, como las VPS. `postgres-init/01-atlas.sql` crea la base `atlas` y las extensiones `vector` y `unaccent`, que en dev, qa y pdn crea el administrador. El volumen `flit-atlas-postgres` guarda los datos entre corridas; bórralo con `docker volume rm flit-atlas-postgres` para volver a ejecutar el script.
- El S3 local es **RustFS** 1.0.1 (Apache-2.0, compatible con la API de MinIO), porque MinIO dejó de publicar imágenes públicas en Docker Hub y quay.io (revisado el 21/10/2026). La clave se genera la primera vez y queda en los user secrets del AppHost. En dev y qa la decisión sigue abierta: ver la pregunta N-18 de `docs/preparacion/05-preguntas-abiertas.md`.
- Solo en local, `atlas-docs` crea su bucket (`ATLAS_DOCS_S3_CREAR_BUCKET`). El servicio rechaza esa bandera en cualquier otro ambiente.
- Cada servicio falla al arrancar, nombrando todas las variables obligatorias que faltan, si se corre fuera del AppHost sin configurarlo.
