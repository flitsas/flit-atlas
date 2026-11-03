using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Emergencia;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Seguridad de la propia cuenta local (F4-SEC-31, ATLAS-ADR-0049 §10), bajo <c>/api/v1/identidad/cuenta</c>: ver los
/// factores, cambiar la contraseña, generar códigos de recuperación nuevos y agregar una passkey. Solo para cuentas
/// locales (externos y la cuenta de emergencia); los empleados administran sus factores en Microsoft. Todo cambio exige
/// sesión, reautenticación (ingreso de hace menos de 10 minutos, ATLAS-ADR-0022) y, para la contraseña, la actual (ASVS
/// V6.2.3); queda en los eventos de seguridad y en la auditoría, y se avisa al titular por correo, sin enlaces.
/// </summary>
public static class EndpointsSeguridadCuenta
{
    [Description("Factores de la cuenta local. Nunca trae secretos: ni la clave TOTP ni los códigos.")]
    public sealed record SeguridadCuenta(
        [property: Description("true si la cuenta tiene contraseña (con TOTP).")] bool Contrasena,
        [property: Description("true si la aplicación de autenticación (TOTP) está activa.")] bool Totp,
        IReadOnlyList<PasskeyCuenta> Passkeys,
        [property: Description("Códigos de recuperación que aún no se usan.")] int CodigosRecuperacionRestantes);

    [Description("Passkey registrada en la cuenta.")]
    public sealed record PasskeyCuenta(
        [property: Description("Nombre que dio el autenticador, si lo dio.")] string? Nombre,
        DateTimeOffset CreadaEn);

    [Description("Contraseña actual y la nueva (mínimo 12 caracteres; se revisa contra contraseñas filtradas).")]
    public sealed record SolicitudCambioContrasena(string ContrasenaActual, string ContrasenaNueva);

    [Description("Respuesta del autenticador a las opciones de POST /api/v1/identidad/cuenta/passkeys/opciones.")]
    public sealed record SolicitudPasskeyNueva(
        [property: Description("Id del paso de las opciones.")] string Estado,
        [property: Description("PublicKeyCredential.toJSON() de navigator.credentials.create(), como texto JSON.")] string Credencial);

    [Description("Códigos de recuperación nuevos: reemplazan a los anteriores y se muestran esta única vez.")]
    public sealed record CodigosRecuperacionNuevos(IReadOnlyList<string> CodigosRecuperacion);

    /// <summary>Passkeys por cuenta: suficientes para varios dispositivos y llaves de respaldo.</summary>
    public const int MaximoPasskeys = 10;

    internal static string AmbitoCuenta(Guid usuarioId) => $"cuenta:{usuarioId}";

