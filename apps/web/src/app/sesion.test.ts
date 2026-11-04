import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/** HU 2.12: lo guardado en el dispositivo es de una sola persona; sin sesión o con otra persona, se borra. */
const almacen = vi.hoisted(() => {
  const memoria = new Map<string, unknown>();
  return {
    memoria,
    guardar: vi.fn(async (clave: string, datos: unknown) => {
      memoria.set(clave, datos);
    }),
    leer: vi.fn(async (clave: string) => (memoria.has(clave) ? { datos: memoria.get(clave), guardadoEn: new Date() } : null)),
    borrarDatosDePersona: vi.fn(async () => memoria.clear()),
    borrarDatosLocales: vi.fn(async () => memoria.clear()),
  };
});

vi.mock('../pwa/almacen', () => almacen);

const { cargarSesion } = await import('./sesion');

const yo = (personaId: string) => ({
  sub: personaId, personaId, nombre: 'Persona Demo', tipoPrincipal: 'empleado', organizacionId: 'o1', proveedor: 'simulado', roles: [], permisos: [],
});

const responder = (status: number, cuerpo?: unknown) =>
  vi.stubGlobal('fetch', vi.fn(async () => new Response(cuerpo === undefined ? null : JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } })));

beforeEach(() => {
  almacen.memoria.clear();
  vi.clearAllMocks();
});
afterEach(() => vi.unstubAllGlobals());

describe('dueño de los datos del dispositivo', () => {
  it('la misma persona conserva lo guardado; otra persona lo borra antes de guardar lo suyo', async () => {
    responder(200, yo('p1'));
    await cargarSesion();
    almacen.memoria.set('soporte.turnos', { turnos: [] });
    await cargarSesion();
    expect(almacen.borrarDatosDePersona).not.toHaveBeenCalled();
    expect(almacen.memoria.has('soporte.turnos')).toBe(true);

    responder(200, yo('p2'));
    await cargarSesion();
    expect(almacen.borrarDatosDePersona).toHaveBeenCalledTimes(1);
    expect(almacen.memoria.has('soporte.turnos')).toBe(false);
    expect(almacen.memoria.get('plataforma.dueno')).toBe('p2');
  });

  it('sin sesión (401) no queda nada de quien estaba', async () => {
    responder(200, yo('p1'));
    await cargarSesion();
    almacen.memoria.set('equipo.responsabilidades', {});

    responder(401);
    expect((await cargarSesion()).yo).toBeNull();
    expect(almacen.borrarDatosDePersona).toHaveBeenCalledTimes(1);
    expect(almacen.memoria.size).toBe(0);
  });
});
