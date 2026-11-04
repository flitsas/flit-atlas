using System.ComponentModel;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Render;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>
/// Lo que pide la pantalla del contrato (HU 3.6, plan 00 §8.5 y §8.9): la ficha de un contrato y el diff entre dos de sus
/// versiones. El diff lo calcula atlas-render con oasdiff (<see cref="IRenderDiff"/>, ATLAS-ADR-0036), que lo cachea por
/// el SHA-256 de las dos specs: aquí no se guarda nada. Todo exige <c>atlas.contratos.ver_internos</c>.
/// </summary>
internal static partial class EndpointsVistaContrato
{
    [Description("Un cambio entre dos versiones, como lo reporta oasdiff.")]
    public sealed record CambioDiff(
        [property: Description("Id de la regla de oasdiff (api-path-removed-without-deprecation…).")] string Regla,
        [property: Description("error, warning o info. error y warning rompen compatibilidad.")] string Nivel,
        [property: Description("Si rompe compatibilidad (está en oasdiff breaking).")] bool Breaking,
        [property: Description("Texto del cambio, en español cuando oasdiff lo trae traducido.")] string Texto,
        string? Comentario,
        string? Metodo,
        string? Ruta,
        string? OperationId,
        [property: Description("paths, components, webhooks…")] string? Seccion);

    [Description("Diff entre dos versiones de un contrato: primero los cambios que rompen compatibilidad, del más grave al menos.")]
    public sealed record DiffContrato(
        Guid ContratoId,
        [property: Description("Versión base.")] string Desde,
        [property: Description("Versión que se compara contra la base.")] string Hasta,
        [property: Description("Motor y versión, p. ej. oasdiff v1.33.0.")] string Motor,
        [property: Description("oasdiff no encontró diferencias estructurales.")] bool SinDiferencias,
        ResumenDiff Resumen,
        IReadOnlyList<CambioDiff> Cambios,
        [property: Description("Changelog legible en Markdown.")] string Changelog,
        [property: Description("La respuesta salió de la caché de atlas-render.")] bool DesdeCache);

    [Description("Por qué no se pudo calcular el diff, con un código estable.")]
    public sealed record ErrorDiff(
        [property: Description("diff_no_soportado, spec_invalida, diff_rechazado (422); render_no_disponible (503).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/{id:guid}", ContratoAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ObtenerContrato")
            .WithSummary("Un contrato")
            .WithDescription("El contrato con su aplicativo, su última versión y su última estable (lo mismo que una fila del listado). 404 si no existe. Requiere atlas.contratos.ver_internos.");

        grupo.MapGet("/{id:guid}/diff", DiffAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("DiffContrato")
            .WithSummary("Diff entre dos versiones")
            .WithDescription("Cambios y changelog entre la versión desde (base) y la versión hasta, calculados por atlas-render con oasdiff (la misma configuración de la CLI del CI) y cacheados allá por el SHA-256 de las dos specs. Los cambios que rompen compatibilidad van primero, del nivel error al info. 400 si falta una versión; 404 si el contrato o una versión no existe; 422 si el tipo no tiene diff (AsyncAPI y proto, por ahora), si una spec no es válida o si atlas-render la rechaza; 503 si atlas-render no responde. Requiere atlas.contratos.ver_internos.")
            .Produces<DiffContrato>()
            .Produces<ErrorDiff>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDiff>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<EndpointsContratos.ContratoResumen>, NotFound>> ContratoAsync(
        [Description("Id del contrato.")] Guid id, ContratosDbContext contexto, ILectorAplicativos lector, CancellationToken ct)
    {
        var contrato = await contexto.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contrato is null)
        {
            return TypedResults.NotFound();
        }
        var versiones = await EndpointsContratos.SinContenido(contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == id)).ToListAsync(ct);
        var aplicativo = (await lector.ListarAsync(ct)).FirstOrDefault(a => a.Id == contrato.AplicativoId);
        return TypedResults.Ok(EndpointsContratos.Resumen(contrato, aplicativo, versiones));
    }

    private static async Task<IResult> DiffAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("Versión base (la anterior).")] string? desde,
        [Description("Versión a comparar (la nueva).")] string? hasta,
        HttpContext http, ContratosDbContext contexto, IRenderDiff render, ILoggerFactory logs, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(desde))
        {
            errores["desde"] = ["Indica la versión base."];
        }
        if (string.IsNullOrWhiteSpace(hasta))
        {
            errores["hasta"] = ["Indica la versión a comparar."];
        }
        if (errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }

