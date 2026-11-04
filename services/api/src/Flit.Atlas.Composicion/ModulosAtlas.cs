using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Modulos.Catalogo;
using Flit.Atlas.Modulos.Contratos;
using Flit.Atlas.Modulos.Equipo;
using Flit.Atlas.Modulos.Hola;
using Flit.Atlas.Modulos.Soporte;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Composicion;

/// <summary>
/// Módulos funcionales de Atlas (ATLAS-ADR-0016 y ATLAS-ADR-0026). Es <b>la única lista</b>: atlas-api los registra y
/// monta desde aquí, y el migrador migra sus contextos, crea sus banderas y aplica sus permisos desde aquí. Agregar un
/// módulo = su proyecto, la referencia en <c>Flit.Atlas.Composicion.csproj</c> y una línea en <see cref="Registrados"/>.
/// </summary>
public static class ModulosAtlas
{
    public static IReadOnlyList<IModuloAtlas> Registrados { get; } =
    [
        new ModuloCatalogo(),
        new ModuloContratos(),
        new ModuloEquipo(),
        new ModuloHola(),
        new ModuloSoporte(),
    ];

    /// <summary>
    /// Todos los manifiestos de permisos: los del núcleo (plataforma, lista base del §7.4 e identidad) y los de cada
    /// módulo registrado. Es lo que aplica el migrador; una prueba verifica que atlas-api tenga exactamente los mismos.
    /// </summary>
    public static IReadOnlyList<ManifiestoPermisos> Manifiestos { get; } =
        [.. PermisosIdentidad.ManifiestosDelNucleo, .. Registrados.Select(m => m.Permisos)];
}
