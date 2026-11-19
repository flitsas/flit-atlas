# apps/web/tests

- Vitest (`pnpm test`): `src/**/*.test.ts(x)` y `tests/*.test.ts`, con jsdom.
- Playwright (`pnpm e2e`) en tres perfiles: escritorio, Pixel 7 e iPhone 14, contra el AppHost. `e2e/sesion.spec.ts` (cookie de sesión), `e2e/shell.spec.ts` (barras, módulos, tema, búsqueda, notificaciones, 360 px, objetivos táctiles), `e2e/accesibilidad.spec.ts` (axe, claro y oscuro), `e2e/invitaciones.spec.ts` (HU 1.5: invitar → aceptar → registrar el factor → entrar → vencer, con el autenticador virtual de WebAuthn de Chromium y TOTP en WebKit; lee el correo de Mailpit en `http://localhost:4038`, `ATLAS_E2E_MAILPIT`; guarda capturas con `ATLAS_CAPTURAS`), `e2e/modulos.spec.ts` (matriz rol × permiso y módulo «hola», con la API simulada de `e2e/api-simulada.ts`: no necesita el AppHost), `e2e/sin-conexion.spec.ts` (HU 2.12: con el service worker real y la API simulada, carga en línea, pasa a modo avión con `context.setOffline(true)`, recarga y ve el próximo turno y los responsables de FLITO; comprueba que no se guardan teléfonos y que cerrar sesión borra todo; necesita la PWA construida, p. ej. `vite preview`), `e2e/csp.spec.ts` (solo contra la imagen nginx) y `e2e/capturas.spec.ts` (solo con `ATLAS_CAPTURAS`).

Lighthouse móvil se corre a mano sobre la imagen nginx (ver la descripción del PR de la HU 1.6).

`arquitectura-modulos.test.ts` falla si un módulo de `src/modulos/` importa a otro o si el shell los conoce fuera de `src/app/modulos/registro.ts` (ATLAS-ADR-0026).
