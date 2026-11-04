using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;
using static Flit.Atlas.Modulos.Contratos.Guias.EndpointsGuias;

namespace Flit.Atlas.Modulos.Contratos.Paquetes;

/// <summary>
/// Paquetes de contratos con revisiones (HU 4.1, plan 00 §8.1, §8.2 regla 6, §8.4, §8.5 y §8.9, ATLAS-ADR-0041), en
/// <c>/api/v1/contratos/paquetes</c>. Leer exige <c>atlas.contratos.ver_internos</c> (lo mismo que los contratos que
/// selecciona); armar, <c>atlas.paquetes.gestionar</c> global o sobre los aplicativos de <b>todos</b> sus contratos (rol
/// implícito del responsable). Cada guardado crea una revisión inmutable; publicar marca la última como vigente y fijar
/// vuelve a publicar una anterior sin borrar las nuevas. Todo va con su auditoría en la misma transacción. Con un id
/// inexistente, 404 antes que 403.
/// </summary>
internal static class EndpointsPaquetes
{
    public const string RecursoAuditoria = "contratos.paquete";
    public const string AccionCreado = "contratos.paquete_creado";
    public const string AccionRevisado = "contratos.paquete_revisado";
    public const string AccionPublicado = "contratos.paquete_publicado";
    public const string AccionFijado = "contratos.paquete_fijado";
    public const string AccionArchivado = "contratos.paquete_archivado";

    private const int DetallesMaximos = 20;

    // --- DTO ---

    [Description("Una guía compartible dentro de un contrato del paquete.")]
    public sealed record GuiaSolicitud(
        Guid GuiaId,
        [property: Description("Revisión que se comparte: debe ser la publicada (aprobada por el responsable). Vacío = la publicada.")] int? Revision);

    [Description("Un contrato del paquete: su versión estable, las operaciones elegidas y sus guías.")]
    public sealed record EntradaSolicitud(
        Guid ContratoId,
        [property: Description("Versión estable (sin prerelease), vigente o deprecada, del contrato.")] Guid VersionId,
        [property: Description("Claves del índice de esa versión (GET /tramites/{id}, POST webhook:<nombre>, send <canal>…).")] IReadOnlyList<string>? Operaciones,
        [property: Description("Guías compartibles y publicadas del mismo contrato.")] IReadOnlyList<GuiaSolicitud>? Guias);

