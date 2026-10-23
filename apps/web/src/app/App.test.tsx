import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from './api';
import { App } from './App';

const yo = {
  sub: 's',
  personaId: 'p',
  nombre: 'Empleada Demo',
  tipoPrincipal: 'empleado',
  organizacionId: 'o',
  proveedor: 'simulado',
  roles: ['atlas.desarrollador'],
};

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

/** API de mentira: sesión según `conSesion`, banderas y bandeja vacía. */
function apiFalsa(opciones: { conSesion: boolean; banderas?: Record<string, boolean> }) {
  const estado = { conSesion: opciones.conSesion };
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  const fetch = vi.fn(async (ruta: string, init?: RequestInit) => {
    llamadas.push({ ruta, init });
    if (ruta === '/api/v1/plataforma/yo') return estado.conSesion ? json(yo) : new Response('', { status: 401 });
    if (ruta === '/api/v1/identidad/proveedores') return json({ microsoft: true, simulado: false });
    if (ruta === '/api/v1/identidad/salir') {
      estado.conSesion = false;
      return new Response(null, { status: 204 });
    }
    if (ruta === '/api/v1/plataforma/banderas') return json(opciones.banderas ?? {});
    if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
    return new Response('', { status: 404 });
  });
  vi.stubGlobal('fetch', fetch);
  return llamadas;
}

beforeEach(() => {
  window.history.pushState({}, '', '/');
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App', () => {
  it('sin sesión ofrece los proveedores configurados', async () => {
    apiFalsa({ conSesion: false });

    render(<App />);

    const enlace = await screen.findByRole('link', { name: 'Iniciar sesión con Microsoft' });
    expect(enlace).toHaveAttribute('href', '/api/v1/identidad/entrar/microsoft?volver=%2F');
    expect(screen.queryByRole('form', { name: /simulado/ })).not.toBeInTheDocument();
  });

  it('sin sesión en una ruta interna, el ingreso vuelve a ella', async () => {
    apiFalsa({ conSesion: false });
    window.history.pushState({}, '', '/soporte');

    render(<App />);

    expect(await screen.findByRole('link', { name: 'Iniciar sesión con Microsoft' })).toHaveAttribute(
      'href',
      '/api/v1/identidad/entrar/microsoft?volver=%2Fsoporte',
    );
  });

  it('con sesión muestra el shell: saludo, roles, navegación completa y los widgets de Inicio', async () => {
    apiFalsa({ conSesion: true });

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Tus roles' })).getByText('Desarrollo')).toBeInTheDocument();
    const lateral = screen.getByRole('navigation', { name: 'Principal' });
    for (const nombre of ['Inicio', 'Catálogo', 'Equipo', 'Soporte', 'Contratos', 'Documentación', 'Asistente']) {
      expect(within(lateral).getByRole('link', { name: new RegExp(nombre) })).toBeInTheDocument();
    }
    const inferior = screen.getByRole('navigation', { name: 'Barra inferior' });
    expect(within(inferior).getAllByRole('link').map((a) => a.textContent)).toEqual(['Inicio', 'Buscar', 'Soporte', 'Asistente']);
    expect(within(inferior).getByRole('button', { name: 'Más' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: /Soporte de este sábado/ })).toBeInTheDocument();
  });

  it('un módulo apagado por bandera desaparece y su ruta no existe', async () => {
    apiFalsa({ conSesion: true, banderas: { 'modulo.contratos': false } });
    window.history.pushState({}, '', '/contratos');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Esta página no existe' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Principal' })).queryByRole('link', { name: /Contratos/ })).toBeNull();
  });

  it('un módulo próximamente tiene su página digna', async () => {
    apiFalsa({ conSesion: true });
    window.history.pushState({}, '', '/soporte');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Soporte de los sábados' })).toBeInTheDocument();
    expect(screen.getByText(/Próximamente · fase F2/)).toBeInTheDocument();
  });

  it('cerrar sesión manda la cabecera antifalsificación y vuelve al ingreso sin guardar nada en el navegador', async () => {
    const llamadas = apiFalsa({ conSesion: true });

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Más' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar sesión' }));

    expect(await screen.findByRole('link', { name: 'Iniciar sesión con Microsoft' })).toBeInTheDocument();
    const salida = llamadas.find((l) => l.ruta === '/api/v1/identidad/salir');
    expect(new Headers(salida?.init?.headers).get(CABECERA_CSRF)).toBe('1');
    expect(salida?.init?.credentials).toBe('same-origin');
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it('muestra el motivo cuando Entra rechaza la cuenta', async () => {
    apiFalsa({ conSesion: false });
    window.history.pushState({}, '', '/?error=cuenta_no_permitida');

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Esa cuenta no puede entrar a Atlas');
  });
});
