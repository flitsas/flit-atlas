---
id: ATLAS-ADR-0012
titulo: Alcance del MVP — fases F0 a F4
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 §16 (orden de fases)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-11, 01-prompts-por-fase-flit-atlas.md (mapa de fases), plan 00 §16]
---

# ATLAS-ADR-0012 · Alcance del MVP — fases F0 a F4

## Contexto y problema

El plan 00 ordenaba las fases priorizando contratos y portal, sin el módulo de equipo. Hoy los responsables y la rotación del soporte de los sábados viven en un Markdown y en un Excel, sin recordatorios ni sincronización con Outlook o Teams, y su valor es inmediato para todo el equipo.

## Factores de decisión

- Entregar valor usable pronto y por partes (equipo pequeño).
- Abrir a externos solo cuando la identidad, el aislamiento y la revisión de seguridad estén listos.
- Reemplazar el Excel de contratos ICT antes que otros módulos.

## Opciones consideradas

1. **MVP = F0 a F4**: cimientos (login Microsoft, invitaciones, roles), catálogo, equipo (responsables y rotación) y contratos (incluidos los legados ICT) **publicados, compartibles y exportables, en pdn**.
2. MVP solo con contratos y portal (plan 00).
3. MVP con todo, asistente incluido.

## Decisión

Opción elegida: **1**.

| Fase | Entrega |
|---|---|
| F0 | ADR, repo y prerrequisitos |
| F1 | Identidad propia, roles, PWA instalable en dev |
| F2 | Catálogo, responsables y rotación de soporte (con Outlook y Teams) |
| F3 | Contratos internos y legado ICT, versionados por CI |
| F4 | Portal de clientes, exportes, revisión de seguridad y salida a `atlas.flitsas.com` |

F5 a F8 (documentación, diagramas, asistente y gobierno de IA) quedan fuera del MVP. F-IA corre en paralelo y es prerrequisito de F7. El alcance de cada fase se recalibra al cerrar F1.

## Consecuencias

### Positivas

- El equipo usa Atlas desde F2 (soporte y responsables) y valida la PWA con usuarios reales antes de abrir a externos.

### Negativas

- El MVP es más grande que el del plan 00: más riesgo de plazo. Mitigación: módulos detrás de banderas y valor independiente por fase.
- La documentación viva y los diagramas, que también reemplazan herramientas sueltas, esperan hasta después de F4.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Solo contratos y portal | Deja fuera el módulo con valor más inmediato para el equipo |
| Todo en el MVP | Inviable con 1–2 desarrolladores; retrasa todo el valor |

## Más información

- Mapa de fases y estimaciones en `docs/atlas/prompts/01-prompts-por-fase-flit-atlas.md`.
