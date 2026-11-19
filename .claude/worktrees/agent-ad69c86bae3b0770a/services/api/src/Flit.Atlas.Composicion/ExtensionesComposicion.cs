using Flit.Atlas.Identidad.Tokens;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Composicion;

public static class ExtensionesComposicion
{
    /// <summary>
    /// atlas-api: registra los módulos de <see cref="ModulosAtlas.Registrados"/> (manifiesto de permisos, datos y
    /// servicios) y sus scopes de OpenIddict (ATLAS-ADR-0024 y ATLAS-ADR-0026). Los módulos no conocen la identidad: la
    /// composición une sus <see cref="ScopeModulo"/> con el registro de scopes.
    /// </summary>
    public static IServiceCollection AgregarModulosAtlas(this IServiceCollection servicios, IConfiguration configuracion, string cadenaConexion)
    {
        servicios.AgregarModulos(configuracion, cadenaConexion, ModulosAtlas.Registrados);
        servicios.AgregarScopes(new ScopesDeModulos(ModulosAtlas.Registrados));
        return servicios;
    }
}

/// <summary>Los scopes que declaran los módulos, con el módulo como dueño (su manifiesto ya está registrado).</summary>
public sealed class ScopesDeModulos(IEnumerable<IModuloAtlas> modulos) : IProveedorScopes
{
    public IEnumerable<ScopeAtlas> Scopes { get; } =
        [.. modulos.SelectMany(m => m.Scopes.Select(s => new ScopeAtlas(s.Nombre, m.Id, s.Descripcion)))];
}
