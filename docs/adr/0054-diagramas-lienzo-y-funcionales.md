---
id: ATLAS-ADR-0054
titulo: Diagramas — lienzo propio, Mermaid y BPMN versionados, sin relajar la CSP
estado: propuesto
fecha: 2026-11-06
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-04, §7.1, §7.4, §8.6, §9 y §10, documento de herramientas casos 1 y 2, ATLAS-ADR-0016, ATLAS-ADR-0039, ATLAS-ADR-0048, ATLAS-ADR-0053]
---

# ATLAS-ADR-0054 · Diagramas: lienzo, Mermaid y BPMN

## Contexto y problema

La F6 trae dos cosas distintas: la arquitectura como código (LikeC4, generador, drift) y los diagramas que no viven en el
código (lienzo, Mermaid y BPMN). Este ADR cubre solo la segunda (HU 6.6 y 6.7) y su conexión con `<Diagrama/>` de la
documentación. La primera tocará `flit`, `flito` y `flit-gitops`, y tendrá su propio ADR.

Atlas corre con una CSP estricta en el shell (`script-src 'self'; style-src 'self'`, ATLAS-ADR-0048), y las librerías de
diagramas suelen inyectar estilos. Además, el XML de BPMN y el texto de Mermaid los escribe una persona y los abre otra.

## Factores de decisión

- No se relaja la CSP del shell.
- Agregar un módulo no toca a los demás, y los módulos no se importan entre sí (§10).
- Seguro por defecto: visibilidad en SQL y contenido limpiado en el servidor (§2 y §12).
- Un cambio en los permisos de §7.4 es una decisión del líder (§1, regla 4).

## Decisión

### 1. Módulo, tablas y permisos

- `Flit.Atlas.Modulos.Diagramas`, con id `diagramas`, rutas `/api/v1/diagramas` y bandera `modulo.diagramas`, encendida.
- **Prefijo de tablas `diagramas_`, no `dgm_`.** El contrato de módulo deriva el prefijo del id (ATLAS-ADR-0016), como
  `documentacion_` frente a `doc_` (ATLAS-ADR-0053 §1).
- **El manifiesto sigue en el núcleo.** `ExtensionesModulos.Validar` exige que los permisos de un módulo empiecen por
  `atlas.<id>.`, y `atlas.arquitectura.*` no cumple. Su id pasa de `diagramas` a `arquitectura`, para no chocar con el
  manifiesto vacío del módulo, y los códigos y las concesiones no cambian. Es la misma salida de la F5.
- **Leer exige `atlas.docs.ver`; escribir, `atlas.diagramas.editar`** global o sobre el aplicativo (rol implícito del
  responsable). `atlas.arquitectura.ver` queda para las vistas de LikeC4: es solo de roles técnicos y del asesor comercial,
  y con él negocio y soporte no verían un proceso incrustado en su propio manual. No se crea `atlas.diagramas.ver` porque
  §7.4 no lo tiene y agregarlo cambia el modelo de permisos. Ver «Por verificar».

### 2. Modelo

- `diagramas_diagrama`: identidad estable. Código único (el `id` de `<Diagrama/>`), aplicativo, tipo (`lienzo`, `mermaid` o
  `bpmn`), y la foto de título y visibilidad de su última versión. El tipo, el código y el aplicativo no cambian.
- `diagramas_version`: **inmutable** por disparador. Guarda la fuente, foto, huella, autor y nota. Restaurar crea una versión
  nueva igual a la elegida. No hay borradores ni aprobación: lo guardado es lo que se lee.
- `diagramas_nodo` y `diagramas_relacion`: proyección de cada versión del lienzo, para consultar sin leer JSON. Es lo que
  permite que la ficha de un aplicativo liste los diagramas que lo mencionan.
- `diagramas_comentario`: por versión y, si se quiere, por nodo. Comenta quien puede leer el diagrama.

### 3. Visibilidad

- Se aplica en SQL en cada consulta, incluida la búsqueda, con los niveles y roles de ATLAS-ADR-0053 §6. Cada versión guarda
  la suya.
