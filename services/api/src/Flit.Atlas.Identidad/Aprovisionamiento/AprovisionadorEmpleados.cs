using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Identidad.Aprovisionamiento;

/// <summary>Lo que sabemos de un empleado al entrar: del token de Entra (o del proveedor simulado) y de Graph.</summary>
/// <param name="Grupos">Ids de los grupos de Entra; <c>null</c> si no se pudieron leer (no se tocan sus roles).</param>
public sealed record CuentaEmpleado(
    string Proveedor,
    string IdExterno,
    Guid? EntraOid,
    string Correo,
    string Nombre,
    IReadOnlyCollection<Guid>? Grupos,
    string? Cargo,
    string? Area,
    IReadOnlyCollection<string> Amr);

public sealed class CorreoEnUsoException(string mensaje) : InvalidOperationException(mensaje);

/// <summary>
/// Aprovisionamiento just-in-time de empleados (prompt maestro §6.1): crea o enlaza la <see cref="Persona"/> y el
/// <see cref="UsuarioAtlas"/>, sincroniza los roles que vienen de los grupos de Entra y arma el principal de Atlas.
/// Es idempotente: si un paso falla, el siguiente ingreso lo completa.
/// </summary>
public sealed class AprovisionadorEmpleados(
    PlataformaDbContext plataforma,
    UserManager<UsuarioAtlas> usuarios,
    TimeProvider reloj)
{
    public const string NombreOrganizacionInterna = "FLIT S.A.S.";
    public const string ProveedorEntra = "entra";
    public const string ProveedorSimulado = "simulado";

    public async Task<ClaimsPrincipal> AprovisionarAsync(CuentaEmpleado cuenta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        try
        {
            return await IntentarAsync(cuenta, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos ingresos a la vez (el primer día, o el mismo empleado en dos pestañas) crearon lo mismo: se descarta
            // lo pendiente y se reintenta una vez; esta vez se encuentra lo que creó el otro.
            plataforma.ChangeTracker.Clear();
            return await IntentarAsync(cuenta, ct);
        }
    }

    private async Task<ClaimsPrincipal> IntentarAsync(CuentaEmpleado cuenta, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();

        var organizacion = await OrganizacionInternaAsync(ct);
        var usuario = await usuarios.FindByLoginAsync(cuenta.Proveedor, cuenta.IdExterno);
        var persona = usuario is null
            ? await BuscarOCrearPersonaAsync(cuenta, organizacion.Id, ct)
            : await plataforma.Personas.SingleAsync(p => p.Id == usuario.PersonaId, ct);

        // Datos que manda Entra o Graph: se refrescan en cada ingreso.
        persona.Nombre = cuenta.Nombre;
        persona.EntraOid ??= cuenta.EntraOid;
        persona.Cargo = cuenta.Cargo ?? persona.Cargo;
        persona.Area = cuenta.Area ?? persona.Area;

        if (cuenta.Grupos is not null)
        {
            await SincronizarRolesDeGruposAsync(persona.Id, organizacion.Id, cuenta.Grupos, ahora, ct);
        }
        await plataforma.SaveChangesAsync(ct);

        usuario ??= await CrearUsuarioAsync(persona, cuenta);

        var roles = await RolesVigentesAsync(persona.Id, ahora, ct);
        plataforma.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = ahora,
            ActorTipo = TipoActor.Persona,
            ActorId = persona.Id.ToString(),
            OrganizacionId = organizacion.Id,
            Accion = "identidad.inicio_sesion",
            RecursoTipo = "usuario",
            RecursoId = usuario.Id.ToString(),
            Datos = JsonSerializer.Serialize(new { proveedor = cuenta.Proveedor }),
        });
        await plataforma.SaveChangesAsync(ct);

        return Principal(usuario, persona, organizacion.Id, roles, cuenta, ahora);
    }

    private async Task<Organizacion> OrganizacionInternaAsync(CancellationToken ct)
    {
        var interna = await plataforma.Organizaciones.FirstOrDefaultAsync(o => o.Tipo == TipoOrganizacion.Interna, ct);
        if (interna is not null)
        {
            return interna;
        }
        interna = new Organizacion { Nombre = NombreOrganizacionInterna, Tipo = TipoOrganizacion.Interna };
        plataforma.Organizaciones.Add(interna);
        await plataforma.SaveChangesAsync(ct);
        return interna;
    }

    private async Task<Persona> BuscarOCrearPersonaAsync(CuentaEmpleado cuenta, Guid organizacionId, CancellationToken ct)
    {
        var correo = cuenta.Correo.Trim().ToLowerInvariant();
        Persona? persona = null;
        if (cuenta.EntraOid is { } oid)
        {
            persona = await plataforma.Personas.FirstOrDefaultAsync(p => p.EntraOid == oid, ct);
        }

        // Solo una cuenta de Entra (correo del tenant de FLIT) puede enlazar una persona que ya existía por correo,
        // por ejemplo, cargada desde el reparto de responsables. El proveedor simulado nunca enlaza personas reales.
        if (persona is null && cuenta.Proveedor == ProveedorEntra)
        {
            persona = await plataforma.Personas.FirstOrDefaultAsync(
                p => p.Correo == correo && p.EntraOid == null && p.Tipo == TipoPersona.Empleado, ct);
        }

        if (persona is not null)
        {
            if (persona.Estado != EstadoPersona.Activa)
            {
                throw new UnauthorizedAccessException("La persona no está activa en Atlas.");
            }
            return persona;
        }

        if (await plataforma.Personas.AnyAsync(p => p.Correo == correo, ct))
        {
            throw new CorreoEnUsoException("El correo ya pertenece a otra persona de Atlas.");
        }

        persona = new Persona
        {
            Tipo = TipoPersona.Empleado,
            OrganizacionId = organizacionId,
            Nombre = cuenta.Nombre,
            Correo = correo,
            EntraOid = cuenta.EntraOid,
        };
        plataforma.Personas.Add(persona);
        return persona;
    }

    private async Task<UsuarioAtlas> CrearUsuarioAsync(Persona persona, CuentaEmpleado cuenta)
    {
        // Si un ingreso anterior creó el usuario y falló al enlazar el login, se reutiliza.
        var usuario = await usuarios.Users.FirstOrDefaultAsync(u => u.PersonaId == persona.Id);
        if (usuario is null)
        {
            usuario = new UsuarioAtlas
            {
                PersonaId = persona.Id,
                UserName = persona.Correo,
                Email = persona.Correo,
                EmailConfirmed = true,
                CreadoEn = reloj.GetUtcNow(),
            };
            var creado = await usuarios.CreateAsync(usuario);
            if (!creado.Succeeded)
            {
                // Otro ingreso simultáneo lo creó primero: se usa ese.
                usuario = await usuarios.Users.FirstOrDefaultAsync(u => u.PersonaId == persona.Id);
                if (usuario is null)
                {
                    Exigir(creado);
                }
            }
        }

        var login = new UserLoginInfo(cuenta.Proveedor, cuenta.IdExterno, cuenta.Proveedor);
        var enlazado = await usuarios.AddLoginAsync(usuario!, login);
        if (!enlazado.Succeeded && (await usuarios.FindByLoginAsync(login.LoginProvider, login.ProviderKey))?.Id != usuario!.Id)
        {
            Exigir(enlazado);
        }
        return usuario!;
    }

    private async Task SincronizarRolesDeGruposAsync(
        Guid personaId, Guid organizacionId, IReadOnlyCollection<Guid> grupos, DateTimeOffset ahora, CancellationToken ct)
    {
        var rolesDeGrupos = await plataforma.MapeosGrupoEntra
            .Where(m => grupos.Contains(m.GrupoId))
            .Select(m => m.RolId)
            .Distinct()
            .ToListAsync(ct);

        var actuales = await plataforma.AsignacionesRol
            .Where(a => a.PersonaId == personaId && a.Origen == OrigenAsignacion.GrupoEntra)
            .ToListAsync(ct);

        foreach (var sobrante in actuales.Where(a => !rolesDeGrupos.Contains(a.RolId)))
        {
            plataforma.AsignacionesRol.Remove(sobrante);
        }
        foreach (var rolId in rolesDeGrupos.Where(r => actuales.All(a => a.RolId != r)))
        {
            plataforma.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = personaId,
                RolId = rolId,
                OrganizacionId = organizacionId,
                VigenteDesde = ahora,
                Origen = OrigenAsignacion.GrupoEntra,
            });
        }
    }

    private Task<List<string>> RolesVigentesAsync(Guid personaId, DateTimeOffset ahora, CancellationToken ct) =>
        plataforma.AsignacionesRol
            .Where(a => a.PersonaId == personaId && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
            .Join(plataforma.Roles, a => a.RolId, r => r.Id, (a, r) => r.Codigo)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    private static ClaimsPrincipal Principal(
        UsuarioAtlas usuario, Persona persona, Guid organizacionId, IEnumerable<string> roles, CuentaEmpleado cuenta,
        DateTimeOffset ahora)
    {
        var identidad = new ClaimsIdentity(ClaimsAtlas.EsquemaCookie, ClaimsAtlas.Nombre, ClaimsAtlas.Rol);
        identidad.AddClaim(new Claim(ClaimsAtlas.Sub, usuario.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.PersonaId, persona.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoEmpleado));
        identidad.AddClaim(new Claim(ClaimsAtlas.OrganizacionId, organizacionId.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.Nombre, persona.Nombre));
        identidad.AddClaim(new Claim(ClaimsAtlas.Proveedor, cuenta.Proveedor));
        identidad.AddClaim(new Claim(ClaimsAtlas.AuthTime, ahora.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        identidad.AddClaims(roles.Select(r => new Claim(ClaimsAtlas.Rol, r)));
        identidad.AddClaims(cuenta.Amr.Append("fed").Distinct().Select(m => new Claim(ClaimsAtlas.Amr, m)));
        return new ClaimsPrincipal(identidad);
    }

    private static void Exigir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(
                "No se pudo crear el usuario: " + string.Join("; ", resultado.Errors.Select(e => e.Code)));
        }
    }
}
