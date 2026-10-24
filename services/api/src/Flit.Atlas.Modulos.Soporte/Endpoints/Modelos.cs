using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Flit.Atlas.Modulos.Soporte.Datos;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

// Contrato de /api/v1/soporte (contracts/openapi/atlas.v1.yaml, etiqueta «Soporte»). Las fechas son días de Bogotá
// (aaaa-mm-dd) y las horas, HH:mm en Bogotá.

[Description("Error de una operación de soporte.")]
public sealed record ErrorSoporte(
    [property: Description("Código estable del error.")] string Error,
    [property: Description("Mensaje para la persona, en español.")] string Mensaje);

[Description("Equipo de soporte de los sábados de un área.")]
public sealed record EquipoDto(
    [property: Description("Id del equipo.")] Guid Id,
    [property: Description("Nombre del equipo (p. ej. Desarrollo TI, Operaciones).")] string Nombre,
    [property: Description("Área a la que pertenece.")] string? Area,
    [property: Description("Mínimo de personas por sábado.")] int PersonasMinimo,
    [property: Description("Máximo de personas por sábado.")] int PersonasMaximo,
    [property: Description("Hora de inicio propia del equipo (HH:mm); vacía, la global.")] string? HoraInicio,
    [property: Description("Hora de fin propia del equipo (HH:mm); vacía, la global.")] string? HoraFin,
    [property: Description("Horario que se aplica a sus turnos nuevos: el propio o el global (HH:mm a HH:mm).")] string HorarioEfectivo,
    [property: Description("Si está activo (los inactivos no se generan ni se muestran en Inicio).")] bool Activo,
    [property: Description("Si la persona en sesión puede gestionar este equipo (atlas.soporte.gestionar global o con alcance al equipo).")] bool PuedeGestionar,
    [property: Description("Si la persona en sesión puede publicar su generación (atlas.soporte.aprobar global o con alcance al equipo).")] bool PuedeAprobar);

[Description("Equipo nuevo o cambio de un equipo (reemplaza todos sus datos).")]
public sealed record CambioEquipo(
    [property: Description("Nombre del equipo, único."), MinLength(1), MaxLength(SoporteDbContext.LargoNombreEquipo)] string? Nombre,
    [property: Description("Área."), MaxLength(SoporteDbContext.LargoNombreEquipo)] string? Area,
    [property: Description("Mínimo de personas por sábado (1 a 10)."), Range(1, SoporteDbContext.PersonasMaximasPorTurno)] int PersonasMinimo,
    [property: Description("Máximo de personas por sábado (mínimo a 10)."), Range(1, SoporteDbContext.PersonasMaximasPorTurno)] int PersonasMaximo,
    [property: Description("Hora de inicio propia (HH:mm); vacía, la global."), MaxLength(5)] string? HoraInicio,
    [property: Description("Hora de fin propia (HH:mm); vacía, la global."), MaxLength(5)] string? HoraFin,
    [property: Description("Si está activo; vacío, activo.")] bool? Activo);

[Description("Equipo de un turno.")]
public sealed record EquipoResumenDto(
    [property: Description("Id del equipo.")] Guid Id,
    [property: Description("Nombre del equipo.")] string Nombre);

[Description("Persona de un turno. El correo y el cargo los ve todo interno con atlas.soporte.ver; el teléfono, solo con atlas.equipo.ver_contacto.")]
public sealed record PersonaSoporteDto(
    [property: Description("Id de la persona (plataforma_persona).")] Guid Id,
    [property: Description("Nombre de la persona.")] string Nombre,
    [property: Description("Correo de la persona.")] string? Correo,
    [property: Description("Cargo, si se conoce.")] string? Cargo,
    [property: Description("Teléfono; vacío sin atlas.equipo.ver_contacto.")] string? Telefono);

