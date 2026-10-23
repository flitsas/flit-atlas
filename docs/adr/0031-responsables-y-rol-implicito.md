---
id: ATLAS-ADR-0031
titulo: Responsables y rol implícito — módulo de equipo con reparto por aplicativo del catálogo, importación del documento con vista previa, historial por fotos con huella, rol implícito de editor por aplicativo y lecturas entre módulos publicadas en Plataforma
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7.2 (rol implícito por propiedad), §7.4, §8.2, §8.9, §9 y §10, insumos/Responsables_Productos_FLIT_2026-10-20.md, HU 2.3, HU 2.4, HU 2.5, ATLAS-ADR-0023, ATLAS-ADR-0026, ATLAS-ADR-0028, ATLAS-ADR-0029, ATLAS-ADR-0030]
---

# ATLAS-ADR-0031 · Responsables y rol implícito

## Contexto y problema

Las HU 2.3, 2.4 y 2.5 piden: importar el documento de responsables (versión 5, 20/10/2026) a responsabilidades principal/secundario por producto y módulo, con dedicación orientativa, vigencia y estado de traspaso; mostrar las definiciones de cada rol tal cual; vistas por persona, por producto y módulo y mapa de carga; historial versionado con comparación y restauración; traspaso acompañado con checklist; revisión trimestral con recordatorio; el **rol implícito** de editor del §7.2; el bloque CODEOWNERS sugerido, la ficha de responsables y las diferencias con los CODEOWNERS reales; y el botón «¿A quién llamo?» con el soporte de turno. CA: las tablas 2, 3.1 y 3.2 se reproducen exactamente desde los datos; cambiar un responsable crea la versión 6 con autor y nota; restaurar la 5 crea la 7 igual a la 5.

Hechos verificados en el código:

- `IProveedorRolesImplicitos` (ATLAS-ADR-0023) existe con una implementación vacía; `CalculadoraPermisos` suma sus roles en cada petición, y `atlas.responsable` ya recibe `atlas.catalogo.editar`, `atlas.docs.editar`, `atlas.docs.aprobar`, `atlas.diagramas.editar`, `atlas.contratos.guias.editar` y `atlas.paquetes.gestionar`. El catálogo evalúa `editar` sobre el aplicativo **y sus ancestros** (ATLAS-ADR-0028).
- Un módulo no puede referenciar a otro (`ArquitecturaModulosTests`); el catálogo y el soporte tienen sus tablas y sus reglas (turnos vigentes, festivos).
- El manifiesto `equipo` seguía en `ManifiestosNucleo.Todos`; el catálogo y el soporte ya mudaron los suyos.
- El directorio de personas (HU 2.2) vive en `/api/v1/plataforma/directorio`; `plataforma_persona` no tiene usuario de GitHub.
- El migrador corre la semilla de cada módulo con los contextos de datos, sin los servicios de atlas-api.

## Factores de decisión

- No duplicar el catálogo ni las personas; nada de nombres reales en el código, las semillas ni las pruebas.
- Lo que no se pueda enlazar se reporta, no se inventa; se confirma exactamente lo que se vio.
- El rol implícito se apaga solo cuando cambia el reparto, sin trabajos ni sesiones que invalidar.
- Un módulo que solo depende de Plataforma y no se acopla a las tablas de otro.

## Opciones consideradas

### Importación

1. **Importador en la PWA**: se sube (o pega) el Markdown, el servidor lo lee, lo enlaza y muestra la vista previa; se confirma con su huella.
2. Semilla del migrador que lee el archivo del repo.

### Historial

1. **Una foto completa por versión** (JSON con el reparto) más el reparto vigente en filas; la huella (SHA-256 sin nombres) dice si dos versiones son iguales.
2. Un registro de eventos por cambio y el reparto reconstruido al vuelo.

### Lectura del catálogo y del soporte

1. **Interfaces de lectura en Plataforma** (`ILectorAplicativos`, `ISoporteDeTurno`) que implementan el catálogo y el soporte, con una implementación vacía por defecto.
2. Leer sus tablas desde el módulo de equipo (consultas SQL o entidades de solo lectura).
3. Que la PWA llame a las dos API y junte los datos.

## Decisión

Opción 1 en las tres.

### Módulo y permisos

- `Flit.Atlas.Modulos.Equipo` (id `equipo`, `/api/v1/equipo`, bandera `modulo.equipo` **encendida por defecto**, como su entrada en la PWA). El manifiesto `equipo` se muda de `ManifiestosNucleo.Todos` a `PermisosEquipo` con los mismos tres códigos y concesiones (`ver`: todo empleado; `gestionar`: líder técnico; `ver_contacto`: líder técnico, desarrollador y soporte). Sin permisos nuevos: leer exige `ver`; importar, cambiar, restaurar, traspasos, revisiones, usuarios de GitHub y comparar con GitHub, `gestionar`. El directorio de la HU 2.2 se queda en `/api/v1/plataforma/directorio`.

### Datos (`equipo_*`, sin `organizacion_id`)

