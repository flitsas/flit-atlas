using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Atlas.Plataforma.Mensajeria;

/// <summary>
/// Sobre de todo mensaje de Atlas. Es el mismo del contrato de plataforma de FLIT §7 (<c>EventEnvelope</c> de
/// <c>Flit.Platform.Sdk</c>), con los nombres de campo en inglés y camelCase tal como viajan (ATLAS-ADR-0018).
/// </summary>
public sealed record SobreEvento(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    string Producer,
    string CorrelationId,
    JsonElement Data)
{
    public const string Productor = "atlas";

    /// <summary>Exchange del productor: <c>flit.atlas</c>.</summary>
    public const string Exchange = "flit." + Productor;

    /// <summary><c>tenantId</c> de los eventos internos, que no pertenecen a una organización externa.</summary>
    public static readonly Guid SinOrganizacion = Guid.Empty;

    public static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string AJson() => JsonSerializer.Serialize(this, OpcionesJson);

    public static SobreEvento DesdeJson(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<SobreEvento>(utf8, OpcionesJson)
        ?? throw new JsonException("El mensaje no trae un sobre.");

    public T DatosComo<T>() => Data.Deserialize<T>(OpcionesJson)
        ?? throw new JsonException($"data no es un {typeof(T).Name}.");
}
