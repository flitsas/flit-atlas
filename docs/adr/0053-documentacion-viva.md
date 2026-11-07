---
id: ATLAS-ADR-0053
titulo: Documentación viva — módulo propio, versiones inmutables desde los repos, revisiones en Atlas y MDX sin evaluar código
estado: propuesto
fecha: 2026-11-05
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §2, §7.1, §8.5, §9 y §10, ATLAS-ADR-0009, ATLAS-ADR-0035, ATLAS-ADR-0040, ATLAS-ADR-0042, ATLAS-ADR-0048, ATLAS-ADR-0052]
---

# ATLAS-ADR-0053 · Documentación viva

## Contexto y problema

La F5 agrega manuales, guías, runbooks, fichas, ADR y notas de versión. Parte del contenido vive en git y llega por CI;
otra parte la escribe negocio en Atlas. Cada documento tiene un nivel de visibilidad, y el contenido puede incrustar
componentes vivos (`<Contrato/>`, `<Responsables/>`) sin que un autor pueda ejecutar código en el navegador de otra
persona. Atlas corre con una CSP estricta, sin `unsafe-eval` (ATLAS-ADR-0048).

## Factores de decisión

- Git es la fuente de verdad de lo que vive en el código; en Atlas solo se edita lo que no vive ahí (prompt maestro §2).
- Seguro por defecto: el filtro de visibilidad va en SQL y el contenido se sanea (§2 y §12).
- Agregar un módulo no toca a los demás (§10).
- Debe servirle a F7: el indexador consumirá eventos `atlas.*.publicada`.

## Decisión

### 1. Módulo

`Flit.Atlas.Modulos.Documentacion`, con id `documentacion`: rutas `/api/v1/documentacion`, bandera
`modulo.documentacion` y permisos `atlas.docs.*` (el manifiesto `ManifiestosNucleo.Documentacion`, que ya existe).

`atlas.docs.*` no lleva el prefijo del módulo, así que no puede mudarse a él (`ExtensionesModulos.Validar` exige
`atlas.<id>.`): se queda en el núcleo, como `atlas.paquetes.gestionar` en el portal. Como dos manifiestos no pueden
compartir id, el del núcleo pasa de `documentacion` a `docs`, y el módulo declara un manifiesto vacío. Los códigos y las
concesiones no cambian.

**Prefijo de tablas `documentacion_`, no `doc_`.** El contrato de módulo deriva el prefijo del id (ATLAS-ADR-0016), igual
que `contratos_` frente al `contrato_` del prompt maestro §9. Este ADR registra la diferencia.

### 2. Dos orígenes, un documento

- `documentacion_documento`: identidad estable, aplicativo, tipo, título, visibilidad, audiencias, responsable y origen
  (`repo` o `atlas`). Clave natural de los de repo: aplicativo + ruta en el repo. La ruta identifica al documento aunque
  haya números de ADR duplicados.
- `documentacion_version`: **inmutable**, para lo que llega por CI: versión correlativa, contenido, SHA-256, repo, commit,
  ruta y fecha. El mismo hash es idempotente y no crea una versión.
- `documentacion_revision`: para lo escrito en Atlas, con el modelo de las guías (ATLAS-ADR-0040): cada guardado es una
  revisión con huella, autor y nota, y restaurar crea una revisión nueva. Al publicar se congela una `documentacion_version`,
  así el lector, la comparación, los exportes y F7 ven siempre una sola forma de versión.
- Un documento de origen `repo` **no se edita** en Atlas (409 `documento_de_repo`).
- Canales `dev`, `qa` y `pdn` por documento (`documentacion_canal`), como en contratos (ATLAS-ADR-0035 §8), con un Atlas
  central (ATLAS-ADR-0052). Un empleado ve la versión del canal `pdn` por defecto y puede elegir otra.

### 3. Estados y aprobación

`borrador → en_revision → publicado → archivado`, solo para el origen `atlas`.

- Aprueba `atlas.docs.aprobar`, que el líder técnico tiene y el responsable tiene solo en su aplicativo.
- Publicar con visibilidad `publico_clientes` exige la aprobación del principal o del secundario (§7.2).
- Quien aprueba su propio texto recibe el mismo trato que en las guías. La pregunta de si se exige aprobación de otra
  persona sigue abierta y se resolverá igual para los dos módulos.
