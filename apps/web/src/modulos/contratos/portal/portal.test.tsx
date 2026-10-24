import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { MODULOS_REGISTRADOS } from '../../../app/modulos/registro';
import { resolverNavegacion } from '../../../app/modulos/resolver';
import { moduloContratos } from '../modulo';
import { conectarAccionPortal, estadoAccion } from './acciones';
import type { DetallePaquetePortal, InicioPortal, PaqueteCompartido } from './api';
import Portal from './Portal';

/** HU 4.4 (ATLAS-ADR-0043): el portal de clientes con una API falsa, la navegación de un externo y los ganchos de las acciones. */
function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const CONTRATO = {
  contratoId: 'c1',
  codigo: 'api-tramites',
  nombre: 'API de Trámites Demo',
  tipo: 'openapi' as const,
  aplicativo: { id: 'a1', slug: 'tramites', nombre: 'Trámites Demo' },
  versionId: 'v1',
  version: '1.2.0',
  estadoVersion: 'deprecada' as const,
  retiroEn: '2027-06-30',
  nivel: 'tecnico' as const,
  operaciones: 1,
  guias: 1,
};

const paquete = (cambios: Partial<PaqueteCompartido> = {}): PaqueteCompartido => ({
  id: 'p1',
  nombre: 'Paquete Integradores Demo',
  descripcion: 'Para Organismo Demo.',
  revision: 2,
  nivel: 'tecnico',
  puedeExportar: false,
  puedeSandbox: true,
  vigenteHasta: '2027-01-31T23:59:59-05:00',
  contratos: [CONTRATO],
  ...cambios,
});

const AVISO = {
  tipo: 'deprecada' as const,
  paqueteId: 'p1',
  paquete: 'Paquete Integradores Demo',
  contratoId: 'c1',
  contrato: 'API de Trámites Demo',
  version: '1.2.0',
  retiroEn: '2027-06-30',
  mensaje: 'La versión 1.2.0 de API de Trámites Demo está deprecada y se retira el 30/06/2027.',
};

const INICIO: InicioPortal = {
  organizacion: { id: 'o1', nombre: 'Organismo Demo' },
  aplicativos: [{ aplicativo: CONTRATO.aplicativo, paquetes: ['p1'], contratos: 1, documentos: [] }],
  paquetes: [paquete()],
  avisos: [AVISO],
};

const detalle = (p: PaqueteCompartido = paquete()): DetallePaquetePortal => ({
  paquete: p,
  contratos: [
    {
      contrato: { ...CONTRATO, nivel: p.nivel, operaciones: p.nivel === 'tecnico' ? 1 : 0, guias: p.nivel === 'tecnico' ? 1 : 0 },
      operaciones: p.nivel === 'tecnico' ? [{ clave: 'GET /tramites/{id}', metodo: 'GET', ruta: '/tramites/{id}', operationId: 'obtenerTramite', resumen: 'Obtener un trámite', etiquetas: [] }] : [],
      guias: p.nivel === 'tecnico' ? [{ id: 'g1', titulo: 'Cómo autenticarte', operacion: null, revision: 1 }] : [],
    },
  ],
  avisos: [AVISO],
});

const SPEC_PODADO = `openapi: 3.1.0
info:
  title: API de Trámites Demo
  version: 1.2.0
paths:
  /tramites/{id}:
    get:
      operationId: obtenerTramite
      summary: Obtener un trámite
      responses:
        '200':
          description: El trámite.
`;

const SANDBOX = {
  id: 's1',
  paqueteId: 'p1',
  paquete: 'Paquete Integradores Demo',
  revision: 2,
  nivel: 1,
  estado: 'activo' as const,
  motivoApagado: null,
  activadoEn: '2026-10-24T10:00:00-05:00',
  llaveCreadaEn: '2026-10-24T10:00:00-05:00',
  expiraEn: '2027-01-31T23:59:59-05:00',
  ultimoUsoEn: null,
  cabecera: 'Authorization: Bearer $LLAVE_SANDBOX',
  contratos: [{ contratoId: 'c1', codigo: 'api-tramites', nombre: 'API de Trámites Demo', version: '1.2.0', urlBase: 'https://sandbox.ejemplo.com/api/v1/contratos/sandbox/0123456789abcdef0123456789abcdef/api-tramites', operaciones: 1 }],
  instrucciones: 'Guarda la llave en la variable de entorno LLAVE_SANDBOX.',
  ejemploCurl: 'curl -g -X GET -H "Authorization: Bearer $LLAVE_SANDBOX" "https://sandbox.ejemplo.com/api/v1/contratos/sandbox/0123456789abcdef0123456789abcdef/api-tramites/tramites/{id}"',
};

