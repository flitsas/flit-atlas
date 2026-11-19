---
id: ATLAS-ADR-0039
titulo: Vista de contratos — Scalar en un iframe aislado con CSP propia, vista propia de operaciones, AsyncAPI y proto, ejemplos con httpsnippet y diff desde atlas-render
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-02, A-09, §5.2, §8.4, §11 y §12, plan 00 §8.5 y §8.9, HU 3.6, HU 3.9, ATLAS-ADR-0020, ATLAS-ADR-0026, ATLAS-ADR-0035, ATLAS-ADR-0036, ATLAS-ADR-0037]
---

# ATLAS-ADR-0039 · Vista de contratos

## Contexto y problema

La HU 3.6 pide la pantalla del contrato (`/contratos/:id` y `/contratos/:id/v/:version`): OpenAPI con Scalar y su
consola «Probar» solo hacia sandbox o QA, AsyncAPI legible, proto, ejemplos con httpsnippet en curl, C#, Python, Go y
JS con variables, línea de tiempo de versiones con diff, guías, paquetes, canales y una vista móvil usable (operaciones
como lista y detalle en hoja inferior, sin scroll horizontal a 360 px). La HU 3.9 pide la matriz de obligatoriedad
(`x-flit-obligatoriedad`) y las tablas de referencia (`enum` + `x-enum-descriptions`) en esa misma vista. Todo con la
CSP del shell (`script-src 'self'; style-src 'self'`, sin `unsafe-inline` ni `unsafe-eval`, ATLAS-ADR-0020) y el
bundle inicial por debajo de 200 KB gzip.

Hechos verificados el 24/10/2026 con `@scalar/api-reference` 1.73.1 (07/10/2026, MIT; la última con al menos dos
semanas) bajo esa CSP, en Chromium con Playwright y en la imagen nginx de atlas-web:

- Scalar inyecta `<style>` en tiempo de ejecución (el tema y los estilos de sus componentes, con `@unhead/vue`): con
  `style-src 'self'` el visor sale sin estilos. No necesita `unsafe-eval`: lo único que prueba `new Function` es Zod
  (para compilar validadores), que se desactiva con su modo `jitless` sin perder la validación.
- Con su configuración por defecto, Scalar carga fuentes de `fonts.scalar.com`, usa `proxy.scalar.com` para «Probar»
  y trae el agente, MCP y la telemetría de scalar.com.
- Scalar más Vue, CodeMirror y sus dependencias pesan ~4,6 MB sin comprimir (el chunk mayor, ~470 KB gzip).
- `@asyncapi/react-component` 3.2.1 (17/09/2026, Apache-2.0) trae el parser oficial, que compila esquemas con Ajv
  (`new Function`): no cabe en la CSP sin `unsafe-eval` y pesa varios MB.
- `httpsnippet` (Kong) dejó de publicar en su línea 3; su sucesor mantenido es `@readme/httpsnippet` 11.4.1
  (05/10/2026, MIT), que importa el módulo `url` de Node (se cubre con el paquete `url` 0.11.4, MIT).
- Con origen opaco (iframe con `sandbox` sin `allow-same-origin`), leer `localStorage` lanza `SecurityError` y Scalar
  no arranca; sus módulos se piden con CORS y `Origin: null`.

## Factores de decisión

- La CSP del shell no se debilita (§12): ni `unsafe-inline` en estilos ni `unsafe-eval`.
- «Probar» nunca apunta a producción, aunque el spec traiga el servidor de pdn o la persona edite el host.
- Nada sale a terceros desde la vista (ni fuentes, ni proxy, ni telemetría).
- El contenido del spec viene de repos: si algo del visor se rompe o se abusa, no debe alcanzar la sesión del BFF.
- Móvil primero y bundle inicial < 200 KB gzip (§11).

## Opciones consideradas

### Scalar

1. Scalar directo en la PWA, agregando `'unsafe-inline'` a `style-src` del shell.
2. Nonces de CSP generados por nginx (`sub_filter`) y pasados a Scalar y a sus librerías.
3. **Scalar en una página aparte (`/visor/openapi.html`) cargada en un iframe con `sandbox="allow-scripts"` y su
   propia CSP**, que es la única que permite estilos en línea.
4. Sin Scalar: solo la vista propia.

### AsyncAPI

1. `@asyncapi/react-component` con `unsafe-eval`.
2. **Vista propia simple** sobre el YAML (canales, operaciones, mensajes y esquemas, 2.x y 3.x).

## Decisión

**Scalar: opción 3.** **AsyncAPI: opción 2.**

### Visor aislado