    [Description("Lo que se guarda en una revisión nueva del paquete.")]
    public sealed record SolicitudPaquete(
        [property: Description("Hasta 200 caracteres.")] string Nombre,
        [property: Description("Hasta 2000 caracteres.")] string? Descripcion,
        [property: Description("Hasta 50 contratos compartibles, uno por contrato. Puede ir vacía en un borrador; publicar exige al menos un contrato con una operación.")] IReadOnlyList<EntradaSolicitud?>? Definicion,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("Al revisar: la última revisión que vio la persona (409 si ya hay otra más nueva). Al crear se ignora.")] int? RevisionBase);

    [Description("Publicar la última revisión del paquete.")]
    public sealed record SolicitudPublicacionPaquete(
        [property: Description("La revisión a publicar: debe ser la última (409 si no). Para volver a una anterior, fíjala.")] int Revision);

    [Description("Guía de un contrato dentro de una revisión del paquete.")]
    public sealed record GuiaEnPaqueteDto(
        Guid GuiaId,
        [property: Description("Título actual de la guía; vacío si ya no existe.")] string? Titulo,
        [property: Description("Revisión que se comparte.")] int Revision);

    [Description("Un contrato dentro de una revisión del paquete, con sus nombres resueltos.")]
    public sealed record EntradaPaqueteDto(
        Guid ContratoId,
        [property: Description("Código del contrato; vacío si ya no existe.")] string? Codigo,
        [property: Description("Nombre del contrato; vacío si ya no existe.")] string? Nombre,
        [property: Description("openapi, asyncapi o proto.")] string? Tipo,
        [property: Description("interna o compartible, hoy.")] string? Visibilidad,
        AplicativoDelContrato? Aplicativo,
        Guid VersionId,
        [property: Description("Versión semántica fijada.")] string? Version,
        [property: Description("vigente, deprecada o retirada, hoy.")] string? EstadoVersion,
        IReadOnlyList<string> Operaciones,
        IReadOnlyList<GuiaEnPaqueteDto> Guias);

    [Description("Revisión inmutable de un paquete, sin la definición.")]
    public sealed record RevisionPaqueteResumen(
        int Numero,
        string Nombre,
        [property: Description("Mensaje del cambio.")] string Nota,
        PersonaRef Autor,
        DateTimeOffset GuardadaEn,
        [property: Description("SHA-256 de la foto (nombre, descripción y definición): igual huella, igual revisión.")] string Huella,
        int Contratos,
        int Operaciones,
        int Guias,
        [property: Description("Si es la revisión vigente (publicada o fijada).")] bool Vigente);

    [Description("Revisión inmutable de un paquete con su definición.")]
    public sealed record RevisionPaqueteDetalle(
        int Numero,
        string Nombre,
        string? Descripcion,
        IReadOnlyList<EntradaPaqueteDto> Definicion,
        string Nota,
        PersonaRef Autor,
        DateTimeOffset GuardadaEn,
        string Huella,
        bool Vigente);

    [Description("Paquete de contratos con la foto de su última revisión.")]
    public sealed record PaqueteResumen(
        Guid Id,
        string Nombre,
        string? Descripcion,
        [property: Description("borrador (nunca publicado), publicado o archivado.")] string Estado,
        [property: Description("Número de la última revisión.")] int RevisionActual,
        [property: Description("Revisión vigente (publicada o fijada); vacío si nunca se publicó.")] int? RevisionPublicada,
        [property: Description("Si hay revisiones más nuevas que la vigente (o nunca se publicó).")] bool CambiosSinPublicar,
        DateTimeOffset? PublicadaEn,
        [property: Description("Quién publicó o fijó la revisión vigente.")] PersonaRef? PublicadaPor,
        [property: Description("Contratos de la última revisión.")] IReadOnlyList<Guid> Contratos,
        [property: Description("Operaciones de la última revisión, sumando todos sus contratos.")] int Operaciones,
        DateTimeOffset ActualizadoEn,
        [property: Description("Si la persona puede revisarlo, publicarlo, fijar una revisión o archivarlo.")] bool PuedeGestionar);

    [Description("Paquetes y si la persona puede crear uno.")]
    public sealed record ListadoPaquetes(
        [property: Description("Si tiene atlas.paquetes.gestionar (global o sobre sus aplicativos).")] bool PuedeCrear,
        IReadOnlyList<PaqueteResumen> Elementos);

    [Description("Un paquete con su última revisión, su historial y lo que la persona puede hacer.")]
    public sealed record PaqueteDetalle(
        PaqueteResumen Paquete,
        [property: Description("La última revisión, con su definición.")] RevisionPaqueteDetalle Revision,
        [property: Description("Todas las revisiones, de la más reciente a la más antigua.")] IReadOnlyList<RevisionPaqueteResumen> Revisiones);

    [Description("Versión que puede ir en un paquete: estable o, donde el ambiente lo permite (QA), candidata rc.")]
    public sealed record VersionElegible(
        Guid Id,
        string Version,
        [property: Description("vigente o deprecada (las retiradas no entran).")] string Estado,
        DateOnly? RetiroEn,
        DateTimeOffset PublicadoEn,
        [property: Description("Operaciones del índice de la versión.")] int Operaciones,
        [property: Description("Cuántas de ellas son x-public o llevan la etiqueta public.")] int Publicas);

    [Description("Guía compartible y publicada que puede ir en un paquete.")]
    public sealed record GuiaElegible(
        Guid Id,
        string Titulo,
        [property: Description("Clave de la operación si la guía es de una; vacío si es del contrato.")] string? Operacion,
        [property: Description("Revisión publicada: la que se comparte.")] int RevisionPublicada,
        [property: Description("Primera versión a la que aplica; vacío = desde la primera.")] string? AplicaDesde,
        [property: Description("Última versión a la que aplica; vacío = hasta la última.")] string? AplicaHasta);

    [Description("Contrato compartible con sus versiones elegibles y sus guías compartibles.")]
    public sealed record ContratoElegible(
        Guid Id,
        string Codigo,
        string Nombre,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        AplicativoDelContrato Aplicativo,
        [property: Description("De la más reciente a la más antigua.")] IReadOnlyList<VersionElegible> Versiones,
        IReadOnlyList<GuiaElegible> Guias);

    [Description("Lo que la persona puede poner en un paquete (pasos 1 y 3 del asistente).")]
    public sealed record ListadoElegibles(IReadOnlyList<ContratoElegible> Elementos);

    [Description("Operaciones de una versión para el paso 2 del asistente.")]
    public sealed record OperacionesParaPaquete(
        Guid ContratoId,
        Guid VersionId,
        string Version,
        [property: Description("Operaciones de la versión, sin filtrar.")] int Total,
        [property: Description("Todas las etiquetas de la versión, ordenadas: las opciones del filtro.")] IReadOnlyList<string> Etiquetas,
        [property: Description("Las que cumplen el filtro, ordenadas por clave.")] IReadOnlyList<OperacionDto> Elementos);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorPaquete(
        [property: Description("no_encontrado (404); sin_permiso (403); revision_desactualizada, sin_cambios, ya_publicada, ya_vigente, es_la_ultima, archivado, no_publicable (409).")] string Codigo,
        string Mensaje,
        [property: Description("Con no_publicable: qué falta o ya no se cumple.")] IReadOnlyList<string>? Detalles);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/paquetes", ListarAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarPaquetesContratos")
            .WithSummary("Paquetes de contratos")
            .WithDescription("Los paquetes (sin archivados, salvo que se pidan), del más reciente al más antiguo, con lo que la persona puede hacer con cada uno. Con contrato, solo los que lo incluyen en su última revisión o en la vigente. Requiere atlas.contratos.ver_internos.")
            .Produces<ListadoPaquetes>()
            .ProducesValidationProblem();

        grupo.MapPost("/paquetes", CrearAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("CrearPaqueteContratos")
            .WithSummary("Crear un paquete")
            .WithDescription("Crea el paquete en borrador con su revisión 1, auditada en la misma transacción. Solo contratos compartibles en versiones estables (vigentes o deprecadas), operaciones de su índice y guías compartibles publicadas del mismo contrato. Requiere atlas.paquetes.gestionar global o sobre el aplicativo de cada contrato (responsable principal o secundario); si no, 403.")
            .Produces<PaqueteDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        grupo.MapGet("/paquetes/elegibles", ElegiblesAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("ListarElegiblesPaquete")
            .WithSummary("Contratos, versiones y guías que pueden ir en un paquete")
            .WithDescription("Pasos 1 y 3 del asistente: los contratos compartibles que la persona puede usar (todos para el gestor global; los de sus aplicativos para el responsable), con sus versiones estables no retiradas y sus guías compartibles publicadas. Requiere atlas.paquetes.gestionar.")
            .Produces<ListadoElegibles>();

        grupo.MapGet("/paquetes/versiones/{versionId:guid}/operaciones", OperacionesAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("ListarOperacionesParaPaquete")
            .WithSummary("Operaciones de una versión, con filtro por etiqueta o x-public")
            .WithDescription("Paso 2 del asistente: el índice de la versión con publica (x-public: true o la etiqueta public) y las etiquetas, filtrado por etiqueta o solo las públicas, más todas las etiquetas de la versión. 404 si la versión no existe; 403 si el contrato no es de un aplicativo de la persona. Requiere atlas.paquetes.gestionar.")
            .Produces<OperacionesParaPaquete>()
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound);

        grupo.MapGet("/paquetes/{paqueteId:guid}", DetalleAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerPaqueteContratos")
            .WithSummary("Un paquete con su última revisión y su historial")
            .WithDescription("El paquete, su última revisión con la definición (nombres resueltos y estado actual de cada versión) y todas sus revisiones sin la definición. 404 si no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<PaqueteDetalle>()
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound);

        grupo.MapGet("/paquetes/{paqueteId:guid}/revisiones/{numero}", RevisionAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerRevisionPaquete")
            .WithSummary("Una revisión de un paquete con su definición")
            .WithDescription("La revisión tal como se guardó (inmutable), con los nombres resueltos. 404 si no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<RevisionPaqueteDetalle>()
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound);

        grupo.MapPost("/paquetes/{paqueteId:guid}/revisiones", RevisarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("RevisarPaqueteContratos")
            .WithSummary("Guardar una revisión nueva")
            .WithDescription("Cada guardado crea una revisión inmutable con autor, fecha y nota; la vigente no cambia hasta publicar. 409 revision_desactualizada si revisionBase no es la última; 409 sin_cambios si es igual a la última; 409 archivado. Requiere atlas.paquetes.gestionar sobre los aplicativos de los contratos de antes y de después.")
            .Produces<PaqueteDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound)
            .Produces<ErrorPaquete>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/paquetes/{paqueteId:guid}/publicacion", PublicarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("PublicarPaqueteContratos")
            .WithSummary("Publicar la última revisión")
            .WithDescription("Marca la última revisión como la vigente, después de revisar otra vez las reglas (contratos compartibles, versiones no retiradas, guías aún compartibles y publicadas, al menos un contrato con una operación): si no se cumplen, 409 no_publicable con el detalle. 409 revision_desactualizada, ya_publicada o archivado. Auditado en la misma transacción.")
            .Produces<PaqueteDetalle>()
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound)
            .Produces<ErrorPaquete>(StatusCodes.Status409Conflict);

        grupo.MapPost("/paquetes/{paqueteId:guid}/revisiones/{numero}/fijacion", FijarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("FijarRevisionPaquete")
            .WithSummary("Fijar una revisión anterior como la vigente")
            .WithDescription("Vuelve a publicar una revisión anterior a la última sin borrar las nuevas («volver» un paquete, plan 00 §8.2 regla 6), con las mismas reglas que publicar (409 no_publicable). 409 es_la_ultima (para la última, publica), ya_vigente o archivado. Requiere atlas.paquetes.gestionar sobre los contratos de la última revisión y de la fijada. Auditado.")
            .Produces<PaqueteDetalle>()
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound)
            .Produces<ErrorPaquete>(StatusCodes.Status409Conflict);

        grupo.MapPost("/paquetes/{paqueteId:guid}/archivo", ArchivarAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("ArchivarPaqueteContratos")
            .WithSummary("Archivar un paquete")
            .WithDescription("El paquete deja de admitir revisiones y publicaciones; su historia queda. 409 archivado si ya lo está. Requiere atlas.paquetes.gestionar sobre los aplicativos de sus contratos. Auditado.")
            .Produces<PaqueteDetalle>()
            .Produces<ErrorPaquete>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPaquete>(StatusCodes.Status404NotFound)
            .Produces<ErrorPaquete>(StatusCodes.Status409Conflict);
    }

    // --- Lectura ---

    private static async Task<IResult> ListarAsync(
        [Description("borrador, publicado o archivado; vacío = borradores y publicados.")] string? estado,
        [Description("Id de un contrato: solo los paquetes que lo incluyen.")] Guid? contrato,
        ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        EstadoPaquete? filtro = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!ReglasPaquetes.Estados.TryGetValue(estado.Trim(), out var e))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = ["Valores válidos: borrador, publicado, archivado."] });
            }
            filtro = e;
        }
        var consulta = contexto.Paquetes.AsNoTracking();
        consulta = filtro is { } f ? consulta.Where(p => p.Estado == f) : consulta.Where(p => p.Estado != EstadoPaquete.Archivado);
        var paquetes = await consulta.OrderByDescending(p => p.ActualizadoEn).ThenByDescending(p => p.Id).ToListAsync(ct);
        var ids = paquetes.Select(p => p.Id).ToList();
        // Las últimas y las vigentes (un paquete tiene a lo sumo dos que importan aquí).
        var revisiones = (await contexto.RevisionesPaquete.AsNoTracking()
                .Where(r => ids.Contains(r.PaqueteId))
                .Join(contexto.Paquetes, r => r.PaqueteId, p => p.Id, (r, p) => new { r, p.RevisionActual, p.RevisionPublicada })
                .Where(x => x.r.Numero == x.RevisionActual || x.r.Numero == x.RevisionPublicada)
                .Select(x => x.r)
                .ToListAsync(ct))
            .ToDictionary(r => (r.PaqueteId, r.Numero));
        var ultimas = paquetes.ToDictionary(p => p.Id, p => FotoPaquete.LeerDefinicion(revisiones[(p.Id, p.RevisionActual)].Definicion));
        if (contrato is { } c)
        {
            paquetes = [.. paquetes.Where(p => ultimas[p.Id].Any(e => e.ContratoId == c)
                || (p.RevisionPublicada is { } n && revisiones.TryGetValue((p.Id, n), out var vigente)
                    && FotoPaquete.LeerDefinicion(vigente.Definicion).Any(e => e.ContratoId == c)))];
        }
        var acceso = await AccesoAsync(usuario, contexto, lector, permisosDeSesion, ultimas.Values.SelectMany(d => d.Select(e => e.ContratoId)), ct);
        var nombres = await NombresAsync(plataforma, paquetes.Select(p => p.PublicadaPor), ct);
        return Results.Ok(new ListadoPaquetes(acceso.Permisos.Tiene(PermisosContratos.PaquetesGestionar),
            [.. paquetes.Select(p => Resumen(p, ultimas[p.Id], nombres, acceso))]));
    }

    private static async Task<IResult> DetalleAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        if (paquete is null)
        {
            return NoEncontrado("El paquete no existe.");
        }
        return Results.Ok(await DetalleDeAsync(paquete, usuario, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    private static async Task<IResult> RevisionAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Número de la revisión.")] int numero,
        ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector, CancellationToken ct)
    {
        var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        var revision = paquete is null ? null : await contexto.RevisionesPaquete.AsNoTracking().FirstOrDefaultAsync(r => r.PaqueteId == paqueteId && r.Numero == numero, ct);
        if (paquete is null || revision is null)
        {
            return NoEncontrado("La revisión no existe.");
        }
        var nombres = await NombresAsync(plataforma, [revision.AutorPersonaId], ct);
        return Results.Ok(await RevisionDetalleAsync(revision, paquete, nombres, contexto, lector, ct));
    }

    private static async Task<IResult> ElegiblesAsync(
        ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion,
        IOptions<OpcionesPaquetes> opcionesPaquetes, CancellationToken ct)
    {
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var contratos = (await contexto.Contratos.AsNoTracking().Where(c => c.Visibilidad == VisibilidadContrato.Compartible).ToListAsync(ct))
            .Where(c => ReglasPaquetes.PuedeUsar(permisos, c.AplicativoId, aplicativos))
            .ToList();
        var ids = contratos.Select(c => c.Id).ToList();
        var aceptadas = opcionesPaquetes.Value.PrereleasesAceptadas.Count > 0;
        var versiones = (await contexto.Versiones.AsNoTracking()
                .Where(v => ids.Contains(v.ContratoId) && (v.EsEstable || aceptadas) && v.Estado != EstadoVersion.Retirada)
                .Select(v => new
                {
                    v.Id, v.ContratoId, v.Version, v.Prerelease, v.Estado, v.RetiroEn, v.PublicadoEn,
                    Operaciones = contexto.Operaciones.Count(o => o.VersionId == v.Id),
                    Publicas = contexto.Operaciones.Count(o => o.VersionId == v.Id && o.Publica),
                })
                .ToListAsync(ct))
            .Where(v => opcionesPaquetes.Value.Acepta(v.Prerelease))
            .ToLookup(v => v.ContratoId);
        var guias = (await contexto.Guias.AsNoTracking()
                .Where(g => ids.Contains(g.ContratoId) && g.Visibilidad == VisibilidadGuia.Compartible && g.RevisionPublicada != null)
                .ToListAsync(ct))
            .ToLookup(g => g.ContratoId);
        var elementos = contratos
            .Where(c => versiones[c.Id].Any())
            .Select(c => new ContratoElegible(c.Id, c.Codigo, c.Nombre, Valores.Texto(c.Tipo), Aplicativo(c.AplicativoId, aplicativos),
                [.. versiones[c.Id].OrderByDescending(v => v.PublicadoEn).ThenByDescending(v => v.Id)
                    .Select(v => new VersionElegible(v.Id, v.Version, Valores.Texto(v.Estado), v.RetiroEn, v.PublicadoEn, v.Operaciones, v.Publicas))],
                [.. guias[c.Id].OrderBy(g => g.Titulo, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Id)
                    .Select(g => new GuiaElegible(g.Id, g.Titulo, g.Operacion, g.RevisionPublicada!.Value, g.AplicaDesde, g.AplicaHasta))]))
            .OrderBy(c => c.Aplicativo.Slug ?? "~", StringComparer.Ordinal).ThenBy(c => c.Codigo, StringComparer.Ordinal)
            .ToList();
        return Results.Ok(new ListadoElegibles(elementos));
    }

    private static async Task<IResult> OperacionesAsync(
        [Description("Id de la versión.")] Guid versionId,
        [Description("Solo las que llevan esta etiqueta (sin distinguir mayúsculas).")] string? etiqueta,
        [Description("Solo las públicas: x-public: true o la etiqueta public.")] bool? soloPublicas,
        ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var version = await contexto.Versiones.AsNoTracking().Where(v => v.Id == versionId)
            .Join(contexto.Contratos, v => v.ContratoId, c => c.Id, (v, c) => new { v.Id, v.ContratoId, v.Version, c.AplicativoId })
            .FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return NoEncontrado("La versión no existe.");
        }
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        if (!ReglasPaquetes.PuedeUsar(permisos, version.AplicativoId, aplicativos))
        {
            return SinPermiso();
        }
        var operaciones = await contexto.Operaciones.AsNoTracking().Where(o => o.VersionId == versionId).ToListAsync(ct);
        var etiquetas = operaciones.SelectMany(o => o.Etiquetas).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var filtradas = ReglasPaquetes.Filtrar(operaciones, etiqueta, soloPublicas == true)
            .OrderBy(o => o.Clave, StringComparer.Ordinal)
            .Select(o => new OperacionDto(o.Clave, o.Clase, o.Metodo, o.Ruta, o.OperationId, o.Resumen, o.Etiquetas, o.Publica))
            .ToList();
        return Results.Ok(new OperacionesParaPaquete(version.ContratoId, version.Id, version.Version, operaciones.Count, etiquetas, filtradas));
    }

    // --- Escritura ---

    private static async Task<IResult> CrearAsync(
        [Description("El paquete y su primera revisión.")] SolicitudPaquete solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, IOptions<OpcionesPaquetes> opcionesPaquetes, TimeProvider reloj, CancellationToken ct)
    {
        var (nombre, descripcion, definicion, nota, errores) = ReglasPaquetes.Leer(solicitud?.Nombre, solicitud?.Descripcion, solicitud?.Definicion, solicitud?.Nota);
        if (definicion is null)
        {
            return Results.ValidationProblem(errores);
        }
        var persona = PersonaDeSesion(http.User);
        var acceso = await AccesoAsync(http.User, contexto, lector, permisosDeSesion, definicion.Select(e => e.ContratoId), ct);
        if (!acceso.PuedeGestionar(definicion.Select(e => e.ContratoId), persona, persona))
        {
            return SinPermiso();
        }
        var (final, problemas) = await ValidarAsync(contexto, definicion, opcionesPaquetes.Value, ct);
        if (final is null)
        {
            return Results.ValidationProblem(problemas);
        }

        var ahora = reloj.GetUtcNow();
        var foto = new FotoPaquete(nombre!, descripcion, final);
        var paquete = new PaqueteContratos { Nombre = foto.Nombre, Descripcion = foto.Descripcion, RevisionActual = 1, CreadoPor = persona };
        contexto.Paquetes.Add(paquete);
        var revision = Revision(paquete.Id, 1, foto, nota!, persona, ahora);
        contexto.RevisionesPaquete.Add(revision);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionCreado, paquete, 1, DatosAuditoria(revision.Numero, null, foto, revision.Huella)), ct);
        return Results.Created($"/api/v1/contratos/paquetes/{paquete.Id}", await DetalleDeAsync(paquete, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    private static async Task<IResult> RevisarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("La revisión nueva.")] SolicitudPaquete solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, IOptions<OpcionesPaquetes> opcionesPaquetes, TimeProvider reloj, CancellationToken ct)
    {
        var paquete = await contexto.Paquetes.FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        if (paquete is null)
        {
            return NoEncontrado("El paquete no existe.");
        }
        var ultima = await contexto.RevisionesPaquete.AsNoTracking().FirstAsync(r => r.PaqueteId == paquete.Id && r.Numero == paquete.RevisionActual, ct);
        var actual = FotoPaquete.De(ultima);
        var (nombre, descripcion, definicion, nota, errores) = ReglasPaquetes.Leer(solicitud?.Nombre, solicitud?.Descripcion, solicitud?.Definicion, solicitud?.Nota);
        var persona = PersonaDeSesion(http.User);
        var nuevos = definicion?.Select(e => e.ContratoId).ToList() ?? [];
        var acceso = await AccesoAsync(http.User, contexto, lector, permisosDeSesion, actual.Definicion.Select(e => e.ContratoId).Concat(nuevos), ct);
        // Antes y después: un responsable no puede quitar ni agregar contratos que no son suyos.
        if (!acceso.PuedeGestionar(actual.Definicion.Select(e => e.ContratoId), paquete.CreadoPor, persona)
            || (nuevos.Count > 0 && !acceso.PuedeGestionar(nuevos, paquete.CreadoPor, persona)))
        {
            return SinPermiso();
        }
        if (definicion is null)
        {
            return Results.ValidationProblem(errores);
        }
        if (paquete.Estado == EstadoPaquete.Archivado)
        {
            return Archivado();
        }
        if (solicitud!.RevisionBase != paquete.RevisionActual)
        {
            return Desactualizada(paquete);
        }
        var (final, problemas) = await ValidarAsync(contexto, definicion, opcionesPaquetes.Value, ct);
        if (final is null)
        {
            return Results.ValidationProblem(problemas);
        }
        var foto = new FotoPaquete(nombre!, descripcion, final);
        if (foto.Huella() == ultima.Huella)
        {
            return Conflicto("sin_cambios", $"No hay cambios frente a la revisión {paquete.RevisionActual}.");
        }

        var ahora = reloj.GetUtcNow();
        var numero = paquete.RevisionActual + 1;
        var revision = Revision(paquete.Id, numero, foto, nota!, persona, ahora);
        paquete.Nombre = foto.Nombre;
        paquete.Descripcion = foto.Descripcion;
        paquete.RevisionActual = numero;
        contexto.RevisionesPaquete.Add(revision);
        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionRevisado, paquete, numero, DatosAuditoria(numero, null, foto, revision.Huella)), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Otro guardado del mismo paquete ganó la carrera por el número.
            return Conflicto("revision_desactualizada", "Alguien guardó otra revisión al mismo tiempo: recarga el paquete y vuelve a intentarlo.");
        }
        return Results.Created($"/api/v1/contratos/paquetes/{paquete.Id}/revisiones/{numero}",
            await DetalleDeAsync(paquete, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    private static async Task<IResult> PublicarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("La revisión a publicar.")] SolicitudPublicacionPaquete solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, IOptions<OpcionesPaquetes> opcionesPaquetes, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, ultima, error) = await PaqueteParaGestionarAsync(paqueteId, null, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (paquete.Estado == EstadoPaquete.Archivado)
        {
            return Archivado();
        }
        if (solicitud?.Revision != paquete.RevisionActual)
        {
            return Desactualizada(paquete);
        }
        if (paquete.RevisionPublicada == paquete.RevisionActual)
        {
            return Conflicto("ya_publicada", $"La revisión {paquete.RevisionActual} ya es la vigente.");
        }
        return await VolverVigenteAsync(http, contexto, plataforma, lector, permisosDeSesion, reloj, paquete, ultima!, AccionPublicado, opcionesPaquetes.Value, ct);
    }

    private static async Task<IResult> FijarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Revisión anterior que vuelve a ser la vigente.")] int numero,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, IOptions<OpcionesPaquetes> opcionesPaquetes, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, objetivo, error) = await PaqueteParaGestionarAsync(paqueteId, numero, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (paquete.Estado == EstadoPaquete.Archivado)
        {
            return Archivado();
        }
        if (numero == paquete.RevisionActual)
        {
            return Conflicto("es_la_ultima", $"La revisión {numero} es la última: publícala en vez de fijarla.");
        }
        if (paquete.RevisionPublicada == numero)
        {
            return Conflicto("ya_vigente", $"La revisión {numero} ya es la vigente.");
        }
        return await VolverVigenteAsync(http, contexto, plataforma, lector, permisosDeSesion, reloj, paquete, objetivo!, AccionFijado, opcionesPaquetes.Value, ct);
    }

    private static async Task<IResult> ArchivarAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, _, error) = await PaqueteParaGestionarAsync(paqueteId, null, http.User, contexto, lector, permisosDeSesion, ct);
        if (paquete is null)
        {
            return error!;
        }
        if (paquete.Estado == EstadoPaquete.Archivado)
        {
            return Archivado();
        }
        var ahora = reloj.GetUtcNow();
        var anterior = ReglasPaquetes.Texto(paquete.Estado);
        paquete.Estado = EstadoPaquete.Archivado;
        paquete.ArchivadoPor = PersonaDeSesion(http.User);
        paquete.ArchivadoEn = ahora;
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionArchivado, paquete, paquete.RevisionActual, new
        {
            revision = paquete.RevisionActual, vigente = paquete.RevisionPublicada, estadoAnterior = anterior,
        }), ct);
        return Results.Ok(await DetalleDeAsync(paquete, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    // --- Piezas comunes ---

    /// <summary>
    /// Publicar o fijar: revisa otra vez las reglas sobre la revisión que queda vigente (lo que valía al guardar puede
    /// haber cambiado: una versión retirada, una guía que volvió a interna) y la marca como vigente.
    /// </summary>
    private static async Task<IResult> VolverVigenteAsync(
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion,
        TimeProvider reloj, PaqueteContratos paquete, RevisionPaquete revision, string accion, OpcionesPaquetes opciones, CancellationToken ct)
    {
        var foto = FotoPaquete.De(revision);
        var problemas = await ProblemasParaPublicarAsync(contexto, foto.Definicion, opciones, ct);
        if (problemas.Count > 0)
        {
            return Results.Json(new ErrorPaquete("no_publicable", $"La revisión {revision.Numero} no se puede publicar: {problemas[0]}",
                [.. problemas.Take(DetallesMaximos)]), statusCode: StatusCodes.Status409Conflict);
        }
        var ahora = reloj.GetUtcNow();
        var anterior = paquete.RevisionPublicada;
        paquete.Estado = EstadoPaquete.Publicado;
        paquete.RevisionPublicada = revision.Numero;
        paquete.PublicadaPor = PersonaDeSesion(http.User);
        paquete.PublicadaEn = ahora;
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, accion, paquete, revision.Numero, DatosAuditoria(revision.Numero, anterior, foto, revision.Huella)), ct);
        return Results.Ok(await DetalleDeAsync(paquete, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    /// <summary>
    /// El paquete (con seguimiento) y su revisión objetivo (la última, o <paramref name="numero"/>) si existen y la persona
    /// puede gestionarlo: sobre los contratos de la última revisión y de la objetivo. Si no, 404 o 403.
    /// </summary>
    internal static async Task<(PaqueteContratos? Paquete, RevisionPaquete? Objetivo, IResult? Error)> PaqueteParaGestionarAsync(
        Guid paqueteId, int? numero, ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var paquete = await contexto.Paquetes.FirstOrDefaultAsync(p => p.Id == paqueteId, ct);
        if (paquete is null)
        {
            return (null, null, NoEncontrado("El paquete no existe."));
        }
        var buscados = numero is { } n ? new[] { paquete.RevisionActual, n } : [paquete.RevisionActual];
        var revisiones = await contexto.RevisionesPaquete.AsNoTracking().Where(r => r.PaqueteId == paqueteId && buscados.Contains(r.Numero)).ToListAsync(ct);
        var objetivo = revisiones.FirstOrDefault(r => r.Numero == (numero ?? paquete.RevisionActual));
        if (objetivo is null)
        {
            return (null, null, NoEncontrado("La revisión no existe."));
        }
        var contratos = revisiones.SelectMany(r => FotoPaquete.LeerDefinicion(r.Definicion)).Select(e => e.ContratoId).Distinct().ToList();
        var acceso = await AccesoAsync(usuario, contexto, lector, permisosDeSesion, contratos, ct);
        return acceso.PuedeGestionar(contratos, paquete.CreadoPor, PersonaDeSesion(usuario))
            ? (paquete, objetivo, null)
            : (null, null, SinPermiso());
    }

    /// <summary>Los permisos de la persona y el aplicativo de cada contrato, para decidir sobre uno o varios paquetes.</summary>
    private sealed class Acceso(
        PermisosEfectivos permisos, Guid persona, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos, IReadOnlyDictionary<Guid, Guid> aplicativoDeContrato)
    {
        public PermisosEfectivos Permisos => permisos;

        public Guid Persona => persona;

        /// <summary>Un contrato que ya no existe cuenta como ajeno para quien no es gestor global.</summary>
        public bool PuedeGestionar(IEnumerable<Guid> contratos, Guid creadoPor, Guid persona)
        {
            var ids = contratos.Distinct().ToList();
            if (ids.Any(c => !aplicativoDeContrato.ContainsKey(c)) && !ReglasPaquetes.GestionaTodo(permisos))
            {
                return false;
            }
            return ReglasPaquetes.PuedeGestionar(permisos, [.. ids.Select(c => aplicativoDeContrato[c]).Distinct()], creadoPor, persona, aplicativos);
        }
    }

    private static async Task<Acceso> AccesoAsync(
        ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion, IEnumerable<Guid> contratos,
        CancellationToken ct)
    {
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var ids = contratos.Distinct().ToList();
        var mapa = await contexto.Contratos.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.AplicativoId, ct);
        return new Acceso(permisos, PersonaDeSesion(usuario), aplicativos, mapa);
    }

    /// <summary>
    /// Las reglas que exige la base al guardar (HU 4.1): contrato compartible, versión estable no retirada del mismo
    /// contrato, operaciones de su índice y guías compartibles del mismo contrato en su revisión publicada, que apliquen a
    /// la versión. Devuelve la definición con las revisiones de guía completas o los errores por campo (400).
    /// </summary>
    private static async Task<(IReadOnlyList<EntradaPaquete>? Definicion, Dictionary<string, string[]> Errores)> ValidarAsync(
        ContratosDbContext contexto, IReadOnlyList<EntradaSolicitada> definicion, OpcionesPaquetes opciones, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var datos = await DatosDeAsync(contexto, definicion.Select(e => (e.ContratoId, e.VersionId, e.Guias.Select(g => (g.GuiaId, g.Revision)))), ct);
        var final = new List<EntradaPaquete>();
        for (var i = 0; i < definicion.Count; i++)
        {
            var e = definicion[i];
            var campo = $"definicion[{i}]";
            var (contrato, version) = (datos.Contratos.GetValueOrDefault(e.ContratoId), datos.Versiones.GetValueOrDefault(e.VersionId));
            var error = ErrorDeContrato(contrato, version, e.ContratoId, opciones);
            if (error is { } x)
            {
                errores[$"{campo}.{x.Campo}"] = [x.Mensaje];
                continue;
            }
            var faltan = e.Operaciones.Where(o => !datos.Operaciones.Contains((e.VersionId, o))).ToList();
            if (faltan.Count > 0)
            {
                errores[$"{campo}.operaciones"] = [$"No están en el índice de la versión {version!.Version}: {Lista(faltan)}."];
            }
            var guias = new List<GuiaEnPaquete>();
            for (var j = 0; j < e.Guias.Count; j++)
            {
                var g = e.Guias[j];
                var guia = datos.Guias.GetValueOrDefault(g.GuiaId);
                var revision = guia?.RevisionPublicada is { } publicada ? g.Revision ?? publicada : g.Revision;
                var mensaje = ErrorDeGuia(guia, revision is { } r ? datos.RevisionesGuia.GetValueOrDefault((g.GuiaId, r)) : null, e.ContratoId, version!, revision, alGuardar: true);
                if (mensaje is not null)
                {
                    errores[$"{campo}.guias[{j}]"] = [mensaje];
                    continue;
                }
                guias.Add(new GuiaEnPaquete(g.GuiaId, revision!.Value));
            }
            final.Add(new EntradaPaquete(e.ContratoId, e.VersionId, e.Operaciones, guias));
        }
        return errores.Count > 0 ? (null, errores) : (final, errores);
    }

    /// <summary>Lo que impide publicar (o fijar) una revisión hoy, en frases; vacío si se puede.</summary>
    internal static async Task<List<string>> ProblemasParaPublicarAsync(
        ContratosDbContext contexto, IReadOnlyList<EntradaPaquete> definicion, OpcionesPaquetes opciones, CancellationToken ct)
    {
        var problemas = new List<string>();
        if (!ReglasPaquetes.TieneAlgoQueCompartir(definicion))
        {
            problemas.Add("debe tener al menos un contrato con una operación.");
        }
        var datos = await DatosDeAsync(contexto, definicion.Select(e => (e.ContratoId, e.VersionId, e.Guias.Select(g => (g.GuiaId, (int?)g.Revision)))), ct);
        foreach (var e in definicion)
        {
            var (contrato, version) = (datos.Contratos.GetValueOrDefault(e.ContratoId), datos.Versiones.GetValueOrDefault(e.VersionId));
            var nombre = contrato?.Codigo ?? e.ContratoId.ToString();
            if (ErrorDeContrato(contrato, version, e.ContratoId, opciones) is { } x)
            {
                problemas.Add($"{nombre}: {Minuscula(x.Mensaje)}");
                continue;
            }
            var faltan = e.Operaciones.Where(o => !datos.Operaciones.Contains((e.VersionId, o))).ToList();
            if (faltan.Count > 0)
            {
                problemas.Add($"{nombre}: no están en el índice de la versión {version!.Version}: {Lista(faltan)}.");
            }
            foreach (var g in e.Guias)
            {
                var guia = datos.Guias.GetValueOrDefault(g.GuiaId);
                if (ErrorDeGuia(guia, datos.RevisionesGuia.GetValueOrDefault((g.GuiaId, g.Revision)), e.ContratoId, version!, g.Revision, alGuardar: false) is { } m)
                {
                    problemas.Add($"{nombre}, guía «{guia?.Titulo ?? g.GuiaId.ToString()}»: {Minuscula(m)}");
                }
            }
        }
        return problemas;
    }

    private static (string Campo, string Mensaje)? ErrorDeContrato(
        Contrato? contrato, VersionContrato? version, Guid contratoId, OpcionesPaquetes opciones)
    {
        if (contrato is null)
        {
            return ("contratoId", "El contrato no existe.");
        }
        if (contrato.Visibilidad != VisibilidadContrato.Compartible)
        {
            return ("contratoId", "El contrato es interno: solo los compartibles (info.x-flit-visibilidad: compartible en su última versión estable) entran a paquetes.");
        }
        if (version is null || version.ContratoId != contratoId)
        {
            return ("versionId", "La versión no es de este contrato.");
        }
        if (!opciones.Acepta(version.Prerelease))
        {
            return ("versionId", $"La versión {version.Version} es una prerelease: solo las estables van en paquetes de clientes.");
        }
        if (version.Estado == EstadoVersion.Retirada)
        {
            return ("versionId", $"La versión {version.Version} está retirada.");
        }
        return null;
    }

    /// <summary>
    /// Una guía va en un paquete si es del mismo contrato, es compartible hoy y tiene una revisión publicada (la aprobó el
    /// responsable, ATLAS-ADR-0040), y la revisión que se comparte es compartible y aplica a la versión. Al guardar, esa
    /// revisión debe ser la publicada; al publicar o fijar después, basta con que la guía siga compartible y publicada.
    /// </summary>
    private static string? ErrorDeGuia(GuiaContrato? guia, RevisionGuia? revision, Guid contratoId, VersionContrato version, int? numero, bool alGuardar)
    {
        if (guia is null || guia.ContratoId != contratoId)
        {
            return "La guía no es de este contrato.";
        }
        if (guia.Visibilidad != VisibilidadGuia.Compartible)
        {
            return "La guía es interna: solo las compartibles con clientes entran a paquetes.";
        }
        if (guia.RevisionPublicada is not { } publicada)
        {
            return "La guía no tiene una revisión publicada (aprobada por el responsable).";
        }
        if (alGuardar && numero != publicada)
        {
            return $"Solo entra la revisión publicada de la guía (la {publicada}).";
        }
        if (revision is null || revision.Visibilidad != VisibilidadGuia.Compartible)
        {
            return $"La revisión {numero} de la guía no es compartible.";
        }
        var semver = VersionSemantica.Leer(version.Version);
        if (semver is not null && !ReglasGuias.AplicaA(revision.AplicaDesde, revision.AplicaHasta, semver))
        {
            return $"La guía no aplica a la versión {version.Version}.";
        }
        return null;
    }

    private sealed record DatosDefinicion(
        IReadOnlyDictionary<Guid, Contrato> Contratos,
        IReadOnlyDictionary<Guid, VersionContrato> Versiones,
        IReadOnlySet<(Guid VersionId, string Clave)> Operaciones,
        IReadOnlyDictionary<Guid, GuiaContrato> Guias,
        IReadOnlyDictionary<(Guid GuiaId, int Numero), RevisionGuia> RevisionesGuia);

    /// <summary>Lo que hace falta de la base para validar una definición, en unas pocas consultas (sin los specs).</summary>
    private static async Task<DatosDefinicion> DatosDeAsync(
        ContratosDbContext contexto, IEnumerable<(Guid ContratoId, Guid VersionId, IEnumerable<(Guid GuiaId, int? Revision)> Guias)> definicion, CancellationToken ct)
    {
        var lista = definicion.Select(e => (e.ContratoId, e.VersionId, Guias: e.Guias.ToList())).ToList();
        var idsContratos = lista.Select(e => e.ContratoId).Distinct().ToList();
        var idsVersiones = lista.Select(e => e.VersionId).Distinct().ToList();
        var idsGuias = lista.SelectMany(e => e.Guias.Select(g => g.GuiaId)).Distinct().ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => idsContratos.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        // Sin el contenido: el spec puede pesar megas.
        var versiones = (await contexto.Versiones.AsNoTracking().Where(v => idsVersiones.Contains(v.Id))
                .Select(v => new VersionContrato
                {
                    Id = v.Id, ContratoId = v.ContratoId, Version = v.Version, Mayor = v.Mayor, Menor = v.Menor, Parche = v.Parche,
                    Prerelease = v.Prerelease, EsEstable = v.EsEstable, Sha256 = v.Sha256, Formato = v.Formato, Contenido = string.Empty, Tamano = v.Tamano,
                    Repositorio = v.Repositorio, Rama = v.Rama, CommitSha = v.CommitSha, WorkflowDelJob = v.WorkflowDelJob, PublicadoPor = v.PublicadoPor,
                    PublicadoEn = v.PublicadoEn, Estado = v.Estado, RetiroEn = v.RetiroEn,
                })
                .ToListAsync(ct))
            .ToDictionary(v => v.Id);
        var operaciones = (await contexto.Operaciones.AsNoTracking().Where(o => idsVersiones.Contains(o.VersionId)).Select(o => new { o.VersionId, o.Clave }).ToListAsync(ct))
            .Select(o => (o.VersionId, o.Clave)).ToHashSet();
        var guias = await contexto.Guias.AsNoTracking().Where(g => idsGuias.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        // Las revisiones pedidas y las publicadas (las que se completan cuando la solicitud no trae número).
        var numeros = lista.SelectMany(e => e.Guias.Select(g => g.Revision ?? guias.GetValueOrDefault(g.GuiaId)?.RevisionPublicada)).OfType<int>().Distinct().ToList();
        var revisiones = (await contexto.RevisionesGuia.AsNoTracking()
                .Where(r => idsGuias.Contains(r.GuiaId) && numeros.Contains(r.Numero))
                .Select(r => new RevisionGuia
                {
                    Id = r.Id, GuiaId = r.GuiaId, Numero = r.Numero, Origen = r.Origen, RestauradaDe = r.RestauradaDe, Titulo = r.Titulo,
                    Contenido = string.Empty, Operacion = r.Operacion, AplicaDesde = r.AplicaDesde, AplicaHasta = r.AplicaHasta, Visibilidad = r.Visibilidad,
                    Nota = string.Empty, AutorPersonaId = r.AutorPersonaId, GuardadaEn = r.GuardadaEn, Huella = r.Huella,
                })
                .ToListAsync(ct))
            .ToDictionary(r => (r.GuiaId, r.Numero));
        return new DatosDefinicion(contratos, versiones, operaciones, guias, revisiones);
    }

    private static string Lista(List<string> claves) =>
        string.Join(", ", claves.Take(5)) + (claves.Count > 5 ? $" y {claves.Count - 5} más" : string.Empty);

    private static string Minuscula(string texto) => texto.Length == 0 ? texto : char.ToLower(texto[0], CultureInfo.CurrentCulture) + texto[1..];

    private static RevisionPaquete Revision(Guid paqueteId, int numero, FotoPaquete foto, string nota, Guid autor, DateTimeOffset ahora) => new()
    {
        PaqueteId = paqueteId,
        Numero = numero,
        Nombre = foto.Nombre,
        Descripcion = foto.Descripcion,
        Definicion = foto.DefinicionJson(),
        Nota = nota,
        AutorPersonaId = autor,
        GuardadaEn = ahora,
        Huella = foto.Huella(),
    };

    private static async Task<PaqueteDetalle> DetalleDeAsync(
        PaqueteContratos paquete, ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var revisiones = await contexto.RevisionesPaquete.AsNoTracking().Where(r => r.PaqueteId == paquete.Id).OrderByDescending(r => r.Numero).ToListAsync(ct);
        var ultima = revisiones.First(r => r.Numero == paquete.RevisionActual);
        var definicion = FotoPaquete.LeerDefinicion(ultima.Definicion);
        var acceso = await AccesoAsync(usuario, contexto, lector, permisosDeSesion, definicion.Select(e => e.ContratoId), ct);
        var nombres = await NombresAsync(plataforma, revisiones.Select(r => (Guid?)r.AutorPersonaId).Append(paquete.PublicadaPor), ct);
        return new PaqueteDetalle(
            Resumen(paquete, definicion, nombres, acceso),
            await RevisionDetalleAsync(ultima, paquete, nombres, contexto, lector, ct),
            [.. revisiones.Select(r =>
            {
                var d = FotoPaquete.LeerDefinicion(r.Definicion);
                return new RevisionPaqueteResumen(r.Numero, r.Nombre, r.Nota, Persona(r.AutorPersonaId, nombres), r.GuardadaEn, r.Huella, d.Count,
                    d.Sum(e => e.Operaciones.Count), d.Sum(e => e.Guias.Count), r.Numero == paquete.RevisionPublicada);
            })]);
    }

    private static async Task<RevisionPaqueteDetalle> RevisionDetalleAsync(
        RevisionPaquete revision, PaqueteContratos paquete, IReadOnlyDictionary<Guid, string> nombres, ContratosDbContext contexto, ILectorAplicativos lector,
        CancellationToken ct)
    {
        var definicion = FotoPaquete.LeerDefinicion(revision.Definicion);
        var datos = await DatosDeAsync(contexto, definicion.Select(e => (e.ContratoId, e.VersionId, e.Guias.Select(g => (g.GuiaId, (int?)g.Revision)))), ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var entradas = definicion.Select(e =>
        {
            var c = datos.Contratos.GetValueOrDefault(e.ContratoId);
            var v = datos.Versiones.GetValueOrDefault(e.VersionId);
            return new EntradaPaqueteDto(e.ContratoId, c?.Codigo, c?.Nombre, c is null ? null : Valores.Texto(c.Tipo), c is null ? null : Valores.Texto(c.Visibilidad),
                c is null ? null : Aplicativo(c.AplicativoId, aplicativos), e.VersionId, v?.Version, v is null ? null : Valores.Texto(v.Estado), e.Operaciones,
                [.. e.Guias.Select(g => new GuiaEnPaqueteDto(g.GuiaId, datos.Guias.GetValueOrDefault(g.GuiaId)?.Titulo, g.Revision))]);
        }).ToList();
        return new RevisionPaqueteDetalle(revision.Numero, revision.Nombre, revision.Descripcion, entradas, revision.Nota,
            Persona(revision.AutorPersonaId, nombres), revision.GuardadaEn, revision.Huella, revision.Numero == paquete.RevisionPublicada);
    }

    private static PaqueteResumen Resumen(PaqueteContratos p, IReadOnlyList<EntradaPaquete> ultima, IReadOnlyDictionary<Guid, string> nombres, Acceso acceso) => new(
        p.Id, p.Nombre, p.Descripcion, ReglasPaquetes.Texto(p.Estado), p.RevisionActual, p.RevisionPublicada,
        p.RevisionPublicada != p.RevisionActual, p.PublicadaEn, p.PublicadaPor is { } por ? Persona(por, nombres) : null,
        [.. ultima.Select(e => e.ContratoId)], ultima.Sum(e => e.Operaciones.Count), p.ActualizadoEn,
        p.Estado != EstadoPaquete.Archivado && acceso.Permisos.Tiene(PermisosContratos.PaquetesGestionar)
            && acceso.PuedeGestionar(ultima.Select(e => e.ContratoId), p.CreadoPor, acceso.Persona));

    private static AplicativoDelContrato Aplicativo(Guid id, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        var a = aplicativos.GetValueOrDefault(id);
        return new AplicativoDelContrato(id, a?.Slug, a?.Nombre);
    }

    private static PersonaRef Persona(Guid id, IReadOnlyDictionary<Guid, string> nombres) => new(id, nombres.GetValueOrDefault(id) ?? "Persona no disponible");

    private static object DatosAuditoria(int revision, int? anterior, FotoPaquete foto, string huella) => new
    {
        revision,
        anterior,
        contratos = foto.Definicion.Select(e => e.ContratoId).ToList(),
        versiones = foto.Definicion.Select(e => e.VersionId).ToList(),
        operaciones = foto.Definicion.Sum(e => e.Operaciones.Count),
        guias = foto.Definicion.Sum(e => e.Guias.Count),
        huella,
    };

    /// <summary>Auditoría de una acción sobre un paquete: ids, números, conteos y huella; nunca el nombre, la descripción ni la nota.</summary>
    private static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, PaqueteContratos paquete, int revision, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = paquete.Id.ToString(),
        Version = revision.ToString(CultureInfo.InvariantCulture),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorPaquete(codigo, mensaje, null), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo el líder técnico o el responsable de los aplicativos de todos sus contratos arman este paquete (atlas.paquetes.gestionar).");

    private static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    private static IResult Archivado() => Conflicto("archivado", "El paquete está archivado: ya no admite revisiones ni publicaciones.");

    private static IResult Desactualizada(PaqueteContratos paquete) =>
        Conflicto("revision_desactualizada", $"El paquete ya va en la revisión {paquete.RevisionActual}: recárgalo y vuelve a intentarlo.");
}
