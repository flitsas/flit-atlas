using System.Security.Cryptography;
using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// El almacén de usuarios de ASP.NET Core Identity con dos cambios en cómo guarda los secretos del segundo factor
/// (ATLAS-ADR-0025), sin tocar los algoritmos de Identity:
/// <list type="bullet">
/// <item>La clave TOTP se guarda cifrada con Data Protection (Identity la guarda en claro en <c>identidad_usuario_token</c>).</item>
/// <item>Los códigos de recuperación se guardan como hashes del <see cref="IPasswordHasher{TUser}"/> de Identity
/// (PBKDF2), no en claro: quien lea la base no puede usarlos (ASVS V2.6).</item>
/// </list>
/// </summary>
public sealed class AlmacenUsuarios(
    IdentidadDbContext contexto,
    IPasswordHasher<UsuarioAtlas> hasher,
    IDataProtectionProvider proteccion,
    IdentityErrorDescriber? errores = null)
    : UserStore<UsuarioAtlas, RolIdentity, IdentidadDbContext, Guid>(contexto, errores)
{
    private const string Proveedor = "[AspNetUserStore]";
    private const string NombreCodigos = "RecoveryCodes";

    private readonly IDataProtector _protector = proteccion.CreateProtector("Flit.Atlas.Identidad.Totp.v1");

    public override Task SetAuthenticatorKeyAsync(UsuarioAtlas user, string key, CancellationToken cancellationToken) =>
        base.SetAuthenticatorKeyAsync(user, _protector.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(UsuarioAtlas user, CancellationToken cancellationToken)
    {
        var guardada = await base.GetAuthenticatorKeyAsync(user, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(guardada))
        {
            return null;
        }
        try
        {
            return _protector.Unprotect(guardada);
        }
        catch (CryptographicException)
        {
            // Llaves de Data Protection perdidas: la clave ya no se puede leer; la persona recupera la cuenta.
            return null;
        }
    }

    public override Task ReplaceCodesAsync(UsuarioAtlas user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        // El hash de Identity es base64 (sin «;»): el conteo de la clase base sigue funcionando.
        var hashes = recoveryCodes.Select(c => hasher.HashPassword(user, Normalizar(c)));
        return SetTokenAsync(user, Proveedor, NombreCodigos, string.Join(';', hashes), cancellationToken);
    }

    public override async Task<bool> RedeemCodeAsync(UsuarioAtlas user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var guardados = (await GetTokenAsync(user, Proveedor, NombreCodigos, cancellationToken).ConfigureAwait(false) ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var normalizado = Normalizar(code ?? string.Empty);
        // Se revisan todos (sin cortar en el primero que coincide) para que el tiempo no diga cuál era.
        string? usado = null;
        foreach (var hash in guardados)
        {
            if (hasher.VerifyHashedPassword(user, hash, normalizado) != PasswordVerificationResult.Failed && usado is null)
            {
                usado = hash;
            }
        }
        if (usado is null)
        {
            return false;
        }
        await SetTokenAsync(user, Proveedor, NombreCodigos, string.Join(';', guardados.Where(h => !ReferenceEquals(h, usado))), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>Mayúsculas y sin espacios: el código se puede escribir como se quiera.</summary>
    private static string Normalizar(string codigo) => codigo.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
