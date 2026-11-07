using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Render;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Diagramas;

/// <summary>
/// Diagramas en <c>/api/v1/diagramas</c> (HU 6.6 y 6.7, ATLAS-ADR-0054). Lo que la persona no puede ver responde 404, igual que
/// lo que no existe, para no revelar nada. Cada guardado, restauración y comentario va con su auditoría en la misma
/// transacción, sin títulos, fuentes, notas ni textos.
/// </summary>
internal static class EndpointsDiagramas
{
    public const string RecursoAuditoria = "diagramas.diagrama";
    public const string AccionCreado = "diagramas.creado";
    public const string AccionVersion = "diagramas.version_guardada";
    public const string AccionRestaurada = "diagramas.version_restaurada";
    public const string AccionComentario = "diagramas.comentario_creado";
    public const string AccionExportado = "diagramas.exportado";

    private const int LimiteMaximo = 100;

    [Description("Persona de Atlas.")]
    public sealed record PersonaDiagrama(Guid Id, [property: Description("Nombre; «Persona no disponible» si ya no está.")] string Nombre);

    [Description("Un diagrama sin su fuente.")]
    public sealed record DiagramaResumen(
        Guid Id,
        [property: Description("Código único: el id de <Diagrama/> en los documentos.")] string Codigo,
        Guid AplicativoId,
        [property: Description("lienzo, mermaid o bpmn.")] string Tipo,
        string Titulo,
        [property: Description("interno_negocio, interno_ti o restringido.")] string Visibilidad,
        int VersionActual,
        DateTimeOffset ActualizadoEn);

    [Description("Diagramas que la persona puede ver.")]
    public sealed record ListadoDiagramas(IReadOnlyList<DiagramaResumen> Elementos, [property: Description("Total con estos filtros, sin el límite.")] int Total);

    [Description("Una versión inmutable, sin la fuente.")]
    public sealed record VersionDiagramaResumen(
        int Numero,
        [property: Description("creacion, edicion o restauracion.")] string Origen,
        [property: Description("Si es una restauración, la versión restaurada.")] int? RestauradaDe,
        string Titulo,
        [property: Description("interno_negocio, interno_ti o restringido.")] string Visibilidad,
        string Nota,
        PersonaDiagrama Autor,
        DateTimeOffset GuardadaEn,
        [property: Description("SHA-256 de título, visibilidad y fuente.")] string Huella);

    [Description("Una versión con su fuente.")]
    public sealed record VersionDiagramaDetalle(
        VersionDiagramaResumen Version,
        [property: Description("Fuente Mermaid, XML BPMN ya limpio o JSON del lienzo.")] string Fuente);

    [Description("Aplicativo del catálogo que menciona un diagrama.")]
    public sealed record ReferenciaAplicativo(Guid AplicativoId, string Slug, string Nombre);

    [Description("Un diagrama con su última versión y lo que la persona puede hacer.")]
    public sealed record DiagramaDetalle(
        DiagramaResumen Diagrama,
        [property: Description("La última versión que la persona puede ver; vacío si todas son de un nivel que no alcanza.")] VersionDiagramaDetalle? Version,
        [property: Description("Aplicativos del catálogo que menciona el lienzo en esa versión.")] IReadOnlyList<ReferenciaAplicativo> Referencias,
        bool PuedeEditar);

    [Description("Un comentario.")]
    public sealed record ComentarioResumen(
        Guid Id,
        int VersionNumero,
        [property: Description("Nodo del lienzo, si es sobre uno.")] string? NodoClave,
        PersonaDiagrama Persona,
        string Texto,
        DateTimeOffset EscritoEn);

    [Description("Crear un diagrama con su primera versión.")]
    public sealed record SolicitudCrearDiagrama(
        [property: Description("Minúsculas, números y guiones, hasta 100 caracteres. No cambia.")] string Codigo,
        [property: Description("Aplicativo del catálogo. No cambia.")] Guid AplicativoId,
        [property: Description("lienzo, mermaid o bpmn. No cambia.")] string Tipo,
        string Titulo,
        [property: Description("interno_negocio, interno_ti o restringido.")] string Visibilidad,
        [property: Description("Fuente Mermaid (hasta 20 mil caracteres), XML BPMN o JSON del lienzo (hasta 1 millón).")] string Fuente,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota);

