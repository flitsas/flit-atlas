---
id: ATLAS-ADR-0002
titulo: Repo privado propio flitsas/flit-atlas
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 D-01 (ubicación en el monorepo flit)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-01, plan 00 D-01, plan 00 §18 P-01]
---

# ATLAS-ADR-0002 · Repo privado propio flitsas/flit-atlas

## Contexto y problema

El plan 00 (D-01) ubicaba Atlas dentro del monorepo `flitsas/flit` (`services/core-atlas` y `frontend-atlas`), para aprovechar la plantilla de servicio, el SDK de plataforma, el CI y los paquetes compartidos. Pero `flitsas/flit` es **público** (verificado con `gh repo view` el 21/10/2026), y Atlas va a guardar el modelo de permisos, la configuración de identidad, fixtures de organizaciones cliente, las especificaciones del portal y su propia documentación de seguridad. La decisión P-01 (pasar `flit` a privado) sigue abierta.

## Factores de decisión

- Confidencialidad del código y de los ADR de seguridad de Atlas.
- Independencia de P-01: Atlas no puede esperar a que se decida la visibilidad de `flit`.
- Reutilizar lo que se pueda de `flit` (tokens de marca, SDK) sin copiar código a ciegas.
- Facilidad de CI y CD con `flit-gitops`.

## Opciones consideradas

1. Repo privado nuevo `flitsas/flit-atlas` que consume paquetes de `flit` cuando estén publicados.
2. Dentro de `flitsas/flit`, como decía D-01.
3. Dentro de `flitsas/flit` a la espera de que P-01 lo vuelva privado.

## Decisión

Opción elegida: **1, el repo privado `flitsas/flit-atlas`**. El repo ya existe y es privado (verificado). Ramas: `develop` (dev), `staging` (qa) y `release` (pdn), protegidas, con revisión de CODEOWNERS y CI obligatorio. Los paquetes compartidos de `flit` se consumen como dice ATLAS-ADR-0015.

## Consecuencias

### Positivas

- El código, los ADR y los fixtures de Atlas no quedan expuestos.
- Ciclo de release propio, sin afectar a FLIT 2.0.
- Atlas puede aplicar desde el día uno las reglas de gitleaks y de protección de ramas sin heredar la deuda de `flit`.

### Negativas

- No hereda automáticamente `core-plantilla`, `servicio-nuevo.md`, el CI ni los paquetes. Hay que replicar o consumir esas piezas (ATLAS-ADR-0015).
- El repo privado consume minutos de GitHub Actions. **[por verificar: usar los runners propios de la 177]**.
- Los cambios de registro de puertos en `docs/despliegue-y-puertos.md` de `flit` siguen siendo un PR en otro repo, que se coordina con el líder.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Dentro de `flit` (D-01) | El repo es público: el modelo de permisos, las fixtures y la documentación de seguridad quedarían expuestos |
| Esperar P-01 | Bloquea F0 y F1 por una decisión de gerencia sin fecha |

## Más información

- Prompt maestro A-01 y §5.3 (estructura del repo).
- P-01 sigue abierta: mientras `flit` sea público, cualquier contrato que viva en él también lo es (afecta a F3 y F4).
