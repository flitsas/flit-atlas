using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>Tipos de <see cref="EventoSeguridad"/>. En la auditoría quedan como <c>identidad.&lt;tipo&gt;</c>.</summary>
public static class TiposEventoSeguridad
{
    public const string InicioSesion = "inicio_sesion";
    public const string InicioRechazado = "inicio_rechazado";
    public const string InicioFallido = "inicio_fallido";
    public const string CuentaBloqueada = "cuenta_bloqueada";
    public const string CierreSesion = "cierre_sesion";
    public const string CierreRemoto = "cierre_remoto";
    public const string SesionesInvalidadas = "sesiones_invalidadas";
    public const string PersonaSuspendida = "persona_suspendida";
    public const string PersonaReactivada = "persona_reactivada";
    public const string Reautenticacion = "reautenticacion";
    public const string ReautenticacionRequerida = "reautenticacion_requerida";
    public const string LimiteTasa = "limite_tasa";

    /// <summary>
    /// Una cuenta de Entra con un <c>oid</c> nuevo traía el correo de una persona existente: no se enlazó ni heredó sus roles
    /// (F4-SEC-07, ATLAS-ADR-0049). También lo registra la sincronización cuando no enlaza por correo a una persona con roles.
    /// </summary>
    public const string VinculoPorCorreoRechazado = "vinculo_por_correo_rechazado";

    // Invitaciones y cuentas externas (HU 1.5, ATLAS-ADR-0025).
    public const string InvitacionCreada = "invitacion_creada";
    public const string InvitacionReenviada = "invitacion_reenviada";
    public const string InvitacionSuspendida = "invitacion_suspendida";
    public const string InvitacionRenovada = "invitacion_renovada";
    public const string InvitacionRevocada = "invitacion_revocada";
    public const string InvitacionAceptada = "invitacion_aceptada";

    /// <summary>Alguien usó un enlace de invitación o de recuperación que no sirve (usado, vencido, revocado o inventado).</summary>
    public const string EnlaceRechazado = "enlace_rechazado";

    public const string CorreoNoEnviado = "correo_no_enviado";
    public const string CuentaCreada = "cuenta_creada";

    /// <summary>Se registró un factor: passkey, contraseña, TOTP o códigos de recuperación (detalle <c>metodo</c>).</summary>
    public const string FactorRegistrado = "factor_registrado";

    /// <summary>Se quitaron los factores anteriores (recuperación o restablecimiento forzado).</summary>
    public const string FactoresRetirados = "factores_retirados";

    public const string MfaVerificado = "mfa_verificado";
    public const string MfaFallido = "mfa_fallido";

    /// <summary>Una cuenta externa sin segundo factor intentó entrar y se rechazó.</summary>
    public const string MfaRequerido = "mfa_requerido";

    public const string CodigoRecuperacionUsado = "codigo_recuperacion_usado";
    public const string RecuperacionSolicitada = "recuperacion_solicitada";
    public const string RecuperacionCompletada = "recuperacion_completada";
    public const string RestablecimientoForzado = "restablecimiento_forzado";
    public const string TerminosAceptados = "terminos_aceptados";

    // Cuenta de administrador de emergencia (ATLAS-ADR-0027): todos de severidad alta.
    public const string EmergenciaCuentaCreada = "emergencia.cuenta_creada";

    /// <summary>El comando de consola emitió un enlace de activación (quitó los factores y cerró las sesiones).</summary>
    public const string EmergenciaEnlaceEmitido = "emergencia.enlace_emitido";

    public const string EmergenciaActivada = "emergencia.activada";
    public const string EmergenciaIngreso = "emergencia.ingreso";

    /// <summary>Prefijo de los eventos de alta severidad: además de la auditoría, quedan como advertencia en el log.</summary>
    public const string PrefijoAltaSeveridad = "emergencia.";

    public const string PrefijoAuditoria = "identidad.";
}

/// <summary>Lo que se sabe de un evento de seguridad. La IP y el navegador se toman de la petición en curso.</summary>
public sealed record DatosEventoSeguridad
{
    public Guid? UsuarioId { get; init; }

    public Guid? PersonaId { get; init; }

    public Guid? OrganizacionId { get; init; }

    public Guid? SesionId { get; init; }

    /// <summary>Quién actúa, si no es el propio afectado (p. ej. el administrador que suspende).</summary>
    public Guid? ActorPersonaId { get; init; }

    /// <summary>Detalle que se serializa a JSON. Nunca contraseñas, tokens ni la IP (ya va en su columna).</summary>
    public object? Detalle { get; init; }

    /// <summary>Recurso de la auditoría cuando no es el usuario o la persona (p. ej. <c>invitacion</c>).</summary>
    public string? RecursoTipo { get; init; }

    public Guid? RecursoId { get; init; }

    /// <summary>Usuario, persona, organización y sesión del principal de la sesión en curso.</summary>
    public static DatosEventoSeguridad DeSesion(ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return new()
        {
            UsuarioId = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.Sub), out var sub) ? sub : null,
            PersonaId = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var persona) ? persona : null,
            OrganizacionId = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.OrganizacionId), out var org) ? org : null,
            SesionId = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.Sid), out var sid) ? sid : null,
        };
    }
}

