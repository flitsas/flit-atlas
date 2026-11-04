import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ErrorApi } from '../app/api';
import type { ModuloAtlas } from '../app/modulos/contrato';
import { DatosGuardados } from '../app/ui/DatosGuardados';

/** IndexedDB en memoria (jsdom no lo trae): lo que guardan y leen `sinConexion.ts` y `sincronizacion.ts`. */
const memoria = vi.hoisted(() => new Map<string, { datos: unknown; guardadoEn: number; venceEn: number; permiso?: string }>());

vi.mock('./almacen', () => ({
  guardar: vi.fn(async (clave: string, datos: unknown, ttlHoras: number, ahora = Date.now(), permiso?: string) => {
    memoria.set(clave, { datos, guardadoEn: ahora, venceEn: ahora + ttlHoras * 3_600_000, permiso });
  }),
  leer: vi.fn(async (clave: string, ahora = Date.now()) => {
    const r = memoria.get(clave);
    if (!r || r.venceEn <= ahora) return null;
    return { datos: r.datos, guardadoEn: new Date(r.guardadoEn) };
  }),
  depurarSinPermiso: vi.fn(async (permisos: readonly string[]) => {
    let n = 0;
    for (const [clave, r] of memoria) {
      if (r.permiso && !permisos.includes(r.permiso)) {
        memoria.delete(clave);
        n++;
      }
    }
    return n;
  }),
}));

const { conRespaldo, conRespaldoDerivado, reiniciarOrigenes, sinContacto } = await import('./sinConexion');
const { sincronizarSinConexion } = await import('./sincronizacion');

const RECURSO = { clave: 'prueba.reparto', permiso: 'atlas.equipo.ver', ttlHoras: 72 };
const sinRed = () => Promise.reject(new TypeError('Failed to fetch'));
const respuesta = (cuerpo: unknown, status = 200) => new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => memoria.clear());
afterEach(() => {
  act(() => reiniciarOrigenes());
  vi.unstubAllGlobals();
});

describe('datos sin conexión (HU 2.12)', () => {
  it('nunca guarda teléfonos, en ningún nivel', () => {
    const datos = { persona: { nombre: 'Persona Demo', telefono: '3000000000' }, lista: [{ Telefono: '1', correo: 'demo@ejemplo.com' }] };
    expect(sinContacto(datos)).toEqual({ persona: { nombre: 'Persona Demo', telefono: null }, lista: [{ Telefono: null, correo: 'demo@ejemplo.com' }] });
    expect(datos.persona.telefono).toBe('3000000000');
  });

  it('en línea guarda lo que llega; sin red muestra lo guardado con el aviso de la fecha', async () => {
    render(<DatosGuardados recurso={RECURSO.clave} />);
    const enLinea = await conRespaldo(RECURSO, async () => ({ principal: { nombre: 'Persona Demo', telefono: '3000000000' } }));
    expect(enLinea.principal.telefono).toBe('3000000000');
    expect(memoria.get(RECURSO.clave)).toMatchObject({ datos: { principal: { telefono: null } }, permiso: 'atlas.equipo.ver' });
    expect(screen.queryByRole('status')).toBeNull();

    const guardado = await act(() => conRespaldo(RECURSO, sinRed));
    expect(guardado).toEqual({ principal: { nombre: 'Persona Demo', telefono: null } });
    expect(screen.getByRole('status')).toHaveTextContent(/^Sin conexión: datos del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/);

    // De vuelta en línea, el aviso se va.
    await act(() => conRespaldo(RECURSO, async () => ({ principal: null })));
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('un error del servidor no se tapa con lo guardado, y sin nada guardado la falla de red sigue', async () => {
    await conRespaldo(RECURSO, async () => ({ version: 5 }));
    await expect(conRespaldo(RECURSO, () => Promise.reject(new ErrorApi(403)))).rejects.toBeInstanceOf(ErrorApi);
    memoria.clear();
    await expect(conRespaldo(RECURSO, sinRed)).rejects.toBeInstanceOf(TypeError);
  });

  it('sin red deriva de otro recurso guardado; en línea solo guarda lo que corresponde', async () => {
    const consulta = (n: number) => async () => ({ elementos: [n] });
    await conRespaldoDerivado(RECURSO, consulta(1), () => null, () => null);
    expect(memoria.has(RECURSO.clave)).toBe(false);
    await conRespaldoDerivado(RECURSO, consulta(2), () => null, (d) => d);
    expect(await conRespaldoDerivado(RECURSO, sinRed, (g: { elementos: number[] }) => ({ elementos: g.elementos.map((n) => n * 10) }))).toEqual({ elementos: [20] });
    await expect(conRespaldoDerivado(RECURSO, sinRed, () => null)).rejects.toBeInstanceOf(TypeError);
  });

  it('la sincronización guarda solo lo permitido de los módulos activos y borra lo que perdió el permiso', async () => {
    memoria.set('viejo.sin.permiso', { datos: {}, guardadoEn: Date.now(), venceEn: Date.now() + 1e6, permiso: 'atlas.catalogo.ver' });
    const fetch = vi.fn(async (ruta: string) => respuesta({ ruta, persona: { telefono: '3000000000' } }));
    vi.stubGlobal('fetch', fetch);
    const modulo = (id: string, bandera: boolean, permiso: string): ModuloAtlas => ({
      id, nombre: id, descripcion: id, icono: 'equipo', estado: 'disponible', rutas: [], navegacion: [],
      bandera: `modulo.${id}`, banderaPorDefecto: bandera,
      offline: { recursos: [{ clave: `${id}.datos`, permiso, ruta: () => `/api/v1/${id}/datos` }], ttlHoras: 72 },
    });

    const resultado = await sincronizarSinConexion(
      [modulo('equipo', true, 'atlas.equipo.ver'), modulo('soporte', true, 'atlas.soporte.ver'), modulo('apagado', false, 'atlas.equipo.ver')],
      { banderas: {}, permisos: ['atlas.equipo.ver'] },
    );

    expect(resultado).toEqual({ guardados: 1, borrados: 1 });
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(memoria.get('equipo.datos')?.datos).toEqual({ ruta: '/api/v1/equipo/datos', persona: { telefono: null } });
    expect(memoria.has('viejo.sin.permiso')).toBe(false);

    // Recién guardado: no se vuelve a pedir.
    await sincronizarSinConexion([modulo('equipo', true, 'atlas.equipo.ver')], { banderas: {}, permisos: ['atlas.equipo.ver'] });
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
