using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Aprovisionamiento;

/// <summary>
/// Asignaciones de origen <see cref="OrigenAsignacion.GrupoEntra"/> de un empleado según sus grupos de Entra (prompt
/// maestro §6.1): las calcula el ingreso interactivo y, desde ATLAS-ADR-0049 (F4-SEC-08), también la sincronización diaria
/// con Graph, para que salir de un grupo quite el rol aunque la persona no vuelva a entrar. Deja los cambios pendientes en
/// el contexto (con su auditoría, con el sistema como actor); quien llama guarda y, si hubo cambios, invalida las sesiones.
/// </summary>
internal static class RolesDeGruposEntra
{
    /// <returns><c>true</c> si agregó o quitó alguna asignación.</returns>
    public static async Task<bool> SincronizarAsync(
        PlataformaDbContext plataforma, Guid personaId, Guid organizacionId, IReadOnlyCollection<Guid> grupos, DateTimeOffset ahora, CancellationToken ct)
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
            return false;
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
        return true;
    }

    private static EventoAuditoria EventoGrupo(string accion, AsignacionRol asignacion, Dictionary<Guid, string> codigos, DateTimeOffset ahora) =>
        AdministracionRoles.CrearEvento(ahora, accion, "asignacion_rol", asignacion.Id, asignacion.OrganizacionId, null,
            AdministracionRoles.DatosAsignacion(asignacion, codigos.GetValueOrDefault(asignacion.RolId, "?")));
}
