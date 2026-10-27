import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  compartir,
  copiarEnlace,
  descargarComoArchivo,
  enlaceInterno,
  ErrorArchivo,
  puedeCompartir,
  puedeCompartirArchivo,
  tipoDeArchivo,
  TOPE_ARCHIVO_BYTES,
  urlCorreo,
  urlTeams,
} from './compartir';

/** HU 4.6: servicio de compartir con `navigator.share`/`canShare` simulados. */
const ORIGEN = 'https://atlas.ejemplo.com';

function simularHoja(opciones: { share?: (datos: ShareData) => Promise<void>; canShare?: ((datos: ShareData) => boolean) | null }) {
  const share = vi.fn(opciones.share ?? (async () => undefined));
  Object.defineProperty(navigator, 'share', { value: share, configurable: true, writable: true });
  if (opciones.canShare === null) Reflect.deleteProperty(navigator, 'canShare');
  else Object.defineProperty(navigator, 'canShare', { value: vi.fn(opciones.canShare ?? (() => true)), configurable: true, writable: true });
  return share;
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'share');
  Reflect.deleteProperty(navigator, 'canShare');
  Reflect.deleteProperty(navigator, 'clipboard');
  vi.unstubAllGlobals();
});

describe('enlace interno', () => {
  it('arma la URL absoluta de una ruta de Atlas', () => {
    expect(enlaceInterno('/contratos/c1?pestana=guias', ORIGEN)).toBe('https://atlas.ejemplo.com/contratos/c1?pestana=guias');
    expect(enlaceInterno('/portal/paquetes/p1', ORIGEN)).toBe('https://atlas.ejemplo.com/portal/paquetes/p1');
  });

  it('nunca comparte una URL externa ni firmada', () => {
    for (const ruta of ['https://s3.ejemplo.com/atlas/x?X-Amz-Signature=abc', '//s3.ejemplo.com/x', '/\\s3.ejemplo.com', 'contratos/c1', 'javascript:alert(1)']) {
      expect(() => enlaceInterno(ruta, ORIGEN), ruta).toThrow(/rutas internas/);
    }
  });
});

describe('correo y Teams', () => {
  it('el correo lleva asunto y el enlace, sin nada más', () => {
    const url = urlCorreo('Contrato Demo · Flit Atlas', 'https://atlas.ejemplo.com/contratos/c1');
    expect(url.startsWith('mailto:?subject=')).toBe(true);
    const parametros = new URLSearchParams(url.slice('mailto:?'.length));
    expect(parametros.get('subject')).toBe('Contrato Demo · Flit Atlas');
    expect(parametros.get('body')).toBe('Te comparto este enlace de Flit Atlas (necesitas iniciar sesión para abrirlo):\nhttps://atlas.ejemplo.com/contratos/c1');
    // Espacios como %20 (no «+», que algunos clientes de correo muestran tal cual).
    expect(url).not.toContain('+');
  });

  it('Teams recibe la URL interna sin vista previa', () => {
    const url = new URL(urlTeams('https://atlas.ejemplo.com/contratos/paquetes/p1', 'Paquete Demo · Flit Atlas'));
    expect(url.origin + url.pathname).toBe('https://teams.microsoft.com/share');
    expect(url.searchParams.get('href')).toBe('https://atlas.ejemplo.com/contratos/paquetes/p1');
    expect(url.searchParams.get('msgText')).toBe('Paquete Demo · Flit Atlas');
    expect(url.searchParams.get('preview')).toBe('false');
  });
});

describe('hoja nativa', () => {
  it('sin navigator.share no hay hoja', async () => {
    expect(puedeCompartir({ url: ORIGEN })).toBe(false);
    expect(await compartir({ url: ORIGEN })).toBe('no_soportado');
  });

  it('comparte un enlace y devuelve «compartido»', async () => {
    const share = simularHoja({});
    expect(await compartir({ title: 'Contrato Demo', url: `${ORIGEN}/contratos/c1` })).toBe('compartido');
    expect(share).toHaveBeenCalledWith({ title: 'Contrato Demo', url: `${ORIGEN}/contratos/c1` });
  });

  it('cancelar (AbortError) no es un error', async () => {
    simularHoja({ share: async () => Promise.reject(new DOMException('Cancelado', 'AbortError')) });
    expect(await compartir({ url: ORIGEN })).toBe('cancelado');
  });

  it('sin el gesto (NotAllowedError) pide volver a tocar; otro fallo es error', async () => {
    simularHoja({ share: async () => Promise.reject(new DOMException('Sin gesto', 'NotAllowedError')) });
    expect(await compartir({ url: ORIGEN })).toBe('no_permitido');
    simularHoja({ share: async () => Promise.reject(new TypeError('raro')) });
    expect(await compartir({ url: ORIGEN })).toBe('error');
  });

  it('archivos solo con canShare({ files }) y según el tipo', () => {
    simularHoja({ canShare: (d) => !d.files || d.files.every((f) => f.type === 'application/pdf') });
    expect(puedeCompartirArchivo('paquete.pdf')).toBe(true);
    expect(puedeCompartirArchivo('paquete.zip')).toBe(false);
  });

  it('sin canShare (Safari viejo) comparte enlaces pero no archivos', () => {
    simularHoja({ canShare: null });
    expect(puedeCompartir({ url: ORIGEN })).toBe(true);
    expect(puedeCompartirArchivo('paquete.pdf')).toBe(false);
  });

  it('los tipos salen de la extensión', () => {
    expect(tipoDeArchivo('Paquete.PDF')).toBe('application/pdf');
    expect(tipoDeArchivo('paquete.xlsx')).toBe('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
    expect(tipoDeArchivo('sin-extension')).toBe('application/octet-stream');
  });
});

describe('portapapeles', () => {
  it('copia, y sin portapapeles devuelve false', async () => {
    expect(await copiarEnlace(ORIGEN)).toBe(false);
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    expect(await copiarEnlace(ORIGEN)).toBe(true);
    expect(writeText).toHaveBeenCalledWith(ORIGEN);
  });
});

describe('descargar como archivo', () => {
  it('baja la URL firmada sin credenciales ni referer y arma el File con su tipo', async () => {
    const fetch = vi.fn(async () => new Response('%PDF-1.7', { status: 200 }));
    vi.stubGlobal('fetch', fetch);
    const archivo = await descargarComoArchivo('https://s3.ejemplo.com/atlas/firmada', 'paquete.pdf');
    expect(archivo.name).toBe('paquete.pdf');
    expect(archivo.type).toBe('application/pdf');
    expect(archivo.size).toBe(8);
    expect(fetch).toHaveBeenCalledWith('https://s3.ejemplo.com/atlas/firmada', expect.objectContaining({ credentials: 'omit', mode: 'cors', referrerPolicy: 'no-referrer' }));
  });

  it('CSP, CORS o red: ErrorArchivo «red»; 403 (venció): «estado»; muy grande: «grande»', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))));
    await expect(descargarComoArchivo('https://s3.ejemplo.com/x', 'a.pdf')).rejects.toEqual(new ErrorArchivo('red'));
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 403 })));
    await expect(descargarComoArchivo('https://s3.ejemplo.com/x', 'a.pdf')).rejects.toMatchObject({ motivo: 'estado' });
    vi.stubGlobal('fetch', vi.fn(async () => new Response('x', { status: 200, headers: { 'Content-Length': String(TOPE_ARCHIVO_BYTES + 1) } })));
    await expect(descargarComoArchivo('https://s3.ejemplo.com/x', 'a.pdf')).rejects.toMatchObject({ motivo: 'grande' });
  });
});
