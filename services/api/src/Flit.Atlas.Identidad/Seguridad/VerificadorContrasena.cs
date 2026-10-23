using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Seguridad;

public enum ResultadoVerificacion
{
    Valida,
    Invalida,
    Bloqueada,
}

/// <summary>
/// Verificación de la contraseña de una cuenta local con <b>bloqueo progresivo</b> (ATLAS-ADR-0022). Hoy los empleados
/// entran por Entra ID, sin contraseña en Atlas; el ingreso local de los externos (HU 1.5) debe pasar por aquí en lugar
/// de llamar a <see cref="UserManager{TUser}.CheckPasswordAsync"/>.
/// <list type="bullet">
/// <item>El conteo y el bloqueo son los de ASP.NET Core Identity: <see cref="IdentidadDbContext.IntentosAntesDelBloqueo"/>
/// fallos seguidos bloquean la cuenta.</item>
/// <item>Progresivo: el primer bloqueo dura <see cref="IdentidadDbContext.BloqueoInicial"/> y cada bloqueo más en las
/// últimas 24 h duplica la duración, hasta <see cref="BloqueoMaximo"/>.</item>
/// <item>Cada fallo, bloqueo e intento sobre una cuenta bloqueada queda en los eventos de seguridad.</item>
/// </list>
/// </summary>
public sealed class VerificadorContrasena(
    UserManager<UsuarioAtlas> usuarios,
    IdentidadDbContext contexto,
    RegistroSeguridad registro,
    TimeProvider reloj)
{
    public static readonly TimeSpan BloqueoMaximo = TimeSpan.FromHours(24);

    public Task<ResultadoVerificacion> VerificarAsync(UsuarioAtlas usuario, string contrasena, CancellationToken ct = default) =>
        VerificarFactorAsync(usuario, "contrasena", TiposEventoSeguridad.InicioFallido, () => usuarios.CheckPasswordAsync(usuario, contrasena), ct);

    /// <summary>
    /// Código TOTP del segundo paso del ingreso (HU 1.5), con el mismo conteo y bloqueo progresivo que la contraseña: los
    /// fallos de uno y otro se suman (ASVS V2.2.1).
    /// </summary>
    public Task<ResultadoVerificacion> VerificarTotpAsync(UsuarioAtlas usuario, string codigo, CancellationToken ct = default) =>
        VerificarFactorAsync(usuario, "totp", TiposEventoSeguridad.MfaFallido,
            () => usuarios.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultAuthenticatorProvider, Normalizar(codigo)), ct);

    /// <summary>Código de recuperación (de un solo uso) en lugar del TOTP; también cuenta para el bloqueo.</summary>
    public Task<ResultadoVerificacion> CanjearCodigoRecuperacionAsync(UsuarioAtlas usuario, string codigo, CancellationToken ct = default) =>
        VerificarFactorAsync(usuario, "codigo_recuperacion", TiposEventoSeguridad.MfaFallido,
            async () => (await usuarios.RedeemTwoFactorRecoveryCodeAsync(usuario, codigo ?? string.Empty)).Succeeded, ct);

    private static string Normalizar(string? codigo) =>
        (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    private async Task<ResultadoVerificacion> VerificarFactorAsync(
        UsuarioAtlas usuario, string metodo, string tipoFallo, Func<Task<bool>> comprobar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = usuario.PersonaId };

        if (await usuarios.IsLockedOutAsync(usuario))
        {
            // Ni siquiera se compara el factor: un bloqueo no se puede usar para adivinarlo.
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado,
                datos with { Detalle = new { metodo, motivo = "cuenta_bloqueada" } }, ct);
            return ResultadoVerificacion.Bloqueada;
        }

        if (await comprobar())
        {
            await usuarios.ResetAccessFailedCountAsync(usuario);
            return ResultadoVerificacion.Valida;
        }

        // Identity suma el fallo y, al llegar al máximo, bloquea la cuenta y reinicia el conteo.
        var intentos = await usuarios.GetAccessFailedCountAsync(usuario) + 1;
        await usuarios.AccessFailedAsync(usuario);
        await registro.RegistrarAsync(tipoFallo, datos with { Detalle = new { metodo, intentos } }, ct);

        if (!await usuarios.IsLockedOutAsync(usuario))
        {
            return ResultadoVerificacion.Invalida;
        }

        var ahora = reloj.GetUtcNow();
        var desde = ahora - TimeSpan.FromHours(24);
        var previos = await contexto.EventosSeguridad.CountAsync(
            e => e.UsuarioId == usuario.Id && e.Tipo == TiposEventoSeguridad.CuentaBloqueada && e.OcurridoEn > desde, ct);
        var duracion = DuracionBloqueo(previos);
        await usuarios.SetLockoutEndDateAsync(usuario, ahora + duracion);
        await registro.RegistrarAsync(TiposEventoSeguridad.CuentaBloqueada,
            datos with { Detalle = new { metodo, minutos = (int)duracion.TotalMinutes, bloqueos_previos = previos } }, ct);
        return ResultadoVerificacion.Bloqueada;
    }

    /// <summary>15 min, 30 min, 1 h, 2 h… hasta 24 h, según los bloqueos de las últimas 24 h.</summary>
    public static TimeSpan DuracionBloqueo(int bloqueosPrevios)
    {
        var factor = Math.Pow(2, Math.Clamp(bloqueosPrevios, 0, 16));
        var duracion = TimeSpan.FromTicks((long)Math.Min(IdentidadDbContext.BloqueoInicial.Ticks * factor, BloqueoMaximo.Ticks));
        return duracion;
    }
}