| Tabla | Para qué |
|---|---|
| `equipo_responsabilidad` | El reparto vigente: aplicativo (id del catálogo, **sin llave foránea**: un módulo no depende de las tablas de otro; si el aplicativo se elimina, la responsabilidad queda huérfana, la vista la cuenta y el siguiente cambio la retira), persona (llave a `plataforma_persona`), tipo, dedicación (1–100, el secundario 20 %), vigencia (desde/hasta) y estado de traspaso. Índice único por aplicativo y tipo. Guarda el orden de las tablas del documento (aplicativo, persona y posición en la lista de la persona) para reproducirlas igual |
| `equipo_responsabilidad_version` | Una foto por versión: número único, origen (`importacion`, `cambio`, `restauracion`), versión restaurada, fecha del documento, «definió» (`@pii`), autor (persona de Atlas), nota (`@pii`), definiciones de los roles (JSON tal cual del §1), contenido (JSON `@pii`) y huella |
| `equipo_traspaso_checklist` | Despliegues y guardias del secundario con el principal (por responsabilidad **y** persona: si cambia la persona, el checklist vuelve a cero) |
| `equipo_revision_programada` | Revisiones trimestrales; a lo sumo una pendiente (índice único parcial); marca del recordatorio |
| `equipo_usuario_github` | Usuario de GitHub por persona (`@pii`), editable; el núcleo no se toca |

Toda escritura va con su evento en `plataforma_auditoria` en la misma transacción (`equipo.reparto_importado`, `.responsable_cambiado`, `.responsable_retirado`, `.reparto_restaurado`, `.traspaso_actividad`, `.traspaso_habilitado`, `.revision_programada`, `.revision_cerrada`, `.usuario_github`), con ids y números, nunca notas ni nombres.

### Importación (HU 2.3)

- `POST /importacion/vista-previa` lee el Markdown (hasta 100 KB) con la estructura de la versión 5: encabezado (fecha, «Definió», versión), §1 (definiciones; la dedicación del secundario sale del «~20 %»), §2 (por persona), §3.1 (productos), §3.2 (módulos) y §4 (próxima revisión). Enlaza los productos de la 3.1 con productos y los módulos de la 3.2 con módulos del catálogo (nombre sin tildes ni mayúsculas, o slug), y las personas con los empleados de `plataforma_persona` (nombre normalizado, o correo si la celda lo trae). Devuelve lo que quedaría, lo **no enlazado** (no encontrado o ambiguo, con qué hacer), las **inconsistencias** entre la tabla 2 y las 3.x (se importa lo de las 3.x) y una huella.
- `POST /importacion` recalcula y exige la misma huella (409 si el documento, el catálogo o las personas cambiaron), que no quede nada sin enlazar y que la versión del documento sea mayor que la vigente. Reemplaza el reparto, crea **la versión que dice el documento** (la 5), con las definiciones, y programa la revisión del §4 si no hay una. Los secundarios quedan `habilitado` (por defecto) o `en_acompanamiento`, a elección de quien importa.
- Se descarta la semilla del migrador: el documento ya está en git, pero el migrador no tiene las personas enlazadas (en qa y pdn dependen de la sincronización de la HU 2.2) ni a nadie que vea y confirme la vista previa; y el archivo quedaría como fuente de verdad en cada despliegue.

### Historial (HU 2.3)

- Cambiar o asignar (`PUT /responsabilidades`) y retirar (`DELETE /responsabilidades/{id}?nota=`) crean la versión siguiente con autor y nota obligatoria. Un secundario nuevo empieza `en_acompanamiento`; si la persona no cambia, conserva su traspaso.
- `POST /versiones/{n}/restauracion` lleva el reparto a la foto de la versión `n` y crea la siguiente con la misma huella (CA: restaurar la 5 crea la 7 igual a la 5). `GET /versiones/comparacion` dice qué se agregó, se retiró o cambió de persona o de datos.
- `GET /responsabilidades/tablas` reconstruye las tablas 2, 3.1 y 3.2 en Markdown desde los datos; las pruebas las comparan con las del documento ficticio, carácter por carácter.

### Rol implícito (§7.2)

`RolesImplicitosEquipo` reemplaza la implementación vacía: cada responsabilidad **vigente** hoy (Bogotá) da `atlas.responsable` con alcance `aplicativo:<id>`. Como la plataforma calcula los permisos en cada petición, un cambio del reparto vale en la siguiente, sin invalidar sesiones. Se da también al secundario en acompañamiento (§7.2 dice «principal o secundario») **[por verificar con el líder]**.

### Traspaso, revisión y carga

- Checklist: al menos un despliegue y una guardia con el principal; habilitar sin ellos es 409 `checklist_incompleto`.
- Revisión trimestral: el trabajo `TrabajoRecordatorioRevision` revisa cada hora y, `Equipo:Revision:DiasAnticipacion` días antes (7), notifica (bandeja y push, centro de notificaciones de la HU 1.6, tipo `equipo.revision_trimestral`) a quien tiene `atlas.equipo.gestionar` global (sin el superadministrador). Una actualización condicionada hace que salga una sola vez aunque haya varias réplicas. Cerrarla programa la siguiente a tres meses. Correo y Teams llegan con la HU 2.11.
- Mapa de carga: principal = 1 y secundaria = su dedicación (0,2); se resalta a quien está más de un 30 % por encima o por debajo de la media.

