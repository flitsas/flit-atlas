using Flit.Atlas.Modulos.Hola.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Plataforma.Permisos.RolesAtlas;

namespace Flit.Atlas.Modulos.Hola;

/// <summary>Permisos del módulo de ejemplo (ATLAS-ADR-0023): <c>atlas.hola.ver</c> y <c>atlas.hola.gestionar</c>.</summary>
public static class PermisosHola
{
    public const string Ver = "atlas.hola.ver";
    public const string Gestionar = "atlas.hola.gestionar";

    public static ManifiestoPermisos Manifiesto { get; } = new(ModuloHola.IdModulo, "Hola (ejemplo)",
    [
        // Todo empleado tiene atlas.lector; el integrador externo demuestra el aislamiento por organización.
        new(Ver, "Ver los saludos de tu organización (módulo de ejemplo).", Lector, Integrador),
        new(Gestionar, "Escribir saludos en tu organización (módulo de ejemplo).", LiderTecnico, Desarrollador),
    ]);
}

/// <summary>
/// Módulo de ejemplo «hola» (HU 1.9, docs/como-crear-un-modulo.md): muestra el contrato completo de un módulo con una
/// tabla con <c>organizacion_id</c> y RLS, dos permisos y tres endpoints. Viene <b>apagado</b> en todos los ambientes
/// (bandera <c>modulo.hola</c>); se enciende en <c>plataforma_modulo</c> para probarlo.
/// </summary>
public sealed class ModuloHola : IModuloAtlas
{
    public const string IdModulo = "hola";

    public string Id => IdModulo;

    public string Nombre => "Hola";

    public string Descripcion =>
        "Módulo de ejemplo de docs/como-crear-un-modulo.md: saludos por organización con permisos, predicado obligatorio y RLS. Apagado por defecto (bandera modulo.hola).";

    public bool ActivoPorDefecto => false;

    public ManifiestoPermisos Permisos => PermisosHola.Manifiesto;

    public Type? ContextoDatos => typeof(HolaDbContext);

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<HolaDbContext>(o => HolaDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        // Sin servicios propios: los endpoints usan HolaDbContext y TimeProvider.
    }

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsHola.Mapear(grupo);
}
