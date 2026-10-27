using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Almacenamiento;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Contratos.Exportaciones.ServicioExportaciones;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

/// <summary>
/// Exportaciones de contratos (HU 4.5, plan 00 §6.3 y §8.7, ATLAS-ADR-0042). Internas en
/// <c>/api/v1/contratos/exportaciones</c> (un paquete en una revisión, podado; o una versión completa) y del portal en
/// <c>/api/v1/portal/exportaciones</c> (solo paquetes compartidos con la organización y con exportación permitida, según
/// <see cref="AccesoPortal.PuedeExportarAsync"/>, con los specs que ve el portal). Solicitar exige reautenticación (prompt maestro §6.3), sube la entrada
/// al S3 y responde 202 sin esperar la generación: atlas-docs la toma de la cola. Cada persona ve y descarga solo las
/// suyas; la descarga es una URL firmada de 15 minutos. Solicitar, descargar y fallar quedan en la auditoría.
/// </summary>
internal static class EndpointsExportaciones
{
    private const int LimiteListado = 20;
    private const int DetallesMaximos = 20;

    // --- DTO ---

    [Description("Qué exportar: un paquete (en una revisión) o una versión de un contrato, y en qué formatos.")]
    public sealed record SolicitudExportacion(
        [property: Description("Paquete a exportar (specs podados a su selección, guías y changelog). Va solo, sin versionId.")] Guid? PaqueteId,
        [property: Description("Revisión del paquete; vacío = la vigente o, si nunca se publicó, la última.")] int? Revision,
        [property: Description("Versión de un contrato a exportar completa (solo internos). Va sola, sin paqueteId.")] Guid? VersionId,
        [property: Description("openapi, postman, html, pdf, word, excel o zip; el ZIP con todo siempre se agrega.")] IReadOnlyList<string?>? Formatos);

    [Description("Qué exportar desde el portal: un paquete compartido con tu organización, en la revisión que ella ve.")]
    public sealed record SolicitudExportacionPortal(
        [property: Description("Paquete compartido con la organización y con exportación permitida.")] Guid PaqueteId,
        [property: Description("openapi, postman, html, pdf, word, excel o zip; el ZIP con todo siempre se agrega.")] IReadOnlyList<string?>? Formatos);

    [Description("De dónde sale la exportación.")]
    public sealed record OrigenExportacionDto(
        [property: Description("paquete o version.")] string Tipo,
        Guid? PaqueteId,
        [property: Description("Revisión del paquete exportada.")] int? Revision,
        Guid? VersionId);

    [Description("Un archivo generado.")]
    public sealed record ArchivoExportacionDto(
        [property: Description("Ruta dentro de la exportación; es el valor de archivo en la descarga.")] string Ruta,
        [property: Description("Nombre con que se descarga.")] string Nombre,
        [property: Description("openapi, postman, html, pdf, word, excel o zip.")] string Formato,
        long Bytes);

    [Description("Por qué falló una exportación.")]
    public sealed record ErrorExportacionDto(
        [property: Description("spec_invalido, pdf_no_disponible, entrada_invalida, tiempo_agotado o interno.")] string Codigo,
        string Mensaje);

    [Description("Una exportación y su estado.")]
    public sealed record ExportacionDto(
        Guid Id,
        [property: Description("pendiente, procesando, lista, fallida o vencida.")] string Estado,
        OrigenExportacionDto Origen,
        [property: Description("Para mostrar: el paquete y su revisión, o el contrato y su versión.")] string Nombre,
        IReadOnlyList<string> Formatos,
        DateTimeOffset SolicitadaEn,
        DateTimeOffset? IniciadaEn,
        [property: Description("Cuándo quedó lista o fallida.")] DateTimeOffset? ListaEn,
        [property: Description("Hasta cuándo se puede descargar; después se borran los archivos.")] DateTimeOffset? VenceEn,
        [property: Description("SHA-256 del contenido exportado (va en la portada de cada formato).")] string? HashContenido,
        [property: Description("Los archivos, cuando está lista.")] IReadOnlyList<ArchivoExportacionDto> Archivos,
        ErrorExportacionDto? Error);

