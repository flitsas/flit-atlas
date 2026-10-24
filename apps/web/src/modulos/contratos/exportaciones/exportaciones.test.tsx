import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { PaqueteDetalle } from '../paquetes/api';
import Paquetes from '../paquetes/Paquetes';
import { type BaseExportaciones, type Exportacion, intervaloSondeo } from './api';
import { abrirEnLaMismaPestana } from './descarga';
import { ExportarPaquete } from './ExportarPaquete';
import { estadoAccion } from '../portal/acciones';
import { ExportacionDelPortal } from '../portal/ExportacionDelPortal';

/** HU 4.5 (ATLAS-ADR-0042): botón Exportar, estado en vivo y descarga en la misma pestaña, con una API falsa. */
const sesion = vi.hoisted(() => ({ permisos: ['atlas.contratos.ver_internos', 'atlas.exportaciones.solicitar'] as string[] }));

vi.mock('../../../app/sesion', () => ({
  useSesionOpcional: () => ({
    yo: { tipoPrincipal: 'empleado', proveedor: 'microsoft' },
    acceso: { permisos: sesion.permisos, banderas: {} },
    cerrarSesion: vi.fn(),
  }),
}));

vi.mock('./descarga', () => ({ abrirEnLaMismaPestana: vi.fn() }));

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const exportacion = (cambios: Partial<Exportacion> = {}): Exportacion => ({
  id: 'e1',
  estado: 'pendiente',
  origen: { tipo: 'paquete', paqueteId: 'p1', revision: 2, versionId: null },
  nombre: 'Paquete Integradores Demo · revisión 2',
  formatos: ['openapi', 'postman', 'html', 'pdf', 'excel', 'zip'],
  solicitadaEn: '2026-10-24T15:00:00Z',
  iniciadaEn: null,
  listaEn: null,
  venceEn: null,
  hashContenido: null,
  archivos: [],
  error: null,
  ...cambios,
});

const LISTA = exportacion({
  estado: 'lista',
  listaEn: '2026-10-24T15:01:00Z',
  venceEn: '2026-10-31T15:01:00Z',
  hashContenido: 'a'.repeat(64),
  archivos: [
    { ruta: 'paquete/paquete.zip', nombre: 'paquete.zip', formato: 'zip', bytes: 2_500_000 },
    { ruta: 'paquete/paquete.pdf', nombre: 'paquete.pdf', formato: 'pdf', bytes: 300_000 },
  ],
});

/** API falsa: el POST deja la exportación pendiente y cada GET avanza un paso de `estados`. */
function apiFalsa(estados: Exportacion[], opciones: { post?: Response } = {}) {
  const llamadas: Array<{ metodo: string; ruta: string; cuerpo: unknown }> = [];
  let paso = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init: RequestInit = {}) => {
      const metodo = (init.method ?? 'GET').toUpperCase();
      llamadas.push({ metodo, ruta, cuerpo: typeof init.body === 'string' ? JSON.parse(init.body) : undefined });
      const url = new URL(ruta, 'https://atlas.ejemplo.com');
      if (metodo === 'POST' && url.pathname.endsWith('/exportaciones')) return opciones.post ?? json(exportacion(), 202);
      if (metodo === 'GET' && url.pathname.endsWith('/exportaciones/e1/descarga')) {
        return json({ url: `https://s3.ejemplo.com/atlas/firmada?archivo=${url.searchParams.get('archivo') ?? 'zip'}`, nombre: 'paquete.zip', bytes: 10, venceEn: '2026-10-24T15:16:00Z' });
      }
      if (metodo === 'GET' && url.pathname.endsWith('/exportaciones/e1')) {
        const actual = estados[Math.min(paso, estados.length - 1)]!;
        paso++;
        return json(actual);
      }
      if (metodo === 'GET' && url.pathname === '/api/v1/contratos/paquetes/p1') return json(DETALLE);
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

const DETALLE: PaqueteDetalle = {
  paquete: {
    id: 'p1',
    nombre: 'Paquete Integradores Demo',
    descripcion: null,
    estado: 'publicado',
    revisionActual: 2,
    revisionPublicada: 2,
    cambiosSinPublicar: false,
    publicadaEn: null,
    publicadaPor: null,
    contratos: [],
    operaciones: 0,
    actualizadoEn: '2026-10-24T15:00:00Z',
    puedeGestionar: false,
  },
  revision: {
    numero: 2,
    nombre: 'Paquete Integradores Demo',
    descripcion: null,
    definicion: [],
    nota: 'Nota',
    autor: { id: 'p', nombre: 'Persona Demo' },
    guardadaEn: '2026-10-24T15:00:00Z',
    huella: 'b'.repeat(64),
    vigente: true,
  },
  revisiones: [],
};

function envolver(hijo: React.ReactNode, ruta = '/') {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[ruta]}>{hijo}</MemoryRouter>
    </QueryClientProvider>,
  );
}

