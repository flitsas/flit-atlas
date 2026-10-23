using System.Net;
using System.Security.Claims;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Sesiones;

/// <summary>Duraciones de la sesión del BFF (sección <c>Identidad:Sesion</c>).</summary>
public sealed class OpcionesSesion
{
    public const string Seccion = "Identidad:Sesion";

    /// <summary>Expiración deslizante de los empleados (ATLAS-ADR-0004 §6).</summary>
    public TimeSpan DeslizanteEmpleados { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Tope absoluto de una sesión de empleado.</summary>
    public TimeSpan AbsolutaEmpleados { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Expiración deslizante de los externos: más corta (§6.3, ATLAS-ADR-0004 §6).</summary>
    public TimeSpan DeslizanteExternos { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Tope absoluto de una sesión de externo.</summary>
    public TimeSpan AbsolutaExternos { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Antigüedad máxima del último ingreso para una acción sensible (ATLAS-ADR-0004 §6.4).</summary>
    public TimeSpan VentanaReautenticacion { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Cada cuánto se actualizan el último uso y la IP de una sesión («dispositivos conectados»).</summary>
    public TimeSpan IntervaloUltimoUso { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Una sesión activa, tal como la ve su dueño en «dispositivos conectados».</summary>
public sealed record SesionActiva(
    Guid Id, string Dispositivo, IPAddress? Ip, DateTimeOffset CreadaEn, DateTimeOffset UltimoUsoEn, DateTimeOffset ExpiraEn);

/// <summary>Motivos con que se revoca una fila de <c>identidad_sesion</c>.</summary>
public static class MotivosRevocacion
{
    public const string CierreDeSesion = "cierre_de_sesion";
    public const string ReemplazadaPorIngreso = "reemplazada_por_ingreso";
    public const string CierreRemoto = "cierre_remoto";
    public const string SelloDeSeguridad = "sello_de_seguridad";
}

/// <summary>
/// Sesiones del BFF en <c>identidad_sesion</c>. La cookie solo lleva la llave (el id de la sesión, cifrado por la
/// cookie); el ticket con los claims queda en la base protegido con Data Protection. Cerrar sesión la revoca en el
/// servidor: una cookie copiada deja de servir. Cada petición compara el sello de seguridad de la sesión con el vigente
/// del usuario: si cambió (roles, suspensión, vencimiento, cierre administrativo), la sesión ya no vale
/// (ATLAS-ADR-0022).
/// </summary>
public sealed class AlmacenSesiones(
    IServiceScopeFactory alcances,
    IDataProtectionProvider proteccion,
    OpcionesSesion opciones,
    TimeProvider reloj) : ITicketStore
{
    private readonly IDataProtector _protector = proteccion.CreateProtector("Flit.Atlas.Identidad.Sesiones.v1");

    /// <summary>
    /// Clave de <see cref="HttpContext.Items"/> con el motivo con que <see cref="RemoveAsync(string, HttpContext, CancellationToken)"/>
    /// revoca la sesión; sin ella, <c>cierre_de_sesion</c>.
    /// </summary>
    public const string ClaveMotivoRevocacion = "atlas.motivo_revocacion";

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, null, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        StoreAsync(ticket, null, cancellationToken);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var ahora = reloj.GetUtcNow();
        var usuarioId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimsAtlas.Sub)!);

        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();

        // El sello lo leyó el aprovisionamiento antes de comprobar que la persona estaba activa; así, una suspensión
        // que llegue en medio del ingreso deja esta sesión inválida desde el principio.
        var sello = ticket.Principal.FindFirstValue(ClaimsAtlas.SelloSeguridad)
            ?? await contexto.Users.Where(u => u.Id == usuarioId).Select(u => u.SecurityStamp).SingleAsync(cancellationToken);
        var sesion = new Sesion
        {
            UsuarioId = usuarioId,
            CreadaEn = ahora,
            UltimoUsoEn = ahora,
            ExpiraEn = ticket.Properties.ExpiresUtc ?? ahora + opciones.DeslizanteEmpleados,
            // Las cuentas locales (externos y la de emergencia, ATLAS-ADR-0027) tienen el tope más corto.
            ExpiraAbsolutaEn = ahora + (ticket.Principal.FindFirstValue(ClaimsAtlas.TipoPrincipal) == ClaimsAtlas.TipoExterno
                || ticket.Principal.FindFirstValue(ClaimsAtlas.Proveedor) == Emergencia.CuentaEmergencia.Proveedor
                ? opciones.AbsolutaExternos
                : opciones.AbsolutaEmpleados),
            Ticket = [],
            AgenteResumen = ticket.Properties.GetString(ClavesPropiedades.Agente)
                ?? (httpContext is null ? null : ResumenAgente.De(httpContext.Request.Headers.UserAgent)),
            Ip = RegistroSeguridad.IpCliente(httpContext),
            SelloSeguridad = sello ?? string.Empty,
        };

        // El sid se conoce aquí: se agrega al principal antes de guardarlo.
        if (ticket.Principal.Identity is ClaimsIdentity identidad && !identidad.HasClaim(c => c.Type == ClaimsAtlas.Sid))
        {
            identidad.AddClaim(new Claim(ClaimsAtlas.Sid, sesion.Id.ToString()));
        }
        sesion.Ticket = Proteger(ticket);

        contexto.Sesiones.Add(sesion);
        await contexto.SaveChangesAsync(cancellationToken);

        // Ingreso desde un navegador que ya tenía sesión: la anterior deja de valer (RenovacionAlIngresar).
        if (httpContext?.Items[RenovacionAlIngresar.ClaveSesionAnterior] is string anterior)
        {
            await RevocarAsync(anterior, MotivosRevocacion.ReemplazadaPorIngreso, cancellationToken);
        }
        return sesion.Id.ToString();
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, null, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, null, cancellationToken);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var sesion = await contexto.Sesiones.FirstOrDefaultAsync(s => s.Id == id && s.RevocadaEn == null, cancellationToken);
        if (sesion is null)
        {
            return;
        }
        // Un ingreso sobre esta misma sesión trae un principal nuevo, sin sid y con el sello vigente
        // (RenovacionAlIngresar lo evita en las rutas de ingreso; esto es la defensa si otra ruta llega a hacerlo).
        if (ticket.Principal.Identity is ClaimsIdentity identidad && !identidad.HasClaim(c => c.Type == ClaimsAtlas.Sid))
        {
            identidad.AddClaim(new Claim(ClaimsAtlas.Sid, sesion.Id.ToString()));
        }
        sesion.SelloSeguridad = ticket.Principal.FindFirstValue(ClaimsAtlas.SelloSeguridad) ?? sesion.SelloSeguridad;
        var ahora = reloj.GetUtcNow();
        var expira = ticket.Properties.ExpiresUtc ?? ahora + opciones.DeslizanteEmpleados;
        sesion.UltimoUsoEn = ahora;
        sesion.Ip = RegistroSeguridad.IpCliente(httpContext) ?? sesion.Ip;
        sesion.ExpiraEn = expira < sesion.ExpiraAbsolutaEn ? expira : sesion.ExpiraAbsolutaEn;
        sesion.Ticket = Proteger(ticket);
        await contexto.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, null, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) =>
        RetrieveAsync(key, null, cancellationToken);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        var fila = await contexto.Sesiones.AsNoTracking()
            .Where(s => s.Id == id && s.RevocadaEn == null && s.ExpiraEn > ahora && s.ExpiraAbsolutaEn > ahora)
            .Join(contexto.Users, s => s.UsuarioId, u => u.Id,
                (s, u) => new { s.Ticket, s.SelloSeguridad, SelloVigente = u.SecurityStamp, s.UltimoUsoEn })
            .FirstOrDefaultAsync(cancellationToken);
        if (fila is null)
        {
            return null;
        }
        if (!string.Equals(fila.SelloSeguridad, fila.SelloVigente, StringComparison.Ordinal))
        {
            // Cambiaron los roles, se suspendió la persona o se cerró todo desde la administración.
            await RevocarAsync(id, MotivosRevocacion.SelloDeSeguridad, cancellationToken);
            return null;
        }

        AuthenticationTicket ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(fila.Ticket))!;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Llaves de Data Protection rotadas o perdidas: la sesión ya no se puede leer; se pide entrar de nuevo.
            return null;
        }

        if (ahora - fila.UltimoUsoEn >= opciones.IntervaloUltimoUso)
        {
            var ip = RegistroSeguridad.IpCliente(httpContext);
            await contexto.Sesiones
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoUsoEn, ahora).SetProperty(x => x.Ip, x => ip ?? x.Ip), cancellationToken);
        }
        return ticket;
    }

    public Task RemoveAsync(string key) => RevocarAsync(key, MotivosRevocacion.CierreDeSesion);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        RevocarAsync(key, MotivosRevocacion.CierreDeSesion, cancellationToken);

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var motivo = httpContext.Items.TryGetValue(ClaveMotivoRevocacion, out var valor) && valor is string texto
            ? texto
            : MotivosRevocacion.CierreDeSesion;
        return RevocarAsync(key, motivo, cancellationToken);
    }

    public Task RevocarAsync(string key, string motivo, CancellationToken ct = default) =>
        Guid.TryParse(key, out var id) ? RevocarAsync(id, motivo, ct) : Task.CompletedTask;

    private async Task RevocarAsync(Guid id, string motivo, CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        await contexto.Sesiones
            .Where(s => s.Id == id && s.RevocadaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevocadaEn, ahora).SetProperty(x => x.MotivoRevocacion, motivo), ct);
    }

    /// <summary>Sesiones vigentes de un usuario, de la más reciente a la más antigua.</summary>
    public async Task<IReadOnlyList<SesionActiva>> ListarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        return await contexto.Sesiones.AsNoTracking()
            .Where(s => s.UsuarioId == usuarioId && s.RevocadaEn == null && s.ExpiraEn > ahora && s.ExpiraAbsolutaEn > ahora)
            .Join(contexto.Users, s => s.UsuarioId, u => u.Id, (s, u) => new { s, u.SecurityStamp })
            .Where(x => x.s.SelloSeguridad == x.SecurityStamp)
            .OrderByDescending(x => x.s.UltimoUsoEn)
            .Select(x => new SesionActiva(
                x.s.Id, x.s.AgenteResumen ?? "Desconocido", x.s.Ip, x.s.CreadaEn, x.s.UltimoUsoEn, x.s.ExpiraEn))
            .ToListAsync(ct);
    }

    /// <summary>Revoca una sesión de un usuario; <c>false</c> si no es suya o ya no estaba activa.</summary>
    public async Task<bool> RevocarDeUsuarioAsync(Guid sesionId, Guid usuarioId, string motivo, CancellationToken ct = default)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        var revocadas = await contexto.Sesiones
            .Where(s => s.Id == sesionId && s.UsuarioId == usuarioId && s.RevocadaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevocadaEn, ahora).SetProperty(x => x.MotivoRevocacion, motivo), ct);
        return revocadas > 0;
    }

    /// <summary>Revoca todas las sesiones activas de un usuario, salvo <paramref name="excepto"/>; devuelve cuántas.</summary>
    public async Task<int> RevocarTodasAsync(Guid usuarioId, string motivo, Guid? excepto = null, CancellationToken ct = default)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        return await contexto.Sesiones
            .Where(s => s.UsuarioId == usuarioId && s.RevocadaEn == null && s.ExpiraAbsolutaEn > ahora && s.Id != excepto)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevocadaEn, ahora).SetProperty(x => x.MotivoRevocacion, motivo), ct);
    }

    private byte[] Proteger(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    public static class ClavesPropiedades
    {
        public const string Agente = "atlas.agente";
    }
}
