---
id: ATLAS-ADR-0017
titulo: El líder técnico puede integrar sus propios PR sin una segunda aprobación
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [ATLAS-ADR-0002, prompt maestro §1]
---

# ATLAS-ADR-0017 · El líder técnico puede integrar sus propios PR sin una segunda aprobación

## Contexto y problema

ATLAS-ADR-0002 deja `develop`, `staging` y `release` protegidas, con revisión de CODEOWNERS y CI obligatorio. Los CODEOWNERS son `@jormancopeteflit` (principal) y `@scardenas-flit` (secundario).

GitHub no permite que el autor de un PR lo apruebe, aunque sea CODEOWNER o administrador. Los PR de Atlas los abre la cuenta del líder técnico, así que hoy solo el secundario puede aprobarlos. Eso frena el avance cuando el secundario no está disponible. El líder técnico decidió el 21/10/2026 que puede aprobar e integrar lo que él mismo desarrolla.

Antes de esta decisión, la cuenta del líder ya podía integrar cualquier PR con el «bypass» de administrador, porque `enforce_admins` estaba apagado. Pero ese bypass también se salta el CI.

## Factores de decisión

- No bloquear el avance por la disponibilidad de una sola persona.
- Mantener el CI (markdownlint, gitleaks y lo que se agregue en F1) obligatorio para todos, incluido el líder.
- Que la excepción sea explícita, auditable y fácil de revertir.

## Opciones consideradas

1. **Permiso de omitir la aprobación solo para el líder** (`bypass_pull_request_allowances`) y `enforce_admins` activado, para que el CI aplique también a los administradores.
2. Bajar a 0 las aprobaciones requeridas para todos.
3. Seguir usando el bypass de administrador, con `enforce_admins` apagado.
4. Abrir los PR con otra identidad (un bot) para que el líder los apruebe.

## Decisión

Opción elegida: **1**, en las tres ramas protegidas:

- `jormancopeteflit` puede integrar un PR sin la aprobación de otra persona.
- El CI es obligatorio para todos, administradores incluidos. Ningún PR entra con el CI en rojo, y no se puede hacer push directo de commits sin verificar.
- Para los demás, la regla de ATLAS-ADR-0002 sigue igual: una aprobación de un CODEOWNER.
- La regla de una HU por PR, con su evidencia, no cambia: el PR sigue siendo el registro de lo que se integró.

Se revisa cuando el equipo crezca o antes de abrir el acceso a externos en pdn (F4). La revisión de seguridad de F4 debería recomendar que los PR de los módulos `Identidad` y de permisos tengan siempre una segunda revisión humana.

## Consecuencias

### Positivas

- El líder no depende de otra persona para integrar su trabajo.
- El CI queda más estricto que antes, porque ahora también obliga a los administradores.

### Negativas

- Los cambios del líder pueden entrar sin un segundo par de ojos, incluido código de identidad y de permisos. Mitigación: CI obligatorio, `/code-review` antes de integrar los PR sensibles, y la revisión de seguridad de F4.
- Con `enforce_admins` activado, ni siquiera un administrador puede saltarse el CI en una emergencia sin desactivarlo primero. Ese cambio queda en el registro de auditoría de la organización.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. Cero aprobaciones para todos | Quita la revisión también a los demás desarrolladores |
| 3. Bypass de administrador | Se salta el CI, incluido gitleaks |
| 4. PR desde un bot | Agrega una credencial más que guardar y rotar, solo para cumplir la forma |

## Más información

- Configuración aplicada en GitHub: `required_pull_request_reviews.bypass_pull_request_allowances.users = [jormancopeteflit]` y `enforce_admins = true` en `develop`, `staging` y `release`.
- ATLAS-ADR-0002 sigue vigente. Este ADR solo agrega la excepción.