[Description("Turno de una persona de un equipo en un sábado.")]
public sealed record TurnoDto(
    [property: Description("Id del turno.")] Guid Id,
    [property: Description("Equipo del turno.")] EquipoResumenDto Equipo,
    [property: Description("Sábado del turno (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Persona de turno.")] PersonaSoporteDto Persona,
    [property: Description("Hora de inicio en Bogotá (HH:mm).")] string? HoraInicio,
    [property: Description("Hora de fin en Bogotá (HH:mm).")] string? HoraFin,
    [property: Description("programado, confirmado, aplicado, reemplazado o cancelado.")] string Estado,
    [property: Description("importado, generado o manual.")] string Origen,
    [property: Description("Turno al que este reemplaza, si es un reemplazo.")] Guid? TurnoReemplazadoId,
    [property: Description("Nombre del festivo si el sábado lo es.")] string? Festivo,
    [property: Description("Observación del turno.")] string? Observacion,
    [property: Description("Marcado para revisión humana (importación del Excel).")] bool RequiereRevision);

[Description("Festivo de Colombia (Ley 51 de 1983 y Ley 2578 de 2026) o día adicional.")]
public sealed record FestivoDto(
    [property: Description("Día del festivo (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Nombre del festivo.")] string Nombre,
    [property: Description("fijo, trasladable_al_lunes, semana_santa, relativo_a_pascua o adicional.")] string Tipo,
    [property: Description("Si la ley lo pasó al lunes.")] bool Trasladado,
    [property: Description("Id del día adicional; vacío para los festivos de ley.")] Guid? Id);

[Description("Turnos y festivos de un rango de fechas, para las vistas de lista, semana y mes.")]
public sealed record CalendarioDto(
    [property: Description("Primer día del rango.")] DateOnly Desde,
    [property: Description("Último día del rango (incluido).")] DateOnly Hasta,
    [property: Description("Equipo filtrado; vacío, todos.")] Guid? EquipoId,
    [property: Description("Turnos del rango, por fecha y equipo; incluye reemplazados y cancelados como historia.")] IReadOnlyList<TurnoDto> Turnos,
    [property: Description("Festivos del rango.")] IReadOnlyList<FestivoDto> Festivos);

[Description("A quién llamar este sábado en un equipo.")]
public sealed record EquipoEsteSabadoDto(
    [property: Description("El equipo.")] EquipoResumenDto Equipo,
    [property: Description("Turnos vigentes del equipo ese sábado (de 0 al máximo del equipo).")] IReadOnlyList<TurnoDto> Turnos,
    [property: Description("Personas que faltan para el mínimo del equipo.")] int Faltan);

[Description("Soporte del próximo sábado (hoy, si es sábado), por equipo: lo que muestra el widget de Inicio.")]
public sealed record EsteSabadoDto(
    [property: Description("El sábado (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Nombre del festivo si el sábado lo es.")] string? Festivo,
    [property: Description("Cada equipo activo con sus turnos de ese sábado.")] IReadOnlyList<EquipoEsteSabadoDto> Equipos);

[Description("Canal de soporte del sábado para externos: sin nombres de personas.")]
public sealed record CanalSoporteDto(
    [property: Description("El próximo sábado (hoy, si es sábado).")] DateOnly Fecha,
    [property: Description("Si se configuró un canal para externos.")] bool Configurado,
    [property: Description("Canal de contacto (correo, línea o canal); vacío si no está configurado.")] string? Canal,
    [property: Description("Hora de inicio del soporte ese sábado (HH:mm), si se conoce.")] string? HoraInicio,
    [property: Description("Hora de fin del soporte ese sábado (HH:mm), si se conoce.")] string? HoraFin,
    [property: Description("Si ese sábado hay soporte programado.")] bool HaySoporte);

