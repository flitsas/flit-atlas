---
id: ATLAS-ADR-0003
titulo: Stack base — PWA React + Vite, API .NET 10, servicios Go y Python
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 D-01 (frontend Next 16)", "plan 00 D-09 (worker Node atlas-exportador)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-02 y §5.2, herramientas-y-lenguajes.md]
---

# ATLAS-ADR-0003 · Stack base — PWA React + Vite, API .NET 10, servicios Go y Python

## Contexto y problema

El plan 00 proponía un frontend Next 16 y un worker Node (`atlas-exportador`, con Redocly y Playwright) para los exportes. El documento de herramientas recomienda en cambio repartir el trabajo por lenguaje según la fuerza de cada ecosistema, sin Node en producción. Atlas, además, debe ser una **PWA** móvil primero (prompt maestro §11), y su API es la única pieza expuesta (ATLAS-ADR-0008).

## Factores de decisión

- Una sola pieza expuesta y dueña de la seguridad.
- PWA instalable, con offline y push, servida como estáticos por la API o el ingress.
- No programar motores: usar librerías maduras (D2, vacuum, oasdiff, WeasyPrint).
- Equipo pequeño que ya domina .NET y React, y que tiene servicios Python (`python-ml`).
- Capacidad limitada de las VPS: Chromium como dependencia de producción es caro.

## Opciones consideradas

1. **React 19 + TypeScript + Vite (PWA)**, **.NET 10** (Minimal APIs, EF Core), **Go** para render y validación, **Python 3.13 + FastAPI** para IA y documentos. Node solo compila.
2. Next 16 + core-atlas .NET + worker Node para exportes (plan 00).
3. Todo en .NET, incluidos el render y los exportes.

## Decisión

Opción elegida: **1**. Reparto:

| Pieza | Tecnología | Responsabilidad |
|---|---|---|
| `apps/web` | React 19, TypeScript, Vite, `vite-plugin-pwa` | Toda la interacción; estáticos sin SSR |
| `atlas-api` | .NET 10 | BFF, identidad, permisos, módulos, YARP interno, SignalR, Web Push |
| `atlas-render` | Go | D2 → SVG/PNG, vacuum, oasdiff, sanitización de SVG |
| `atlas-ia` | Python 3.13 + FastAPI | Asistente, ingesta RAG, MCP |
| `atlas-docs` | Python 3.13 + FastAPI | PDF (WeasyPrint), Word, Excel, HTML, Postman; importadores |
| Local | .NET Aspire | Orquesta todo en desarrollo |

Comunicación: la PWA solo habla con `atlas-api`, y `atlas-api` llama a Go y Python por gRPC interno o les publica trabajos en la cola (toda operación de más de ~1 s). Cada librería se fija en una versión estable con al menos dos semanas de publicada y licencia revisada, registrada en el PR que la agrega.

## Consecuencias

### Positivas

- El SPA con Vite se sirve como estáticos y facilita el control del service worker y del offline.
- Sin Chromium en producción para los PDF: WeasyPrint es más liviano.
- D2, vacuum y oasdiff están escritos en Go y se integran como librerías.

### Negativas

- Tres lenguajes en el backend: más imágenes, más pipelines y más dependencias que vigilar (CodeQL, Trivy y Renovate para cada uno).
- Sin SSR: el SEO no importa (es una app privada), pero el primer pintado depende del bundle (meta < 200 KB gzip).
- No se reutilizan `packages/{shell,ui}` de `flit` si están atados a Next **[por verificar en ATLAS-ADR-0015]**.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Next 16 + worker Node (plan 00) | Agrega Node en producción y Chromium para el PDF; el SSR no aporta en una app privada y complica la PWA |
| Todo en .NET | Los motores de D2, vacuum y oasdiff son Go; en .NET habría que llamar binarios externos o librerías de pago para el PDF |

## Más información

- `docs/atlas/herramientas-y-lenguajes.md` (stack por caso).
- Prompt maestro §5.2 (tabla de piezas clave, con versiones por verificar).
