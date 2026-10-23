using System.Globalization;
using System.Security.Claims;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Reautenticación para acciones sensibles (ATLAS-ADR-0004 §6.4 y ATLAS-ADR-0022): la acción exige que el último
/// ingreso (claim <c>auth_time</c> de la sesión) tenga menos de <see cref="OpcionesSesion.VentanaReautenticacion"/>.
/// Si no, responde <b>401</b> con <c>codigo = "reautenticacion_requerida"</c>; la PWA lo distingue de una sesión
/// vencida y lleva al usuario a <c>GET /api/v1/identidad/entrar/microsoft?reautenticar=true&amp;volver=…</c>, que
/// fuerza a Entra a pedir las credenciales otra vez (<c>prompt=login</c>).
/// </summary>
public static class Reautenticacion
{
    public const string Codigo = "reautenticacion_requerida";

    private static readonly TimeSpan DesfaseTolerado = TimeSpan.FromMinutes(5);

    /// <summary>Exige un ingreso reciente en el endpoint o en el grupo.</summary>
    public static TBuilder RequiereReautenticacion<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;
            return EsReciente(http) ? await siguiente(contexto) : await RechazarAsync(http);
        });

    /// <summary>¿El último ingreso de la sesión está dentro de la ventana?</summary>
    public static bool EsReciente(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var ventana = http.RequestServices.GetRequiredService<OpcionesSesion>().VentanaReautenticacion;
        var ahora = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        // Se tolera un desfase pequeño de reloj con Entra ID.
        return MomentoIngreso(http.User) is { } ingreso && ingreso - ahora <= DesfaseTolerado && ahora - ingreso <= ventana;
    }

    /// <summary>Registra el rechazo y arma la respuesta que la PWA interpreta.</summary>
    public static async Task<IResult> RechazarAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var usuario = http.User;
        var ventana = http.RequestServices.GetRequiredService<OpcionesSesion>().VentanaReautenticacion;
        await http.RequestServices.GetRequiredService<RegistroSeguridad>().RegistrarAsync(
            TiposEventoSeguridad.ReautenticacionRequerida, DatosEventoSeguridad.DeSesion(usuario) with
            {
                Detalle = new { metodo = http.Request.Method, ruta = http.GetEndpoint()?.DisplayName },
            }, http.RequestAborted);

        var segundos = (int)ventana.TotalSeconds;
        http.Response.Headers.WWWAuthenticate =
            $"Atlas error=\"{Codigo}\", max_age=\"{segundos.ToString(CultureInfo.InvariantCulture)}\"";
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Vuelve a ingresar para confirmar esta acción.",
            extensions: new Dictionary<string, object?> { ["codigo"] = Codigo, ["max_edad_segundos"] = segundos });
    }

    internal static DateTimeOffset? MomentoIngreso(ClaimsPrincipal usuario) =>
        long.TryParse(usuario.FindFirstValue(ClaimsAtlas.AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos)
            : null;
}
