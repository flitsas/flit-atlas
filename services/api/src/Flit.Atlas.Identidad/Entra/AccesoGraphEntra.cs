using Flit.Atlas.Plataforma.Microsoft365;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Identidad.Entra;

/// <summary>
/// Token de aplicación de Graph para los módulos (ATLAS-ADR-0033) con la credencial de Entra de Atlas: MSAL lo pide y lo
/// guarda en su caché en memoria, como para <see cref="PerfilGraph"/>. Los permisos los da el registro de la app.
/// </summary>
internal sealed class AccesoGraphEntra(IAuthorizationHeaderProvider autorizacion) : IAccesoGraph
{
    public bool Disponible => true;

    public Task<string> CabeceraAsync(CancellationToken ct = default) =>
        autorizacion.CreateAuthorizationHeaderForAppAsync(ClienteGraph.ScopeAplicacion, cancellationToken: ct);
}
