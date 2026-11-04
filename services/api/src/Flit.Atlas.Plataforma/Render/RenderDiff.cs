using System.Text.Json.Serialization;

namespace Flit.Atlas.Plataforma.Render;

/// <summary>
/// Diff entre dos versiones de un contrato, calculado por atlas-render con oasdiff (HU 3.5, ATLAS-ADR-0036). Lo usa el
/// módulo de contratos (HU 3.6) para la pantalla de diff y el changelog. atlas-render cachea por el SHA-256 de las dos
/// specs, así que pedir dos veces el mismo par es barato. Las specs viajan completas y nunca se registran en los logs.
/// </summary>
public interface IRenderDiff
{
    /// <summary>
    /// Compara <paramref name="solicitud"/>.Base con <paramref name="solicitud"/>.Revision. Lanza
    /// <see cref="RenderDiffException"/> si el tipo no tiene diff (AsyncAPI y proto, por ahora), si una spec no es válida
    /// o si atlas-render no responde.
    /// </summary>
    Task<ResultadoDiff> CalcularAsync(SolicitudDiff solicitud, CancellationToken ct = default);
}

/// <summary>Tipos de contrato que entiende atlas-render (los de la publicación, plan 00 §8.4).</summary>
public static class TiposContrato
{
    public const string OpenApi = "openapi";
    public const string AsyncApi = "asyncapi";
    public const string Proto = "proto";

    /// <summary>Tipos con diff disponible hoy.</summary>
    public static readonly IReadOnlySet<string> ConDiff = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { OpenApi };
}

/// <summary>Idiomas del texto de los cambios. Lo que oasdiff no tenga traducido al español sale en inglés.</summary>
public static class IdiomasDiff
{
    public const string Espanol = "es";
    public const string Ingles = "en";
}

/// <summary>Las dos specs completas (YAML o JSON) en el orden base → revisión.</summary>
public sealed record SolicitudDiff(string Tipo, string Base, string Revision, string Idioma = IdiomasDiff.Espanol);

/// <summary>Resultado del diff, tal como lo devuelve atlas-render.</summary>
public sealed record ResultadoDiff
{
    public required string Tipo { get; init; }

    /// <summary>Motor y versión, p. ej. <c>oasdiff v1.33.0</c>.</summary>
    public required string Motor { get; init; }

    public required string Idioma { get; init; }

    /// <summary>SHA-256 en hexadecimal (minúsculas) de los bytes UTF-8 de la spec base, tal como se envió.</summary>
    public required string HashBase { get; init; }

    public required string HashRevision { get; init; }

    /// <summary><c>info.version</c> de cada spec.</summary>
    public string? VersionBase { get; init; }

    public string? VersionRevision { get; init; }

    /// <summary>oasdiff no encontró ninguna diferencia estructural.</summary>
    public bool SinDiferencias { get; init; }

    public required ResumenDiff Resumen { get; init; }

    /// <summary>Changelog completo (nivel info y superiores), como <c>oasdiff changelog</c>.</summary>
    public IReadOnlyList<CambioContrato> Cambios { get; init; } = [];

    /// <summary>Cambios incompatibles (error y warning), como <c>oasdiff breaking</c>.</summary>
    public IReadOnlyList<CambioContrato> Breaking { get; init; } = [];

    /// <summary>Changelog legible en Markdown.</summary>
    public string Changelog { get; init; } = "";

    /// <summary>La respuesta salió de la caché de atlas-render (cabecera <c>X-Atlas-Cache</c>). No viene en el cuerpo.</summary>
    [JsonIgnore]
    public bool DesdeCache { get; init; }
}

/// <summary>Cuántos cambios hay por nivel.</summary>
public sealed record ResumenDiff(int Total, int Errores, int Advertencias, int Informativos, int Breaking, bool HayBreaking);

/// <summary>Niveles de oasdiff. Error y warning son incompatibles.</summary>
public static class NivelesCambio
{
    public const string Error = "error";
    public const string Advertencia = "warning";
    public const string Informativo = "info";
}

/// <summary>Un cambio detectado por oasdiff.</summary>
public sealed record CambioContrato
{
    /// <summary>Id de la regla de oasdiff, p. ej. <c>api-removed-without-deprecation</c>.</summary>
    public required string Regla { get; init; }

    /// <summary><see cref="NivelesCambio"/>: error, warning o info.</summary>
    public required string Nivel { get; init; }

    public bool Breaking { get; init; }

    public required string Texto { get; init; }

    public string? Comentario { get; init; }

    public string? Metodo { get; init; }

    public string? Ruta { get; init; }

    public string? OperationId { get; init; }

    public string? Seccion { get; init; }

    public IReadOnlyList<string>? Avisos { get; init; }

    /// <summary>Huella estable de oasdiff (no depende de la ubicación en el archivo).</summary>
    public string? Huella { get; init; }

    public FuenteCambio? FuenteBase { get; init; }

    public FuenteCambio? FuenteRevision { get; init; }
}

/// <summary>Ubicación del cambio en la spec.</summary>
public sealed record FuenteCambio(int Linea, int Columna);

/// <summary>Por qué falló el diff.</summary>
public enum MotivoErrorDiff
{
    /// <summary>El tipo no tiene diff todavía (AsyncAPI y proto): atlas-render respondió 422 «diff no soportado».</summary>
    NoSoportado,

    /// <summary>Una de las specs no es válida; <see cref="RenderDiffException.Lado"/> dice cuál.</summary>
    SpecInvalida,

    /// <summary>atlas-render rechazó la petición (400, 413, 415): es un error de quien llama.</summary>
    Rechazado,

    /// <summary>atlas-render no respondió, está ocupado o tardó demasiado: se puede reintentar.</summary>
    NoDisponible,
}

/// <summary>El diff no se pudo calcular. El mensaje es apto para los logs: no lleva el contenido de las specs.</summary>
public sealed class RenderDiffException(string mensaje, MotivoErrorDiff motivo, int? estado = null, string? lado = null, Exception? causa = null)
    : Exception(mensaje, causa)
{
    public MotivoErrorDiff Motivo { get; } = motivo;

    /// <summary>Código HTTP de atlas-render, si respondió.</summary>
    public int? Estado { get; } = estado;

    /// <summary><c>base</c> o <c>revision</c> cuando el motivo es <see cref="MotivoErrorDiff.SpecInvalida"/>.</summary>
    public string? Lado { get; } = lado;
}

/// <summary>Sección <c>Plataforma:Render</c>.</summary>
public sealed class OpcionesRender
{
    public const string Seccion = "Plataforma:Render";

    /// <summary>
    /// URL base de atlas-render. Por defecto <c>http://render</c>, que resuelve el descubrimiento de servicios de Aspire
    /// (en local, con la referencia del AppHost; en el clúster, con <c>services__render__http__0</c> o esta misma clave).
    /// </summary>
    public Uri Url { get; set; } = new("http://render");
}
