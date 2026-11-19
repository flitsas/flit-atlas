# Flit Atlas

Aplicación central de conocimiento de FLIT S.A.S.: catálogo de aplicativos, equipo y responsables, soporte de los sábados, contratos de integración, documentación viva, diagramas, Asistente Atlas y gobierno de IA. Todo en un solo lugar y con control de acceso fino. Es una **PWA** móvil primero.

> Repo **privado** (ATLAS-ADR-0002). Aquí no van secretos, IPs internas, tokens, rutas de SealedSecrets ni datos personales reales.

## Antes de trabajar

1. Lee [`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`](docs/atlas/prompts/00-prompt-maestro-flit-atlas.md), la fuente que prevalece.
2. Lee el bloque de tu fase en [`docs/atlas/prompts/01-prompts-por-fase-flit-atlas.md`](docs/atlas/prompts/01-prompts-por-fase-flit-atlas.md).
3. Revisa las decisiones en [`docs/adr/`](docs/adr/README.md).
4. Para agregar una capacidad, sigue [`docs/como-crear-un-modulo.md`](docs/como-crear-un-modulo.md) (módulo de ejemplo «hola»).

## Estructura

| Ruta | Contenido |
|---|---|
| `apps/web/` | PWA React + TypeScript + Vite |
| `services/api/` | `atlas-api` (.NET 10): BFF, identidad, permisos, módulos |
| `services/render/` | `atlas-render` (Go): D2, vacuum, oasdiff |
| `services/ia/` | `atlas-ia` (Python FastAPI): asistente, ingesta RAG, MCP |
| `services/docs/` | `atlas-docs` (Python FastAPI): exportes e importadores |
| `aspire/` | AppHost de .NET Aspire para desarrollo local |
| `contracts/` | OpenAPI, AsyncAPI y proto propios de Atlas |
| `deploy/` | Base kustomize/helm que consume `flit-gitops` |
| `docs/adr/` | ADR de Atlas (serie `ATLAS-ADR-NNNN`) |
| `docs/arquitectura/` | Modelo LikeC4 de Atlas |
| `docs/preparacion/` | Entregables de F0: puertos, prerrequisitos y preguntas abiertas |
| `docs/atlas/` | Planes, prompts e insumos del proyecto |

## Ramas

| Rama | Ambiente |
|---|---|
| `develop` (predeterminada) | dev |
| `staging` | qa |
| `release` | pdn |

Las tres están protegidas: se entra solo por PR, con revisión de CODEOWNERS y CI en verde. El líder técnico puede integrar sus propios PR sin una segunda aprobación, pero nunca sin el CI ([ATLAS-ADR-0017](docs/adr/0017-aprobacion-del-lider-tecnico.md)). Cada PR lleva **una** historia de usuario, con su evidencia (ver la plantilla de PR).

## Configuración del clon

```bash
scripts/install-githooks.sh   # activa el pre-commit (archivos sensibles + gitleaks)
```

El hook necesita [gitleaks](https://github.com/gitleaks/gitleaks#installing) en el `PATH`.

## Responsables

Principal: Jorman Copete · Secundario: Samuel Cárdenas (reparto v5, `docs/atlas/insumos/Responsables_Productos_FLIT_2026-10-20.md`).
