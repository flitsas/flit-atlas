using System.ComponentModel;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Render;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Canales;

/// <summary>
/// <c>GET /api/v1/contratos/consolidado</c> (HU 3.4, plan 00 §8.9, pantalla «Consolidado»): por contrato, la versión de
/// cada ambiente, los desfases entre ambientes vecinos (qa adelante de pdn, sin versión en pdn…) y los breaking
/// pendientes entre la versión de pdn y la de qa. Los breaking salen del changelog que guardó la CI al publicar la de qa
/// cuando se calculó contra la misma versión que hoy corre en pdn; si no, de atlas-render (<see cref="IRenderDiff"/>, que
/// cachea por el par de hashes). Requiere <c>atlas.contratos.ver_internos</c>.
/// </summary>
internal static partial class EndpointsConsolidado
{
    /// <summary>Diffs a atlas-render en paralelo (los demás esperan turno).</summary>
    private const int DiffsALaVez = 4;

    [Description("Versión de un contrato en un ambiente; vacía si nunca se desplegó allí.")]
    public sealed record CanalConsolidado(
        string Ambiente,
        string? Version,
        bool? EsEstable,
        DateTimeOffset? DesplegadoEn);

    [Description("Diferencia entre dos ambientes vecinos en el orden de promoción (dev → qa → pdn).")]
    public sealed record Desfase(
        [property: Description("El ambiente al que se refiere el aviso.")] string Ambiente,
        [property: Description("El ambiente con el que se compara.")] string RespectoDe,
        [property: Description("adelante (lleva una versión mayor), atrasado (una menor) o ausente (no tiene versión y el anterior sí).")] string Tipo,
        string Mensaje);

    [Description("Un cambio incompatible (nivel error de oasdiff) entre la versión de pdn y la de qa.")]
    public sealed record CambioBreaking(
        [property: Description("Id de la regla de oasdiff (api-removed-without-deprecation…).")] string Regla,
        string Texto,
        string? Metodo,
        string? Ruta);

    [Description("Breaking pendientes: lo que rompería pdn si se promueve la versión de qa.")]
    public sealed record BreakingPendientes(
        [property: Description("Versión en pdn.")] string Desde,
        [property: Description("Versión en qa.")] string Hasta,
        [property: Description("sin_breaking, con_breaking, no_disponible (atlas-render no respondió o una spec no es válida) o no_soportado (AsyncAPI y proto sin changelog que sirva).")] string Estado,
        [property: Description("changelog (el que guardó la CI al publicar) o render (calculado ahora); vacío si no se pudo calcular.")] string? Fuente,
        IReadOnlyList<CambioBreaking> Cambios);

    [Description("Un contrato con su versión por ambiente.")]
    public sealed record ContratoConsolidado(
        Guid Id,
        string Codigo,
        string Nombre,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        EndpointsContratos.AplicativoDelContrato Aplicativo,
        [property: Description("Uno por ambiente, en el orden de «ambientes».")] IReadOnlyList<CanalConsolidado> Canales,
        IReadOnlyList<Desfase> Desfases,
        [property: Description("Vacío si qa o pdn no tienen versión o tienen la misma versión base (X.Y.Z).")] BreakingPendientes? BreakingPendientes);

    [Description("El consolidado de contratos por ambiente.")]
    public sealed record ConsolidadoContratos(
        [property: Description("Ambientes en orden de promoción (dev, qa, pdn).")] IReadOnlyList<string> Ambientes,
        [property: Description("Cuántos contratos devuelve (después de todos los filtros; a lo sumo 500).")] int Total,
        [property: Description("Cuántos de los que cumplen aplicativo, tipo y texto tienen algún desfase.")] int ConDesfases,
        [property: Description("Cuántos de los que cumplen aplicativo, tipo y texto tienen breaking pendientes.")] int ConBreaking,
        IReadOnlyList<ContratoConsolidado> Elementos);

