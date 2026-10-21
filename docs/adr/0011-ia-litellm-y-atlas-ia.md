---
id: ATLAS-ADR-0011
titulo: IA a través del gateway LiteLLM y orquestador en atlas-ia (Python)
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 03 (orquestador del asistente en .NET)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-10 y §8.7, plan 00 D-11 y D-12, plan 02, plan 03 D5]
---

# ATLAS-ADR-0011 · IA a través del gateway LiteLLM y orquestador en atlas-ia (Python)

## Contexto y problema

Hoy cada app de FLIT llama a los proveedores de LLM con llaves propias. El plan 02 centraliza todo en un gateway LiteLLM con llaves virtuales, presupuestos y Presidio. El plan 03 ponía el orquestador del asistente en .NET; con ATLAS-ADR-0003, la IA vive en Python. Además, el ADR-0070 de `flit` descartó en su día un gateway externo: el plan 00 (P-08) pide un ADR en `flit` que lo reemplace parcialmente.

## Factores de decisión

- Control de costos, de proveedores y de PII en un solo punto.
- Ecosistema de RAG y agentes más maduro en Python.
- Los permisos nunca se delegan al cliente ni al LLM: se filtran en SQL.

## Opciones consideradas

1. **Toda llamada a LLM por LiteLLM**, con llaves virtuales y alias (`flit-asistente-principal`, `flit-rapido`, `flit-embeddings`). Orquestador e ingesta en **`atlas-ia` (Python)**. Embeddings bge-m3 servidos por `python-ml`. Contexto de permisos firmado por `atlas-api`.
2. Orquestador en .NET dentro de `atlas-api` (plan 03).
3. Llamadas directas a los proveedores desde `atlas-ia`.

## Decisión

Opción elegida: **1**.

- `atlas-ia` usa un cliente compatible con OpenAI/Anthropic apuntando al gateway, con la llave virtual `atlas-asistente-{dev|qa|pdn}` en SealedSecret. LiteLLM con versión ≥ 1.83.0 fijada por digest (**nunca 1.82.7 ni 1.82.8**).
- `atlas-api` construye el **contexto de permisos** (persona, tipo de principal, organización, roles, alcances, visibilidades), lo firma (JWS con llave de OpenIddict, vida corta) y lo envía con cada petición a `atlas-ia`. `atlas-ia` verifica la firma y aplica los filtros en SQL (pgvector + `tsvector`); nunca usa datos del cliente para decidir qué puede ver.
- El MCP para Claude Code y Cursor vive en `atlas-ia` y autentica con tokens de OpenIddict (`atlas.mcp`).
- Modo degradado sin LLM (búsqueda léxica) cuando el gateway no responda.

## Consecuencias

### Positivas

- Costos, presupuestos y proveedores gobernados desde un punto; PII enmascarada en el gateway.
- La seguridad de los datos no depende del LLM.

### Negativas

- El gateway es un punto único de falla (la preocupación del ADR-0070 de `flit`): 2 réplicas en pdn y modo degradado.
- F7 depende de F-IA (el gateway desplegado) y de que en `flit` se apruebe el ADR que reemplaza parcialmente al ADR-0070 (P-08).
- La elección de proveedores (P-04) sigue abierta: hasta que se decida, solo los aprobados en el plan 02.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Orquestador en .NET (plan 03) | Contradice el reparto de ATLAS-ADR-0003; el ecosistema de RAG y agentes es más fuerte en Python |
| Llamadas directas | Llaves por app, sin presupuesto ni Presidio: lo que el plan 02 busca eliminar |

## Más información

- Plan 02 (gateway y gobierno), plan 03 (asistente). P-04 y P-08 del plan 00 §18.
