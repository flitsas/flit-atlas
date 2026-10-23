---
id: ATLAS-ADR-0029
titulo: Soporte de los sábados — módulo propio con equipos por área, turnos por persona, festivos de Colombia calculados en Atlas y generación equitativa determinista con vista previa y huella
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7, §8.3, §9, §10 y §11, HU 2.6, HU 2.8, ATLAS-ADR-0020, ATLAS-ADR-0023, ATLAS-ADR-0026]
---

# ATLAS-ADR-0029 · Soporte de los sábados

## Contexto y problema

Las HU 2.6 (modelo y calendario) y 2.8 (generación automática) piden el primer módulo funcional con datos de Atlas: configuración, participantes y su orden, turnos con horario propio, estados `programado → confirmado → aplicado` o `reemplazado` / `cancelado`, origen `importado | generado | manual`, vistas mes, semana y lista, el widget «Soporte de este sábado», indicadores básicos y una rotación equitativa para los próximos N meses que salte ausencias y aplique una regla para los festivos de Colombia calculados localmente (Ley Emiliani), con vista previa y sin publicar nada sin confirmación.

Hechos verificados antes de decidir:

- La lista base del §7.4 ya declaraba `atlas.soporte.ver` (todo empleado), `solicitar_cambio`, `aprobar` y `gestionar` en `ManifiestosNucleo.SoporteSabados` (ATLAS-ADR-0023), y el registro de módulos exige que el manifiesto de un módulo tenga su mismo id (ATLAS-ADR-0026). Con los dos, «soporte» quedaba registrado dos veces.
- El modelo de alcances de ATLAS-ADR-0023 ya resuelve `PermisosEfectivos.TieneEn(permiso, tipo:id)` y deja que un manifiesto agregue tipos de alcance sin tocar Plataforma.
- El shell (ATLAS-ADR-0020) ya registraba el módulo «soporte» como «próximamente», en la barra inferior y con el widget «Soporte de este sábado».
- Decisiones del líder del 23/10/2026: horario por defecto **08:00–12:00** (Bogotá), configurable y con horario propio por turno; un sábado festivo de Colombia **se salta** (regla por defecto, configurable); aprueba **quien tenga el permiso**, no una persona fija; y el soporte no es un solo equipo: hay **equipos por área** («Desarrollo TI», una persona por sábado; «Operaciones», una o dos), cada uno con sus participantes, su orden y su generación.
- La Ley 2578 del 1 de junio de 2026 agregó el festivo de la Virgen del Rosario de Chiquinquirá (9 de julio, trasladable al lunes): desde 2026 hay 19 festivos nacionales. Está demandada ante la Corte Constitucional **[por verificar]**.

## Factores de decisión

- Nada se publica sin confirmación y nada distinto de lo que se vio en la vista previa.
- Equidad comprobable con pruebas (diferencia máxima de un turno por persona en la ventana), también con dos personas por sábado.
- Sin servicios externos para los festivos y un componente reutilizable por las HU 2.10 y 2.11.
- Externos sin nombres de personas (§7.3); el filtro en el servidor.
- Un módulo que solo depende de Plataforma (ATLAS-ADR-0026) y deja el modelo listo para las HU 2.7 (importador), 2.9 (cambios y compensaciones), 2.10 (Microsoft 365), 2.11 (recordatorios) y 2.12 (sin conexión).

## Opciones consideradas

### Turno de un equipo con varias personas

1. **Un turno = una persona de un equipo en un sábado**; el soporte del equipo ese día son sus turnos vigentes (de 1 al máximo del equipo).
2. Un turno por equipo y fecha con una tabla hija de personas.

### Generación y confirmación

1. **Algoritmo determinista y confirmación que recalcula y compara una huella (SHA-256)** de la vista previa.
2. Guardar la propuesta en una tabla de borradores y publicarla por id.

### Festivos

