---
id: ATLAS-ADR-0015
titulo: Consumo de paquetes compartidos de flit — NuGet publicado para el SDK y réplica de los tokens de marca
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-01, plan 00 D-01]
---

# ATLAS-ADR-0015 · Consumo de paquetes compartidos de flit

## Contexto y problema

Al salir del monorepo (ATLAS-ADR-0002), Atlas pierde el acceso directo a los paquetes compartidos de `flit`. El prompt maestro supone que se consumen por GitHub Packages, **[por verificar que estén publicados]**. Lo que se verificó el 21/10/2026 en `flitsas/flit` (repo público):

| Paquete | Estado | ¿Sirve para Atlas? |
|---|---|---|
| `Flit.Platform.Sdk` (.NET) | Proyecto en `services/core-api/src/`. **No hay publicación** (`dotnet nuget push`) en ningún workflow | Sí: mensajería y telemetría (ATLAS-ADR-0014) |
| `@flit/brand` | `private: true`, versión `0.0.0`, peer **`next` 16.3.8** y `server-only` | Parcial: `derive-tokens`, `contrast` y `derive-dark` son TS puro, pero `resolve-brand.server` y `BrandStyle` dependen de Next |
| `@flit/ui` | `private: true`, `0.0.0`; exporta `tokens.ts` y `tokens.css` | Los tokens sí; los componentes, por evaluar |
| `@flit/shell` | `private: true`, peer `next` 16.3.8 | No: es la barra de la Suite para apps Next; Atlas tiene su propio shell PWA |
| `@flit/auth` | `private: true`, sesión OIDC con core-identity para Next | No: Atlas tiene identidad propia (ATLAS-ADR-0004) |

No se pudo listar GitHub Packages de la organización porque al token le falta el scope `read:packages`. Pero sin un workflow de publicación es muy poco probable que existan.

## Factores de decisión

- No copiar código de .NET que evoluciona (el SDK): divergencia segura.
- Mantener la marca FLIT coherente sin depender de Next.
- No crear trabajo grande en `flit` para paquetes que Atlas no usará (`shell`, `auth`).
- Trazabilidad: saber de qué versión de `flit` sale lo replicado.

## Opciones consideradas

1. **Mixta**: publicar `Flit.Platform.Sdk` como paquete NuGet en GitHub Packages desde `flit` y **replicar solo los tokens de marca** (CSS y TS puros de `@flit/ui` y `@flit/brand`) en `apps/web`, con un script de sincronización fijado a un commit de `flit`.
2. Publicar todos los paquetes npm y NuGet de `flit` en GitHub Packages.
3. Copiar el código necesario (SDK incluido) en Atlas.
4. Git submodule de `flit`.

## Decisión

Opción elegida: **1**.

### NuGet (`Flit.Platform.Sdk`)

- Proponer en `flit` (PR coordinado con el líder; **no se hace desde esta sesión**) un workflow que empaquete y publique `Flit.Platform.Sdk` en `https://nuget.pkg.github.com/flitsas/index.json` con versión semántica por tag (`sdk-v1.2.3`).
- Atlas lo consume con `nuget.config` hacia GitHub Packages y la versión fijada en `Directory.Packages.props`. En CI, el `GITHUB_TOKEN` de `flit-atlas` necesita acceso de lectura al paquete (configuración de acceso del paquete hacia el repo). En local, cada desarrollador usa un PAT con `read:packages`, que nunca va al repo.
- Si el paquete no está antes de F1: se aplica el plan B de ATLAS-ADR-0014. No se copia el SDK.

### Marca (npm)

- `apps/web/src/marca/` contiene una **réplica** de los tokens de diseño (colores, tipografía, radios, sombras, claro y oscuro) y de las funciones puras `derive-tokens`, `derive-dark` y `contrast`.
- Un script `tools/marca/sincronizar` descarga esos archivos de `flit` **en un commit fijado** (el repo es público: lectura sin credenciales) y escribe un encabezado con la ruta de origen y el sha. CI verifica que los archivos coincidan con el sha declarado.
- La marca por host (marca blanca, ADR-0060 de `flit`) no aplica a Atlas: Atlas usa la marca FLIT.
- Si más adelante `@flit/ui` y `@flit/brand` se separan de Next y se publican, se reemplaza la réplica por la dependencia (un ADR nuevo lo registra).

## Consecuencias

### Positivas

- El SDK tiene una sola fuente de verdad. La marca queda coherente sin arrastrar Next.
- No se pide a `flit` publicar paquetes que Atlas no usaría.

### Negativas

- La réplica de los tokens de marca puede desactualizarse: se revisa en cada actualización del sha y en la revisión trimestral.
- Hay que coordinar un PR en `flit` y la configuración de acceso al paquete.
- Mientras `flit` siga público, el paquete del SDK publicado desde ese repo probablemente también lo será **[por verificar la visibilidad del paquete]**. No es un problema nuevo: el código ya es público.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Publicar todo | `shell` y `auth` no sirven para Atlas (Next y core-identity); `brand` exige separarlo de Next: trabajo en `flit` sin beneficio para Atlas |
| Copiar el SDK | Divergencia segura en código de infraestructura que sí evoluciona |
| Submodule | Acopla Atlas a todo el monorepo y a su ritmo; complica el CI |

## Más información

- Paquetes revisados con `gh api repos/flitsas/flit/contents/packages/*/package.json` el 21/10/2026.
- ATLAS-ADR-0014 (uso del SDK y plan B).
