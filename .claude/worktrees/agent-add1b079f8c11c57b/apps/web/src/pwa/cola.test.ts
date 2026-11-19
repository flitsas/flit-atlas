import { afterEach, describe, expect, it, vi } from 'vitest';

import { descartar, ejecutarOEncolar, enviarPendientes, vaciarCola } from './cola';

/** ATLAS-ADR-0032: la cola de acciones sin conexión (en memoria cuando no hay IndexedDB, como en jsdom). */
const accion = (llave: string) => ({ llave, modulo: 'prueba', descripcion: `Acción ${llave}`, ruta: '/api/v1/prueba', metodo: 'POST' as const, cuerpo: { llave } });

const respuesta = (status: number, cuerpo: unknown = {}) =>
  new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });

afterEach(async () => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  await vaciarCola();
});

describe('cola de acciones sin conexión', () => {
  it('con red envía de una vez; sin red (o si la red falla en el camino) la encola', async () => {
    const fetch = vi.fn(async () => respuesta(200, { ok: true }));
    vi.stubGlobal('fetch', fetch);
    expect(await ejecutarOEncolar(accion('a'))).toEqual({ estado: 'enviada', respuesta: { ok: true } });

    fetch.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    expect((await ejecutarOEncolar(accion('b'))).estado).toBe('encolada');
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    expect((await ejecutarOEncolar(accion('c'))).estado).toBe('encolada');
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('al reintentar envía en orden, guarda el motivo de lo que el servidor rechaza y se detiene si no hay red', async () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    vi.stubGlobal('fetch', vi.fn());
    for (const llave of ['1', '2', '3']) await ejecutarOEncolar(accion(llave));

    const fetch = vi
      .fn()
      .mockResolvedValueOnce(respuesta(201))
      .mockResolvedValueOnce(respuesta(409, { error: 'solicitud_abierta', mensaje: 'Ya hay una solicitud abierta.' }))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'));
    vi.stubGlobal('fetch', fetch);
    expect(await enviarPendientes()).toEqual({ enviadas: 1, rechazadas: 1, pendientes: 1 });

    // Lo rechazado ya no se reintenta; lo pendiente sí, con la misma llave.
    fetch.mockResolvedValue(respuesta(200));
    expect(await enviarPendientes()).toEqual({ enviadas: 1, rechazadas: 0, pendientes: 0 });
    expect(JSON.parse(String(fetch.mock.calls.at(-1)?.[1]?.body))).toEqual({ llave: '3' });
    await descartar('2');
    expect(await enviarPendientes()).toEqual({ enviadas: 0, rechazadas: 0, pendientes: 0 });
  });
});
