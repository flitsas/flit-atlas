import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import type { ContratoResumen, DiffContrato, VersionResumen } from './api';

/**
 * Pantalla del contrato (HU 3.6 y 3.9) con el spec real del legado ICT (`contracts/legado/external-transact.yaml`, ya
 * saneado) como fixture: operaciones con su ficha, matriz de obligatoriedad, tablas de referencia, versiones con diff y
 * los marcadores de guías, canales y paquetes. En jsdom no hay escritorio: no aparece la referencia con Scalar.
 */
const LEGADO = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../../contracts/legado');
const SPEC = readFileSync(join(LEGADO, 'external-transact.yaml'), 'utf8');

// El spec real pesa ~400 KB: leerlo y armar la vista puede pasar del segundo por defecto con la máquina cargada.
const ESPERA = { timeout: 8000 };
vi.setConfig({ testTimeout: 30_000 });

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const version = (v: string, estable: boolean, breaking: boolean, publicadoEn: string): VersionResumen => ({
  id: `v-${v}`,
  version: v,
  esEstable: estable,
  sha256: 'b'.repeat(64),
  formato: 'yaml',
  tamano: SPEC.length,
  repositorio: 'flitsas-demo/back-api-external-transact',
  rama: estable ? 'release' : 'develop',
  commit: 'abcdef0123456789abcdef0123456789abcdef01',
  workflow: '.github/workflows/atlas-publicar.yml',
  workflowDelJob: 'flitsas-demo/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v1',
  ejecucion: '42',
  publicadoPor: 'github:flitsas-demo/back-api-external-transact@release',
  publicadoEn,
  tieneBreaking: breaking,
  estado: 'vigente',
  retiroEn: null,
});

const v3 = version('3.0.0', true, true, '2026-10-20T15:00:00Z');
const v2 = version('2.9.0', true, false, '2026-09-01T15:00:00Z');

const contrato: ContratoResumen = {
  id: 'c7',
  codigo: 'external-transact',
  nombre: 'API externa ICT · Trámites',
  descripcion: null,
  tipo: 'openapi',
  visibilidad: 'compartible',
  aplicativo: { id: 'a1', slug: 'ict', nombre: 'ICT' },
  repositorio: 'flitsas-demo/back-api-external-transact',
  versiones: 2,
  ultimaVersion: v3,
  ultimaEstable: v3,
};

const diff: DiffContrato = {
  contratoId: 'c7',
  desde: '2.9.0',
  hasta: '3.0.0',
  motor: 'oasdiff v1.33.0',
  sinDiferencias: false,
  resumen: { total: 2, errores: 1, advertencias: 0, informativos: 1, breaking: 1, hayBreaking: true },
  cambios: [
    { regla: 'api-path-removed-without-deprecation', nivel: 'error', breaking: true, texto: 'Se quitó la ruta sin deprecarla', comentario: null, metodo: 'GET', ruta: '/api/v1/viejo', operationId: null, seccion: 'paths' },
    { regla: 'endpoint-added', nivel: 'info', breaking: false, texto: 'Se agregó una ruta', comentario: null, metodo: 'POST', ruta: '/api/v1/nuevo', operationId: null, seccion: 'paths' },
  ],
  changelog: '',
  desdeCache: false,
};

function apiFalsa() {
  const llamadas: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string) => {
      llamadas.push(ruta);
      if (ruta === '/api/v1/plataforma/yo')
        return json({ sub: 's', personaId: 'p', nombre: 'Persona Demo', tipoPrincipal: 'empleado', organizacionId: 'o', proveedor: 'simulado', roles: [], permisos: ['atlas.contratos.ver_internos'] });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta === '/api/v1/contratos/c7') return json(contrato);
      if (ruta === '/api/v1/contratos/c7/versiones') return json([v3, v2]);
      if (/\/versiones\/[^/]+\/spec$/.test(ruta)) return new Response(SPEC, { headers: { 'Content-Type': 'application/yaml' } });
      if (/\/versiones\/[^/]+\/operaciones$/.test(ruta)) return json([]);
      if (ruta.startsWith('/api/v1/contratos/c7/diff')) return json(diff);
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

beforeEach(() => {
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
});

afterEach(() => vi.unstubAllGlobals());

