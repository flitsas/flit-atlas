using Microsoft.AspNetCore.Builder;

namespace Flit.Atlas.Plataforma.Modulos;

/// <summary>
/// Marca de los endpoints que encolan una exportación (HU 4.10, ATLAS-ADR-0048). El limitador de tasa de la identidad la
/// lee para aplicarles el límite por persona y, en el portal, por organización. Un módulo no referencia a la identidad:
/// por eso la marca vive en Plataforma.
/// </summary>
public sealed class MetadatoSolicitudExportacion;

public static class ExtensionesLimitesDeTasa
{
    /// <summary>Aplica al endpoint los límites de tasa de las solicitudes de exportación (por persona y por organización).</summary>
    public static TBuilder ConLimiteDeExportacion<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadatoSolicitudExportacion());
}
