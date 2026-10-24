using System.ComponentModel;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>
/// Lectura interna de los contratos en <c>/api/v1/contratos</c> (HU 3.2, plan 00 §8.5): listado (el consolidado de todos
/// los aplicativos), versiones, spec de una versión e índice de operaciones. Todo exige
/// <c>atlas.contratos.ver_internos</c>. Los canales por ambiente (HU 3.4) y el diff (HU 3.5) llegan después.
/// </summary>
internal static class EndpointsContratos
{
    public const int LimiteListado = 500;

    private const int TerminosMaximos = 5;

    [Description("Aplicativo del catálogo al que pertenece el contrato.")]
    public sealed record AplicativoDelContrato(
        Guid Id,
        [property: Description("Slug del aplicativo; vacío si ya no está en el catálogo.")] string? Slug,
        [property: Description("Nombre del aplicativo; vacío si ya no está en el catálogo.")] string? Nombre);

    [Description("Resumen de una versión.")]
    public sealed record VersionResumen(
        Guid Id,
        string Version,
        bool EsEstable,
        [property: Description("SHA-256 del spec (hexadecimal).")] string Sha256,
        [property: Description("json, yaml o proto.")] string Formato,
        [property: Description("Bytes del spec.")] int Tamano,
        [property: Description("Repositorio de GitHub (dueño/nombre).")] string Repositorio,
        string Rama,
        [property: Description("Commit del que salió.")] string Commit,
        [property: Description("Workflow que llamó (.github/workflows/<nombre>.yml), si se conoce.")] string? Workflow,
        [property: Description("Workflow reutilizable que publicó, con su referencia (job_workflow_ref).")] string WorkflowDelJob,
        [property: Description("run_id de GitHub Actions.")] string? Ejecucion,
        [property: Description("github:dueño/repo@rama.")] string PublicadoPor,
        DateTimeOffset PublicadoEn,
        [property: Description("Si el changelog de la CI trae cambios que rompen compatibilidad.")] bool TieneBreaking,
        [property: Description("vigente, deprecada o retirada.")] string Estado,
        [property: Description("Fecha de retiro, si está deprecada.")] DateOnly? RetiroEn);

    [Description("Contrato de un aplicativo con su última versión publicada.")]
    public sealed record ContratoResumen(
        Guid Id,
        [property: Description("Código del contrato dentro del aplicativo.")] string Codigo,
        [property: Description("info.title de la última versión, o el paquete en proto.")] string Nombre,
        string? Descripcion,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        [property: Description("interna o compartible.")] string Visibilidad,
        AplicativoDelContrato Aplicativo,
        [property: Description("Repositorio de la última publicación.")] string Repositorio,
        [property: Description("Cuántas versiones tiene.")] int Versiones,
        [property: Description("La última publicada (por fecha).")] VersionResumen? UltimaVersion,
        [property: Description("La última estable, si la hay.")] VersionResumen? UltimaEstable);

    [Description("Contratos que cumplen el filtro, ordenados por aplicativo y código.")]
    public sealed record ListadoContratos(
        [property: Description("Cuántos cumplen el filtro (el listado trae a lo sumo 500).")] int Total,
        IReadOnlyList<ContratoResumen> Elementos);