- Build propio (`vite.visor.config.ts`, después del de la PWA en `pnpm build`) en `dist/visor/`: no comparte chunks
  con la PWA, no entra al bundle inicial ni a la precarga del service worker (que además excluye `/visor/` de su ruta
  de navegación).
- La pantalla lo monta solo en escritorio (≥ 1024 px), en la pestaña «Referencia interactiva», en un iframe con
  `sandbox="allow-scripts"` y `referrerpolicy="no-referrer"`: origen opaco, sin la cookie del BFF (que además es
  `SameSite=Strict`), sin acceso a la API ni al DOM de Atlas. En el móvil se usa la vista propia.
- La pantalla le entrega el spec por `postMessage` (el visor no tiene a quién pedírselo); el visor solo acepta mensajes
  de su ventana padre con el origen de Atlas y valida su forma.
- **CSP de `/visor/`** (location propia en `deploy/nginx.conf.template`): `default-src 'none'; script-src 'self';
  style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src ${ATLAS_VISOR_CONEXIONES};
  frame-ancestors 'self'; base-uri 'none'; form-action 'none'; object-src 'none'; sandbox allow-scripts`. La directiva
  `sandbox` también va por cabecera: abierto suelto, el visor igual queda con origen opaco. `ATLAS_VISOR_CONEXIONES`
  (por defecto `https://*.qa.flitsas.online`) lista los hosts de QA y sandbox a los que llama «Probar»; los archivos de
  `/visor/` llevan `Access-Control-Allow-Origin: *` (son estáticos y públicos) porque el documento de origen opaco los
  pide con CORS. La CSP del shell no cambia (`default-src 'self'` ya permite el iframe del mismo origen).
- **Solo QA y sandbox:** `servidoresPermitidos` (función pura, con pruebas) se queda con los servidores `https` cuyo
  `x-flit-ambiente` es `qa` o `sandbox` o, sin esa extensión, cuyo host dice `qa` o `sandbox`; descarta pdn, dev,
  rutas relativas y variables sin valor, y resuelve las variables con su valor por defecto para que la persona no
  pueda cambiar el host. El visor recibe esa lista como `servers`, su `customFetch` rechaza cualquier URL fuera de ella
  (y nunca manda cookies) y la CSP es la tercera barrera. Sin servidores permitidos, «Probar» no aparece.
- Configuración de Scalar: `proxyUrl` vacío, `withDefaultFonts: false` (tipografía del sistema), `telemetry: false`,
  agente y MCP desactivados, sin botón de cliente ni herramientas de desarrollo, descarga y cambio de tema ocultos (los
  da Atlas), y `localStorage`/`sessionStorage` en memoria. Los grises de su tema se oscurecen y el acento es el de la
  marca para pasar el contraste AA que revisa axe.

### Vista propia (todas las pantallas y el móvil)

- La pantalla lee el spec (`GET …/versiones/{v}/spec`) con `yaml` 2.9.1 (ISC; tope de alias) y arma: el catálogo de
  operaciones con búsqueda y filtros por método y etiqueta, y la ficha de cada una (cabeceras, parámetros, cuerpo,
  ejemplos por lenguaje con «Copiar», respuesta de ejemplo y tabla de errores); en escritorio va al lado de la lista,
  en el móvil en la hoja inferior del shell. Las tablas se vuelven tarjetas en el móvil. Las referencias externas
  (`$ref` a URL) no se siguen.
- **Ejemplos** con `@readme/httpsnippet` 11.4.1, en su propio chunk (solo al abrir una operación): `{{baseUrl}}`
  (`{{urlWebhook}}` en un webhook), `Bearer $TOKEN`, `$API_KEY` y `{{parámetro}}`; el cuerpo es el ejemplo del spec, que
  ya viene saneado (ATLAS-ADR-0037).
- **AsyncAPI** 2.x y 3.x: canales, operaciones, mensajes con sus campos y un ejemplo, y esquemas. **Proto**: servicios
  y rpc (del texto y del índice de operaciones del servidor), mensajes y enums con sus campos, y el archivo completo.
- **Matriz de obligatoriedad** (HU 3.9): cada `x-flit-obligatoriedad` de `components.schemas` (filas por `campos` o por
  `documentos`; celda que falta = no aplica), con filtro por tipo de trámite y búsqueda; tabla en escritorio y, en el
  móvil, lista por tipo de trámite agrupada en obligatorios, opcionales y no aplica (plegado). Cada celda lleva ícono y
  texto, no solo color. **Tablas de referencia**: los esquemas con `enum` y `x-enum-descriptions` (título de
  `x-flit-tabla`), con búsqueda en todas. Ambas se exportan a CSV (BOM, `;`, y las celdas que empiezan por `=`, `+`, `-`
  o `@` se neutralizan contra la inyección de fórmulas).
