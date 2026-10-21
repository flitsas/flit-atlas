# apps/web · PWA de Flit Atlas

SPA **React + TypeScript + Vite** instalable como PWA (Android, iOS y escritorio). Node solo compila; no corre en producción (ATLAS-ADR-0003).

| Carpeta | Contenido |
|---|---|
| `src/app/` | Shell, rutas, layout móvil y escritorio, registro de módulos |
| `src/modulos/<modulo>/` | Un directorio por módulo, con carga diferida (ATLAS-ADR-0016) |
| `src/pwa/` | Service worker, push, offline e instalación |
| `tests/` | Vitest y Playwright (escritorio, Pixel 7, iPhone 14) |

Se llena en la fase **F1**.
