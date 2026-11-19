# apps/web · PWA de Flit Atlas

SPA **React + TypeScript + Vite** instalable como PWA (Android, iOS y escritorio). Node solo compila; no corre en producción (ATLAS-ADR-0003). El shell móvil primero es de la HU 1.6 (ATLAS-ADR-0020).

| Carpeta | Contenido |
|---|---|
| `src/app/` | Shell (`layout/`), enrutador (`App.tsx`), sesión, tema, búsqueda global (`busqueda/`), páginas del núcleo (`paginas/`) y componentes compartidos (`ui/`) |
| `src/app/modulos/` | Contrato `ModuloAtlas`, registro explícito (`registro.ts`) y reglas de banderas, permisos y navegación (`resolver.ts`) |
| `src/modulos/<modulo>/` | Un directorio por módulo con su `modulo.ts`, páginas y widgets de Inicio, todos con carga diferida (ATLAS-ADR-0016) |
| `src/pwa/` | Service worker (`sw.ts`), Web Push, caché sin conexión en IndexedDB y estado de la red |
| `src/marca/` | Réplica de los tokens y el isotipo de FLIT (ATLAS-ADR-0015); no se edita a mano |
| `public/iconos/` | Íconos, maskable, atajos e imágenes de arranque de iOS (`pnpm iconos`) |
| `tests/` | Vitest y Playwright (escritorio, Pixel 7, iPhone 14) con axe |

## Comandos

```bash
pnpm install
pnpm dev        # necesita ATLAS_API_URL; lo normal es arrancarla desde el AppHost (aspire/)
pnpm typecheck  # app y service worker
pnpm test       # Vitest
pnpm build      # build de producción con el service worker
pnpm bundle     # tamaño del bundle inicial (gzip); falla por encima de 200 KB
pnpm e2e        # Playwright contra el AppHost corriendo
pnpm iconos     # regenera public/iconos desde src/marca (solo si cambia la marca)
```

En desarrollo, Vite hace de proxy de `/api`, `/hubs`, `/connect`, `/.well-known` y las rutas de OIDC hacia `atlas-api`, para que la PWA y la API compartan origen (ATLAS-ADR-0008). El service worker solo existe en el build de producción.

## Shell

- **Móvil (< 768 px)**: barra superior y barra inferior (Inicio, Buscar, Soporte, Asistente y Más). «Más» es una hoja inferior con el resto de módulos, la cuenta, el tema y el cierre de sesión. Áreas seguras con `env(safe-area-inset-*)`.
- **Escritorio**: barra lateral, buscador con paleta ⌘K / Ctrl+K, cambio de tema, notificaciones y menú de la cuenta.
- **Inicio por rol**: saludo con los roles de `/api/v1/plataforma/yo`, accesos rápidos y los widgets que aporta cada módulo visible.
- **Centro de notificaciones** (`/notificaciones`) y **Ajustes** (`/ajustes`: tema, push en este dispositivo, preferencias por tipo y cuenta).
- **Sin conexión**: aviso con la fecha de lo último guardado; la sesión y las banderas se guardan 24 h en IndexedDB.
- **Nueva versión**: el service worker nuevo espera; el aviso «Hay una nueva versión» lo activa cuando la persona acepta.
- **«Algo salió mal»** (`ErrorDeRuta`, `src/app/errores.ts`): si no se pudo descargar el chunk de una pantalla (lo normal tras un despliegue), recarga sola una vez por pestaña (marca en `sessionStorage`, 10 minutos) y, si vuelve a fallar, ofrece recargar. Los demás errores se envían a `POST /api/v1/plataforma/errores-cliente` (solo datos técnicos y la ruta sin consulta ni fragmento) **si hay sesión**: en las páginas públicas (`/invitacion`, `/recuperar`, `/emergencia`) o antes de entrar no se envía nada. En ambos casos la persona ve una referencia de ocho caracteres para citarla y el botón de recargar.
- **Cerrar sesión** quita la suscripción push del dispositivo, borra IndexedDB y las cachés, y revoca la sesión.

## Módulos

Cada `src/modulos/<id>/modulo.ts` exporta un `ModuloAtlas` (prompt maestro §10) y se agrega a `MODULOS_REGISTRADOS`. Reglas (`resolver.ts`):

- Bandera: la de `GET /api/v1/plataforma/banderas` (tabla `plataforma_modulo`) o, si el servidor no la define, `banderaPorDefecto`. Con la bandera apagada, el módulo desaparece de la navegación, de Inicio y de la búsqueda, y su ruta responde «Esta página no existe».
- Permiso: mientras `/yo` no traiga `permisos` (llegan con la HU 1.4) no se filtra. Con la lista, se filtran rutas, entradas y widgets por su `permiso`.
- El Catálogo ya está disponible (HU 2.1); los demás módulos del MVP (Equipo, Soporte, Contratos, Documentación y Asistente) están en `estado: 'proximamente'` con su página «Próximamente».

Para apagar un módulo en un ambiente: `INSERT INTO atlas.plataforma_modulo (id, activo, organizaciones_habilitadas, actualizado_en) VALUES ('contratos', false, '{}', now());`.

## Seguridad

- CSP de `deploy/atlas-seguridad.conf`: sin scripts ni estilos en línea. El tema se aplica antes de pintar con `public/tema.js` (archivo aparte) y no se usan componentes que inyecten `<style>` (ATLAS-ADR-0020). `tests/e2e/csp.spec.ts` lo comprueba contra la imagen nginx.
- Visor de contratos con Scalar (ATLAS-ADR-0039): build aparte (`vite.visor.config.ts`, lo corre `pnpm build`) en `dist/visor/`, cargado solo en un iframe con `sandbox="allow-scripts"`. Su CSP (`location /visor/` de `deploy/nginx.conf.template`) es la única con estilos en línea y solo conecta a `ATLAS_VISOR_CONEXIONES` (hosts de QA y sandbox, por defecto `https://*.qa.flitsas.online`). Ni Scalar ni Vue entran al bundle de la PWA.
- La única clave en `localStorage` es la preferencia de tema (`atlas.tema`); la sesión vive solo en la cookie `HttpOnly`.

## Pruebas de extremo a extremo

Con el AppHost corriendo (`aspire/README.md`):

```bash
pnpm exec playwright install chromium webkit   # una vez
pnpm e2e                                        # sesión, shell, accesibilidad (axe)
ATLAS_E2E_URL=http://localhost:5032 pnpm e2e csp          # CSP contra la imagen nginx
ATLAS_CAPTURAS=<carpeta fuera del repo> pnpm e2e capturas # capturas para el PR
```

Para la imagen nginx en local: `docker build -t atlas-web .` y `docker run --rm -p 5032:5032 -e ATLAS_API_URL=http://host.docker.internal:4031 atlas-web`.
