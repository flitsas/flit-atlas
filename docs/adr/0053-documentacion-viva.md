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
- Mermaid usa `securityLevel: 'strict'`.
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
superadmin, líder técnico y desarrollador; `restringido` para superadmin y gobierno de IA; los demás empleados, hasta
`interno_negocio`. El responsable ve todos los documentos de su aplicativo, porque los edita (§7.2). Las tablas con
datos de organizaciones externas (`documentacion_comparticion`, `documentacion_exportacion`) llevan `organizacion_id` y RLS.

Un externo solo ve `publico_clientes` dentro de lo compartido con su organización. Compartir se construye en esta fase,
pero nadie externo entra a pdn hasta cerrar la revisión de seguridad (`docs/seguridad/revision-f4.md` §8), que ya incluirá
este módulo. `IDocumentacionDelPortal` deja de ser la implementación vacía cuando exista compartir.

### 7. Búsqueda, exportes y eventos

- Búsqueda: columna `tsvector` generada con la configuración de español y un `IProveedorBusqueda`. Las tildes se quitan
  con `translate`, no con `unaccent`: una columna generada solo admite funciones inmutables, `unaccent` no lo es, y esa
  extensión la crea el administrador en cada ambiente y sigue sin verificarse en la 177
  (`docs/preparacion/04-prerrequisitos.md`). Si falta, la migración fallaría y atlas-api no arrancaría. La búsqueda
  normaliza el texto igual (`FuncionesSql.NormalizarBusqueda`) y cubre el español y el portugués.
- Exportes: los hace atlas-docs por la cola existente (ATLAS-ADR-0042), con un tipo de entrada nuevo. El MDX se convierte
  en contenido estático con los valores vigentes al exportar.
- Eventos: `atlas.documentacion.version_publicada` (para F7), en el AsyncAPI.

### 8. Editor y tiempo real

- Editor: Tiptap (visual) y Monaco (MDX), cargados de forma diferida solo en la ruta del editor.
- Antes de adoptar cada librería se verifica versión estable con ≥ 2 semanas, licencia y que funcione con la CSP. **Si
  Monaco no funciona sin relajar la CSP, no se relaja:** se vuelve a este ADR con la alternativa.
- SignalR es el primer hub de Atlas: «quién está viendo» y «versión nueva». La edición colaborativa con Yjs queda fuera.

### 9. Asistencia con IA

La HU 5.7 queda detrás de la bandera `documentacion.borradores_ia`, apagada. Toda llamada pasa por el gateway (A-10), que
aún no existe; el borrador siempre queda en `borrador` y lo aprueba una persona.

## Consecuencias

- Buenas: un solo modelo de versión para el lector y para F7; incrustar componentes no abre ejecución de código; Atlas no
  edita lo que vive en git.
- Malas: dos analizadores de MDX (PWA y servidor) que pueden diferir, por lo que el de la PWA nunca confía en el servidor;
  no se puede usar MDX con expresiones.
- A vigilar: tamaño del bundle del editor (presupuesto de §11) y capacidad de la cola de exportes.

## Opciones descartadas

- **Compilar MDX en el cliente** (`@mdx-js/mdx`, `evaluate`): necesita `unsafe-eval` y ejecuta lo que escribe el autor.
- **Compilar MDX en la CI y publicar JavaScript**: Atlas serviría código de los repos, y lo escrito en Atlas no tendría CI.
- **Reutilizar las guías de contratos**: están atadas a un contrato y no tienen canales, tipos ni audiencias.
- **Markdown sin componentes**: no permite contenido vivo, que es un requisito de §8.5.

## Por verificar

- **[por verificar con el líder]** Quién aprueba si un aplicativo no tiene responsables (hoy, el líder técnico) y si se
  exige aprobación de otra persona. Abierto también en las guías.
- **[por verificar con el líder]** Quién ve `interno_ti` y `restringido`: el líder técnico no ve `restringido`, y el auditor
  tampoco (§7.1 solo nombra a administración y gobierno de IA).
- **[por verificar con el líder]** Los ADR de los repos no traen este front-matter: hasta decidir cómo llegan (HU 6.1), el
  workflow publica `docs/funcional` y `docs/integraciones` por defecto.
- **[por verificar]** Versiones y licencias de `remark-mdx`, Tiptap y Monaco; Monaco con la CSP.