- **Versiones**: línea de tiempo con fecha, repo, rama, workflow, commit y «Breaking: sí/no», y el diff entre dos
  versiones cualesquiera con el endpoint nuevo `GET /api/v1/contratos/{id}/diff?desde=&hasta=` (abajo), lo que rompe
  primero y con su nivel.
- **Guías**: punto de montaje por nombre de archivo (`import.meta.glob('../guias/GuiasDelContrato.tsx')`): si la HU 3.7
  deja ese componente (`export default`, props `{ contratoId, version, tipo }`), se carga diferido; si no, «Próximamente».
  **Canales** (HU 3.4) y **Paquetes** (F4) quedan como marcadores.

### API

- `GET /api/v1/contratos/{id}`: la ficha del contrato (la misma fila del listado), para abrir la pantalla por enlace.
- `GET /api/v1/contratos/{id}/diff?desde=&hasta=`: lee las dos specs y llama a `IRenderDiff` (ATLAS-ADR-0036); la caché
  es la de atlas-render (por el par de SHA-256), y como las versiones son inmutables la respuesta lleva
  `Cache-Control: private, max-age=3600`. Une el changelog y `breaking` sin repetir (por la huella de oasdiff) y ordena
  lo incompatible primero, de error a info. 400 si falta una versión; 404 si el contrato o una versión no existe; 422
  `diff_no_soportado` (AsyncAPI y proto, sin llamar a atlas-render), `spec_invalida` o `diff_rechazado`; 503
  `render_no_disponible`. Ambos con `atlas.contratos.ver_internos`; `AutorizacionTests` los recorre con todos los roles.

## Consecuencias

### Positivas

- La CSP del shell sigue sin excepciones; lo permisivo queda encerrado en un documento sin sesión ni acceso a la API.
- «Probar» tiene tres barreras contra producción (lista filtrada sin variables, `customFetch` y `connect-src`).
- El bundle inicial no cambia por Scalar ni por httpsnippet (174 KB gzip con la pantalla nueva).
- La vista propia sirve igual en el móvil, en escritorio y para AsyncAPI y proto, y es la base del portal (F4).

### Negativas

- Dos visores de OpenAPI que mantener (Scalar y la vista propia), y un build más en `pnpm build`.
- `style-src 'unsafe-inline'` en `/visor/`: aceptable porque el documento no tiene secretos y no puede enviar nada
  fuera de `self` y de los hosts de QA y sandbox.
- «Probar» desde el navegador necesita que el host de QA o sandbox permita CORS con `Origin: null` **[por verificar]**;
  si no lo permite, la consola falla (sin exponer nada) hasta que exista el sandbox de Microcks (plan 00 §8.8) o un
  reenvío propio en atlas-api.
- Varias dependencias transitivas de Scalar (Vue 3.5.44, CodeMirror, Zod…) se publicaron hace menos de dos semanas:
  quedan fijadas con `overrides` en `apps/web/pnpm-workspace.yaml` a su versión madura y hay que revisarlas al subir
  Scalar.
- La vista propia no valida el spec ni sigue `$ref` externas; la validación completa sigue en la CI (vacuum) y al
  publicar (ATLAS-ADR-0035).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| `'unsafe-inline'` en el shell | Debilita la CSP de toda la PWA por un componente |
| Nonces con `sub_filter` | Frágil: cada librería (Vue/unhead, CodeMirror) tendría que tomar el nonce, y el service worker cachea `index.html` |
| Sin Scalar | La HU lo pide para escritorio y su consola es lo que más ayuda a probar contra QA |
| `@asyncapi/react-component` | Necesita `unsafe-eval` (Ajv) y pesa varios MB |
| `httpsnippet` de Kong | Sin publicaciones recientes; `@readme/httpsnippet` es el sucesor mantenido |

## Más información

- [ATLAS-ADR-0020](0020-shell-pwa-ui-banderas-y-web-push.md) (CSP del shell), [ATLAS-ADR-0035](0035-publicacion-de-contratos.md)
  (lectura de specs), [ATLAS-ADR-0036](0036-diff-de-contratos-en-render.md) (`IRenderDiff`),
  [ATLAS-ADR-0037](0037-conversion-del-legado-ict.md) (extensiones del legado ICT).
- Pendientes **[por verificar]**: hosts definitivos de QA y del sandbox para `ATLAS_VISOR_CONEXIONES`; CORS de esos
  hosts; vista de «flujo» (petición → respuesta → webhook) cuando un spec lo describa completo (F5/F6).