function montarBoton(base: BaseExportaciones = '/api/v1/contratos', inicial: string | null = null) {
  return envolver(<ExportarPaquete base={base} origen={{ paqueteId: 'p1' }} exportacionInicial={inicial} />);
}

beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }));

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.mocked(abrirEnLaMismaPestana).mockClear();
  sesion.permisos = ['atlas.contratos.ver_internos', 'atlas.exportaciones.solicitar'];
});

describe('sondeo de una exportación', () => {
  it('cada 2 s, cada 5 s pasados 30 s, y nada en un estado final', () => {
    expect(intervaloSondeo(undefined, 0)).toBe(2_000);
    expect(intervaloSondeo('procesando', 10_000)).toBe(2_000);
    expect(intervaloSondeo('pendiente', 31_000)).toBe(5_000);
    expect(intervaloSondeo('lista', 0)).toBe(false);
    expect(intervaloSondeo('fallida', 0)).toBe(false);
    expect(intervaloSondeo('vencida', 0)).toBe(false);
  });
});

describe('ExportarPaquete', () => {
  it('elige formatos (ZIP siempre), solicita, sigue el estado en vivo y descarga en la misma pestaña', async () => {
    const llamadas = apiFalsa([exportacion(), exportacion({ estado: 'procesando' }), LISTA]);
    montarBoton();

    fireEvent.click(screen.getByRole('button', { name: /Exportar/ }));
    const hoja = screen.getByRole('dialog', { name: 'Exportar' });
    const zip = within(hoja).getByRole('checkbox', { name: /ZIP con todo/ });
    expect(zip).toBeChecked();
    expect(zip).toBeDisabled();
    fireEvent.click(within(hoja).getByRole('checkbox', { name: 'Word' }));
    fireEvent.click(within(hoja).getByRole('button', { name: /Solicitar exportación/ }));

    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST')).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')).toEqual({
      metodo: 'POST',
      ruta: '/api/v1/contratos/exportaciones',
      cuerpo: { paqueteId: 'p1', formatos: ['openapi', 'postman', 'html', 'pdf', 'excel', 'zip'] },
    });
    expect(await screen.findByText(/En cola/)).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(2_100));
    expect(await screen.findByText(/Generando los archivos/)).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(2_100));
    expect(await screen.findByText('Tu exportación está lista')).toBeInTheDocument();

    // En un estado final ya no se consulta más.
    const consultas = llamadas.filter((l) => l.ruta === '/api/v1/contratos/exportaciones/e1').length;
    await act(() => vi.advanceTimersByTimeAsync(6_000));
    expect(llamadas.filter((l) => l.ruta === '/api/v1/contratos/exportaciones/e1')).toHaveLength(consultas);

    fireEvent.click(screen.getByRole('button', { name: /Descargar ZIP \(2,4 MB\)/ }));
    await waitFor(() => expect(abrirEnLaMismaPestana).toHaveBeenCalledWith('https://s3.ejemplo.com/atlas/firmada?archivo=zip'));
    fireEvent.click(screen.getByRole('button', { name: /paquete\.pdf/ }));
    await waitFor(() => expect(abrirEnLaMismaPestana).toHaveBeenLastCalledWith('https://s3.ejemplo.com/atlas/firmada?archivo=paquete/paquete.pdf'));
    expect(llamadas.some((l) => l.ruta === '/api/v1/contratos/exportaciones/e1/descarga?archivo=paquete%2Fpaquete.pdf')).toBe(true);
  });

  it('en el portal manda solo el paquete y usa la base del portal', async () => {
    const llamadas = apiFalsa([LISTA]);
    montarBoton('/api/v1/portal');
    fireEvent.click(screen.getByRole('button', { name: /Exportar/ }));
    fireEvent.click(screen.getByRole('button', { name: /Solicitar exportación/ }));
    expect(await screen.findByText('Tu exportación está lista')).toBeInTheDocument();
    expect(llamadas[0]).toMatchObject({ metodo: 'POST', ruta: '/api/v1/portal/exportaciones', cuerpo: { paqueteId: 'p1' } });
    expect(llamadas.some((l) => l.ruta === '/api/v1/portal/exportaciones/e1')).toBe(true);
  });

  it('si falla muestra el motivo, y si venció pide solicitarla de nuevo', async () => {
    apiFalsa([exportacion({ estado: 'fallida', error: { codigo: 'spec_invalido', mensaje: 'Un spec del paquete no se pudo leer.' } })]);
    const { unmount } = montarBoton('/api/v1/contratos', 'e1');
    expect(await screen.findByText('La exportación falló.')).toBeInTheDocument();
    expect(screen.getByText('Un spec del paquete no se pudo leer.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    unmount();

    apiFalsa([exportacion({ estado: 'vencida' })]);
    montarBoton('/api/v1/contratos', 'e1');
    expect(await screen.findByText('La exportación venció; solicítala de nuevo.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva exportación' }));
    expect(screen.getByRole('button', { name: /Solicitar exportación/ })).toBeInTheDocument();
  });

  it('con reautenticación requerida ofrece confirmar la identidad y no se queda esperando', async () => {
    apiFalsa([], {
      post: new Response(JSON.stringify({ status: 401, title: 'Vuelve a ingresar.', codigo: 'reautenticacion_requerida' }), {
        status: 401,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    });
    montarBoton();
    fireEvent.click(screen.getByRole('button', { name: /Exportar/ }));
    fireEvent.click(screen.getByRole('button', { name: /Solicitar exportación/ }));
    expect(await screen.findByText(/confirma tu identidad antes de exportar/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Confirmar con Microsoft' }).getAttribute('href')).toContain('reautenticar=true');
  });
});

describe('Exportar en el paquete', () => {
  function montarPaquetes(ruta: string) {
    return envolver(
      <Routes>
        <Route path="/contratos/paquetes/*" element={<Paquetes />} />
      </Routes>,
      ruta,
    );
  }

  it('el enlace de la notificación abre el paquete con el estado de la exportación', async () => {
    apiFalsa([LISTA]);
    montarPaquetes('/contratos/paquetes?paquete=p1&exportacion=e1');
    expect(await screen.findByRole('heading', { level: 1, name: 'Paquete Integradores Demo' })).toBeInTheDocument();
    expect(await screen.findByText('Tu exportación está lista')).toBeInTheDocument();
  });

  it('sin atlas.exportaciones.solicitar no aparece el botón', async () => {
    sesion.permisos = ['atlas.contratos.ver_internos'];
    apiFalsa([]);
    montarPaquetes('/contratos/paquetes/p1');
    expect(await screen.findByRole('heading', { level: 1, name: 'Paquete Integradores Demo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Exportar/ })).not.toBeInTheDocument();
  });

  it('con el permiso aparece el botón', async () => {
    apiFalsa([]);
    montarPaquetes('/contratos/paquetes/p1');
    expect(await screen.findByRole('button', { name: /Exportar/ })).toBeInTheDocument();
  });
});

describe('exportar desde el portal', () => {
  it('el botón «Exportar» del paquete abre la hoja con la base del portal y solicita ahí', async () => {
    const llamadas = apiFalsa([exportacion()]);
    envolver(<ExportacionDelPortal />, '/portal/paquetes/p1');
    const accion = estadoAccion('exportar', true);
    expect(accion.habilitada).toBe(true);
    act(() => void accion.ejecutar?.('p1'));
    fireEvent.click(await screen.findByRole('button', { name: /Solicitar exportación/ }));
    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta === '/api/v1/portal/exportaciones')).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')?.cuerpo).toMatchObject({ paqueteId: 'p1' });
  });

  it('el enlace del aviso del portal abre el estado de esa exportación', async () => {
    apiFalsa([LISTA]);
    envolver(<ExportacionDelPortal />, '/portal/paquetes/0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d52?exportacion=e1');
    expect(await screen.findByText('Tu exportación está lista')).toBeInTheDocument();
  });
});
