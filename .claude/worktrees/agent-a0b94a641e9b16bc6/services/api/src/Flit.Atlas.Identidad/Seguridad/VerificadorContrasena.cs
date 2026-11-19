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

    public async Task<ResultadoVerificacion> VerificarAsync(UsuarioAtlas usuario, string contrasena, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = usuario.PersonaId };

        if (await usuarios.IsLockedOutAsync(usuario))
        {
            // Ni siquiera se compara la contraseña: un bloqueo no se puede usar para adivinarla.
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado,
                datos with { Detalle = new { metodo = "contrasena", motivo = "cuenta_bloqueada" } }, ct);
            return ResultadoVerificacion.Bloqueada;
        }

        if (await usuarios.CheckPasswordAsync(usuario, contrasena))
        {
            await usuarios.ResetAccessFailedCountAsync(usuario);
            return ResultadoVerificacion.Valida;
        }

        // Identity suma el fallo y, al llegar al máximo, bloquea la cuenta y reinicia el conteo.
        var intentos = await usuarios.GetAccessFailedCountAsync(usuario) + 1;
        await usuarios.AccessFailedAsync(usuario);
        await registro.RegistrarAsync(TiposEventoSeguridad.InicioFallido,
            datos with { Detalle = new { metodo = "contrasena", intentos } }, ct);

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
            datos with { Detalle = new { metodo = "contrasena", minutos = (int)duracion.TotalMinutes, bloqueos_previos = previos } }, ct);
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
