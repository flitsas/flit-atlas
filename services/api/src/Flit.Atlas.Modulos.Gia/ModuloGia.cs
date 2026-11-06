using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Modulos.Gia.Semilla;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>
/// Permisos del gobierno de IA (prompt maestro §7.4): los <c>atlas.gia.*</c> de la lista base, mudados de
/// <see cref="ManifiestosNucleo.GobiernoDeIa"/> con los mismos códigos y concesiones (ATLAS-ADR-0055 §1).
/// </summary>
public static class PermisosGia
{
    public const string Ver = "atlas.gia.ver";
    public const string Gestionar = "atlas.gia.gestionar";
    public const string Auditar = "atlas.gia.auditar";

    public static ManifiestoPermisos Manifiesto { get; } =
        new(ModuloGia.IdModulo, ManifiestosNucleo.GobiernoDeIa.Nombre, [.. ManifiestosNucleo.GobiernoDeIa.Permisos]);
}

/// <summary>
/// Gobierno de IA (F8, prompt maestro §8.8, plan 02, ATLAS-ADR-0055), sin lo que depende del gateway. HU 8.1: inventario de
/// casos de uso. HU 8.2: evaluación de riesgo y EIPD con aprobación según el nivel. HU 8.3: políticas versionadas con
/// aceptación. HU 8.5: herramientas, asientos e incidentes. HU 8.6: tablero de KPI, sin envío al Comité. <b>Viene apagado</b>
/// (bandera <c>modulo.gia</c>): encenderlo es decisión del líder, después de revisar lo sembrado.
/// </summary>
public sealed class ModuloGia : IModuloAtlas
{
    public const string IdModulo = "gia";

    public string Id => IdModulo;

    public string Nombre => "Gobierno de IA";

    public string Descripcion =>
        "Inventario de casos de uso de IA, evaluación de riesgo y de impacto en privacidad con aprobación según el nivel, políticas versionadas con registro de aceptación, herramientas del equipo e incidentes.";

    public bool ActivoPorDefecto => false;

    public ManifiestoPermisos Permisos => PermisosGia.Manifiesto;

    public Type? ContextoDatos => typeof(GiaDbContext);

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<GiaDbContext>(o => GiaDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
    }

    public void MapearEndpoints(RouteGroupBuilder grupo)
    {
        EndpointsCasos.Mapear(grupo);
        EndpointsEvaluaciones.Mapear(grupo);
        EndpointsPoliticas.Mapear(grupo);
        EndpointsInventario.Mapear(grupo);
        EndpointsTablero.Mapear(grupo);
    }

    /// <summary>Casos CU-01 a CU-13 y políticas POL-SGSI-IA-01 a 07, en borrador; idempotente y sin pisar lo editado.</summary>
    public async Task SembrarAsync(IServiceProvider servicios, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        await SemillaGia.AplicarAsync(servicios.GetRequiredService<GiaDbContext>(), TimeProvider.System, ct).ConfigureAwait(false);
    }
}