interface OpcionesApi {
  /** `false`: el ambiente sin Microcks. */
  sandboxDisponible?: boolean;
  /** Sandboxes que ya tiene la organización. */
  sandboxes?: (typeof SANDBOX)[];
}

function apiFalsa(paq: PaqueteCompartido = paquete(), { sandboxDisponible = true, sandboxes = [] }: OpcionesApi = {}) {
  const llamadas: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, opciones?: RequestInit) => {
      llamadas.push(opciones?.method === 'POST' ? `POST ${ruta}` : ruta);
      const url = new URL(ruta, 'https://atlas.ejemplo.com');
      if (url.pathname === '/api/v1/portal/sandbox' && opciones?.method === 'POST')
        return sandboxDisponible
          ? json({ sandbox: SANDBOX, llave: 'atlsbx_llaveFicticiaDePrueba' }, 201)
          : json({ codigo: 'sandbox_no_disponible', mensaje: 'El sandbox no está disponible en este ambiente.' }, 503);
      if (url.pathname === '/api/v1/portal/sandbox')
        return json({ disponible: sandboxDisponible, motivo: sandboxDisponible ? null : 'El sandbox no está disponible en este ambiente.', sandboxes });
      if (url.pathname === '/api/v1/portal/inicio') return json(INICIO);
      if (url.pathname === '/api/v1/portal/paquetes/p1') return json(detalle(paq));
      if (url.pathname === '/api/v1/portal/paquetes/p1/contratos/c1/spec') return new Response(SPEC_PODADO, { headers: { 'Content-Type': 'application/yaml' } });
      if (url.pathname === '/api/v1/portal/paquetes/p1/contratos/c1/cambios')
        return json({ contratoId: 'c1', version: '1.2.0', base: '1.1.0', elementos: [{ id: 'response-property-added', texto: 'Se agregó estado', nivel: 1, metodo: 'GET', ruta: '/tramites/{id}' }] });
      if (url.pathname === '/api/v1/portal/paquetes/p1/guias/g1')
        return json({ id: 'g1', paqueteId: 'p1', contratoId: 'c1', titulo: 'Cómo autenticarte', operacion: null, revision: 1, contenido: 'Usa `Bearer $TOKEN`.' });
      if (url.pathname === '/api/v1/portal/buscar')
        return json([{ tipo: 'operacion', titulo: 'GET /tramites/{id}', detalle: 'Obtener un trámite', paqueteId: 'p1', contratoId: 'c1', ruta: '/portal/paquetes/p1/contratos/c1' }]);
      return json({ codigo: 'no_encontrado', mensaje: 'No tienes este paquete compartido.' }, 404);
    }),
  );
  return llamadas;
}

