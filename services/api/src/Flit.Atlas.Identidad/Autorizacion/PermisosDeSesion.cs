using System.Security.Claims;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;

namespace Flit.Atlas.Identidad.Autorizacion;

/// <summary>Manifiesto de permisos de la identidad (ATLAS-ADR-0022 §3 y ATLAS-ADR-0023).</summary>
public static class PermisosIdentidad
{
    /// <summary>Cerrar todas las sesiones de una persona, suspenderla y reactivarla, y la sincronización de personas con Graph (ATLAS-ADR-0030).</summary>
    public const string Administrar = "atlas.identidad.administrar";

    public static ManifiestoPermisos Manifiesto { get; } = new("identidad", "Identidad",
    [
        new(Administrar, "Cerrar todas las sesiones de una persona, suspenderla y reactivarla; ver y pedir la sincronización de personas con Graph."),
    ]);

    /// <summary>Manifiestos del núcleo (plataforma, lista base del §7.4 e identidad): lo que aplica el migrador.</summary>
    public static IReadOnlyList<ManifiestoPermisos> ManifiestosDelNucleo { get; } = [.. ManifiestosNucleo.Todos, Manifiesto];
}

/// <summary>
/// Permisos efectivos de la sesión: se calculan en el servidor con la persona de la sesión, una vez por petición y con
/// la hora actual (ATLAS-ADR-0023). Por eso un rol vencido deja de valer en la petición siguiente, aunque la sesión siga
/// viva, y la cookie nunca lleva permisos.
/// </summary>
internal sealed class PermisosDeSesion(CalculadoraPermisos calculadora) : IPermisosDeSesion
{
    private Guid? _persona;
    private Task<PermisosEfectivos>? _calculo;

    public Task<PermisosEfectivos> ObtenerAsync(ClaimsPrincipal usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (usuario.Identity?.IsAuthenticated != true
            || !Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var persona))
        {
            return Task.FromResult(PermisosEfectivos.Ninguno);
        }
        if (_calculo is null || _persona != persona)
        {
            _persona = persona;
            _calculo = calculadora.CalcularAsync(persona, ct);
        }
        return _calculo;
    }
}

/// <summary>
/// Alcance de datos de la petición según la sesión (predicado obligatorio y RLS, ATLAS-ADR-0023): un externo ve solo su
/// organización; un empleado, todas; sin sesión (ingreso, procesos en segundo plano), el del sistema.
/// </summary>
internal sealed class AlcanceDatosDeSesion(IHttpContextAccessor accesor) : IAlcanceDatos
{
    public AlcanceDatos Actual => De(accesor.HttpContext?.User);

    internal static AlcanceDatos De(ClaimsPrincipal? usuario)
    {
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return AlcanceDatos.Sistema;
        }
        if (usuario.FindFirstValue(ClaimsAtlas.TipoPrincipal) == ClaimsAtlas.TipoEmpleado)
        {
            return AlcanceDatos.Interno;
        }
        return Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.OrganizacionId), out var organizacion)
            ? AlcanceDatos.DeOrganizaciones(organizacion)
            : AlcanceDatos.Ninguno;
    }
}
