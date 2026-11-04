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

const fav = await import('./sinConexion');
const { conRespaldoGuardado } = await import('../../../pwa/sinConexion');

const json = (cuerpo: unknown, status = 200) => new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
const texto = (cuerpo: string) => new Response(cuerpo, { status: 200, headers: { 'Content-Type': 'application/yaml' } });

const PAQUETE = 'p1';
const DETALLE = {
  paquete: { id: PAQUETE },
  contratos: [
    { contrato: { contratoId: 'c1', tipo: 'openapi', nivel: 'tecnico', estadoVersion: 'vigente' } },
    { contrato: { contratoId: 'c2', tipo: 'asyncapi', nivel: 'tecnico', estadoVersion: 'deprecada' } },
    { contrato: { contratoId: 'c3', tipo: 'openapi', nivel: 'tecnico', estadoVersion: 'retirada' } },
  ],
};
const favoritoPortal = (tipo: 'paquete' | 'contrato' | 'guia', extra: Partial<{ contratoId: string; guiaId: string }> = {}) => ({
  id: `f-${tipo}`, tipo, paqueteId: PAQUETE, contratoId: extra.contratoId ?? null, guiaId: extra.guiaId ?? null, titulo: 'Demo', detalle: null,
  ruta: '/portal', marcadoEn: '2026-10-24T10:00:00Z',
});

/** Una API simulada: las rutas dan su respuesta; `prohibidas` responden 404. */
function api(rutas: Record<string, () => Response>, prohibidas: string[] = []) {
  const pedidas: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (entrada: string) => {
    pedidas.push(entrada);
    if (prohibidas.includes(entrada)) return json({ codigo: 'no_encontrado' }, 404);
    const r = rutas[entrada];
    return r ? r() : json({ codigo: 'no_encontrado' }, 404);
  }));
  return pedidas;
}

const clave = (ruta: string) => `${fav.PREFIJO}${ruta}`;

beforeEach(() => memoria.clear());
afterEach(() => vi.unstubAllGlobals());