    public static void Mapear(RouteGroupBuilder grupo) =>
        grupo.MapGet("/consolidado", ConsolidadoAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerConsolidadoContratos")
            .WithSummary("Consolidado de contratos por ambiente")
            .WithDescription($"Por contrato, la versión de cada ambiente (dev, qa, pdn), los desfases entre ambientes vecinos y los breaking pendientes entre pdn y qa (del changelog guardado al publicar o calculados por atlas-render). Filtros por aplicativo, tipo, texto y solo con desfases. Máximo {EndpointsContratos.LimiteListado}. Requiere atlas.contratos.ver_internos.");

    private static async Task<Results<Ok<ConsolidadoContratos>, ValidationProblem>> ConsolidadoAsync(
        [Description("Slug del aplicativo.")] string? aplicativo,
        [Description("openapi, asyncapi o proto.")] string? tipo,
        [Description("Texto a buscar en el nombre, el código o la descripción.")] string? texto,
        [Description("true: solo los contratos con algún desfase.")] bool? desfases,
        ContratosDbContext contexto, ILectorAplicativos lector, IRenderDiff render, IOptionsMonitor<OpcionesCanales> opciones, ILoggerFactory logs,
        CancellationToken ct)
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

        var o = opciones.CurrentValue;
        var ambientes = o.Nombres;
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
        var contratos = await EndpointsContratos.FiltrarTexto(consulta, texto).ToListAsync(ct);
        var idsContratos = contratos.Select(c => c.Id).ToList();
        var canales = (await contexto.Canales.AsNoTracking().Where(c => idsContratos.Contains(c.ContratoId))
                .Join(contexto.Versiones.AsNoTracking(), c => c.VersionId, v => v.Id,
                    (c, v) => new FilaCanal(c.ContratoId, c.Ambiente, v.Id, v.Version, v.EsEstable, c.DesplegadoEn, v.ChangelogBase, v.Changelog))
                .ToListAsync(ct))
            .ToLookup(c => c.ContratoId);

        var log = logs.CreateLogger(typeof(EndpointsConsolidado));
        var elementos = new List<ContratoConsolidado>(contratos.Count);
        var pendientes = new List<(int Indice, Contrato Contrato, FilaCanal Referencia, FilaCanal Candidato)>();
        foreach (var c in contratos)
        {
            var delContrato = canales[c.Id].ToDictionary(x => x.Ambiente, StringComparer.Ordinal);
            var porAmbiente = ambientes.Select(a => delContrato.GetValueOrDefault(a)).ToList();
            var referencia = delContrato.GetValueOrDefault(o.Referencia);
            var candidato = delContrato.GetValueOrDefault(o.Candidato);
            BreakingPendientes? breaking = null;
            if (referencia is not null && candidato is not null && Nucleo(referencia.Version) != Nucleo(candidato.Version))
            {
                breaking = DelChangelog(c, referencia, candidato);
                if (breaking is null)
                {
                    pendientes.Add((elementos.Count, c, referencia, candidato));
                }
            }
            var app = aplicativos.GetValueOrDefault(c.AplicativoId);
            elementos.Add(new ContratoConsolidado(c.Id, c.Codigo, c.Nombre, Valores.Texto(c.Tipo),
                new EndpointsContratos.AplicativoDelContrato(c.AplicativoId, app?.Slug, app?.Nombre),
                [.. ambientes.Select((a, i) => new CanalConsolidado(a, porAmbiente[i]?.Version, porAmbiente[i]?.EsEstable, porAmbiente[i]?.DesplegadoEn))],
                Desfases(ambientes, porAmbiente), breaking));
        }

        // Lo que el changelog guardado no responde se calcula en atlas-render (solo OpenAPI).
        if (pendientes.Count > 0)
        {
            var idsSpecs = pendientes.SelectMany(p => new[] { p.Referencia.VersionId, p.Candidato.VersionId }).Distinct().ToList();
            var specs = await contexto.Versiones.AsNoTracking().Where(v => idsSpecs.Contains(v.Id)).Select(v => new { v.Id, v.Contenido })
                .ToDictionaryAsync(v => v.Id, v => v.Contenido, ct);
            using var turnos = new SemaphoreSlim(DiffsALaVez);
            var calculados = await Task.WhenAll(pendientes.Select(async p =>
            {
                await turnos.WaitAsync(ct);
                try
                {
                    return (p.Indice, Breaking: await DeRenderAsync(render, p.Contrato, p.Referencia, p.Candidato, specs, log, ct));
                }
                finally
                {
                    turnos.Release();
                }
            }));
            foreach (var (indice, breaking) in calculados)
            {
                elementos[indice] = elementos[indice] with { BreakingPendientes = breaking };
            }
        }

        var ordenados = elementos
            .OrderBy(c => c.Aplicativo.Slug ?? "~", StringComparer.Ordinal)
            .ThenBy(c => c.Codigo, StringComparer.Ordinal)
            .ToList();
        var conDesfases = ordenados.Count(c => c.Desfases.Count > 0);
        var conBreaking = ordenados.Count(c => c.BreakingPendientes?.Estado == EstadoConBreaking);
        var filtrados = desfases == true ? ordenados.Where(c => c.Desfases.Count > 0).ToList() : ordenados;
        return TypedResults.Ok(new ConsolidadoContratos(ambientes, Math.Min(filtrados.Count, EndpointsContratos.LimiteListado), conDesfases, conBreaking,
            [.. filtrados.Take(EndpointsContratos.LimiteListado)]));
    }

    private const string EstadoConBreaking = "con_breaking";

    internal sealed record FilaCanal(
        Guid ContratoId, string Ambiente, Guid VersionId, string Version, bool EsEstable, DateTimeOffset DesplegadoEn, string? ChangelogBase,
        string Changelog);

