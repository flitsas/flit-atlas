using System.Globalization;
using System.Security.Claims;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Entra;

/// <summary>
/// Vuelta de Entra ID: valida la cuenta, aprovisiona al empleado y cambia el principal de Microsoft por el de Atlas.
/// La cookie del BFF solo recibe claims de Atlas (ATLAS-ADR-0004 §3).
/// </summary>
internal static class IngresoEntra
{
    /// <summary>Marca, en las propiedades del desafío, un ingreso pedido por una acción sensible.</summary>
    public const string ClaveReautenticar = "atlas.reautenticar";

    /// <summary>Si el ingreso es una reautenticación, Entra debe pedir las credenciales otra vez.</summary>
    public static Task AlRedirigirAsync(RedirectContext ctx)
    {
        if (ctx.Properties.Items.ContainsKey(ClaveReautenticar))
        {
            ctx.ProtocolMessage.Prompt = "login";
        }
        return Task.CompletedTask;
    }

    public static async Task AlValidarTokenAsync(TokenValidatedContext ctx, Guid tenantFlit)
    {
        var servicios = ctx.HttpContext.RequestServices;
        var ct = ctx.HttpContext.RequestAborted;
        var validacion = ValidadorCuentaEntra.Validar(ctx.Principal!, tenantFlit);
        if (!validacion.Valida)
        {
            await AuditarRechazoAsync(servicios, validacion.Motivo!, ct).ConfigureAwait(false);
            ctx.HandleResponse();
            ctx.Response.Redirect("/?error=cuenta_no_permitida");
            return;
        }

        var principal = ctx.Principal!;
        var correo = ValidadorCuentaEntra.Valor(principal, "email", ClaimTypes.Email, "preferred_username", ClaimTypes.Upn) ?? string.Empty;
        var nombre = ValidadorCuentaEntra.Valor(principal, "name", ClaimTypes.Name) ?? correo;
        var amr = principal.FindAll("amr").Concat(principal.FindAll("http://schemas.microsoft.com/claims/authnmethodsreferences"))
            .Select(c => c.Value).ToList();

        // auth_time es un claim opcional del registro de la app en Entra; si no viene, cuenta el momento del ingreso.
        DateTimeOffset? autenticadoEn = long.TryParse(principal.FindFirstValue("auth_time"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var authTime) ? DateTimeOffset.FromUnixTimeSeconds(authTime) : null;
        var reautenticacion = ctx.Properties?.Items.ContainsKey(ClaveReautenticar) == true;

        var perfil = await servicios.GetRequiredService<IPerfilEmpleados>().ObtenerAsync(validacion.Oid, ct).ConfigureAwait(false);
        var cuenta = new CuentaEmpleado(
            AprovisionadorEmpleados.ProveedorEntra, validacion.Oid.ToString(), validacion.Oid, correo, nombre,
            perfil.Grupos, perfil.Cargo, perfil.Area, amr, autenticadoEn, reautenticacion);

        try
        {
            ctx.Principal = await servicios.GetRequiredService<AprovisionadorEmpleados>()
                .AprovisionarAsync(cuenta, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IngresoNoPermitidoException or CorreoEnUsoException)
        {
            await AuditarRechazoAsync(servicios, ex is IngresoNoPermitidoException noPermitido ? noPermitido.Motivo : "correo_en_uso", ct)
                .ConfigureAwait(false);
            ctx.HandleResponse();
            ctx.Response.Redirect("/?error=cuenta_no_permitida");
            return;
        }

        ctx.Properties ??= new AuthenticationProperties();
        ctx.Properties.IsPersistent = true;
        ctx.Properties.SetString(AlmacenSesiones.ClavesPropiedades.Agente, ResumenAgente.De(ctx.Request.Headers.UserAgent));
    }

    public static Task AlFallarAsync(RemoteFailureContext ctx)
    {
        // Cancelaciones, errores de Entra (p. ej. AADSTS50020: cuenta de otro tenant) o un estado vencido.
        ctx.HandleResponse();
        ctx.Response.Redirect("/?error=inicio_fallido");
        return Task.CompletedTask;
    }

    private static Task AuditarRechazoAsync(IServiceProvider servicios, string motivo, CancellationToken ct) =>
        servicios.GetRequiredService<RegistroSeguridad>().RegistrarAsync(TiposEventoSeguridad.InicioRechazado,
            new DatosEventoSeguridad { Detalle = new { proveedor = AprovisionadorEmpleados.ProveedorEntra, motivo } }, ct);
}

/// <summary>Resumen corto del navegador para «dispositivos conectados», sin guardar el user agent completo.</summary>
internal static class ResumenAgente
{
    public static string De(string? agente)
    {
        if (string.IsNullOrWhiteSpace(agente))
        {
            return "Desconocido";
        }
        var navegador = agente.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : agente.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : agente.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : agente.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "Otro navegador";
        var sistema = agente.Contains("Android", StringComparison.Ordinal) ? "Android"
            : agente.Contains("iPhone", StringComparison.Ordinal) || agente.Contains("iPad", StringComparison.Ordinal) ? "iOS"
            : agente.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : agente.Contains("Mac OS X", StringComparison.Ordinal) ? "macOS"
            : agente.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : "otro sistema";
        return $"{navegador} en {sistema}";
    }
}