/// <summary>
/// Registra los eventos de seguridad (ATLAS-ADR-0022): una fila en <c>identidad_evento_seguridad</c> y otra en
/// <c>plataforma_auditoria</c>, en la <b>misma transacción</b> (si la auditoría falla, tampoco queda el evento). Las
/// dos tablas son de solo inserción. Los de la cuenta de emergencia también quedan como advertencia en el log (ATLAS-ADR-0027).
/// </summary>
public sealed partial class RegistroSeguridad(PlataformaDbContext plataforma, TimeProvider reloj, IHttpContextAccessor contexto, ILogger<RegistroSeguridad> log)
{
    public async Task RegistrarAsync(string tipo, DatosEventoSeguridad datos, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentNullException.ThrowIfNull(datos);
        var http = contexto.HttpContext;
        var ahora = reloj.GetUtcNow();
        var ip = IpCliente(http);
        var agente = http is null ? null : ResumenAgente.De(http.Request.Headers.UserAgent);
        var detalle = datos.Detalle is null ? null : JsonSerializer.Serialize(datos.Detalle);
        var actor = datos.ActorPersonaId ?? datos.PersonaId;
        var id = Guid.CreateVersion7();

        var transaccion = plataforma.Database.CurrentTransaction is null
            ? await plataforma.Database.BeginTransactionAsync(ct)
            : null;
        await using (transaccion)
        {
            // La tabla es del contexto de Identidad, pero se escribe por la conexión de la plataforma para compartir la
            // transacción con la auditoría sin coordinar dos DbContext.
            await plataforma.Database.ExecuteSqlRawAsync("""
                INSERT INTO atlas.identidad_evento_seguridad
                    (id, ocurrido_en, tipo, usuario_id, persona_id, sesion_id, actor_persona_id, ip, agente_resumen, datos)
                VALUES (@id, @ocurrido_en, @tipo, @usuario_id, @persona_id, @sesion_id, @actor_persona_id, @ip, @agente, @datos)
                """,
                [
                    Parametro("id", NpgsqlDbType.Uuid, id),
                    Parametro("ocurrido_en", NpgsqlDbType.TimestampTz, ahora),
                    Parametro("tipo", NpgsqlDbType.Varchar, tipo),
                    Parametro("usuario_id", NpgsqlDbType.Uuid, datos.UsuarioId),
                    Parametro("persona_id", NpgsqlDbType.Uuid, datos.PersonaId),
                    Parametro("sesion_id", NpgsqlDbType.Uuid, datos.SesionId),
                    Parametro("actor_persona_id", NpgsqlDbType.Uuid, datos.ActorPersonaId),
                    Parametro("ip", NpgsqlDbType.Inet, ip),
                    Parametro("agente", NpgsqlDbType.Varchar, agente),
                    Parametro("datos", NpgsqlDbType.Jsonb, detalle),
                ], ct);

            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = ahora,
                ActorTipo = actor is null ? TipoActor.Sistema : TipoActor.Persona,
                ActorId = actor?.ToString(),
                OrganizacionId = datos.OrganizacionId,
                Accion = TiposEventoSeguridad.PrefijoAuditoria + tipo,
                RecursoTipo = datos.RecursoTipo
                    ?? (datos.UsuarioId is not null ? "usuario" : datos.PersonaId is not null ? "persona" : "identidad"),
                RecursoId = (datos.RecursoId ?? datos.UsuarioId ?? datos.PersonaId)?.ToString(),
                Ip = ip,
                Datos = datos.SesionId is null
                    ? detalle
                    : JsonSerializer.Serialize(new { sesion_id = datos.SesionId, detalle = datos.Detalle }),
                CorrelacionId = Activity.Current?.TraceId.ToString(),
            });
            await plataforma.SaveChangesAsync(ct);
            if (transaccion is not null)
            {
                await transaccion.CommitAsync(ct);
            }
        }
        if (tipo.StartsWith(TiposEventoSeguridad.PrefijoAltaSeveridad, StringComparison.Ordinal))
        {
            // Para las alertas sobre los logs: solo el tipo y el id del evento (nada personal ni secreto).
            AltaSeveridad(log, TiposEventoSeguridad.PrefijoAuditoria + tipo, id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evento de seguridad de alta severidad: {Tipo} ({EventoId})")]
    private static partial void AltaSeveridad(ILogger log, string tipo, Guid eventoId);

    private static NpgsqlParameter Parametro(string nombre, NpgsqlDbType tipo, object? valor) =>
        new(nombre, tipo) { Value = valor ?? DBNull.Value };

    /// <summary>IP del cliente ya resuelta por <c>UseForwardedHeaders</c> (solo proxies confiables).</summary>
    internal static IPAddress? IpCliente(HttpContext? http)
    {
        var ip = http?.Connection.RemoteIpAddress;
        return ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;
    }
}
