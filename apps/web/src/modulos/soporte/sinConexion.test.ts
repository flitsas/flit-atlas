import { describe, expect, it } from 'vitest';

import { type Calendario, recortarCalendario, type Turno } from './api';
import { tresMeses } from './recursos';

/** HU 2.12: sin conexión, cualquier rango del calendario sale de los tres meses guardados. */
const turno = (id: string, fecha: string, equipo: string): Turno => ({
  id,
  equipo: { id: equipo, nombre: equipo },
  fecha,
  persona: { id: 'p1', nombre: 'Persona Demo', correo: null, cargo: null, telefono: null },
  horaInicio: '08:00',
  horaFin: '12:00',
  estado: 'programado',
  origen: 'generado',
  turnoReemplazadoId: null,
  festivo: null,
  observacion: null,
  requiereRevision: false,
});

const guardado: Calendario = {
  desde: '2026-10-23',
  hasta: '2027-01-22',
  equipoId: null,
  turnos: [turno('t1', '2026-10-24', 'e1'), turno('t2', '2026-11-07', 'e2'), turno('t3', '2026-12-05', 'e1')],
  festivos: [{ fecha: '2026-11-02', nombre: 'Todos los Santos', tipo: 'trasladable_al_lunes', trasladado: true, id: null }],
};

describe('calendario sin conexión', () => {
  it('recorta por rango y por equipo, y no inventa lo que no está guardado', () => {
    const noviembre = recortarCalendario(guardado, '2026-10-26', '2026-12-06', null)!;
    expect(noviembre.turnos.map((t) => t.id)).toEqual(['t2', 't3']);
    expect(noviembre.festivos).toHaveLength(1);
    expect(recortarCalendario(guardado, '2026-10-01', '2027-01-31', 'e1')!.turnos.map((t) => t.id)).toEqual(['t1', 't3']);
    expect(recortarCalendario(guardado, '2027-03-01', '2027-03-31', null)).toBeNull();
  });

  it('los tres meses son el rango de la vista en lista', () => {
    expect(tresMeses('2026-10-23')).toEqual({ desde: '2026-10-23', hasta: '2026-12-31' });
  });
});
