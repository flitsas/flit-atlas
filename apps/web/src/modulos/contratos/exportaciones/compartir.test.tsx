import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Exportacion } from './api';
import { CompartirExportacion } from './CompartirExportacion';

/**
 * HU 4.6: compartir el resultado de una exportación. El archivo se baja de la URL firmada con `fetch` y se comparte como
 * `File`; el enlace es el interno del paquete. `navigator.share` y `canShare` simulados.
 */
vi.mock('../../../app/sesion', () => ({ useSesionOpcional: () => ({ yo: { tipoPrincipal: 'empleado' } }) }));

const LISTA: Exportacion = {
  id: 'e1',
  estado: 'lista',
  origen: { tipo: 'paquete', paqueteId: 'p1', revision: 2, versionId: null },
  nombre: 'Paquete Integradores Demo · revisión 2',
  formatos: ['pdf', 'excel', 'zip'],
  solicitadaEn: '2026-10-24T15:00:00Z',
  iniciadaEn: null,
  listaEn: '2026-10-24T15:01:00Z',
  venceEn: '2026-10-31T15:01:00Z',
  hashContenido: 'a'.repeat(64),
  archivos: [
    { ruta: 'paquete/paquete.zip', nombre: 'paquete.zip', formato: 'zip', bytes: 2_500_000 },
    { ruta: 'paquete/paquete.xlsx', nombre: 'paquete.xlsx', formato: 'excel', bytes: 90_000 },
    { ruta: 'paquete/paquete.pdf', nombre: 'paquete.pdf', formato: 'pdf', bytes: 300_000 },
  ],
  error: null,
};

/** La API (URL firmada) y el S3 falsos. */
function apiFalsa(s3: () => Promise<Response> = async () => new Response('%PDF-1.7', { status: 200 })) {
  const llamadas: Array<{ url: string; init: RequestInit }> = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init: RequestInit = {}) => {
      llamadas.push({ url, init });
      const u = new URL(url, 'https://atlas.ejemplo.com');
      if (u.pathname === '/api/v1/contratos/exportaciones/e1/descarga') {
        const archivo = u.searchParams.get('archivo') ?? 'zip';
        return new Response(JSON.stringify({ url: `https://s3.ejemplo.com/atlas/firmada?archivo=${archivo}`, nombre: 'x', bytes: 8, venceEn: '2026-10-24T15:16:00Z' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (u.host === 's3.ejemplo.com') return s3();
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

function simularHoja(opciones: { archivos: boolean; share?: (d: ShareData) => Promise<void> }) {
  const share = vi.fn(opciones.share ?? (async () => undefined));
  Object.defineProperty(navigator, 'share', { value: share, configurable: true, writable: true });
  Object.defineProperty(navigator, 'canShare', {
    // Como Chrome en Android: PDF y Excel sí, ZIP no.
    value: (d: ShareData) => !d.files || (opciones.archivos && d.files.every((f) => f.type !== 'application/zip')),
    configurable: true,
    writable: true,
  });
  return share;
}

function montar() {
  return render(<CompartirExportacion base="/api/v1/contratos" exportacion={LISTA} enlace={{ ruta: '/contratos/paquetes/p1', que: 'este paquete', etiqueta: 'Compartir enlace al paquete' }} />);
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'share');
  Reflect.deleteProperty(navigator, 'canShare');
  vi.unstubAllGlobals();
});

describe('CompartirExportacion', () => {
  it('con soporte de archivos: prepara el PDF (primero) desde la URL firmada y lo comparte como File', async () => {
    const share = simularHoja({ archivos: true });
    const llamadas = apiFalsa();
    montar();

    const selector = screen.getByRole('combobox', { name: 'Archivo para compartir' });
    // El ZIP no se ofrece (el dispositivo no lo comparte); el PDF va primero.
    expect(within(selector).getAllByRole('option').map((o) => o.textContent)).toEqual(['paquete.pdf', 'paquete.xlsx']);

    fireEvent.click(screen.getByRole('button', { name: 'Preparar paquete.pdf para compartir' }));
    const boton = await screen.findByRole('button', { name: 'Compartir paquete.pdf' });
    const s3 = llamadas.find((l) => l.url.startsWith('https://s3.ejemplo.com/'))!;
    expect(s3.url).toBe('https://s3.ejemplo.com/atlas/firmada?archivo=paquete/paquete.pdf');
    expect(s3.init).toMatchObject({ credentials: 'omit', referrerPolicy: 'no-referrer' });

    // Segundo toque: la hoja nativa recibe el archivo; nada vuelve a la API.
    const antes = llamadas.length;
    fireEvent.click(boton);
    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    const datos = share.mock.calls[0]![0];
    expect(datos.files).toHaveLength(1);
    expect(datos.files![0]!.name).toBe('paquete.pdf');
    expect(datos.files![0]!.type).toBe('application/pdf');
    expect(datos.url).toBeUndefined();
    expect(llamadas).toHaveLength(antes);
  });

  it('si la persona cancela la hoja no muestra error y puede volver a compartir', async () => {
    simularHoja({ archivos: true, share: async () => Promise.reject(new DOMException('Cancelado', 'AbortError')) });
    apiFalsa();
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Preparar paquete.pdf para compartir' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Compartir paquete.pdf' }));
    await new Promise((r) => setTimeout(r, 0));
    expect(screen.getByRole('button', { name: 'Compartir paquete.pdf' })).toBeInTheDocument();
    expect(screen.queryByText(/no pudo/i)).not.toBeInTheDocument();
  });

  it('si el S3 no deja bajarlo (CSP o CORS) ofrece descargar o compartir el enlace', async () => {
    simularHoja({ archivos: true });
    apiFalsa(async () => Promise.reject(new TypeError('Failed to fetch')));
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Preparar paquete.pdf para compartir' }));
    expect(await screen.findByText(/No pudimos preparar el archivo en este dispositivo/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Compartir enlace al paquete' })).toBeInTheDocument();
  });

  it('sin soporte de archivos solo ofrece el enlace interno del paquete', async () => {
    const share = simularHoja({ archivos: false });
    apiFalsa();
    montar();
    expect(screen.queryByRole('button', { name: /Preparar/ })).not.toBeInTheDocument();
    expect(screen.getByText(/no se comparten archivos/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir enlace al paquete' }));
    await waitFor(() => expect(share).toHaveBeenCalledWith(expect.objectContaining({ url: `${window.location.origin}/contratos/paquetes/p1` })));
  });
});
