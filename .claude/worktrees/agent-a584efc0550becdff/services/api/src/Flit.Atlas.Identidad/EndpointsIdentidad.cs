using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Atlas.Identidad;

public static class EndpointsIdentidad
{
    /// <summary>Cabecera que exigen las peticiones que cambian estado (ATLAS-ADR-0008).</summary>
    public const string CabeceraCsrf = "X-Atlas-Csrf";

    /// <summary>
    /// Redirect URI de Entra ID, la que quedó registrada en Azure para cada ambiente. Está bajo <c>/api/</c>, así que
    /// los proxies de la web ya la reenvían a la API.
    /// </summary>
    public const string RutaRetornoEntra = "/api/auth/callback/azure-ad";

    public sealed record SolicitudSimulada(string Correo, string Nombre, Guid[]? Grupos);

    [Description("Usuario de la sesión actual: persona, organización, proveedor con el que entró y roles de Atlas.")]
    public sealed record Yo(
        Guid Sub, Guid PersonaId, string Nombre, string TipoPrincipal, Guid OrganizacionId, string Proveedor,
        IReadOnlyList<string> Roles);

    public static IEndpointRouteBuilder MapIdentidad(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var proveedores = rutas.ServiceProvider.GetService(typeof(ProveedoresHabilitados)) as ProveedoresHabilitados
            ?? throw new InvalidOperationException("Falta AgregarIdentidad en el arranque.");

        var identidad = rutas.MapGroup("/api/v1/identidad").WithTags("Identidad");

        identidad.MapGet("/proveedores", () => proveedores)
            .WithName("ListarProveedoresDeIngreso")
            .WithSummary("Proveedores de ingreso habilitados")
            .WithDescription("Qué botones de ingreso muestra la PWA en este ambiente. No requiere sesión.");

        if (proveedores.Microsoft)
        {
            // Navegación completa del navegador (no fetch): termina en RutaRetornoEntra y vuelve a la PWA.
            identidad.MapGet("/entrar/microsoft", (
                    [Description("Ruta local de la PWA a la que se vuelve después del ingreso (por defecto «/»).")] string? volver) =>
                    Results.Challenge(new AuthenticationProperties { RedirectUri = RutaLocal(volver) }, [ClaimsAtlas.EsquemaEntra]))
                .WithName("EntrarConMicrosoft")
                .WithSummary("Iniciar sesión con Microsoft")
                .WithDescription(
                    "Navegación completa del navegador (no fetch). Redirige a Entra ID (OIDC con PKCE); al volver por " +
                    $"{RutaRetornoEntra} crea la sesión y lleva a «volver», que solo puede ser una ruta local de la PWA.")
                .Produces(StatusCodes.Status302Found);
        }

        if (proveedores.Simulado)
        {
            identidad.MapPost("/simulado/entrar", async (SolicitudSimulada solicitud, AprovisionadorEmpleados aprovisionador,
                HttpContext http, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(solicitud.Correo) || string.IsNullOrWhiteSpace(solicitud.Nombre))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["correo"] = ["El correo y el nombre son obligatorios."],
                    });
                }
                var correo = solicitud.Correo.Trim().ToLowerInvariant();
                var cuenta = new CuentaEmpleado(
                    AprovisionadorEmpleados.ProveedorSimulado, correo, null, correo, solicitud.Nombre.Trim(),
                    solicitud.Grupos ?? [], null, null, []);
                try
                {
                    var principal = await aprovisionador.AprovisionarAsync(cuenta, ct);
                    var propiedades = new AuthenticationProperties { IsPersistent = true };
                    propiedades.SetString(AlmacenSesiones.ClavesPropiedades.Agente, ResumenAgente.De(http.Request.Headers.UserAgent));
                    await http.SignInAsync(ClaimsAtlas.EsquemaCookie, principal, propiedades);
                    return Results.NoContent();
                }
                catch (CorreoEnUsoException)
                {
                    return Results.Conflict(new { error = "correo_en_uso" });
                }
            })
            // Solo existe en local y dev: no es parte del contrato de qa y pdn (ATLAS-ADR-0021).
            .ExcludeFromDescription();
        }

        identidad.MapPost("/salir", async (HttpContext http) =>
        {
            // Revoca la sesión en el servidor (AlmacenSesiones.RemoveAsync) y borra la cookie.
            await http.SignOutAsync(ClaimsAtlas.EsquemaCookie);
            // El navegador borra la caché, el almacenamiento (IndexedDB, service worker) y las cookies del origen.
            http.Response.Headers["Clear-Site-Data"] = "\"cache\", \"cookies\", \"storage\"";
            return Results.NoContent();
        })
            .WithName("CerrarSesion")
            .WithSummary("Cerrar sesión")
            .WithDescription(
                "Revoca la sesión en el servidor, borra la cookie y envía Clear-Site-Data para que el navegador limpie " +
                "caché, almacenamiento y cookies del origen. Responde 204 aunque no haya sesión.")
            .Produces(StatusCodes.Status204NoContent);

        rutas.MapGet("/api/v1/plataforma/yo", (ClaimsPrincipal usuario) => new Yo(
                Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.Sub)!),
                Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.PersonaId)!),
                usuario.FindFirstValue(ClaimsAtlas.Nombre) ?? string.Empty,
                usuario.FindFirstValue(ClaimsAtlas.TipoPrincipal)!,
                Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.OrganizacionId)!),
                usuario.FindFirstValue(ClaimsAtlas.Proveedor)!,
                usuario.FindAll(ClaimsAtlas.Rol).Select(c => c.Value).ToList()))
            .RequireAuthorization()
            .WithName("ObtenerUsuarioActual")
            .WithTags("Plataforma")
            .WithSummary("Usuario en sesión")
            .WithDescription("Persona, organización, proveedor de ingreso y roles de la sesión actual.");

        return rutas;
    }

    /// <summary>
    /// Antifalsificación de la API (ATLAS-ADR-0008), además de SameSite=Strict: toda petición a <c>/api/</c> que
    /// cambia estado trae la cabecera propia (un formulario de otro sitio no puede ponerla sin CORS, y Atlas no habilita
    /// CORS) y, si el navegador manda <c>Origin</c>, debe ser el mismo host. Se exceptúa <see cref="RutaRetornoEntra"/>:
    /// Microsoft la llama con un POST desde su propio origen, y la protegen el <c>state</c>, el <c>nonce</c> y la cookie
    /// de correlación de OIDC.
    /// </summary>
    public static IApplicationBuilder UseAntifalsificacionApi(this IApplicationBuilder app) =>
        app.Use(async (http, siguiente) =>
        {
            var peticion = http.Request;
            var cambia = !(HttpMethods.IsGet(peticion.Method) || HttpMethods.IsHead(peticion.Method) || HttpMethods.IsOptions(peticion.Method));
            if (cambia && peticion.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !peticion.Path.Equals(RutaRetornoEntra, StringComparison.OrdinalIgnoreCase))
            {
                var origen = peticion.Headers.Origin.ToString();
                var mismoOrigen = string.IsNullOrEmpty(origen)
                    || string.Equals(origen, $"{peticion.Scheme}://{peticion.Host}", StringComparison.OrdinalIgnoreCase);
                if (!peticion.Headers.ContainsKey(CabeceraCsrf) || !mismoOrigen)
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await siguiente(http);
        });

    /// <summary>Evita redirecciones abiertas: solo rutas locales de la PWA.</summary>
    internal static string RutaLocal(string? volver) =>
        !string.IsNullOrEmpty(volver) && volver.StartsWith('/') && !volver.StartsWith("//", StringComparison.Ordinal)
            && !volver.StartsWith("/\\", StringComparison.Ordinal)
            ? volver
            : "/";
}
