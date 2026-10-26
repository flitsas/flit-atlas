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
/// Alcance de datos de la petición según la sesión (predicado obligatorio y RLS, ATLAS-ADR-0023 y ATLAS-ADR-0050): un
/// externo ve solo su organización; un empleado, todas; una petición sin sesión, nada (<see cref="AlcanceDatos.Ninguno"/>),
/// salvo en los endpoints que piden el del sistema con <see cref="ExtensionesAlcanceDatos.ConAlcanceDelSistemaSinSesion"/>
/// (ingreso, protocolo OAuth, proxy del sandbox, feed .ics). Fuera de una petición HTTP (trabajos en segundo plano,
/// consumidores de la cola, comandos) es el del sistema: no hay a quién más atribuirlo.
/// </summary>
internal sealed class AlcanceDatosDeSesion(IHttpContextAccessor accesor) : IAlcanceDatos
{
    public AlcanceDatos Actual => De(accesor.HttpContext);

    internal static AlcanceDatos De(HttpContext? http)
    {
        if (http is null)
        {
            return AlcanceDatos.Sistema;
        }
        // «Sin sesión» es sin una persona de Atlas en el principal: en medio del ingreso o la activación, Identity puede dejar
        // un principal autenticado que todavía no es la sesión del BFF.
        var conSesion = http.User.Identity?.IsAuthenticated == true && http.User.FindFirstValue(ClaimsAtlas.PersonaId) is not null;
        if (!conSesion && http.GetEndpoint()?.Metadata.GetMetadata<AlcanceDelSistemaSinSesion>() is not null)
        {
            return AlcanceDatos.Sistema;
        }
        return De(http.User);
    }

    internal static AlcanceDatos De(ClaimsPrincipal? usuario)
    {
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return AlcanceDatos.Ninguno;
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
