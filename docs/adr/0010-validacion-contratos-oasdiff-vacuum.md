---
id: ATLAS-ADR-0010
titulo: Validación de contratos con oasdiff y vacuum (Go)
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 D-07 (Spectral CLI en Node para el estilo)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-09, plan 00 D-07, herramientas-y-lenguajes.md caso 4]
---

# ATLAS-ADR-0010 · Validación de contratos con oasdiff y vacuum (Go)

## Contexto y problema

Los contratos publicados deben pasar reglas de estilo y de seguridad, y los cambios incompatibles deben bloquear la publicación si no suben la versión MAJOR. El plan 00 (D-07) usaba Spectral CLI, que es Node, y oasdiff. Con ATLAS-ADR-0003 no hay Node en producción, y el servicio `atlas-render` ya es Go.

## Factores de decisión

- Un solo motor de reglas en CI y en el servidor (mismos resultados).
- Sin Node en producción.
- Reglas compatibles con el ecosistema Spectral (reglas existentes, OWASP API).

## Opciones consideradas

1. **oasdiff** (breaking y changelog) y **vacuum** (lint compatible con rulesets de Spectral), como librerías o binarios de Go en CI y en `atlas-render`.
2. Spectral CLI + oasdiff (plan 00 D-07).
3. Validación propia en .NET con `Microsoft.OpenApi`.

## Decisión

Opción elegida: **1**.

- CI (`atlas-publicar`): `vacuum lint` con el ruleset de FLIT (`.vacuum.yaml`, compatible con Spectral) y `oasdiff breaking --fail-on ERR` contra la versión publicada en pdn; `oasdiff changelog` en JSON se adjunta a la publicación.
- Servidor: `atlas-render` repite la validación al recibir la publicación (no se confía en la CI) y calcula los diffs bajo demanda, con caché por par de versiones.
- Reglas mínimas: autenticación declarada en cada operación, sin datos sensibles en la URL, `operationId` únicos, ejemplos sin credenciales reales.
- AsyncAPI: diff estructural propio hasta que haya una herramienta madura. Proto: `buf breaking`.
- Referencia al 21/10/2026: **oasdiff v1.34.0** (Apache-2.0; el repo se movió a `github.com/oasdiff/oasdiff`, y `tufin/oasdiff` queda solo como imagen de Docker Hub; se puede embeber como librería Go) y **vacuum v0.33.2** (MIT, módulo `github.com/daveshanley/vacuum`). Ambas son muy recientes: en F3 se fija la versión más nueva que tenga al menos dos semanas de publicada.

## Consecuencias

### Positivas

- Los mismos resultados en CI y en el servidor; sin Node; integración nativa en el servicio Go.

### Negativas

- vacuum no implementa el 100 % de las funciones personalizadas de Spectral en JavaScript: las reglas propias deben escribirse sin JS.
- El diff de AsyncAPI es trabajo propio y limitado.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Spectral CLI | Node en la imagen del servidor; dos motores distintos si en CI se usa uno y en el servidor otro |
| Validación propia en .NET | Reinventar un motor de reglas y de diff ya maduro (prompt maestro §1 regla 6) |

## Más información

- Plan 00 §8.2 (reglas de versionado) y §8.3 (flujo de publicación).
