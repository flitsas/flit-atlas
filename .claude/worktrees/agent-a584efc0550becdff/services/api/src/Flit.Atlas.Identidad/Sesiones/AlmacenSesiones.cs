using System.Security.Claims;
using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Sesiones;

public sealed class OpcionesSesion
{
    /// <summary>Expiración deslizante de los empleados (ATLAS-ADR-0004 §6).</summary>
    public TimeSpan DeslizanteEmpleados { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Tope absoluto de una sesión de empleado.</summary>
    public TimeSpan AbsolutaEmpleados { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Sesiones del BFF en <c>identidad_sesion</c>. La cookie solo lleva la llave (el id de la sesión, cifrado por la
/// cookie); el ticket con los claims queda en la base protegido con Data Protection. Cerrar sesión la revoca en el
/// servidor: una cookie copiada deja de servir.
/// </summary>
public sealed class AlmacenSesiones(
    IServiceScopeFactory alcances,
    IDataProtectionProvider proteccion,
    OpcionesSesion opciones,
    TimeProvider reloj) : ITicketStore
{
    private readonly IDataProtector _protector = proteccion.CreateProtector("Flit.Atlas.Identidad.Sesiones.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var ahora = reloj.GetUtcNow();
        var sesion = new Sesion
        {
            UsuarioId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimsAtlas.Sub)!),
            CreadaEn = ahora,
            UltimoUsoEn = ahora,
            ExpiraEn = ticket.Properties.ExpiresUtc ?? ahora + opciones.DeslizanteEmpleados,
            ExpiraAbsolutaEn = ahora + opciones.AbsolutaEmpleados,
            Ticket = [],
            AgenteResumen = ticket.Properties.GetString(ClavesPropiedades.Agente),
        };

        // El sid se conoce aquí: se agrega al principal antes de guardarlo.
        if (ticket.Principal.Identity is ClaimsIdentity identidad && !identidad.HasClaim(c => c.Type == ClaimsAtlas.Sid))
        {
            identidad.AddClaim(new Claim(ClaimsAtlas.Sid, sesion.Id.ToString()));
        }
        sesion.Ticket = Proteger(ticket);

        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        contexto.Sesiones.Add(sesion);
        await contexto.SaveChangesAsync();
        return sesion.Id.ToString();
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var sesion = await contexto.Sesiones.FirstOrDefaultAsync(s => s.Id == id && s.RevocadaEn == null);
        if (sesion is null)
        {
            return;
        }
        var ahora = reloj.GetUtcNow();
        var expira = ticket.Properties.ExpiresUtc ?? ahora + opciones.DeslizanteEmpleados;
        sesion.UltimoUsoEn = ahora;
        sesion.ExpiraEn = expira < sesion.ExpiraAbsolutaEn ? expira : sesion.ExpiraAbsolutaEn;
        sesion.Ticket = Proteger(ticket);
        await contexto.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        var sesion = await contexto.Sesiones.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.RevocadaEn == null && s.ExpiraEn > ahora && s.ExpiraAbsolutaEn > ahora);
        if (sesion is null)
        {
            return null;
        }
        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(sesion.Ticket));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Llaves de Data Protection rotadas o perdidas: la sesión ya no se puede leer; se pide entrar de nuevo.
            return null;
        }
    }

    public Task RemoveAsync(string key) => RevocarAsync(key, "cierre_de_sesion");

    public async Task RevocarAsync(string key, string motivo)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var ahora = reloj.GetUtcNow();
        await contexto.Sesiones
            .Where(s => s.Id == id && s.RevocadaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevocadaEn, ahora).SetProperty(x => x.MotivoRevocacion, motivo));
    }

    private byte[] Proteger(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    public static class ClavesPropiedades
    {
        public const string Agente = "atlas.agente";
    }
}
