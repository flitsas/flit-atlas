using System.ComponentModel;
using System.Text;
using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace Flit.Atlas.Identidad.Externos;

[Description("Opciones para que el navegador cree o use una passkey (WebAuthn) y el id del paso que las acompaña.")]
public sealed record OpcionesPasskey(
    [property: Description("Id del paso; se devuelve con la respuesta del autenticador. Vence en 5 minutos y sirve una sola vez.")] string Estado,
    [property: Description("PublicKeyCredentialCreationOptionsJSON o PublicKeyCredentialRequestOptionsJSON (WebAuthn nivel 3), como texto JSON.")] string Opciones);

[Description("Clave TOTP nueva para registrar en la aplicación de autenticación.")]
public sealed record ClaveTotp(
    [property: Description("Id del paso; vence en 5 minutos.")] string Estado,
    [property: Description("Clave en base32, en grupos de 4, para escribirla a mano.")] string Clave,
    [property: Description("URI otpauth:// (la misma que lleva el QR).")] string Uri,
    [property: Description("QR de la URI como imagen SVG en data URI (img-src data: de la CSP).")] string Qr);

[Description("Factor con el que se crea o se recupera una cuenta externa: passkey (preferida) o contraseña más TOTP.")]
public sealed record SolicitudFactor(
    [property: Description("passkey o contrasena.")] string Metodo,
    [property: Description("Id del paso de las opciones de passkey o de la clave TOTP.")] string Estado,
    [property: Description("Con passkey: la respuesta del autenticador (PublicKeyCredential.toJSON()) como texto JSON.")] string? Credencial,
    [property: Description("Con contraseña: mínimo 12 caracteres; se revisa contra contraseñas filtradas.")] string? Contrasena,
    [property: Description("Con contraseña: el código de 6 dígitos que muestra la aplicación de autenticación.")] string? Codigo);

/// <summary>Error de un factor: código estable y mensaje para la persona.</summary>
public sealed record ErrorFactor(string Codigo, string Mensaje);

/// <summary>Factor ya verificado (sin escribir nada todavía).</summary>
public abstract record FactorPreparado(string Metodo);

public sealed record PasskeyPreparada(UserPasskeyInfo Passkey, Guid UsuarioId) : FactorPreparado(MetodosExternos.Passkey);

public sealed record ContrasenaPreparada(string Contrasena, string ClaveTotp, string Codigo, string Estado) : FactorPreparado(MetodosExternos.Contrasena);

