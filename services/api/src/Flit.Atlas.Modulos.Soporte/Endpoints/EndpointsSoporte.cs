using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Endpoints de <c>/api/v1/soporte</c> (HU 2.6 y 2.8, ATLAS-ADR-0029). El host ya puso la etiqueta, la sesión y la
/// bandera; aquí cada endpoint declara su permiso. Leer exige <c>atlas.soporte.ver</c> (todo empleado). Lo de un equipo
/// (participantes, turnos a mano, vista previa) exige <c>atlas.soporte.gestionar</c> global o con alcance
/// <c>equipo_soporte:&lt;id&gt;</c>; publicar su generación, <c>atlas.soporte.aprobar</c> igual. La configuración global,
/// los equipos nuevos, las ausencias y los festivos adicionales exigen <c>atlas.soporte.gestionar</c> global. Un
/// externo solo llega a <c>/canal</c> (<c>atlas.soporte.ver_canal</c>), sin nombres.
/// </summary>
internal static class EndpointsSoporte
{
    public const int LimiteCandidatos = 20;

    private const string SoloGlobal = "Esta acción exige atlas.soporte.gestionar sin alcance a un solo equipo.";
    private const string SoloDelEquipo = "No tienes atlas.soporte.gestionar sobre este equipo.";

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/configuracion", ObtenerConfiguracionAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ObtenerConfiguracionSoporte")
            .WithSummary("Configuración del soporte")
            .WithDescription(
                "Horario por defecto (08:00 a 12:00 en Bogotá si nunca se cambió), regla de festivos (por defecto, sin turno), " +
                "anticipación y hora de los recordatorios (07:00 si nunca se cambió) y canal para externos. Requiere atlas.soporte.ver.")
            .Produces<ConfiguracionDto>();

        grupo.MapPut("/configuracion", GuardarConfiguracionAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("GuardarConfiguracionSoporte")
            .WithSummary("Guardar la configuración del soporte")
            .WithDescription(
                "Reemplaza la configuración global. No cambia el horario de los turnos que ya existen. Queda en la auditoría. " +
                "Requiere atlas.soporte.gestionar con alcance global (403 si solo lo tienes sobre un equipo).")
            .Produces<ConfiguracionDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        MapearEquipos(grupo);
        MapearParticipantes(grupo);
        EndpointsTurnos.Mapear(grupo);
        EndpointsGeneracion.Mapear(grupo);
        EndpointsConsultas.Mapear(grupo);
        EndpointsImportacion.Mapear(grupo);
        EndpointsCambios.Mapear(grupo);
        EndpointsM365.Mapear(grupo);
        EndpointsRecordatorios.Mapear(grupo);
    }

    private static void MapearEquipos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/equipos", ListarEquiposAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarEquiposSoporte")
            .WithSummary("Equipos de soporte")
            .WithDescription(
                "Equipos de soporte de los sábados por área, con cuántas personas van por sábado, su horario y si la persona " +
                "en sesión los puede gestionar o aprobar. Requiere atlas.soporte.ver.")
            .Produces<List<EquipoDto>>();

        grupo.MapPost("/equipos", CrearEquipoAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CrearEquipoSoporte")
            .WithSummary("Crear un equipo de soporte")
            .WithDescription(
                "Equipo nuevo con su área, mínimo y máximo de personas por sábado (1 a 10) y, si quiere, su horario propio. 409 " +
                "si el nombre ya existe. Requiere atlas.soporte.gestionar con alcance global.")
            .Produces<EquipoDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/equipos/{id:guid}", CambiarEquipoAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CambiarEquipoSoporte")
            .WithSummary("Cambiar un equipo de soporte")
            .WithDescription(
                "Reemplaza los datos del equipo. Sus turnos ya creados no cambian. Requiere atlas.soporte.gestionar global o " +
                "con alcance al equipo.")
            .Produces<EquipoDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    private static void MapearParticipantes(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/equipos/{equipoId:guid}/participantes", ListarParticipantesAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarParticipantesSoporte")
            .WithSummary("Participantes de un equipo")
            .WithDescription("Personas de la rotación del equipo en su orden, activas e inactivas. Requiere atlas.soporte.ver.")
            .Produces<List<ParticipanteDto>>()
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        grupo.MapPost("/equipos/{equipoId:guid}/participantes", AgregarParticipanteAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("AgregarParticipanteSoporte")
            .WithSummary("Agregar a la rotación de un equipo")
            .WithDescription(
                "Agrega una persona interna activa al final del orden del equipo, activa. Una persona puede estar en varios " +
                "equipos. 409 si ya está en este. Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<ParticipanteDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/equipos/{equipoId:guid}/participantes/orden", OrdenarParticipantesAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("OrdenarParticipantesSoporte")
            .WithSummary("Cambiar el orden de la rotación de un equipo")
            .WithDescription(
                "Recibe todos los participantes del equipo en el orden nuevo (sin faltantes ni repetidos). La generación sigue " +
                "este orden. Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<List<ParticipanteDto>>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapPut("/participantes/{id:guid}", CambiarParticipanteAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CambiarParticipanteSoporte")
            .WithSummary("Activar o desactivar un participante")
            .WithDescription(
                "Un participante inactivo conserva su historial y no entra en la generación; sus turnos ya creados no cambian. " +
                "Requiere atlas.soporte.gestionar global o con alcance a su equipo.")
            .Produces<ParticipanteDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        grupo.MapGet("/candidatos", BuscarCandidatosAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("BuscarCandidatosSoporte")
            .WithSummary("Buscar personas para la rotación")
            .WithDescription(
                $"Personas internas activas, por nombre o correo, sin distinguir mayúsculas ni tildes (mínimo 2 caracteres, hasta " +
                $"{LimiteCandidatos} resultados); con equipo, sin las que ya están en él. Requiere atlas.soporte.gestionar.")
            .Produces<List<CandidatoDto>>()
            .ProducesValidationProblem();
    }

    // --- Configuración ---

    private static async Task<IResult> ObtenerConfiguracionAsync(SoporteDbContext soporte, CancellationToken ct) =>
        Results.Ok(ConfiguracionDto(await ConfiguracionAsync(soporte, ct)));

    internal static ConfiguracionDto ConfiguracionDto(ConfiguracionSoporte c) => new(
        Hora(c.HoraInicioPorDefecto)!, Hora(c.HoraFinPorDefecto)!, ReglasTurno.Texto(c.ReglaFestivos),
        c.DiasAnticipacionRecordatorio, c.CanalExterno, c.CreadoEn == default ? null : c.ActualizadoEn, Hora(c.HoraRecordatorios)!);

    private static async Task<IResult> GuardarConfiguracionAsync(
        [Description("Configuración global del soporte.")] CambioConfiguracion cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await PuedeGlobalAsync(permisos, http.User, PermisosSoporte.Gestionar, ct))
        {
            return SinPermiso(SoloGlobal);
        }
        var errores = new Dictionary<string, string[]>();
        var (inicio, fin) = LeerHorario(cambio.HoraInicioPorDefecto, cambio.HoraFinPorDefecto, "horaInicioPorDefecto", "horaFinPorDefecto", errores);
        if (!errores.ContainsKey("horaInicioPorDefecto") && !errores.ContainsKey("horaFinPorDefecto") && inicio is null)
        {
            errores["horaInicioPorDefecto"] = ["El horario por defecto lleva hora de inicio y de fin."];
        }
        var regla = ReglasTurno.Leer<ReglaFestivos>(cambio.ReglaFestivos);
        if (regla is null)
        {
            errores["reglaFestivos"] = [$"La regla de festivos es una de: {ReglasTurno.Valores<ReglaFestivos>()}."];
        }
        if (cambio.DiasAnticipacionRecordatorio is < 1 or > 30)
        {
            errores["diasAnticipacionRecordatorio"] = ["La anticipación de los recordatorios va de 1 a 30 días."];
        }
        if (!LeerHora(cambio.HoraRecordatorios, out var horaRecordatorios))
        {
            errores["horaRecordatorios"] = ["La hora de los recordatorios va en formato HH:mm (por ejemplo, 07:00)."];
        }
        var canal = Limpiar(cambio.CanalExterno);
        if (canal is { Length: > SoporteDbContext.LargoCanal })
        {
            errores["canalExterno"] = [$"El canal tiene máximo {SoporteDbContext.LargoCanal} caracteres."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var configuracion = await soporte.Configuraciones.FirstOrDefaultAsync(ct);
        if (configuracion is null)
        {
            configuracion = new ConfiguracionSoporte();
            soporte.Configuraciones.Add(configuracion);
        }
        configuracion.HoraInicioPorDefecto = inicio!.Value;
        configuracion.HoraFinPorDefecto = fin!.Value;
        configuracion.ReglaFestivos = regla!.Value;
        configuracion.DiasAnticipacionRecordatorio = cambio.DiasAnticipacionRecordatorio;
        configuracion.CanalExterno = canal;
        configuracion.HoraRecordatorios = horaRecordatorios ?? ConfiguracionSoporte.HoraRecordatoriosPorDefecto;
        configuracion.ActualizadoPorPersonaId = PersonaDeSesion(http.User);
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "configuracion_actualizada", "soporte_configuracion", configuracion.Id, new
            {
                horaInicio = Hora(inicio),
                horaFin = Hora(fin),
                regla = ReglasTurno.Texto(regla.Value),
                dias = cambio.DiasAnticipacionRecordatorio,
                horaRecordatorios = Hora(configuracion.HoraRecordatorios),
                canalExterno = canal is not null,
            })], ct);
        return Results.Ok(ConfiguracionDto(configuracion));
    }

    // --- Equipos ---

    private static EquipoDto Dto(EquipoSoporte e, ConfiguracionSoporte configuracion, PermisosEfectivos permisos)
    {
        var (inicio, fin) = HorarioDe(e, configuracion);
        return new EquipoDto(e.Id, e.Nombre, e.Area, e.PersonasMinimo, e.PersonasMaximo, Hora(e.HoraInicio), Hora(e.HoraFin),
            $"{Hora(inicio)} a {Hora(fin)}", e.Activo,
            permisos.TieneEn(PermisosSoporte.Gestionar, Alcance(e.Id)), permisos.TieneEn(PermisosSoporte.Aprobar, Alcance(e.Id)));
    }

    private static async Task<IResult> ListarEquiposAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, CancellationToken ct)
    {
        var efectivos = await permisos.ObtenerAsync(usuario, ct);
        var configuracion = await ConfiguracionAsync(soporte, ct);
        var equipos = await soporte.Equipos.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(ct);
        return Results.Ok(equipos.Select(e => Dto(e, configuracion, efectivos)).ToList());
    }

    /// <summary>Valida el cuerpo de un equipo; deja los errores y devuelve el horario leído.</summary>
    private static (string? Nombre, TimeOnly? Inicio, TimeOnly? Fin) Validar(CambioEquipo cambio, Dictionary<string, string[]> errores)
    {
        var nombre = Limpiar(cambio.Nombre);
        if (nombre is null or { Length: > SoporteDbContext.LargoNombreEquipo })
        {
            errores["nombre"] = [$"El nombre es obligatorio (hasta {SoporteDbContext.LargoNombreEquipo} caracteres)."];
        }
        if (Limpiar(cambio.Area) is { Length: > SoporteDbContext.LargoNombreEquipo })
        {
            errores["area"] = [$"El área tiene máximo {SoporteDbContext.LargoNombreEquipo} caracteres."];
        }
        if (cambio.PersonasMinimo < 1 || cambio.PersonasMaximo < cambio.PersonasMinimo || cambio.PersonasMaximo > SoporteDbContext.PersonasMaximasPorTurno)
        {
            errores["personasMaximo"] = [$"Por sábado van de 1 a {SoporteDbContext.PersonasMaximasPorTurno} personas, y el máximo no puede ser menor que el mínimo."];
        }
        var (inicio, fin) = LeerHorario(cambio.HoraInicio, cambio.HoraFin, "horaInicio", "horaFin", errores);
        return (nombre, inicio, fin);
    }

    private static void Aplicar(EquipoSoporte equipo, CambioEquipo cambio, TimeOnly? inicio, TimeOnly? fin)
    {
        equipo.Area = Limpiar(cambio.Area);
        equipo.PersonasMinimo = cambio.PersonasMinimo;
        equipo.PersonasMaximo = cambio.PersonasMaximo;
        equipo.HoraInicio = inicio;
        equipo.HoraFin = fin;
        equipo.Activo = cambio.Activo ?? true;
    }

    private static async Task<IResult> CrearEquipoAsync(
        [Description("Datos del equipo nuevo.")] CambioEquipo cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await PuedeGlobalAsync(permisos, http.User, PermisosSoporte.Gestionar, ct))
        {
            return SinPermiso(SoloGlobal);
        }
        var errores = new Dictionary<string, string[]>();
        var (nombre, inicio, fin) = Validar(cambio, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await soporte.Equipos.AnyAsync(e => e.Nombre == nombre, ct))
        {
            return Conflicto("equipo_existente", $"Ya existe un equipo llamado «{nombre}».");
        }
        var equipo = new EquipoSoporte { Nombre = nombre! };
        Aplicar(equipo, cambio, inicio, fin);
        soporte.Equipos.Add(equipo);
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "equipo_creado", "soporte_equipo", equipo.Id, new { equipo.Nombre, equipo.PersonasMinimo, equipo.PersonasMaximo })], ct);
        }
        catch (DbUpdateException)
        {
            return Conflicto("equipo_existente", $"Ya existe un equipo llamado «{nombre}».");
        }
        return Results.Created($"/api/v1/soporte/equipos/{equipo.Id}",
            Dto(equipo, await ConfiguracionAsync(soporte, ct), await permisos.ObtenerAsync(http.User, ct)));
    }

    private static async Task<IResult> CambiarEquipoAsync(
        [Description("Id del equipo.")] Guid id,
        [Description("Datos nuevos del equipo.")] CambioEquipo cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var equipo = await soporte.Equipos.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (equipo is null)
        {
            return NoEncontrado("equipo_no_encontrado", "El equipo no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, id, ct))
        {
            return SinPermiso(SoloDelEquipo);
        }
        var errores = new Dictionary<string, string[]>();
        var (nombre, inicio, fin) = Validar(cambio, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await soporte.Equipos.AnyAsync(e => e.Nombre == nombre && e.Id != id, ct))
        {
            return Conflicto("equipo_existente", $"Ya existe un equipo llamado «{nombre}».");
        }
        equipo.Nombre = nombre!;
        Aplicar(equipo, cambio, inicio, fin);
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "equipo_cambiado", "soporte_equipo", equipo.Id, new { equipo.Nombre, equipo.PersonasMinimo, equipo.PersonasMaximo, equipo.Activo })], ct);
        return Results.Ok(Dto(equipo, await ConfiguracionAsync(soporte, ct), await permisos.ObtenerAsync(http.User, ct)));
    }

    // --- Participantes ---

    internal static async Task<List<ParticipanteDto>> ParticipantesAsync(SoporteDbContext soporte, PlataformaDbContext plataforma, Guid equipoId, CancellationToken ct)
    {
        var participantes = await soporte.Participantes.AsNoTracking().Where(p => p.EquipoId == equipoId)
            .OrderBy(p => p.Orden).ThenBy(p => p.CreadoEn).ToListAsync(ct);
        var personas = await PersonasAsync(plataforma, participantes.Select(p => p.PersonaId), ct);
        return [.. participantes.Select(p => Dto(p, personas))];
    }

    private static ParticipanteDto Dto(Participante p, Dictionary<Guid, Persona> personas) =>
        personas.TryGetValue(p.PersonaId, out var persona)
            ? new ParticipanteDto(p.Id, p.EquipoId, p.PersonaId, persona.Nombre, persona.Correo, p.Orden, p.Activo, ReglasTurno.Texto(persona.Estado))
            : new ParticipanteDto(p.Id, p.EquipoId, p.PersonaId, "Persona no disponible", string.Empty, p.Orden, p.Activo, "inactiva");

    private static async Task<IResult> ListarParticipantesAsync(
        [Description("Id del equipo.")] Guid equipoId, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (!await soporte.Equipos.AnyAsync(e => e.Id == equipoId, ct))
        {
            return NoEncontrado("equipo_no_encontrado", "El equipo no existe.");
        }
        return Results.Ok(await ParticipantesAsync(soporte, plataforma, equipoId, ct));
    }

    private static async Task<IResult> AgregarParticipanteAsync(
        [Description("Id del equipo.")] Guid equipoId,
        [Description("Persona que se agrega a la rotación del equipo.")] NuevoParticipante nuevo,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await soporte.Equipos.AnyAsync(e => e.Id == equipoId, ct))
        {
            return NoEncontrado("equipo_no_encontrado", "El equipo no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, equipoId, ct))
        {
            return SinPermiso(SoloDelEquipo);
        }
        var persona = await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == nuevo.PersonaId, ct);
        if (persona is null || persona.Tipo != TipoPersona.Empleado || persona.Estado != EstadoPersona.Activa)
        {
            return Invalido("personaId", "Solo se agregan personas internas activas.");
        }
        if (await soporte.Participantes.AnyAsync(p => p.EquipoId == equipoId && p.PersonaId == persona.Id, ct))
        {
            return Conflicto("ya_participa", $"{persona.Nombre} ya está en la rotación de este equipo.");
        }
        var orden = (await soporte.Participantes.Where(p => p.EquipoId == equipoId).MaxAsync(p => (int?)p.Orden, ct) ?? 0) + 1;
        var participante = new Participante { EquipoId = equipoId, PersonaId = persona.Id, Orden = orden };
        soporte.Participantes.Add(participante);
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "participante_agregado", "soporte_participante", participante.Id, new { equipoId, personaId = persona.Id, orden })], ct);
        }
        catch (DbUpdateException)
        {
            return Conflicto("ya_participa", $"{persona.Nombre} ya está en la rotación de este equipo.");
        }
        return Results.Created($"/api/v1/soporte/participantes/{participante.Id}",
            Dto(participante, new Dictionary<Guid, Persona> { [persona.Id] = persona }));
    }

    private static async Task<IResult> CambiarParticipanteAsync(
        [Description("Id del participante.")] Guid id,
        [Description("Estado nuevo del participante.")] CambioParticipante cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var participante = await soporte.Participantes.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (participante is null)
        {
            return NoEncontrado("participante_no_encontrado", "El participante no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, participante.EquipoId, ct))
        {
            return SinPermiso(SoloDelEquipo);
        }
        participante.Activo = cambio.Activo;
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, cambio.Activo ? "participante_activado" : "participante_desactivado", "soporte_participante", participante.Id)], ct);
        var personas = await PersonasAsync(plataforma, [participante.PersonaId], ct);
        return Results.Ok(Dto(participante, personas));
    }

    private static async Task<IResult> OrdenarParticipantesAsync(
        [Description("Id del equipo.")] Guid equipoId,
        [Description("Todos los participantes del equipo en el orden nuevo.")] OrdenParticipantes orden,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await soporte.Equipos.AnyAsync(e => e.Id == equipoId, ct))
        {
            return NoEncontrado("equipo_no_encontrado", "El equipo no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, equipoId, ct))
        {
            return SinPermiso(SoloDelEquipo);
        }
        var ids = orden.Participantes ?? [];
        var participantes = await soporte.Participantes.Where(p => p.EquipoId == equipoId).ToListAsync(ct);
        if (ids.Count != participantes.Count || ids.Distinct().Count() != ids.Count || !participantes.All(p => ids.Contains(p.Id)))
        {
            return Invalido("participantes", "Envía todos los participantes del equipo, cada uno una vez, en el orden nuevo.");
        }
        var porId = participantes.ToDictionary(p => p.Id);
        for (var i = 0; i < ids.Count; i++)
        {
            porId[ids[i]].Orden = i + 1;
        }
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "participantes_ordenados", "soporte_equipo", equipoId, new { orden = ids })], ct);
        return Results.Ok(await ParticipantesAsync(soporte, plataforma, equipoId, ct));
    }

    private static async Task<IResult> BuscarCandidatosAsync(
        [Description("Nombre o correo (mínimo 2 caracteres, máximo 100).")] string? q,
        [Description("Equipo: sin las personas que ya están en él.")] Guid? equipo,
        SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var texto = q?.Trim() ?? string.Empty;
        if (texto.Length is < 2 or > 100)
        {
            return Invalido("q", "Escribe entre 2 y 100 caracteres.");
        }
        var yaParticipan = equipo is { } e
            ? await soporte.Participantes.Where(p => p.EquipoId == e).Select(p => p.PersonaId).ToListAsync(ct)
            : [];
        var personas = plataforma.Personas.AsNoTracking()
            .Where(p => p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa && !yaParticipan.Contains(p.Id));
        foreach (var termino in FuncionesSql.NormalizarBusqueda(texto).Split(' ').Distinct(StringComparer.Ordinal).Take(5))
        {
            var patron = FuncionesSql.PatronContiene(termino);
            personas = personas.Where(p =>
                EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(p.Nombre, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(p.Correo, patron, FuncionesSql.EscapeLike));
        }
        var lista = await personas.OrderBy(p => p.Nombre).Take(LimiteCandidatos)
            .Select(p => new CandidatoDto(p.Id, p.Nombre, p.Correo, p.Cargo)).ToListAsync(ct);
        return Results.Ok(lista);
    }
}
