using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Requisito de autorización: la sesión tiene el permiso (en algún alcance).</summary>
public sealed class RequisitoPermiso(string permiso) : IAuthorizationRequirement
{
    public string Permiso { get; } = permiso;

    public override string ToString() => $"Permiso {Permiso}";
}

/// <summary>Metadato de los endpoints protegidos con <see cref="ExtensionesPermisos.RequierePermiso"/>.</summary>
public sealed class MetadatoPermiso(string permiso)
{
    public string Permiso { get; } = permiso;
}

/// <summary>
/// Permisos efectivos de la sesión en curso. Lo implementa la identidad (lee la persona de la sesión del servidor);
/// calcula una vez por petición.
/// </summary>
public interface IPermisosDeSesion
{
    Task<PermisosEfectivos> ObtenerAsync(ClaimsPrincipal usuario, CancellationToken ct = default);
}

/// <summary>Atiende <see cref="RequisitoPermiso"/> con los permisos efectivos de la sesión, calculados en el servidor.</summary>
internal sealed class ManejadorPermiso(IHttpContextAccessor accesor) : AuthorizationHandler<RequisitoPermiso>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RequisitoPermiso requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }
        var http = context.Resource as HttpContext ?? accesor.HttpContext;
        if (http is null)
        {
            return;
        }
        var permisos = await http.RequestServices.GetRequiredService<IPermisosDeSesion>()
            .ObtenerAsync(context.User, http.RequestAborted).ConfigureAwait(false);
        if (permisos.Tiene(requirement.Permiso))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>
/// Cada permiso del catálogo es una política con su mismo nombre (<c>atlas.roles.gestionar</c>): sesión y permiso. Las
/// políticas con nombre propio (p. ej. <c>identidad.administrar</c>) siguen viniendo de <c>AddPolicy</c>.
/// </summary>
internal sealed class ProveedorPoliticasPermiso(IOptions<AuthorizationOptions> opciones, CatalogoPermisos catalogo)
    : DefaultAuthorizationPolicyProvider(opciones)
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _politicas = new(StringComparer.Ordinal);

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var politica = await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        if (politica is not null || !catalogo.Existe(policyName))
        {
            return politica;
        }
        return _politicas.GetOrAdd(policyName, ExtensionesPermisos.Politica);
    }
}

public static class ExtensionesPermisos
{
    /// <summary>
    /// Exige una sesión con el permiso. El permiso debe estar en un manifiesto registrado: si no, la petición falla y la
    /// prueba que recorre todos los endpoints lo detecta.
    /// </summary>
    public static TBuilder RequierePermiso<TBuilder>(this TBuilder builder, string permiso) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permiso);
        return builder.RequireAuthorization(permiso).WithMetadata(new MetadatoPermiso(permiso));
    }

    /// <summary>Política de un permiso: sesión autenticada y <see cref="RequisitoPermiso"/>.</summary>
    public static AuthorizationPolicy Politica(string permiso) =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new RequisitoPermiso(permiso)).Build();

    /// <summary>Registra el manifiesto de permisos de un módulo (ATLAS-ADR-0016 y ATLAS-ADR-0023).</summary>
    public static IServiceCollection AgregarManifiestoPermisos(this IServiceCollection servicios, ManifiestoPermisos manifiesto)
    {
        ArgumentNullException.ThrowIfNull(manifiesto);
        servicios.AddSingleton(manifiesto);
        return servicios;
    }

    /// <summary>
    /// Catálogo de permisos (de todos los manifiestos registrados), políticas por permiso, calculadora de permisos
    /// efectivos y el proveedor de roles implícitos por defecto (ninguno hasta F2).
    /// </summary>
    internal static IServiceCollection AgregarPermisos(this IServiceCollection servicios)
    {
        foreach (var manifiesto in ManifiestosNucleo.Todos)
        {
            servicios.AgregarManifiestoPermisos(manifiesto);
        }
        servicios.TryAddSingleton(sp => CatalogoPermisos.Crear(sp.GetServices<ManifiestoPermisos>()));
        servicios.AddHttpContextAccessor();
        servicios.AddAuthorizationCore();
        servicios.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, ProveedorPoliticasPermiso>());
        servicios.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, ManejadorPermiso>());
        servicios.TryAddScoped<IProveedorRolesImplicitos, SinRolesImplicitos>();
        servicios.TryAddScoped<CalculadoraPermisos>();
        return servicios;
    }
}
