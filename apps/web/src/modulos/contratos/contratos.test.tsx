import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { resolverNavegacion } from '../../app/modulos/resolver';
import type { ContratoResumen, VersionResumen } from './api';
import { moduloContratos } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const version = (v: string, estable: boolean, publicadoEn: string): VersionResumen => ({
  id: `v-${v}`,
  version: v,
  esEstable: estable,
  sha256: 'a'.repeat(64),
  formato: 'yaml',
  tamano: 1200,
  repositorio: 'flitsas-demo/api-demo',
  rama: estable ? 'release' : 'develop',
  commit: '0123456789abcdef0123456789abcdef01234567',
  workflow: '.github/workflows/ci.yml',
  workflowDelJob: 'flitsas-demo/plantillas/.github/workflows/atlas-publicar.yml@refs/tags/v1',
  ejecucion: '1234567890',
  publicadoPor: `github:flitsas-demo/api-demo@${estable ? 'release' : 'develop'}`,
  publicadoEn,
  tieneBreaking: !estable,
  estado: 'vigente',
  retiroEn: null,
});

const estable = version('1.2.0', true, '2026-10-23T15:00:00Z');
const dev = version('1.3.0-dev.a1b2c3d', false, '2026-10-23T16:00:00Z');

const contrato: ContratoResumen = {
  id: 'c1',
  codigo: 'api-externa',
  nombre: 'API Demo',
  descripcion: 'Contrato ficticio de prueba.',
  tipo: 'openapi',
  visibilidad: 'interna',
  aplicativo: { id: 'a1', slug: 'api-demo', nombre: 'Aplicativo Demo' },
  repositorio: 'flitsas-demo/api-demo',
  versiones: 2,
  ultimaVersion: dev,
  ultimaEstable: estable,
};

function apiFalsa(permisos: string[]) {
  const llamadas: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string) => {
      llamadas.push(ruta);
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: 'p',
          nombre: 'Empleada Demo',
          tipoPrincipal: 'empleado',
          organizacionId: 'o',
          proveedor: 'simulado',
          roles: ['atlas.desarrollador'],
          permisos,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta.startsWith('/api/v1/contratos/c1/versiones')) return json([dev, estable]);
      if (ruta.startsWith('/api/v1/contratos')) return json({ total: 1, elementos: [contrato] });
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

describe('manifiesto de contratos', () => {
  it('está disponible, encendido por defecto y solo con atlas.contratos.ver_internos', () => {
    expect(moduloContratos.estado).toBe('disponible');
    expect(moduloContratos.banderaPorDefecto).toBe(true);
    const con = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.contratos.ver_internos'] });
    const sin = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.catalogo.ver'] });
    expect(con.mas.map((e) => e.ruta)).toContain('/contratos');
    expect(con.widgets.map((w) => w.id)).toContain('contratos-cambios');
    expect(sin.mas.map((e) => e.ruta)).not.toContain('/contratos');
    expect(moduloContratos.rutas[0]?.cargar.toString()).toMatch(/import/);
  });
});

describe('pantalla de contratos', () => {
  it('lista los contratos con su última versión y, al abrir uno, pide y muestra sus versiones', async () => {
    const llamadas = apiFalsa(['atlas.contratos.ver_internos']);
    window.history.pushState({}, '', '/contratos');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Contratos de integración' })).toBeInTheDocument();
    expect(await screen.findByText('API Demo')).toBeInTheDocument();
    expect(screen.getByText('Estable 1.2.0')).toBeInTheDocument();
    expect(screen.getByText('1.3.0-dev.a1b2c3d')).toBeInTheDocument();
    expect(llamadas.some((r) => r.includes('/versiones'))).toBe(false);

    fireEvent.click(screen.getByText('API Demo'));
    const lista = await screen.findByRole('list', { name: 'Versiones' });
    expect(within(lista).getAllByRole('listitem')).toHaveLength(2);
    expect(within(lista).getByText('Rompe compatibilidad')).toBeInTheDocument();
    expect(within(lista).getAllByText(/flitsas-demo\/api-demo@/)).toHaveLength(2);
    expect(llamadas).toContain('/api/v1/contratos/c1/versiones');
  });

  it('filtrar por tipo vuelve a pedir la lista con el tipo', async () => {
    const llamadas = apiFalsa(['atlas.contratos.ver_internos']);
    window.history.pushState({}, '', '/contratos');

    render(<App />);
    fireEvent.click(await screen.findByRole('radio', { name: 'AsyncAPI' }));

    await screen.findByText('API Demo');
    expect(llamadas.some((r) => r === '/api/v1/contratos?tipo=asyncapi')).toBe(true);
  });
});
