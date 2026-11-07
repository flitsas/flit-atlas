using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>
/// Modelos y vistas en <c>/api/v1/arquitectura</c> (HU 6.3, ATLAS-ADR-0056). Exigen <c>atlas.arquitectura.ver</c>, y además el predicado
/// de visibilidad en cada consulta: lo que la persona no puede ver responde 404, igual que lo que no existe. Un externo con el permiso
/// (<c>ext.asesor_comercial</c>) recibe listas vacías y 404.
/// </summary>
internal static class EndpointsArquitectura
{
    private const int LimiteHistorial = 100;

    [Description("Publicación sin su contenido.")]
    public sealed record ResumenPublicacionArquitectura(
        Guid Id,
        [property: Description("modelo o despliegue.")] string Tipo,
        [property: Description("dev, qa o pdn.")] string Canal,
        [property: Description("Vacío en un modelo.")] string Cluster,
        string Rama,
        string CommitSha,
        string? Version,
        [property: Description("borrador o publicado.")] string Estado,
        [property: Description("interno_ti o restringido.")] string Visibilidad,
        string? HerramientaVersion,
        DateTimeOffset PublicadoEn);

    [Description("Lo vigente de un proyecto en un canal.")]
    public sealed record CanalProyectoArquitectura(string Canal, ResumenPublicacionArquitectura Publicacion);

    [Description("Un proyecto con lo vigente en cada canal que la persona puede ver.")]
    public sealed record ProyectoArquitecturaResumen(
        string Codigo,
        [property: Description("Ficha del aplicativo; null si ya no está en el catálogo.")] AplicativoEnlaceArquitectura? Aplicativo,
        IReadOnlyList<CanalProyectoArquitectura> Canales);

    [Description("Enlace a la ficha de un aplicativo.")]
    public sealed record AplicativoEnlaceArquitectura(string Slug, string Nombre, [property: Description("Ruta de la PWA.")] string Ruta);

    [Description("Una vista del modelo.")]
    public sealed record VistaArquitecturaResumen(
        string Id,
        [property: Description("estatica, dinamica o despliegue.")] string Tipo,
        [property: Description("landscape, contexto, contenedores, componentes, dinamica, despliegue u otra.")] string Categoria,
        string Titulo);

    [Description("Una publicación y sus vistas. Sin ver_despliegue, no trae las vistas de despliegue.")]
    public sealed record DetallePublicacionArquitectura(string Proyecto, ResumenPublicacionArquitectura Publicacion, IReadOnlyList<VistaArquitecturaResumen> Vistas);

    [Description("Historial de un proyecto.")]
    public sealed record HistorialPublicacionesArquitectura(IReadOnlyList<ResumenPublicacionArquitectura> Elementos);

    [Description("Un contrato que declara el elemento.")]
    public sealed record ContratoElementoArquitectura(
        string Ruta,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        [property: Description("Nombre del contrato en Atlas; null si no está publicado o no puedes ver contratos.")] string? Nombre,
        string? UltimaVersion,
        [property: Description("Ruta de la PWA; null si no se resolvió.")] string? RutaAtlas);

    [Description("Un ADR que cita el elemento.")]
    public sealed record AdrElementoArquitectura(string Codigo, [property: Description("Búsqueda en la documentación de Atlas: los ADR se ubican por ruta, no por número (HU 6.1).")] string RutaAtlas);

    [Description("Enlace https del modelo.")]
    public sealed record EnlaceFichaArquitectura(string Url, string? Titulo);

