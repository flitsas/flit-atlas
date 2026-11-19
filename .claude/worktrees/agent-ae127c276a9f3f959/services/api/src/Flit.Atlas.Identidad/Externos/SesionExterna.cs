using System.Globalization;
using System.Security.Claims;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>Métodos de autenticación (claim <c>amr</c>, RFC 8176) de las cuentas locales.</summary>
public static class MetodosExternos
{
    public const string Passkey = "passkey";
    public const string Contrasena = "contrasena";

    /// <summary>Proveedor de la sesión (<c>proveedor</c> en <c>/yo</c>) de las cuentas locales de externos.</summary>
    public const string ProveedorLocal = "local";

    /// <summary><c>amr</c> de cada método: la passkey con verificación de usuario es un factor fuerte por sí sola.</summary>
    public static IReadOnlyList<string> Amr(string metodo) => metodo == Passkey
        ? ["hwk", "user", "mfa"]
        : ["pwd", "otp", "mfa"];
}

/// <summary>
/// Reglas del ingreso de una persona externa y la sesión que recibe (ATLAS-ADR-0025): solo entra una persona externa
/// activa, de una organización activa y con al menos un rol vigente (al vencer, el acceso se corta sin intervención); la
/// sesión dura menos que la de un empleado (<see cref="OpcionesSesion.DeslizanteExternos"/>) y lleva
/// <c>tipo_principal = externo</c> y la organización de la persona, con lo que el predicado obligatorio y RLS la limitan
/// a su organización (ATLAS-ADR-0023).
/// </summary>
public sealed class SesionExterna(PlataformaDbContext plataforma, RegistroSeguridad registro, OpcionesSesion opciones, TimeProvider reloj)
{
    public const string PersonaNoExterna = "no_externa";
    public const string OrganizacionSuspendida = "organizacion_suspendida";
    public const string AccesoVencido = "acceso_vencido";

    /// <summary>Por qué la persona no puede entrar ahora, o <c>null</c> si puede.</summary>
    public async Task<string?> MotivoNoPermitidoAsync(Guid personaId, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var persona = await plataforma.Personas.AsNoTracking().Where(p => p.Id == personaId)
            .Select(p => new { p.Tipo, p.Estado, p.OrganizacionId }).FirstOrDefaultAsync(ct);
        if (persona is null || persona.Tipo != TipoPersona.Externo)
        {
            return PersonaNoExterna;
        }
        if (persona.Estado != EstadoPersona.Activa)
        {
            return persona.Estado == EstadoPersona.Suspendida
                ? Aprovisionamiento.IngresoNoPermitidoException.PersonaSuspendida
                : Aprovisionamiento.IngresoNoPermitidoException.PersonaInactiva;
        }
        if (!await plataforma.Organizaciones.AnyAsync(o => o.Id == persona.OrganizacionId && o.Estado == EstadoOrganizacion.Activa, ct))
        {
            return OrganizacionSuspendida;
        }
        var vigente = await plataforma.AsignacionesRol.AnyAsync(a => a.PersonaId == personaId && a.VigenteDesde <= ahora
            && (a.VigenteHasta == null || a.VigenteHasta > ahora), ct);
        return vigente ? null : AccesoVencido;
    }

    /// <summary>Crea la sesión del BFF (fila nueva en <c>identidad_sesion</c> y cookie) y registra el ingreso.</summary>
    public async Task IniciarAsync(HttpContext http, UsuarioAtlas usuario, string metodo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(usuario);
        var persona = await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Id == usuario.PersonaId, ct);
        var ahora = reloj.GetUtcNow();

        var identidad = new ClaimsIdentity(ClaimsAtlas.EsquemaCookie, ClaimsAtlas.Nombre, ClaimsAtlas.Rol);
        identidad.AddClaim(new Claim(ClaimsAtlas.Sub, usuario.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.PersonaId, persona.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno));
        identidad.AddClaim(new Claim(ClaimsAtlas.OrganizacionId, persona.OrganizacionId.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.Nombre, persona.Nombre));
        identidad.AddClaim(new Claim(ClaimsAtlas.Proveedor, MetodosExternos.ProveedorLocal));
        identidad.AddClaim(new Claim(ClaimsAtlas.AuthTime, ahora.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        identidad.AddClaim(new Claim(ClaimsAtlas.SelloSeguridad, usuario.SecurityStamp ?? string.Empty));
        identidad.AddClaims(MetodosExternos.Amr(metodo).Select(m => new Claim(ClaimsAtlas.Amr, m)));

        // Sesión más corta para externos (§6.3): el manejador de la cookie desliza con la duración de cada ticket, y
        // AlmacenSesiones aplica el tope absoluto de externos.
        var propiedades = new AuthenticationProperties { IsPersistent = true, IssuedUtc = ahora, ExpiresUtc = ahora + opciones.DeslizanteExternos };
        propiedades.SetString(AlmacenSesiones.ClavesPropiedades.Agente, ResumenAgente.De(http.Request.Headers.UserAgent));
        await http.SignInAsync(ClaimsAtlas.EsquemaCookie, new ClaimsPrincipal(identidad), propiedades);

        await registro.RegistrarAsync(TiposEventoSeguridad.InicioSesion, new DatosEventoSeguridad
        {
            UsuarioId = usuario.Id,
            PersonaId = persona.Id,
            OrganizacionId = persona.OrganizacionId,
            Detalle = new { proveedor = MetodosExternos.ProveedorLocal, metodo },
        }, ct);
    }
}