function montar(ruta: string) {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[ruta]}>
        <Routes>
          <Route path="/portal/*" element={<Portal />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('navegación del portal', () => {
  const externo = { banderas: {}, permisos: ['atlas.portal.ver', 'atlas.docs.ver', 'atlas.asistente.usar'], tipoPrincipal: 'externo' as const };

  it('el manifiesto registra /portal con atlas.portal.ver y las entradas internas de contratos exigen ver_internos', () => {
    expect(moduloContratos.rutas.find((r) => r.path === '/portal')?.permiso).toBe('atlas.portal.ver');
    const portal = moduloContratos.navegacion.find((n) => n.ruta === '/portal');
    expect(portal).toMatchObject({ permiso: 'atlas.portal.ver', audiencia: 'externos', movil: 'barra_inferior' });
    expect(moduloContratos.navegacion.filter((n) => n.ruta !== '/portal').every((n) => n.permiso === 'atlas.contratos.ver_internos')).toBe(true);
  });

  it('un externo ve Inicio, Buscar, Portal y lo próximo, nunca la navegación interna (catálogo, equipo, soporte, contratos)', () => {
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, externo);
    const rutas = nav.lateral.map((n) => n.ruta);
    expect(rutas).toContain('/portal');
    for (const interna of ['/catalogo', '/equipo', '/soporte', '/contratos', '/contratos/paquetes', '/contratos/consolidado', '/administracion/roles']) {
      expect(rutas).not.toContain(interna);
    }
    expect(nav.barraInferior.map((n) => n.ruta)).toContain('/portal');
    expect(nav.widgets.map((w) => w.id)).toEqual(expect.arrayContaining(['portal-inicio-cliente']));
    expect(nav.widgets.map((w) => w.id)).not.toContain('contratos-cambios');
  });

  it('un empleado con atlas.portal.ver no recibe la entrada ni el widget del portal (audiencia externos)', () => {
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.portal.ver', 'atlas.contratos.ver_internos'], tipoPrincipal: 'empleado' });
    expect(nav.lateral.map((n) => n.ruta)).not.toContain('/portal');
    expect(nav.widgets.map((w) => w.id)).not.toContain('portal-inicio-cliente');
    expect(nav.lateral.map((n) => n.ruta)).toContain('/contratos');
  });
});

describe('acciones del portal', () => {
  it('sin permiso, deshabilitada con el motivo; con permiso pero sin conectar, «pronto»; conectada, habilitada', () => {
    expect(estadoAccion('exportar', false)).toMatchObject({ habilitada: false, motivo: expect.stringContaining('no incluyen exportar') });
    expect(estadoAccion('exportar', true)).toMatchObject({ habilitada: false, motivo: expect.stringContaining('pronto') });
    const ejecutar = vi.fn();
    const desconectar = conectarAccionPortal('exportar', ejecutar);
    expect(estadoAccion('exportar', true)).toMatchObject({ habilitada: true, motivo: null, ejecutar });
    desconectar();
    expect(estadoAccion('exportar', true).habilitada).toBe(false);
  });
});