- En los documentos de repo, la revisión es el PR. Su front-matter debe traer `estado: aprobado` para publicarse como
  `publico_clientes`; si no, se publica y se muestra como borrador solo a roles técnicos (plan 03 §18.3). Además,
  `estado: borrador` o `estado: en_revision` marcan la versión como borrador en cualquier nivel de visibilidad.

### 4. Publicación desde la CI

- Workflow reutilizable `atlas-publicar-docs.yml` con el token OIDC (ATLAS-ADR-0009), tipo de fuente `docs` en
  `catalogo_fuente_publicacion` (ATLAS-ADR-0035).
- `POST /api/v1/documentacion/publicaciones` recibe **un lote** del commit: rutas, contenidos, y el ambiente según la rama
  (`develop` → dev, `staging` → qa, `release` → pdn). Cada documento se resuelve por separado y la respuesta dice cuál
  se creó, cuál quedó igual y cuál se rechazó.
- Validación en la CI **y** en el servidor: front-matter obligatorio (aplicativo, tipo, título, visibilidad, audiencias,
  responsable), topes de tamaño y lista blanca de MDX (§5). El aplicativo del front-matter debe estar autorizado para el
  repo. El servidor nunca confía en lo validado por la CI.
- El front-matter no trae secretos ni datos reales; el workflow corre gitleaks sobre lo que publica.
- Una ruta que desaparece del repo no borra nada. El último lote puede traer `rutasVigentes`: lo que no esté ahí sale del
  canal de ese ambiente y, cuando ya no le queda ningún canal, se marca `archivado`. Así, borrar un archivo en `develop` no
  retira lo que sigue corriendo en pdn. Sus versiones siguen ahí.

### 5. MDX sin evaluar código

Nunca se compila ni se evalúa MDX como JavaScript. La PWA **analiza** el texto a un árbol (`remark-parse` + `remark-mdx`) y
lo recorre con un renderizador propio:

- Solo se pintan componentes de la lista blanca: `Contrato`, `Diagrama`, `Responsables`, `SoporteDeTurno`, `Alerta`,
  `Pestanas`. Otro nombre se muestra como aviso, sin ejecutarlo.
- Los atributos solo pueden ser **cadenas literales**. Las expresiones (`{...}`), `import` y `export` son error de validación
  al publicar y texto inerte al mostrar.
- El HTML crudo no se pinta; los enlaces solo admiten `https`, `mailto` y rutas internas.
- Mermaid usa `securityLevel: 'strict'` y corre en `/visor/mermaid.html`, un iframe con `sandbox="allow-scripts"` y la CSP del
  visor (ATLAS-ADR-0039): inyecta estilos en línea, que la CSP del shell no permite, y así no se relaja. Sin conexión, con
  una fuente inválida o de más de 20 mil caracteres se muestra el texto. Versión 12.2.0.
- Las imágenes no se cargan: queda su descripción y, si la dirección es segura, un enlace. Una imagen ajena revelaría quién
  lee y la CSP solo admite `img-src 'self' data:`.
- `Pestanas` no tiene un componente hijo, porque el servidor no admite otra etiqueta: cada título abre una pestaña.
- `Alerta` admite `tipo` (`info`, `aviso`, `peligro`, `exito`) y `titulo`. `Diagrama` muestra un aviso hasta que llegue la F6.
- Cada componente vivo pide sus datos con la sesión de quien lee: incrustar `<Contrato/>` no da permiso para verlo; sin
  permiso se muestra «No tienes acceso a este contenido».

El servidor valida la misma lista blanca con un analizador propio, conservador: rechaza los `import` y `export` de nivel
superior, las expresiones entre llaves fuera de bloques de código y las etiquetas fuera de la lista. Ante la duda, rechaza.

Lo escrito en Atlas se valida al enviar a revisión y al publicar, no al guardar: un borrador a medias se puede guardar.

Cada versión guarda su propia visibilidad y el predicado la revisa, además de la del documento: una versión vieja más
restringida no se abre si el documento se abre después. El documento copia la foto de su versión vigente, así que una versión
de `develop` no cambia el título ni la visibilidad de lo que corre en pdn.

### 6. Visibilidad y permisos en SQL

Los niveles de §7.1 se traducen a un predicado obligatorio en cada consulta, incluidas búsqueda y exportes: `interno_ti` para
superadmin, líder técnico y desarrollador; `restringido` para superadmin, líder técnico (decisión del líder, 06/11/2026) y gobierno de IA; los demás empleados, hasta
`interno_negocio`. El responsable ve todos los documentos de su aplicativo, porque los edita (§7.2).

