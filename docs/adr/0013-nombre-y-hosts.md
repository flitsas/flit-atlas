---
id: ATLAS-ADR-0013
titulo: Nombre «Flit Atlas» y hosts por ambiente
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-12, plan 00 D-13 y §18 P-05]
---

# ATLAS-ADR-0013 · Nombre «Flit Atlas» y hosts por ambiente

## Contexto y problema

La aplicación necesita un nombre estable (marca, manifiesto de la PWA, registro en Entra ID, correos de invitación) y un host por ambiente para el ingress propio (ATLAS-ADR-0008) y las redirect URIs de Entra ID.

## Factores de decisión

- Coherencia con la marca FLIT y con los hosts existentes (`*.flitsas.online` para dev y qa).
- Un dominio de producción apto para clientes externos.
- Las redirect URIs y las cookies `__Host-` exigen hosts fijos por ambiente.

## Opciones consideradas

1. **«Flit Atlas»** con `dev.atlas.flitsas.online`, `qa.atlas.flitsas.online` y `atlas.flitsas.com`.
2. Producción también bajo `.online` (`atlas.flitsas.online`).
3. Ruta bajo un host existente (p. ej. `/atlas` en el host de la Suite).

## Decisión

Opción elegida: **1**. Nombre: **Flit Atlas** (en la interfaz y en los documentos). Identificadores técnicos: `atlas` y `flit-atlas`.

| Ambiente | Host |
|---|---|
| dev | `dev.atlas.flitsas.online` |
| qa | `qa.atlas.flitsas.online` |
| pdn | `atlas.flitsas.com` **[por verificar: confirmar el dominio .com con la estrategia de dominios]** |

## Consecuencias

### Positivas

- Un host propio por ambiente: cookies `__Host-`, CSP y ingress sin compartir origen con otras apps.

### Negativas

- Requiere DNS y certificados por host, y registrar las redirect URIs en Entra ID por ambiente (F0.4).
- Si el `.com` no se aprueba, cambiar el host de pdn obliga a actualizar Entra ID, el manifiesto de la PWA y los enlaces enviados a clientes: hay que decidirlo antes de F4.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Ruta bajo un host existente | Comparte origen y cookies con otra app; complica la PWA (alcance del service worker) y la CSP |
| `.online` en pdn | Válida si no se aprueba el `.com`; queda como plan B |

## Más información

- Plan 00 §18 P-05.
