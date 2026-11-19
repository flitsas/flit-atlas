using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Alcance de una asignación o de un recurso: tipo (<see cref="TiposAlcance"/>) e id del recurso.</summary>
public sealed record AlcanceAsignacion(string Tipo, string? Id = null)
{
    public static AlcanceAsignacion Global { get; } = new(TiposAlcance.Global);

    public bool EsGlobal => Tipo == TiposAlcance.Global;

    public override string ToString() => EsGlobal ? Tipo : $"{Tipo}:{Id}";
}

/// <summary>Un rol vigente de la persona, con su organización, su alcance y de dónde viene.</summary>
/// <param name="Origen"><c>manual</c>, <c>grupo_entra</c>, <c>invitacion</c>, <c>base</c> (atlas.lector de todo empleado) o <c>implicito</c>.</param>
public sealed record ConcesionRol(string Rol, Guid? OrganizacionId, AlcanceAsignacion Alcance, DateTimeOffset? VigenteHasta, string Origen);

/// <summary>
/// Rol que da la propiedad de un recurso (responsable de un aplicativo, §7.2). Lo entrega
/// <see cref="IProveedorRolesImplicitos"/>; el rol debe ser de tipo <see cref="TipoRol.Implicito"/>.
/// </summary>
public sealed record RolImplicito(string Rol, AlcanceAsignacion Alcance);

/// <summary>
/// Punto de extensión del rol implícito por propiedad (§7.2, ATLAS-ADR-0023). En F1 no hay responsables: la
/// implementación por defecto no entrega nada. En F2 el módulo de equipo registra la suya (responsable principal o
/// secundario ⇒ <see cref="RolesAtlas.Responsable"/> con alcance <c>aplicativo:&lt;id&gt;</c>).
/// </summary>
public interface IProveedorRolesImplicitos
{
    Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default);
}

internal sealed class SinRolesImplicitos : IProveedorRolesImplicitos
{
    public Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RolImplicito>>([]);
}

/// <summary>
/// Roles y permisos de una persona en este momento, calculados en el servidor (ATLAS-ADR-0023). No viajan en la cookie
/// ni se guardan en la sesión.
/// </summary>
public sealed class PermisosEfectivos
{
    private readonly CatalogoPermisos? _catalogo;