Un externo solo verá `publico_clientes` dentro de lo compartido con su organización. **Compartir no está construido:** por
decisión del líder (06/11/2026) lo de externos va al final, así que hoy un externo no ve ningún documento, aunque tenga
`atlas.docs.ver`. Al construirlo, `documentacion_comparticion` y `documentacion_exportacion` llevarán `organizacion_id` y RLS,
e `IDocumentacionDelPortal` dejará de ser la implementación vacía. Hoy `documentacion_exportacion` no la lleva: es de una
persona y solo exportan empleados. Nadie externo entra a pdn hasta cerrar la revisión de seguridad
(`docs/seguridad/revision-f4.md` §8), que deberá incluir este módulo.

### 7. Búsqueda, exportes y eventos

- Búsqueda: columna `tsvector` generada con la configuración de español y un `IProveedorBusqueda`. Las tildes se quitan
  con `translate`, no con `unaccent`: una columna generada solo admite funciones inmutables, `unaccent` no lo es, y esa
  extensión la crea el administrador en cada ambiente y sigue sin verificarse en la 177
  (`docs/preparacion/04-prerrequisitos.md`). Si falta, la migración fallaría y atlas-api no arrancaría. La búsqueda
  normaliza el texto igual (`FuncionesSql.NormalizarBusqueda`) y cubre el español y el portugués.
- Exportes (HU 5.6): uno o, como colección, hasta 50 documentos a PDF, Word, HTML y Markdown, siempre con un ZIP. Los hace
  atlas-docs por la cola existente (ATLAS-ADR-0042), con una entrada propia (`EntradaDocumentos`). Solicitar exige
  `atlas.docs.exportar` y un ingreso reciente, con el predicado de lectura de cada documento y versión: basta uno que la persona
  no pueda leer para responder 404 a toda la solicitud, sin decir cuál. Solo quien pidió la exportación la ve y la descarga.
- atlas-api convierte el MDX en Markdown estático con los valores vigentes al exportar, sin evaluar nada: `Alerta` deja su
  título y su contenido, `Pestanas` sus títulos y su contenido, y `Contrato`, `Responsables` y `SoporteDeTurno` su valor solo si
  **quien exporta** tiene el permiso de ese dato, como en el lector; si no, «No tienes acceso a este contenido». Un archivo sale
  de Atlas: lo que la persona no ve no entra. `Responsables` lista los nombres sin distinguir principal de secundario, porque
  es lo que da `ILectorResponsables`. `Diagrama` deja un texto hasta la F6. **Un bloque Mermaid queda como código en los
  archivos:** dibujarlo exigiría un navegador o D2 en atlas-docs.
- `ILectorContratos`, nueva lectura en Plataforma, le da el contrato a este módulo sin referenciar al de contratos.
- **Sin permisos nuevos en RabbitMQ ni en el S3.** atlas-docs liga `atlas.documentacion.exportacion_solicitada` a su misma cola
  `atlas.docs.exportaciones` y publica `atlas.documentacion.exportacion_{iniciada,lista,fallida}` en `flit.atlas.docs`. Los
  resultados llegan a la misma cola `atlas.api.exportaciones`: los permisos de QA y pdn son una lista exacta de colas, y una cola
  nueva sin su permiso tumbaría también el consumo de los contratos, porque `ConsumidorEventos` abre todo en un solo ciclo. Por
  eso el consumidor ahora admite varios manejadores en una cola, con un consumidor por cola y despacho por tipo. La entrada va
  bajo el mismo `exportaciones/<id>/`, con la misma regla de ciclo de vida.
- Eventos: `atlas.documentacion.version_publicada` (para F7) y los de exportación, en el AsyncAPI.

### 8. Editor y tiempo real

- Editor: un `<textarea>` de MDX con vista previa del mismo renderizador del lector, en la ruta del editor con carga diferida.
- **Monaco se descarta:** 0.57.0 (MIT) crea elementos `<style>` en tiempo de ejecución
  (`vs/base/browser/domStylesheets.js`), que `style-src 'self'` bloquea. No se relaja la CSP.
- **Tiptap queda para después [por verificar con el líder]:** 3.33.0 (MIT) se publicó el 05/11, sin las dos semanas. Un editor
  visual además debe conservar los componentes MDX al ida y vuelta, y eso necesita su propio trabajo.
