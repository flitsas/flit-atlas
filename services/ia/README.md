# services/ia · atlas-ia (Python FastAPI)

Orquestador del Asistente Atlas, ingesta RAG y servidor MCP. Toda llamada a LLM pasa por el gateway LiteLLM; el contexto de permisos lo firma `atlas-api` (ATLAS-ADR-0011). No se expone a internet. Fase **F7**.
