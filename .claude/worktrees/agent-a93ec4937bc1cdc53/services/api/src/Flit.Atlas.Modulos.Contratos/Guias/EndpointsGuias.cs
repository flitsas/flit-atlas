using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
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
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Contratos.Guias;

/// <summary>
/// Guías de integración con revisiones (HU 3.7, plan 00 §8.2 regla 6 y §8.5, ATLAS-ADR-0040), en
/// <c>/api/v1/contratos</c>. Ver exige <c>atlas.contratos.ver_internos</c>; escribir, <c>atlas.contratos.guias.editar</c>
/// global o sobre el aplicativo del contrato (o un ancestro: rol implícito del responsable). Cada guardado, restauración
/// y publicación va con su auditoría en la misma transacción. Publicar una guía compartible es aprobarla: solo el
/// responsable principal o secundario (§7.2). Respuestas con id inexistente: 404 antes que 403, para no revelar nada.
/// </summary>
internal static class EndpointsGuias
{
    public const string RecursoAuditoria = "contratos.guia";
    public const string AccionCreada = "contratos.guia_creada";
    public const string AccionRevisada = "contratos.guia_revisada";
    public const string AccionRestaurada = "contratos.guia_restaurada";
    public const string AccionPublicada = "contratos.guia_publicada";

    [Description("Persona de Atlas (autor o aprobador).")]
    public sealed record PersonaRef(Guid Id, [property: Description("Nombre; «Persona no disponible» si ya no está.")] string Nombre);

    [Description("Guía de integración con la foto de su última revisión.")]
    public sealed record GuiaResumen(
        Guid Id,
        Guid ContratoId,
        string Titulo,
        [property: Description("Clave del índice de operaciones (GET /tramites/{id}) si es de una operación; vacío si es del contrato.")] string? Operacion,
        [property: Description("Primera versión del contrato a la que aplica; vacío = desde la primera.")] string? AplicaDesde,
        [property: Description("Última versión a la que aplica; vacío = hasta la última. Sin las dos, aplica a todas.")] string? AplicaHasta,
        [property: Description("interna o compartible (con clientes, por paquetes en F4).")] string Visibilidad,
        [property: Description("borrador (la última revisión no está publicada) o publicada.")] string Estado,
        [property: Description("Número de la última revisión.")] int RevisionActual,
        [property: Description("Revisión publicada (la que verán los clientes si es compartible); vacío si nunca se publicó.")] int? RevisionPublicada,
        DateTimeOffset? PublicadaEn,
        [property: Description("Quién publicó la revisión publicada.")] PersonaRef? PublicadaPor,
        [property: Description("Principal o secundario que aprobó la revisión publicada, si es compartible.")] PersonaRef? AprobadaPor,
        DateTimeOffset ActualizadaEn);

    [Description("Guías de un contrato y lo que la persona puede hacer con ellas.")]
    public sealed record ListadoGuias(
        Guid ContratoId,
        [property: Description("Si puede crear, editar y restaurar guías de este contrato (y publicar las internas).")] bool PuedeEditar,
        [property: Description("Si puede aprobar (publicar) las compartibles: solo el responsable principal o secundario del aplicativo.")] bool PuedeAprobar,
        IReadOnlyList<GuiaResumen> Elementos);

    [Description("Revisión inmutable de una guía, sin el cuerpo.")]
    public sealed record RevisionResumen(
        int Numero,
        [property: Description("creacion, edicion o restauracion.")] string Origen,
        [property: Description("Si es una restauración, la revisión restaurada (esta es igual a aquella).")] int? RestauradaDe,
        string Titulo,
        [property: Description("interna o compartible.")] string Visibilidad,
        [property: Description("Mensaje del cambio.")] string Nota,
        PersonaRef Autor,
        DateTimeOffset GuardadaEn,
        [property: Description("SHA-256 de la foto (título, cuerpo, operación, versiones y visibilidad): igual huella, igual revisión.")] string Huella,
        [property: Description("Si es la revisión publicada.")] bool Publicada);

