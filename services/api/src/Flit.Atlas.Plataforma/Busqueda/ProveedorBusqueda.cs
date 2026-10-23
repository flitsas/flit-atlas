using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Busqueda;

/// <summary>
/// Proveedor de la búsqueda global (prompt maestro §8.9, ATLAS-ADR-0016): cada módulo busca en sus propias tablas, con
/// el predicado de organización y los permisos de la persona aplicados en SQL, y devuelve resultados ya filtrados. El
/// endpoint de búsqueda que los combina llega con el primer módulo que tenga datos (F2); hoy la PWA busca solo en los
/// manifiestos.
/// </summary>
public interface IProveedorBusqueda
{
    /// <summary>Id del proveedor; coincide con <c>busqueda.proveedor</c> del manifiesto del frontend.</summary>
    string Proveedor { get; }

    /// <summary>Tipos de resultado que entrega (<c>turno</c>, <c>persona</c>…).</summary>
    IReadOnlyList<string> Tipos { get; }

    Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(ConsultaBusqueda consulta, CancellationToken ct = default);
}

/// <param name="Texto">Lo que escribió la persona (nunca va a los logs).</param>
/// <param name="Usuario">Sesión de quien busca, para que el proveedor aplique sus permisos.</param>
public sealed record ConsultaBusqueda(string Texto, ClaimsPrincipal Usuario, int Limite = 10);

/// <param name="Ruta">Ruta de la PWA a la que lleva el resultado (<c>/soporte/turnos/…</c>).</param>
public sealed record ResultadoBusqueda(string Tipo, string Id, string Titulo, string? Resumen, string Ruta);

public static class ExtensionesBusqueda
{
    /// <summary>Registra un proveedor de búsqueda del módulo (en <c>IModuloAtlas.RegistrarServicios</c>).</summary>
    public static IServiceCollection AgregarProveedorBusqueda<TProveedor>(this IServiceCollection servicios)
        where TProveedor : class, IProveedorBusqueda
    {
        servicios.AddScoped<IProveedorBusqueda, TProveedor>();
        return servicios;
    }
}