    /// <summary>
    /// Desfases entre vecinos del orden de promoción. Se comparan las versiones base (X.Y.Z): <c>3.3.0-rc.a1b2c3d</c> en qa
    /// y <c>3.3.0</c> en pdn son la misma entrega promovida, no un desfase.
    /// </summary>
    internal static IReadOnlyList<Desfase> Desfases(IReadOnlyList<string> ambientes, IReadOnlyList<FilaCanal?> porAmbiente)
    {
        var desfases = new List<Desfase>();
        for (var i = 0; i + 1 < ambientes.Count; i++)
        {
            var (anterior, siguiente) = (porAmbiente[i], porAmbiente[i + 1]);
            if (anterior is null)
            {
                continue;
            }
            if (siguiente is null)
            {
                desfases.Add(new Desfase(ambientes[i + 1], ambientes[i], "ausente",
                    $"Sin versión en {ambientes[i + 1]} ({ambientes[i]} tiene {anterior.Version})."));
                continue;
            }
            var comparacion = Comparar(anterior.Version, siguiente.Version);
            if (comparacion > 0)
            {
                desfases.Add(new Desfase(ambientes[i], ambientes[i + 1], "adelante",
                    $"{ambientes[i]} ({anterior.Version}) va adelante de {ambientes[i + 1]} ({siguiente.Version})."));
            }
            else if (comparacion < 0)
            {
                desfases.Add(new Desfase(ambientes[i], ambientes[i + 1], "atrasado",
                    $"{ambientes[i]} ({anterior.Version}) va atrás de {ambientes[i + 1]} ({siguiente.Version})."));
            }
        }
        return desfases;
    }

    /// <summary>Compara las versiones base (X.Y.Z), sin el prerelease.</summary>
    internal static int Comparar(string a, string b)
    {
        var (x, y) = (VersionSemantica.Leer(a), VersionSemantica.Leer(b));
        if (x is null || y is null)
        {
            return string.CompareOrdinal(a, b);
        }
        return (x.Mayor, x.Menor, x.Parche).CompareTo((y.Mayor, y.Menor, y.Parche));
    }

    private static string? Nucleo(string version) => VersionSemantica.Leer(version)?.Nucleo ?? version;

    /// <summary>
    /// Del changelog que la CI guardó al publicar la versión candidata, si se calculó contra la que hoy corre en el
    /// ambiente de referencia. <c>null</c> si no sirve (otra base o sin base) y hay que calcularlo.
    /// </summary>
    internal static BreakingPendientes? DelChangelog(Contrato contrato, FilaCanal referencia, FilaCanal candidato)
    {
        if (!string.Equals(candidato.ChangelogBase, referencia.Version, StringComparison.Ordinal))
        {
            return contrato.Tipo == TipoContrato.Openapi
                ? null
                : new BreakingPendientes(referencia.Version, candidato.Version, "no_soportado", null, []);
        }
        var cambios = new List<CambioBreaking>();
        try
        {
            using var documento = JsonDocument.Parse(candidato.Changelog, new JsonDocumentOptions { MaxDepth = 64 });
            if (documento.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in documento.RootElement.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("level", out var nivel) && nivel.ValueKind == JsonValueKind.Number
                        && nivel.TryGetInt32(out var n) && n >= 3)
                    {
                        cambios.Add(new CambioBreaking(Texto(c, "id") ?? "desconocida", Texto(c, "text") ?? string.Empty, Texto(c, "operation"), Texto(c, "path")));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // La publicación ya validó el changelog; si aun así no se lee, se calcula en atlas-render.
            return contrato.Tipo == TipoContrato.Openapi ? null : new BreakingPendientes(referencia.Version, candidato.Version, "no_disponible", null, []);
        }
        return new BreakingPendientes(referencia.Version, candidato.Version, cambios.Count > 0 ? EstadoConBreaking : "sin_breaking", "changelog", cambios);
    }

    private static string? Texto(JsonElement objeto, string propiedad) =>
        objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static async Task<BreakingPendientes> DeRenderAsync(
        IRenderDiff render, Contrato contrato, FilaCanal referencia, FilaCanal candidato, Dictionary<Guid, string> specs, ILogger log,
        CancellationToken ct)
    {
        try
        {
            var resultado = await render.CalcularAsync(
                new SolicitudDiff(TiposContrato.OpenApi, specs[referencia.VersionId], specs[candidato.VersionId], IdiomasDiff.Espanol), ct);
            var cambios = resultado.Breaking.Where(b => b.Nivel == NivelesCambio.Error)
                .Select(b => new CambioBreaking(b.Regla, b.Texto, b.Metodo, b.Ruta)).ToList();
            return new BreakingPendientes(referencia.Version, candidato.Version, cambios.Count > 0 ? EstadoConBreaking : "sin_breaking", "render", cambios);
        }
        catch (RenderDiffException ex)
        {
            DiffFallido(log, contrato.Codigo, ex.Motivo.ToString());
            return new BreakingPendientes(referencia.Version, candidato.Version,
                ex.Motivo == MotivoErrorDiff.NoSoportado ? "no_soportado" : "no_disponible", null, []);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consolidado de contratos: no se pudo calcular el diff de {Contrato} ({Motivo})")]
    private static partial void DiffFallido(ILogger log, string contrato, string motivo);
}
