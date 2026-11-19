import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { moduloContratos } from '../modulo';
import type { ContratoElegible, OperacionesParaPaquete, PaqueteDetalle, PaqueteResumen, RevisionPaqueteDetalle } from './api';
import Paquetes from './Paquetes';
import {
  alternarContrato,
  alternarGuia,
  alternarOperacion,
  aplicaA,
  cambiarVersion,
  compararSemver,
  quitarTodas,
  seleccionarTodas,
  solicitudDe,
  tieneAlgoQueCompartir,
  VACIO,
} from './reglas';

/** HU 4.1 (ATLAS-ADR-0041): reglas del asistente y la pantalla de paquetes con una API falsa. */
function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const AUTORA = { id: 'p1', nombre: 'Persona Demo Uno' };

const CONTRATO: ContratoElegible = {
  id: 'c1',
  codigo: 'core-ict',
  nombre: 'API de ICT · Trámites',
  tipo: 'openapi',
  aplicativo: { id: 'a1', slug: 'ict', nombre: 'ICT' },
  versiones: [
    { id: 'v13', version: '1.3.0', estado: 'vigente', retiroEn: null, publicadoEn: '2026-10-20T15:00:00Z', operaciones: 3, publicas: 2 },
    { id: 'v12', version: '1.2.0', estado: 'deprecada', retiroEn: '2027-01-31', publicadoEn: '2026-09-01T15:00:00Z', operaciones: 3, publicas: 2 },
  ],
  guias: [
    { id: 'g1', titulo: 'Firmas de webhooks', operacion: null, revisionPublicada: 2, aplicaDesde: null, aplicaHasta: null },
    { id: 'g2', titulo: 'Solo para la 1.2', operacion: null, revisionPublicada: 1, aplicaDesde: null, aplicaHasta: '1.2.9' },
  ],
};

const OPERACIONES: OperacionesParaPaquete['elementos'] = [
  { clave: 'GET /tramites/{id}', clase: 'operacion', metodo: 'GET', ruta: '/tramites/{id}', operationId: 'obtenerTramite', resumen: 'Obtener un trámite', etiquetas: ['tramites', 'public'], publica: true },
  { clave: 'DELETE /tramites/{id}', clase: 'operacion', metodo: 'DELETE', ruta: '/tramites/{id}', operationId: null, resumen: null, etiquetas: [], publica: false },
  { clave: 'POST webhook:tramiteRadicado', clase: 'webhook', metodo: 'POST', ruta: 'tramiteRadicado', operationId: null, resumen: null, etiquetas: [], publica: true },
];

const resumen = (cambios: Partial<PaqueteResumen> = {}): PaqueteResumen => ({
  id: 'p1',
  nombre: 'Paquete Integradores Demo',
  descripcion: null,
  estado: 'borrador',
  revisionActual: 1,
  revisionPublicada: null,
  cambiosSinPublicar: true,
  publicadaEn: null,
  publicadaPor: null,
  contratos: ['c1'],
  operaciones: 2,
  actualizadoEn: '2026-10-24T15:00:00Z',
  puedeGestionar: true,
  ...cambios,
});

const revision = (numero: number, vigente = false): RevisionPaqueteDetalle => ({
  numero,
  nombre: 'Paquete Integradores Demo',
  descripcion: null,
  definicion: [
    {
      contratoId: 'c1',
      codigo: 'core-ict',
      nombre: 'API de ICT · Trámites',
      tipo: 'openapi',
      visibilidad: 'compartible',
      aplicativo: { id: 'a1', slug: 'ict', nombre: 'ICT' },
      versionId: 'v13',
      version: '1.3.0',
      estadoVersion: 'vigente',
      operaciones: ['GET /tramites/{id}', 'POST webhook:tramiteRadicado'],
      guias: [{ guiaId: 'g1', titulo: 'Firmas de webhooks', revision: 2 }],
    },
  ],
  nota: numero === 1 ? 'Primera versión.' : 'Otra revisión.',
  autor: AUTORA,
  guardadaEn: '2026-10-24T15:00:00Z',
  huella: String(numero).repeat(64),
  vigente,
});

