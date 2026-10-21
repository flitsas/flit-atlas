# apps/web · PWA de Flit Atlas

SPA **React + TypeScript + Vite** instalable como PWA (Android, iOS y escritorio). Node solo compila; no corre en producción (ATLAS-ADR-0003).

| Carpeta | Contenido |
|---|---|
| `src/app/` | Shell, rutas, layout móvil y escritorio, registro de módulos |
| `src/modulos/<modulo>/` | Un directorio por módulo, con carga diferida (ATLAS-ADR-0016) |
| `src/pwa/` | Service worker, push, offline e instalación |
| `tests/` | Vitest y Playwright (escritorio, Pixel 7, iPhone 14) |

## Comandos

```bash
pnpm install
pnpm dev        # necesita ATLAS_API_URL; lo normal es arrancarla desde el AppHost (aspire/)
pnpm typecheck
pnpm test
pnpm build
```

En desarrollo, Vite hace de proxy de `/api`, `/hubs`, `/connect`, `/.well-known` y las rutas de OIDC hacia `atlas-api`, para que la PWA y la API compartan origen (ATLAS-ADR-0008). El shell móvil primero llega en la HU 1.6.
