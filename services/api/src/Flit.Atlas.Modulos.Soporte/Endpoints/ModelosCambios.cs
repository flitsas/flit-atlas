using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Flit.Atlas.Modulos.Soporte.Datos;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

// Contrato de los cambios de turno y las compensaciones (HU 2.9, ATLAS-ADR-0029 y ATLAS-ADR-0032). Cada acción sobre una
// solicitud lleva una llave de idempotencia que genera el cliente: si la PWA la reenvía al reconectar, el servidor
// responde lo mismo sin repetirla.

[Description("Persona de un paso del historial.")]
public sealed record PersonaResumenDto(
    [property: Description("Id de la persona.")] Guid Id,
    [property: Description("Nombre.")] string Nombre);

[Description("Paso del historial de una solicitud de cambio.")]
public sealed record EventoSolicitudDto(
    [property: Description("solicitada, aceptada, rechazada_por_reemplazo, aprobada, rechazada o cancelada.")] string Accion,
    [property: Description("Quién lo hizo.")] PersonaResumenDto Persona,
    [property: Description("Nota de quien lo hizo.")] string? Nota,
    [property: Description("Cuándo llegó al servidor.")] DateTimeOffset OcurridoEn,
    [property: Description("Cuándo lo hizo la persona en su dispositivo, si lo hizo sin conexión.")] DateTimeOffset? HechaEn);

[Description("Solicitud de cambio de turno: la persona de turno propone un reemplazo, el reemplazo acepta y alguien con permiso aprueba.")]
public sealed record SolicitudCambioDto(
    [property: Description("Id de la solicitud.")] Guid Id,
    [property: Description("Turno que se quiere cambiar.")] TurnoDto Turno,
    [property: Description("Quien pide el cambio (la persona de turno).")] PersonaSoporteDto Solicitante,
    [property: Description("Quien tomaría el turno.")] PersonaSoporteDto Reemplazo,
    [property: Description("pendiente_reemplazo, pendiente_aprobacion, aprobada, rechazada o cancelada.")] string Estado,
    [property: Description("Motivo del cambio.")] string? Motivo,
    [property: Description("Turno creado para el reemplazo al aprobar.")] Guid? TurnoNuevoId,
    [property: Description("Quien aprobó o rechazó.")] PersonaSoporteDto? ResueltaPor,
    [property: Description("Cuándo se aprobó o rechazó.")] DateTimeOffset? ResueltaEn,
    [property: Description("Cuándo se creó.")] DateTimeOffset CreadaEn,
    [property: Description("Historial completo: quién, cuándo y su nota.")] IReadOnlyList<EventoSolicitudDto> Historial,
    [property: Description("Si la persona en sesión (el reemplazo) puede aceptarla o rechazarla.")] bool PuedeResponder,
    [property: Description("Si la persona en sesión puede aprobarla o rechazarla (atlas.soporte.aprobar global o con alcance al equipo).")] bool PuedeResolver,
    [property: Description("Si la persona en sesión (quien la pidió) puede cancelarla.")] bool PuedeCancelar);

[Description("Solicitud nueva de cambio de turno.")]
public sealed record NuevaSolicitudCambio(
    [property: Description("Turno de la persona en sesión.")] Guid TurnoId,
    [property: Description("Persona del mismo equipo que tomaría el turno.")] Guid ReemplazoPersonaId,
    [property: Description("Motivo."), MaxLength(SoporteDbContext.LargoObservacion)] string? Motivo,
    [property: Description("Llave de idempotencia de la acción (uuid que genera el cliente).")] Guid LlaveIdempotencia,
    [property: Description("Cuándo la hizo la persona, si la hizo sin conexión.")] DateTimeOffset? HechaEn);

[Description("Respuesta del reemplazo: acepta o rechaza tomar el turno.")]
public sealed record RespuestaSolicitud(
    [property: Description("Si acepta.")] bool Acepta,
    [property: Description("Nota."), MaxLength(SoporteDbContext.LargoObservacion)] string? Nota,
    [property: Description("Llave de idempotencia de la acción.")] Guid LlaveIdempotencia,
    [property: Description("Cuándo la hizo la persona, si la hizo sin conexión.")] DateTimeOffset? HechaEn);