- Se aceptan `interno_negocio`, `interno_ti` y `restringido`. **`publico_clientes` se rechaza por ahora**: §7.2 pide
  aprobación del responsable y aquí no hay flujo de aprobación. Compartir con externos, igual que en la documentación, va al
  final por decisión del líder (06/11). Ningún externo ve diagramas.

### 4. Validación en el servidor

- **BPMN:** `XmlReader` con DTD prohibida y sin resolver, para cerrar XXE y expansión de entidades. Se exige
  `bpmn:definitions`, un proceso o colaboración, y un `bpmndi:BPMNDiagram`. La limpieza quita comentarios, instrucciones de
  proceso, elementos y atributos de espacios de nombres ajenos a BPMN y DD, `extensionElements`, `import`, `script`
  (la tarea queda, sin su código), atributos `on*` y `xsi:schemaLocation`. Se guarda lo limpio, y sobre eso va la huella.
- **Lienzo:** JSON estricto, con propiedades desconocidas rechazadas, topes de nodos y relaciones, ids con formato, relaciones
  con extremos existentes y aplicativos que existan en el catálogo.
- **Mermaid:** tope de 20 mil caracteres, el mismo del visor. Corre aislado (§5), y el servidor no intenta analizarlo.
- Los eventos `atlas.diagramas.*` no se crean todavía: nadie los consume. Llegarán con el indexador del asistente (F7).

### 5. Librerías y CSP

| Librería | Versión | Licencia | Dónde corre | Motivo |
|---|---|---|---|---|
| `@xyflow/react` | 12.12.0 | MIT | Shell, chunk diferido del editor | Sus estilos son un `.css` estático, y los de posición van por CSSOM, que `style-src-attr` no bloquea. Lo comprueba la E2E con la CSP real |
| `elkjs` | 0.12.0 | EPL-2.0 o GPL-3.0 | Shell, solo al pulsar «Acomodar» | `elk.bundled.js` corre en el hilo principal sin `Worker` |
| `bpmn-js` | 18.31.1 | bpmn.io (conservar la marca) | `/visor/bpmn.html`, iframe con `sandbox="allow-scripts"` | Inyecta estilos en línea; se aísla como Scalar (ATLAS-ADR-0039) y Mermaid (ATLAS-ADR-0053 §5) |
| `mermaid` | 12.2.0 | MIT | `/visor/mermaid.html` | Ya estaba |
| `oss.terrastruct.com/d2` | 0.7.2 | MPL-2.0 | atlas-render (Go), sin navegador | Exporte a SVG (§7) |

- La marca de bpmn.io es la propia de `bpmn-js`, sin ocultar. El visor no tiene cookie ni acceso a la API (origen opaco) y solo
  intercambia XML con quien lo enmarca por `postMessage`.
- **Si `@xyflow/react` o `elkjs` no pasan con la CSP real, no se relaja:** van a un visor aislado como `bpmn-js`.
- La lectura del lienzo es un SVG propio, sin React Flow. Pesa poco, no necesita estilos en línea y `<Diagrama/>` no carga el
  editor.
- **Comprobado con la imagen nginx y su CSP** en Chromium y WebKit: `@xyflow/react` y `elkjs` corren en el shell sin
  violaciones, y no hizo falta el visor aislado. La marca «React Flow» del lienzo se conserva.
- La entrada del menú es solo para empleados: un externo tiene `atlas.docs.ver`, pero no ve ningún diagrama.

### 6. `<Diagrama id="…"/>`

El visor Mermaid pasa de `modulos/documentacion` a `app/diagramas`, junto con la vista por tipo, porque un módulo no importa a
otro. `<Diagrama/>` pide `GET /api/v1/diagramas/por-codigo/{codigo}` con la sesión de quien lee. Sin permiso o sin diagrama,
muestra «No tienes acceso a este contenido»; no revela cuál de los dos es. Los exportes de documentos conservan el texto
«disponible en Atlas» de ATLAS-ADR-0053.