const detalle = (paquete: Partial<PaqueteResumen>, revisiones: number[], vigente: number | null): PaqueteDetalle => ({
  paquete: resumen({ revisionActual: Math.max(...revisiones), revisionPublicada: vigente, ...paquete }),
  revision: revision(Math.max(...revisiones), Math.max(...revisiones) === vigente),
  revisiones: [...revisiones]
    .sort((a, b) => b - a)
    .map((n) => ({ numero: n, nombre: 'Paquete Integradores Demo', nota: `Nota ${n}`, autor: AUTORA, guardadaEn: '2026-10-24T15:00:00Z', huella: 'a'.repeat(64), contratos: 1, operaciones: 2, guias: 1, vigente: n === vigente })),
});

function apiFalsa(inicial: PaqueteDetalle = detalle({}, [1], null)) {
  const llamadas: Array<{ metodo: string; ruta: string; cuerpo: unknown }> = [];
  let actual = inicial;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, opciones: RequestInit = {}) => {
      const metodo = (opciones.method ?? 'GET').toUpperCase();
      const cuerpo = typeof opciones.body === 'string' ? JSON.parse(opciones.body) : undefined;
      llamadas.push({ metodo, ruta, cuerpo });
      const url = new URL(ruta, 'https://atlas.ejemplo.com');
      if (metodo === 'GET' && url.pathname === '/api/v1/contratos/paquetes') return json({ puedeCrear: true, elementos: [actual.paquete] });
      if (metodo === 'GET' && url.pathname === '/api/v1/contratos/paquetes/elegibles') return json({ elementos: [CONTRATO] });
      const ops = /^\/api\/v1\/contratos\/paquetes\/versiones\/(v1[23])\/operaciones$/.exec(url.pathname);
      if (metodo === 'GET' && ops) {
        const etiqueta = url.searchParams.get('etiqueta');
        const soloPublicas = url.searchParams.get('soloPublicas') === 'true';
        const elementos = OPERACIONES.filter((o) => (!soloPublicas || o.publica) && (!etiqueta || o.etiquetas.includes(etiqueta)));
        const cuerpoOps: OperacionesParaPaquete = { contratoId: 'c1', versionId: ops[1]!, version: ops[1] === 'v13' ? '1.3.0' : '1.2.0', total: 3, etiquetas: ['public', 'tramites'], elementos };
        return json(cuerpoOps);
      }
      if (metodo === 'GET' && url.pathname === '/api/v1/contratos/paquetes/p1') return json(actual);
      if (metodo === 'GET' && url.pathname.startsWith('/api/v1/contratos/paquetes/p1/revisiones/')) return json(revision(Number(url.pathname.split('/').pop())));
      if (metodo === 'POST' && url.pathname === '/api/v1/contratos/paquetes') return json(actual, 201);
      if (metodo === 'POST' && url.pathname === '/api/v1/contratos/paquetes/p1/publicacion') {
        actual = detalle({ estado: 'publicado', cambiosSinPublicar: false }, actual.revisiones.map((r) => r.numero), actual.paquete.revisionActual);
        return json(actual);
      }
      const fijar = /^\/api\/v1\/contratos\/paquetes\/p1\/revisiones\/(\d+)\/fijacion$/.exec(url.pathname);
      if (metodo === 'POST' && fijar) {
        actual = detalle({ estado: 'publicado', cambiosSinPublicar: true }, actual.revisiones.map((r) => r.numero), Number(fijar[1]));
        return json(actual);
      }
      return new Response('', { status: 404 });
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
          <Route path="/contratos/paquetes/*" element={<Paquetes />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('reglas del asistente de paquetes', () => {
  it('compara versiones con SemVer y decide si una guía aplica', () => {
    expect(compararSemver('1.10.0', '1.9.0')).toBe(1);
    expect(compararSemver('1.0.0-rc.1', '1.0.0')).toBe(-1);
    expect(compararSemver('1.0.0-beta.2', '1.0.0-beta.11')).toBe(-1);
    expect(compararSemver('2.0.0', '2.0.0')).toBe(0);
    expect(aplicaA({ aplicaDesde: null, aplicaHasta: '1.2.9' }, '1.3.0')).toBe(false);
    expect(aplicaA({ aplicaDesde: '1.2.0', aplicaHasta: null }, '1.3.0')).toBe(true);
  });

  it('arma la selección: contrato con su última estable, operaciones, todo/ninguna sobre lo visible y la versión vacía las operaciones', () => {
    let e = alternarContrato(VACIO, CONTRATO);
    expect(e.contratos).toEqual([{ contratoId: 'c1', versionId: 'v13', operaciones: [], guias: [] }]);
    e = alternarOperacion(e, 'c1', 'GET /tramites/{id}');
    e = seleccionarTodas(e, 'c1', ['POST webhook:tramiteRadicado', 'GET /tramites/{id}']);
    expect(e.contratos[0]!.operaciones).toEqual(['GET /tramites/{id}', 'POST webhook:tramiteRadicado']);
    // «Ninguna» con un filtro solo quita lo que se ve.
    e = quitarTodas(e, 'c1', ['POST webhook:tramiteRadicado']);
    expect(e.contratos[0]!.operaciones).toEqual(['GET /tramites/{id}']);
    expect(tieneAlgoQueCompartir(e.contratos)).toBe(true);
    e = alternarGuia(e, 'c1', CONTRATO.guias[0]!);
    // Otra versión: las operaciones se vacían y la guía que aplica se queda.
    const otra = cambiarVersion(e, CONTRATO, 'v12');
    expect(otra.contratos[0]).toEqual({ contratoId: 'c1', versionId: 'v12', operaciones: [], guias: [{ guiaId: 'g1', revision: 2 }] });
    expect(tieneAlgoQueCompartir(otra.contratos)).toBe(false);
    // Al guardar, las guías van sin número (el servidor pone la publicada) y se recortan los textos.
    expect(solicitudDe({ ...e, nombre: '  Paquete  ', nota: ' Nota ' }, 3)).toEqual({
      nombre: 'Paquete',
      descripcion: null,
      definicion: [{ contratoId: 'c1', versionId: 'v13', operaciones: ['GET /tramites/{id}'], guias: [{ guiaId: 'g1', revision: null }] }],
      nota: 'Nota',
      revisionBase: 3,
    });
    expect(alternarContrato(e, CONTRATO).contratos).toEqual([]);
  });
});

describe('pantalla de paquetes', () => {
  it('el manifiesto registra la ruta y la entrada de navegación con ver_internos', () => {
    expect(moduloContratos.rutas.find((r) => r.path === '/contratos/paquetes')?.permiso).toBe('atlas.contratos.ver_internos');
    expect(moduloContratos.navegacion.some((n) => n.ruta === '/contratos/paquetes')).toBe(true);
  });

  it('asistente de 3 pasos: contrato y versión en la hoja, operaciones con filtro y seleccionar todo, guías y resumen; guarda la revisión', async () => {
    const llamadas = apiFalsa();
    montar('/contratos/paquetes');

    fireEvent.click(await screen.findByRole('link', { name: /Nuevo paquete/ }));
    // Paso 1: el contrato y su versión (hoja inferior).
    fireEvent.click(await screen.findByRole('checkbox', { name: /API de ICT/ }));
    expect(screen.getByText('1.3.0')).toBeInTheDocument();
    const cambiar = () => fireEvent.click(screen.getByRole('button', { name: 'Cambiar la versión de API de ICT · Trámites' }));
    cambiar();
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Versión de API de ICT · Trámites' })).getByRole('radio', { name: /1\.2\.0/ }));
    expect(screen.getByText('1.2.0')).toBeInTheDocument();
    expect(screen.getByText('Deprecada')).toBeInTheDocument();
    cambiar();
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Versión de API de ICT · Trámites' })).getByRole('radio', { name: /1\.3\.0/ }));

    // Paso 2: filtros que van al servidor, seleccionar todo y ninguna.
    fireEvent.click(screen.getByRole('button', { name: /Siguiente: Operaciones/ }));
    const lista = await screen.findByRole('list', { name: 'Operaciones de API de ICT · Trámites' });
    expect(within(lista).getAllByRole('checkbox')).toHaveLength(3);
    fireEvent.click(screen.getByRole('checkbox', { name: /Solo las públicas/ }));
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Operaciones de API de ICT · Trámites' })).getAllByRole('checkbox')).toHaveLength(2));
    expect(llamadas.some((l) => l.ruta === '/api/v1/contratos/paquetes/versiones/v13/operaciones?soloPublicas=true')).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Seleccionar todo (2)' }));
    expect(screen.getByText(/2 elegidas de 3/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: 'tramites' }));
    await waitFor(() => expect(llamadas.some((l) => l.ruta.includes('etiqueta=tramites&soloPublicas=true'))).toBe(true));
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Operaciones de API de ICT · Trámites' })).getAllByRole('checkbox')).toHaveLength(1));
    fireEvent.click(screen.getByRole('button', { name: 'Ninguna' }));
    expect(screen.getByText(/1 elegidas de 3/)).toBeInTheDocument();

    // Paso 3: solo las guías que aplican a la 1.3.0.
    fireEvent.click(screen.getByRole('button', { name: /Siguiente: Guías/ }));
    expect(screen.queryByText('Solo para la 1.2')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: /Firmas de webhooks/ }));

    // Resumen: nombre y nota obligatorios; guardar manda la selección.
    fireEvent.click(screen.getByRole('button', { name: /Siguiente: Resumen/ }));
    const guardar = screen.getByRole('button', { name: /Guardar revisión/ });
    expect(guardar).toBeDisabled();
    fireEvent.change(screen.getByRole('textbox', { name: 'Nombre del paquete' }), { target: { value: 'Paquete Integradores Demo' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Nota del cambio' }), { target: { value: 'Primera versión.' } });
    fireEvent.click(guardar);

    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta === '/api/v1/contratos/paquetes')).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')?.cuerpo).toEqual({
      nombre: 'Paquete Integradores Demo',
      descripcion: null,
      definicion: [{ contratoId: 'c1', versionId: 'v13', operaciones: ['POST webhook:tramiteRadicado'], guias: [{ guiaId: 'g1', revision: null }] }],
      nota: 'Primera versión.',
    });
    expect(await screen.findByText('Paquete creado en borrador (revisión 1).')).toBeInTheDocument();
  });

  it('publicar la última y fijar una anterior desde el historial (con confirmación en la hoja)', async () => {
    const llamadas = apiFalsa(detalle({ estado: 'publicado', cambiosSinPublicar: true }, [1, 2], 1));
    montar('/contratos/paquetes/p1');

    fireEvent.click(await screen.findByRole('button', { name: 'Publicar la revisión 2' }));
    expect(await screen.findByText('Publicada: la revisión 2 es la vigente.')).toBeInTheDocument();
    expect(llamadas.find((l) => l.ruta.endsWith('/publicacion'))?.cuerpo).toEqual({ revision: 2 });

    const historial = screen.getByRole('list', { name: 'Revisiones del paquete' });
    const uno = within(historial).getByRole('listitem', { name: 'Revisión 1' });
    fireEvent.click(within(uno).getByRole('button', { name: /Fijar esta/ }));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Fijar la revisión 1' })).getByRole('button', { name: /Fijar la revisión 1/ }));
    expect(await screen.findByText('Fijada: la revisión 1 es la vigente.')).toBeInTheDocument();
    expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta === '/api/v1/contratos/paquetes/p1/revisiones/1/fijacion')).toBe(true);
    expect(within(within(historial).getByRole('listitem', { name: 'Revisión 1' })).getByText('Vigente')).toBeInTheDocument();
    // Ver una revisión carga su definición.
    fireEvent.click(within(within(historial).getByRole('listitem', { name: 'Revisión 2' })).getByRole('button', { name: 'Ver' }));
    expect(await within(historial).findByText('2 operaciones')).toBeInTheDocument();
  });

  it('sin gestionar se lee, pero no hay acciones', async () => {
    apiFalsa(detalle({ estado: 'publicado', cambiosSinPublicar: true, puedeGestionar: false }, [1, 2], 1));
    montar('/contratos/paquetes/p1');

    expect(await screen.findByRole('heading', { level: 1, name: 'Paquete Integradores Demo' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Acciones del paquete' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Fijar esta/ })).not.toBeInTheDocument();
  });
});
