using System.Globalization;
using System.Security.Claims;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Identidad.Aprovisionamiento;

/// <summary>Lo que sabemos de un empleado al entrar: del token de Entra (o del proveedor simulado) y de Graph.</summary>
/// <param name="Grupos">Ids de los grupos de Entra; <c>null</c> si no se pudieron leer (no se tocan sus roles).</param>
/// <param name="AutenticadoEn">Cuándo se autenticó en el proveedor (<c>auth_time</c> de Entra); si falta, ahora.</param>
/// <param name="Reautenticacion">El ingreso lo pidió una acción sensible (ATLAS-ADR-0022).</param>
public sealed record CuentaEmpleado(
    string Proveedor,
    string IdExterno,
    Guid? EntraOid,
    string Correo,
    string Nombre,
    IReadOnlyCollection<Guid>? Grupos,
    string? Cargo,
    string? Area,
    IReadOnlyCollection<string> Amr,
    DateTimeOffset? AutenticadoEn = null,
    bool Reautenticacion = false);

public sealed class CorreoEnUsoException(string mensaje) : InvalidOperationException(mensaje);

/// <summary>La persona no puede entrar: inactiva, suspendida o con la cuenta bloqueada.</summary>
public sealed class IngresoNoPermitidoException(string motivo)
    : UnauthorizedAccessException($"Ingreso no permitido: {motivo}.")
{
    public const string PersonaInactiva = "persona_inactiva";
    public const string PersonaSuspendida = "persona_suspendida";
    public const string CuentaBloqueada = "cuenta_bloqueada";

    public string Motivo { get; } = motivo;
}