### CODEOWNERS y ficha (HU 2.4)

- El bloque sugerido sale del catálogo: el repo es el del aplicativo o el de su ancestro más cercano; el patrón es `*` si el aplicativo define el repo y `/<ruta>/` si tiene carpeta (un módulo sin carpeta propia no genera línea). Los responsables son los del aplicativo o, si no tiene, los de su ancestro más cercano (principal primero). Quien no tiene usuario de GitHub queda comentado.
- Las diferencias leen el CODEOWNERS real con la API REST de GitHub, **solo lectura**, en `.github/`, la raíz o `docs/` (`Plataforma:GitHub`: token o GitHub App con id, instalación y llave PEM; el JWT de la App lo firma `Microsoft.IdentityModel.JsonWebTokens`, la versión que ya trae `Microsoft.Identity.Web`). Sin configuración, la pantalla lo dice y no falla; un 401/403 o la red caída se muestran como mensaje. Abrir un PR con el bloque es mejora futura.
- La ficha de responsables en Markdown para cada README (principal, secundario, cómo desplegar, a quién escalar y las definiciones) se copia o descarga desde la pestaña del aplicativo.

### «¿A quién llamo?» (HU 2.5) y lecturas entre módulos

- `ILectorAplicativos` y `ISoporteDeTurno` viven en `Flit.Atlas.Plataforma.Modulos` con implementaciones vacías (`TryAdd`); el catálogo registra la suya (todos los aplicativos) y el soporte la suya (equipos activos con sus turnos vigentes de una fecha y su horario). Es el único cambio en el módulo de soporte: un archivo y una línea en su registro.
- `GET /aplicativos/{slug}/a-quien-llamo`: si hoy es sábado en Bogotá, el soporte de turno de **cada equipo** (por área); además, principal y secundario (los heredados del ancestro si no tiene). El teléfono solo con `atlas.equipo.ver_contacto`.
- PWA: la pestaña `responsables` del punto de extensión `pestanasFicha` (sin importar el catálogo), con el botón «¿A quién llamo?» y la ficha; la página `/equipo/responsables` (por producto y módulo, por persona, mapa de carga, historial, traspasos, CODEOWNERS e importar) y el widget «Mis responsabilidades». El directorio `/equipo` sigue igual, con un enlace a los responsables.

## Consecuencias

### Positivas

- El rol implícito llega sin tocar el catálogo, la plataforma ni las sesiones; los módulos futuros (documentos, diagramas, paquetes) lo reciben con su alcance.
- Lo importado es exactamente lo que se vio, y lo que no se pudo enlazar queda a la vista.
- Comparar y restaurar son operaciones sobre fotos, sin reconstrucciones; «igual a la 5» es una huella igual.
- El catálogo y el soporte siguen siendo dueños de sus tablas y sus reglas.

### Negativas

- Sin llave foránea al catálogo, eliminar un aplicativo deja responsabilidades huérfanas hasta el siguiente cambio del reparto.
- Las personas del documento deben estar en Atlas antes de importar: en qa y pdn hace falta el origen `grupo` de la sincronización (ATLAS-ADR-0030) o que cada una entre una vez.
- El reparto se reproduce con los nombres de Entra: si un nombre del documento difiere del de Entra (más allá de tildes y mayúsculas), la importación lo deja sin enlazar y la tabla reproducida usará el nombre de Entra.
- Una versión por cambio puede crecer rápido si se hacen muchos cambios pequeños (cada foto pesa unos KB).
- El tipo de notificación nuevo aún no aparece en las preferencias de la persona (`TiposNotificacion.Todos` es estático, ATLAS-ADR-0026).
- `Plataforma:GitHub` vive en el módulo de equipo; si contratos (F3) necesita GitHub, se mueve el cliente a Plataforma.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Semilla del migrador | Sin personas enlazadas ni vista previa; el archivo pisaría lo cambiado en Atlas en cada despliegue |
| Historial por eventos | Comparar y restaurar exigirían reconstruir el reparto; la foto es pequeña y simple |
| Leer las tablas del catálogo o del soporte | Acopla el módulo a esquemas ajenos y repite reglas (turnos vigentes, festivos) |
| Juntar los datos en la PWA | El contacto y el turno se filtrarían por permisos en dos lugares; un solo endpoint es más simple de proteger y de probar |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Equipo/`, `apps/web/src/modulos/equipo/`, `services/api/README.md` (configuración), `deploy/README.md` (cómo importar el documento en qa y pdn).
- Pendientes **[por verificar]**: rol implícito para el secundario en acompañamiento; estado de traspaso de los secundarios de la versión 5 al importarla; destinatarios del recordatorio (hoy, quien tiene `gestionar` global); token o GitHub App por ambiente; usuarios de GitHub de cada persona; repo y carpeta de los aplicativos en el catálogo (sin ellos no hay línea de CODEOWNERS); qué equipos de soporte se muestran en «¿A quién llamo?» (hoy, todos los activos).
