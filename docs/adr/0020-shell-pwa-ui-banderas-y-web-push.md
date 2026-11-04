---
id: ATLAS-ADR-0020
titulo: Shell PWA — Tailwind con tokens FLIT y componentes compatibles con la CSP, banderas de módulo y Web Push con Lib.Net.Http.WebPush
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §5.2, §8.9, §10, §11 y §12, HU 1.6, ATLAS-ADR-0003, ATLAS-ADR-0015, ATLAS-ADR-0016]
---

# ATLAS-ADR-0020 · Shell PWA — Tailwind con tokens FLIT, banderas de módulo y Web Push

## Contexto y problema

La HU 1.6 arma el shell móvil primero de la PWA: navegación (barra inferior y lateral), tema, registro de módulos con carga diferida y banderas, Inicio, búsqueda, centro de notificaciones, Web Push, aviso de nueva versión e indicador sin conexión. El prompt maestro §5.2 fija el stack general (React Router, TanStack Query, `vite-plugin-pwa`, Tailwind + Radix/shadcn, `idb`), pero quedan decisiones abiertas:

1. La CSP de `atlas-web` (HU 1.8) es `script-src 'self'; style-src 'self'`, **sin `unsafe-inline` también en estilos**. Varias librerías de UI inyectan `<style>` en tiempo de ejecución: entre ellas el bloqueo de scroll de los diálogos modales de Radix (`react-remove-scroll` → `react-style-singleton`).
2. Cómo se resuelven las banderas `modulo.<id>` en la PWA mientras el servidor aún no siembra `plataforma_modulo`, y cómo se filtra por permiso antes de que `/yo` traiga permisos (HU 1.4).
3. Qué librería de Web Push (VAPID) usa .NET.
4. Cómo se replica la marca FLIT (ATLAS-ADR-0015) sin red en el CI, y qué logotipo usa Atlas.
5. Qué estrategia de service worker permite push y el aviso «Hay una nueva versión».

Verificado el 22/10/2026: versiones con al menos dos semanas de publicadas y licencias permisivas (abajo).

## Factores de decisión

- CSP estricta sin excepciones (prompt maestro §12).
- Bundle inicial < 200 KB gzip, carga diferida por módulo (§11).
- Accesibilidad WCAG 2.1 AA y objetivos táctiles ≥ 44 px.
- El servidor es la autoridad de banderas y permisos (ATLAS-ADR-0016); la PWA solo oculta.
- No escribir criptografía propia (§6, regla del módulo Identidad).
- Atlas hace `POST` a la URL de la suscripción push, que llega del cliente: riesgo de SSRF.

## Opciones consideradas

### UI

1. Tailwind v4 con los tokens FLIT replicados, **Radix solo donde no inyecta estilos** (menú de la cuenta, `modal={false}`) y `<dialog>` nativo para hojas inferiores, «Más» y la paleta ⌘K.
2. shadcn/ui completo (Radix Dialog modal, Sheet, Command).
3. Agregar `'unsafe-inline'` o hashes a `style-src`.

### Banderas y permisos en la PWA

1. `GET /api/v1/plataforma/banderas` (tabla `plataforma_modulo`, filtrada por organización) con **valor por defecto en cada manifiesto** (`banderaPorDefecto`) para lo que el servidor no defina; permisos opcionales en `/yo`.
2. Banderas en tiempo de compilación (variables de Vite).
3. Esperar la HU 1.4 para mostrar módulos.

### Web Push en .NET

1. **Lib.Net.Http.WebPush 3.3.1** (MIT, de Tomasz Pęczek; en net6+ usa `System.Security.Cryptography`, sin BouncyCastle).
2. `WebPush` (web-push-csharp 1.0.13): depende de `Portable.BouncyCastle`, paquete sin mantenimiento.
3. Implementar VAPID y aes128gcm a mano.

### Service worker

1. `vite-plugin-pwa` 2.0.0 con **`injectManifest`** (SW propio en `src/pwa/sw.ts` con `workbox-precaching` y `workbox-routing` 7.4.1), `registerType: 'prompt'` e `injectRegister: false`.
2. `generateSW` con `importScripts` para el push.

## Decisión

**UI: opción 1.** Tailwind 4.3.3 (`@tailwindcss/vite`), los tokens de `@flit/ui` replicados, íconos `lucide-react` 1.52.0 (se empaquetan solo los usados) y Poppins de `@fontsource/poppins` 5.3.0 (OFL-1.1, solo el subconjunto latino, servida desde el propio origen: `font-src 'self'`). Radix (`@radix-ui/react-dropdown-menu` 2.1.25) solo con `modal={false}`; los diálogos son `<dialog>` nativos (foco atrapado, Escape y fondo inerte del navegador). Los estilos que ponen React y Floating UI van por CSSOM (`element.style`), que la CSP no bloquea. Lo comprueba `tests/e2e/csp.spec.ts` contra la imagen nginx.

**Marca.** Se mantiene la réplica de ATLAS-ADR-0015, con dos ajustes:

