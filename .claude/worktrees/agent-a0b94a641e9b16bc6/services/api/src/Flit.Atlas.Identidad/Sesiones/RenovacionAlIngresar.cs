using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Flit.Atlas.Identidad.Sesiones;

/// <summary>
/// Sesión nueva en cada ingreso (OWASP ASVS V3.2.1). Si el navegador ya trae una sesión válida, el manejador de la
/// cookie la <i>renovaría</i> con el mismo id; así, una cookie robada seguiría sirviendo después de que su dueño
/// vuelva a entrar o se reautentique. En las rutas que inician sesión se aparta la cookie de la petición (el ingreso
/// crea una fila nueva) y <see cref="AlmacenSesiones"/> revoca la anterior como <c>reemplazada_por_ingreso</c>.
/// </summary>
internal static class RenovacionAlIngresar
{
    /// <summary>Clave de <see cref="HttpContext.Items"/> con el id de la sesión que el ingreso reemplaza.</summary>
    public const string ClaveSesionAnterior = "atlas.sesion_anterior";

    /// <summary>Claim con el id de la sesión dentro de la cookie (constante privada del manejador de ASP.NET Core).</summary>
    private const string ClaimIdSesion = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    public static void Usar(IApplicationBuilder app) =>
        app.Use((http, siguiente) =>
        {
            if (EsIngreso(http.Request))
            {
                Apartar(http);
            }
            return siguiente(http);
        });

    /// <summary>El retorno de Entra (POST con form_post, o GET si cambiara el modo de respuesta) y el simulado.</summary>
    private static bool EsIngreso(HttpRequest peticion) =>
        peticion.Path.Equals(EndpointsIdentidad.RutaRetornoEntra, StringComparison.OrdinalIgnoreCase)
        || (HttpMethods.IsPost(peticion.Method)
            && peticion.Path.Equals("/api/v1/identidad/simulado/entrar", StringComparison.OrdinalIgnoreCase));

    private static void Apartar(HttpContext http)
    {
        var opciones = http.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(ClaimsAtlas.EsquemaCookie);
        var valor = opciones.CookieManager.GetRequestCookie(http, ClaimsAtlas.NombreCookie);
        if (string.IsNullOrEmpty(valor))
        {
            return;
        }
        // Se descifra con el mismo formato de la cookie (Data Protection), sin criptografía propia.
        var clave = opciones.TicketDataFormat.Unprotect(valor)?.Principal.FindFirst(ClaimIdSesion)?.Value;
        if (clave is not null)
        {
            http.Items[ClaveSesionAnterior] = clave;
        }

        // La cookie (y sus trozos __Host-atlasC1, …) sale de la petición; las de correlación de OIDC se quedan.
        var restantes = http.Request.Cookies
            .Where(c => !c.Key.StartsWith(ClaimsAtlas.NombreCookie, StringComparison.Ordinal))
            .Select(c => new CookieHeaderValue(c.Key, Uri.EscapeDataString(c.Value)).ToString());
        http.Request.Headers.Cookie = string.Join("; ", restantes);
    }
}
