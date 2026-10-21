---
id: ATLAS-ADR-0009
titulo: Publicación desde CI con el token OIDC de GitHub Actions y lista blanca
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 03 §8.2 (ATLAS_PUBLICADOR_TOKEN con client credentials)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-08, plan 00 D-05 y §8.3, plan 03 §8.2]
---

# ATLAS-ADR-0009 · Publicación desde CI con el token OIDC de GitHub Actions y lista blanca

## Contexto y problema

Git es la fuente de verdad de los contratos, de los modelos LikeC4 y de la documentación de los repos: la CI de cada repo publica en Atlas versiones inmutables. El plan 00 (D-05) usaba el OIDC de GitHub Actions; el plan 03 (§8.2) usaba un secreto `ATLAS_PUBLICADOR_TOKEN` de client credentials. Hay que unificar el mecanismo para **todas** las fuentes.

## Factores de decisión

- Sin secretos estáticos en ~40 repos (nada que rotar ni que se filtre).
- Saber con certeza qué repo, rama y workflow publicó cada versión.
- Un solo mecanismo para contratos, LikeC4 y documentación.

## Opciones consideradas

1. **Token OIDC de GitHub Actions** (`token.actions.githubusercontent.com`), validado contra una lista blanca por repo, rama y workflow.
2. Secreto de client credentials por repo (plan 03 §8.2).
3. GitHub App que firma las publicaciones.

## Decisión

Opción elegida: **1**.

- El workflow reutilizable `atlas-publicar` pide un id-token con `audience` igual a la del ambiente de Atlas (p. ej. `https://atlas.flitsas.com`) y lo envía como `Bearer`.
- `atlas-api` valida firma (JWKS de GitHub), `iss`, `aud`, `exp` y `nbf`, y luego los claims `repository`, `repository_owner` (`flitsas`), `ref`, `job_workflow_ref` y `event_name` contra `catalogo_fuente_publicacion` (aplicativo ↔ repo ↔ ramas permitidas ↔ workflow). Se aceptan solo `develop`, `staging` y `release`, mapeados a dev, qa y pdn.
- `job_workflow_ref` debe apuntar al workflow reutilizable oficial en una referencia fijada (tag o sha), para que un repo no pueda publicar con un workflow propio.
- Respuestas: 401 para tokens inválidos o vencidos; 403 si está fuera de la lista blanca; 201 al crear; 200 idempotente si el hash coincide; 409 con hash distinto para la misma versión; 422 si la fuente es inválida.
- Cada publicación queda auditada con `repository`, `sha`, `run_id` y el hash del contenido.
- Aplica a contratos (F3), LikeC4 (F6) y documentación (F5).

## Consecuencias

### Positivas

- Cero secretos en los repos. La identidad del publicador la da GitHub y se puede verificar.
- Revocar una fuente es borrar una fila de la lista blanca.

### Negativas

- Depende de la disponibilidad del JWKS de GitHub: se cachea con margen y se reintenta.
- Los runners propios (si se usan) deben poder pedir id-tokens; eso funciona con runners autoalojados del mismo GitHub **[verificar en F3]**.
- Se descarta la opción de publicar desde fuera de GitHub Actions (por ejemplo, desde un portátil); para eso queda el permiso excepcional `atlas.contratos.publicar_manual`.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Client credentials por repo (plan 03 §8.2) | Secreto en cada repo, que hay que rotar y proteger; si se filtra, otro puede publicar |
| GitHub App | Más piezas (llave privada de la app, instalación) para lo que el OIDC ya da sin secretos |

## Más información

- Plan 00 §8.3 (secuencia y borrador del workflow, que se actualiza a vacuum y oasdiff según ATLAS-ADR-0010).
