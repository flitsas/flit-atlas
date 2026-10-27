using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Contratos.Guias.EndpointsGuias;

namespace Flit.Atlas.Modulos.Contratos.Comparticiones;

/// <summary>
/// Comparticiones de paquetes con organizaciones (HU 4.3, plan 00 §6.2, §8.4 y §8.5, ATLAS-ADR-0043), en
/// <c>/api/v1/contratos</c>. Leer exige <c>atlas.contratos.ver_internos</c> (como los paquetes, ATLAS-ADR-0041); compartir,
/// cambiar y revocar, <c>atlas.paquetes.gestionar</c> global o sobre los aplicativos de todos los contratos del paquete (el
/// rol implícito del responsable) y un ingreso reciente (§6.3: compartir es una acción sensible). Todo va con su auditoría
/// en la misma transacción. Con un id inexistente, 404 antes que 403.
/// </summary>
internal static class EndpointsComparticiones
{
    public const string RecursoAuditoria = "contratos.comparticion";
    public const string AccionCreada = "contratos.comparticion_creada";
    public const string AccionCambiada = "contratos.comparticion_cambiada";
    public const string AccionRevocada = "contratos.comparticion_revocada";

    // --- DTO ---

    [Description("Organización con la que se comparte.")]
    public sealed record OrganizacionDeComparticion(
        Guid Id,
        [property: Description("Nombre; «Organización no disponible» si ya no está.")] string Nombre,
        [property: Description("cliente o proveedor.")] string? Tipo,
        [property: Description("activa o suspendida (una suspendida no entra a Atlas).")] string? Estado);

    [Description("Un paquete compartido con una organización.")]
    public sealed record ComparticionDto(
        Guid Id,
        Guid PaqueteId,
        [property: Description("Nombre actual del paquete.")] string PaqueteNombre,
        [property: Description("borrador, publicado o archivado: el portal solo muestra los publicados.")] string PaqueteEstado,
        OrganizacionDeComparticion Organizacion,
        [property: Description("Revisión fijada para esta organización; vacío = sigue la vigente del paquete.")] int? RevisionFijada,
        [property: Description("La revisión que ve hoy la organización (la fijada o la vigente); vacío si el paquete no tiene.")] int? RevisionQueVe,
        [property: Description("Si las personas de la organización pueden exportar (además de su permiso).")] bool PuedeExportar,
        [property: Description("Si pueden usar el sandbox (además de su permiso).")] bool PuedeSandbox,
        DateTimeOffset VigenteDesde,
        [property: Description("Al pasar, el acceso se corta sin intervención; vacío = sin vencimiento.")] DateTimeOffset? VigenteHasta,
        [property: Description("vigente, programada (empieza después), vencida o revocada; se calcula al leer.")] string Estado,
        [property: Description("Quién compartió (o reactivó).")] PersonaRef OtorgadoPor,
        DateTimeOffset CreadaEn,
        DateTimeOffset ActualizadaEn,
        DateTimeOffset? RevocadaEn,
        [property: Description("Si la persona puede cambiarla o revocarla.")] bool PuedeGestionar);

    [Description("Comparticiones de un paquete o de una organización.")]
    public sealed record ListadoComparticiones(
        [property: Description("Si la persona puede compartir este paquete (solo en el listado de un paquete).")] bool PuedeCompartir,
        [property: Description("Las vigentes y programadas primero; luego vencidas y revocadas, de la más reciente a la más antigua.")] IReadOnlyList<ComparticionDto> Elementos);

    [Description("Compartir un paquete publicado con una organización cliente o proveedora.")]
    public sealed record SolicitudComparticion(
        Guid OrganizacionId,
        [property: Description("Revisión del paquete que verá la organización; vacío = sigue la vigente (publicada o fijada).")] int? RevisionFijada,
        [property: Description("Si sus personas pueden exportar el paquete (si además tienen el permiso).")] bool PuedeExportar,
        [property: Description("Si pueden usar el sandbox (si además tienen el permiso).")] bool PuedeSandbox,
        [property: Description("Desde cuándo vale; vacío = ahora.")] DateTimeOffset? VigenteDesde,
        [property: Description("Hasta cuándo vale; vacío = sin vencimiento (obligatorio con un proveedor).")] DateTimeOffset? VigenteHasta);