describe('pantalla del contrato', () => {
  it('muestra las operaciones del spec y la ficha de una en la hoja, con ejemplos y errores', async () => {
    apiFalsa();
    window.history.pushState({}, '', '/contratos/c7');
    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'API externa ICT · Trámites' }, ESPERA)).toBeInTheDocument();
    const pestanas = await screen.findByRole('tablist', { name: 'Secciones del contrato' }, ESPERA);
    expect(within(pestanas).getAllByRole('tab').map((t) => t.textContent)).toEqual([
      'Operaciones',
      'Obligatoriedad',
      expect.stringMatching(/^Tablas de referencia\d+$/),
      'Versiones2',
      'Guías',
      'Canales',
      'Paquetes',
    ]);

    const operaciones = await screen.findByRole('navigation', { name: 'Operaciones' }, ESPERA);
    fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar operaciones' }), { target: { value: 'external-transaction/register' } });
    const boton = (await within(operaciones).findAllByRole('button')).find((b) => b.textContent?.includes('/api/v1/external-transaction/register'))!;
    fireEvent.click(boton);

    const hoja = await screen.findByRole('dialog', { name: /POST \/api\/v1\/external-transaction\/register/ });
    expect(within(hoja).getByRole('heading', { name: 'Errores' })).toBeInTheDocument();
    expect(within(hoja).getAllByText('No autenticado').length).toBeGreaterThan(0);
    expect(within(hoja).getAllByText('Bearer $TOKEN').length).toBeGreaterThan(0);
    // Ejemplos con httpsnippet, en pestañas por lenguaje y con variables.
    const lenguajes = await within(hoja).findByRole('tablist', { name: 'Lenguaje del ejemplo' }, ESPERA);
    expect(within(lenguajes).getAllByRole('tab').map((t) => t.textContent)).toEqual(['curl', 'C#', 'Python', 'Go', 'JavaScript']);
    expect(await within(hoja).findByText(/curl --request POST/, {}, ESPERA)).toHaveTextContent('{{baseUrl}}/api/v1/external-transaction/register');
    fireEvent.click(within(lenguajes).getByRole('tab', { name: 'Python' }));
    expect(within(hoja).getByText(/import requests/)).toBeInTheDocument();
  });

  it('matriz de obligatoriedad por tipo de trámite y tablas de referencia con búsqueda', async () => {
    apiFalsa();
    window.history.pushState({}, '', '/contratos/c7?pestana=obligatoriedad');
    render(<App />);

    const matriz = await screen.findByRole('region', { name: 'Matriz de obligatoriedad de SolicitudRegistroTramite' }, ESPERA);
    const tabla = within(matriz).getByRole('table');
    expect(within(tabla).getAllByRole('columnheader').map((c) => c.textContent)).toEqual(['Campo', 'Traspaso bilateral', 'Traspaso unilateral', 'Matricula inicial', 'Matricula tipo leasing', 'Condición']);
    fireEvent.change(within(matriz).getByRole('searchbox', { name: 'Buscar campo' }), { target: { value: 'vin' } });
    fireEvent.click(within(matriz).getByRole('radio', { name: /Matricula inicial/ }));
    const fila = (await within(tabla).findAllByRole('row')).find((r) => within(r).queryByText('vin', { selector: 'code' }))!;
    expect(within(fila).getByText('Obligatorio')).toBeInTheDocument();
    expect(within(tabla).getAllByRole('columnheader')).toHaveLength(3);

    fireEvent.click(screen.getByRole('tab', { name: /Tablas de referencia/ }));
    fireEvent.change(await screen.findByRole('searchbox', { name: 'Buscar en las tablas de referencia' }), { target: { value: 'pasaporte' } });
    const tablaDocumento = await screen.findByRole('table', { name: 'Tabla Tipo Documento' });
    expect(within(tablaDocumento).getAllByRole('row')).toHaveLength(2);
    expect(within(tablaDocumento).getByText('PA')).toBeInTheDocument();
  });

  it('línea de tiempo y diff con lo que rompe primero; guías, canales y paquetes como marcadores', async () => {
    const llamadas = apiFalsa();
    window.history.pushState({}, '', '/contratos/c7/v/3.0.0?pestana=versiones');
    render(<App />);

    expect(await screen.findByText('Breaking: sí', {}, ESPERA)).toBeInTheDocument();
    expect(screen.getByText('Breaking: no')).toBeInTheDocument();
    expect(screen.getAllByText(/flitsas-demo\/back-api-external-transact@release/).length).toBe(2);
    const rompen = await screen.findByRole('region', { name: 'Rompen compatibilidad' }, ESPERA);
    expect(within(rompen).getByText('Se quitó la ruta sin deprecarla')).toBeInTheDocument();
    expect(within(rompen).getByText('Error')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Otros cambios' })).toBeInTheDocument();
    expect(llamadas).toContain('/api/v1/contratos/c7/diff?desde=2.9.0&hasta=3.0.0');

    fireEvent.click(screen.getByRole('tab', { name: 'Guías' }));
    expect(await screen.findByText(/Próximamente: guías de integración/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Canales' }));
    expect(await screen.findByText(/HU 3.4/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Paquetes' }));
    expect(await screen.findByText(/fase F4/)).toBeInTheDocument();
  });

  it('una versión que no existe es 404', async () => {
    apiFalsa();
    window.history.pushState({}, '', '/contratos/c7/v/9.9.9');
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Esta página no existe' }, ESPERA)).toBeInTheDocument();
  });
});