    internal static void MapSeguridadCuenta(this RouteGroupBuilder identidad)
    {
        var cuenta = identidad.MapGroup("/cuenta").RequireAuthorization();

        cuenta.MapGet("/seguridad", async (ClaimsPrincipal principal, UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, CancellationToken ct) =>
        {
            var (local, error) = await CuentaLocalAsync(principal, usuarios, plataforma, ct);
            if (local is null)
            {
                return error!;
            }
            var usuario = local.Usuario;
            var passkeys = await usuarios.GetPasskeysAsync(usuario);
            return Results.Ok(new SeguridadCuenta(
                await usuarios.HasPasswordAsync(usuario),
                await usuarios.GetTwoFactorEnabledAsync(usuario) && await usuarios.GetAuthenticatorKeyAsync(usuario) is not null,
                [.. passkeys.OrderBy(p => p.CreatedAt).Select(p => new PasskeyCuenta(p.Name, p.CreatedAt))],
                await usuarios.CountRecoveryCodesAsync(usuario)));
        })
            .WithName("VerSeguridadDeMiCuenta")
            .WithSummary("Factores de mi cuenta")
            .WithDescription(
                "Contraseña, aplicación de autenticación, passkeys y cuántos códigos de recuperación quedan, de la propia cuenta " +
                "local. 409 cuenta_microsoft si la sesión es de un empleado (sus factores se administran en Microsoft).")
            .Produces<SeguridadCuenta>()
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        cuenta.MapPost("/contrasena", CambiarContrasenaAsync)
            .RequiereReautenticacion()
            .WithName("CambiarMiContrasena")
            .WithSummary("Cambiar mi contraseña")
            .WithDescription(
                "Exige la contraseña actual y un ingreso de hace menos de 10 minutos (401 reautenticacion_requerida). La nueva " +
                "debe tener 12 o más caracteres, ser distinta de la actual y no estar entre las contraseñas filtradas. Una " +
                "contraseña actual errada suma al bloqueo progresivo. Cierra las demás sesiones y los tokens de las aplicaciones " +
                "conectadas, renueva la de esta petición y avisa al titular por correo. 409 sin_contrasena si la cuenta solo " +
                "tiene passkeys.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status403Forbidden)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        cuenta.MapPost("/codigos-recuperacion", RegenerarCodigosAsync)
            .RequiereReautenticacion()
            .WithName("RegenerarMisCodigosDeRecuperacion")
            .WithSummary("Generar códigos de recuperación nuevos")
            .WithDescription(
                "Diez códigos nuevos de un solo uso que reemplazan a los anteriores (se muestran esta única vez). Exige un ingreso " +
                "de hace menos de 10 minutos y avisa al titular por correo.")
            .Produces<CodigosRecuperacionNuevos>()
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        cuenta.MapPost("/passkeys/opciones", OpcionesPasskeyAsync)
            .RequiereReautenticacion()
            .WithName("OpcionesPasskeyDeMiCuenta")
            .WithSummary("Opciones para agregar una passkey")
            .WithDescription(
                $"Desafío de WebAuthn (navigator.credentials.create) para una passkey más de la propia cuenta, sin repetir las ya " +
                $"registradas. Vence en 5 minutos y sirve una sola vez. Exige un ingreso de hace menos de 10 minutos. 409 " +
                $"demasiadas_passkeys con {MaximoPasskeys}.")
            .Produces<OpcionesPasskey>()
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        cuenta.MapPost("/passkeys", AgregarPasskeyAsync)
            .RequiereReautenticacion()
            .WithName("AgregarPasskeyAMiCuenta")
            .WithSummary("Agregar una passkey")
            .WithDescription(
                "Registra la passkey creada con las opciones anteriores (verificación de usuario obligatoria). Exige un ingreso de " +
                "hace menos de 10 minutos y avisa al titular por correo.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);
    }

    /// <summary>La cuenta local de la sesión: usuario y persona.</summary>
    private sealed record CuentaLocal(UsuarioAtlas Usuario, Guid PersonaId, Guid OrganizacionId, string Nombre, string Correo)
    {
        public DatosEventoSeguridad Datos(ClaimsPrincipal principal) =>
            DatosEventoSeguridad.DeSesion(principal) with { UsuarioId = Usuario.Id, PersonaId = PersonaId, OrganizacionId = OrganizacionId };
    }

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorExternos(codigo, mensaje), statusCode: estado);

    private static async Task<(CuentaLocal? Cuenta, IResult? Error)> CuentaLocalAsync(
        ClaimsPrincipal principal, UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var proveedor = principal.FindFirstValue(ClaimsAtlas.Proveedor);
        var usuario = proveedor is MetodosExternos.ProveedorLocal or CuentaEmergencia.Proveedor
            && principal.FindFirstValue(ClaimsAtlas.Sub) is { } sub ? await usuarios.FindByIdAsync(sub) : null;
        if (usuario is null)
        {
            return (null, Error(StatusCodes.Status409Conflict, "cuenta_microsoft",
                "Tu cuenta entra con Microsoft: la contraseña y los factores se administran en tu cuenta de Microsoft."));
        }
        var persona = await plataforma.Personas.AsNoTracking().Where(p => p.Id == usuario.PersonaId)
            .Select(p => new { p.Id, p.OrganizacionId, p.Nombre }).SingleAsync(ct);
        return (new CuentaLocal(usuario, persona.Id, persona.OrganizacionId, persona.Nombre, usuario.Email!), null);
    }

    /// <summary>Avisa al titular por la cola en memoria (como el aviso de recuperación); si la cola está llena, lo registra.</summary>
    private static async Task AvisarAsync(CuentaLocal cuenta, string cambio, DatosEventoSeguridad datos, ColaCorreosIdentidad cola,
        RegistroSeguridad registro, TimeProvider reloj, CancellationToken ct)
    {
        if (!cola.Encolar(new CorreoPendiente(
            CorreosExternos.AvisoCambioSeguridad(cuenta.Correo, cuenta.Nombre, cambio, reloj.GetUtcNow()), datos, "aviso_cambio_seguridad")))
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.CorreoNoEnviado, datos with { Detalle = new { motivo = "aviso_cambio_seguridad" } }, ct);
        }
    }

    /// <summary>
    /// Si el cambio movió el sello de seguridad (o se pide), cierra las demás sesiones y los tokens de la persona y vuelve a
    /// firmar la sesión de esta petición con el sello nuevo: sigue abierta, con el mismo id y el mismo <c>auth_time</c>
    /// (no se gana tiempo de reautenticación). Si el sello no cambió, no toca nada.
    /// </summary>
    private static async Task RenovarSesionAsync(HttpContext http, CuentaLocal cuenta, string selloAnterior, bool cerrarLasDemas,
        UserManager<UsuarioAtlas> usuarios, IInvalidadorSesiones invalidador, CancellationToken ct)
    {
        var sid = Guid.TryParse(http.User.FindFirstValue(ClaimsAtlas.Sid), out var id) ? id : Guid.Empty;
        if (!cerrarLasDemas && (await usuarios.FindByIdAsync(cuenta.Usuario.Id.ToString()))!.SecurityStamp == selloAnterior)
        {
            return;
        }
        // Cambia el sello otra vez (las sesiones y los Bearer con el anterior dejan de servir) y revoca lo demás.
        await invalidador.InvalidarOtrasSesionesAsync(cuenta.PersonaId, sid, MotivosInvalidacion.CambioDeCredenciales, ct);
        var sello = (await usuarios.FindByIdAsync(cuenta.Usuario.Id.ToString()))!.SecurityStamp ?? string.Empty;
        var actual = await http.AuthenticateAsync(ClaimsAtlas.EsquemaCookie);
        if (!actual.Succeeded)
        {
            return;
        }
        var identidad = new ClaimsIdentity(actual.Principal.Claims.Where(c => c.Type != ClaimsAtlas.SelloSeguridad),
            ClaimsAtlas.EsquemaCookie, ClaimsAtlas.Nombre, ClaimsAtlas.Rol);
        identidad.AddClaim(new Claim(ClaimsAtlas.SelloSeguridad, sello));
        // El manejador de la cookie renueva la misma sesión (AlmacenSesiones.RenewAsync guarda el sello nuevo).
        await http.SignInAsync(ClaimsAtlas.EsquemaCookie, new ClaimsPrincipal(identidad), actual.Properties);
    }

    private static async Task<IResult> CambiarContrasenaAsync(
        [Description("Contraseña actual y nueva.")] SolicitudCambioContrasena solicitud, HttpContext http, UserManager<UsuarioAtlas> usuarios,
        PlataformaDbContext plataforma, VerificadorContrasena verificador, IInvalidadorSesiones invalidador, ColaCorreosIdentidad cola, RegistroSeguridad registro, TimeProvider reloj, CancellationToken ct)
    {
        var (cuenta, errorCuenta) = await CuentaLocalAsync(http.User, usuarios, plataforma, ct);
        if (cuenta is null)
        {
            return errorCuenta!;
        }
        var usuario = cuenta.Usuario;
        if (!await usuarios.HasPasswordAsync(usuario))
        {
            return Error(StatusCodes.Status409Conflict, "sin_contrasena", "Tu cuenta entra con passkey: no tiene una contraseña que cambiar.");
        }
        var actual = solicitud.ContrasenaActual ?? string.Empty;
        var nueva = solicitud.ContrasenaNueva ?? string.Empty;
        // La actual se verifica con el mismo contador y bloqueo progresivo que el ingreso (ATLAS-ADR-0049 §1).
        switch (await verificador.VerificarAsync(usuario, actual, ct))
        {
            case ResultadoVerificacion.Valida:
                break;
            case ResultadoVerificacion.Bloqueada:
                return Error(StatusCodes.Status403Forbidden, "cuenta_bloqueada",
                    "La cuenta quedó bloqueada un tiempo por intentos fallidos. Vuelve a intentarlo más tarde.");
            default:
                return Error(StatusCodes.Status400BadRequest, "contrasena_actual_invalida", "La contraseña actual no es correcta.");
        }
        if (string.Equals(actual, nueva, StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, "contrasena_invalida", "La contraseña nueva debe ser distinta de la actual.");
        }
        var errores = new List<IdentityError>();
        foreach (var validador in usuarios.PasswordValidators)
        {
            errores.AddRange((await validador.ValidateAsync(usuarios, usuario, nueva)).Errors);
        }
        if (errores.Count > 0)
        {
            return Error(StatusCodes.Status400BadRequest, "contrasena_invalida", string.Join(" ", errores.Select(FactoresExternos.Mensaje).Distinct()));
        }
        var sello = usuario.SecurityStamp ?? string.Empty;
        var cambio = await usuarios.ChangePasswordAsync(usuario, actual, nueva);
        if (!cambio.Succeeded)
        {
            return Error(StatusCodes.Status409Conflict, "cambio_concurrente", "La cuenta cambió mientras tanto. Vuelve a intentarlo.");
        }
        var datos = cuenta.Datos(http.User);
        await registro.RegistrarAsync(TiposEventoSeguridad.ContrasenaCambiada, datos, ct);
        // Una contraseña nueva cierra las demás sesiones y los tokens (ASVS V7.4.3), como la recuperación.
        await RenovarSesionAsync(http, cuenta, sello, cerrarLasDemas: true, usuarios, invalidador, ct);
        await AvisarAsync(cuenta, CorreosExternos.CambiosSeguridad.Contrasena, datos, cola, registro, reloj, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerarCodigosAsync(
        HttpContext http, UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, FactoresExternos factores,
        IInvalidadorSesiones invalidador, ColaCorreosIdentidad cola, RegistroSeguridad registro, TimeProvider reloj, CancellationToken ct)
    {
        var (cuenta, errorCuenta) = await CuentaLocalAsync(http.User, usuarios, plataforma, ct);
        if (cuenta is null)
        {
            return errorCuenta!;
        }
        if (!await factores.TieneMfaAsync(cuenta.Usuario))
        {
            return Error(StatusCodes.Status409Conflict, "sin_mfa", "Tu cuenta no tiene un segundo factor: recupérala para registrar uno.");
        }
        var sello = cuenta.Usuario.SecurityStamp ?? string.Empty;
        var codigos = await factores.NuevosCodigosAsync(cuenta.Usuario);
        var datos = cuenta.Datos(http.User);
        await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with
        {
            Detalle = new { metodo = "codigos_recuperacion", cantidad = codigos.Count, motivo = "regenerados" },
        }, ct);
        await RenovarSesionAsync(http, cuenta, sello, cerrarLasDemas: false, usuarios, invalidador, ct);
        await AvisarAsync(cuenta, CorreosExternos.CambiosSeguridad.CodigosRecuperacion, datos, cola, registro, reloj, ct);
        return Results.Ok(new CodigosRecuperacionNuevos(codigos));
    }

    private static async Task<IResult> OpcionesPasskeyAsync(
        HttpContext http, UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, FactoresExternos factores, CancellationToken ct)
    {
        var (cuenta, errorCuenta) = await CuentaLocalAsync(http.User, usuarios, plataforma, ct);
        if (cuenta is null)
        {
            return errorCuenta!;
        }
        if ((await usuarios.GetPasskeysAsync(cuenta.Usuario)).Count >= MaximoPasskeys)
        {
            return Error(StatusCodes.Status409Conflict, "demasiadas_passkeys", $"Tu cuenta ya tiene {MaximoPasskeys} passkeys.");
        }
        return Results.Ok(await factores.OpcionesRegistroAsync(cuenta.Usuario.Id, cuenta.Correo, cuenta.Nombre, AmbitoCuenta(cuenta.Usuario.Id), http));
    }

    private static async Task<IResult> AgregarPasskeyAsync(
        [Description("Paso de las opciones y respuesta del autenticador.")] SolicitudPasskeyNueva solicitud, HttpContext http,
        UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, FactoresExternos factores, IInvalidadorSesiones invalidador,
        ColaCorreosIdentidad cola, RegistroSeguridad registro, TimeProvider reloj, CancellationToken ct)
    {
        var (cuenta, errorCuenta) = await CuentaLocalAsync(http.User, usuarios, plataforma, ct);
        if (cuenta is null)
        {
            return errorCuenta!;
        }
        if ((await usuarios.GetPasskeysAsync(cuenta.Usuario)).Count >= MaximoPasskeys)
        {
            return Error(StatusCodes.Status409Conflict, "demasiadas_passkeys", $"Tu cuenta ya tiene {MaximoPasskeys} passkeys.");
        }
        // El paso es de esta cuenta (ámbito cuenta:<usuario>): uno de una invitación, una recuperación u otra cuenta no sirve.
        var (factor, errorFactor) = await factores.PrepararAsync(
            new SolicitudFactor(MetodosExternos.Passkey, solicitud.Estado, solicitud.Credencial, null, null), AmbitoCuenta(cuenta.Usuario.Id), cuenta.Usuario, http);
        if (errorFactor is not null)
        {
            return Error(StatusCodes.Status400BadRequest, errorFactor.Codigo, errorFactor.Mensaje);
        }
        if (factor is not PasskeyPreparada passkey || passkey.UsuarioId != cuenta.Usuario.Id)
        {
            return Error(StatusCodes.Status400BadRequest, "passkey_invalida", "No se pudo registrar la passkey. Vuelve a intentarlo.");
        }
        if (await usuarios.GetPasskeyAsync(cuenta.Usuario, passkey.Passkey.CredentialId) is not null)
        {
            return Error(StatusCodes.Status409Conflict, "passkey_existente", "Esa passkey ya está registrada en tu cuenta.");
        }
        var sello = cuenta.Usuario.SecurityStamp ?? string.Empty;
        await factores.AplicarAsync(cuenta.Usuario, passkey);
        var datos = cuenta.Datos(http.User);
        await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with
        {
            Detalle = new { metodo = MetodosExternos.Passkey, motivo = "cuenta" },
        }, ct);
        await RenovarSesionAsync(http, cuenta, sello, cerrarLasDemas: false, usuarios, invalidador, ct);
        await AvisarAsync(cuenta, CorreosExternos.CambiosSeguridad.Passkey, datos, cola, registro, reloj, ct);
        return Results.NoContent();
    }
}
