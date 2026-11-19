import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from '../../app/api';
import { App } from '../../app/App';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { banderaActiva, resolverNavegacion } from '../../app/modulos/resolver';
import { moduloHola } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const saludo = { id: 's1', texto: 'Hola desde Organismo Demo', organizacionId: 'o', creadoEn: new Date().toISOString() };

function apiFalsa(opciones: { banderas: Record<string, boolean>; permisos: string[] }) {
  const saludos = [saludo];
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
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
          proveedor: 'simulado',
          roles: ['atlas.desarrollador'],
          permisos: opciones.permisos,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json(opciones.banderas);
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta === '/api/v1/hola/saludos' && init?.method === 'POST') {
        const nuevo = { ...saludo, id: 's2', texto: (JSON.parse(String(init.body)) as { texto: string }).texto };
        saludos.unshift(nuevo);
        return json(nuevo, 201);
      }
      if (ruta === '/api/v1/hola/saludos') return json(saludos);
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

describe('manifiesto de hola', () => {
  it('viene apagado: sin bandera del servidor no aparece en ningún lado', () => {
    expect(moduloHola.bandera).toBe('modulo.hola');
    expect(banderaActiva(moduloHola, {})).toBe(false);
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.hola.ver'] });
    expect(nav.modulos.map((m) => m.id)).not.toContain('hola');
    expect(nav.widgets.map((w) => w.id)).not.toContain('hola-ultimo-saludo');
  });

  it('encendido por bandera y con atlas.hola.ver: entrada en «Más» y widget en Inicio, cargados de forma diferida', () => {
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: { 'modulo.hola': true }, permisos: ['atlas.hola.ver'] });
    expect(nav.mas.map((e) => e.ruta)).toContain('/hola');
    expect(nav.widgets.map((w) => w.id)).toContain('hola-ultimo-saludo');
    expect(moduloHola.rutas[0]?.cargar.toString()).toMatch(/import/);
    expect(moduloHola.widgetsInicio?.[0]?.cargar.toString()).toMatch(/import/);
  });
});

describe('pantalla de hola', () => {
  it('con la bandera apagada la ruta no existe', async () => {
    apiFalsa({ banderas: {}, permisos: ['atlas.hola.ver'] });
    window.history.pushState({}, '', '/hola');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Esta página no existe' })).toBeInTheDocument();
  });

  it('el widget de Inicio muestra el último saludo', async () => {
    apiFalsa({ banderas: { 'modulo.hola': true }, permisos: ['atlas.hola.ver'] });
    window.history.pushState({}, '', '/');

    render(<App />);

    const widget = await screen.findByRole('region', { name: 'Último saludo' });
    expect(await within(widget).findByText(/Hola desde Organismo Demo/)).toBeInTheDocument();
  });

  it('solo con atlas.hola.ver lista los saludos y no ofrece escribir', async () => {
    apiFalsa({ banderas: { 'modulo.hola': true }, permisos: ['atlas.hola.ver'] });
    window.history.pushState({}, '', '/hola');

    render(<App />);

    expect(await screen.findByText('Hola desde Organismo Demo')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Escribir un saludo' })).toBeNull();
  });

  it('con atlas.hola.gestionar publica un saludo con la cabecera antifalsificación', async () => {
    const llamadas = apiFalsa({ banderas: { 'modulo.hola': true }, permisos: ['atlas.hola.ver', 'atlas.hola.gestionar'] });
    window.history.pushState({}, '', '/hola');

    render(<App />);
    const formulario = await screen.findByRole('form', { name: 'Escribir un saludo' });
    const boton = within(formulario).getByRole('button', { name: 'Publicar saludo' });
    expect(boton).toBeDisabled();
    fireEvent.change(within(formulario).getByLabelText('Tu saludo'), { target: { value: '  Buenos días  ' } });
    fireEvent.click(boton);

    expect(await screen.findByText('Buenos días')).toBeInTheDocument();
    const envio = llamadas.find((l) => l.init?.method === 'POST');
    expect(envio?.init?.body).toBe(JSON.stringify({ texto: 'Buenos días' }));
    expect(new Headers(envio?.init?.headers).get(CABECERA_CSRF)).toBe('1');
  });
});
