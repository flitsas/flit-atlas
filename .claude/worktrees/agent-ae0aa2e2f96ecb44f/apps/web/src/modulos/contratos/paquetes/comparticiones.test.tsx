import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Comparticion } from './apiComparticiones';
import { finDelDia } from './apiComparticiones';
import { Comparticiones } from './Comparticiones';

/** HU 4.3 (ATLAS-ADR-0043): «Compartido con» del paquete con una API falsa. */
function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const COMPARTICION: Comparticion = {
  id: 'k1',
  paqueteId: 'p1',
  paqueteNombre: 'Paquete Integradores Demo',
  paqueteEstado: 'publicado',
  organizacion: { id: 'o1', nombre: 'Organismo Demo', tipo: 'cliente', estado: 'activa' },
  revisionFijada: null,
  revisionQueVe: 2,
  puedeExportar: true,
  puedeSandbox: false,
  vigenteDesde: '2026-10-24T15:00:00Z',
  vigenteHasta: null,
  estado: 'vigente',
  otorgadoPor: { id: 'u1', nombre: 'Persona Demo Uno' },
  creadaEn: '2026-10-24T15:00:00Z',
  actualizadaEn: '2026-10-24T15:00:00Z',
  revocadaEn: null,
  puedeGestionar: true,
};

function apiFalsa(respuestaCompartir?: Response) {
  const llamadas: Array<{ metodo: string; ruta: string; cuerpo: unknown }> = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, opciones: RequestInit = {}) => {
      const metodo = (opciones.method ?? 'GET').toUpperCase();
      const cuerpo = typeof opciones.body === 'string' ? JSON.parse(opciones.body) : undefined;
      llamadas.push({ metodo, ruta, cuerpo });
      if (metodo === 'GET' && ruta === '/api/v1/contratos/paquetes/p1/comparticiones') return json({ puedeCompartir: true, elementos: [COMPARTICION] });
      if (metodo === 'GET' && ruta === '/api/v1/contratos/comparticiones/organizaciones')
        return json([
          { id: 'o2', nombre: 'Proveedor Demo', tipo: 'proveedor' },
          { id: 'o3', nombre: 'Organismo Demo Dos', tipo: 'cliente' },
        ]);
      if (metodo === 'POST' && ruta === '/api/v1/contratos/paquetes/p1/comparticiones')
        return respuestaCompartir ?? json({ ...COMPARTICION, id: 'k2', organizacion: { id: 'o3', nombre: 'Organismo Demo Dos', tipo: 'cliente', estado: 'activa' } }, 201);
      if (metodo === 'DELETE' && ruta === '/api/v1/contratos/paquetes/p1/comparticiones/k1') return json({ ...COMPARTICION, estado: 'revocada', revocadaEn: '2026-10-24T16:00:00Z' });
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

function montar() {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter>
        <Comparticiones paqueteId="p1" revisiones={[1, 2]} vigente={2} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('compartido con', () => {
  it('lista la organización con su revisión, vigencia, exportar y sandbox', async () => {
    apiFalsa();
    montar();

    const lista = await screen.findByRole('list', { name: 'Organizaciones con las que se comparte' });
    expect(within(lista).getByText('Organismo Demo')).toBeInTheDocument();
    expect(within(lista).getByText('Sigue la vigente (la 2)')).toBeInTheDocument();
    expect(within(lista).getByText(/sin vencimiento/)).toBeInTheDocument();
    expect(within(lista).getByText('Vigente')).toBeInTheDocument();
  });

  it('comparte con otra organización fijando una revisión, con sandbox y vencimiento al final del día en Bogotá', async () => {
    const llamadas = apiFalsa();
    montar();

    fireEvent.click(await screen.findByRole('button', { name: /Compartir/ }));
    const hoja = await screen.findByRole('dialog', { name: 'Compartir con una organización' });
    fireEvent.click(await within(hoja).findByRole('radio', { name: /Organismo Demo Dos/ }));
    fireEvent.click(within(hoja).getByRole('radio', { name: 'Fijar la 1' }));
    fireEvent.click(within(hoja).getByRole('checkbox', { name: /Puede usar el sandbox/ }));
    fireEvent.change(within(hoja).getByLabelText(/Compartido hasta/), { target: { value: '2027-01-31' } });
    fireEvent.click(within(hoja).getByRole('button', { name: 'Compartir' }));

    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST')).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')?.cuerpo).toEqual({
      organizacionId: 'o3',
      revisionFijada: 1,
      puedeExportar: true,
      puedeSandbox: true,
      vigenteHasta: '2027-01-31T23:59:59-05:00',
    });
    expect(await screen.findByText('Compartido con Organismo Demo Dos.')).toBeInTheDocument();
  });

  it('si el servidor pide reautenticación, lo dice y ofrece confirmar', async () => {
    apiFalsa(json({ codigo: 'reautenticacion_requerida', title: 'Vuelve a ingresar.' }, 401));
    montar();

    fireEvent.click(await screen.findByRole('button', { name: /Compartir/ }));
    const hoja = await screen.findByRole('dialog', { name: 'Compartir con una organización' });
    fireEvent.click(await within(hoja).findByRole('radio', { name: /Organismo Demo Dos/ }));
    fireEvent.click(within(hoja).getByRole('button', { name: 'Compartir' }));

    expect(await within(hoja).findByText(/confirma tu identidad antes de compartir/)).toBeInTheDocument();
    expect(within(hoja).getByRole('link', { name: 'Confirmar con Microsoft' })).toBeInTheDocument();
  });

  it('revocar pide confirmación y avisa', async () => {
    const llamadas = apiFalsa();
    montar();

    fireEvent.click(await screen.findByRole('button', { name: 'Revocar la compartición con Organismo Demo' }));
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Revocar la compartición' })).getByRole('button', { name: /Revocar/ }));

    expect(await screen.findByText('Revocada: Organismo Demo ya no ve el paquete.')).toBeInTheDocument();
    expect(llamadas.some((l) => l.metodo === 'DELETE' && l.ruta === '/api/v1/contratos/paquetes/p1/comparticiones/k1')).toBe(true);
  });

  it('la fecha de vencimiento es el final de ese día en Bogotá', () => {
    expect(finDelDia('2027-01-31')).toBe('2027-01-31T23:59:59-05:00');
  });
});