    [Description("Operación, webhook, canal o rpc del índice de una versión.")]
    public sealed record OperacionDto(
        [property: Description("GET /ruta, POST webhook:<nombre>, send <canal> o rpc:<paquete>.<Servicio>/<Método>.")] string Clave,
        [property: Description("operacion, webhook, canal o rpc.")] string Clase,
        [property: Description("Método HTTP, acción de AsyncAPI o rpc.")] string? Metodo,
        [property: Description("Ruta, webhook, dirección del canal o servicio.")] string? Ruta,
        string? OperationId,
        string? Resumen,
        IReadOnlyList<string> Etiquetas,
        [property: Description("x-public: true o la etiqueta public.")] bool Publica);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("", ListarAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarContratos")
            .WithSummary("Contratos de todos los aplicativos")
            .WithDescription($"El consolidado interno: contratos con su aplicativo, su última versión y su última estable, filtrados por aplicativo, tipo y texto (nombre, código o descripción, sin distinguir tildes ni mayúsculas). Máximo {LimiteListado}. Requiere atlas.contratos.ver_internos.");

        grupo.MapGet("/{id:guid}/versiones", VersionesAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarVersionesContrato")
            .WithSummary("Versiones de un contrato")
            .WithDescription("Todas las versiones publicadas, de la más reciente a la más antigua, con su hash y su procedencia (repo, rama, commit, workflow y ejecución). 404 si el contrato no existe. Requiere atlas.contratos.ver_internos.");

        grupo.MapGet("/{id:guid}/versiones/{version}/spec", SpecAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerSpecVersionContrato")
            .WithSummary("Spec de una versión")
            .Produces<string>(StatusCodes.Status200OK, "application/json", "application/yaml", "text/plain")
            .WithDescription("El spec completo tal como se publicó (JSON, YAML o proto), inmutable; el ETag es su SHA-256. Solo para uso interno: lo compartible con clientes irá podado (F4). 404 si no existe. Requiere atlas.contratos.ver_internos.");

        grupo.MapGet("/{id:guid}/versiones/{version}/operaciones", OperacionesAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarOperacionesVersionContrato")
            .WithSummary("Índice de operaciones de una versión")
            .WithDescription("Operaciones y webhooks (OpenAPI), canales y operaciones (AsyncAPI) o rpc (proto) de la versión, ordenados por clave. 404 si no existe. Requiere atlas.contratos.ver_internos.");
    }

    private static async Task<Results<Ok<ListadoContratos>, ValidationProblem>> ListarAsync(
        [Description("Slug del aplicativo.")] string? aplicativo,
        [Description("openapi, asyncapi o proto.")] string? tipo,
        [Description("Texto a buscar en el nombre, el código o la descripción.")] string? texto,
        ContratosDbContext contexto, ILectorAplicativos lector, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        TipoContrato? filtroTipo = null;
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            filtroTipo = Valores.Tipo(tipo);
            if (filtroTipo is null)
            {
                errores["tipo"] = [$"Valores válidos: {string.Join(", ", Valores.Tipos.Keys)}."];
            }
        }
        if ((texto?.Length ?? 0) > 200)
        {
            errores["texto"] = ["El texto de búsqueda va hasta 200 caracteres."];
        }
        if (errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }

        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var consulta = contexto.Contratos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(aplicativo))
        {
            var ids = aplicativos.Values.Where(a => a.Slug == aplicativo.Trim()).Select(a => a.Id).ToList();
            consulta = consulta.Where(c => ids.Contains(c.AplicativoId));
        }
        if (filtroTipo is { } t)
        {
            consulta = consulta.Where(c => c.Tipo == t);
        }
        consulta = FiltrarTexto(consulta, texto);
        var contratos = await consulta.ToListAsync(ct);
        var idsContratos = contratos.Select(c => c.Id).ToList();
        var versiones = (await SinContenido(contexto.Versiones.AsNoTracking().Where(v => idsContratos.Contains(v.ContratoId))).ToListAsync(ct))
            .ToLookup(v => v.ContratoId);

        var elementos = contratos
            .Select(c => Resumen(c, aplicativos.GetValueOrDefault(c.AplicativoId), versiones[c.Id].ToList()))
            .OrderBy(c => c.Aplicativo.Slug ?? "~", StringComparer.Ordinal)
            .ThenBy(c => c.Codigo, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new ListadoContratos(elementos.Count, [.. elementos.Take(LimiteListado)]));
    }

    internal static IQueryable<Contrato> FiltrarTexto(IQueryable<Contrato> consulta, string? texto)
    {
        foreach (var termino in FuncionesSql.NormalizarBusqueda(texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Distinct(StringComparer.Ordinal).Take(TerminosMaximos))
        {
            var patron = FuncionesSql.PatronContiene(termino);
            consulta = consulta.Where(c =>
                EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(c.Nombre, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(c.Codigo, patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(c.Descripcion ?? string.Empty, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike));
        }
        return consulta;
    }

    private static async Task<Results<Ok<List<VersionResumen>>, NotFound>> VersionesAsync(
        [Description("Id del contrato.")] Guid id, ContratosDbContext contexto, CancellationToken ct)
    {
        if (!await contexto.Contratos.AnyAsync(c => c.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        var versiones = await SinContenido(contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == id)
            .OrderByDescending(v => v.PublicadoEn).ThenByDescending(v => v.Id)).ToListAsync(ct);
        return TypedResults.Ok(versiones.Select(Version).ToList());
    }

    private static async Task<Results<ContentHttpResult, NotFound>> SpecAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("Versión semántica (3.3.0, 3.4.0-dev.828c9fe).")] string version,
        HttpContext http, ContratosDbContext contexto, CancellationToken ct)
    {
        var fila = await contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == id && v.Version == version)
            .Select(v => new { v.Contenido, v.Formato, v.Sha256 }).FirstOrDefaultAsync(ct);
        if (fila is null)
        {
            return TypedResults.NotFound();
        }
        http.Response.Headers.ETag = $"\"{fila.Sha256}\"";
        var tipo = fila.Formato switch
        {
            FormatoSpec.Json => "application/json",
            FormatoSpec.Yaml => "application/yaml",
            _ => "text/plain",
        };
        return TypedResults.Text(fila.Contenido, tipo, System.Text.Encoding.UTF8);
    }

    private static async Task<Results<Ok<List<OperacionDto>>, NotFound>> OperacionesAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("Versión semántica.")] string version,
        ContratosDbContext contexto, CancellationToken ct)
    {
        var versionId = await contexto.Versiones.Where(v => v.ContratoId == id && v.Version == version).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        if (versionId is null)
        {
            return TypedResults.NotFound();
        }
        var operaciones = await contexto.Operaciones.AsNoTracking().Where(o => o.VersionId == versionId).ToListAsync(ct);
        return TypedResults.Ok(operaciones.OrderBy(o => o.Clave, StringComparer.Ordinal)
            .Select(o => new OperacionDto(o.Clave, o.Clase, o.Metodo, o.Ruta, o.OperationId, o.Resumen, o.Etiquetas, o.Publica)).ToList());
    }

