using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Evaluación de riesgo, votos, EIPD y roles de gobierno (HU 8.2, ATLAS-ADR-0055 §2, §5 y §6).</summary>
internal static class EndpointsEvaluaciones
{
    private const string RecursoEvaluacion = "gia.evaluacion";
    private const string RecursoEipd = "gia.eipd";
    private const string RecursoRol = "gia.rol_gobierno";

    [Description("Principios de la Circular 002 (plan 02 §8.3).")]
    public sealed record PrincipiosCircular(string? Idoneidad, string? Necesidad, string? Razonabilidad, string? Proporcionalidad);

    [Description("Crear una evaluación: el servidor calcula los niveles.")]
    public sealed record SolicitudEvaluacion(
        [property: Description("Escenarios; las seis dimensiones D1 a D6 deben tener al menos uno.")] IReadOnlyList<EscenarioRiesgo> Dimensiones,
        PrincipiosCircular? Principios,
        string? TransferenciaInternacional);

    [Description("Un voto.")]
    public sealed record VotoResumen(PersonaGia Persona, [property: Description("responsable_ia, opd, comite o seguridad.")] string Rol,
        [property: Description("aprobar o rechazar.")] string Voto, string? Condiciones, string? Comentario, DateTimeOffset VotadoEn);

    [Description("Una evaluación con sus votos.")]
    public sealed record EvaluacionDetalle(
        Guid Id, int Version, IReadOnlyList<EscenarioRiesgo> Dimensiones,
        int PuntajeInherente, [property: Description("bajo, medio, alto o critico: el de la peor dimensión.")] string NivelInherente,
        int PuntajeResidual, string NivelResidual,
        PrincipiosCircular Principios, string? TransferenciaInternacional,
        [property: Description("pendiente, aprobado, aprobado_condiciones o rechazado.")] string Decision,
        string? Condiciones, PersonaGia Evaluador, DateTimeOffset EvaluadaEn, DateTimeOffset? DecididaEn,
        [property: Description("Roles cuyo voto a favor hace falta, según el nivel inherente.")] IReadOnlyList<string> RolesRequeridos,
        [property: Description("Lo que falta para poder aprobar, según el nivel.")] IReadOnlyList<string> Requisitos,
        IReadOnlyList<VotoResumen> Votos);

    [Description("Votar sobre la evaluación.")]
    public sealed record SolicitudVoto(
        [property: Description("responsable_ia, opd, comite o seguridad: uno que la persona tenga.")] string Rol,
        [property: Description("aprobar o rechazar.")] string Voto,
        [property: Description("Condiciones de la aprobación, si las hay.")] string? Condiciones,
        string? Comentario);

    [Description("La evaluación de impacto en privacidad (plan 02 §8.4).")]
    public sealed record EipdDetalle(
        Guid Id, [property: Description("borrador, en_revision, aprobada o rechazada.")] string Estado,
        string Descripcion, string Necesidad, string Riesgos, string Medidas, string OpinionOpd, string PlanAccion, string? DocumentoUrl,
        PersonaGia? DecididaPor, DateTimeOffset? DecididaEn);

    [Description("Las seis secciones de la EIPD.")]
    public sealed record SolicitudEipd(
        [property: Description("Descripción del tratamiento y del flujo de datos.")] string? Descripcion,
        [property: Description("Necesidad y proporcionalidad.")] string? Necesidad,
        [property: Description("Riesgos para los titulares.")] string? Riesgos,
        string? Medidas,
        [property: Description("Opinión del OPD.")] string? OpinionOpd,
        [property: Description("Plan de acción.")] string? PlanAccion,
        [property: Description("Enlace https al documento, si vive fuera.")] string? DocumentoUrl);

    [Description("Decisión del OPD sobre la EIPD.")]
    public sealed record SolicitudDecisionEipd([property: Description("aprobada o rechazada.")] string Decision);

    [Description("Una persona con un rol de gobierno.")]
    public sealed record RolGobiernoResumen(Guid Id, PersonaGia Persona, [property: Description("responsable_ia, opd, comite o seguridad.")] string Rol, DateTimeOffset AsignadoEn);

