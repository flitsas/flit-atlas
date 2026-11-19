using System.ComponentModel;
using Flit.Atlas.Identidad.Externos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Atlas.Identidad.Emergencia;

/// <summary>
/// Activación de la cuenta de administrador de emergencia con el enlace que imprime el comando de consola
/// (ATLAS-ADR-0027), bajo <c>/api/v1/identidad/emergencia</c>. Es el mismo flujo de la recuperación de una cuenta
/// externa: públicos, con el límite de tasa de los externos, y apartan la cookie anterior.
/// </summary>
public static class EndpointsEmergencia
{
    [Description("Enlace de activación de la cuenta de emergencia (el fragmento de la URL que imprime el comando).")]
    public sealed record SolicitudEnlaceEmergencia(string Enlace);

    [Description("Enlace de activación y el factor de la cuenta de emergencia.")]
    public sealed record SolicitudActivacionEmergencia(string Enlace, SolicitudFactor Factor);

    private const string MensajeEnlace = "Este enlace ya se usó, venció o no es válido. Genera otro con el comando de emergencia.";

    internal static void MapEmergencia(this RouteGroupBuilder identidad)
    {
        var emergencia = identidad.MapGroup("/emergencia");
        emergencia.MapPost("/consultar", async ([Description("Enlace de activación.")] SolicitudEnlaceEmergencia solicitud, CuentaEmergencia cuenta, CancellationToken ct) =>
            {
                var publica = await cuenta.PublicaAsync(solicitud.Enlace, ct);
                if (publica is null)
                {
                    await cuenta.RechazarEnlaceAsync(ct);
                    return EnlaceNoValido();
                }
                return Results.Ok(publica);
            })
            .WithName("ConsultarActivacionEmergencia")
            .WithSummary("Ver la cuenta del enlace de emergencia")
            .WithDescription("Correo y nombre de la cuenta de administrador de emergencia. 410 si el enlace se usó, venció o no es válido. No requiere sesión.")
            .Produces<CuentaEmergenciaPublica>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);

        emergencia.MapPost("/passkey/opciones", async ([Description("Enlace de activación.")] SolicitudEnlaceEmergencia solicitud, CuentaEmergencia cuenta,
                FactoresExternos factores, HttpContext http, CancellationToken ct) =>
            {
                var usuario = await cuenta.UsuarioDelEnlaceAsync(solicitud.Enlace, ct);
                if (usuario is null)
                {
                    return EnlaceNoValido();
                }
                var publica = await cuenta.PublicaAsync(solicitud.Enlace, ct);
                return Results.Ok(await factores.OpcionesRegistroAsync(usuario.Id, usuario.Email!, publica!.Nombre, CuentaEmergencia.Ambito(usuario.Id), http));
            })
            .WithName("OpcionesPasskeyEmergencia")
            .WithSummary("Opciones para registrar la passkey de la cuenta de emergencia")
            .WithDescription("Desafío de WebAuthn (navigator.credentials.create). Vence en 5 minutos y sirve una sola vez. No requiere sesión.")
            .Produces<OpcionesPasskey>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);

        emergencia.MapPost("/totp/clave", async ([Description("Enlace de activación.")] SolicitudEnlaceEmergencia solicitud, CuentaEmergencia cuenta,
                FactoresExternos factores, CancellationToken ct) =>
            {
                var usuario = await cuenta.UsuarioDelEnlaceAsync(solicitud.Enlace, ct);
                return usuario is null
                    ? EnlaceNoValido()
                    : Results.Ok(await factores.NuevaClaveTotpAsync(usuario.Email!, CuentaEmergencia.Ambito(usuario.Id)));
            })
            .WithName("ClaveTotpEmergencia")
            .WithSummary("Clave TOTP para la cuenta de emergencia con contraseña")
            .WithDescription("Clave nueva y su QR (SVG) para la aplicación de autenticación. Se confirma con el primer código al activar. No requiere sesión.")
            .Produces<ClaveTotp>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);

        emergencia.MapPost("/activar", async ([Description("Enlace de activación y factor.")] SolicitudActivacionEmergencia solicitud, CuentaEmergencia cuenta,
                SesionExterna sesion, HttpContext http, CancellationToken ct) =>
            {
                var resultado = await cuenta.CompletarActivacionAsync(solicitud.Enlace, solicitud.Factor, http, sesion, ct);
                return resultado.Error is { } error ? Results.Json(error, statusCode: resultado.Estado) : Results.Ok(resultado.Valor);
            })
            .WithName("ActivarCuentaEmergencia")
            .WithSummary("Activar la cuenta de administrador de emergencia")
            .WithDescription(
                "Registra el factor (passkey, preferida, o contraseña de mínimo 12 caracteres con TOTP), genera diez códigos de " +
                "recuperación (se muestran una sola vez), cierra las demás sesiones de la cuenta y abre una nueva. El enlace deja de " +
                "servir. Cada activación es un evento de seguridad de alta severidad. No requiere sesión.")
            .Produces<CuentaLista>()
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
    }

    private static IResult EnlaceNoValido() =>
        Results.Json(new ErrorExternos(CuentasExternas.EnlaceNoValido, MensajeEnlace), statusCode: StatusCodes.Status410Gone);
}