/// <summary>
/// Aprovisionamiento just-in-time de empleados (prompt maestro §6.1): crea o enlaza la <see cref="Persona"/> y el
/// <see cref="UsuarioAtlas"/>, sincroniza los roles que vienen de los grupos de Entra y arma el principal de Atlas.
/// Es idempotente: si un paso falla, el siguiente ingreso lo completa.
/// </summary>
public sealed class AprovisionadorEmpleados(
    PlataformaDbContext plataforma,
    UserManager<UsuarioAtlas> usuarios,
    RegistroSeguridad registro,
    TimeProvider reloj)
{
    public const string NombreOrganizacionInterna = "FLIT S.A.S.";
    public const string ProveedorEntra = "entra";
    public const string ProveedorSimulado = "simulado";

    public async Task<ClaimsPrincipal> AprovisionarAsync(CuentaEmpleado cuenta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cuenta);

        // Los ingresos de una misma cuenta (dos pestañas) van en fila, con un candado consultivo de Postgres sobre la
        // conexión: sin él, el segundo veía la persona recién creada sin su login y respondía «correo en uso».
        var clave = $"atlas.identidad.ingreso:{cuenta.Proveedor}:{cuenta.IdExterno}";
        await plataforma.Database.OpenConnectionAsync(ct);
        try
        {
            await plataforma.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtextextended({clave}, 0))", ct);
            try
            {
                return await IntentarAsync(cuenta, ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Dos cuentas distintas entraron a la vez el primer día y crearon lo mismo (la organización interna):
                // se descarta lo pendiente y se reintenta una vez; esta vez se encuentra lo que creó el otro.
                plataforma.ChangeTracker.Clear();
                return await IntentarAsync(cuenta, ct);
            }
            finally
            {
                await plataforma.Database.ExecuteSqlAsync(
                    $"SELECT pg_advisory_unlock(hashtextextended({clave}, 0))", CancellationToken.None);
            }
        }
        finally
        {
            await plataforma.Database.CloseConnectionAsync();
        }
    }

    private async Task<ClaimsPrincipal> IntentarAsync(CuentaEmpleado cuenta, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();

        var organizacion = await OrganizacionInternaAsync(ct);
        var usuario = await usuarios.FindByLoginAsync(cuenta.Proveedor, cuenta.IdExterno);
        if (usuario is not null && await usuarios.IsLockedOutAsync(usuario))
        {
            throw new IngresoNoPermitidoException(IngresoNoPermitidoException.CuentaBloqueada);
        }
        var persona = usuario is null
            ? await BuscarOCrearPersonaAsync(cuenta, organizacion.Id, ct)
            : await plataforma.Personas.SingleAsync(p => p.Id == usuario.PersonaId, ct);
        ExigirActiva(persona.Estado);

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

        // El sello va en la sesión y se lee ANTES de la última comprobación del estado; la suspensión hace lo
        // contrario (estado y luego sello). Así una suspensión simultánea nunca deja viva esta sesión (ATLAS-ADR-0022).
        var sello = usuario.SecurityStamp ?? string.Empty;
        var estado = await plataforma.Personas.AsNoTracking().Where(p => p.Id == persona.Id).Select(p => p.Estado).SingleAsync(ct);
        ExigirActiva(estado);

        var roles = await RolesVigentesAsync(persona.Id, ahora, ct);
        var datos = new DatosEventoSeguridad
        {
            UsuarioId = usuario.Id,
            PersonaId = persona.Id,
            OrganizacionId = organizacion.Id,
            Detalle = new { proveedor = cuenta.Proveedor },
        };
        await registro.RegistrarAsync(TiposEventoSeguridad.InicioSesion, datos, ct);
        if (cuenta.Reautenticacion)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.Reautenticacion, datos, ct);
        }

        return Principal(usuario, persona, organizacion.Id, roles, cuenta, sello, cuenta.AutenticadoEn ?? ahora);
    }

    private static void ExigirActiva(EstadoPersona estado)
    {
        if (estado != EstadoPersona.Activa)
        {
            throw new IngresoNoPermitidoException(estado == EstadoPersona.Suspendida
                ? IngresoNoPermitidoException.PersonaSuspendida
                : IngresoNoPermitidoException.PersonaInactiva);
        }
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

        var sobrantes = actuales.Where(a => !rolesDeGrupos.Contains(a.RolId)).ToList();
        var nuevas = rolesDeGrupos.Where(r => actuales.All(a => a.RolId != r)).Select(rolId => new AsignacionRol
        {
            PersonaId = personaId,
            RolId = rolId,
            OrganizacionId = organizacionId,
            VigenteDesde = ahora,
            Origen = OrigenAsignacion.GrupoEntra,
        }).ToList();
        if (sobrantes.Count == 0 && nuevas.Count == 0)
        {
            return;
        }

        // Los cambios que vienen de Entra también quedan en la auditoría (HU 1.4), con el sistema como actor.
        var ids = sobrantes.Concat(nuevas).Select(a => a.RolId).ToList();
        var codigos = await plataforma.Roles.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        foreach (var sobrante in sobrantes)
        {
            plataforma.AsignacionesRol.Remove(sobrante);
            plataforma.Auditoria.Add(EventoGrupo(AccionesAuditoriaRoles.AsignacionRetirada, sobrante, codigos, ahora));
        }
        foreach (var nueva in nuevas)
        {
            plataforma.AsignacionesRol.Add(nueva);
            plataforma.Auditoria.Add(EventoGrupo(AccionesAuditoriaRoles.AsignacionCreada, nueva, codigos, ahora));
        }
    }

    private static EventoAuditoria EventoGrupo(string accion, AsignacionRol asignacion, Dictionary<Guid, string> codigos, DateTimeOffset ahora) =>
        AdministracionRoles.CrearEvento(ahora, accion, "asignacion_rol", asignacion.Id, asignacion.OrganizacionId, null,
            AdministracionRoles.DatosAsignacion(asignacion, codigos.GetValueOrDefault(asignacion.RolId, "?")));

    private Task<List<string>> RolesVigentesAsync(Guid personaId, DateTimeOffset ahora, CancellationToken ct) =>
        plataforma.AsignacionesRol
            .Where(a => a.PersonaId == personaId && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
            .Join(plataforma.Roles, a => a.RolId, r => r.Id, (a, r) => r.Codigo)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    private static ClaimsPrincipal Principal(
        UsuarioAtlas usuario, Persona persona, Guid organizacionId, IEnumerable<string> roles, CuentaEmpleado cuenta,
        string sello, DateTimeOffset autenticadoEn)
    {
        var identidad = new ClaimsIdentity(ClaimsAtlas.EsquemaCookie, ClaimsAtlas.Nombre, ClaimsAtlas.Rol);
        identidad.AddClaim(new Claim(ClaimsAtlas.Sub, usuario.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.PersonaId, persona.Id.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoEmpleado));
        identidad.AddClaim(new Claim(ClaimsAtlas.OrganizacionId, organizacionId.ToString()));
        identidad.AddClaim(new Claim(ClaimsAtlas.Nombre, persona.Nombre));
        identidad.AddClaim(new Claim(ClaimsAtlas.Proveedor, cuenta.Proveedor));
        identidad.AddClaim(new Claim(ClaimsAtlas.AuthTime, autenticadoEn.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        identidad.AddClaim(new Claim(ClaimsAtlas.SelloSeguridad, sello));
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