    private PermisosEfectivos(CatalogoPermisos? catalogo, IReadOnlyList<ConcesionRol> concesiones)
    {
        _catalogo = catalogo;
        Concesiones = concesiones;
        Roles = [.. concesiones.Select(c => c.Rol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Permisos = catalogo is null
            ? []
            : [.. concesiones.SelectMany(c => catalogo.PermisosDe(c.Rol)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        _conjunto = new HashSet<string>(Permisos, StringComparer.Ordinal);
    }

    private readonly HashSet<string> _conjunto;

    public static PermisosEfectivos Ninguno { get; } = new(null, []);

    public static PermisosEfectivos Desde(CatalogoPermisos catalogo, IReadOnlyList<ConcesionRol> concesiones)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        ArgumentNullException.ThrowIfNull(concesiones);
        return new PermisosEfectivos(catalogo, concesiones);
    }

    public IReadOnlyList<ConcesionRol> Concesiones { get; }

    /// <summary>Códigos de los roles vigentes, ordenados.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>Permisos que concede al menos un rol vigente, en cualquier alcance; ordenados. Es lo que recibe la PWA.</summary>
    public IReadOnlyList<string> Permisos { get; }

    /// <summary>¿Tiene el permiso en algún alcance? Es lo que exige la política de un endpoint.</summary>
    public bool Tiene(string permiso) => _conjunto.Contains(permiso);

    /// <summary>
    /// ¿Tiene el permiso sobre un recurso? Vale una concesión global o una cuyo alcance sea el del recurso (mismo tipo e
    /// id) o el de su organización. Las consultas de los módulos lo usan junto con el predicado de organización.
    /// </summary>
    public bool TieneEn(string permiso, AlcanceAsignacion recurso, Guid? organizacionDelRecurso = null)
    {
        ArgumentNullException.ThrowIfNull(recurso);
        if (_catalogo is null || !Tiene(permiso))
        {
            return false;
        }
        return Concesiones.Any(c => _catalogo.PermisosDe(c.Rol).Contains(permiso) && (
            c.Alcance.EsGlobal
            || (c.Alcance.Tipo == recurso.Tipo && string.Equals(c.Alcance.Id, recurso.Id, StringComparison.Ordinal))
            || (c.Alcance.Tipo == TiposAlcance.Organizacion && organizacionDelRecurso is { } org
                && string.Equals(c.Alcance.Id, org.ToString(), StringComparison.OrdinalIgnoreCase))));
    }
}

/// <summary>
/// Calcula los <see cref="PermisosEfectivos"/> de una persona con la hora actual: asignaciones vigentes (manuales, de
/// grupos de Entra o de invitaciones), <see cref="RolesAtlas.Lector"/> para todo empleado y los roles implícitos. Una
/// persona inactiva o suspendida, o de una organización suspendida, no tiene permisos; <see cref="RolesAtlas.AdminOrganizacion"/>
/// solo cuenta si su organización lo tiene habilitado. Una asignación vencida deja de
/// contar en la primera petición después de su vencimiento, sin que nadie intervenga.
/// </summary>
public sealed class CalculadoraPermisos(
    PlataformaDbContext plataforma,
    CatalogoPermisos catalogo,
    IProveedorRolesImplicitos implicitos,
    TimeProvider reloj)
{
    public const string OrigenBase = "base";
    public const string OrigenImplicito = "implicito";

    public async Task<PermisosEfectivos> CalcularAsync(Guid personaId, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow();
        var persona = await plataforma.Personas.AsNoTracking()
            .Where(p => p.Id == personaId && p.Estado == EstadoPersona.Activa)
            .Join(plataforma.Organizaciones.Where(o => o.Estado == EstadoOrganizacion.Activa),
                p => p.OrganizacionId, o => o.Id, (p, o) => new { p.Tipo, p.OrganizacionId, o.AdminOrganizacionHabilitado })
            .FirstOrDefaultAsync(ct);
        if (persona is null)
        {
            return PermisosEfectivos.Ninguno;
        }

        var asignaciones = await plataforma.AsignacionesRol.AsNoTracking()
            .Where(a => a.PersonaId == personaId && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
            .Join(plataforma.Roles, a => a.RolId, r => r.Id, (a, r) => new
            {
                r.Codigo, r.Tipo, a.OrganizacionId, a.AlcanceTipo, a.AlcanceId, a.VigenteHasta, a.Origen,
            })
            .ToListAsync(ct);

        var concesiones = new List<ConcesionRol>();
        foreach (var a in asignaciones)
        {
            // Defensa: un rol externo solo vale para un externo de esa organización, y un interno solo para un empleado.
            // ext.admin_organizacion solo vale si la organización lo tiene habilitado (§7.3, HU 1.5).
            var valida = a.Tipo switch
            {
                TipoRol.Externo => persona.Tipo == TipoPersona.Externo && a.OrganizacionId == persona.OrganizacionId
                    && (a.Codigo != RolesAtlas.AdminOrganizacion || persona.AdminOrganizacionHabilitado),
                TipoRol.Interno => persona.Tipo == TipoPersona.Empleado,
                _ => false,
            };
            if (valida)
            {
                concesiones.Add(new ConcesionRol(a.Codigo, a.OrganizacionId, new AlcanceAsignacion(a.AlcanceTipo, a.AlcanceId),
                    a.VigenteHasta, ConvertidorEnumSnake<OrigenAsignacion>.ASnake(a.Origen.ToString())));
            }
        }
        if (persona.Tipo == TipoPersona.Empleado)
        {
            concesiones.Add(new ConcesionRol(RolesAtlas.Lector, persona.OrganizacionId, AlcanceAsignacion.Global, null, OrigenBase));
        }
        foreach (var implicito in await implicitos.ObtenerAsync(personaId, ct))
        {
            if (catalogo.Rol(implicito.Rol)?.Tipo == TipoRol.Implicito && !implicito.Alcance.EsGlobal)
            {
                concesiones.Add(new ConcesionRol(implicito.Rol, persona.OrganizacionId, implicito.Alcance, null, OrigenImplicito));
            }
        }
        return PermisosEfectivos.Desde(catalogo, concesiones);
    }
}