    [Description("Cambiar una compartición: reemplaza la revisión fijada, los permisos y la vigencia.")]
    public sealed record SolicitudCambioComparticion(
        [property: Description("Vacío = sigue la vigente del paquete.")] int? RevisionFijada,
        bool PuedeExportar,
        bool PuedeSandbox,
        [property: Description("Vacío = conserva la actual.")] DateTimeOffset? VigenteDesde,
        [property: Description("Vacío = sin vencimiento (obligatorio con un proveedor).")] DateTimeOffset? VigenteHasta);

    [Description("Organización con la que se puede compartir: cliente o proveedora, activa.")]
    public sealed record OrganizacionCompartible(Guid Id, string Nombre, [property: Description("cliente o proveedor.")] string Tipo);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorComparticion(
        [property: Description("no_encontrado (404); sin_permiso (403); no_publicado, ya_compartido, revocada, no_publicable (409).")] string Codigo,
        string Mensaje,
        [property: Description("Con no_publicable: qué ya no se cumple en la revisión fijada.")] IReadOnlyList<string>? Detalles);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/paquetes/{paqueteId:guid}/comparticiones", DelPaqueteAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarComparticionesPaquete")
            .WithSummary("Organizaciones con las que se comparte un paquete")
            .WithDescription("«Compartido con»: cada organización con su revisión, exportar, sandbox, vigencia y estado (calculado al leer), y si la persona puede compartir. 404 si el paquete no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<ListadoComparticiones>()
            .Produces<ErrorComparticion>(StatusCodes.Status404NotFound);

        grupo.MapGet("/organizaciones/{organizacionId:guid}/comparticiones", DeLaOrganizacionAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarComparticionesOrganizacion")
            .WithSummary("Paquetes compartidos con una organización")
            .WithDescription("Para la página de organizaciones de la administración: los paquetes compartidos con ella, con su vigencia y estado. Requiere atlas.contratos.ver_internos.")
            .Produces<ListadoComparticiones>();

        grupo.MapGet("/comparticiones/organizaciones", OrganizacionesAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("ListarOrganizacionesCompartibles")
            .WithSummary("Organizaciones con las que se puede compartir")
            .WithDescription("Clientes y proveedores activos, por nombre, para elegir al compartir un paquete. Requiere atlas.paquetes.gestionar.")
            .Produces<List<OrganizacionCompartible>>();

        grupo.MapPost("/paquetes/{paqueteId:guid}/comparticiones", CompartirAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .RequiereReautenticacion()
            .WithName("CompartirPaquete")
            .WithSummary("Compartir un paquete con una organización")
            .WithDescription("Solo un paquete publicado y con una organización cliente o proveedora activa (con un proveedor, con vencimiento). Una por paquete y organización: si ya se compartió, 409 ya_compartido; si se había revocado, la reactiva. Con revisión fijada, esa revisión debe seguir cumpliendo las reglas de publicación (409 no_publicable). Requiere atlas.paquetes.gestionar sobre los contratos del paquete y un ingreso reciente. Auditado.")
            .Produces<ComparticionDto>(StatusCodes.Status201Created)
            .Produces<ErrorComparticion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorComparticion>(StatusCodes.Status404NotFound)
            .Produces<ErrorComparticion>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/paquetes/{paqueteId:guid}/comparticiones/{comparticionId:guid}", CambiarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .RequiereReautenticacion()
            .WithName("CambiarComparticionPaquete")
            .WithSummary("Cambiar una compartición")
            .WithDescription("Reemplaza la revisión fijada, exportar, sandbox y la vigencia; el cambio vale desde la siguiente petición del portal. 409 revocada (compártelo otra vez para reactivarla). Requiere atlas.paquetes.gestionar sobre los contratos del paquete y un ingreso reciente. Auditado.")
            .Produces<ComparticionDto>()
            .Produces<ErrorComparticion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorComparticion>(StatusCodes.Status404NotFound)
            .Produces<ErrorComparticion>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapDelete("/paquetes/{paqueteId:guid}/comparticiones/{comparticionId:guid}", RevocarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .RequiereReautenticacion()
            .WithName("RevocarComparticionPaquete")
            .WithSummary("Revocar una compartición")
            .WithDescription("La organización deja de ver el paquete desde la siguiente petición; la fila queda para la historia (revocadaEn). 409 revocada si ya lo estaba. Requiere atlas.paquetes.gestionar sobre los contratos del paquete y un ingreso reciente. Auditado.")
            .Produces<ComparticionDto>()
            .Produces<ErrorComparticion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorComparticion>(StatusCodes.Status404NotFound)
            .Produces<ErrorComparticion>(StatusCodes.Status409Conflict);
    }

    // --- Lectura ---

    private static async Task<IResult> DelPaqueteAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        if (paquete is null)
        {
            return NoEncontrado("El paquete no existe.");
        }
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        var puede = permisos.Tiene(PermisosContratos.PaquetesGestionar)
            && (await EndpointsPaquetes.PaqueteParaGestionarAsync(paqueteId, null, usuario, contexto, lector, permisosDeSesion, ct)).Paquete is not null;
        var comparticiones = await contexto.Comparticiones.AsNoTracking().Where(c => c.PaqueteId == paqueteId).ToListAsync(ct);
        var elementos = await DtosAsync(comparticiones, new Dictionary<Guid, PaqueteContratos> { [paquete.Id] = paquete }, _ => puede, plataforma, reloj, ct);
        return Results.Ok(new ListadoComparticiones(puede && paquete.Estado == EstadoPaquete.Publicado, elementos));
    }

    private static async Task<IResult> DeLaOrganizacionAsync(
        [Description("Id de la organización.")] Guid organizacionId,
        ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var comparticiones = await contexto.Comparticiones.AsNoTracking().Where(c => c.OrganizacionId == organizacionId).ToListAsync(ct);
        var ids = comparticiones.Select(c => c.PaqueteId).Distinct().ToList();
        var paquetes = await contexto.Paquetes.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        // Aquí no se calcula si puede gestionar cada una: se gestiona desde el paquete.
        return Results.Ok(new ListadoComparticiones(false, await DtosAsync(comparticiones, paquetes, _ => false, plataforma, reloj, ct)));
    }

    private static async Task<IResult> OrganizacionesAsync(PlataformaDbContext plataforma, CancellationToken ct)
    {
        var organizaciones = await plataforma.Organizaciones.AsNoTracking()
            .Where(o => o.Tipo != TipoOrganizacion.Interna && o.Estado == EstadoOrganizacion.Activa)
            .Select(o => new { o.Id, o.Nombre, o.Tipo })
            .ToListAsync(ct);
        return Results.Ok(organizaciones
            .OrderBy(o => o.Nombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.Id)
            .Select(o => new OrganizacionCompartible(o.Id, o.Nombre, ReglasComparticiones.Texto(o.Tipo)))
            .ToList());
    }

    // --- Escritura ---

    private static async Task<IResult> CompartirAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("La organización, la revisión, los permisos y la vigencia.")] SolicitudComparticion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, error) = await GestionableAsync(paqueteId, solicitud?.RevisionFijada, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (solicitud is null || solicitud.OrganizacionId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["organizacionId"] = ["Elige la organización."] });
        }
        var organizacion = await plataforma.Organizaciones.AsNoTracking().FirstOrDefaultAsync(o => o.Id == solicitud.OrganizacionId, ct);
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (organizacion is null)
        {
            errores["organizacionId"] = ["La organización no existe."];
        }
        else if (organizacion.Tipo == TipoOrganizacion.Interna)
        {
            errores["organizacionId"] = ["Los paquetes se comparten con clientes y proveedores, no con FLIT."];
        }
        else if (organizacion.Estado != EstadoOrganizacion.Activa)
        {
            errores["organizacionId"] = ["La organización está suspendida."];
        }
        var ahora = reloj.GetUtcNow();
        var (desde, hasta, deVigencia) = ReglasComparticiones.Vigencia(solicitud.VigenteDesde, solicitud.VigenteHasta, organizacion?.Tipo ?? TipoOrganizacion.Cliente, ahora);
        foreach (var (campo, mensajes) in deVigencia)
        {
            errores[campo] = mensajes;
        }
        if (await ErrorDeRevisionAsync(paquete, solicitud.RevisionFijada, errores, contexto, ct) is { } conflicto)
        {
            return conflicto;
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (paquete.Estado != EstadoPaquete.Publicado)
        {
            return NoPublicado(paquete);
        }

        var existente = await contexto.Comparticiones.FirstOrDefaultAsync(c => c.PaqueteId == paqueteId && c.OrganizacionId == solicitud.OrganizacionId, ct);
        if (existente is { RevocadoEn: null })
        {
            return Conflicto("ya_compartido", "El paquete ya está compartido con esa organización: cámbiala en vez de crear otra.");
        }
        var persona = PersonaDeSesion(http.User);
        var comparticion = existente ?? new ComparticionPaquete
        {
            PaqueteId = paqueteId, OrganizacionId = solicitud.OrganizacionId, VigenteDesde = desde, OtorgadoPor = persona,
        };
        comparticion.RevisionFijada = solicitud.RevisionFijada;
        comparticion.PuedeExportar = solicitud.PuedeExportar;
        comparticion.PuedeSandbox = solicitud.PuedeSandbox;
        comparticion.VigenteDesde = desde;
        comparticion.VigenteHasta = hasta;
        comparticion.OtorgadoPor = persona;
        comparticion.RevocadoEn = null;
        comparticion.RevocadoPor = null;
        if (existente is null)
        {
            contexto.Comparticiones.Add(comparticion);
        }
        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionCreada, comparticion, DatosAuditoria(comparticion, reactivada: existente is not null)), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Conflicto("ya_compartido", "Alguien compartió el paquete con esa organización al mismo tiempo.");
        }
        return Results.Created($"/api/v1/contratos/paquetes/{paqueteId}/comparticiones/{comparticion.Id}",
            await DtoAsync(comparticion, paquete, true, plataforma, reloj, ct));
    }

    private static async Task<IResult> CambiarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Id de la compartición.")] Guid comparticionId,
        [Description("La revisión, los permisos y la vigencia.")] SolicitudCambioComparticion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, Sandbox.ServicioSandbox sandbox, TimeProvider reloj, CancellationToken ct)
    {
        var comparticion = await contexto.Comparticiones.FirstOrDefaultAsync(c => c.Id == comparticionId && c.PaqueteId == paqueteId, ct);
        if (comparticion is null)
        {
            return NoEncontrado("La compartición no existe.");
        }
        var (paquete, error) = await GestionableAsync(paqueteId, solicitud?.RevisionFijada, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (comparticion.RevocadoEn is not null)
        {
            return Revocada();
        }
        if (solicitud is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [""] = ["Falta el cuerpo."] });
        }
        var tipo = await plataforma.Organizaciones.AsNoTracking().Where(o => o.Id == comparticion.OrganizacionId).Select(o => (TipoOrganizacion?)o.Tipo).FirstOrDefaultAsync(ct);
        var ahora = reloj.GetUtcNow();
        var (desde, hasta, errores) = ReglasComparticiones.Vigencia(solicitud.VigenteDesde ?? comparticion.VigenteDesde, solicitud.VigenteHasta,
            tipo ?? TipoOrganizacion.Cliente, ahora);
        if (await ErrorDeRevisionAsync(paquete, solicitud.RevisionFijada, errores, contexto, ct) is { } conflicto)
        {
            return conflicto;
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var anterior = DatosAuditoria(comparticion, reactivada: false);
        comparticion.RevisionFijada = solicitud.RevisionFijada;
        comparticion.PuedeExportar = solicitud.PuedeExportar;
        comparticion.PuedeSandbox = solicitud.PuedeSandbox;
        comparticion.VigenteDesde = desde;
        comparticion.VigenteHasta = hasta;
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionCambiada, comparticion, new
        {
            despues = DatosAuditoria(comparticion, reactivada: false), antes = anterior,
        }), ct);
        // HU 4.7 (ATLAS-ADR-0045): sin sandbox o con otra revisión fijada, su sandbox se apaga o se reimporta ya.
        await sandbox.RevisarComparticionAsync(comparticion.Id, ct);
        return Results.Ok(await DtoAsync(comparticion, paquete, true, plataforma, reloj, ct));
    }

    private static async Task<IResult> RevocarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Id de la compartición.")] Guid comparticionId,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, Sandbox.ServicioSandbox sandbox, TimeProvider reloj, CancellationToken ct)
    {
        var comparticion = await contexto.Comparticiones.FirstOrDefaultAsync(c => c.Id == comparticionId && c.PaqueteId == paqueteId, ct);
        if (comparticion is null)
        {
            return NoEncontrado("La compartición no existe.");
        }
        var (paquete, error) = await GestionableAsync(paqueteId, comparticion.RevisionFijada, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (comparticion.RevocadoEn is not null)
        {
            return Revocada();
        }
        var ahora = reloj.GetUtcNow();
        comparticion.RevocadoEn = ahora;
        comparticion.RevocadoPor = PersonaDeSesion(http.User);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionRevocada, comparticion, DatosAuditoria(comparticion, reactivada: false)), ct);
        // HU 4.7 (ATLAS-ADR-0045): revocar apaga su sandbox y lo borra de Microcks en este momento.
        await sandbox.RevisarComparticionAsync(comparticion.Id, ct);
        return Results.Ok(await DtoAsync(comparticion, paquete, true, plataforma, reloj, ct));
    }

    // --- Piezas comunes ---

    /// <summary>
    /// El paquete (con seguimiento) si existe y la persona puede gestionarlo sobre los contratos de su última revisión y de
    /// la que vería la organización (la fijada pedida, o la vigente). Si no, 404 o 403.
    /// </summary>
    private static async Task<(PaqueteContratos? Paquete, IResult? Error)> GestionableAsync(
        Guid paqueteId, int? revisionFijada, ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var vigente = await contexto.Paquetes.AsNoTracking().Where(p => p.Id == paqueteId).Select(p => new { p.RevisionPublicada, p.RevisionActual }).FirstOrDefaultAsync(ct);
        if (vigente is null)
        {
            return (null, NoEncontrado("El paquete no existe."));
        }
        // Una revisión fijada que no existe se rechaza después con un 400; aquí se revisa el permiso con la vigente.
        var numero = revisionFijada is { } n && n >= 1 && n <= vigente.RevisionActual ? n : vigente.RevisionPublicada;
        var (paquete, _, error) = await EndpointsPaquetes.PaqueteParaGestionarAsync(paqueteId, numero, usuario, contexto, lector, permisosDeSesion, ct);
        return paquete is null && error is not null && error is IStatusCodeHttpResult { StatusCode: StatusCodes.Status403Forbidden }
            ? (null, SinPermiso())
            : (paquete, paquete is null ? error : null);
    }

    /// <summary>La revisión fijada debe existir y cumplir hoy las reglas de publicación (409 no_publicable con el detalle).</summary>
    private static async Task<IResult?> ErrorDeRevisionAsync(
        PaqueteContratos paquete, int? revisionFijada, Dictionary<string, string[]> errores, ContratosDbContext contexto, CancellationToken ct)
    {
        if (revisionFijada is not { } numero)
        {
            return null;
        }
        if (numero < 1 || numero > paquete.RevisionActual)
        {
            errores["revisionFijada"] = [$"El paquete tiene las revisiones 1 a {paquete.RevisionActual}."];
            return null;
        }
        var revision = await contexto.RevisionesPaquete.AsNoTracking().FirstAsync(r => r.PaqueteId == paquete.Id && r.Numero == numero, ct);
        var problemas = await EndpointsPaquetes.ProblemasParaPublicarAsync(contexto, FotoPaquete.LeerDefinicion(revision.Definicion), ct);
        return problemas.Count == 0
            ? null
            : Results.Json(new ErrorComparticion("no_publicable", $"La revisión {numero} ya no se puede compartir: {problemas[0]}", [.. problemas.Take(20)]),
                statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<List<ComparticionDto>> DtosAsync(
        List<ComparticionPaquete> comparticiones, Dictionary<Guid, PaqueteContratos> paquetes, Func<ComparticionPaquete, bool> puedeGestionar,
        PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var idsOrganizaciones = comparticiones.Select(c => c.OrganizacionId).Distinct().ToList();
        var organizaciones = await plataforma.Organizaciones.AsNoTracking().Where(o => idsOrganizaciones.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var nombres = await NombresAsync(plataforma, comparticiones.Select(c => (Guid?)c.OtorgadoPor), ct);
        var ahora = reloj.GetUtcNow();
        return
        [
            .. comparticiones
                .Where(c => paquetes.ContainsKey(c.PaqueteId))
                .Select(c => Dto(c, paquetes[c.PaqueteId], organizaciones.GetValueOrDefault(c.OrganizacionId), nombres, puedeGestionar(c), ahora))
                .OrderBy(c => c.Estado is ReglasComparticiones.Vigente or ReglasComparticiones.Programada ? 0 : 1)
                .ThenByDescending(c => c.ActualizadaEn)
                .ThenBy(c => c.Id),
        ];
    }

    private static async Task<ComparticionDto> DtoAsync(
        ComparticionPaquete comparticion, PaqueteContratos paquete, bool puedeGestionar, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct) =>
        (await DtosAsync([comparticion], new Dictionary<Guid, PaqueteContratos> { [paquete.Id] = paquete }, _ => puedeGestionar, plataforma, reloj, ct))[0];

    private static ComparticionDto Dto(
        ComparticionPaquete c, PaqueteContratos p, Organizacion? o, IReadOnlyDictionary<Guid, string> nombres, bool puedeGestionar, DateTimeOffset ahora) => new(
        c.Id, p.Id, p.Nombre, ReglasPaquetes.Texto(p.Estado),
        new OrganizacionDeComparticion(c.OrganizacionId, o?.Nombre ?? "Organización no disponible", o is null ? null : ReglasComparticiones.Texto(o.Tipo),
            o is null ? null : o.Estado == EstadoOrganizacion.Activa ? "activa" : "suspendida"),
        c.RevisionFijada, c.RevisionFijada ?? p.RevisionPublicada, c.PuedeExportar, c.PuedeSandbox, c.VigenteDesde, c.VigenteHasta,
        ReglasComparticiones.Estado(c, ahora), new PersonaRef(c.OtorgadoPor, nombres.GetValueOrDefault(c.OtorgadoPor) ?? "Persona no disponible"),
        c.CreadoEn, c.ActualizadoEn, c.RevocadoEn, puedeGestionar && c.RevocadoEn is null && p.Estado != EstadoPaquete.Archivado);

    private static object DatosAuditoria(ComparticionPaquete c, bool reactivada) => new
    {
        paquete = c.PaqueteId,
        organizacion = c.OrganizacionId,
        revisionFijada = c.RevisionFijada,
        puedeExportar = c.PuedeExportar,
        puedeSandbox = c.PuedeSandbox,
        vigenteDesde = c.VigenteDesde,
        vigenteHasta = c.VigenteHasta,
        reactivada,
    };

    /// <summary>
    /// Auditoría de una acción sobre una compartición, con la organización destino (la página de la organización y su
    /// auditoría la encuentran por ella): ids, revisión, permisos y vigencia; nunca nombres.
    /// </summary>
    private static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, ComparticionPaquete c, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = c.OrganizacionId,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = c.Id.ToString(),
        Version = c.RevisionFijada?.ToString(CultureInfo.InvariantCulture),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorComparticion(codigo, mensaje, null), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo el líder técnico o el responsable de los aplicativos de todos sus contratos comparten este paquete (atlas.paquetes.gestionar).");

    private static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    private static IResult NoPublicado(PaqueteContratos paquete) => Conflicto("no_publicado",
        paquete.Estado == EstadoPaquete.Archivado ? "El paquete está archivado: ya no se comparte." : "Publica el paquete antes de compartirlo.");

    private static IResult Revocada() => Conflicto("revocada", "La compartición está revocada: compártelo otra vez para reactivarla.");
}
