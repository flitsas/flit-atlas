import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from '../App';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const SEGURIDAD = {
  contrasena: true,
  totp: true,
  passkeys: [{ nombre: null, creadaEn: '2026-10-28T15:00:00Z' }],
  codigosRecuperacionRestantes: 7,
};

/** atlas-api falsa: `/yo` de una cuenta local (o de Microsoft) y los endpoints de la seguridad de la cuenta. */
function apiFalsa({ proveedor = 'local', cambio }: { proveedor?: string; cambio?: (ruta: string) => Response } = {}) {
  const llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      const metodo = init?.method ?? 'GET';
      llamadas.push({ ruta, metodo, cuerpo: init?.body ? JSON.parse(String(init.body)) : undefined });
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: 'p',
          nombre: 'Externa Demo',
          tipoPrincipal: proveedor === 'local' ? 'externo' : 'empleado',
          organizacionId: 'o',
          proveedor,
          roles: ['ext.integrador'],
          permisos: [],
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta === '/api/v1/identidad/cuenta/seguridad') return json(SEGURIDAD);
      if (metodo === 'POST' && cambio) return cambio(ruta);
      if (ruta === '/api/v1/identidad/cuenta/contrasena') return new Response(null, { status: 204 });
      if (ruta === '/api/v1/identidad/cuenta/codigos-recuperacion')
        return json({ codigosRecuperacion: Array.from({ length: 10 }, (_, i) => `ABCDE-0000${i}`) });
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.pushState({}, '', '/');
});

describe('seguridad de la cuenta (F4-SEC-31)', () => {
  it('muestra los factores y cambia la contraseña con la actual', async () => {
    const llamadas = apiFalsa();
    window.history.pushState({}, '', '/ajustes/seguridad');
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Seguridad de la cuenta' })).toBeInTheDocument();
    expect(await screen.findByText('Passkeys (1)')).toBeInTheDocument();
    expect(screen.getByText(/creada el 28\/10\/2026/)).toBeInTheDocument();

    const formulario = screen.getByRole('form', { name: 'Cambiar la contraseña' });
    fireEvent.change(within(formulario).getByLabelText('Contraseña actual'), { target: { value: 'Rio Magdalena al amanecer 2026' } });
    fireEvent.change(within(formulario).getByLabelText(/Contraseña nueva/), { target: { value: 'Nevado del Ruiz en la tarde 2026' } });
    fireEvent.change(within(formulario).getByLabelText('Repite la contraseña nueva'), { target: { value: 'otra distinta de la nueva' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Cambiar contraseña' }));
    expect(await within(formulario).findByRole('alert')).toHaveTextContent('no coinciden');
    expect(llamadas.some((l) => l.ruta === '/api/v1/identidad/cuenta/contrasena')).toBe(false);

    fireEvent.change(within(formulario).getByLabelText('Repite la contraseña nueva'), { target: { value: 'Nevado del Ruiz en la tarde 2026' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Cambiar contraseña' }));
    expect(await within(formulario).findByRole('status')).toHaveTextContent('Cambiamos tu contraseña');
    expect(llamadas.find((l) => l.ruta === '/api/v1/identidad/cuenta/contrasena')?.cuerpo).toEqual({
      contrasenaActual: 'Rio Magdalena al amanecer 2026',
      contrasenaNueva: 'Nevado del Ruiz en la tarde 2026',
    });
  });

  it('si el ingreso no es reciente pide salir y volver a entrar', async () => {
    apiFalsa({
      cambio: () =>
        new Response(JSON.stringify({ title: 'Vuelve a ingresar.', codigo: 'reautenticacion_requerida' }), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    });
    window.history.pushState({}, '', '/ajustes/seguridad');
    render(<App />);

    fireEvent.click(await screen.findByRole('button', { name: 'Generar códigos nuevos' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Generar' }));
    expect(await screen.findByRole('button', { name: 'Salir y volver a entrar' })).toBeInTheDocument();
  });

  it('genera códigos nuevos tras confirmar y los muestra una sola vez', async () => {
    const llamadas = apiFalsa();
    window.history.pushState({}, '', '/ajustes/seguridad');
    render(<App />);

    fireEvent.click(await screen.findByRole('button', { name: 'Generar códigos nuevos' }));
    expect(llamadas.some((l) => l.ruta === '/api/v1/identidad/cuenta/codigos-recuperacion')).toBe(false);
    fireEvent.click(await screen.findByRole('button', { name: 'Generar' }));
    const lista = await screen.findByRole('list', { name: 'Códigos de recuperación' });
    expect(within(lista).getAllByRole('listitem')).toHaveLength(10);
  });

  it('una cuenta de Microsoft no tiene esta pantalla ni el enlace en ajustes', async () => {
    const llamadas = apiFalsa({ proveedor: 'microsoft' });
    window.history.pushState({}, '', '/ajustes');
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Ajustes' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Seguridad de la cuenta/ })).not.toBeInTheDocument();

    window.history.pushState({}, '', '/ajustes/seguridad');
    render(<App />);
    expect(await screen.findByText(/Entras con tu cuenta de Microsoft/)).toBeInTheDocument();
    expect(llamadas.some((l) => l.ruta === '/api/v1/identidad/cuenta/seguridad')).toBe(false);
  });

  it('una cuenta local ve el enlace en ajustes', async () => {
    apiFalsa();
    window.history.pushState({}, '', '/ajustes');
    render(<App />);
    expect(await screen.findByRole('link', { name: /Seguridad de la cuenta/ })).toHaveAttribute('href', '/ajustes/seguridad');
  });
});