/// <summary>
/// Registro de los factores de una cuenta externa, para la aceptación de la invitación y la recuperación de la cuenta
/// (§6.2, ATLAS-ADR-0025). Todo lo criptográfico es de ASP.NET Core Identity: <see cref="IPasskeyHandler{TUser}"/>
/// (WebAuthn nativo de .NET 10), el hash de contraseñas, el TOTP del <c>AuthenticatorTokenProvider</c> y los códigos de
/// recuperación. El desafío de WebAuthn y la clave TOTP sin confirmar viven en <see cref="PasosTemporales"/>.
/// </summary>
public sealed class FactoresExternos(
    UserManager<UsuarioAtlas> usuarios,
    IUserStore<UsuarioAtlas> almacen,
    IPasskeyHandler<UsuarioAtlas> passkeys,
    PasosTemporales pasos)
{
    public const string Emisor = "Flit Atlas";
    public const int CodigosRecuperacion = 10;

    private const string TipoRegistroPasskey = "registro-passkey";
    private const string TipoTotp = "totp";

    private sealed record PasoRegistroPasskey(string Estado, Guid UsuarioId, string Ambito);

    private sealed record PasoTotp(string Clave, string Ambito);

    /// <param name="ambito">A qué pertenece el paso (<c>invitacion:&lt;id&gt;</c> o <c>recuperacion:&lt;usuario&gt;</c>): un paso no sirve en otro.</param>
    public async Task<OpcionesPasskey> OpcionesRegistroAsync(Guid usuarioId, string correo, string nombre, string ambito, HttpContext http)
    {
        var resultado = await passkeys.MakeCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = usuarioId.ToString(),
            Name = correo,
            DisplayName = nombre,
        }, http);
        var estado = await pasos.GuardarAsync(TipoRegistroPasskey, new PasoRegistroPasskey(resultado.AttestationState!, usuarioId, ambito));
        return new OpcionesPasskey(estado, resultado.CreationOptionsJson);
    }

    public async Task<ClaveTotp> NuevaClaveTotpAsync(string correo, string ambito)
    {
        var clave = usuarios.GenerateNewAuthenticatorKey();
        var estado = await pasos.GuardarAsync(TipoTotp, new PasoTotp(clave, ambito));
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Emisor)}:{Uri.EscapeDataString(correo)}?secret={clave}" +
            $"&issuer={Uri.EscapeDataString(Emisor)}&digits=6&period=30";
        return new ClaveTotp(estado, Agrupar(clave), uri, Qr(uri));
    }

    /// <summary>Verifica el factor sin escribir nada: la passkey contra su desafío y la contraseña contra las reglas.</summary>
    public async Task<(FactorPreparado? Factor, ErrorFactor? Error)> PrepararAsync(
        SolicitudFactor solicitud, string ambito, UsuarioAtlas usuarioParaValidar, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (solicitud.Metodo == MetodosExternos.Passkey)
        {
            var paso = await pasos.TomarAsync<PasoRegistroPasskey>(TipoRegistroPasskey, solicitud.Estado);
            if (paso is null || paso.Ambito != ambito || string.IsNullOrEmpty(solicitud.Credencial))
            {
                return (null, new ErrorFactor("paso_vencido", "La solicitud de la passkey venció. Vuelve a intentarlo."));
            }
            var resultado = await passkeys.PerformAttestationAsync(new PasskeyAttestationContext
            {
                CredentialJson = solicitud.Credencial,
                AttestationState = paso.Estado,
                HttpContext = http,
            });
            if (!resultado.Succeeded || resultado.UserEntity.Id != paso.UsuarioId.ToString())
            {
                return (null, new ErrorFactor("passkey_invalida", "No se pudo registrar la passkey. Vuelve a intentarlo."));
            }
            return (new PasskeyPreparada(resultado.Passkey, paso.UsuarioId), null);
        }
        if (solicitud.Metodo == MetodosExternos.Contrasena)
        {
            var paso = await pasos.LeerAsync<PasoTotp>(TipoTotp, solicitud.Estado);
            if (paso is null || paso.Ambito != ambito)
            {
                return (null, new ErrorFactor("paso_vencido", "La clave de la aplicación de autenticación venció. Genera una nueva."));
            }
            if (string.IsNullOrEmpty(solicitud.Codigo))
            {
                return (null, new ErrorFactor("codigo_invalido", "Escribe el código de 6 dígitos de tu aplicación de autenticación."));
            }
            var errores = new List<IdentityError>();
            foreach (var validador in usuarios.PasswordValidators)
            {
                var r = await validador.ValidateAsync(usuarios, usuarioParaValidar, solicitud.Contrasena ?? string.Empty);
                errores.AddRange(r.Errors);
            }
            if (errores.Count > 0)
            {
                return (null, new ErrorFactor("contrasena_invalida", string.Join(" ", errores.Select(Mensaje).Distinct())));
            }
            return (new ContrasenaPreparada(solicitud.Contrasena!, paso.Clave, solicitud.Codigo, solicitud.Estado), null);
        }
        return (null, new ErrorFactor("metodo_invalido", "El método debe ser passkey o contrasena."));
    }

    /// <summary>
    /// Escribe el factor en la cuenta (dentro de la transacción de quien llama). Con contraseña, comprueba el código TOTP
    /// contra la clave recién guardada: si no coincide, devuelve el error y quien llama deshace la transacción.
    /// </summary>
    public async Task<ErrorFactor?> AplicarAsync(UsuarioAtlas usuario, FactorPreparado factor)
    {
        ArgumentNullException.ThrowIfNull(factor);
        switch (factor)
        {
            case PasskeyPreparada passkey:
                Exigir(await usuarios.AddOrUpdatePasskeyAsync(usuario, passkey.Passkey));
                return null;
            case ContrasenaPreparada contrasena:
                var agregada = await usuarios.AddPasswordAsync(usuario, contrasena.Contrasena);
                if (!agregada.Succeeded)
                {
                    return new ErrorFactor("contrasena_invalida", string.Join(" ", agregada.Errors.Select(Mensaje).Distinct()));
                }
                await ((IUserAuthenticatorKeyStore<UsuarioAtlas>)almacen).SetAuthenticatorKeyAsync(usuario, contrasena.ClaveTotp, CancellationToken.None);
                Exigir(await usuarios.UpdateAsync(usuario));
                var codigo = contrasena.Codigo.Replace(" ", string.Empty, StringComparison.Ordinal);
                if (!await usuarios.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultAuthenticatorProvider, codigo))
                {
                    return new ErrorFactor("codigo_invalido", "El código no coincide. Revisa la hora del teléfono y escribe el código actual.");
                }
                Exigir(await usuarios.SetTwoFactorEnabledAsync(usuario, true));
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(factor));
        }
    }

    /// <summary>Borra la clave TOTP pendiente una vez usada.</summary>
    public Task OlvidarAsync(FactorPreparado factor) =>
        factor is ContrasenaPreparada c ? pasos.BorrarAsync(TipoTotp, c.Estado) : Task.CompletedTask;

    /// <summary>Diez códigos de recuperación nuevos (reemplazan los anteriores). Se muestran una sola vez.</summary>
    public async Task<IReadOnlyList<string>> NuevosCodigosAsync(UsuarioAtlas usuario) =>
        [.. await usuarios.GenerateNewTwoFactorRecoveryCodesAsync(usuario, CodigosRecuperacion) ?? []];

    /// <summary>Quita todos los factores: passkeys, contraseña, clave TOTP y códigos de recuperación.</summary>
    public async Task<IReadOnlyList<string>> RetirarTodosAsync(UsuarioAtlas usuario)
    {
        var retirados = new List<string>();
        foreach (var passkey in await usuarios.GetPasskeysAsync(usuario))
        {
            Exigir(await usuarios.RemovePasskeyAsync(usuario, passkey.CredentialId));
            retirados.Add(MetodosExternos.Passkey);
        }
        if (await usuarios.HasPasswordAsync(usuario))
        {
            Exigir(await usuarios.RemovePasswordAsync(usuario));
            retirados.Add(MetodosExternos.Contrasena);
        }
        if (await usuarios.GetAuthenticatorKeyAsync(usuario) is not null)
        {
            Exigir(await usuarios.RemoveAuthenticationTokenAsync(usuario, "[AspNetUserStore]", "AuthenticatorKey"));
            retirados.Add("totp");
        }
        Exigir(await usuarios.RemoveAuthenticationTokenAsync(usuario, "[AspNetUserStore]", "RecoveryCodes"));
        Exigir(await usuarios.SetTwoFactorEnabledAsync(usuario, false));
        return [.. retirados.Distinct()];
    }

    /// <summary>¿La cuenta tiene un segundo factor? Una passkey, o TOTP activo (con contraseña).</summary>
    public async Task<bool> TieneMfaAsync(UsuarioAtlas usuario) =>
        (await usuarios.GetPasskeysAsync(usuario)).Count > 0
        || (await usuarios.GetTwoFactorEnabledAsync(usuario) && await usuarios.GetAuthenticatorKeyAsync(usuario) is not null);

    private static string Agrupar(string clave)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < clave.Length; i += 4)
        {
            sb.Append(clave.AsSpan(i, Math.Min(4, clave.Length - i))).Append(' ');
        }
        return sb.ToString().TrimEnd().ToLowerInvariant();
    }

    private static string Qr(string uri)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(datos).GetGraphic(4);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    private static string Mensaje(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "La contraseña debe tener al menos 12 caracteres.",
        "PasswordRequiresUniqueChars" => "La contraseña repite demasiado los mismos caracteres.",
        _ => error.Description,
    };

    private static void Exigir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException("Identity rechazó el cambio: " + string.Join("; ", resultado.Errors.Select(e => e.Code)));
        }
    }
}
