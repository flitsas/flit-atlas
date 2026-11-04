---
id: ATLAS-ADR-0052
titulo: Publicación de contratos — un Atlas central en pdn para todos los canales y una publicación de prueba en el Atlas de QA desde staging
estado: aceptado (decisión del líder, 04/11/2026)
fecha: 2026-11-04
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [ATLAS-ADR-0019, ATLAS-ADR-0035 §7 y §8]
---

# ATLAS-ADR-0052 · Un Atlas central en pdn y una publicación de prueba en QA

## Contexto y problema

ATLAS-ADR-0035 (§7 y §8) supone **un solo Atlas** que recibe los contratos de todos los repos y los canales `dev`, `qa`
y `pdn`. Con eso funcionan el Consolidado, los desfases entre ambientes y los breaking pendientes entre qa y pdn.
Mientras Atlas no estuvo en pdn, la variable de organización `ATLAS_URL` apuntaba al Atlas de QA.

El 04/11/2026 se hizo el primer CD de `release`: pdn quedó con la versión de `release`, pero el job `contratos` publicó
en el Atlas de QA. El líder cambió `ATLAS_URL` a `https://atlas.flitsas.com` y relanzó la publicación: `atlas` y
`atlas-eventos` 1.0.0 quedaron en el canal pdn del Atlas de pdn.

Desde ese cambio, el Atlas de QA deja de recibir publicaciones. Una versión nueva de Atlas que llega a QA ya no se
prueba con una publicación real antes de pasar a `release`.

## Factores de decisión

- El Consolidado y los desfases necesitan los canales de todos los ambientes en un mismo Atlas.
- La publicación es parte del producto: una versión nueva de atlas-api debe probarse publicando antes de llegar a pdn.
- `ATLAS_URL` de la organización la usan todos los repos (el CI de `flitsas/flit` y el de flit-atlas); no conviene
  duplicar la configuración en cada uno.
- Definir la variable en el entorno `production` del repo no sirve: el job `contratos` llama a un workflow
  reutilizable y no puede declarar `environment:`.

## Opciones consideradas

1. Un Atlas central en pdn y, además, una publicación de prueba en el Atlas de QA desde `staging`.
2. Separar por ambiente: `staging` publica solo en el Atlas de QA y `release` solo en el de pdn.
3. Todo al Atlas de pdn, sin publicar en QA.

## Decisión

Opción elegida: «un Atlas central en pdn y una publicación de prueba en el Atlas de QA», porque conserva el Consolidado
de ATLAS-ADR-0035 y sigue probando la publicación con la versión de Atlas que corre en QA.

- **Variable de organización `ATLAS_URL` = `https://atlas.flitsas.com`.** Es el Atlas central: ahí publican todos los
  repos (CI de `develop`) y ahí marca cada CD su canal (`qa` desde `staging`, `pdn` desde `release`).
- **Variable de organización `ATLAS_URL_QA` = `https://qa.atlas.flitsas.com`.** En `cd.yml`, el job nuevo
  `contratos-qa` corre solo desde `staging`, después de `gitops`, y publica los dos contratos de Atlas en el Atlas de QA
  con `ambiente: qa`, con el mismo workflow reutilizable. Sin la variable no corre. Si falla, el job queda rojo, pero no
  frena el despliegue ni la publicación central.
- Los demás repos (por ejemplo, `flitsas/flit`) no cambian: publican solo en el Atlas central.

## Consecuencias

### Positivas

- El Consolidado de pdn ve los canales `qa` y `pdn`, y sus desfases, como pide ATLAS-ADR-0035.
- Cada versión de Atlas que llega a QA publica y marca un canal real antes de `release`.
- El Atlas de pdn no recibe datos de prueba; el de QA sigue con datos de Atlas propios.

### Negativas

- El CD de `staging` publica dos veces (central y QA): unos segundos más y un segundo token OIDC con otra audiencia.
- La lista blanca de `flitsas/flit-atlas` debe existir en los dos Atlas.
- El Atlas de QA solo tiene los contratos de Atlas, no los de los demás repos.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Separar por ambiente | El Consolidado de pdn queda sin canal qa: sin desfases ni breaking pendientes (ATLAS-ADR-0035 §8) |
| Todo a pdn sin publicar en QA | La publicación de una versión nueva de Atlas solo se probaría en pdn |
| `ATLAS_URL` en el entorno `production` del repo | El job `contratos` llama a un workflow reutilizable y no lee variables de entorno de GitHub |

## Más información

- ATLAS-ADR-0035 §7 (lo que hace el líder, punto 3) y §8 (canales y Consolidado).
- Lo que hace el líder: crear la variable de organización `ATLAS_URL_QA` = `https://qa.atlas.flitsas.com`.
