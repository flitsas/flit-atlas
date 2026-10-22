import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

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

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App', () => {
  it('sin sesión ofrece los proveedores configurados', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (ruta: string) =>
        ruta === '/api/v1/plataforma/yo' ? new Response('', { status: 401 }) : json({ microsoft: true, simulado: false }),
      ),
    );

    render(<App />);

    const enlace = await screen.findByRole('link', { name: 'Iniciar sesión con Microsoft' });
    expect(enlace).toHaveAttribute('href', '/api/v1/identidad/entrar/microsoft?volver=%2F');
    expect(screen.queryByRole('form', { name: /simulado/ })).not.toBeInTheDocument();
  });

  it('con sesión muestra a la persona y sus roles', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(yo)));

    render(<App />);

    expect(await screen.findByText('Hola, Empleada Demo.')).toBeInTheDocument();
    expect(screen.getByText('Roles: atlas.desarrollador')).toBeInTheDocument();
  });

  it('cerrar sesión manda la cabecera antifalsificación y no guarda nada en el navegador', async () => {
    const llamadas: { ruta: string; init?: RequestInit }[] = [];
    let conSesion = true;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (ruta: string, init?: RequestInit) => {
        llamadas.push({ ruta, init });
        if (ruta === '/api/v1/identidad/salir') {
          conSesion = false;
          return new Response(null, { status: 204 });
        }
        if (ruta === '/api/v1/plataforma/yo') return conSesion ? json(yo) : new Response('', { status: 401 });
        return json({ microsoft: true, simulado: false });
      }),
    );

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar sesión' }));

    expect(await screen.findByRole('link', { name: 'Iniciar sesión con Microsoft' })).toBeInTheDocument();
    const salida = llamadas.find((l) => l.ruta === '/api/v1/identidad/salir');
    expect(new Headers(salida?.init?.headers).get(CABECERA_CSRF)).toBe('1');
    expect(salida?.init?.credentials).toBe('same-origin');
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it('muestra el motivo cuando Entra rechaza la cuenta', async () => {
    window.history.pushState({}, '', '/?error=cuenta_no_permitida');
    vi.stubGlobal(
      'fetch',
      vi.fn(async (ruta: string) =>
        ruta === '/api/v1/plataforma/yo' ? new Response('', { status: 401 }) : json({ microsoft: true, simulado: false }),
      ),
    );

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Esa cuenta no puede entrar a Atlas');
    window.history.pushState({}, '', '/');
  });
});
