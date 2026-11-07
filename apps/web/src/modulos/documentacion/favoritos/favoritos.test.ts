import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/** IndexedDB en memoria (jsdom no lo trae), con lo que usan los favoritos. */
const memoria = vi.hoisted(() => new Map<string, { datos: unknown; guardadoEn: number; venceEn: number; permiso?: string }>());

vi.mock('../../../pwa/almacen', () => ({
  guardar: vi.fn(async (clave: string, datos: unknown, ttlHoras: number, ahora = Date.now(), permiso?: string) => {
    memoria.set(clave, { datos, guardadoEn: ahora, venceEn: ahora + ttlHoras * 3_600_000, permiso });
  }),
  leer: vi.fn(async (clave: string, ahora = Date.now()) => {
    const r = memoria.get(clave);
    if (!r || r.venceEn <= ahora) return null;
    return { datos: r.datos, guardadoEn: new Date(r.guardadoEn) };
  }),
  olvidar: vi.fn(async (clave: string) => {
    memoria.delete(clave);
  }),
  clavesConPrefijo: vi.fn(async (prefijo: string) => [...memoria.keys()].filter((c) => c.startsWith(prefijo))),
}));

const { clavesConPrefijo, guardar } = await import('../../../pwa/almacen');
const { guardadoEn, guardarFavorito, olvidarFavorito, PERMISO, PREFIJO, recurso, RUTA_FAVORITOS, rutaDocumento, rutaVersiones, sincronizar } = await import('./sinConexion');

function api(rutas: Record<string, [number, unknown]>) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string) => {
      const r = rutas[ruta];
      return new Response(JSON.stringify(r?.[1] ?? {}), { status: r?.[0] ?? 404, headers: { 'Content-Type': 'application/json' } });
    }),
  );
}

const favorito = (id: string) => ({ id: `f-${id}`, documentoId: id, titulo: 'Doc', tipo: 'runbook', versionVigente: 1, ruta: `/documentacion/${id}`, marcadoEn: '2026-11-06T12:00:00Z' });

beforeEach(() => memoria.clear());

afterEach(() => vi.unstubAllGlobals());

describe('favoritos de documentos sin conexión (HU 5.4)', () => {
  it('sin el permiso no pide ni guarda nada', async () => {
    api({});
    expect(await sincronizar({ permisos: [] })).toEqual({ guardados: 0, borrados: 0 });
    expect(fetch).not.toHaveBeenCalled();
  });

  it('guarda cada favorito y borra lo que ya no es de ninguno', async () => {
    await guardar(`${PREFIJO}${rutaDocumento('viejo')}`, { documento: 'viejo' }, 72, Date.now(), PERMISO);
    api({
      [RUTA_FAVORITOS]: [200, [favorito('d1')]],
      [rutaDocumento('d1')]: [200, { documento: { id: 'd1' } }],
      [rutaVersiones('d1')]: [200, []],
    });

    const resultado = await sincronizar({ permisos: [PERMISO] });

    expect(resultado).toEqual({ guardados: 2, borrados: 1 });
    expect(await guardadoEn('d1')).toBeInstanceOf(Date);
    expect(await guardadoEn('viejo')).toBeNull();
  });

  it('sin red o con la lista en error no borra lo guardado', async () => {
    await guardar(recurso(rutaDocumento('d1')).clave, { documento: 'd1' }, 72, Date.now(), PERMISO);
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('sin red'))));

    expect(await sincronizar({ permisos: [PERMISO] })).toEqual({ guardados: 0, borrados: 0 });
    expect(await guardadoEn('d1')).toBeInstanceOf(Date);
  });

  it('si el servidor ya no lo deja ver, lo borra del dispositivo', async () => {
    await guardar(recurso(rutaDocumento('d1')).clave, { documento: 'd1' }, 72, Date.now() - 3_600_000, PERMISO);
    api({ [rutaDocumento('d1')]: [404, {}] });

    expect(await guardarFavorito('d1')).toBe('sin_acceso');
    expect(await guardadoEn('d1')).toBeNull();
  });

  it('al quitar el favorito se borra lo suyo', async () => {
    api({ [rutaDocumento('d1')]: [200, {}], [rutaVersiones('d1')]: [200, []] });
    await guardarFavorito('d1');
    expect(await clavesConPrefijo(PREFIJO)).toHaveLength(2);

    await olvidarFavorito('d1');

    expect(await clavesConPrefijo(PREFIJO)).toHaveLength(0);
  });
});