[Description("Configuración global del soporte de los sábados.")]
public sealed record ConfiguracionDto(
    [property: Description("Hora de inicio por defecto (HH:mm, Bogotá).")] string HoraInicioPorDefecto,
    [property: Description("Hora de fin por defecto (HH:mm, Bogotá).")] string HoraFinPorDefecto,
    [property: Description("sin_turno o turno_normal: qué hace la generación con un sábado festivo.")] string ReglaFestivos,
    [property: Description("Días antes del turno en que sale el recordatorio a la persona (HU 2.11); vacío, solo el del mismo sábado.")] int? DiasAnticipacionRecordatorio,
    [property: Description("Canal de soporte del sábado que ven los externos; vacío, no ven nada.")] string? CanalExterno,
    [property: Description("Cuándo se guardó por última vez; vacío si nunca (valores por defecto).")] DateTimeOffset? ActualizadoEn,
    [property: Description("Hora (HH:mm, Bogotá) a la que corre cada día el trabajo de recordatorios (HU 2.11); 07:00 si nunca se cambió.")] string HoraRecordatorios);

[Description("Configuración global nueva: reemplaza toda la anterior.")]
public sealed record CambioConfiguracion(
    [property: Description("Hora de inicio por defecto (HH:mm, Bogotá)."), MaxLength(5)] string? HoraInicioPorDefecto,
    [property: Description("Hora de fin por defecto (HH:mm, Bogotá); posterior al inicio."), MaxLength(5)] string? HoraFinPorDefecto,
    [property: Description("sin_turno o turno_normal."), MaxLength(20)] string? ReglaFestivos,
    [property: Description("Días de anticipación de los recordatorios, de 1 a 30."), Range(1, 30)] int? DiasAnticipacionRecordatorio,
    [property: Description("Canal de soporte del sábado para externos, sin nombres de personas."), MaxLength(SoporteDbContext.LargoCanal)] string? CanalExterno,
    [property: Description("Hora (HH:mm, Bogotá) del trabajo diario de recordatorios; vacío, 07:00."), MaxLength(5)] string? HoraRecordatorios = null);

[Description("Participante de la rotación de un equipo.")]
public sealed record ParticipanteDto(
    [property: Description("Id del participante.")] Guid Id,
    [property: Description("Id del equipo.")] Guid EquipoId,
    [property: Description("Id de la persona (plataforma_persona).")] Guid PersonaId,
    [property: Description("Nombre de la persona.")] string Nombre,
    [property: Description("Correo de la persona.")] string Correo,
    [property: Description("Puesto en la rotación del equipo, desde 1.")] int Orden,
    [property: Description("Si entra en la generación.")] bool Activo,
    [property: Description("Estado de la persona en Atlas: activa, inactiva o suspendida.")] string EstadoPersona);

[Description("Persona interna que se agrega a la rotación del equipo (queda de última y activa).")]
public sealed record NuevoParticipante([property: Description("Id de la persona interna.")] Guid PersonaId);

[Description("Cambio de estado de un participante.")]
public sealed record CambioParticipante([property: Description("Si entra en la generación.")] bool Activo);

[Description("Nuevo orden de la rotación del equipo: todos sus participantes, del primero al último.")]
public sealed record OrdenParticipantes(
    [property: Description("Ids de los participantes en el orden nuevo."), MaxLength(200)] IReadOnlyList<Guid>? Participantes);

[Description("Persona interna que se puede agregar a la rotación.")]
public sealed record CandidatoDto(
    [property: Description("Id de la persona.")] Guid Id,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Correo.")] string Correo,
    [property: Description("Cargo, si se conoce.")] string? Cargo);

[Description("Turno nuevo creado a mano (origen manual, estado programado).")]
public sealed record NuevoTurno(
    [property: Description("Equipo del turno.")] Guid EquipoId,
    [property: Description("Sábado del turno (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Persona de turno: un participante del equipo.")] Guid PersonaId,
    [property: Description("Hora de inicio (HH:mm); vacía, la del equipo o la global."), MaxLength(5)] string? HoraInicio,
    [property: Description("Hora de fin (HH:mm); vacía, la del equipo o la global."), MaxLength(5)] string? HoraFin,
    [property: Description("Observación."), MaxLength(SoporteDbContext.LargoObservacion)] string? Observacion);

