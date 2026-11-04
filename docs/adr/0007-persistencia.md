---
id: ATLAS-ADR-0007
titulo: Persistencia — esquema atlas con prefijos por módulo, Redis, S3 y RabbitMQ
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-06 y §9, plan 00 D-03]
---

# ATLAS-ADR-0007 · Persistencia — esquema atlas con prefijos por módulo, Redis, S3 y RabbitMQ

## Contexto y problema

La regla de plataforma de FLIT es «un servicio = un esquema» en el PostgreSQL compartido de cada ambiente (script `servicio-con-esquema-propio.sql`). Atlas tiene muchos módulos, y algunos guardan datos de organizaciones externas que exigen aislamiento fuerte.

## Factores de decisión

- Cumplir la regla de plataforma y su script de alta.
- Módulos desacoplados sin multiplicar esquemas.
- Aislamiento entre organizaciones como defensa en profundidad.
- Búsqueda full-text en español y vectores en la misma base (sin otro componente que operar).

## Opciones consideradas

1. **Un esquema `atlas`**, usuario `flit_atlas`, EF Core con `snake_case` y módulos separados por **prefijo de tabla**.
2. Un esquema por módulo.
3. Una base de datos propia para Atlas.

## Decisión

Opción elegida: **1**.

- Prefijos: `identidad_`, `plataforma_`, `catalogo_`, `equipo_`, `soporte_`, `contrato_`, `doc_`, `arq_`, `dgm_`, `asistente_`, `gia_` (prompt maestro §9). Cada módulo tiene sus propias migraciones y su prefijo (ATLAS-ADR-0016).
- Convenciones: claves `uuid` v7; `creado_en` y `actualizado_en` en `timestamptz`; borrado lógico solo donde haga falta la historia; columnas con datos personales marcadas con el comentario `@pii` y excluidas de los logs.
- Toda tabla con datos de una organización externa lleva `organizacion_id` y una **política RLS**, además del predicado obligatorio en cada consulta. En pdn hay que confirmar la interacción con PgBouncer en modo session **[por verificar]**.
- Extensiones `vector` y `unaccent`: las crea el **administrador**, porque el usuario del servicio no puede.
- **Redis**: HybridCache, llaves de Data Protection compartidas entre réplicas y limitación de tasa.
- **S3**: MinIO en dev y qa, Contabo en pdn, con bucket o prefijo `atlas/`. URL firmadas de 15 minutos (Contabo rechaza el POST policy: firma en cabecera o en query).
- **RabbitMQ**: exchange `flit.atlas`, publicación por outbox (ATLAS-ADR-0014).

## Consecuencias

### Positivas

- Se usa el script y el modelo de permisos de base de datos que ya conoce el equipo.
- `pgvector` y `tsvector` conviven con los datos y con los filtros de permisos en SQL (necesario para el asistente).

### Negativas

- Todas las migraciones de Atlas comparten un esquema: hace falta disciplina con los prefijos y un chequeo en CI de que cada módulo solo toca los suyos.
- RLS agrega complejidad a las pruebas y a las conexiones (variables de sesión por petición).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Un esquema por módulo | Rompe la regla de plataforma y el script de alta |
| Base propia | Otra instancia que operar y respaldar en VPS con capacidad limitada |

## Más información

- Plan 00 §13 (alta de la base). Prompt maestro §9 (tablas mínimas por prefijo).
