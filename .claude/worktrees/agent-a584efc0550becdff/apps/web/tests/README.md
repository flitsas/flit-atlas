# apps/web/tests

- Vitest (`pnpm test`): `src/**/*.test.ts(x)` y `tests/*.test.ts`, con jsdom.
- Playwright (`pnpm e2e`) en tres perfiles: escritorio, Pixel 7 e iPhone 14, contra el AppHost. `e2e/sesion.spec.ts` (cookie de sesión), `e2e/shell.spec.ts` (barras, módulos, tema, búsqueda, notificaciones, 360 px, objetivos táctiles), `e2e/accesibilidad.spec.ts` (axe, claro y oscuro), `e2e/csp.spec.ts` (solo contra la imagen nginx) y `e2e/capturas.spec.ts` (solo con `ATLAS_CAPTURAS`).

Lighthouse móvil se corre a mano sobre la imagen nginx (ver la descripción del PR de la HU 1.6).