[Description("Horario y observación de un turno que aún no está aplicado. Reemplaza los valores anteriores.")]
public sealed record CambioTurno(
    [property: Description("Hora de inicio (HH:mm); vacía, sin horario."), MaxLength(5)] string? HoraInicio,
    [property: Description("Hora de fin (HH:mm); vacía, sin horario."), MaxLength(5)] string? HoraFin,
    [property: Description("Observación."), MaxLength(SoporteDbContext.LargoObservacion)] string? Observacion);

[Description("Cambio de estado de un turno: confirmado, aplicado o cancelado (el reemplazo tiene su propia operación).")]
public sealed record CambioEstadoTurno(
    [property: Description("confirmado, aplicado o cancelado."), MaxLength(20)] string? Estado,
    [property: Description("Observación (se agrega a la del turno)."), MaxLength(SoporteDbContext.LargoObservacion)] string? Observacion);

[Description("Reemplazo de la persona de un turno por otra del mismo equipo.")]
public sealed record ReemplazoTurno(
    [property: Description("Persona que toma el turno.")] Guid PersonaId,
    [property: Description("Observación."), MaxLength(SoporteDbContext.LargoObservacion)] string? Observacion);

[Description("Ausencia de una persona (vale en todos sus equipos).")]
public sealed record AusenciaDto(
    [property: Description("Id de la ausencia.")] Guid Id,
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Nombre de la persona.")] string Nombre,
    [property: Description("Primer día (aaaa-mm-dd).")] DateOnly Desde,
    [property: Description("Último día, incluido (aaaa-mm-dd).")] DateOnly Hasta,
    [property: Description("vacaciones, incapacidad u otra.")] string Tipo);

[Description("Ausencia nueva.")]
public sealed record NuevaAusencia(
    [property: Description("Persona que participa en algún equipo.")] Guid PersonaId,
    [property: Description("Primer día (aaaa-mm-dd).")] DateOnly Desde,
    [property: Description("Último día, incluido (aaaa-mm-dd); máximo un año después del primero.")] DateOnly Hasta,
    [property: Description("vacaciones, incapacidad u otra."), MaxLength(20)] string? Tipo);

[Description("Día no laborable adicional (p. ej. un día cívico decretado).")]
public sealed record NuevoFestivo(
    [property: Description("Día (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Nombre del día."), MinLength(1), MaxLength(SoporteDbContext.LargoNombreFestivo)] string? Nombre);

[Description("Ventana de la generación automática de un equipo.")]
public sealed record SolicitudGeneracion(
    [property: Description("Equipo que se genera.")] Guid EquipoId,
    [property: Description("Meses hacia adelante, de 1 a 12."), Range(1, 12)] int Meses,
    [property: Description("Primer día de la ventana; vacío, mañana. Debe ser posterior a hoy.")] DateOnly? Desde);

[Description("Confirmación de una vista previa: el mismo equipo, la misma ventana y la huella que devolvió.")]
public sealed record ConfirmacionGeneracion(
    [property: Description("Equipo (el mismo de la vista previa).")] Guid EquipoId,
    [property: Description("Meses hacia adelante, de 1 a 12 (los mismos de la vista previa)."), Range(1, 12)] int Meses,
    [property: Description("Primer día de la ventana (el mismo de la vista previa).")] DateOnly? Desde,
    [property: Description("Huella de la vista previa: si algo cambió desde entonces, la confirmación se rechaza."), MaxLength(64)] string? Huella);

