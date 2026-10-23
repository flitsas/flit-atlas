using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Seguridad;
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

    /// <param name="Reautenticar">La PWA vuelve a pedir el ingreso porque una acción sensible lo exigió.</param>
    public sealed record SolicitudSimulada(string Correo, string Nombre, Guid[]? Grupos, bool Reautenticar = false);

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
            // Con reautenticar=true, Entra vuelve a pedir las credenciales (prompt=login) aunque haya sesión de Microsoft.
            identidad.MapGet("/entrar/microsoft", (
                    [Description("Ruta local de la PWA a la que se vuelve después del ingreso (por defecto «/»).")] string? volver,
                    [Description("true cuando una acción sensible pidió reautenticación (responde 401 con codigo reautenticacion_requerida).")] bool? reautenticar) =>
                {
                    var propiedades = new AuthenticationProperties { RedirectUri = RutaLocal(volver) };
                    if (reautenticar == true)
                    {
                        propiedades.Items[IngresoEntra.ClaveReautenticar] = "1";
                    }
                    return Results.Challenge(propiedades, [ClaimsAtlas.EsquemaEntra]);
                })
                .WithName("EntrarConMicrosoft")
                .WithSummary("Iniciar sesión con Microsoft")
                .WithDescription(
                    "Navegación completa del navegador (no fetch). Redirige a Entra ID (OIDC con PKCE); al volver por " +
                    $"{RutaRetornoEntra} crea una sesión nueva y lleva a «volver», que solo puede ser una ruta local de la PWA. " +
                    "Con reautenticar=true, Entra vuelve a pedir las credenciales (prompt=login).")
                .Produces(StatusCodes.Status302Found);
        }

        if (proveedores.Simulado)
        {
            identidad.MapPost("/simulado/entrar", async (SolicitudSimulada solicitud, AprovisionadorEmpleados aprovisionador,
                RegistroSeguridad registro, HttpContext http, CancellationToken ct) =>
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
                    solicitud.Grupos ?? [], null, null, [], Reautenticacion: solicitud.Reautenticar);
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
                catch (IngresoNoPermitidoException ex)
                {
                    await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, new DatosEventoSeguridad
                    {
                        Detalle = new { proveedor = AprovisionadorEmpleados.ProveedorSimulado, motivo = ex.Motivo },
                    }, ct);
                    // Mensaje genérico: no revela si la cuenta existe, está suspendida o bloqueada.
                    return Results.Json(new { error = "ingreso_no_permitido" }, statusCode: StatusCodes.Status403Forbidden);
                }
            })
            // Solo existe en local y dev: no es parte del contrato de qa y pdn (ATLAS-ADR-0021).
            .ExcludeFromDescription();
        }

        identidad.MapPost("/salir", async (HttpContext http, RegistroSeguridad registro, CancellationToken ct) =>
        {
            await EndpointsSesiones.SalirAsync(http, registro, ct);
            return Results.NoContent();
        })
            .WithName("CerrarSesion")
            .WithSummary("Cerrar sesión")
            .WithDescription(
                "Revoca la sesión en el servidor, borra la cookie y envía Clear-Site-Data para que el navegador limpie " +
                "caché, almacenamiento y cookies del origen. Responde 204 aunque no haya sesión.")
            .Produces(StatusCodes.Status204NoContent);

        identidad.MapSesiones();

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
