import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { resolverNavegacion } from '../../app/modulos/resolver';
import type { ConsolidadoContratos, ContratoConsolidado } from './consolidado';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const conDesfase: ContratoConsolidado = {
  id: 'c1',
  codigo: 'api-externa',
  nombre: 'API Demo',
  tipo: 'openapi',
  aplicativo: { id: 'a1', slug: 'api-demo', nombre: 'Aplicativo Demo' },
  canales: [
    { ambiente: 'dev', version: '3.4.0-dev.a1b2c3d', esEstable: false, desplegadoEn: '2026-10-22T15:00:00Z' },
    { ambiente: 'qa', version: '3.3.0', esEstable: true, desplegadoEn: '2026-10-21T15:00:00Z' },
    { ambiente: 'pdn', version: '3.2.0', esEstable: true, desplegadoEn: '2026-10-01T15:00:00Z' },
  ],
  desfases: [{ ambiente: 'qa', respectoDe: 'pdn', tipo: 'adelante', mensaje: 'qa (3.3.0) va adelante de pdn (3.2.0)' }],
  breakingPendientes: {
    desde: '3.2.0',
    hasta: '3.3.0',
    estado: 'con_breaking',
    fuente: 'changelog',
    cambios: [{ regla: 'api-removed-without-deprecation', texto: 'Se quitó la operación sin deprecarla', metodo: 'GET', ruta: '/demo/{id}' }],
  },
};

const alDia: ContratoConsolidado = {
  id: 'c2',
  codigo: 'eventos-demo',
  nombre: 'Eventos Demo',
  tipo: 'asyncapi',
  aplicativo: { id: 'a1', slug: 'api-demo', nombre: 'Aplicativo Demo' },
  canales: [
    { ambiente: 'dev', version: null, esEstable: null, desplegadoEn: null },
    { ambiente: 'qa', version: '1.0.0', esEstable: true, desplegadoEn: '2026-10-20T15:00:00Z' },
    { ambiente: 'pdn', version: '1.0.0', esEstable: true, desplegadoEn: '2026-10-20T16:00:00Z' },
  ],
  desfases: [],
  breakingPendientes: null,
};

const consolidado = (elementos: ContratoConsolidado[]): ConsolidadoContratos => ({
  ambientes: ['dev', 'qa', 'pdn'],
  total: elementos.length,
  conDesfases: elementos.filter((e) => e.desfases.length > 0).length,
  conBreaking: elementos.filter((e) => e.breakingPendientes?.estado === 'con_breaking').length,
  elementos,
});

function apiFalsa(respuesta: (ruta: string) => Response) {
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
          permisos: ['atlas.contratos.ver_internos'],
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta.startsWith('/api/v1/contratos/consolidado')) return respuesta(ruta);
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

describe('navegación del consolidado', () => {
  it('aparece en «Más» solo con atlas.contratos.ver_internos', () => {
    const con = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.contratos.ver_internos'] });
    const sin = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.catalogo.ver'] });
    expect(con.mas.map((e) => e.ruta)).toContain('/contratos/consolidado');
    expect(sin.mas.map((e) => e.ruta)).not.toContain('/contratos/consolidado');
  });
});

describe('pantalla Consolidado', () => {
  it('muestra el resumen, la versión por ambiente, los desfases y los breaking pendientes', async () => {
    apiFalsa(() => json(consolidado([conDesfase, alDia])));
    window.history.pushState({}, '', '/contratos/consolidado');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Consolidado de contratos' })).toBeInTheDocument();
    const resumen = await screen.findByRole('list', { name: 'Contratos por ambiente' });
    const tarjeta = within(resumen).getByRole('article', { name: 'API Demo' });
    expect(within(tarjeta).getByText('3.4.0-dev.a1b2c3d')).toBeInTheDocument();
    expect(within(tarjeta).getByText('3.2.0')).toBeInTheDocument();
    expect(within(tarjeta).getByText('qa (3.3.0) va adelante de pdn (3.2.0)')).toBeInTheDocument();
    expect(within(tarjeta).getByText('01/10/2026')).toBeInTheDocument();

    fireEvent.click(within(tarjeta).getByText(/1 cambio rompe compatibilidad/));
    const cambios = within(tarjeta).getByRole('list', { name: 'Breaking pendientes de 3.2.0 a 3.3.0' });
    expect(within(cambios).getByText('api-removed-without-deprecation')).toBeInTheDocument();
    expect(within(cambios).getByText('GET /demo/{id}')).toBeInTheDocument();

    const otra = within(resumen).getByRole('article', { name: 'Eventos Demo' });
    expect(within(otra).getByText('—')).toBeInTheDocument();

    const cifras = within(screen.getByLabelText('Resumen')).getAllByRole('definition');
    expect(cifras.map((c) => c.textContent)).toEqual(['2', '1', '1']);
    // La tabla de escritorio también está (se oculta con CSS en el móvil).
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  it('explica cuando no hay diff disponible o el tipo no lo tiene', async () => {
    const sinDiff: ContratoConsolidado = {
      ...conDesfase,
      breakingPendientes: { desde: '3.2.0', hasta: '3.3.0', estado: 'no_disponible', fuente: null, cambios: [] },
    };
    apiFalsa(() => json(consolidado([sinDiff])));
    window.history.pushState({}, '', '/contratos/consolidado');

    render(<App />);

    expect((await screen.findAllByText(/No pudimos calcular el diff entre pdn y qa/)).length).toBeGreaterThan(0);
  });

  it('los filtros quedan en la URL y se envían al servidor', async () => {
    const llamadas = apiFalsa(() => json(consolidado([conDesfase])));
    window.history.pushState({}, '', '/contratos/consolidado?tipo=openapi');

    render(<App />);
    await screen.findByRole('article', { name: 'API Demo' });
    expect(llamadas).toContain('/api/v1/contratos/consolidado?tipo=openapi');

    fireEvent.click(screen.getByRole('checkbox', { name: 'Solo con desfases' }));
    await screen.findByRole('article', { name: 'API Demo' });
    expect(window.location.search).toContain('desfases=true');
    expect(llamadas).toContain('/api/v1/contratos/consolidado?tipo=openapi&desfases=true');
  });

  it('sin contratos muestra el estado vacío', async () => {
    apiFalsa(() => json(consolidado([])));
    window.history.pushState({}, '', '/contratos/consolidado');

    render(<App />);

    expect(await screen.findByText(/Todavía no hay contratos publicados/)).toBeInTheDocument();
  });

  it('si la API falla muestra el error', async () => {
    apiFalsa(() => new Response('', { status: 500 }));
    window.history.pushState({}, '', '/contratos/consolidado');

    render(<App />);

    // El cliente de consultas reintenta una vez antes de dar el error.
    expect(await screen.findByRole('alert', {}, { timeout: 5000 })).toHaveTextContent('No pudimos cargar el consolidado');
  });
});