[Description("Persona de un sábado de la vista previa.")]
public sealed record PersonaPreviaDto(
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Si es un turno nuevo (falso: ya publicado y no cambia).")] bool Nueva);

[Description("Un sábado de la vista previa.")]
public sealed record SabadoPreviaDto(
    [property: Description("El sábado.")] DateOnly Fecha,
    [property: Description("asignado (con turnos nuevos), fijo (ya completo, no cambia), festivo (sin turno por la regla) o sin_disponibles (no se llega al mínimo).")] string Decision,
    [property: Description("Personas de turno ese sábado: las que ya están y las nuevas.")] IReadOnlyList<PersonaPreviaDto> Personas,
    [property: Description("Nombre del festivo, si lo es.")] string? Festivo,
    [property: Description("Si confirmar cambia lo que hoy está publicado para ese sábado.")] bool Cambia,
    [property: Description("Un turno fijo es de alguien con una ausencia ese día (revisar a mano).")] bool AusenteEnFijo,
    [property: Description("Personas que faltan para el mínimo del equipo.")] int Faltan);

[Description("Turnos de una persona en la ventana.")]
public sealed record ConteoPersonaDto(
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Turnos en la ventana (fijos y nuevos).")] int Turnos);

[Description("Vista previa de la generación de un equipo: nada se publica hasta que alguien con permiso de aprobar la confirme.")]
public sealed record VistaPreviaDto(
    [property: Description("Equipo.")] EquipoResumenDto Equipo,
    [property: Description("Primer día de la ventana.")] DateOnly Desde,
    [property: Description("Último día de la ventana (incluido).")] DateOnly Hasta,
    [property: Description("Huella para confirmar exactamente esta propuesta.")] string Huella,
    [property: Description("Cada sábado de la ventana.")] IReadOnlyList<SabadoPreviaDto> Sabados,
    [property: Description("Turnos por persona en la ventana.")] IReadOnlyList<ConteoPersonaDto> TurnosPorPersona,
    [property: Description("Diferencia entre quien más y quien menos turnos tiene.")] int Desbalance,
    [property: Description("Turnos nuevos que se crearían.")] int Nuevos,
    [property: Description("Turnos generados antes que se quitarían (cambio de persona, festivo o sin disponibles).")] int Eliminados,
    [property: Description("Avisos para quien publica.")] IReadOnlyList<string> Avisos);

[Description("Resultado de publicar la generación.")]
public sealed record ResultadoGeneracionDto(
    [property: Description("Id de esta generación (queda en cada turno creado).")] Guid GeneracionId,
    [property: Description("Turnos creados.")] int Creados,
    [property: Description("Turnos generados antes que se quitaron.")] int Eliminados);

[Description("Indicadores de una persona en el rango.")]
public sealed record IndicadorPersonaDto(
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Turnos vigentes asignados (programados, confirmados y aplicados).")] int Turnos,
    [property: Description("Turnos aplicados.")] int Aplicados,
    [property: Description("Turnos suyos que tomó otra persona.")] int Reemplazados,
    [property: Description("Turnos que tomó en reemplazo de otra persona.")] int ReemplazosHechos,
    [property: Description("Turnos suyos cancelados.")] int Cancelados,
    [property: Description("Compensaciones pendientes.")] int CompensacionesPendientes,
    [property: Description("Aplicados sobre sus turnos ya pasados (0 a 1); vacío si aún no tiene turnos pasados.")] double? Cumplimiento);

[Description("Indicadores básicos del soporte: turnos por persona, reemplazos, compensaciones pendientes y cumplimiento.")]
public sealed record IndicadoresDto(
    [property: Description("Primer día del rango.")] DateOnly Desde,
    [property: Description("Último día del rango (incluido).")] DateOnly Hasta,
    [property: Description("Equipo filtrado; vacío, todos.")] Guid? EquipoId,
    [property: Description("Por persona, de más a menos turnos.")] IReadOnlyList<IndicadorPersonaDto> Personas,
    [property: Description("Turnos vigentes del rango.")] int Turnos,
    [property: Description("Reemplazos del rango.")] int Reemplazos,
    [property: Description("Compensaciones pendientes (de todos los tiempos).")] int CompensacionesPendientes,
    [property: Description("Aplicados sobre los turnos ya pasados del rango (0 a 1); vacío si no hay.")] double? Cumplimiento);
