---
id: ATLAS-ADR-0001
titulo: Registro de decisiones con MADR y serie propia ATLAS-ADR
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §1 regla 2, plan 01 §13.2]
---

# ATLAS-ADR-0001 · Registro de decisiones con MADR y serie propia ATLAS-ADR

## Contexto y problema

El prompt maestro exige escribir un ADR antes que el código para toda decisión de arquitectura, con «numeración única y continua» en `docs/adr/` del repo `flit-atlas`. En el monorepo `flit` los ADR están repartidos en cinco carpetas y tienen 14 números duplicados (plan 01 §13.1). El plan 01 §13.2 propone ahí un contador global que arranca en ADR-0072. Atlas, en cambio, vive en su propio repo privado (ATLAS-ADR-0002), y FLITO ya tiene su propia serie, `FLITO-ADR-NNNN`.

## Factores de decisión

- Numeración sin colisiones, verificable en CI.
- No depender de coordinar números con un repo público (`flit`).
- Que el módulo de arquitectura de Atlas pueda consolidar los ADR de todos los repos sin choques de id.
- Formato que entiendan las personas y las herramientas (front-matter legible por máquina).

## Opciones consideradas

1. Serie propia `ATLAS-ADR-NNNN` en `docs/adr/`, formato MADR 4 con el front-matter del plan 01 §13.2.
2. Contador global del monorepo `flit` (ADR-0072 en adelante).
3. Sin prefijo: `ADR-0001` en el repo de Atlas.

## Decisión

Opción elegida: **1, la serie propia `ATLAS-ADR-NNNN`**, decidida por el líder técnico en la sesión F0 del 21/10/2026.

- Archivos: `docs/adr/NNNN-<slug>.md`, con 4 dígitos y slug en español sin tildes.
- Front-matter: el de la [`plantilla`](plantilla.md), es decir, `id`, `titulo`, `estado`, `fecha`, `decisores`, `ambito`, `elementos` (ids LikeC4), `reemplaza`, `reemplazado_por`, `alias` y `fuentes`.
- Secciones: contexto y problema, factores, opciones, decisión, consecuencias, opciones descartadas y más información.
- Estados: `propuesto → aceptado | rechazado`. Un ADR aceptado no se edita en su fondo; se reemplaza con uno nuevo, que llena `reemplaza` y deja el viejo en `reemplazado`.
- Índice en [`README.md`](README.md), que se actualiza en el mismo PR que agrega el ADR.
- En F1 se agrega un chequeo de CI que rechaza números o ids repetidos y front-matter incompleto.

## Consecuencias

### Positivas

- Sin colisiones con `flit` ni con FLITO. El prefijo deja claro el origen cuando Atlas consolida todas las series.
- Las decisiones de Atlas no quedan en un repo público.

### Negativas

- Una serie más que mantener. Al citar decisiones de `flit` hay que usar su id completo (p. ej. `ADR-0070` de `flit`).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Contador global de `flit` | Obliga a coordinar números con un repo público, cuya limpieza (plan 01 §13) aún no está hecha |
| `ADR-0001` sin prefijo | Choca con `ADR-0001` de `core-ict` y con las series consolidadas en el módulo de arquitectura |

## Más información

- Plan 01 §13.2 (formato MADR 4 y front-matter).
- Los ADR de F0 (0001 a 0016) los aceptó el líder técnico el 21/10/2026. Desde ahí, todo cambio a ellos va en un ADR nuevo.
