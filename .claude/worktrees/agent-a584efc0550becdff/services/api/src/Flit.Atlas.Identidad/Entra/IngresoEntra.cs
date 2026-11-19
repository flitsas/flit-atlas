using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
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

        var perfil = await servicios.GetRequiredService<IPerfilEmpleados>().ObtenerAsync(validacion.Oid, ct).ConfigureAwait(false);
        var cuenta = new CuentaEmpleado(
            AprovisionadorEmpleados.ProveedorEntra, validacion.Oid.ToString(), validacion.Oid, correo, nombre,
            perfil.Grupos, perfil.Cargo, perfil.Area, amr);

        try
        {
            ctx.Principal = await servicios.GetRequiredService<AprovisionadorEmpleados>()
                .AprovisionarAsync(cuenta, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or CorreoEnUsoException)
        {
            await AuditarRechazoAsync(servicios, ex is UnauthorizedAccessException ? "persona_inactiva" : "correo_en_uso", ct)
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

    private static async Task AuditarRechazoAsync(IServiceProvider servicios, string motivo, CancellationToken ct)
    {
        var plataforma = servicios.GetRequiredService<PlataformaDbContext>();
        plataforma.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = servicios.GetRequiredService<TimeProvider>().GetUtcNow(),
            ActorTipo = TipoActor.Sistema,
            Accion = "identidad.inicio_rechazado",
            RecursoTipo = "usuario",
            Datos = JsonSerializer.Serialize(new { proveedor = AprovisionadorEmpleados.ProveedorEntra, motivo }),
        });
        await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
    }
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