### 7. Exportar el lienzo a SVG (HU 6.8)

- **atlas-render recibe el modelo, no texto D2.** Los nodos se llaman `n0`, `n1`… y las etiquetas van entre comillas simples,
  así que nadie puede usar imports, enlaces ni variables de D2. D2 0.7.2 se enlaza como librería, sin leer archivos, y su logger
  se descarta porque puede citar etiquetas.
- **El SVG se limpia con una lista blanca** de elementos, sin `script`, `foreignObject`, `a`, `image` ni `use`. Quita `on*` y los
  `href` que no sean `#`. El `<style>` solo admite `url()` con fuentes `data:` y referencias internas; si trae otra cosa, se
  rechaza el SVG entero.
- **Caché en atlas-render** por el hash del modelo y la versión de D2, en memoria, por réplica.
- **Sin PNG.** `d2` lo genera con Playwright y un navegador, lo que A-02 no admite en producción. No se evaluó a fondo un
  rasterizador en Go puro, que tendría que pintar el texto con las fuentes incrustadas. Queda fuera hasta que el líder lo pida.
- **Solo lienzos.** Mermaid y BPMN no pasan por D2.
- **D2 acomoda de nuevo:** el archivo no conserva las posiciones del lienzo. Lo dice la página. Para eso sirve D2 aquí; si se
  necesitan las posiciones, habría que generar el SVG desde `LienzoSvg`.
- `GET /api/v1/diagramas/{id}/versiones/{numero}/svg` exige `atlas.docs.ver`, la misma visibilidad de leer esa versión, y queda
  auditado (`diagramas.exportado`). No exige `atlas.docs.exportar`: quien lee ya recibe la fuente.
- **atlas-render aún no está desplegado en QA ni en pdn:** no tiene Deployment ni entrada en `kustomization.yaml`. El endpoint
  responde 503 `render_no_disponible` y la página lo explica, sin afectar la lectura; el aviso puede tardar varios segundos por
  los reintentos del cliente HTTP. `cd.yml` publica la imagen y fija su digest solo si el ambiente declara la entrada.
- **Sin token de servicio:** como el diff (ATLAS-ADR-0036), el endpoint depende de la política de red, y el token de §5.1 sigue
  pendiente para todas las llamadas a atlas-render. Va con los pendientes de seguridad.

## Consecuencias

- Buenas: la CSP del shell no cambia; el XML inseguro no llega a la base; un diagrama se lee igual en su página y en un
  documento.
- Malas: una lectura de BPMN carga un iframe y su visor. Lo que quite la limpieza no se recupera, incluidos los
  `extensionElements` de otras herramientas.
- A vigilar: tamaño del chunk de ELK y tiempo de acomodo con diagramas grandes.

## Opciones descartadas

- **Relajar `style-src` en el shell:** abre inyección de estilos en toda la PWA por una sola pantalla.
- **Guardar el lienzo solo como JSON:** no permite saber qué diagramas mencionan un aplicativo sin leer cada uno.
- **Quitar del XML solo lo peligroso conocido:** una lista negra envejece mal; se conserva solo lo del estándar.
- **Crear `atlas.diagramas.ver`:** es más limpio, pero cambia §7.4, así que lo decide el líder.

## Por verificar

- **[por verificar con el líder]** Leer con `atlas.docs.ver` o crear `atlas.diagramas.ver`.
- **[por verificar con el líder]** Licencia de `elkjs` (EPL-2.0 o GPL-3.0): es copyleft débil. Se usa sin modificar; A-04 la
  nombra, pero el aviso de licencia y su aprobación quedan a su cargo.
- **[por verificar con el líder]** Licencia de D2 (MPL-2.0, copyleft por archivo): se usa sin modificar.
- **[por verificar con el líder]** Si hace falta PNG, y si el SVG debe conservar las posiciones del lienzo.
- **[por verificar]** La `@xyflow/react` 12.12.1 y la `elkjs` 0.12.1 se publicaron hace menos de dos semanas: se fijan las
  anteriores.