    [Description("Ficha de un elemento: lo que se abre al tocarlo en la vista.")]
    public sealed record FichaElementoArquitectura(
        string Id,
        string Kind,
        string Titulo,
        string? Descripcion,
        string? Tecnologia,
        IReadOnlyList<string> Etiquetas,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Metadatos,
        AplicativoEnlaceArquitectura? Aplicativo,
        IReadOnlyList<ContratoElementoArquitectura> Contratos,
        IReadOnlyList<AdrElementoArquitectura> Adr,
        IReadOnlyList<EnlaceFichaArquitectura> Enlaces,
        [property: Description("Hallazgos de drift abiertos; null sin ver_despliegue.")] int? DriftAbierto);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/proyectos", ListarProyectosAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ListarProyectosArquitectura")
            .WithSummary("Listar los proyectos de arquitectura")
            .WithDescription("Los proyectos con un modelo que la persona puede ver y lo vigente en cada canal. Un externo recibe la lista vacía.");

        grupo.MapGet("/proyectos/{codigo}/publicaciones", HistorialAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ListarPublicacionesArquitectura")
            .WithSummary("Listar el historial de modelos de un proyecto")
            .WithDescription("Las publicaciones del modelo que la persona puede ver, de la más reciente a la más antigua, hasta 100. Se puede filtrar por canal y por fechas.");

        grupo.MapGet("/proyectos/{codigo}/vigente", VigenteAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ObtenerModeloVigente")
            .WithSummary("Obtener el modelo vigente de un proyecto, también en una fecha pasada")
            .WithDescription("La última publicación del canal (pdn por defecto) con fecha menor o igual a «en». Sin «en», la última. Es «volver en el tiempo» (plan 01 §9.2).");

        grupo.MapGet("/publicaciones/{id:guid}", DetalleAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ObtenerPublicacionArquitectura")
            .WithSummary("Obtener una publicación del modelo y sus vistas")
            .WithDescription("Sin atlas.arquitectura.ver_despliegue no trae las vistas de despliegue. Lo que no se puede ver responde 404.");

        grupo.MapGet("/publicaciones/{id:guid}/modelo", ModeloAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ObtenerModeloArquitectura")
            .WithSummary("Obtener el JSON de LikeC4 para dibujar las vistas")
            .WithDescription("La salida de likec4 export json con su layout. Sin atlas.arquitectura.ver_despliegue, sin vistas ni nodos de despliegue.")
            .Produces<JsonElement>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapGet("/publicaciones/{id:guid}/elementos/{elementoId}", FichaAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("ObtenerFichaElemento")
            .WithSummary("Obtener la ficha de un elemento")
            .WithDescription("Metadatos del elemento y sus enlaces a la ficha del aplicativo, a los contratos (si la persona puede verlos) y a los ADR.");

        grupo.MapGet("/publicaciones/{a:guid}/diff/{b:guid}", DiffAsync)
            .RequierePermiso(PermisosArquitectura.Ver)
            .WithName("CompararModelos")
            .WithSummary("Comparar dos publicaciones del modelo de un proyecto")
            .WithDescription("Elementos, relaciones y vistas agregados, quitados o cambiados, hasta 500 por grupo. Las dos deben ser del mismo proyecto.");
    }

    internal static ResumenPublicacionArquitectura Resumen(PublicacionSinContenido p) => new(
        p.Id, ReglasArquitectura.Texto(p.Tipo), p.Canal, p.Cluster, p.Rama, p.CommitSha, p.VersionPropuesta, ReglasArquitectura.Texto(p.Estado),
        ReglasArquitectura.VisibilidadTexto(p.Visibilidad), p.HerramientaVersion, p.PublicadoEn);

    /// <summary>La publicación sin su contenido, que puede pesar megas: lo que necesitan las listas.</summary>
    internal sealed record PublicacionSinContenido(
        Guid Id, Guid ProyectoId, TipoPublicacion Tipo, string Canal, string Cluster, string Rama, string CommitSha, string? VersionPropuesta,
        EstadoPublicacion Estado, VisibilidadArquitectura Visibilidad, string? HerramientaVersion, DateTimeOffset PublicadoEn);

    internal static IQueryable<PublicacionSinContenido> SinContenido(IQueryable<PublicacionArquitectura> consulta) =>
        consulta.Select(p => new PublicacionSinContenido(
            p.Id, p.ProyectoId, p.Tipo, p.Canal, p.Cluster, p.Rama, p.CommitSha, p.VersionPropuesta, p.Estado, p.Visibilidad, p.HerramientaVersion, p.PublicadoEn));

    private static AplicativoEnlaceArquitectura? Enlace(IEnumerable<AplicativoLeido> aplicativos, Guid id) =>
        aplicativos.FirstOrDefault(a => a.Id == id) is { } a ? Enlace(a) : null;

    private static AplicativoEnlaceArquitectura Enlace(AplicativoLeido a) => new(a.Slug, a.Nombre, $"/catalogo/{a.Slug}");

    private static async Task<Ok<IReadOnlyList<ProyectoArquitecturaResumen>>> ListarProyectosAsync(
        ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        if (acceso.Niveles.Length == 0)
        {
            return TypedResults.Ok<IReadOnlyList<ProyectoArquitecturaResumen>>([]);
        }
        var modelos = acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.Tipo == TipoPublicacion.Modelo));
        // Lo más nuevo de cada proyecto y canal, sin traer el contenido.
        var vigentes = await SinContenido(modelos
                .Where(p => !modelos.Any(q => q.ProyectoId == p.ProyectoId && q.Canal == p.Canal && (q.PublicadoEn > p.PublicadoEn || (q.PublicadoEn == p.PublicadoEn && q.Id.CompareTo(p.Id) > 0)))))
            .ToListAsync(ct);
        var ids = vigentes.Select(v => v.ProyectoId).Distinct().ToList();
        var proyectos = await contexto.Proyectos.AsNoTracking().Where(p => ids.Contains(p.Id)).OrderBy(p => p.Codigo).ToListAsync(ct);
        var aplicativos = await lector.ListarAsync(ct);
        return TypedResults.Ok<IReadOnlyList<ProyectoArquitecturaResumen>>(
        [
            .. proyectos.Select(p => new ProyectoArquitecturaResumen(p.Codigo, Enlace(aplicativos, p.AplicativoId),
                [.. vigentes.Where(v => v.ProyectoId == p.Id).OrderBy(v => OrdenCanal(v.Canal)).Select(v => new CanalProyectoArquitectura(v.Canal, Resumen(v)))])),
        ]);
    }

    /// <summary>Primero lo que corre en producción.</summary>
    private static int OrdenCanal(string canal) => canal switch
    {
        "pdn" => 0,
        "qa" => 1,
        _ => 2,
    };

    private static async Task<Results<Ok<HistorialPublicacionesArquitectura>, NotFound>> HistorialAsync(
        string codigo, string? canal, DateTimeOffset? desde, DateTimeOffset? hasta,
        ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var proyecto = await contexto.Proyectos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (proyecto is null)
        {
            return TypedResults.NotFound();
        }
        var consulta = acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.ProyectoId == proyecto && p.Tipo == TipoPublicacion.Modelo));
        if (canal is not null)
        {
            consulta = consulta.Where(p => p.Canal == canal);
        }
        if (desde is { } d)
        {
            consulta = consulta.Where(p => p.PublicadoEn >= d);
        }
        if (hasta is { } h)
        {
            consulta = consulta.Where(p => p.PublicadoEn <= h);
        }
        var filas = await SinContenido(consulta.OrderByDescending(p => p.PublicadoEn).ThenByDescending(p => p.Id).Take(LimiteHistorial)).ToListAsync(ct);
        // Un proyecto del que la persona no ve nada no se revela.
        return filas.Count == 0 && !await acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.ProyectoId == proyecto && p.Tipo == TipoPublicacion.Modelo)).AnyAsync(ct)
            ? TypedResults.NotFound()
            : TypedResults.Ok(new HistorialPublicacionesArquitectura([.. filas.Select(Resumen)]));
    }

    private static async Task<Results<Ok<DetallePublicacionArquitectura>, NotFound>> VigenteAsync(
        string codigo, string? canal, DateTimeOffset? en,
        ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var proyecto = await contexto.Proyectos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (proyecto is null)
        {
            return TypedResults.NotFound();
        }
        var consulta = acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.ProyectoId == proyecto && p.Tipo == TipoPublicacion.Modelo && p.Canal == (canal ?? "pdn")));
        if (en is { } fecha)
        {
            consulta = consulta.Where(p => p.PublicadoEn <= fecha);
        }
        var fila = await SinContenido(consulta.OrderByDescending(p => p.PublicadoEn).ThenByDescending(p => p.Id)).FirstOrDefaultAsync(ct);
        return fila is null ? TypedResults.NotFound() : TypedResults.Ok(await DetalleDeAsync(contexto, fila, codigo, acceso, ct));
    }

    private static async Task<DetallePublicacionArquitectura> DetalleDeAsync(ArquitecturaDbContext contexto, PublicacionSinContenido p, string codigo, AccesoArquitectura acceso, CancellationToken ct)
    {
        var vistas = await contexto.Vistas.AsNoTracking().Where(v => v.PublicacionId == p.Id && (acceso.VeDespliegue || v.Tipo != "despliegue"))
            .OrderBy(v => v.VistaId).Select(v => new VistaArquitecturaResumen(v.VistaId, v.Tipo, v.Categoria, v.Titulo)).ToListAsync(ct);
        return new DetallePublicacionArquitectura(codigo, Resumen(p), vistas);
    }

    private static async Task<Results<Ok<DetallePublicacionArquitectura>, NotFound>> DetalleAsync(
        Guid id, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var fila = await SinContenido(acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.Id == id && p.Tipo == TipoPublicacion.Modelo))).FirstOrDefaultAsync(ct);
        if (fila is null)
        {
            return TypedResults.NotFound();
        }
        var codigo = await contexto.Proyectos.AsNoTracking().Where(p => p.Id == fila.ProyectoId).Select(p => p.Codigo).FirstAsync(ct);
        return TypedResults.Ok(await DetalleDeAsync(contexto, fila, codigo, acceso, ct));
    }

    private static async Task<IResult> ModeloAsync(
        Guid id, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var contenido = await acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.Id == id && p.Tipo == TipoPublicacion.Modelo)).Select(p => p.Contenido).FirstOrDefaultAsync(ct);
        return contenido is null
            ? Results.NotFound()
            : Results.Text(acceso.VeDespliegue ? contenido : ReglasArquitectura.SinDespliegue(contenido), "application/json");
    }

    private static async Task<Results<Ok<FichaElementoArquitectura>, NotFound>> FichaAsync(
        Guid id, string elementoId, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorContratos contratos, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var publicacion = await SinContenido(acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.Id == id && p.Tipo == TipoPublicacion.Modelo))).FirstOrDefaultAsync(ct);
        var elemento = publicacion is null
            ? null
            : await contexto.Elementos.AsNoTracking().FirstOrDefaultAsync(e => e.PublicacionId == id && e.ElementoId == elementoId, ct);
        if (publicacion is null || elemento is null)
        {
            return TypedResults.NotFound();
        }

        var metadatos = JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(elemento.Metadatos) ?? [];
        var enlaces = JsonSerializer.Deserialize<List<EnlaceFichaArquitectura>>(elemento.Enlaces, JsonSerializerOptions.Web) ?? [];
        var leido = new ElementoLeido(elemento.ElementoId, elemento.Kind, elemento.Titulo, elemento.Descripcion, elemento.Tecnologia, elemento.Etiquetas, metadatos, []);
        var proyecto = await contexto.Proyectos.AsNoTracking().FirstAsync(p => p.Id == publicacion.ProyectoId, ct);
        var aplicativos = await lector.ListarAsync(ct);
        // El repo se hereda del sistema que lo contiene (plan 01 §8.4: repo va en el system, repo_path en el container).
        var sistemaId = elemento.ElementoId.Split('.')[0];
        var repoHeredado = sistemaId == elemento.ElementoId
            ? null
            : await contexto.Elementos.AsNoTracking().Where(e => e.PublicacionId == id && e.ElementoId == sistemaId).Select(e => e.Metadatos).FirstOrDefaultAsync(ct) is { } delSistema
                ? (JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(delSistema)?.GetValueOrDefault("repo") is { Count: > 0 } r ? r[0] : null)
                : null;
        var aplicativo = ReglasArquitectura.ResolverAplicativo(leido, repoHeredado, proyecto.Repositorio, proyecto.AplicativoId, aplicativos);

        var deContrato = new List<ContratoElementoArquitectura>();
        foreach (var tipo in new[] { "openapi", "asyncapi", "proto" })
        {
            foreach (var ruta in metadatos.GetValueOrDefault(tipo) ?? [])
            {
                var contrato = acceso.VeContratos && ReglasArquitectura.CodigoContrato(ruta) is { } codigo ? await contratos.PorCodigoAsync(codigo, ct) : null;
                deContrato.Add(new ContratoElementoArquitectura(ruta, tipo, contrato?.Nombre, contrato?.UltimaVersion, contrato is null ? null : $"/contratos/{contrato.Id}"));
            }
        }
        var adr = (metadatos.GetValueOrDefault("adr") ?? []).SelectMany(a => a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(a => new AdrElementoArquitectura(a, $"/documentacion?texto={Uri.EscapeDataString(a)}")).ToList();
        int? drift = acceso.VeDespliegue
            ? await acceso.Filtrar(contexto.Hallazgos.AsNoTracking()).CountAsync(h => h.ElementoId == elementoId && h.Estado == EstadoHallazgo.Abierto, ct)
            : null;
        return TypedResults.Ok(new FichaElementoArquitectura(
            elemento.ElementoId, elemento.Kind, elemento.Titulo, elemento.Descripcion, elemento.Tecnologia, elemento.Etiquetas, metadatos,
            aplicativo is null ? null : Enlace(aplicativo), deContrato, adr, enlaces, drift));
    }

    private static async Task<Results<Ok<DiffArquitectura>, NotFound>> DiffAsync(
        Guid a, Guid b, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var filas = await acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => (p.Id == a || p.Id == b) && p.Tipo == TipoPublicacion.Modelo))
            .Select(p => new { p.Id, p.ProyectoId, p.Contenido }).ToListAsync(ct);
        var (antes, despues) = (filas.FirstOrDefault(f => f.Id == a), filas.FirstOrDefault(f => f.Id == b));
        return antes is null || despues is null || antes.ProyectoId != despues.ProyectoId
            ? TypedResults.NotFound()
            : TypedResults.Ok(ReglasArquitectura.DiffModelos(antes.Contenido, despues.Contenido, acceso.VeDespliegue));
    }
}
