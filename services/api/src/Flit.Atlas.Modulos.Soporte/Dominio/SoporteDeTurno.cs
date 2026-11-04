using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Endpoints;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>
/// «¿Quién está de turno?» para los demás módulos (<see cref="ISoporteDeTurno"/>, HU 2.5, ATLAS-ADR-0031): los equipos
/// activos con sus turnos vigentes de una fecha y su horario. El módulo de equipo lo usa en «¿A quién llamo?» sin leer
/// las tablas de soporte.
/// </summary>
internal sealed class SoporteDeTurno(SoporteDbContext soporte) : ISoporteDeTurno
{
    public async Task<SoporteDelDia?> DelDiaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        var configuracion = await Comunes.ConfiguracionAsync(soporte, ct).ConfigureAwait(false);
        var equipos = await soporte.Equipos.AsNoTracking().Where(e => e.Activo).OrderBy(e => e.Nombre).ToListAsync(ct).ConfigureAwait(false);
        var turnos = await EndpointsTurnos.TurnosVigentes(soporte).Where(t => t.Fecha == fecha).OrderBy(t => t.CreadoEn)
            .ToListAsync(ct).ConfigureAwait(false);
        var festivo = FestivosColombia.Entre(fecha, fecha).Select(f => f.Nombre).FirstOrDefault()
            ?? await soporte.FestivosAdicionales.AsNoTracking().Where(f => f.Fecha == fecha).Select(f => f.Nombre).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new SoporteDelDia(fecha, festivo,
        [
            .. equipos.Select(e =>
            {
                var delEquipo = turnos.Where(t => t.EquipoId == e.Id).ToList();
                var (inicio, fin) = Comunes.HorarioDe(e, configuracion);
                var primero = delEquipo.FirstOrDefault();
                return new EquipoDeTurno(e.Id, e.Nombre, e.Area, [.. delEquipo.Select(t => t.PersonaId)],
                    Comunes.Hora(primero?.HoraInicio ?? inicio), Comunes.Hora(primero?.HoraFin ?? fin));
            }),
        ]);
    }
}