    /// <summary>Una versión sin el spec (que puede pesar megas): lo que necesitan los listados.</summary>
    internal sealed record VersionContratoSinContenido(
        Guid Id, Guid ContratoId, string Version, bool EsEstable, string Sha256, FormatoSpec Formato, int Tamano, string Repositorio, string Rama,
        string CommitSha, string? Workflow, string WorkflowDelJob, string? Ejecucion, string PublicadoPor, DateTimeOffset PublicadoEn, bool TieneBreaking,
        EstadoVersion Estado, DateOnly? RetiroEn);

    private static IQueryable<VersionContratoSinContenido> SinContenido(IQueryable<VersionContrato> consulta) =>
        consulta.Select(v => new VersionContratoSinContenido(v.Id, v.ContratoId, v.Version, v.EsEstable, v.Sha256, v.Formato, v.Tamano, v.Repositorio,
            v.Rama, v.CommitSha, v.Workflow, v.WorkflowDelJob, v.Ejecucion, v.PublicadoPor, v.PublicadoEn, v.TieneBreaking, v.Estado, v.RetiroEn));

    private static VersionResumen Version(VersionContratoSinContenido v) => new(
        v.Id, v.Version, v.EsEstable, v.Sha256, Valores.Texto(v.Formato), v.Tamano, v.Repositorio, v.Rama, v.CommitSha, v.Workflow, v.WorkflowDelJob,
        v.Ejecucion, v.PublicadoPor, v.PublicadoEn, v.TieneBreaking, Valores.Texto(v.Estado), v.RetiroEn);

    private static ContratoResumen Resumen(Contrato c, AplicativoLeido? aplicativo, List<VersionContratoSinContenido> versiones)
    {
        var ordenadas = versiones.OrderByDescending(v => v.PublicadoEn).ThenByDescending(v => v.Id).ToList();
        var ultima = ordenadas.FirstOrDefault();
        var estable = ordenadas.FirstOrDefault(v => v.EsEstable);
        return new ContratoResumen(c.Id, c.Codigo, c.Nombre, c.Descripcion, Valores.Texto(c.Tipo), Valores.Texto(c.Visibilidad),
            new AplicativoDelContrato(c.AplicativoId, aplicativo?.Slug, aplicativo?.Nombre), c.Repositorio, versiones.Count,
            ultima is null ? null : Version(ultima), estable is null ? null : Version(estable));
    }
}
