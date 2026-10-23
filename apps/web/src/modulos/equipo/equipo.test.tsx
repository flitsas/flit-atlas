import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from '../../app/api';
import { App } from '../../app/App';
import type { PersonaDirectorio, VistaSincronizacion } from './api';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const ANA: PersonaDirectorio = {
  id: 'a1',
  nombre: 'Ana Demo Pérez',
  correo: 'ana.demo@ejemplo.com',
  cargo: 'Desarrolladora',
  area: 'Tecnología',
  estado: 'activa',
  foto: '/api/v1/plataforma/personas/a1/foto?v=0123456789abcdef',
  telefono: null,
  sincronizadaEn: '2026-10-23T10:00:00Z',
};

const BETO: PersonaDirectorio = { ...ANA, id: 'b2', nombre: 'Beto Demo', correo: 'beto.demo@ejemplo.com', cargo: null, foto: null };

function apiFalsa(opciones: { permisos: string[]; veContacto?: boolean; foto?: string | null }) {
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  let corridas = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      llamadas.push({ ruta, init });
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: 'p',
          nombre: 'Empleada Demo',
          tipoPrincipal: 'empleado',
          organizacionId: 'o',
          proveedor: 'entra',
          roles: ['atlas.lector'],
          permisos: opciones.permisos,
          foto: opciones.foto ?? null,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta === '/api/v1/plataforma/directorio/sincronizacion' && init?.method === 'POST') {
        corridas++;
        return new Response(null, { status: 202 });
      }
      if (ruta === '/api/v1/plataforma/directorio/sincronizacion') {
        const vista: VistaSincronizacion = {
          disponible: true,
          programada: true,
          origen: 'grupo',
          hora: '05:00',
          proxima: '2026-10-24T10:00:00Z',
          enCurso: false,
          ultima:
            corridas === 0
              ? null
              : {
                  id: `c${corridas}`,
                  disparo: 'manual',
                  origen: 'grupo',
                  resultado: 'exitosa',
                  inicio: '2026-10-23T15:00:00Z',
                  fin: '2026-10-23T15:00:05Z',
                  creadas: 1,
                  actualizadas: 2,
                  deshabilitadas: 0,
                  reactivadas: 0,
                  sinCambios: 3,
                  omitidas: 0,
                  retenidas: 0,
                  error: null,
                },
        };
        return json(vista);
      }
      if (ruta.startsWith('/api/v1/plataforma/directorio')) {
        const q = new URLSearchParams(ruta.split('?')[1]).get('q') ?? '';
        const personas = [ANA, BETO]
          .filter((p) => !q || p.nombre.toLowerCase().includes(q.toLowerCase()))
          .map((p) => ({ ...p, telefono: opciones.veContacto && p.id === 'a1' ? '+57 300 000 0001' : null }));
        return json({ personas, hayMas: false, veContacto: opciones.veContacto ?? false });
      }
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

beforeEach(() => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});

afterEach(() => vi.unstubAllGlobals());

describe('equipo: directorio interno (HU 2.2)', () => {
  it('lista a las personas con foto o iniciales, cargo y área, sin teléfono sin atlas.equipo.ver_contacto', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver'] });
    window.history.pushState({}, '', '/equipo');

    render(<App />);

    const lista = await screen.findByRole('list', { name: 'Personas del equipo' });
    const ana = within(lista).getByText('Ana Demo Pérez').closest('li')!;
    expect(ana).toHaveTextContent('Desarrolladora · Tecnología');
    expect(ana.querySelector('img')).toHaveAttribute('src', ANA.foto);
    const beto = within(lista).getByText('Beto Demo').closest('li')!;
    expect(beto.querySelector('img')).toBeNull();
    expect(beto).toHaveTextContent('BD');
    expect(within(lista).queryByRole('link', { name: /\+57/ })).toBeNull();
    // Sin atlas.identidad.administrar no aparece la sincronización.
    expect(screen.queryByRole('button', { name: 'Sincronizar ahora' })).toBeNull();
  });

  it('con atlas.equipo.ver_contacto muestra el teléfono como enlace tel:', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver', 'atlas.equipo.ver_contacto'], veContacto: true });
    window.history.pushState({}, '', '/equipo');

    render(<App />);

    const telefono = await screen.findByRole('link', { name: '+57 300 000 0001' });
    expect(telefono).toHaveAttribute('href', 'tel:+573000000001');
  });

  it('busca con retraso y no consulta con una sola letra', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.equipo.ver'] });
    window.history.pushState({}, '', '/equipo');

    render(<App />);
    const campo = await screen.findByRole('searchbox', { name: /Nombre, correo, cargo o área/ });
    fireEvent.change(campo, { target: { value: 'b' } });
    expect(await screen.findByText('Escribe al menos 2 letras.')).toBeInTheDocument();
    fireEvent.change(campo, { target: { value: 'beto' } });

    expect(await screen.findByText('Beto Demo')).toBeInTheDocument();
    await vi.waitFor(() => expect(screen.queryByText('Ana Demo Pérez')).toBeNull());
    const consultas = llamadas.map((l) => l.ruta).filter((r) => r.startsWith('/api/v1/plataforma/directorio?'));
    expect(consultas).toEqual(['/api/v1/plataforma/directorio?q=beto']);
  });

  it('el superadmin pide «Sincronizar ahora» y ve el resultado de la corrida', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.equipo.ver', 'atlas.identidad.administrar'] });
    window.history.pushState({}, '', '/equipo');

    render(<App />);
    const tarjeta = await screen.findByRole('region', { name: 'Sincronización con Microsoft 365' });
    expect(await within(tarjeta).findByText('Todavía no ha corrido.')).toBeInTheDocument();
    expect(within(tarjeta).getByText(/cada día a las 05:00, hora de Bogotá/)).toBeInTheDocument();

    fireEvent.click(within(tarjeta).getByRole('button', { name: 'Sincronizar ahora' }));

    expect(await within(tarjeta).findByText(/1 nuevas · 2 actualizadas · 0 deshabilitadas · 0 reactivadas/, {}, { timeout: 4000 })).toBeInTheDocument();
    const envio = llamadas.find((l) => l.init?.method === 'POST');
    expect(new Headers(envio?.init?.headers).get(CABECERA_CSRF)).toBe('1');
  });

  it('el avatar del shell usa la foto de /yo', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver'], foto: '/api/v1/plataforma/personas/p/foto?v=fedcba9876543210' });
    window.history.pushState({}, '', '/equipo');

    const { container } = render(<App />);
    await screen.findByRole('list', { name: 'Personas del equipo' });

    expect(container.querySelector('header img[src="/api/v1/plataforma/personas/p/foto?v=fedcba9876543210"]')).not.toBeNull();
  });
});
