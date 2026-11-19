---
id: ATLAS-ADR-0019
titulo: Solo dos ambientes desplegados (qa y pdn) con hosts bajo flitsas.com
estado: aceptado
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["ATLAS-ADR-0013 (solo la tabla de hosts)"]
reemplazado_por: []
alias: []
fuentes: [decisión del líder del 22/10/2026, prompt maestro A-05 y A-12, F0.3]
---

# ATLAS-ADR-0019 · Solo dos ambientes desplegados (qa y pdn) con hosts bajo flitsas.com

## Contexto y problema

ATLAS-ADR-0006 y ATLAS-ADR-0013 suponían tres ambientes desplegados (dev, qa y pdn) y hosts `dev.atlas.flitsas.online`, `qa.atlas.flitsas.online` y `atlas.flitsas.com` (este último por confirmar). El 22/10/2026 el líder técnico definió que Atlas **no tendrá ambiente dev desplegado** y que los hosts son `qa.atlas.flitsas.com` y `atlas.flitsas.com`.

## Factores de decisión

- Un equipo pequeño y una VPS 177 con capacidad limitada (F0.4 ítem 3.1): un ambiente menos que operar y pagar.
- El desarrollo diario ya corre completo en local con .NET Aspire (Postgres, Redis, RabbitMQ y S3 reales).
- Un solo dominio para los ambientes que ven usuarios: certificados, CSP y redirect URIs más simples.

## Opciones consideradas

1. **qa y pdn**, con `qa.atlas.flitsas.com` y `atlas.flitsas.com`.
2. dev, qa y pdn, como en ATLAS-ADR-0013.

## Decisión

Opción elegida: **1**.

| Ambiente | Rama | Clúster | Namespace | Host | Puertos (F0.3) |
|---|---|---|---|---|---|
| local | cualquiera | .NET Aspire | — | `https://localhost:4032` | `40xx` |
| qa | `staging` | k3s 177 | `atlas-qa` | `qa.atlas.flitsas.com` | `50xx` |
| pdn | `release` | k3s 169 | `atlas-pdn` | `atlas.flitsas.com` | `60xx` |

- `develop` solo corre el CI: integra las HU, pero no despliega.
- Namespaces propios de Atlas, como recomienda F0.3 §4. Cierra el **[por verificar]** de ATLAS-ADR-0006.
- El proveedor de ingreso simulado queda solo en local (la API sigue rechazándolo en qa y pdn).
- Registros de Entra ID: el de «dev» que ya existe se usa **solo en local**; qa y pdn tienen cada uno el suyo, con su credencial (F0.4 ítems 5.1 y 5.2).
- Cierra la pregunta N-01 (dominio de pdn): `atlas.flitsas.com`.
- Los manifiestos viven completos en `flit-gitops` (`services/atlas-qa/`), como los de FLIT 2.0 y los satélites, en vez de una base en `deploy/` de este repo (ATLAS-ADR-0006): así Argo CD no necesita credenciales para leer `flit-atlas`, que es privado.
- En la 177 no hay Ingress: el nginx del host termina TLS y entra a `atlas-web` por una ClusterIP fija; `atlas-web` reenvía el BFF a `atlas-api` (mismo origen, F0.3 §3).

## Consecuencias

### Positivas

- Menos manifiestos, secretos y capacidad que mantener.
- Hosts coherentes bajo `flitsas.com`.

### Negativas

- Lo que se integra en `develop` no se ve en un servidor hasta pasar a `staging`: qa recibe cambios con más frecuencia y puede estar menos estable.
- Las pruebas contra Entra ID real, fuera de local, ocurren por primera vez en qa.
- El valor `dev` de `Atlas:Ambiente` queda sin uso desplegado; se conserva para no romper la configuración.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Tres ambientes | Decisión del líder: el costo de operar dev no se justifica con el desarrollo local completo |

## Más información

- Reemplaza la tabla de hosts de ATLAS-ADR-0013; el nombre «Flit Atlas» sigue vigente.
- F0.3 (`docs/preparacion/03-puertos-y-namespaces.md`) y F0.4 (`docs/preparacion/04-prerrequisitos.md`) se actualizan en el mismo PR.
