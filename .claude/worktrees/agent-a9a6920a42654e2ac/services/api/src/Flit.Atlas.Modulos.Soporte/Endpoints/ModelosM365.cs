using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

// Contrato de Microsoft 365 del soporte (HU 2.10, ATLAS-ADR-0033).

[Description("Estado de la integración del soporte con Microsoft 365 en este ambiente.")]
public sealed record EstadoM365Dto(
    [property: Description("Calendario compartido de Outlook.")] EstadoCalendarioM365Dto Calendario,
    [property: Description("Avisos en el canal de Teams.")] EstadoTeamsM365Dto Teams);

[Description("Estado del calendario compartido «Soporte TI sábados».")]
public sealed record EstadoCalendarioM365Dto(
    [property: Description("Si los turnos se sincronizan con el calendario compartido.")] bool Activo,
    [property: Description("Por qué está apagado; vacío si está activo.")] string? Motivo,
    [property: Description("Nombre del calendario.")] string NombreCalendario,
    [property: Description("Turnos con cambios que aún no llegan a Outlook.")] int Pendientes,
    [property: Description("De esos, los que fallaron al menos una vez (se reintentan solos).")] int ConErrores,
    [property: Description("Última vez que un evento quedó sincronizado.")] DateTimeOffset? UltimaSincronizacion);

[Description("Estado de los avisos en el canal de Teams del soporte.")]
public sealed record EstadoTeamsM365Dto(
    [property: Description("Si los avisos salen al canal.")] bool Activo,
    [property: Description("Por qué está apagado; vacío si está activo.")] string? Motivo,
    [property: Description("Avisos en cola.")] int Pendientes);

[Description("Feed .ics vigente de la persona en sesión (sin su URL).")]
public sealed record FeedIcsDto(
    [property: Description("Id del feed.")] Guid Id,
    [property: Description("personal (mis turnos) o equipo.")] string Tipo,
    [property: Description("Equipo del feed de equipo; vacío en el personal.")] EquipoResumenDto? Equipo,
    [property: Description("Cuándo se creó o se regeneró.")] DateTimeOffset CreadoEn,
    [property: Description("Última vez que un calendario lo leyó (con precisión de una hora).")] DateTimeOffset? UltimoUsoEn);

[Description("Feed .ics recién creado: la URL lleva el token y no se vuelve a mostrar.")]
public sealed record FeedIcsCreadoDto(
    [property: Description("Id del feed.")] Guid Id,
    [property: Description("personal o equipo.")] string Tipo,
    [property: Description("Equipo del feed de equipo.")] EquipoResumenDto? Equipo,
    [property: Description("URL HTTPS para suscribirse desde Outlook, Google Calendar o el teléfono.")] string Url,
    [property: Description("La misma URL con el esquema webcal:// (abre la app de calendario).")] string UrlWebcal,
    [property: Description("Cuándo se creó.")] DateTimeOffset CreadoEn);

[Description("Feed .ics que se crea o se regenera.")]
public sealed record SolicitudFeedIcs(
    [property: Description("personal o equipo."), MaxLength(20)] string? Tipo,
    [property: Description("Equipo del feed de equipo.")] Guid? EquipoId);