describe('favoritos sin conexión (HU 4.9)', () => {
  it('un paquete favorito guarda su detalle y el spec podado de sus contratos técnicos no retirados, con el permiso del portal', async () => {
    api({
      [fav.RUTA_FAVORITOS_PORTAL]: () => json([favoritoPortal('paquete')]),
      [fav.rutaPaquete(PAQUETE)]: () => json(DETALLE),
      [fav.rutaSpecPodado(PAQUETE, 'c1')]: () => texto('openapi: 3.1.0'),
      [fav.rutaCambios(PAQUETE, 'c1')]: () => json({ elementos: [] }),
      [fav.rutaSpecPodado(PAQUETE, 'c2')]: () => texto('asyncapi: 3.0.0'),
    });
    const resultado = await fav.sincronizar({ permisos: ['atlas.portal.ver'] });
    expect(resultado.guardados).toBe(4);
    expect([...memoria.keys()].sort()).toEqual(
      [fav.RUTA_FAVORITOS_PORTAL, fav.rutaPaquete(PAQUETE), fav.rutaSpecPodado(PAQUETE, 'c1'), fav.rutaCambios(PAQUETE, 'c1'), fav.rutaSpecPodado(PAQUETE, 'c2')]
        .map(clave)
        .sort(),
    );
    expect([...memoria.values()].every((r) => r.permiso === 'atlas.portal.ver')).toBe(true);
    // El TTL es el de ATLAS-ADR-0034: 72 horas.
    const r = memoria.get(clave(fav.rutaPaquete(PAQUETE)))!;
    expect(r.venceEn - r.guardadoEn).toBe(72 * 3_600_000);
  });

  it('sin el permiso no guarda nada; lo que el servidor ya no devuelve se borra al resincronizar', async () => {
    api({ [fav.RUTA_FAVORITOS_PORTAL]: () => json([]) });
    expect(await fav.sincronizar({ permisos: [] })).toEqual({ guardados: 0, borrados: 0 });
    expect(memoria.size).toBe(0);

    memoria.set(clave(fav.rutaPaquete(PAQUETE)), { datos: DETALLE, guardadoEn: Date.now(), venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    memoria.set(clave(fav.rutaGuia(PAQUETE, 'g1')), { datos: {}, guardadoEn: Date.now(), venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    const resultado = await fav.sincronizar({ permisos: ['atlas.portal.ver'] });
    expect(resultado.borrados).toBe(2);
    expect([...memoria.keys()]).toEqual([clave(fav.RUTA_FAVORITOS_PORTAL)]);
  });

  it('sin red no borra nada', async () => {
    memoria.set(clave(fav.rutaPaquete(PAQUETE)), { datos: DETALLE, guardadoEn: Date.now(), venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))));
    expect(await fav.sincronizar({ permisos: ['atlas.portal.ver'] })).toEqual({ guardados: 0, borrados: 0 });
    expect(memoria.has(clave(fav.rutaPaquete(PAQUETE)))).toBe(true);
  });

  it('un 404 al resincronizar (compartición revocada o vencida) borra lo del favorito', async () => {
    const ruta = fav.rutaGuia(PAQUETE, 'g1');
    memoria.set(clave(fav.rutaPaquete(PAQUETE)), { datos: DETALLE, guardadoEn: 0, venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    memoria.set(clave(ruta), { datos: { contenido: 'x' }, guardadoEn: 0, venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    api({}, [fav.rutaPaquete(PAQUETE)]);
    const { rutas, resultado } = await fav.guardarFavorito({ ambito: 'portal', tipo: 'guia', paqueteId: PAQUETE, guiaId: 'g1' });
    expect(resultado).toBe('sin_acceso');
    expect(rutas).toEqual([]);
    expect(memoria.size).toBe(0);
  });

  it('un spec más grande que el tope no se guarda', async () => {
    api({
      [fav.rutaPaquete(PAQUETE)]: () => json(DETALLE),
      [fav.rutaSpecPodado(PAQUETE, 'c1')]: () => texto('x'.repeat(fav.TOPE_RECURSO_BYTES + 1)),
      [fav.rutaCambios(PAQUETE, 'c1')]: () => json({ elementos: [] }),
    });
    const { resultado } = await fav.guardarFavorito({ ambito: 'portal', tipo: 'contrato', paqueteId: PAQUETE, contratoId: 'c1' });
    expect(resultado).toBe('grande');
    expect(memoria.has(clave(fav.rutaSpecPodado(PAQUETE, 'c1')))).toBe(false);
    expect(memoria.has(clave(fav.rutaPaquete(PAQUETE)))).toBe(true);
  });

  it('un contrato interno guarda el contrato, sus versiones, el spec y las operaciones de la última versión', async () => {
    api({
      [fav.rutaContrato('k1')]: () => json({ id: 'k1', ultimaVersion: { version: '2.0.0' } }),
      [fav.rutaVersiones('k1')]: () => json([{ version: '2.0.0' }]),
      [fav.rutaSpec('k1', '2.0.0')]: () => texto('openapi: 3.1.0'),
      [fav.rutaOperaciones('k1', '2.0.0')]: () => json([]),
    });
    const { rutas } = await fav.guardarFavorito({ ambito: 'interno', contratoId: 'k1' });
    expect(rutas).toHaveLength(4);
    expect(memoria.get(clave(fav.rutaSpec('k1', '2.0.0')))).toMatchObject({ datos: 'openapi: 3.1.0', permiso: 'atlas.contratos.ver_internos' });
  });

  it('quitar un favorito borra lo suyo pero no lo que otro favorito usa', async () => {
    const guardar = (ruta: string) => memoria.set(clave(ruta), { datos: {}, guardadoEn: 0, venceEn: Date.now() + 1e9, permiso: 'atlas.portal.ver' });
    [fav.rutaPaquete(PAQUETE), fav.rutaSpecPodado(PAQUETE, 'c1'), fav.rutaCambios(PAQUETE, 'c1'), fav.rutaGuia(PAQUETE, 'g1')].forEach(guardar);
    const guia = { ambito: 'portal' as const, tipo: 'guia' as const, paqueteId: PAQUETE, guiaId: 'g1' };
    const contrato = { ambito: 'portal' as const, tipo: 'contrato' as const, paqueteId: PAQUETE, contratoId: 'c1' };
    expect(await fav.olvidarFavorito(contrato, [guia])).toBe(2);
    expect([...memoria.keys()].sort()).toEqual([clave(fav.rutaPaquete(PAQUETE)), clave(fav.rutaGuia(PAQUETE, 'g1'))].sort());
    expect(await fav.olvidarFavorito(guia, [])).toBe(2);
    expect(memoria.size).toBe(0);
  });

  it('en línea solo refresca lo ya guardado; un 403 lo borra; sin red responde con lo guardado', async () => {
    const recurso = fav.recurso(fav.rutaPaquete(PAQUETE), fav.PERMISO_PORTAL);
    expect(await conRespaldoGuardado(recurso, async () => ({ v: 1 }))).toEqual({ v: 1 });
    expect(memoria.size).toBe(0);

    memoria.set(recurso.clave, { datos: { v: 0 }, guardadoEn: 0, venceEn: Date.now() + 1e9, permiso: fav.PERMISO_PORTAL });
    await conRespaldoGuardado(recurso, async () => ({ v: 2 }));
    expect(memoria.get(recurso.clave)?.datos).toEqual({ v: 2 });
    expect(await conRespaldoGuardado(recurso, () => Promise.reject(new TypeError('Failed to fetch')))).toEqual({ v: 2 });

    const { ErrorApi } = await import('../../../app/api');
    await expect(conRespaldoGuardado(recurso, () => Promise.reject(new ErrorApi(403)))).rejects.toBeInstanceOf(ErrorApi);
    expect(memoria.has(recurso.clave)).toBe(false);
  });
});