        var tipo = await contexto.Contratos.Where(c => c.Id == id).Select(c => (TipoContrato?)c.Tipo).FirstOrDefaultAsync(ct);
        if (tipo is null)
        {
            return TypedResults.NotFound();
        }
        var textoTipo = Valores.Texto(tipo.Value);
        // Antes de leer las specs: si el tipo no tiene diff, no vale la pena traerlas ni llamar a atlas-render.
        if (!TiposContrato.ConDiff.Contains(textoTipo))
        {
            return NoProcesable("diff_no_soportado", $"El diff todavía no está disponible para contratos {textoTipo}; por ahora solo para OpenAPI.");
        }

        var specs = await contexto.Versiones.AsNoTracking()
            .Where(v => v.ContratoId == id && (v.Version == desde!.Trim() || v.Version == hasta!.Trim()))
            .Select(v => new { v.Version, v.Contenido }).ToListAsync(ct);
        var baseSpec = specs.FirstOrDefault(v => v.Version == desde!.Trim());
        var revision = specs.FirstOrDefault(v => v.Version == hasta!.Trim());
        if (baseSpec is null || revision is null)
        {
            return TypedResults.NotFound();
        }

        ResultadoDiff resultado;
        try
        {
            resultado = await render.CalcularAsync(new SolicitudDiff(textoTipo, baseSpec.Contenido, revision.Contenido), ct);
        }
        catch (RenderDiffException ex)
        {
            // El mensaje de la excepción no trae contenido de las specs (ATLAS-ADR-0036): se puede registrar.
            NoCalculado(logs.CreateLogger(typeof(EndpointsVistaContrato)), id, ex.Motivo, ex.Estado, ex.Message);
            return ex.Motivo switch
            {
                MotivoErrorDiff.NoSoportado => NoProcesable("diff_no_soportado", "atlas-render no tiene diff para este tipo de contrato."),
                MotivoErrorDiff.SpecInvalida => NoProcesable("spec_invalida",
                    $"La spec de la versión {(ex.Lado == "base" ? baseSpec.Version : revision.Version)} no se pudo analizar para el diff."),
                MotivoErrorDiff.Rechazado => NoProcesable("diff_rechazado", "atlas-render rechazó el par de specs (por ejemplo, por su tamaño)."),
                _ => TypedResults.Json(new ErrorDiff("render_no_disponible", "El servicio de diff no responde. Inténtalo de nuevo en unos minutos."),
                    statusCode: StatusCodes.Status503ServiceUnavailable),
            };
        }

        // Las versiones son inmutables: el mismo par da siempre el mismo diff.
        http.Response.Headers.CacheControl = "private, max-age=3600";
        return TypedResults.Ok(new DiffContrato(id, baseSpec.Version, revision.Version, resultado.Motor, resultado.SinDiferencias, resultado.Resumen,
            Ordenar(resultado), resultado.Changelog, resultado.DesdeCache));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diff de {Contrato} no calculado: {Motivo} ({Estado}) {Mensaje}")]
    private static partial void NoCalculado(ILogger log, Guid contrato, MotivoErrorDiff motivo, int? estado, string mensaje);

    private static JsonHttpResult<ErrorDiff> NoProcesable(string codigo, string mensaje) =>
        TypedResults.Json(new ErrorDiff(codigo, mensaje), statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>
    /// El changelog completo más lo de <c>breaking</c> que no venga en él, sin repetir (por la huella de oasdiff), con los
    /// incompatibles primero y luego por nivel; dentro de cada grupo, el orden de oasdiff.
    /// </summary>
    internal static List<CambioDiff> Ordenar(ResultadoDiff resultado)
    {
        var claves = new HashSet<string>(StringComparer.Ordinal);
        var breaking = new HashSet<string>(resultado.Breaking.Select(Clave), StringComparer.Ordinal);
        var todos = new List<(CambioContrato Cambio, bool Breaking, int Orden)>();
        foreach (var cambio in resultado.Cambios.Concat(resultado.Breaking))
        {
            var clave = Clave(cambio);
            if (claves.Add(clave))
            {
                todos.Add((cambio, cambio.Breaking || breaking.Contains(clave), todos.Count));
            }
        }
        return
        [
            .. todos.OrderBy(t => t.Breaking ? 0 : 1).ThenBy(t => Gravedad(t.Cambio.Nivel)).ThenBy(t => t.Orden)
                .Select(t => new CambioDiff(t.Cambio.Regla, t.Cambio.Nivel, t.Breaking, t.Cambio.Texto, t.Cambio.Comentario, t.Cambio.Metodo,
                    t.Cambio.Ruta, t.Cambio.OperationId, t.Cambio.Seccion)),
        ];

        static string Clave(CambioContrato c) => c.Huella ?? $"{c.Regla}|{c.Metodo}|{c.Ruta}|{c.Texto}";

        static int Gravedad(string nivel) => nivel switch
        {
            NivelesCambio.Error => 0,
            NivelesCambio.Advertencia => 1,
            _ => 2,
        };
    }
}