    [Description("Guardar una versión nueva.")]
    public sealed record SolicitudGuardarDiagrama(
        string Titulo,
        [property: Description("interno_negocio, interno_ti o restringido.")] string Visibilidad,
        [property: Description("Fuente Mermaid, XML BPMN o JSON del lienzo.")] string Fuente,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("La versión sobre la que se trabajó: 409 si ya hay otra más nueva.")] int VersionBase);

    [Description("Restaurar una versión: crea otra igual a ella.")]
    public sealed record SolicitudRestaurarDiagrama(
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("La última versión que vio la persona: 409 si ya hay otra más nueva.")] int VersionBase);

    [Description("Escribir un comentario.")]
    public sealed record SolicitudComentario(
        [property: Description("Versión a la que se refiere; debe existir y ser visible.")] int VersionNumero,
        [property: Description("Nodo del lienzo, si es sobre uno.")] string? NodoClave,
        [property: Description("Hasta 2.000 caracteres.")] string Texto);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorDiagrama(
        [property: Description("no_encontrado (404); sin_permiso (403); version_desactualizada, sin_cambios, codigo_existente (409); exportacion_no_soportada (422); render_no_disponible (503).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", ListarAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ListarDiagramas")
            .WithSummary("Diagramas visibles")
            .WithDescription("Los diagramas que la persona puede ver, del más reciente al más antiguo. Con aplicativo, los suyos y los lienzos cuya última versión lo menciona. Requiere atlas.docs.ver; la visibilidad se aplica en la consulta.")
            .Produces<ListadoDiagramas>()
            .ProducesValidationProblem();

        grupo.MapPost("/", CrearAsync)
            .RequierePermiso(PermisosDiagramas.Editar)
            .WithName("CrearDiagrama")
            .WithSummary("Crear un diagrama")
            .WithDescription("Crea el diagrama con su versión 1, auditado en la misma transacción. El XML BPMN se valida y se limpia antes de guardarse. Requiere atlas.diagramas.editar global o sobre el aplicativo; si no, 403. 409 codigo_existente si el código ya está en uso.")
            .Produces<DiagramaDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorDiagrama>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDiagrama>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/por-codigo/{codigo}", PorCodigoAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ObtenerDiagramaPorCodigo")
            .WithSummary("Un diagrama por su código")
            .WithDescription("Lo que usa <Diagrama/> en los documentos: el diagrama y su última versión visible. 404 si no existe o la persona no puede verlo. Requiere atlas.docs.ver.")
            .Produces<DiagramaDetalle>()
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound);

        grupo.MapGet("/{id:guid}", DetalleAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ObtenerDiagrama")
            .WithSummary("Un diagrama con su última versión")
            .WithDescription("El diagrama, su última versión visible, los aplicativos que menciona y si la persona puede editarlo. 404 si no existe o no puede verlo. Requiere atlas.docs.ver.")
            .Produces<DiagramaDetalle>()
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound);

        grupo.MapPut("/{id:guid}", GuardarAsync)
            .RequierePermiso(PermisosDiagramas.Editar)
            .WithName("GuardarDiagrama")
            .WithSummary("Guardar una versión nueva")
            .WithDescription("Cada guardado crea una versión inmutable con autor, fecha y nota. 409 version_desactualizada si versionBase no es la última; 409 sin_cambios si es igual a la última. Requiere atlas.diagramas.editar sobre el aplicativo.")
            .Produces<DiagramaDetalle>()
            .Produces<ErrorDiagrama>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound)
            .Produces<ErrorDiagrama>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/{id:guid}/versiones", VersionesAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ListarVersionesDiagrama")
            .WithSummary("Historial de versiones")
            .WithDescription("Las versiones que la persona puede ver, de la más reciente a la más antigua, sin la fuente. 404 si el diagrama no existe o no puede verlo. Requiere atlas.docs.ver.")
            .Produces<List<VersionDiagramaResumen>>()
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound);

        grupo.MapGet("/{id:guid}/versiones/{numero}", VersionAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ObtenerVersionDiagrama")
            .WithSummary("Una versión con su fuente")
            .WithDescription("La versión tal como se guardó. 404 si no existe o la persona no alcanza su nivel. Requiere atlas.docs.ver.")
            .Produces<VersionDiagramaDetalle>()
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound);

        grupo.MapGet("/{id:guid}/versiones/{numero}/svg", ExportarSvgAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ExportarVersionDiagramaSvg")
            .WithSummary("Exportar una versión del lienzo a SVG")
            .WithDescription("El SVG de la versión, generado con D2 en atlas-render y limpio de scripts y enlaces externos, como archivo para descargar. Acomoda los nodos de nuevo: no conserva las posiciones del lienzo. Auditado. 404 si no existe o la persona no alcanza su nivel; 422 exportacion_no_soportada si no es un lienzo; 503 render_no_disponible si atlas-render no está disponible. Requiere atlas.docs.ver.")
            .Produces<string>(StatusCodes.Status200OK, "image/svg+xml")
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound)
            .Produces<ErrorDiagrama>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDiagrama>(StatusCodes.Status503ServiceUnavailable);

        grupo.MapPost("/{id:guid}/versiones/{numero}/restauracion", RestaurarAsync)
            .RequierePermiso(PermisosDiagramas.Editar)
            .WithName("RestaurarVersionDiagrama")
            .WithSummary("Restaurar una versión")
            .WithDescription("Crea una versión nueva igual a la elegida, sin reescribir la historia. 409 version_desactualizada si versionBase no es la última; 409 sin_cambios si la última ya es igual. Requiere atlas.diagramas.editar sobre el aplicativo.")
            .Produces<DiagramaDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorDiagrama>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound)
            .Produces<ErrorDiagrama>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/{id:guid}/comentarios", ComentariosAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ListarComentariosDiagrama")
            .WithSummary("Comentarios de un diagrama")
            .WithDescription("Los comentarios de las versiones que la persona puede ver, del más antiguo al más reciente, hasta 200. 404 si el diagrama no existe o no puede verlo. Requiere atlas.docs.ver.")
            .Produces<List<ComentarioResumen>>()
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/comentarios", ComentarAsync)
            .RequierePermiso(PermisosDiagramas.Ver)
            .WithName("ComentarDiagrama")
            .WithSummary("Comentar un diagrama")
            .WithDescription("Comenta quien puede leer la versión. Auditado, sin el texto. 404 si el diagrama no existe o no puede verlo. Requiere atlas.docs.ver.")
            .Produces<ComentarioResumen>(StatusCodes.Status201Created)
            .Produces<ErrorDiagrama>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    // --- Lectura ---

    private static async Task<IResult> ListarAsync(
        [Description("Aplicativo: sus diagramas y los lienzos que lo mencionan.")] Guid? aplicativo,
        [Description("lienzo, mermaid o bpmn.")] string? tipo,
        [Description("Busca en el título y el código.")] string? texto,
        [Description("Hasta 100; 50 por defecto.")] int? limite,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        TipoDiagrama? filtroTipo = null;
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            if (!ReglasDiagramas.Tipos.TryGetValue(tipo.Trim(), out var t))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["Valores válidos: lienzo, mermaid, bpmn."] });
            }
            filtroTipo = t;
        }
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var consulta = acceso.Filtrar(contexto.Diagramas.AsNoTracking());
        if (aplicativo is { } a)
        {
            consulta = consulta.Where(d => d.AplicativoId == a
                || contexto.Nodos.Any(n => n.AplicativoId == a
                    && contexto.Versiones.Any(v => v.Id == n.VersionId && v.DiagramaId == d.Id && v.Numero == d.VersionActual)));
        }
        if (filtroTipo is { } f)
        {
            consulta = consulta.Where(d => d.Tipo == f);
        }
        if (!string.IsNullOrWhiteSpace(texto))
        {
            consulta = BusquedaDiagramas.PorTexto(consulta, texto);
        }
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderByDescending(d => d.ActualizadoEn).ThenBy(d => d.Id)
            .Take(Math.Clamp(limite ?? 50, 1, LimiteMaximo)).ToListAsync(ct);
        return Results.Ok(new ListadoDiagramas([.. filas.Select(Resumen)], total));
    }

    private static async Task<IResult> PorCodigoAsync(
        [Description("Código del diagrama.")] string codigo,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var diagrama = ReglasDiagramas.CodigoValido(codigo)
            ? await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Codigo == codigo, ct)
            : null;
        return diagrama is null ? NoEncontrado() : Results.Ok(await DetalleDeAsync(diagrama, acceso, contexto, plataforma, ct));
    }

    private static async Task<IResult> DetalleAsync(
        [Description("Id del diagrama.")] Guid id,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        return diagrama is null ? NoEncontrado() : Results.Ok(await DetalleDeAsync(diagrama, acceso, contexto, plataforma, ct));
    }

    private static async Task<IResult> VersionesAsync(
        [Description("Id del diagrama.")] Guid id,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (diagrama is null)
        {
            return NoEncontrado();
        }
        var versiones = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id), diagrama.AplicativoId)
            .OrderByDescending(v => v.Numero)
            .Select(v => new { v.Numero, v.Origen, v.RestauradaDe, v.Titulo, v.Visibilidad, v.Nota, v.AutorPersonaId, v.GuardadaEn, v.Huella })
            .ToListAsync(ct);
        var nombres = await NombresAsync(plataforma, versiones.Select(v => v.AutorPersonaId), ct);
        return Results.Ok(versiones.Select(v => new VersionDiagramaResumen(v.Numero, ReglasDiagramas.Texto(v.Origen), v.RestauradaDe, v.Titulo,
            ReglasDiagramas.Texto(v.Visibilidad), v.Nota, Persona(v.AutorPersonaId, nombres), v.GuardadaEn, v.Huella)).ToList());
    }

    private static async Task<IResult> VersionAsync(
        [Description("Id del diagrama.")] Guid id,
        [Description("Número de la versión.")] int numero,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = diagrama is null
            ? null
            : await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == numero), diagrama.AplicativoId).FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return NoEncontrado();
        }
        var nombres = await NombresAsync(plataforma, [version.AutorPersonaId], ct);
        return Results.Ok(new VersionDiagramaDetalle(ResumenVersion(version, nombres), version.Fuente));
    }

    private static async Task<IResult> ExportarSvgAsync(
        [Description("Id del diagrama.")] Guid id,
        [Description("Número de la versión.")] int numero,
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        IRenderLienzo render, TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = diagrama is null
            ? null
            : await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == numero), diagrama.AplicativoId).FirstOrDefaultAsync(ct);
        if (diagrama is null || version is null)
        {
            return NoEncontrado();
        }
        if (diagrama.Tipo != TipoDiagrama.Lienzo)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "exportacion_no_soportada", "Solo los lienzos se exportan a SVG.");
        }

        var lienzo = ValidadorLienzo.Leer(version.Fuente);
        byte[] svg;
        try
        {
            svg = await render.SvgAsync(new ModeloExporte(
                [.. lienzo.Nodos.Select(n => new NodoExporte(n.Id, n.Tipo, n.Etiqueta))],
                [.. lienzo.Relaciones.Select(r => new RelacionExporte(r.Origen, r.Destino, r.Etiqueta))]), ct);
        }
        catch (RenderLienzoException ex) when (ex.NoDisponible)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "render_no_disponible", "La exportación a SVG no está disponible en este momento.");
        }

        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj.GetUtcNow(), AccionExportado, id, numero, new { formato = "svg", huella = version.Huella }), ct);
        // Se descarga, no se muestra en el origen de Atlas; por si alguien lo abre directo, sin scripts ni recursos externos.
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; font-src data:; sandbox";
        return Results.File(svg, "image/svg+xml", $"{diagrama.Codigo}-v{numero}.svg");
    }

    private static async Task<IResult> ComentariosAsync(
        [Description("Id del diagrama.")] Guid id,
        ClaimsPrincipal usuario, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (diagrama is null)
        {
            return NoEncontrado();
        }
        // Un comentario sobre una versión de un nivel que la persona no alcanza no se muestra.
        var visibles = acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id), diagrama.AplicativoId).Select(v => v.Numero);
        var comentarios = await contexto.Comentarios.AsNoTracking()
            .Where(c => c.DiagramaId == id && visibles.Contains(c.VersionNumero))
            .OrderBy(c => c.EscritoEn).ThenBy(c => c.Id).Take(200).ToListAsync(ct);
        var nombres = await NombresAsync(plataforma, comentarios.Select(c => c.PersonaId), ct);
        return Results.Ok(comentarios.Select(c => ResumenComentario(c, nombres)).ToList());
    }

    // --- Escritura ---

    private static async Task<IResult> CrearAsync(
        [Description("El diagrama y su primera versión.")] SolicitudCrearDiagrama solicitud,
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var codigo = (solicitud.Codigo ?? string.Empty).Trim();
        if (!ReglasDiagramas.CodigoValido(codigo))
        {
            errores["codigo"] = [$"El código va en minúsculas, números y guiones, hasta {DiagramasDbContext.LargoCodigo} caracteres."];
        }
        if (!ReglasDiagramas.Tipos.TryGetValue((solicitud.Tipo ?? string.Empty).Trim(), out var tipo))
        {
            errores["tipo"] = ["Valores válidos: lienzo, mermaid, bpmn."];
        }
        var acceso = await AccesoDiagramas.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        if (!acceso.Aplicativos.ContainsKey(solicitud.AplicativoId))
        {
            errores["aplicativoId"] = ["El aplicativo no existe en el catálogo."];
        }
        else if (!acceso.PuedeEditar(solicitud.AplicativoId))
        {
            return SinPermiso();
        }
        if (errores.ContainsKey("tipo"))
        {
            return Results.ValidationProblem(errores);
        }
        var campos = LeerCampos(tipo, solicitud.Titulo, solicitud.Visibilidad, solicitud.Fuente, solicitud.Nota, solicitud.AplicativoId, acceso, errores);
        if (campos is null || errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var ahora = reloj.GetUtcNow();
        var diagrama = new Diagrama
        {
            Codigo = codigo, AplicativoId = solicitud.AplicativoId, Tipo = tipo, Titulo = campos.Titulo, Visibilidad = campos.Visibilidad, VersionActual = 1,
        };
        contexto.Diagramas.Add(diagrama);
        var version = AgregarVersion(contexto, diagrama, 1, OrigenVersionDiagrama.Creacion, null, campos, PersonaDeSesion(http.User), ahora);
        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionCreado, diagrama.Id, 1, new
            {
                tipo = ReglasDiagramas.Texto(tipo), visibilidad = ReglasDiagramas.Texto(campos.Visibilidad), huella = version.Huella,
            }), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "codigo_existente", "Ya hay un diagrama con ese código.");
        }
        return Results.Created($"/api/v1/diagramas/{diagrama.Id}", await DetalleDeAsync(diagrama, acceso, contexto, plataforma, ct));
    }

    private static async Task<IResult> GuardarAsync(
        [Description("Id del diagrama.")] Guid id,
        [Description("La versión nueva.")] SolicitudGuardarDiagrama solicitud,
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (diagrama is null)
        {
            return NoEncontrado();
        }
        if (!acceso.PuedeEditar(diagrama.AplicativoId))
        {
            return SinPermiso();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var campos = LeerCampos(diagrama.Tipo, solicitud.Titulo, solicitud.Visibilidad, solicitud.Fuente, solicitud.Nota, diagrama.AplicativoId, acceso, errores);
        if (campos is null || errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (solicitud.VersionBase != diagrama.VersionActual)
        {
            return Desactualizada(diagrama);
        }
        var ultima = await contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == diagrama.VersionActual).Select(v => v.Huella).FirstAsync(ct);
        if (ultima == ReglasDiagramas.Huella(campos.Titulo, campos.Visibilidad, campos.Fuente))
        {
            return Error(StatusCodes.Status409Conflict, "sin_cambios", $"No hay cambios frente a la versión {diagrama.VersionActual}.");
        }
        return await NuevaVersionAsync(http, contexto, plataforma, acceso, reloj, diagrama, campos, OrigenVersionDiagrama.Edicion, null, AccionVersion, created: false, ct);
    }

    private static async Task<IResult> RestaurarAsync(
        [Description("Id del diagrama.")] Guid id,
        [Description("Versión a restaurar.")] int numero,
        [Description("La nota y la última versión que se vio.")] SolicitudRestaurarDiagrama solicitud,
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (diagrama is null)
        {
            return NoEncontrado();
        }
        if (!acceso.PuedeEditar(diagrama.AplicativoId))
        {
            return SinPermiso();
        }
        var origen = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == numero), diagrama.AplicativoId).FirstOrDefaultAsync(ct);
        if (origen is null)
        {
            return NoEncontrado();
        }
        var nota = (solicitud.Nota ?? string.Empty).Trim();
        if (nota.Length is 0 or > DiagramasDbContext.LargoNota)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["nota"] = [$"La nota del cambio es obligatoria y va hasta {DiagramasDbContext.LargoNota} caracteres."] });
        }
        if (solicitud.VersionBase != diagrama.VersionActual)
        {
            return Desactualizada(diagrama);
        }
        var ultima = await contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == diagrama.VersionActual).Select(v => v.Huella).FirstAsync(ct);
        if (ultima == origen.Huella)
        {
            return Error(StatusCodes.Status409Conflict, "sin_cambios", $"La versión {diagrama.VersionActual} ya es igual a la {numero}.");
        }
        var lienzo = diagrama.Tipo == TipoDiagrama.Lienzo ? ValidadorLienzo.Leer(origen.Fuente) : null;
        var campos = new CamposVersion(origen.Titulo, origen.Visibilidad, origen.Fuente, lienzo, nota);
        return await NuevaVersionAsync(http, contexto, plataforma, acceso, reloj, diagrama, campos, OrigenVersionDiagrama.Restauracion, numero, AccionRestaurada, created: true, ct);
    }

    private static async Task<IResult> ComentarAsync(
        [Description("Id del diagrama.")] Guid id,
        [Description("El comentario.")] SolicitudComentario solicitud,
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoDiagramas.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        var diagrama = await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (diagrama is null)
        {
            return NoEncontrado();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var texto = (solicitud.Texto ?? string.Empty).Trim();
        if (texto.Length is 0 or > DiagramasDbContext.LargoComentario)
        {
            errores["texto"] = [$"El comentario es obligatorio y va hasta {DiagramasDbContext.LargoComentario} caracteres."];
        }
        var version = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == id && v.Numero == solicitud.VersionNumero), diagrama.AplicativoId)
            .Select(v => new { v.Id }).FirstOrDefaultAsync(ct);
        if (version is null)
        {
            errores["versionNumero"] = ["La versión no existe."];
        }
        var nodo = string.IsNullOrWhiteSpace(solicitud.NodoClave) ? null : solicitud.NodoClave.Trim();
        if (nodo is not null && version is not null && !await contexto.Nodos.AnyAsync(n => n.VersionId == version.Id && n.Clave == nodo, ct))
        {
            errores["nodoClave"] = ["El nodo no existe en esa versión."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var ahora = reloj.GetUtcNow();
        var persona = PersonaDeSesion(http.User);
        var comentario = new ComentarioDiagrama { DiagramaId = id, VersionNumero = solicitud.VersionNumero, NodoClave = nodo, PersonaId = persona, Texto = texto, EscritoEn = ahora };
        contexto.Comentarios.Add(comentario);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, AccionComentario, id, solicitud.VersionNumero, new { comentario = comentario.Id, nodo }), ct);
        var nombres = await NombresAsync(plataforma, [persona], ct);
        return Results.Created($"/api/v1/diagramas/{id}/comentarios", ResumenComentario(comentario, nombres));
    }

    // --- Piezas comunes ---

    private sealed record CamposVersion(string Titulo, VisibilidadDiagrama Visibilidad, string Fuente, LienzoFuente? Lienzo, string Nota);

    private static CamposVersion? LeerCampos(
        TipoDiagrama tipo, string? titulo, string? visibilidad, string? fuente, string? nota, Guid aplicativoId, AccesoDiagramas acceso, Dictionary<string, string[]> errores)
    {
        var t = (titulo ?? string.Empty).Trim();
        if (t.Length is 0 or > DiagramasDbContext.LargoTitulo)
        {
            errores["titulo"] = [$"El título es obligatorio y va hasta {DiagramasDbContext.LargoTitulo} caracteres."];
        }
        VisibilidadDiagrama? nivel = ReglasDiagramas.Visibilidades.TryGetValue((visibilidad ?? string.Empty).Trim(), out var v) ? v : null;
        if (nivel is null)
        {
            errores["visibilidad"] = ["Valores válidos: interno_negocio, interno_ti, restringido."];
        }
        else if (!acceso.AlcanzaNivel(aplicativoId, nivel.Value))
        {
            errores["visibilidad"] = ["No puedes dejar el diagrama en un nivel que no podrías ver."];
        }
        var n = (nota ?? string.Empty).Trim();
        if (n.Length is 0 or > DiagramasDbContext.LargoNota)
        {
            errores["nota"] = [$"La nota del cambio es obligatoria y va hasta {DiagramasDbContext.LargoNota} caracteres."];
        }
        var validada = tipo switch
        {
            TipoDiagrama.Bpmn => ValidadorBpmn.Validar(fuente),
            TipoDiagrama.Lienzo => ValidadorLienzo.Validar(fuente, acceso.Aplicativos),
            _ => ValidarMermaid(fuente),
        };
        if (validada.Errores.Count > 0)
        {
            errores["fuente"] = [.. validada.Errores];
        }
        return errores.Count > 0 ? null : new CamposVersion(t, nivel!.Value, validada.Fuente!, validada.Lienzo, n);
    }

    private static FuenteValidada ValidarMermaid(string? fuente)
    {
        var texto = ReglasDiagramas.Normalizar(fuente);
        return string.IsNullOrWhiteSpace(texto) || texto.Length > ReglasDiagramas.LargoMermaid
            ? FuenteValidada.Mal($"La fuente es obligatoria y va hasta {ReglasDiagramas.LargoMermaid / 1000} mil caracteres.")
            : new FuenteValidada(texto, null, []);
    }

    private static VersionDiagrama AgregarVersion(
        DiagramasDbContext contexto, Diagrama diagrama, int numero, OrigenVersionDiagrama origen, int? restauradaDe, CamposVersion campos, Guid autor, DateTimeOffset ahora)
    {
        var version = new VersionDiagrama
        {
            DiagramaId = diagrama.Id,
            Numero = numero,
            Origen = origen,
            RestauradaDe = restauradaDe,
            Titulo = campos.Titulo,
            Visibilidad = campos.Visibilidad,
            Fuente = campos.Fuente,
            Nota = campos.Nota,
            AutorPersonaId = autor,
            GuardadaEn = ahora,
            Huella = ReglasDiagramas.Huella(campos.Titulo, campos.Visibilidad, campos.Fuente),
        };
        contexto.Versiones.Add(version);
        if (campos.Lienzo is { } lienzo)
        {
            contexto.Nodos.AddRange(lienzo.Nodos.Select(n => new NodoDiagrama
            {
                VersionId = version.Id, Clave = n.Id, Tipo = n.Tipo, Etiqueta = n.Etiqueta, AplicativoId = n.AplicativoId, ElementoLikeC4 = n.ElementoLikeC4,
            }));
            contexto.Relaciones.AddRange(lienzo.Relaciones.Select(r => new RelacionDiagrama
            {
                VersionId = version.Id, Clave = r.Id, Origen = r.Origen, Destino = r.Destino, Etiqueta = r.Etiqueta,
            }));
        }
        return version;
    }

    private static async Task<IResult> NuevaVersionAsync(
        HttpContext http, DiagramasDbContext contexto, PlataformaDbContext plataforma, AccesoDiagramas acceso, TimeProvider reloj, Diagrama diagrama, CamposVersion campos,
        OrigenVersionDiagrama origen, int? restauradaDe, string accion, bool created, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var numero = diagrama.VersionActual + 1;
        var version = AgregarVersion(contexto, diagrama, numero, origen, restauradaDe, campos, PersonaDeSesion(http.User), ahora);
        diagrama.Titulo = campos.Titulo;
        diagrama.Visibilidad = campos.Visibilidad;
        diagrama.VersionActual = numero;
        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, ahora, accion, diagrama.Id, numero, new
            {
                restauradaDe, visibilidad = ReglasDiagramas.Texto(campos.Visibilidad), huella = version.Huella,
            }), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "version_desactualizada", "Alguien guardó otra versión al mismo tiempo: recarga el diagrama y vuelve a intentarlo.");
        }
        var detalle = await DetalleDeAsync(diagrama, acceso, contexto, plataforma, ct);
        return created ? Results.Created($"/api/v1/diagramas/{diagrama.Id}/versiones/{numero}", detalle) : Results.Ok(detalle);
    }

    private static async Task<DiagramaDetalle> DetalleDeAsync(
        Diagrama diagrama, AccesoDiagramas acceso, DiagramasDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var version = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DiagramaId == diagrama.Id), diagrama.AplicativoId)
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return new DiagramaDetalle(Resumen(diagrama), null, [], acceso.PuedeEditar(diagrama.AplicativoId));
        }
        var nombres = await NombresAsync(plataforma, [version.AutorPersonaId], ct);
        var mencionados = await contexto.Nodos.AsNoTracking().Where(n => n.VersionId == version.Id && n.AplicativoId != null)
            .Select(n => n.AplicativoId!.Value).Distinct().ToListAsync(ct);
        var referencias = mencionados.Where(acceso.Aplicativos.ContainsKey).Select(id => acceso.Aplicativos[id])
            .OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(a => new ReferenciaAplicativo(a.Id, a.Slug, a.Nombre)).ToList();
        return new DiagramaDetalle(Resumen(diagrama), new VersionDiagramaDetalle(ResumenVersion(version, nombres), version.Fuente), referencias, acceso.PuedeEditar(diagrama.AplicativoId));
    }

    private static DiagramaResumen Resumen(Diagrama d) => new(
        d.Id, d.Codigo, d.AplicativoId, ReglasDiagramas.Texto(d.Tipo), d.Titulo, ReglasDiagramas.Texto(d.Visibilidad), d.VersionActual, d.ActualizadoEn);

    private static VersionDiagramaResumen ResumenVersion(VersionDiagrama v, IReadOnlyDictionary<Guid, string> nombres) => new(
        v.Numero, ReglasDiagramas.Texto(v.Origen), v.RestauradaDe, v.Titulo, ReglasDiagramas.Texto(v.Visibilidad), v.Nota, Persona(v.AutorPersonaId, nombres), v.GuardadaEn, v.Huella);

    private static ComentarioResumen ResumenComentario(ComentarioDiagrama c, IReadOnlyDictionary<Guid, string> nombres) => new(
        c.Id, c.VersionNumero, c.NodoClave, Persona(c.PersonaId, nombres), c.Texto, c.EscritoEn);

    private static PersonaDiagrama Persona(Guid id, IReadOnlyDictionary<Guid, string> nombres) => new(id, nombres.GetValueOrDefault(id) ?? "Persona no disponible");

    private static async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(PlataformaDbContext plataforma, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await plataforma.Personas.AsNoTracking().Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
    }

    private static Guid PersonaDeSesion(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    /// <summary>Auditoría: ids, números, nivel y huella; nunca el título, la fuente, la nota ni el texto.</summary>
    private static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, Guid diagramaId, int version, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = diagramaId.ToString(),
        Version = version.ToString(CultureInfo.InvariantCulture),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>Los cambios y su auditoría en <b>una sola transacción</b>.</summary>
    private static async Task GuardarConAuditoriaAsync(DiagramasDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria evento, CancellationToken ct)
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

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorDiagrama(codigo, mensaje), statusCode: estado);

    private static IResult NoEncontrado() => Error(StatusCodes.Status404NotFound, "no_encontrado", "El diagrama no existe o no tienes acceso.");

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo quien tiene atlas.diagramas.editar o es responsable del aplicativo edita sus diagramas.");

    private static IResult Desactualizada(Diagrama diagrama) =>
        Error(StatusCodes.Status409Conflict, "version_desactualizada", $"El diagrama ya va en la versión {diagrama.VersionActual}: recárgalo y vuelve a intentarlo.");
}