- El script vive en `scripts/marca/sincronizar.mjs` (no en `tools/marca/`) y copia desde un **clon local** de `flit` en el commit declarado en `apps/web/src/marca/origen.json`, donde anota el sha de blob de git de cada archivo. `--verificar` recalcula esos sha **sin red**; el job `web` del CI lo corre. El sha de blob es el mismo que muestra GitHub para ese commit, así que se audita con `gh api repos/flitsas/flit/git/trees/<sha>`.
- Se replican `tokens.css`, `tokens.ts` y los dos isotipos. No se replican `derive-*` (Atlas no tiene marca blanca) ni el logotipo de FLIT 2.0, que trae «Versión 2.0»: Atlas usa el **isotipo FLIT con el nombre «Flit Atlas»** en Poppins. Íconos, maskable, atajos e imágenes de arranque de iOS los genera `pnpm iconos` con el Chromium de Playwright (sin `sharp`) y quedan versionados.

**Banderas y permisos: opción 1.** `banderaActiva = servidor[modulo.<id>] ?? manifiesto.banderaPorDefecto`. Los módulos del MVP (Catálogo, Equipo, Soporte, Contratos, Documentación, Asistente) están registrados con `estado: 'proximamente'` y `banderaPorDefecto: true`: la navegación está completa y cada uno lleva a una página «Próximamente». Apagar uno es insertar `plataforma_modulo (id, activo = false)`. `tienePermiso` devuelve `true` mientras `/yo` no traiga `permisos`; cuando la HU 1.4 los agregue, el mismo código filtra rutas, entradas y widgets sin más cambios. Todas las rutas de los módulos registrados existen en el enrutador y un envoltorio decide: bandera apagada → 404, sin permiso → «No tienes acceso»; el chunk del módulo solo se descarga si se muestra.

**Web Push: opción 1.** Llaves VAPID en `Plataforma__Push__ClavePublica` y `Plataforma__Push__ClavePrivada` (SealedSecret en qa y pdn; user secrets del AppHost en local, o efímeras generadas al arrancar en local y dev). Sin llaves en qa o pdn, push queda deshabilitado con un aviso en el log y las notificaciones siguen llegando al centro de notificaciones (no se rompe el despliegue mientras se crea el SealedSecret). Contra SSRF, el endpoint de una suscripción debe ser `https` de un servicio de push conocido (FCM, Mozilla, Apple, WNS; configurable con `Plataforma__Push__HostsPermitidos`). Las respuestas 400/401/403/404/410 borran la suscripción; cinco fallos temporales seguidos, también.

**Service worker: opción 1.** Precarga solo el shell (no guarda respuestas de la API), muestra los push, maneja el clic de una notificación (solo rutas internas) y espera `SKIP_WAITING` para activar una versión nueva cuando la persona acepta. Los datos sin conexión van en IndexedDB (`idb` 8.0.4) con TTL y se borran al cerrar sesión.

**Cierre de sesión**: quita la suscripción push del dispositivo (en el navegador y en `plataforma_suscripcion_push`), borra IndexedDB y las cachés, y revoca la sesión; la API además envía `Clear-Site-Data`. La preferencia de tema (`atlas.tema` en `localStorage`) se conserva: no es un dato de la sesión.

Otras piezas fijadas: `react-router` 8.4.0, `@tanstack/react-query` 5.104.1, `workbox-window` 7.4.1, `@axe-core/playwright` 4.13.0 (MPL-2.0, solo en pruebas). Todas MIT, ISC, Apache-2.0 u OFL salvo la indicada.

## Consecuencias

### Positivas

- Cero excepciones en la CSP y una prueba E2E que lo vigila.
- Navegación completa desde F1 sin código de los módulos: cada fase solo cambia el `estado` y las páginas de su módulo.
- La HU 1.4 solo tiene que agregar `permisos` a `/yo`.
- Web Push sin criptografía propia ni dependencias sin mantenimiento, y sin SSRF por la URL de la suscripción.

### Negativas

- Sin Radix Dialog ni el `Command` de shadcn: la paleta, las hojas y su teclado son propios (pocos cientos de líneas) y hay que mantener su accesibilidad.
- Las llaves efímeras de local invalidan las suscripciones en cada arranque del AppHost (la PWA las renueva).
- El envío de push es en línea dentro de la petición (hoy solo la notificación de prueba). **[pendiente]** Con los recordatorios de soporte (F2) pasa a un consumidor de la outbox.
- Lib.Net.Http.WebPush publicó su última versión el 09/03/2025: es madura y estable, pero hay que vigilar su mantenimiento en la revisión trimestral.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| shadcn/ui completo | Dialog, Sheet y Command modales inyectan `<style>`: violan `style-src 'self'` |
| `'unsafe-inline'` o hashes en `style-src` | Debilita la CSP del §12; los `<style>` dinámicos no tienen hash fijo |
| Banderas en compilación | No permiten apagar un módulo por ambiente u organización sin publicar otra imagen |
| Esperar la HU 1.4 | El shell se veía vacío («solo una ventana sin menú»); los permisos se pueden enchufar después |
| web-push-csharp | Depende de `Portable.BouncyCastle`, sin mantenimiento |
| VAPID propio | Criptografía propia, prohibida por el prompt maestro |
| `generateSW` + `importScripts` | El código de push quedaría fuera del bundle tipado y sin pasar por Vite |

## Más información

- `apps/web/README.md` (shell, módulos, PWA, pruebas) y `services/api/README.md` (endpoints y variables de Web Push).
- `scripts/generar-vapid.mjs` genera un par de llaves; `scripts/marca/sincronizar.mjs` sincroniza la marca.
- **[por verificar]** push real en un Android de gama media y en un iPhone con iOS 16.4+ instalado (lo hace el líder en qa).