    [Description("Revisión inmutable de una guía con su cuerpo en Markdown.")]
    public sealed record RevisionDetalle(
        int Numero,
        [property: Description("creacion, edicion o restauracion.")] string Origen,
        int? RestauradaDe,
        string Titulo,
        [property: Description("Cuerpo en Markdown, tal como se guardó (la PWA lo muestra sanitizado, sin HTML).")] string Contenido,
        string? Operacion,
        string? AplicaDesde,
        string? AplicaHasta,
        [property: Description("interna o compartible.")] string Visibilidad,
        string Nota,
        PersonaRef Autor,
        DateTimeOffset GuardadaEn,
        string Huella,
        bool Publicada);

    [Description("Una guía con su última revisión y lo que la persona puede hacer.")]
    public sealed record GuiaDetalle(
        GuiaResumen Guia,
        [property: Description("La última revisión, con su cuerpo.")] RevisionDetalle Revision,
        bool PuedeEditar,
        bool PuedeAprobar);

    [Description("Lo que se guarda en una revisión nueva.")]
    public sealed record SolicitudGuia(
        [property: Description("Hasta 200 caracteres.")] string Titulo,
        [property: Description("Cuerpo en Markdown, hasta 200 mil caracteres. El HTML no se interpreta.")] string Contenido,
        [property: Description("Clave de una operación del índice de alguna versión del contrato; vacío si la guía es de todo el contrato.")] string? Operacion,
        [property: Description("Versión semántica inicial; vacío = desde la primera.")] string? AplicaDesde,
        [property: Description("Versión semántica final; vacío = hasta la última.")] string? AplicaHasta,
        [property: Description("interna o compartible. Publicar una compartible exige la aprobación del principal o del secundario.")] string Visibilidad,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("Al editar: la revisión sobre la que se trabajó (409 si ya hay otra más nueva). Al crear se ignora.")] int? RevisionBase);

    [Description("Restaurar una revisión: crea otra igual a ella.")]
    public sealed record SolicitudRestauracion(
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("La última revisión que vio la persona (409 si ya hay otra más nueva).")] int RevisionBase);

    [Description("Publicar la última revisión de una guía.")]
    public sealed record SolicitudPublicacionGuia(
        [property: Description("La revisión a publicar: debe ser la última (409 si no).")] int Revision);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorGuia(
        [property: Description("no_encontrado (404); sin_permiso, aprobacion_requerida (403); revision_desactualizada, sin_cambios, ya_publicada (409).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/{id:guid}/guias", ListarAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarGuiasContrato")
            .WithSummary("Guías de un contrato")
            .WithDescription("Las guías del contrato (sin los cuerpos), ordenadas por título, con lo que la persona puede hacer. Con version, solo las que aplican a esa versión. 404 si el contrato no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<ListadoGuias>()
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapPost("/{id:guid}/guias", CrearAsync)
            .RequierePermiso(PermisosContratos.GuiasEditar)
            .WithName("CrearGuiaContrato")
            .WithSummary("Crear una guía")
            .WithDescription("Crea la guía en borrador con su revisión 1, auditada en la misma transacción. Requiere atlas.contratos.guias.editar global o sobre el aplicativo del contrato (responsable principal o secundario); si no, 403. 404 si el contrato no existe.")
            .Produces<GuiaDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorGuia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapGet("/guias/{guiaId:guid}", DetalleAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerGuiaContrato")
            .WithSummary("Una guía con su última revisión")
            .WithDescription("La guía, su última revisión con el cuerpo en Markdown y lo que la persona puede hacer. 404 si no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<GuiaDetalle>()
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound);

        grupo.MapPut("/guias/{guiaId:guid}", GuardarAsync)
            .RequierePermiso(PermisosContratos.GuiasEditar)
            .WithName("GuardarGuiaContrato")
            .WithSummary("Guardar una guía (revisión nueva)")
            .WithDescription("Cada guardado crea una revisión inmutable con autor, fecha y nota, y deja la guía en borrador (la revisión publicada sigue siendo la misma hasta volver a publicar). 409 revision_desactualizada si revisionBase no es la última; 409 sin_cambios si es igual a la última. Requiere atlas.contratos.guias.editar sobre el aplicativo.")
            .Produces<GuiaDetalle>()
            .Produces<ErrorGuia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGuia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/guias/{guiaId:guid}/revisiones", RevisionesAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarRevisionesGuia")
            .WithSummary("Historial de revisiones de una guía")
            .WithDescription("Todas las revisiones, de la más reciente a la más antigua, sin los cuerpos. 404 si la guía no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<List<RevisionResumen>>()
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound);

        grupo.MapGet("/guias/{guiaId:guid}/revisiones/{numero}", RevisionAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerRevisionGuia")
            .WithSummary("Una revisión con su cuerpo")
            .WithDescription("La revisión tal como se guardó: la PWA compara dos revisiones línea a línea con estos cuerpos. 404 si no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<RevisionDetalle>()
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound);

        grupo.MapPost("/guias/{guiaId:guid}/revisiones/{numero}/restauracion", RestaurarAsync)
            .RequierePermiso(PermisosContratos.GuiasEditar)
            .WithName("RestaurarRevisionGuia")
            .WithSummary("Restaurar una revisión")
            .WithDescription("Crea una revisión nueva igual a la elegida (misma huella), sin reescribir la historia, y deja la guía en borrador. 409 revision_desactualizada si revisionBase no es la última; 409 sin_cambios si la última ya es igual. Requiere atlas.contratos.guias.editar sobre el aplicativo.")
            .Produces<GuiaDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorGuia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGuia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/guias/{guiaId:guid}/publicacion", PublicarAsync)
            .RequierePermiso(PermisosContratos.GuiasEditar)
            .WithName("PublicarGuiaContrato")
            .WithSummary("Publicar (y, si es compartible, aprobar) la última revisión")
            .WithDescription("Publica la última revisión. Una guía interna la publica quien puede editarla; una compartible solo el responsable principal o secundario del aplicativo (o de un ancestro): para los demás, 403 aprobacion_requerida. 409 revision_desactualizada si revision no es la última; 409 ya_publicada. Auditado en la misma transacción.")
            .Produces<GuiaDetalle>()
            .Produces<ErrorGuia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGuia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGuia>(StatusCodes.Status409Conflict);
    }

    // --- Lectura ---

    private static async Task<IResult> ListarAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("Versión del contrato: solo las guías que le aplican.")] string? version,
        ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        VersionSemantica? filtro = null;
        if (!string.IsNullOrWhiteSpace(version))
        {
            filtro = VersionSemantica.Leer(version);
            if (filtro is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Debe ser una versión semántica."] });
            }
        }
        var aplicativoId = await contexto.Contratos.Where(c => c.Id == id).Select(c => (Guid?)c.AplicativoId).FirstOrDefaultAsync(ct);
        if (aplicativoId is null)
        {
            return NoEncontrado("El contrato no existe.");
        }
        var (editar, aprobar) = await PermisosAsync(usuario, aplicativoId.Value, lector, permisosDeSesion, ct);
        var guias = (await contexto.Guias.AsNoTracking().Where(g => g.ContratoId == id).ToListAsync(ct))
            .Where(g => filtro is null || ReglasGuias.AplicaA(g.AplicaDesde, g.AplicaHasta, filtro))
            .OrderBy(g => g.Titulo, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Id)
            .ToList();
        var nombres = await NombresAsync(plataforma, guias.SelectMany(g => new[] { g.PublicadaPor, g.AprobadaPor }), ct);
        return Results.Ok(new ListadoGuias(id, editar, aprobar, [.. guias.Select(g => Resumen(g, nombres))]));
    }

    private static async Task<IResult> DetalleAsync(
        [Description("Id de la guía.")] Guid guiaId,
        ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var guia = await contexto.Guias.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guiaId, ct);
        if (guia is null)
        {
            return NoEncontrado("La guía no existe.");
        }
        return Results.Ok(await DetalleDeAsync(guia, usuario, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    private static async Task<IResult> RevisionesAsync(
        [Description("Id de la guía.")] Guid guiaId, ContratosDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var guia = await contexto.Guias.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guiaId, ct);
        if (guia is null)
        {
            return NoEncontrado("La guía no existe.");
        }
        var revisiones = await contexto.RevisionesGuia.AsNoTracking().Where(r => r.GuiaId == guiaId).OrderByDescending(r => r.Numero)
            .Select(r => new { r.Numero, r.Origen, r.RestauradaDe, r.Titulo, r.Visibilidad, r.Nota, r.AutorPersonaId, r.GuardadaEn, r.Huella })
            .ToListAsync(ct);
        var nombres = await NombresAsync(plataforma, revisiones.Select(r => (Guid?)r.AutorPersonaId), ct);
        return Results.Ok(revisiones.Select(r => new RevisionResumen(r.Numero, ReglasGuias.Texto(r.Origen), r.RestauradaDe, r.Titulo,
            ReglasGuias.Texto(r.Visibilidad), r.Nota, Persona(r.AutorPersonaId, nombres), r.GuardadaEn, r.Huella, r.Numero == guia.RevisionPublicada)).ToList());
    }

    private static async Task<IResult> RevisionAsync(
        [Description("Id de la guía.")] Guid guiaId,
        [Description("Número de la revisión.")] int numero,
        ContratosDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var guia = await contexto.Guias.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guiaId, ct);
        var revision = guia is null ? null : await contexto.RevisionesGuia.AsNoTracking().FirstOrDefaultAsync(r => r.GuiaId == guiaId && r.Numero == numero, ct);
        if (guia is null || revision is null)
        {
            return NoEncontrado("La revisión no existe.");
        }
        var nombres = await NombresAsync(plataforma, [revision.AutorPersonaId], ct);
        return Results.Ok(Detalle(revision, guia, nombres));
    }

    // --- Escritura ---

    private static async Task<IResult> CrearAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("La guía y su primera revisión.")] SolicitudGuia solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var aplicativoId = await contexto.Contratos.Where(c => c.Id == id).Select(c => (Guid?)c.AplicativoId).FirstOrDefaultAsync(ct);
        if (aplicativoId is null)
        {
            return NoEncontrado("El contrato no existe.");
        }
        if (!(await PermisosAsync(http.User, aplicativoId.Value, lector, permisosDeSesion, ct)).Editar)
        {
            return SinPermiso();
        }
        var (foto, nota, errores) = await LeerAsync(contexto, id, solicitud, ct);
        if (foto is null)
        {
            return Results.ValidationProblem(errores);
        }

        var ahora = reloj.GetUtcNow();
        var autor = PersonaDeSesion(http.User);
        var guia = new GuiaContrato { ContratoId = id, Titulo = foto.Titulo };
        Aplicar(guia, foto, 1);
        contexto.Guias.Add(guia);
        var revision = Revision(guia.Id, 1, OrigenRevision.Creacion, null, foto, nota!, autor, ahora);
        contexto.RevisionesGuia.Add(revision);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionCreada, guia, revision.Numero, new
        {
            contrato = id, revision = 1, visibilidad = ReglasGuias.Texto(foto.Visibilidad), huella = revision.Huella,
        }), ct);
        return Results.Created($"/api/v1/contratos/guias/{guia.Id}", await DetalleDeAsync(guia, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    private static async Task<IResult> GuardarAsync(
        [Description("Id de la guía.")] Guid guiaId,
        [Description("La revisión nueva.")] SolicitudGuia solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var (guia, error) = await GuiaParaEscribirAsync(guiaId, http.User, contexto, lector, permisosDeSesion, ct);
        if (guia is null)
        {
            return error!;
        }
        var (foto, nota, errores) = await LeerAsync(contexto, guia.ContratoId, solicitud, ct);
        if (foto is null)
        {
            return Results.ValidationProblem(errores);
        }
        if (solicitud.RevisionBase != guia.RevisionActual)
        {
            return Desactualizada(guia);
        }
        var ultima = await contexto.RevisionesGuia.AsNoTracking().Where(r => r.GuiaId == guia.Id && r.Numero == guia.RevisionActual)
            .Select(r => r.Huella).FirstAsync(ct);
        if (ultima == foto.Huella())
        {
            return Conflicto("sin_cambios", $"No hay cambios frente a la revisión {guia.RevisionActual}.");
        }
        return await NuevaRevisionAsync(http, contexto, plataforma, lector, permisosDeSesion, reloj, guia, foto, nota!, OrigenRevision.Edicion, null,
            AccionRevisada, created: false, ct);
    }

    private static async Task<IResult> RestaurarAsync(
        [Description("Id de la guía.")] Guid guiaId,
        [Description("Revisión a restaurar.")] int numero,
        [Description("La nota y la última revisión que se vio.")] SolicitudRestauracion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var (guia, error) = await GuiaParaEscribirAsync(guiaId, http.User, contexto, lector, permisosDeSesion, ct);
        if (guia is null)
        {
            return error!;
        }
        var origen = await contexto.RevisionesGuia.AsNoTracking().FirstOrDefaultAsync(r => r.GuiaId == guia.Id && r.Numero == numero, ct);
        if (origen is null)
        {
            return NoEncontrado("La revisión no existe.");
        }
        var nota = (solicitud.Nota ?? string.Empty).Trim();
        if (nota.Length is 0 or > ContratosDbContext.LargoNotaGuia)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["nota"] = [$"La nota del cambio es obligatoria y va hasta {ContratosDbContext.LargoNotaGuia} caracteres."],
            });
        }
        if (solicitud.RevisionBase != guia.RevisionActual)
        {
            return Desactualizada(guia);
        }
        var foto = FotoGuia.De(origen);
        var ultima = await contexto.RevisionesGuia.AsNoTracking().Where(r => r.GuiaId == guia.Id && r.Numero == guia.RevisionActual)
            .Select(r => r.Huella).FirstAsync(ct);
        if (ultima == foto.Huella())
        {
            return Conflicto("sin_cambios", $"La revisión {guia.RevisionActual} ya es igual a la {numero}.");
        }
        return await NuevaRevisionAsync(http, contexto, plataforma, lector, permisosDeSesion, reloj, guia, foto, nota, OrigenRevision.Restauracion, numero,
            AccionRestaurada, created: true, ct);
    }

    private static async Task<IResult> PublicarAsync(
        [Description("Id de la guía.")] Guid guiaId,
        [Description("La revisión a publicar.")] SolicitudPublicacionGuia solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var guia = await contexto.Guias.FirstOrDefaultAsync(g => g.Id == guiaId, ct);
        var aplicativoId = guia is null ? null : await contexto.Contratos.Where(c => c.Id == guia.ContratoId).Select(c => (Guid?)c.AplicativoId).FirstOrDefaultAsync(ct);
        if (guia is null || aplicativoId is null)
        {
            return NoEncontrado("La guía no existe.");
        }
        var (editar, aprobar) = await PermisosAsync(http.User, aplicativoId.Value, lector, permisosDeSesion, ct);
        if (!editar && !aprobar)
        {
            return SinPermiso();
        }
        if (guia.Visibilidad == VisibilidadGuia.Compartible && !aprobar)
        {
            return Error(StatusCodes.Status403Forbidden, "aprobacion_requerida",
                "Una guía compartible con clientes solo la aprueba el responsable principal o secundario del aplicativo.");
        }
        if (solicitud.Revision != guia.RevisionActual)
        {
            return Desactualizada(guia);
        }
        if (guia.RevisionPublicada == guia.RevisionActual)
        {
            return Conflicto("ya_publicada", $"La revisión {guia.RevisionActual} ya está publicada.");
        }

        var ahora = reloj.GetUtcNow();
        var persona = PersonaDeSesion(http.User);
        var anterior = guia.RevisionPublicada;
        guia.Estado = EstadoGuia.Publicada;
        guia.RevisionPublicada = guia.RevisionActual;
        guia.PublicadaPor = persona;
        guia.PublicadaEn = ahora;
        guia.AprobadaPor = guia.Visibilidad == VisibilidadGuia.Compartible ? persona : null;
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionPublicada, guia, guia.RevisionActual, new
        {
            contrato = guia.ContratoId, revision = guia.RevisionActual, anterior, visibilidad = ReglasGuias.Texto(guia.Visibilidad),
            aprobacion = guia.AprobadaPor is not null,
        }), ct);
        return Results.Ok(await DetalleDeAsync(guia, http.User, contexto, plataforma, lector, permisosDeSesion, ct));
    }

    // --- Piezas comunes ---

    /// <summary>La guía (con seguimiento) si existe y la persona puede editarla; si no, 404 o 403.</summary>
    private static async Task<(GuiaContrato? Guia, IResult? Error)> GuiaParaEscribirAsync(
        Guid guiaId, ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var guia = await contexto.Guias.FirstOrDefaultAsync(g => g.Id == guiaId, ct);
        var aplicativoId = guia is null ? null : await contexto.Contratos.Where(c => c.Id == guia.ContratoId).Select(c => (Guid?)c.AplicativoId).FirstOrDefaultAsync(ct);
        if (guia is null || aplicativoId is null)
        {
            return (null, NoEncontrado("La guía no existe."));
        }
        return (await PermisosAsync(usuario, aplicativoId.Value, lector, permisosDeSesion, ct)).Editar ? (guia, null) : (null, SinPermiso());
    }

    private static async Task<IResult> NuevaRevisionAsync(
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion,
        TimeProvider reloj, GuiaContrato guia, FotoGuia foto, string nota, OrigenRevision origen, int? restauradaDe, string accion, bool created,
        CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var numero = guia.RevisionActual + 1;
        var revision = Revision(guia.Id, numero, origen, restauradaDe, foto, nota, PersonaDeSesion(http.User), ahora);
        Aplicar(guia, foto, numero);
        // Lo publicado no cambia hasta volver a publicar; la guía queda en borrador.
        guia.Estado = EstadoGuia.Borrador;
        contexto.RevisionesGuia.Add(revision);
        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, accion, guia, numero, new
            {
                contrato = guia.ContratoId, revision = numero, restauradaDe, visibilidad = ReglasGuias.Texto(foto.Visibilidad), huella = revision.Huella,
            }), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Otro guardado de la misma guía ganó la carrera por el número.
            return Conflicto("revision_desactualizada", "Alguien guardó otra revisión al mismo tiempo: recarga la guía y vuelve a intentarlo.");
        }
        var detalle = await DetalleDeAsync(guia, http.User, contexto, plataforma, lector, permisosDeSesion, ct);
        return created
            ? Results.Created($"/api/v1/contratos/guias/{guia.Id}/revisiones/{numero}", detalle)
            : Results.Ok(detalle);
    }

    private static void Aplicar(GuiaContrato guia, FotoGuia foto, int numero)
    {
        guia.Titulo = foto.Titulo;
        guia.Operacion = foto.Operacion;
        guia.AplicaDesde = foto.AplicaDesde;
        guia.AplicaHasta = foto.AplicaHasta;
        guia.Visibilidad = foto.Visibilidad;
        guia.RevisionActual = numero;
    }

    private static RevisionGuia Revision(Guid guiaId, int numero, OrigenRevision origen, int? restauradaDe, FotoGuia foto, string nota, Guid autor,
        DateTimeOffset ahora) => new()
    {
        GuiaId = guiaId,
        Numero = numero,
        Origen = origen,
        RestauradaDe = restauradaDe,
        Titulo = foto.Titulo,
        Contenido = foto.Contenido,
        Operacion = foto.Operacion,
        AplicaDesde = foto.AplicaDesde,
        AplicaHasta = foto.AplicaHasta,
        Visibilidad = foto.Visibilidad,
        Nota = nota,
        AutorPersonaId = autor,
        GuardadaEn = ahora,
        Huella = foto.Huella(),
    };

    /// <summary>Valida la solicitud y, si trae operación, que exista en el índice de alguna versión del contrato.</summary>
    private static async Task<(FotoGuia? Foto, string? Nota, Dictionary<string, string[]> Errores)> LeerAsync(
        ContratosDbContext contexto, Guid contratoId, SolicitudGuia? solicitud, CancellationToken ct)
    {
        var leida = ReglasGuias.Leer(solicitud?.Titulo, solicitud?.Contenido, solicitud?.Operacion, solicitud?.AplicaDesde, solicitud?.AplicaHasta,
            solicitud?.Visibilidad, solicitud?.Nota);
        if (leida.Foto?.Operacion is { } operacion)
        {
            var existe = await contexto.Operaciones.AnyAsync(o => o.Clave == operacion
                && contexto.Versiones.Any(v => v.Id == o.VersionId && v.ContratoId == contratoId), ct);
            if (!existe)
            {
                return (null, null, new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["operacion"] = ["No está en el índice de operaciones de ninguna versión del contrato."],
                });
            }
        }
        return leida;
    }

    private static async Task<(bool Editar, bool Aprobar)> PermisosAsync(
        ClaimsPrincipal usuario, Guid aplicativoId, ILectorAplicativos lector, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        return (ReglasGuias.PuedeEditar(permisos, aplicativoId, aplicativos), ReglasGuias.PuedeAprobar(permisos, aplicativoId, aplicativos));
    }

    private static async Task<GuiaDetalle> DetalleDeAsync(
        GuiaContrato guia, ClaimsPrincipal usuario, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var revision = await contexto.RevisionesGuia.AsNoTracking().FirstAsync(r => r.GuiaId == guia.Id && r.Numero == guia.RevisionActual, ct);
        var aplicativoId = await contexto.Contratos.Where(c => c.Id == guia.ContratoId).Select(c => c.AplicativoId).FirstAsync(ct);
        var (editar, aprobar) = await PermisosAsync(usuario, aplicativoId, lector, permisosDeSesion, ct);
        var nombres = await NombresAsync(plataforma, [revision.AutorPersonaId, guia.PublicadaPor, guia.AprobadaPor], ct);
        return new GuiaDetalle(Resumen(guia, nombres), Detalle(revision, guia, nombres), editar, aprobar);
    }

    private static GuiaResumen Resumen(GuiaContrato g, IReadOnlyDictionary<Guid, string> nombres) => new(
        g.Id, g.ContratoId, g.Titulo, g.Operacion, g.AplicaDesde, g.AplicaHasta, ReglasGuias.Texto(g.Visibilidad), ReglasGuias.Texto(g.Estado),
        g.RevisionActual, g.RevisionPublicada, g.PublicadaEn, g.PublicadaPor is { } p ? Persona(p, nombres) : null,
        g.AprobadaPor is { } a ? Persona(a, nombres) : null, g.ActualizadoEn);

    private static RevisionDetalle Detalle(RevisionGuia r, GuiaContrato g, IReadOnlyDictionary<Guid, string> nombres) => new(
        r.Numero, ReglasGuias.Texto(r.Origen), r.RestauradaDe, r.Titulo, r.Contenido, r.Operacion, r.AplicaDesde, r.AplicaHasta,
        ReglasGuias.Texto(r.Visibilidad), r.Nota, Persona(r.AutorPersonaId, nombres), r.GuardadaEn, r.Huella, r.Numero == g.RevisionPublicada);

    private static PersonaRef Persona(Guid id, IReadOnlyDictionary<Guid, string> nombres) => new(id, nombres.GetValueOrDefault(id) ?? "Persona no disponible");

    internal static async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(PlataformaDbContext plataforma, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var lista = ids.OfType<Guid>().Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await plataforma.Personas.AsNoTracking().Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
    }

    internal static Guid PersonaDeSesion(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    /// <summary>Auditoría de una acción sobre una guía: ids, números, visibilidad y huella; nunca el título, el cuerpo ni la nota.</summary>
    private static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, GuiaContrato guia, int revision, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = guia.Id.ToString(),
        Version = revision.ToString(CultureInfo.InvariantCulture),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>Los cambios de la guía y su auditoría en <b>una sola transacción</b> (el contexto de la plataforma usa la del módulo).</summary>
    internal static async Task GuardarConAuditoriaAsync(ContratosDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria evento, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        plataforma.Auditoria.Add(evento);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        await plataforma.Database.UseTransactionAsync(null, ct);
    }

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorGuia(codigo, mensaje), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo el líder técnico o el responsable del aplicativo editan sus guías (atlas.contratos.guias.editar).");

    private static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    private static IResult Desactualizada(GuiaContrato guia) =>
        Conflicto("revision_desactualizada", $"La guía ya va en la revisión {guia.RevisionActual}: recárgala y vuelve a intentarlo.");
}