1. **Cálculo local** (Pascua por el algoritmo de Meeus/Jones/Butcher, fijos, trasladables al lunes y relativos a Pascua) más una tabla de días adicionales.
2. Una librería de festivos o un servicio externo.

### Aprobadores y responsables por equipo

1. **Permisos `atlas.soporte.gestionar` y `atlas.soporte.aprobar` con alcance `equipo_soporte:<id>`** (ATLAS-ADR-0023), además de los globales.
2. Una lista de aprobadores en la configuración.

## Decisión

Opción 1 en las cuatro.

### Módulo y permisos

- Proyecto `Flit.Atlas.Modulos.Soporte` (id `soporte`, `/api/v1/soporte`, bandera `modulo.soporte` **encendida por defecto**, como su entrada en la PWA), registrado en `Flit.Atlas.Composicion`. Solo referencia Plataforma.
- `ManifiestosNucleo.SoporteSabados` deja de estar en `ManifiestosNucleo.Todos`: lo publica el módulo con los mismos cuatro códigos (siguen siendo contrato) y suma `atlas.soporte.ver_canal` (todos los roles externos: el canal del sábado sin nombres). El manifiesto agrega el tipo de alcance `equipo_soporte`.
- Alcance: lo de un equipo (cambiarlo, sus participantes y orden, turnos a mano, estados, reemplazos y la vista previa) exige `atlas.soporte.gestionar` global o con alcance a ese equipo; publicar su generación, `atlas.soporte.aprobar` igual. La configuración global, crear equipos y los festivos adicionales exigen `gestionar` global; las ausencias, `gestionar` global o sobre algún equipo de la persona. Las políticas de endpoint siguen pidiendo el permiso en algún alcance (`AutorizacionTests`); el alcance lo revisa el endpoint con `TieneEn` y responde 403 con `ErrorSoporte`. No hay aprobadores fijos en código ni en la configuración.

### Datos (`soporte_*`, sin `organizacion_id`)

| Tabla | Para qué |
|---|---|
| `soporte_configuracion` | Una fila: horario por defecto (08:00–12:00), regla de festivos (`sin_turno` por defecto, o `turno_normal`), días de anticipación de los recordatorios (HU 2.11) y canal para externos (`@pii`) |
| `soporte_equipo` | Nombre (único), área, mínimo y máximo de personas por sábado (1 a 10), horario propio opcional (si no, el global), activo. La migración crea «Desarrollo TI» (1–1) y «Operaciones» (1–2) |
| `soporte_participante` | Equipo + persona (`plataforma_persona`), orden y activo; una persona puede estar en varios equipos |
| `soporte_turno` | Equipo, sábado, persona, horario propio, estado, origen, turno reemplazado, generación, observación (`@pii`) y «requiere revisión» (HU 2.7). Índice único parcial: una persona con a lo sumo un turno vigente por equipo y sábado; el máximo del equipo lo controla la aplicación |
| `soporte_ausencia` | Persona y rango (vacaciones, incapacidad, otra; el motivo es `@pii` y solo lo ve quien gestiona). Vale en todos sus equipos |
| `soporte_festivo` | Días no laborables adicionales (los de ley se calculan) |
| `soporte_compensacion` | Solo el modelo (turno, persona, día compensado, `pendiente \| tomada`, quién aprobó); el flujo es de la HU 2.9 |

Las llaves foráneas hacia `plataforma_persona` van a mano en la migración. Ninguna tabla tiene `organizacion_id`: la rotación es de empleados de FLIT y ningún externo llega a sus filas (los endpoints lo hacen cumplir con permisos que solo tienen los internos); por eso no llevan RLS. `solicitud_cambio`, `evento_calendario` y `feed_ics` del §9 llegan con sus HU (2.9 y 2.10). Toda acción que cambia datos queda en `plataforma_auditoria` **en la misma transacción** (el contexto de la plataforma usa la conexión y la transacción del de soporte), sin observaciones ni motivos de ausencia.

### Festivos (`FestivosColombia`)

