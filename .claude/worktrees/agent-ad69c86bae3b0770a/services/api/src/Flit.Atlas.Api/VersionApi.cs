using System.ComponentModel;

namespace Flit.Atlas.Api;

/// <summary>Respuesta de <c>GET /api/v1/version</c>.</summary>
[Description("Servicio y versión del ensamblado desplegado.")]
internal sealed record VersionApi(string Servicio, string? Version);