[Description("Decisión de quien aprueba.")]
public sealed record ResolucionSolicitud(
    [property: Description("Si aprueba.")] bool Aprueba,
    [property: Description("Nota."), MaxLength(SoporteDbContext.LargoObservacion)] string? Nota,
    [property: Description("Llave de idempotencia de la acción.")] Guid LlaveIdempotencia,
    [property: Description("Cuándo la hizo la persona, si la hizo sin conexión.")] DateTimeOffset? HechaEn);

[Description("Cancelación de una solicitud por quien la pidió.")]
public sealed record CancelacionSolicitud(
    [property: Description("Nota."), MaxLength(SoporteDbContext.LargoObservacion)] string? Nota,
    [property: Description("Llave de idempotencia de la acción.")] Guid LlaveIdempotencia,
    [property: Description("Cuándo la hizo la persona, si la hizo sin conexión.")] DateTimeOffset? HechaEn);

[Description("Turno de una compensación.")]
public sealed record TurnoResumenDto(
    [property: Description("Id del turno.")] Guid Id,
    [property: Description("Sábado del turno.")] DateOnly Fecha,
    [property: Description("Equipo del turno.")] EquipoResumenDto Equipo);

[Description("Día compensado por un turno.")]
public sealed record CompensacionDto(
    [property: Description("Id de la compensación.")] Guid Id,
    [property: Description("Turno por el que se compensa.")] TurnoResumenDto Turno,
    [property: Description("A quién se compensa.")] PersonaSoporteDto Persona,
    [property: Description("Día compensado; vacío mientras no se elija.")] DateOnly? FechaCompensada,
    [property: Description("pendiente o tomada.")] string Estado,
    [property: Description("Quien la aprobó; vacío si vino del Excel sin ese dato.")] PersonaSoporteDto? AprobadaPor,
    [property: Description("Nota; solo la ven la persona compensada y quien aprueba o gestiona el equipo.")] string? Nota,
    [property: Description("Si la persona en sesión puede cambiarla (atlas.soporte.aprobar sobre el equipo).")] bool PuedeCambiar,
    [property: Description("Cuándo se registró.")] DateTimeOffset CreadaEn);

[Description("Compensación nueva para la persona de un turno.")]
public sealed record NuevaCompensacion(
    [property: Description("Turno por el que se compensa (a su persona).")] Guid TurnoId,
    [property: Description("Día compensado, si ya se sabe.")] DateOnly? FechaCompensada,
    [property: Description("Nota."), MaxLength(SoporteDbContext.LargoObservacion)] string? Nota);

[Description("Cambio de una compensación: el día y el estado (tomada exige el día).")]
public sealed record CambioCompensacion(
    [property: Description("Día compensado.")] DateOnly? FechaCompensada,
    [property: Description("pendiente o tomada."), MaxLength(20)] string? Estado,
    [property: Description("Nota."), MaxLength(SoporteDbContext.LargoObservacion)] string? Nota);

[Description("Compensaciones pendientes de una persona.")]
public sealed record PendientesPersonaDto(
    [property: Description("La persona.")] PersonaSoporteDto Persona,
    [property: Description("Compensaciones pendientes.")] int Pendientes,
    [property: Description("Pendientes sin día elegido.")] int SinFecha,
    [property: Description("Próximo día compensado (hoy o después), si hay.")] DateOnly? Proxima,
    [property: Description("Sus compensaciones pendientes.")] IReadOnlyList<CompensacionDto> Compensaciones);

[Description("Reporte de compensaciones pendientes por persona.")]
public sealed record ReporteCompensacionesDto(
    [property: Description("Equipo filtrado; vacío, todos.")] Guid? EquipoId,
    [property: Description("Total de compensaciones pendientes.")] int Total,
    [property: Description("Por persona, de más a menos pendientes.")] IReadOnlyList<PendientesPersonaDto> Personas);