Componente puro (sin reloj, base ni red): fijos (1 ene, 1 may, 20 jul, 7 ago, 8 dic, 25 dic), trasladables al lunes (6 ene, 19 mar, 29 jun, 9 jul desde 2026, 15 ago, 12 oct, 1 nov, 11 nov), Jueves y Viernes Santo y, en lunes, Ascensión (Pascua + 43), Corpus Christi (Pascua + 64) y Sagrado Corazón (Pascua + 71). Rango 1984–9999. Las pruebas fijan las listas oficiales completas de 2026 y 2027.

### Generación (`GeneradorRotacion`)

Puro y determinista, por equipo: recorre los sábados de la ventana; uno festivo con la regla `sin_turno` se salta; los turnos fijos (confirmados, aplicados, importados, manuales) se respetan y cuentan para la equidad; los puestos libres hasta el máximo del equipo se llenan con quien lleve **menos turnos en la ventana** y, si empatan, con quien sigue en el orden después del último elegido. Quien está ausente o ya tiene turno ese sábado en otro equipo no se asigna. Si no se llega al mínimo, el sábado queda `sin_disponibles` con un aviso. Solo se regeneran los turnos `generado` + `programado`; al confirmar se conservan los que siguen propuestos, se quitan los demás y se crean los nuevos. La confirmación recalcula el plan y exige la huella de la vista previa (409 si algo cambió).

### API y PWA

- 26 endpoints: configuración, equipos, participantes y orden, candidatos, turnos (por rango y equipo), este sábado (por equipo), turnos a mano, horario, estado, reemplazo, ausencias, festivos, vista previa y confirmación, indicadores y canal.
- PWA `apps/web/src/modulos/soporte/`: calendario en lista (por defecto en el móvil), semana y mes, filtrable por equipo con «Todos»; gestión (configuración, equipos, participantes, generación, ausencias y festivos); indicadores; widget «Soporte de este sábado» con a quién llamar por equipo; para externos, la página y el widget del canal. Todo con carga diferida; sin librería de calendario (las conocidas inyectan estilos que la CSP no permite, ATLAS-ADR-0020).

## Consecuencias

### Positivas

- Lo que se publica es exactamente lo que se vio, sin tablas de borradores.
- La equidad y los festivos tienen pruebas unitarias sin base; el componente de festivos lo reutilizan las HU 2.10 y 2.11.
- Cada equipo se gestiona y aprueba con permisos con alcance, sin código nuevo en Plataforma.
- Un turno por persona deja listo el evento de Outlook por asistente (HU 2.10) y el reemplazo de una sola persona (HU 2.9).

### Negativas

- El máximo de personas por sábado lo hace cumplir la aplicación, no la base (dos peticiones simultáneas podrían pasarlo; se revisa si pasa).
- La vista previa se recalcula al confirmar (dos lecturas del plan).
- Un festivo nuevo de ley exige cambiar el código; los días cívicos se agregan en `soporte_festivo`.
- La bandera viene encendida: en qa y pdn el módulo aparece con los dos equipos sin participantes hasta que alguien los agregue.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Turno con tabla hija de personas | Estados, reemplazos, compensaciones y eventos de calendario son por persona; habría que duplicar el estado en la hija |
| Tabla de borradores de generación | Más estado que limpiar y vencer; la huella logra lo mismo |
| Librería o servicio de festivos | Dependencia externa para 19 reglas; el §8.3 pide cálculo local |
| Aprobadores en la configuración | El líder pidió que apruebe quien tenga el permiso; ATLAS-ADR-0023 ya da alcances |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Soporte/`, `apps/web/src/modulos/soporte/`, `docs/permisos/matriz-rol-permiso.md`.
- Pendientes **[por verificar]**: estado de la demanda contra la Ley 2578 de 2026; concesiones de `atlas.soporte.ver_canal` a todos los externos; quién recibe `gestionar`/`aprobar` con alcance a «Operaciones»; si el máximo por sábado debe protegerse también en la base.
