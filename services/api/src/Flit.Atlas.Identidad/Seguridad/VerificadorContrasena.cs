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
/// Verificación de los factores de una cuenta local con <b>bloqueo progresivo</b> (ATLAS-ADR-0022 y ATLAS-ADR-0049). Hoy
/// los empleados entran por Entra ID, sin contraseña en Atlas; el ingreso local de los externos (HU 1.5) y de la cuenta de
/// emergencia debe pasar por aquí en lugar de llamar a <see cref="UserManager{TUser}.CheckPasswordAsync"/>.
/// <list type="bullet">
/// <item>Dos contadores: los fallos de contraseña (<c>access_failed_count</c> de Identity) y los del segundo factor (TOTP o
/// código de recuperación, en <see cref="SegundoFactorUsuario"/>). <see cref="IdentidadDbContext.IntentosAntesDelBloqueo"/>
/// fallos seguidos de cualquiera de los dos bloquean la cuenta. La contraseña correcta <b>no</b> reinicia ninguno: se
/// reinician al terminar el ingreso (<see cref="IngresoCompletadoAsync"/>), así que repetir la contraseña no da intentos
/// nuevos para adivinar el TOTP (F4-SEC-01).</item>
/// <item>Cada fallo suma con un <c>UPDATE … SET n = n + 1 RETURNING n</c> atómico: los fallos concurrentes no se pierden
/// (F4-SEC-02), y no suma con la cuenta ya bloqueada, para que los fallos rezagados no la bloqueen dos veces. Las
/// escrituras sobre <c>identidad_usuario</c> cambian el sello de concurrencia, así que un
/// <c>UpdateAsync</c> de Identity con la fila vieja falla en lugar de pisar el bloqueo.</item>
/// <item>Una cuenta sin contraseña (solo passkey) no suma fallos de contraseña: nadie la bloquea escribiendo su correo
/// (F4-SEC-03). Sin contraseña que comparar, o con la cuenta bloqueada, se calcula el hash ficticio: el tiempo no dice nada
/// (F4-SEC-04).</item>
/// <item>Un código TOTP ya aceptado (o de un paso anterior) no sirve otra vez, en ningún camino (F4-SEC-05).</item>
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

    private enum Contador
    {
        Contrasena,
        SegundoFactor,
    }

    public async Task<ResultadoVerificacion> VerificarAsync(UsuarioAtlas usuario, string contrasena, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        const string metodo = "contrasena";
        if (await BloqueadaAsync(usuario, metodo, ct))
        {
            HashFicticio.Verificar(usuarios.PasswordHasher, contrasena);
            return ResultadoVerificacion.Bloqueada;
        }
        if (!await usuarios.HasPasswordAsync(usuario))
        {
            // Solo passkey: la misma respuesta y el mismo tiempo que una contraseña errada, pero sin sumar al bloqueo.
            HashFicticio.Verificar(usuarios.PasswordHasher, contrasena);
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioFallido, Datos(usuario) with
            {
                Detalle = new { metodo, motivo = "sin_contrasena" },
            }, ct);
            return ResultadoVerificacion.Invalida;
        }
        return await usuarios.CheckPasswordAsync(usuario, contrasena)
            ? ResultadoVerificacion.Valida
            : await FalloAsync(usuario, Contador.Contrasena, metodo, TiposEventoSeguridad.InicioFallido, null, ct);
    }

    /// <summary>
    /// Código TOTP del segundo paso del ingreso (HU 1.5), ya normalizado con <see cref="CodigosTotp.Normalizar"/>
    /// (<c>null</c> si tenía otro formato: cuenta como fallo). Lo valida Identity; si es válido, su paso de tiempo debe ser
    /// posterior al último aceptado.
    /// </summary>
    public async Task<ResultadoVerificacion> VerificarTotpAsync(UsuarioAtlas usuario, string? codigo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        const string metodo = "totp";
        if (await BloqueadaAsync(usuario, metodo, ct))
        {
            return ResultadoVerificacion.Bloqueada;
        }
        if (codigo is null || !await usuarios.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultAuthenticatorProvider, codigo))
        {
            return await FalloAsync(usuario, Contador.SegundoFactor, metodo, TiposEventoSeguridad.MfaFallido, null, ct);
        }
        if (!await MarcarCodigoTotpAsync(usuario, codigo, ct: ct))
        {
            return await FalloAsync(usuario, Contador.SegundoFactor, metodo, TiposEventoSeguridad.MfaFallido, "codigo_reutilizado", ct);
        }
        return ResultadoVerificacion.Valida;
    }

    /// <summary>Código de recuperación (de un solo uso) en lugar del TOTP; suma al contador del segundo factor.</summary>
    public async Task<ResultadoVerificacion> CanjearCodigoRecuperacionAsync(UsuarioAtlas usuario, string codigo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        const string metodo = "codigo_recuperacion";
        if (await BloqueadaAsync(usuario, metodo, ct))
        {
            return ResultadoVerificacion.Bloqueada;
        }
        return (await usuarios.RedeemTwoFactorRecoveryCodeAsync(usuario, codigo ?? string.Empty)).Succeeded
            ? ResultadoVerificacion.Valida
            : await FalloAsync(usuario, Contador.SegundoFactor, metodo, TiposEventoSeguridad.MfaFallido, null, ct);
    }

    /// <summary>
    /// Guarda el paso de tiempo de un código TOTP que Identity ya aceptó; <c>false</c> si ese paso (o uno posterior) ya se
    /// había aceptado: el código no sirve otra vez. También lo llama el registro de la clave (aceptar, recuperar, activar
    /// la cuenta de emergencia), para que el código usado al registrar tampoco sirva para entrar.
    /// </summary>
    /// <param name="claveNueva">
    /// La clave TOTP se acaba de registrar: el paso guardado era de la clave anterior y se reemplaza sin compararlo.
    /// </param>
    public async Task<bool> MarcarCodigoTotpAsync(UsuarioAtlas usuario, string codigo, bool claveNueva = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var clave = await usuarios.GetAuthenticatorKeyAsync(usuario);
        // Identity lee la hora del sistema, no el TimeProvider: el paso se ubica con la misma hora.
        if (clave is null || CodigosTotp.Paso(clave, codigo, TimeProvider.System.GetUtcNow()) is not { } paso)
        {
            return false;
        }
        var filas = await contexto.Database.SqlQuery<Guid>($"""
            INSERT INTO atlas.identidad_segundo_factor (usuario_id, fallos, ultimo_paso_totp) VALUES ({usuario.Id}, 0, {paso})
            ON CONFLICT (usuario_id) DO UPDATE SET ultimo_paso_totp = EXCLUDED.ultimo_paso_totp
            WHERE {claveNueva} OR identidad_segundo_factor.ultimo_paso_totp IS NULL OR identidad_segundo_factor.ultimo_paso_totp < EXCLUDED.ultimo_paso_totp
            RETURNING usuario_id AS "Value"
            """).ToListAsync(ct);
        return filas.Count == 1;
    }

    /// <summary>
    /// El ingreso terminó con todos sus factores (segundo factor o passkey), o la cuenta se recuperó: reinicia los dos
    /// contadores de fallos.
    /// </summary>
    public async Task IngresoCompletadoAsync(UsuarioAtlas usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        await contexto.Database.ExecuteSqlAsync($"""
            UPDATE atlas.identidad_usuario SET access_failed_count = 0, concurrency_stamp = gen_random_uuid()::text
            WHERE id = {usuario.Id} AND access_failed_count <> 0
            """, ct);
        await contexto.Database.ExecuteSqlAsync(
            $"UPDATE atlas.identidad_segundo_factor SET fallos = 0 WHERE usuario_id = {usuario.Id} AND fallos <> 0", ct);
        await RecargarAsync(usuario, ct);
    }

    private static DatosEventoSeguridad Datos(UsuarioAtlas usuario) => new() { UsuarioId = usuario.Id, PersonaId = usuario.PersonaId };

    private async Task<bool> BloqueadaAsync(UsuarioAtlas usuario, string metodo, CancellationToken ct)
    {
        if (!await usuarios.IsLockedOutAsync(usuario))
        {
            return false;
        }
        // Ni siquiera se compara el factor: un bloqueo no se puede usar para adivinarlo.
        await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, Datos(usuario) with
        {
            Detalle = new { metodo, motivo = "cuenta_bloqueada" },
        }, ct);
        return true;
    }

    private async Task<ResultadoVerificacion> FalloAsync(
        UsuarioAtlas usuario, Contador contador, string metodo, string tipoFallo, string? motivo, CancellationToken ct)
    {
        // No suma si la cuenta ya quedó bloqueada: un fallo concurrente que pasó la revisión de BloqueadaAsync antes del
        // bloqueo contaría desde el contador ya en cero y podría bloquearla otra vez, duplicando la duración (F4-SEC-02).
        var ahora = reloj.GetUtcNow();
        var sumados = contador == Contador.Contrasena
            ? await contexto.Database.SqlQuery<int>($"""
                UPDATE atlas.identidad_usuario
                SET access_failed_count = access_failed_count + 1, concurrency_stamp = gen_random_uuid()::text
                WHERE id = {usuario.Id} AND (lockout_end IS NULL OR lockout_end <= {ahora})
                RETURNING access_failed_count AS "Value"
                """).ToListAsync(ct)
            : await contexto.Database.SqlQuery<int>($"""
                INSERT INTO atlas.identidad_segundo_factor (usuario_id, fallos)
                SELECT {usuario.Id}, 1
                WHERE NOT EXISTS (SELECT 1 FROM atlas.identidad_usuario u WHERE u.id = {usuario.Id} AND u.lockout_end > {ahora})
                ON CONFLICT (usuario_id) DO UPDATE SET fallos = identidad_segundo_factor.fallos + 1
                WHERE NOT EXISTS (SELECT 1 FROM atlas.identidad_usuario u WHERE u.id = {usuario.Id} AND u.lockout_end > {ahora})
                RETURNING fallos AS "Value"
                """).ToListAsync(ct);
        if (sumados.Count == 0)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, Datos(usuario) with
            {
                Detalle = new { metodo, motivo = "cuenta_bloqueada" },
            }, ct);
            await RecargarAsync(usuario, ct);
            return ResultadoVerificacion.Bloqueada;
        }
        var intentos = sumados.Single();
        await registro.RegistrarAsync(tipoFallo, Datos(usuario) with
        {
            Detalle = motivo is null ? new { metodo, intentos } : (object)new { metodo, intentos, motivo },
        }, ct);

        if (intentos < IdentidadDbContext.IntentosAntesDelBloqueo)
        {
            await RecargarAsync(usuario, ct);
            return ResultadoVerificacion.Invalida;
        }
        if (intentos > IdentidadDbContext.IntentosAntesDelBloqueo)
        {
            // Fallos concurrentes que llegaron detrás del que bloquea: la cuenta ya se está bloqueando.
            await RecargarAsync(usuario, ct);
            return ResultadoVerificacion.Bloqueada;
        }

        var desde = ahora - TimeSpan.FromHours(24);
        var previos = await contexto.EventosSeguridad.CountAsync(
            e => e.UsuarioId == usuario.Id && e.Tipo == TiposEventoSeguridad.CuentaBloqueada && e.OcurridoEn > desde, ct);
        var duracion = DuracionBloqueo(previos);
        var fin = ahora + duracion;
        // Como Identity: al bloquear, el contador vuelve a cero.
        await contexto.Database.ExecuteSqlAsync($"""
            UPDATE atlas.identidad_usuario
            SET lockout_end = {fin}, access_failed_count = 0, concurrency_stamp = gen_random_uuid()::text
            WHERE id = {usuario.Id}
            """, ct);
        await contexto.Database.ExecuteSqlAsync($"UPDATE atlas.identidad_segundo_factor SET fallos = 0 WHERE usuario_id = {usuario.Id}", ct);
        await RecargarAsync(usuario, ct);
        await registro.RegistrarAsync(TiposEventoSeguridad.CuentaBloqueada, Datos(usuario) with
        {
            Detalle = new { metodo, minutos = (int)duracion.TotalMinutes, bloqueos_previos = previos },
        }, ct);
        return ResultadoVerificacion.Bloqueada;
    }

    /// <summary>El usuario en memoria vuelve a leer la fila (bloqueo, contador y sello de concurrencia nuevos).</summary>
    private async Task RecargarAsync(UsuarioAtlas usuario, CancellationToken ct)
    {
        var entrada = contexto.Entry(usuario);
        if (entrada.State != EntityState.Detached)
        {
            await entrada.ReloadAsync(ct);
        }
    }

    /// <summary>15 min, 30 min, 1 h, 2 h… hasta 24 h, según los bloqueos de las últimas 24 h.</summary>
    public static TimeSpan DuracionBloqueo(int bloqueosPrevios)
    {
        var factor = Math.Pow(2, Math.Clamp(bloqueosPrevios, 0, 16));
        var duracion = TimeSpan.FromTicks((long)Math.Min(IdentidadDbContext.BloqueoInicial.Ticks * factor, BloqueoMaximo.Ticks));
        return duracion;
    }
}