    [Description("Las exportaciones de la persona, de la más reciente a la más antigua.")]
    public sealed record ListadoExportaciones(IReadOnlyList<ExportacionDto> Elementos);

    [Description("URL firmada para descargar un archivo de una exportación.")]
    public sealed record DescargaExportacion(
        [property: Description("URL del almacenamiento S3, firmada y de un solo archivo; deja de servir al vencer.")] Uri Url,
        string Nombre,
        long Bytes,
        [property: Description("Cuándo vence la URL (15 minutos).")] DateTimeOffset VenceEn);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorExportacion(
        [property: Description("no_encontrado (404); sin_permiso (403); no_exportable, no_lista (409); vencida (410); almacenamiento_no_disponible (503).")] string Codigo,
        string Mensaje,
        [property: Description("Con no_exportable: qué impide exportar.")] IReadOnlyList<string>? Detalles);

    // --- Rutas ---

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/exportaciones", SolicitarAsync)
            .RequierePermiso(PermisosContratos.ExportacionesSolicitar)
            .RequiereReautenticacion()
            .ConLimiteDeExportacion()
            .WithName("SolicitarExportacionContratos")
            .WithSummary("Solicitar una exportación")
            .WithDescription("Arma el paquete a exportar (specs de la revisión podados a su selección, guías y changelog; o, para una versión, el spec completo), lo deja en el S3 y encola la generación: responde 202 sin esperarla. El estado se consulta en GET /exportaciones/{id} y al terminar llega un aviso. 409 no_exportable si la poda falla o el contrato no se exporta (proto). 429 exportaciones_en_curso si la persona ya tiene el máximo de exportaciones pendientes o en proceso (2 por defecto), con Retry-After. Requiere atlas.exportaciones.solicitar, atlas.contratos.ver_internos y un ingreso reciente.")
            .Produces<ExportacionDto>(StatusCodes.Status202Accepted)
            .Produces<ErrorExportacion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacion>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacion>(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        grupo.MapGet("/exportaciones", ListarAsync)
            .RequierePermiso(PermisosContratos.ExportacionesSolicitar)
            .WithName("ListarExportacionesContratos")
            .WithSummary("Mis exportaciones")
            .WithDescription("Las últimas 20 exportaciones internas de la persona, de la más reciente a la más antigua; con paquete o version, solo las de ese origen. Requiere atlas.exportaciones.solicitar.")
            .Produces<ListadoExportaciones>();

        grupo.MapGet("/exportaciones/{exportacionId:guid}", ObtenerAsync)
            .RequierePermiso(PermisosContratos.ExportacionesSolicitar)
            .WithName("ObtenerExportacionContratos")
            .WithSummary("Estado de una exportación")
            .WithDescription("pendiente, procesando, lista (con sus archivos), fallida (con el error) o vencida. Solo la ve quien la pidió (404 para los demás). Requiere atlas.exportaciones.solicitar.")
            .Produces<ExportacionDto>()
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound);