- Antes de adoptar cada librería se verifica versión estable con ≥ 2 semanas, licencia y que funcione con la CSP.
- SignalR es el primer hub de Atlas (HU 5.8), en `/api/v1/documentacion/tiempo-real`: «quién está viendo» y «versión nueva». Pide
  sesión y `atlas.docs.ver` al negociar; la bandera y el origen se revisan al conectar, porque el filtro de los endpoints y la
  antifalsificación no cubren un WebSocket (un GET). Cada documento se revisa al entrar con el predicado de lectura. Por el hub
  solo viajan nombres de quienes pueden ver el documento, ids y números de versión; nunca contenido.
- Una conexión abierta no vuelve a pasar por la cookie: se cierra a los 5 minutos (`Documentacion:TiempoReal:VidaMaxima`) y el
  cliente se reconecta, y al negociar se revisan otra vez la sesión y los permisos. Ese es el retraso máximo tras suspender a
  alguien o quitarle el permiso, y durante ese rato solo puede recibir lo anterior.
- **Sin backplane: una sola réplica.** QA y pdn corren atlas-api con `replicas: 1`. La presencia vive en memoria y los avisos solo
  llegan a las conexiones de la réplica que publica. Con más réplicas, la presencia mostraría solo a los de la misma réplica, un
  aviso de versión podría no llegar (el documento se ve al recargar) y el long polling exigiría afinidad de sesión. Antes de
  escalar hay que agregar el backplane de Redis y llevar la presencia allí. La PWA lo trata como un añadido: sin hub, el lector
  funciona igual y no muestra a nadie.
- El transporte es el que el navegador negocie: WebSocket, y si no, SSE o long polling. `connect-src 'self'` cubre el `wss` del
  mismo host en los navegadores vigentes **[por verificar en Safari real]**.
- La edición colaborativa con Yjs queda como mejora posterior: necesitaría un servidor de sincronización y persistir el documento
  CRDT, y lo que se escribe aquí ya resuelve los conflictos con `revision_desactualizada`. Se decide con su propio ADR.
- Favoritos sin conexión (HU 5.4, ATLAS-ADR-0034): `documentacion_favorito` por persona; la PWA guarda el documento con su versión
  vigente y la lista de versiones, con 72 horas de vida. Los componentes vivos no se guardan: sin red dicen que se verán al volver
  la conexión.

### 9. Asistencia con IA

La HU 5.7 **no está construida**: depende del gateway (A-10), que aún no existe. Cuando llegue irá detrás de la bandera
`documentacion.borradores_ia`, apagada; el borrador siempre quedará en `borrador` y lo aprobará una persona.

## Consecuencias

- Buenas: un solo modelo de versión para el lector y para F7; incrustar componentes no abre ejecución de código; Atlas no
  edita lo que vive en git.
- Malas: dos analizadores de MDX (PWA y servidor) que pueden diferir, por lo que el de la PWA nunca confía en el servidor;
  no se puede usar MDX con expresiones.
- A vigilar: tamaño del bundle del editor (presupuesto de §11) y capacidad de la cola de exportes, que los documentos comparten
  con los contratos, de a uno por pod.

## Opciones descartadas

- **Compilar MDX en el cliente** (`@mdx-js/mdx`, `evaluate`): necesita `unsafe-eval` y ejecuta lo que escribe el autor.
- **Compilar MDX en la CI y publicar JavaScript**: Atlas serviría código de los repos, y lo escrito en Atlas no tendría CI.
- **Reutilizar las guías de contratos**: están atadas a un contrato y no tienen canales, tipos ni audiencias.
- **Markdown sin componentes**: no permite contenido vivo, que es un requisito de §8.5.

## Por verificar

- **[por verificar con el líder]** Quién aprueba si un aplicativo no tiene responsables (hoy, el líder técnico) y si se
  exige aprobación de otra persona. Abierto también en las guías.
- **[por verificar con el líder]** Si el auditor debe ver `restringido`: hoy no lo ve (§7.1).
- **[por verificar con el líder]** Los ADR de los repos no traen este front-matter: hasta decidir cómo llegan (HU 6.1), el
  workflow publica `docs/funcional` y `docs/integraciones` por defecto.
- **[por verificar con el líder]** Si se quiere un editor visual para negocio (Tiptap, §8).
- **[por verificar con el líder]** Si un bloque Mermaid debe dibujarse en los exportes (§7); hoy queda como código.