    [Description("Asignar un rol de gobierno.")]
    public sealed record SolicitudRolGobierno(Guid PersonaId, [property: Description("responsable_ia, opd, comite o seguridad.")] string Rol);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/casos/{id:guid}/evaluaciones", ListarAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarEvaluacionesCasoIa").WithSummary("Evaluaciones de un caso")
            .WithDescription("De la más reciente a la más antigua, con sus votos y lo que falta para aprobar. 404 si el caso no existe. Requiere atlas.gia.ver.")
            .Produces<List<EvaluacionDetalle>>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);

        grupo.MapPost("/casos/{id:guid}/evaluaciones", CrearAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearEvaluacionCasoIa").WithSummary("Crear una evaluación de riesgo")
            .WithDescription("Calcula los puntajes y el nivel por la peor dimensión. Solo con el caso en borrador; una evaluación pendiente anterior queda reemplazada como rechazada. Requiere atlas.gia.gestionar.")
            .Produces<EvaluacionDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/casos/{id:guid}/evaluaciones/{version}/votos", VotarAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("VotarEvaluacionCasoIa").WithSummary("Votar una evaluación")
            .WithDescription("Vota en la calidad de un rol de gobierno que la persona tenga (403 sin_rol_gobierno). Aprobar exige los requisitos del nivel (422 requisitos_pendientes) y un residual que no sea crítico (422 riesgo_critico). Cuando están todos los votos, el caso queda aprobado; un rechazo lo devuelve a borrador. Requiere atlas.gia.gestionar.")
            .Produces<EvaluacionDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .Produces<ErrorGia>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();

        grupo.MapGet("/casos/{id:guid}/eipd", ObtenerEipdAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ObtenerEipdCasoIa").WithSummary("La EIPD de un caso")
            .WithDescription("404 si el caso no existe o aún no tiene EIPD. Requiere atlas.gia.ver.")
            .Produces<EipdDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);

        grupo.MapPut("/casos/{id:guid}/eipd", GuardarEipdAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarEipdCasoIa").WithSummary("Guardar la EIPD")
            .WithDescription("Crea o edita la EIPD. Una decidida o en revisión no se edita, salvo una rechazada, que vuelve a borrador (409 estado_invalido). Requiere atlas.gia.gestionar.")
            .Produces<EipdDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/casos/{id:guid}/eipd/envio", EnviarEipdAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("EnviarEipdCasoIa").WithSummary("Enviar la EIPD al OPD")
            .WithDescription("De borrador a en_revision. 422 ficha_incompleta si falta alguna sección distinta de la opinión del OPD. Requiere atlas.gia.gestionar.")
            .Produces<EipdDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .Produces<ErrorGia>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/casos/{id:guid}/eipd/decision", DecidirEipdAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("DecidirEipdCasoIa").WithSummary("Decidir la EIPD")
            .WithDescription("Solo quien tenga el rol OPD (403 sin_rol_gobierno), con la EIPD en revisión y su opinión escrita. Requiere atlas.gia.gestionar.")
            .Produces<EipdDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .Produces<ErrorGia>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();

        grupo.MapGet("/roles-gobierno", RolesAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarRolesGobiernoIa").WithSummary("Quién tiene cada rol de gobierno")
            .WithDescription("Responsable de IA, OPD, Comité y Seguridad. Requiere atlas.gia.ver.")
            .Produces<List<RolGobiernoResumen>>();

        grupo.MapPost("/roles-gobierno", AsignarRolAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("AsignarRolGobiernoIa").WithSummary("Asignar un rol de gobierno")
            .WithDescription("A un empleado activo. Auditado. 409 ya_existe si ya lo tiene. Requiere atlas.gia.gestionar.")
            .Produces<RolGobiernoResumen>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapDelete("/roles-gobierno/{id:guid}", QuitarRolAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("QuitarRolGobiernoIa").WithSummary("Quitar un rol de gobierno")
            .WithDescription("Los votos ya dados se conservan. Auditado. 404 si no existe. Requiere atlas.gia.gestionar.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListarAsync(
        [Description("Id del caso.")] Guid id, GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var caso = await contexto.Casos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caso is null)
        {
            return EndpointsCasos.NoEncontrado();
        }
        var evaluaciones = await contexto.Evaluaciones.AsNoTracking().Where(e => e.CasoUsoId == id).OrderByDescending(e => e.Version).ToListAsync(ct);
        var ids = evaluaciones.Select(e => e.Id).ToList();
        var votos = await contexto.Votos.AsNoTracking().Where(v => ids.Contains(v.EvaluacionId)).OrderBy(v => v.VotadoEn).ToListAsync(ct);
        var eipd = await EstadoEipdAsync(contexto, id, ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, evaluaciones.Select(e => (Guid?)e.EvaluadorPersonaId).Concat(votos.Select(v => (Guid?)v.PersonaId)), ct);
        return Results.Ok(evaluaciones.Select(e => Detalle(e, caso, votos.Where(v => v.EvaluacionId == e.Id), eipd, nombres)).ToList());
    }

    private static async Task<IResult> CrearAsync(
        [Description("Id del caso.")] Guid id,
        [Description("Los escenarios.")] SolicitudEvaluacion solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caso is null)
        {
            return EndpointsCasos.NoEncontrado();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var dimensiones = LeerDimensiones(solicitud.Dimensiones, errores);
        var transferencia = ApoyoGia.TextoOpcional(solicitud.TransferenciaInternacional, "transferenciaInternacional", GiaDbContext.LargoMedio, errores);
        var principios = solicitud.Principios ?? new PrincipiosCircular(null, null, null, null);
        if (new[] { principios.Idoneidad, principios.Necesidad, principios.Razonabilidad, principios.Proporcionalidad }.Any(p => p?.Length > GiaDbContext.LargoMedio))
        {
            errores["principios"] = [$"Cada principio va hasta {GiaDbContext.LargoMedio} caracteres."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (caso.Estado != ValoresGia.EstadoBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se evalúa un caso en borrador: reabre el caso primero.");
        }
        var ahora = reloj.GetUtcNow();
        var anteriores = await contexto.Evaluaciones.Where(e => e.CasoUsoId == id).OrderByDescending(e => e.Version).ToListAsync(ct);
        if (anteriores.FirstOrDefault() is { Decision: ValoresGia.DecisionPendiente } pendiente)
        {
            pendiente.Decision = ValoresGia.DecisionRechazado;
            pendiente.Condiciones = "Reemplazada por una evaluación nueva.";
            pendiente.DecididaEn = ahora;
        }
        var (inherente, residual) = ReglasGia.Puntajes(dimensiones);
        var evaluacion = new EvaluacionRiesgo
        {
            CasoUsoId = id,
            Version = (anteriores.FirstOrDefault()?.Version ?? 0) + 1,
            Dimensiones = JsonSerializer.Serialize(dimensiones, JsonSerializerOptions.Web),
            PuntajeInherente = inherente,
            NivelInherente = ReglasGia.Nivel(inherente),
            PuntajeResidual = residual,
            NivelResidual = ReglasGia.Nivel(residual),
            Principios = JsonSerializer.Serialize(principios, JsonSerializerOptions.Web),
            TransferenciaInternacional = transferencia,
            EvaluadorPersonaId = ApoyoGia.Persona(http.User),
            EvaluadaEn = ahora,
        };
        contexto.Evaluaciones.Add(evaluacion);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.evaluacion_creada", RecursoEvaluacion, evaluacion.Id,
                evaluacion.Version.ToString(CultureInfo.InvariantCulture), new { caso = id, inherente = evaluacion.NivelInherente, residual = evaluacion.NivelResidual }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("version_desactualizada", "Alguien evaluó al mismo tiempo: recarga el caso y vuelve a intentarlo.");
        }
        var nombres = await ApoyoGia.NombresAsync(plataforma, [evaluacion.EvaluadorPersonaId], ct);
        return Results.Created($"/api/v1/gia/casos/{id}/evaluaciones", Detalle(evaluacion, caso, [], await EstadoEipdAsync(contexto, id, ct), nombres));
    }

    private static async Task<IResult> VotarAsync(
        [Description("Id del caso.")] Guid id,
        [Description("Versión de la evaluación.")] int version,
        [Description("El voto.")] SolicitudVoto solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.FirstOrDefaultAsync(c => c.Id == id, ct);
        var evaluacion = caso is null ? null : await contexto.Evaluaciones.FirstOrDefaultAsync(e => e.CasoUsoId == id && e.Version == version, ct);
        if (caso is null || evaluacion is null)
        {
            return ApoyoGia.NoEncontrado("La evaluación no existe.");
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rol = ApoyoGia.Valor(solicitud.Rol, "rol", ValoresGia.RolesGobierno, errores);
        var voto = ApoyoGia.Valor(solicitud.Voto, "voto", ValoresGia.Votos, errores);
        var condiciones = ApoyoGia.TextoOpcional(solicitud.Condiciones, "condiciones", GiaDbContext.LargoMedio, errores);
        var comentario = ApoyoGia.TextoOpcional(solicitud.Comentario, "comentario", GiaDbContext.LargoMedio, errores);
        if (rol is null)
        {
            errores["rol"] = [$"Valores válidos: {string.Join(", ", ValoresGia.RolesGobierno)}."];
        }
        if (voto is null)
        {
            errores["voto"] = [$"Valores válidos: {string.Join(", ", ValoresGia.Votos)}."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var persona = ApoyoGia.Persona(http.User);
        if (!await ApoyoGia.TieneRolAsync(contexto, persona, rol!, ct))
        {
            return ApoyoGia.Error(StatusCodes.Status403Forbidden, "sin_rol_gobierno", "No tienes ese rol de gobierno.");
        }
        if (caso.Estado != ValoresGia.EstadoEnRevision || evaluacion.Decision != ValoresGia.DecisionPendiente)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se vota una evaluación pendiente de un caso en revisión.");
        }
        var regla = ReglasGia.Regla(evaluacion.NivelInherente, ReglasGia.TieneDatosPersonales(caso));
        if (!regla.Roles.Contains(rol!))
        {
            return ApoyoGia.Invalido("rol_no_aplica", $"Este nivel lo deciden: {string.Join(", ", regla.Roles)}.", regla.Roles);
        }
        if (voto == ValoresGia.VotoAprobar)
        {
            if (ReglasGia.Bloqueada(evaluacion.NivelResidual))
            {
                return ApoyoGia.Invalido("riesgo_critico", "Con riesgo residual crítico no se aprueba: hay que reducirlo a alto o menos.");
            }
            var requisitos = ReglasGia.FaltantesNivel(caso, evaluacion.NivelInherente, await EstadoEipdAsync(contexto, id, ct));
            if (requisitos.Count > 0)
            {
                return ApoyoGia.Invalido("requisitos_pendientes", "Faltan requisitos del nivel.", requisitos);
            }
        }

        var ahora = reloj.GetUtcNow();
        contexto.Votos.Add(new VotoEvaluacion { EvaluacionId = evaluacion.Id, PersonaId = persona, Rol = rol!, Voto = voto!, Condiciones = condiciones, Comentario = comentario, VotadoEn = ahora });
        var previos = await contexto.Votos.AsNoTracking().Where(v => v.EvaluacionId == evaluacion.Id).ToListAsync(ct);
        if (voto == ValoresGia.VotoRechazar)
        {
            evaluacion.Decision = ValoresGia.DecisionRechazado;
            evaluacion.Condiciones = condiciones;
            evaluacion.DecididaEn = ahora;
            caso.Estado = ValoresGia.EstadoBorrador;
        }
        else if (ReglasGia.Completa(regla, previos.Where(v => v.Voto == ValoresGia.VotoAprobar).Select(v => v.Rol).Append(rol!)))
        {
            var todasLasCondiciones = previos.Select(v => v.Condiciones).Append(condiciones).OfType<string>().ToList();
            evaluacion.Decision = todasLasCondiciones.Count > 0 ? ValoresGia.DecisionAprobadoCondiciones : ValoresGia.DecisionAprobado;
            evaluacion.Condiciones = todasLasCondiciones.Count > 0 ? string.Join("\n", todasLasCondiciones) : null;
            evaluacion.DecididaEn = ahora;
            caso.Estado = ValoresGia.EstadoAprobado;
            caso.NivelRiesgo = evaluacion.NivelInherente;
            caso.AprobadoPor = persona;
            caso.AprobadoEn = ahora;
            caso.ProximaRevision = EndpointsCasos.Hoy(reloj).AddMonths(regla.MesesRevision);
        }
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.evaluacion_votada", RecursoEvaluacion, evaluacion.Id,
                evaluacion.Version.ToString(CultureInfo.InvariantCulture), new { caso = id, rol, voto, decision = evaluacion.Decision }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_voto", "Ya votaste esta evaluación con ese rol.");
        }
        var votos = await contexto.Votos.AsNoTracking().Where(v => v.EvaluacionId == evaluacion.Id).OrderBy(v => v.VotadoEn).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, votos.Select(v => (Guid?)v.PersonaId).Append(evaluacion.EvaluadorPersonaId), ct);
        return Results.Ok(Detalle(evaluacion, caso, votos, await EstadoEipdAsync(contexto, id, ct), nombres));
    }

    // --- EIPD ---

    private static async Task<IResult> ObtenerEipdAsync(
        [Description("Id del caso.")] Guid id, GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var eipd = await contexto.Eipds.AsNoTracking().FirstOrDefaultAsync(e => e.CasoUsoId == id, ct);
        return eipd is null ? ApoyoGia.NoEncontrado("El caso no tiene EIPD.") : Results.Ok(await EipdDeAsync(eipd, plataforma, ct));
    }

    private static async Task<IResult> GuardarEipdAsync(
        [Description("Id del caso.")] Guid id,
        [Description("Las secciones.")] SolicitudEipd solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await contexto.Casos.AnyAsync(c => c.Id == id, ct))
        {
            return EndpointsCasos.NoEncontrado();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string Seccion(string? valor, string campo) => ApoyoGia.TextoOpcional(valor, campo, GiaDbContext.LargoLargo, errores) ?? string.Empty;
        var descripcion = Seccion(solicitud.Descripcion, "descripcion");
        var necesidad = Seccion(solicitud.Necesidad, "necesidad");
        var riesgos = Seccion(solicitud.Riesgos, "riesgos");
        var medidas = Seccion(solicitud.Medidas, "medidas");
        var opinion = Seccion(solicitud.OpinionOpd, "opinionOpd");
        var plan = Seccion(solicitud.PlanAccion, "planAccion");
        var url = ApoyoGia.TextoOpcional(solicitud.DocumentoUrl, "documentoUrl", GiaDbContext.LargoCorto, errores);
        if (!ApoyoGia.UrlSegura(url))
        {
            errores["documentoUrl"] = ["Solo direcciones https."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var eipd = await contexto.Eipds.FirstOrDefaultAsync(e => e.CasoUsoId == id, ct);
        if (eipd is not null && eipd.Estado is not (ValoresGia.EipdBorrador or ValoresGia.EipdRechazada))
        {
            // La opinión la escribe el OPD mientras está en revisión; lo demás queda quieto.
            if (eipd.Estado != ValoresGia.EipdEnRevision || !await ApoyoGia.TieneRolAsync(contexto, ApoyoGia.Persona(http.User), ValoresGia.RolOpd, ct))
            {
                return ApoyoGia.Conflicto("estado_invalido", "Una EIPD en revisión o aprobada no se edita.");
            }
            eipd.OpinionOpd = opinion;
        }
        else if (eipd is null)
        {
            eipd = new Eipd { CasoUsoId = id, Descripcion = descripcion, Necesidad = necesidad, Riesgos = riesgos, Medidas = medidas, OpinionOpd = opinion, PlanAccion = plan, DocumentoUrl = url };
            contexto.Eipds.Add(eipd);
        }
        else
        {
            eipd.Estado = ValoresGia.EipdBorrador;
            eipd.Descripcion = descripcion;
            eipd.Necesidad = necesidad;
            eipd.Riesgos = riesgos;
            eipd.Medidas = medidas;
            eipd.OpinionOpd = opinion;
            eipd.PlanAccion = plan;
            eipd.DocumentoUrl = url;
            eipd.DecididaPor = null;
            eipd.DecididaEn = null;
        }
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.eipd_guardada", RecursoEipd, eipd.Id, null, new { caso = id, eipd.Estado }), ct);
        return Results.Ok(await EipdDeAsync(eipd, plataforma, ct));
    }

    private static async Task<IResult> EnviarEipdAsync(
        [Description("Id del caso.")] Guid id,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var eipd = await contexto.Eipds.FirstOrDefaultAsync(e => e.CasoUsoId == id, ct);
        if (eipd is null)
        {
            return ApoyoGia.NoEncontrado("El caso no tiene EIPD.");
        }
        if (eipd.Estado != ValoresGia.EipdBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se envía una EIPD en borrador.");
        }
        var faltan = new (string Texto, string Campo)[]
        {
            (eipd.Descripcion, "descripción del tratamiento"), (eipd.Necesidad, "necesidad y proporcionalidad"), (eipd.Riesgos, "riesgos para los titulares"),
            (eipd.Medidas, "medidas"), (eipd.PlanAccion, "plan de acción"),
        }.Where(s => string.IsNullOrWhiteSpace(s.Texto)).Select(s => s.Campo).ToList();
        if (faltan.Count > 0)
        {
            return ApoyoGia.Invalido("ficha_incompleta", "Faltan secciones de la EIPD.", faltan);
        }
        eipd.Estado = ValoresGia.EipdEnRevision;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.eipd_enviada", RecursoEipd, eipd.Id, null, new { caso = id }), ct);
        return Results.Ok(await EipdDeAsync(eipd, plataforma, ct));
    }

    private static async Task<IResult> DecidirEipdAsync(
        [Description("Id del caso.")] Guid id,
        [Description("La decisión.")] SolicitudDecisionEipd solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var eipd = await contexto.Eipds.FirstOrDefaultAsync(e => e.CasoUsoId == id, ct);
        if (eipd is null)
        {
            return ApoyoGia.NoEncontrado("El caso no tiene EIPD.");
        }
        var decision = (solicitud.Decision ?? string.Empty).Trim();
        if (decision is not (ValoresGia.EipdAprobada or ValoresGia.EipdRechazada))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["decision"] = ["Valores válidos: aprobada, rechazada."] });
        }
        var persona = ApoyoGia.Persona(http.User);
        if (!await ApoyoGia.TieneRolAsync(contexto, persona, ValoresGia.RolOpd, ct))
        {
            return ApoyoGia.Error(StatusCodes.Status403Forbidden, "sin_rol_gobierno", "Solo decide el Oficial de Protección de Datos.");
        }
        if (eipd.Estado != ValoresGia.EipdEnRevision)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se decide una EIPD en revisión.");
        }
        if (string.IsNullOrWhiteSpace(eipd.OpinionOpd))
        {
            return ApoyoGia.Invalido("ficha_incompleta", "Falta la opinión del OPD.", ["opinión del OPD"]);
        }
        var ahora = reloj.GetUtcNow();
        eipd.Estado = decision;
        eipd.DecididaPor = persona;
        eipd.DecididaEn = ahora;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.eipd_decidida", RecursoEipd, eipd.Id, null, new { caso = id, decision }), ct);
        return Results.Ok(await EipdDeAsync(eipd, plataforma, ct));
    }

    // --- Roles de gobierno ---

    private static async Task<IResult> RolesAsync(GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var roles = await contexto.RolesGobierno.AsNoTracking().OrderBy(r => r.Rol).ThenBy(r => r.AsignadoEn).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, roles.Select(r => (Guid?)r.PersonaId), ct);
        return Results.Ok(roles.Select(r => new RolGobiernoResumen(r.Id, ApoyoGia.Ref(r.PersonaId, nombres)!, r.Rol, r.AsignadoEn)).ToList());
    }

    private static async Task<IResult> AsignarRolAsync(
        [Description("La persona y el rol.")] SolicitudRolGobierno solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rol = ApoyoGia.Valor(solicitud.Rol, "rol", ValoresGia.RolesGobierno, errores);
        if (rol is null)
        {
            errores["rol"] = [$"Valores válidos: {string.Join(", ", ValoresGia.RolesGobierno)}."];
        }
        if (!await ApoyoGia.EmpleadoActivoAsync(plataforma, solicitud.PersonaId, ct))
        {
            errores["personaId"] = ["Debe ser un empleado activo."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await ApoyoGia.TieneRolAsync(contexto, solicitud.PersonaId, rol!, ct))
        {
            return ApoyoGia.Conflicto("ya_existe", "Esa persona ya tiene ese rol.");
        }
        var ahora = reloj.GetUtcNow();
        var fila = new RolGobierno { PersonaId = solicitud.PersonaId, Rol = rol!, AsignadoPor = ApoyoGia.Persona(http.User), AsignadoEn = ahora };
        contexto.RolesGobierno.Add(fila);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.rol_asignado", RecursoRol, fila.Id, null, new { persona = fila.PersonaId, rol }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_existe", "Esa persona ya tiene ese rol.");
        }
        var nombres = await ApoyoGia.NombresAsync(plataforma, [fila.PersonaId], ct);
        return Results.Created($"/api/v1/gia/roles-gobierno/{fila.Id}", new RolGobiernoResumen(fila.Id, ApoyoGia.Ref(fila.PersonaId, nombres)!, fila.Rol, fila.AsignadoEn));
    }

    private static async Task<IResult> QuitarRolAsync(
        [Description("Id de la asignación.")] Guid id,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var fila = await contexto.RolesGobierno.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (fila is null)
        {
            return ApoyoGia.NoEncontrado("La asignación no existe.");
        }
        contexto.RolesGobierno.Remove(fila);
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.rol_quitado", RecursoRol, fila.Id, null, new { persona = fila.PersonaId, fila.Rol }), ct);
        return Results.NoContent();
    }

    // --- Piezas comunes ---

    private static Task<string?> EstadoEipdAsync(GiaDbContext contexto, Guid casoId, CancellationToken ct) =>
        contexto.Eipds.AsNoTracking().Where(e => e.CasoUsoId == casoId).Select(e => e.Estado).FirstOrDefaultAsync(ct);

    private static async Task<EipdDetalle> EipdDeAsync(Eipd e, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var nombres = await ApoyoGia.NombresAsync(plataforma, [e.DecididaPor], ct);
        return new EipdDetalle(e.Id, e.Estado, e.Descripcion, e.Necesidad, e.Riesgos, e.Medidas, e.OpinionOpd, e.PlanAccion, e.DocumentoUrl, ApoyoGia.Ref(e.DecididaPor, nombres), e.DecididaEn);
    }

    private static EvaluacionDetalle Detalle(EvaluacionRiesgo e, CasoUso caso, IEnumerable<VotoEvaluacion> votos, string? estadoEipd, IReadOnlyDictionary<Guid, string> nombres) => new(
        e.Id, e.Version, JsonSerializer.Deserialize<List<EscenarioRiesgo>>(e.Dimensiones, JsonSerializerOptions.Web)!,
        e.PuntajeInherente, e.NivelInherente, e.PuntajeResidual, e.NivelResidual,
        JsonSerializer.Deserialize<PrincipiosCircular>(e.Principios, JsonSerializerOptions.Web)!, e.TransferenciaInternacional,
        e.Decision, e.Condiciones, ApoyoGia.Ref(e.EvaluadorPersonaId, nombres)!, e.EvaluadaEn, e.DecididaEn,
        ReglasGia.Regla(e.NivelInherente, ReglasGia.TieneDatosPersonales(caso)).Roles,
        ReglasGia.FaltantesNivel(caso, e.NivelInherente, estadoEipd),
        [.. votos.Select(v => new VotoResumen(ApoyoGia.Ref(v.PersonaId, nombres)!, v.Rol, v.Voto, v.Condiciones, v.Comentario, v.VotadoEn))]);

    private static List<EscenarioRiesgo> LeerDimensiones(IReadOnlyList<EscenarioRiesgo>? dimensiones, Dictionary<string, string[]> errores)
    {
        var lista = (dimensiones ?? []).ToList();
        if (lista.Count > 60)
        {
            errores["dimensiones"] = ["Hasta 60 escenarios."];
            return lista;
        }
        static bool Escala(int v) => v is >= 1 and <= 5;
        foreach (var e in lista)
        {
            if (!ReglasGia.DimensionesRiesgo.Contains(e.Dimension)
                || string.IsNullOrWhiteSpace(e.Escenario) || e.Escenario.Length > GiaDbContext.LargoMedio
                || !Escala(e.P) || !Escala(e.I)
                || (e.P2 is null) != (e.I2 is null)
                || (e.P2 is { } p && !Escala(p)) || (e.I2 is { } i && !Escala(i))
                || e.ControlesExistentes?.Length > GiaDbContext.LargoMedio || e.ControlesNuevos?.Length > GiaDbContext.LargoMedio)
            {
                errores["dimensiones"] = ["Cada escenario lleva dimensión D1 a D6, texto, probabilidad e impacto de 1 a 5 y, si hay residual, los dos valores."];
                return lista;
            }
        }
        var faltan = ReglasGia.DimensionesRiesgo.Where(d => lista.All(e => e.Dimension != d)).ToList();
        if (faltan.Count > 0)
        {
            errores["dimensiones"] = [$"Faltan las dimensiones: {string.Join(", ", faltan)}."];
        }
        return lista;
    }
}