        grupo.MapGet("/exportaciones/{exportacionId:guid}/descarga", DescargarAsync)
            .RequierePermiso(PermisosContratos.ExportacionesSolicitar)
            .WithName("DescargarExportacionContratos")
            .WithSummary("URL firmada de descarga")
            .WithDescription("Una URL del S3 firmada por 15 minutos para el ZIP o, con archivo, para uno de sus archivos. Pasado ese tiempo la URL deja de servir: se pide otra. 409 no_lista; 410 vencida (pasó la retención). Auditado. Requiere atlas.exportaciones.solicitar.")
            .Produces<DescargaExportacion>()
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacion>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacion>(StatusCodes.Status410Gone)
            .Produces<ErrorExportacion>(StatusCodes.Status503ServiceUnavailable);
    }

    public static void MapearPortal(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/exportaciones", SolicitarPortalAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .RequiereReautenticacion()
            .ConLimiteDeExportacion()
            .WithName("SolicitarExportacionPortal")
            .WithSummary("Solicitar una exportación desde el portal")
            .WithDescription("Exporta un paquete compartido con la organización de la persona, en la revisión que ella ve, con sus specs podados: responde 202 y la generación sigue en la cola. 403 si la compartición no está vigente o no permite exportar. 429 exportaciones_en_curso si la persona o su organización ya tienen el máximo de exportaciones pendientes o en proceso (2 por defecto en cada caso), con Retry-After. Requiere atlas.portal.ver, la compartición con puede_exportar y un ingreso reciente.")
            .Produces<ExportacionDto>(StatusCodes.Status202Accepted)
            .Produces<ErrorExportacion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacion>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacion>(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        grupo.MapGet("/exportaciones/{exportacionId:guid}", ObtenerPortalAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ObtenerExportacionPortal")
            .WithSummary("Estado de una exportación del portal")
            .WithDescription("Solo la ve quien la pidió, dentro de su organización (404 para los demás). Requiere atlas.portal.ver.")
            .Produces<ExportacionDto>()
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound);

        grupo.MapGet("/exportaciones/{exportacionId:guid}/descarga", DescargarPortalAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("DescargarExportacionPortal")
            .WithSummary("URL firmada de descarga desde el portal")
            .WithDescription("Como la interna, y además revisa que la compartición siga vigente y permita exportar (403 si no). URL firmada de 15 minutos; auditada. Requiere atlas.portal.ver.")
            .Produces<DescargaExportacion>()
            .Produces<ErrorExportacion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorExportacion>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacion>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacion>(StatusCodes.Status410Gone)
            .Produces<ErrorExportacion>(StatusCodes.Status503ServiceUnavailable);
    }

    // --- Internas ---

    private static async Task<IResult> SolicitarAsync(
        [Description("Qué exportar y en qué formatos.")] SolicitudExportacion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen, IPermisosDeSesion permisosDeSesion,
        ILectorAplicativos lector, IOptions<OpcionesExportaciones> opciones, IOptions<OpcionesPortal> portal, TimeProvider reloj, CancellationToken ct)
    {
        var formatos = FormatosExportacion.Normalizar(solicitud?.Formatos);
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (formatos is null)
        {
            errores["formatos"] = [$"Elige uno o más de: {string.Join(", ", FormatosExportacion.Todos)}."];
        }
        if ((solicitud?.PaqueteId is null) == (solicitud?.VersionId is null))
        {
            errores["origen"] = ["Indica paqueteId o versionId, no los dos."];
        }
        if (solicitud?.VersionId is not null && solicitud.Revision is not null)
        {
            errores["revision"] = ["La revisión es de un paquete; una versión se exporta sin ella."];
        }
        if (solicitud?.Revision is <= 0)
        {
            errores["revision"] = ["La revisión empieza en 1."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var permisos = await permisosDeSesion.ObtenerAsync(http.User, ct);
        if (!permisos.Tiene(PermisosContratos.VerInternos))
        {
            return Error(StatusCodes.Status403Forbidden, "sin_permiso", "Exportar contratos internos exige atlas.contratos.ver_internos.");
        }
        if (!almacen.Disponible)
        {
            return SinAlmacen();
        }
        var tope = Tope(http.User, null, opciones.Value);
        if (await ExcedeTopeAsync(contexto, tope, ct) is { } porOrganizacion)
        {
            return EnCurso(http, porOrganizacion, opciones.Value);
        }

        var ahora = reloj.GetUtcNow();
        var id = Guid.CreateVersion7();
        EntradaArmada armada;
        ExportacionContratos borrador;
        try
        {
            if (solicitud!.PaqueteId is { } paqueteId)
            {
                var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
                if (paquete is null)
                {
                    return NoEncontrado("El paquete no existe.");
                }
                var numero = solicitud.Revision ?? paquete.RevisionPublicada ?? paquete.RevisionActual;
                var revision = await contexto.RevisionesPaquete.AsNoTracking().FirstOrDefaultAsync(r => r.PaqueteId == paqueteId && r.Numero == numero, ct);
                if (revision is null)
                {
                    return NoEncontrado("La revisión no existe.");
                }
                var slugs = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id, a => a.Slug);
                var ambiente = opciones.Value.Ambiente;
                armada = await ArmadorEntrada.DePaqueteAsync(contexto, paquete, revision, ArmadorEntrada.OrganizacionInterna, ahora, ambiente,
                    c => portal.Value.UrlsDe(slugs.GetValueOrDefault(c.AplicativoId) ?? string.Empty, c.Codigo, ambiente), ct);
                borrador = Borrador(id, http.User, null, false, OrigenExportacion.Paquete, paqueteId, numero, null, armada, formatos!);
            }
            else
            {
                var version = await contexto.Versiones.AsNoTracking().FirstOrDefaultAsync(v => v.Id == solicitud.VersionId, ct);
                var contrato = version is null ? null : await contexto.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == version.ContratoId, ct);
                if (version is null || contrato is null)
                {
                    return NoEncontrado("La versión no existe.");
                }
                armada = await ArmadorEntrada.DeVersionAsync(contexto, contrato, version, ahora, ct);
                borrador = Borrador(id, http.User, null, false, OrigenExportacion.Version, null, null, version.Id, armada, formatos!);
            }
        }
        catch (NoExportableException ex)
        {
            return NoExportable(ex);
        }
        return await CrearYResponderAsync(http, contexto, plataforma, almacen, borrador, armada, ahora, "/api/v1/contratos", tope, opciones.Value, ct);
    }

    private static async Task<IResult> ListarAsync(
        [Description("Solo las de este paquete.")] Guid? paquete,
        [Description("Solo las de esta versión.")] Guid? version,
        HttpContext http, ContratosDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(http.User);
        var consulta = contexto.Exportaciones.AsNoTracking().Where(e => e.SolicitadaPor == persona && !e.DesdePortal);
        if (paquete is { } p)
        {
            consulta = consulta.Where(e => e.PaqueteId == p);
        }
        if (version is { } v)
        {
            consulta = consulta.Where(e => e.VersionId == v);
        }
        var filas = await consulta.OrderByDescending(e => e.SolicitadaEn).ThenByDescending(e => e.Id).Take(LimiteListado).ToListAsync(ct);
        return Results.Ok(new ListadoExportaciones([.. filas.Select(Dto)]));
    }

    private static async Task<IResult> ObtenerAsync(
        [Description("Id de la exportación.")] Guid exportacionId, HttpContext http, ContratosDbContext contexto, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, portal: false, ct);
        return exportacion is null ? NoEncontrado("La exportación no existe.") : Results.Ok(Dto(exportacion));
    }

    private static async Task<IResult> DescargarAsync(
        [Description("Id de la exportación.")] Guid exportacionId,
        [Description("Ruta de un archivo de la exportación; vacío = el ZIP.")] string? archivo,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen,
        IOptions<OpcionesExportaciones> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, portal: false, ct);
        return exportacion is null
            ? NoEncontrado("La exportación no existe.")
            : await UrlAsync(http, plataforma, almacen, opciones.Value, reloj, exportacion, archivo, ct);
    }

    // --- Portal ---

    private static async Task<IResult> SolicitarPortalAsync(
        [Description("El paquete compartido y los formatos.")] SolicitudExportacionPortal solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen,
        AccesoPortal acceso, SpecsDelPortal specs, IOptions<OpcionesExportaciones> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var formatos = FormatosExportacion.Normalizar(solicitud?.Formatos);
        if (formatos is null || solicitud!.PaqueteId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [formatos is null ? "formatos" : "paqueteId"] = formatos is null
                    ? [$"Elige uno o más de: {string.Join(", ", FormatosExportacion.Todos)}."]
                    : ["Indica el paquete."],
            });
        }
        if (Organizacion(http.User) is not { } organizacionId)
        {
            return Error(StatusCodes.Status403Forbidden, "sin_permiso", "El portal es para personas de una organización cliente.");
        }
        // Un paquete que no está compartido con su organización (o fuera de su alcance) es 404, como si no existiera.
        var decision = await acceso.PuedeExportarAsync(http.User, solicitud.PaqueteId, ct);
        if (!decision.Permitida || decision.Paquete is null)
        {
            return await NegadaAuditadaAsync(http, plataforma, acceso, reloj, decision, solicitud.PaqueteId, "solicitar", ct);
        }
        if (!almacen.Disponible)
        {
            return SinAlmacen();
        }
        var tope = Tope(http.User, organizacionId, opciones.Value);
        if (await ExcedeTopeAsync(contexto, tope, ct) is { } porOrganizacion)
        {
            return EnCurso(http, porOrganizacion, opciones.Value);
        }
        var organizacion = await plataforma.Organizaciones.AsNoTracking().Where(o => o.Id == organizacionId).Select(o => o.Nombre).FirstOrDefaultAsync(ct);
        if (organizacion is null)
        {
            return NoEncontrado("El paquete no existe.");
        }

        var ahora = reloj.GetUtcNow();
        EntradaArmada armada;
        try
        {
            armada = await ArmadorEntrada.DePortalAsync(contexto, specs, decision.Paquete, organizacion, ahora, ct);
        }
        catch (NoExportableException)
        {
            // Al cliente no se le detalla qué falló en la poda (nombra contratos y operaciones internas).
            return Results.Json(new ErrorExportacion("no_exportable", "Este paquete no se puede exportar en este momento. Avísale a tu contacto en FLIT.", null),
                statusCode: StatusCodes.Status409Conflict);
        }
        var borrador = Borrador(Guid.CreateVersion7(), http.User, organizacionId, true, OrigenExportacion.Paquete, decision.Paquete.Paquete.Id,
            decision.Paquete.Revision, null, armada, formatos);
        return await CrearYResponderAsync(http, contexto, plataforma, almacen, borrador, armada, ahora, ExtensionesRutaPortal, tope, opciones.Value, ct);
    }

    /// <summary>La negación de un externo queda en la auditoría como las del portal (§8.9, F4-SEC-25).</summary>
    private static async Task<IResult> NegadaAuditadaAsync(
        HttpContext http, PlataformaDbContext plataforma, AccesoPortal acceso, TimeProvider reloj, DecisionPortal decision, Guid paqueteId,
        string operacion, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        await EndpointsPortal.AuditarAsync(http, plataforma, reloj, EndpointsPortal.AccionNegado, vista, paqueteId.ToString(), null,
            new { detalle = $"exportacion_{operacion}:{decision.Codigo ?? "no_encontrado"}" }, ct);
        return Negada(decision);
    }

    /// <summary>no_encontrado → 404 sin revelar el paquete; lo demás (solo_resumen, no_permitido_por_comparticion, sin_permiso) → 403.</summary>
    private static IResult Negada(DecisionPortal decision) => decision.Codigo switch
    {
        null or "no_encontrado" => NoEncontrado("El paquete no existe."),
        "no_permitido_por_comparticion" => Error(StatusCodes.Status403Forbidden, decision.Codigo, "La compartición de este paquete no permite exportarlo."),
        "solo_resumen" => Error(StatusCodes.Status403Forbidden, decision.Codigo, "Tu acceso a este paquete es solo de resumen."),
        _ => Error(StatusCodes.Status403Forbidden, decision.Codigo, "No tienes permiso para exportar este paquete."),
    };

    private const string ExtensionesRutaPortal = "/api/v1/portal";

    private static async Task<IResult> ObtenerPortalAsync(
        [Description("Id de la exportación.")] Guid exportacionId, HttpContext http, ContratosDbContext contexto, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, portal: true, ct);
        return exportacion is null ? NoEncontrado("La exportación no existe.") : Results.Ok(Dto(exportacion));
    }

    private static async Task<IResult> DescargarPortalAsync(
        [Description("Id de la exportación.")] Guid exportacionId,
        [Description("Ruta de un archivo de la exportación; vacío = el ZIP.")] string? archivo,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen,
        AccesoPortal acceso, IOptions<OpcionesExportaciones> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, portal: true, ct);
        if (exportacion is null)
        {
            return NoEncontrado("La exportación no existe.");
        }
        // Una compartición revocada o vencida corta también la descarga de lo ya exportado.
        var decision = await acceso.PuedeExportarAsync(http.User, exportacion.PaqueteId!.Value, ct);
        if (!decision.Permitida)
        {
            return await NegadaAuditadaAsync(http, plataforma, acceso, reloj, decision, exportacion.PaqueteId.Value, "descargar", ct);
        }
        return await UrlAsync(http, plataforma, almacen, opciones.Value, reloj, exportacion, archivo, ct);
    }

    // --- Piezas comunes ---

    private static ExportacionContratos Borrador(
        Guid id, ClaimsPrincipal usuario, Guid? organizacionId, bool portal, OrigenExportacion origen, Guid? paqueteId, int? revision, Guid? versionId,
        EntradaArmada armada, IReadOnlyList<string> formatos) => new()
    {
        Id = id,
        SolicitadaPor = Persona(usuario),
        OrganizacionId = organizacionId,
        DesdePortal = portal,
        Origen = origen,
        PaqueteId = paqueteId,
        Revision = revision,
        VersionId = versionId,
        ContratoId = armada.ContratoId,
        Nombre = armada.Nombre.Length <= ContratosDbContext.LargoNombreExportacion ? armada.Nombre : armada.Nombre[..ContratosDbContext.LargoNombreExportacion],
        Formatos = [.. formatos],
        EntradaClave = ClaveEntrada(id),
        EntradaSha256 = new string('0', 64),
        SolicitadaEn = default,
    };

    private static async Task<IResult> CrearYResponderAsync(
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen, ExportacionContratos borrador,
        EntradaArmada armada, DateTimeOffset ahora, string baseRuta, TopeEnCurso tope, OpcionesExportaciones opciones, CancellationToken ct)
    {
        try
        {
            var exportacion = await CrearAsync(contexto, plataforma, almacen, borrador, armada.Entrada, Actor(http), ahora, ct, tope);
            return Results.Accepted($"{baseRuta}/exportaciones/{exportacion.Id}", Dto(exportacion));
        }
        catch (AlmacenNoDisponibleException)
        {
            return SinAlmacen();
        }
        catch (ExportacionesEnCursoException ex)
        {
            // Otra solicitud simultánea tomó el último cupo; la entrada subida queda huérfana (como si fallara la base).
            return EnCurso(http, ex.PorOrganizacion, opciones);
        }
    }

    private static TopeEnCurso Tope(ClaimsPrincipal usuario, Guid? organizacionId, OpcionesExportaciones opciones) =>
        new(Persona(usuario), organizacionId, opciones.MaximoEnCursoPorPersona, opciones.MaximoEnCursoPorOrganizacion);

    /// <summary>
    /// 429 con <c>Retry-After</c> y problem+json en español (como el limitador de tasa), con <c>codigo</c>
    /// <c>exportaciones_en_curso</c>: la PWA muestra el título.
    /// </summary>
    private static IResult EnCurso(HttpContext http, bool porOrganizacion, OpcionesExportaciones opciones)
    {
        var segundos = Math.Max(1, (int)Math.Ceiling(opciones.EsperaEnCurso.TotalSeconds));
        var maximo = porOrganizacion ? opciones.MaximoEnCursoPorOrganizacion : opciones.MaximoEnCursoPorPersona;
        var titulo = porOrganizacion
            ? $"Tu organización ya tiene {maximo} exportaciones en curso. Espera a que terminen e intenta de nuevo."
            : $"Ya tienes {maximo} exportaciones en curso. Espera a que terminen e intenta de nuevo.";
        http.Response.Headers.RetryAfter = segundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: titulo,
            extensions: new Dictionary<string, object?> { ["codigo"] = CodigoEnCurso, ["reintentarEnSegundos"] = segundos });
    }

    public const string CodigoEnCurso = "exportaciones_en_curso";

    private static async Task<IResult> UrlAsync(
        HttpContext http, PlataformaDbContext plataforma, IAlmacenObjetos almacen, OpcionesExportaciones opciones, TimeProvider reloj,
        ExportacionContratos exportacion, string? archivo, CancellationToken ct)
    {
        if (exportacion.Estado == EstadoExportacion.Vencida)
        {
            return Error(StatusCodes.Status410Gone, "vencida", "La exportación venció y sus archivos se borraron; solicítala de nuevo.");
        }
        if (exportacion.Estado != EstadoExportacion.Lista)
        {
            return Error(StatusCodes.Status409Conflict, "no_lista", "La exportación aún no está lista.");
        }
        var archivos = LeerArchivos(exportacion.Archivos);
        var elegido = string.IsNullOrWhiteSpace(archivo)
            ? archivos.FirstOrDefault(a => a.Clave == exportacion.Objeto)
            : archivos.FirstOrDefault(a => a.Ruta == archivo);
        if (elegido is null)
        {
            return NoEncontrado("El archivo no está en esta exportación.");
        }
        if (!almacen.Disponible)
        {
            return SinAlmacen();
        }
        var ahora = reloj.GetUtcNow();
        var nombre = NombreArchivo(elegido.Ruta);
        var url = almacen.UrlFirmada(elegido.Clave, opciones.DuracionUrl, nombre);
        await AuditarDescargaAsync(plataforma, Actor(http), ahora, exportacion, elegido, ct);
        return Results.Ok(new DescargaExportacion(url, nombre, elegido.Bytes, ahora + opciones.DuracionUrl));
    }

    /// <summary>La exportación si es de la persona y del lado que pregunta (interno o portal); el predicado de organización y RLS hacen el resto.</summary>
    private static Task<ExportacionContratos?> PropiaAsync(ContratosDbContext contexto, Guid id, ClaimsPrincipal usuario, bool portal, CancellationToken ct)
    {
        var persona = Persona(usuario);
        return contexto.Exportaciones.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.SolicitadaPor == persona && e.DesdePortal == portal, ct);
    }

    internal static ExportacionDto Dto(ExportacionContratos e)
    {
        var archivos = e.Estado == EstadoExportacion.Lista
            ? LeerArchivos(e.Archivos).Select(a => new ArchivoExportacionDto(a.Ruta, NombreArchivo(a.Ruta), a.Formato, a.Bytes)).ToList()
            : [];
        return new ExportacionDto(
            e.Id, Texto(e.Estado), new OrigenExportacionDto(Texto(e.Origen), e.PaqueteId, e.Revision, e.VersionId), e.Nombre, e.Formatos,
            e.SolicitadaEn, e.IniciadaEn, e.TerminadaEn, e.VenceEn, e.HashContenido, archivos,
            e.ErrorCodigo is { } codigo ? new ErrorExportacionDto(codigo, e.Error ?? string.Empty) : null);
    }

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    /// <summary>La organización de un externo; vacío para un empleado (su claim es la organización interna).</summary>
    private static Guid? Organizacion(ClaimsPrincipal usuario) =>
        usuario.FindFirstValue(EndpointsPlataforma.ClaimTipoPrincipal) == EndpointsPlataforma.TipoExterno && Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null;

    private static ActorAuditoria Actor(HttpContext http) => new(
        TipoActor.Persona,
        http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        http.Connection.RemoteIpAddress,
        http.TraceIdentifier);

    private static IResult Error(int estado, string codigo, string mensaje, IReadOnlyList<string>? detalles = null) =>
        Results.Json(new ErrorExportacion(codigo, mensaje, detalles), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult NoExportable(NoExportableException ex) =>
        Error(StatusCodes.Status409Conflict, "no_exportable", ex.Message, [.. ex.Detalles.Take(DetallesMaximos)]);

    private static IResult SinAlmacen() =>
        Error(StatusCodes.Status503ServiceUnavailable, "almacenamiento_no_disponible", "El almacenamiento de exportaciones no está disponible; intenta más tarde.");
}