describe('pantallas del portal', () => {
  it('Inicio del cliente: su organización, avisos de deprecación, sus aplicativos con sus paquetes y la búsqueda en lo compartido', async () => {
    const llamadas = apiFalsa();
    montar('/portal');

    expect(await screen.findByRole('heading', { name: 'Lo que FLIT comparte contigo' })).toBeInTheDocument();
    expect(screen.getByText(/Organismo Demo/)).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Avisos de deprecación' })).getByText(/se retira el 30\/06\/2027/)).toBeInTheDocument();
    const aplicativos = screen.getByRole('list', { name: /Paquetes de Trámites Demo/ });
    expect(within(aplicativos).getByRole('link', { name: /Paquete Integradores Demo/ })).toHaveAttribute('href', '/portal/paquetes/p1');
    expect(within(aplicativos).getByText(/compartido hasta el 31\/01\/2027/)).toBeInTheDocument();

    fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar en lo compartido' }), { target: { value: 'tramite' } });
    expect(await screen.findByRole('link', { name: /GET \/tramites\/\{id\}/ })).toHaveAttribute('href', '/portal/paquetes/p1/contratos/c1');
    expect(llamadas.some((l) => l.startsWith('/api/v1/portal/buscar?q=tramite'))).toBe(true);
  });

  it('paquete técnico: operaciones y guías compartidas; Exportar deshabilitado con su motivo y Sandbox conectado (HU 4.7)', async () => {
    apiFalsa();
    montar('/portal/paquetes/p1');

    expect(await screen.findByRole('heading', { name: 'Paquete Integradores Demo', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'API de Trámites Demo' })).toHaveAttribute('href', '/portal/paquetes/p1/contratos/c1');
    expect(screen.getByRole('link', { name: /Cómo autenticarte/ })).toHaveAttribute('href', '/portal/paquetes/p1/guias/g1');
    const acciones = screen.getByRole('group', { name: 'Acciones del paquete' });
    expect(within(acciones).getByRole('button', { name: /Exportar/ })).toBeDisabled();
    expect(screen.getByText(/Exportar: la compartición con tu organización o tu rol no incluyen exportar/)).toBeInTheDocument();
    await waitFor(() => expect(within(acciones).getByRole('button', { name: /Sandbox/ })).toBeEnabled());
    expect(await screen.findByRole('button', { name: 'Activar el sandbox' })).toBeInTheDocument();
  });

  it('sandbox: activarlo muestra la llave una sola vez para copiar, la URL base y el ejemplo con curl con $LLAVE_SANDBOX', async () => {
    const llamadas = apiFalsa();
    montar('/portal/paquetes/p1/contratos/c1');

    const panel = await screen.findByRole('region', { name: 'Sandbox' }, { timeout: 10_000 });
    fireEvent.click(await within(panel).findByRole('button', { name: 'Activar el sandbox' }));
    expect(await within(panel).findByLabelText('Llave del sandbox')).toHaveTextContent('atlsbx_llaveFicticiaDePrueba');
    expect(within(panel).getByText(/no la volverás a ver/)).toBeInTheDocument();
    expect(within(panel).getByText(SANDBOX.contratos[0]!.urlBase)).toBeInTheDocument();
    expect(within(panel).getByText(/curl -g -X GET -H "Authorization: Bearer \$LLAVE_SANDBOX"/)).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Generar una llave nueva' })).toBeInTheDocument();
    expect(llamadas).toContain('POST /api/v1/portal/sandbox');
  });

  it('sandbox ya activo: muestra la URL sin la llave y permite generar una nueva', async () => {
    apiFalsa(paquete(), { sandboxes: [SANDBOX] });
    montar('/portal/paquetes/p1/contratos/c1');

    const panel = await screen.findByRole('region', { name: 'Sandbox' }, { timeout: 10_000 });
    expect(await within(panel).findByText(SANDBOX.contratos[0]!.urlBase)).toBeInTheDocument();
    expect(within(panel).queryByLabelText('Llave del sandbox')).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Generar una llave nueva' })).toBeInTheDocument();
  });

  it('sin Microcks en el ambiente: el botón Sandbox deshabilitado con «El sandbox no está disponible»', async () => {
    apiFalsa(paquete(), { sandboxDisponible: false });
    montar('/portal/paquetes/p1');

    const acciones = await screen.findByRole('group', { name: 'Acciones del paquete' });
    await waitFor(() => expect(within(acciones).getByRole('button', { name: /Sandbox/ })).toBeDisabled());
    expect(screen.getByText('El sandbox no está disponible.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Activar el sandbox' })).not.toBeInTheDocument();
  });

  it('paquete en resumen: sin enlaces al detalle técnico ni acciones', async () => {
    apiFalsa(paquete({ nivel: 'resumen' }));
    montar('/portal/paquetes/p1');

    expect(await screen.findByText(/Tu rol ve el resumen de este paquete/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'API de Trámites Demo' })).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Acciones del paquete' })).not.toBeInTheDocument();
  });

  it('contrato compartido: la vista de operaciones sale del spec podado y los cambios solo de lo compartido', async () => {
    const llamadas = apiFalsa();
    montar('/portal/paquetes/p1/contratos/c1');

    expect(await screen.findByRole('heading', { name: 'API de Trámites Demo' })).toBeInTheDocument();
    expect(await screen.findByText('/tramites/{id}')).toBeInTheDocument();
    expect(llamadas).toContain('/api/v1/portal/paquetes/p1/contratos/c1/spec');
    // Nunca pide el spec completo de la vista interna.
    expect(llamadas.some((l) => l.includes('/api/v1/contratos/'))).toBe(false);
    fireEvent.click(screen.getByRole('tab', { name: 'Cambios' }));
    expect(await screen.findByText('Se agregó estado')).toBeInTheDocument();
  });

  it('una guía compartida se lee en Markdown sanitizado', async () => {
    apiFalsa();
    montar('/portal/paquetes/p1/guias/g1');

    expect(await screen.findByRole('heading', { name: 'Cómo autenticarte' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('article', { name: 'Contenido de Cómo autenticarte' })).toHaveTextContent('Bearer $TOKEN'));
  });

  it('un paquete que no está compartido (o de otra organización) dice que no lo tienes, sin más detalle', async () => {
    apiFalsa();
    montar('/portal/paquetes/otro');

    expect(await screen.findByText('No tienes este paquete compartido.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ir al inicio del portal' })).toHaveAttribute('href', '/portal');
  });
});
