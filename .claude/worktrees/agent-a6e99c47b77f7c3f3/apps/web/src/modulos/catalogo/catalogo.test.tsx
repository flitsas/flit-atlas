import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from '../../app/api';
import { App } from '../../app/App';
import type { AplicativoDeFicha, ModuloAtlas } from '../../app/modulos/contrato';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { resolverNavegacion } from '../../app/modulos/resolver';
import type { AplicativoResumen, FichaAplicativo } from './api';
import { slugDe, tiposHijo } from './Formulario';
import { moduloCatalogo } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const resumen = (parcial: Partial<AplicativoResumen>): AplicativoResumen => ({
  id: 'a',
  slug: 'a',
  nombre: 'A',
  tipo: 'producto',
  grupo: 'flit2',
  estado: 'activo',
  criticidad: 'media',
  manejaDatosPersonales: false,
  padreId: null,
  descripcion: null,
  tecnologias: [],
  etiquetas: [],
  ancestros: [],
  hijos: 0,
  ...parcial,
});

const FLIT2 = resumen({ id: 'p1', slug: 'flit2', nombre: 'FLIT 2.0', hijos: 1, tecnologias: ['.NET 10'] });
const TRAMITES = resumen({
  id: 'm1',
  slug: 'flit2-tramites',
  nombre: 'Trámites',
  tipo: 'modulo',
  padreId: 'p1',
  manejaDatosPersonales: true,
  ancestros: [{ id: 'p1', slug: 'flit2', nombre: 'FLIT 2.0' }],
});
const SENTINEL = resumen({ id: 'p2', slug: 'sentinel', nombre: 'Sentinel', grupo: 'satelite', descripcion: '[por verificar] Confirmar con el líder.' });
const LEGADO = resumen({ id: 'p3', slug: 'legados-v1', nombre: 'FLIT V1 (legados)', grupo: 'legado', estado: 'en_retiro', criticidad: 'alta' });

const ficha = (base: AplicativoResumen, permisos = { editar: false, gestionar: false }, hijos: AplicativoResumen[] = []): FichaAplicativo => ({
  ...base,
  repo: 'flitsas/ejemplo',
  rutaEnRepo: 'services/demo',
  ambientes: ['qa', 'pdn'],
  hijos,
  permisos,
  creadoEn: '2026-10-23T12:00:00Z',
  actualizadoEn: '2026-10-23T12:00:00Z',
});

function apiFalsa(opciones: { permisos: string[]; fichaPermisos?: { editar: boolean; gestionar: boolean } }) {
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      llamadas.push({ ruta, init });
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: 'p',
          nombre: 'Persona Demo',
          tipoPrincipal: 'empleado',
          organizacionId: 'o',
          proveedor: 'simulado',
          roles: ['atlas.lector'],
          permisos: opciones.permisos,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta.startsWith('/api/v1/catalogo/aplicativos/') && init?.method === 'PUT') {
        const cuerpo = JSON.parse(String(init.body)) as { nombre: string; descripcion: string | null };
        return json(ficha({ ...SENTINEL, nombre: cuerpo.nombre, descripcion: cuerpo.descripcion }, opciones.fichaPermisos));
      }
      if (ruta === '/api/v1/catalogo/aplicativos/sentinel') return json(ficha(SENTINEL, opciones.fichaPermisos));
      if (ruta === '/api/v1/catalogo/aplicativos/flit2') return json(ficha(FLIT2, opciones.fichaPermisos, [TRAMITES]));
      if (ruta.startsWith('/api/v1/catalogo/aplicativos/')) return json({}, 404);
      if (ruta.startsWith('/api/v1/catalogo/aplicativos')) {
        const parametros = new URL(ruta, 'http://x').searchParams;
        const todos = [FLIT2, TRAMITES, SENTINEL, LEGADO];
        const elementos = todos.filter(
          (a) =>
            (!parametros.get('grupo') || a.grupo === parametros.get('grupo')) &&
            (!parametros.get('texto') || a.nombre.toLowerCase().includes(parametros.get('texto')!.toLowerCase())),
        );
        return json({ total: elementos.length, elementos });
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

describe('manifiesto del catálogo', () => {
  it('ya está disponible, encendido por defecto y con su página y su widget diferidos', () => {
    expect(moduloCatalogo.estado).toBe('disponible');
    expect(moduloCatalogo.banderaPorDefecto).toBe(true);
    expect(moduloCatalogo.rutas.map((r) => [r.path, r.permiso])).toEqual([['/catalogo', 'atlas.catalogo.ver']]);
    expect(moduloCatalogo.rutas[0]?.cargar.toString()).toMatch(/import/);
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.catalogo.ver'] });
    expect(nav.widgets.map((w) => w.id)).toContain('catalogo-aplicativos');
  });

  it('el slug sugerido no tiene tildes ni espacios y los hijos siguen la jerarquía', () => {
    expect(slugDe('flit2 Doctor FLIT')).toBe('flit2-doctor-flit');
    expect(slugDe('  Trámites / Ñandú  ')).toBe('tramites-nandu');
    expect(tiposHijo(null)).toEqual(['producto']);
    expect(tiposHijo(ficha(FLIT2))).toEqual(['modulo', 'servicio']);
    expect(tiposHijo(ficha(TRAMITES))).toEqual(['servicio']);
    expect(tiposHijo(ficha(resumen({ tipo: 'servicio' })))).toEqual([]);
  });
});

describe('listado del catálogo', () => {
  it('agrupa por grupo, muestra la ruta en la jerarquía y las insignias, y sin gestionar no ofrece crear', async () => {
    apiFalsa({ permisos: ['atlas.catalogo.ver'] });
    window.history.pushState({}, '', '/catalogo');

    render(<App />);

    const flit2 = await screen.findByRole('region', { name: /FLIT 2\.0/ });
    expect(within(flit2).getByText('Trámites')).toBeInTheDocument();
    expect(within(flit2).getByText('FLIT 2.0', { selector: 'span.text-xs' })).toBeInTheDocument();
    expect(within(flit2).getByText('Datos personales')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /Satélites/ })).toHaveTextContent('Por verificar');
    expect(screen.getByRole('region', { name: /Legados/ })).toHaveTextContent('Criticidad alta');
    expect(screen.getByText('4 aplicativos')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Nuevo producto/ })).toBeNull();
  });

  it('los filtros y la búsqueda van al servidor y quedan en la URL', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.catalogo.ver', 'atlas.catalogo.gestionar'] });
    window.history.pushState({}, '', '/catalogo');

    render(<App />);
    await screen.findByText('4 aplicativos');
    expect(screen.getByRole('button', { name: /Nuevo producto/ })).toBeInTheDocument();

    fireEvent.click(within(screen.getByRole('group', { name: 'Grupo' })).getByLabelText('Legados'));
    expect(await screen.findByText('1 aplicativo')).toBeInTheDocument();
    expect(llamadas.some((l) => l.ruta === '/api/v1/catalogo/aplicativos?grupo=legado')).toBe(true);
    expect(window.location.search).toBe('?grupo=legado');

    fireEvent.click(within(screen.getByRole('group', { name: 'Grupo' })).getByLabelText('Todos'));
    fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar en el catálogo' }), { target: { value: 'trám' } });
    await waitFor(() => expect(llamadas.some((l) => l.ruta === '/api/v1/catalogo/aplicativos?texto=tr%C3%A1m')).toBe(true));
    expect(await screen.findByText('1 aplicativo')).toBeInTheDocument();
  });
});

describe('ficha del aplicativo', () => {
  it('muestra el resumen con la marca «[por verificar]» y los espacios de los demás módulos como próximamente', async () => {
    apiFalsa({ permisos: ['atlas.catalogo.ver'] });
    window.history.pushState({}, '', '/catalogo/sentinel');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Sentinel' })).toBeInTheDocument();
    expect(screen.getByText('[por verificar]', { selector: 'mark' })).toBeInTheDocument();
    const pestanas = screen.getByRole('tablist', { name: 'Secciones de la ficha' });
    expect(within(pestanas).getAllByRole('tab').map((t) => t.textContent)).toEqual([
      'Resumen',
      'Responsables',
      'Soporte de turno',
      'Contratos',
      'Versión por ambiente',
      'Documentos',
      'Diagramas',
      'Casos de IA',
    ]);
    expect(screen.queryByRole('button', { name: /Editar/ })).toBeNull();

    fireEvent.click(within(pestanas).getByRole('tab', { name: /Contratos/ }));
    expect(await screen.findByText(/Llega con el módulo Contratos de integración \(fase F3\)/)).toBeInTheDocument();
    expect(window.location.search).toBe('?pestana=contratos');
  });

  it('un módulo registra su pestaña sin que el catálogo lo importe y recibe el aplicativo', async () => {
    apiFalsa({ permisos: ['atlas.catalogo.ver', 'atlas.equipo.ver'] });
    const recibido: AplicativoDeFicha[] = [];
    const equipoFalso: ModuloAtlas = {
      id: 'equipo_prueba',
      nombre: 'Equipo de prueba',
      descripcion: 'Aporta la pestaña de responsables.',
      icono: 'equipo',
      estado: 'disponible',
      rutas: [],
      navegacion: [],
      pestanasFicha: [
        {
          id: 'responsables',
          titulo: 'Responsables',
          orden: 10,
          permiso: 'atlas.equipo.ver',
          cargar: async () => ({
            default: ({ aplicativo }: { aplicativo: AplicativoDeFicha }) => {
              recibido.push(aplicativo);
              return <p>Responsables de {aplicativo.nombre}</p>;
            },
          }),
        },
      ],
      bandera: 'modulo.equipo_prueba',
      banderaPorDefecto: true,
    };
    window.history.pushState({}, '', '/catalogo/flit2?pestana=responsables');

    render(<App modulos={[...MODULOS_REGISTRADOS, equipoFalso]} />);

    expect(await screen.findByText('Responsables de FLIT 2.0')).toBeInTheDocument();
    expect(recibido[0]).toEqual({ id: 'p1', slug: 'flit2', nombre: 'FLIT 2.0', tipo: 'producto', grupo: 'flit2' });
    fireEvent.click(screen.getByRole('tab', { name: 'Resumen' }));
    expect(await screen.findByRole('link', { name: /Trámites/ })).toHaveAttribute('href', '/catalogo/flit2-tramites');
  });

  it('con editar guarda los cambios con PUT y la cabecera antifalsificación', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.catalogo.ver', 'atlas.catalogo.editar'], fichaPermisos: { editar: true, gestionar: false } });
    window.history.pushState({}, '', '/catalogo/sentinel');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: /Editar/ }));
    const formulario = await screen.findByRole('form', { name: 'Datos del aplicativo' });
    // Sin gestionar, el slug no se cambia.
    expect(within(formulario).getByLabelText(/^Slug/)).toHaveAttribute('readonly');
    fireEvent.change(within(formulario).getByLabelText('Descripción'), { target: { value: 'Monitoreo de FLIT.' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument();
    const envio = llamadas.find((l) => l.init?.method === 'PUT');
    expect(envio?.ruta).toBe('/api/v1/catalogo/aplicativos/p2');
    expect(new Headers(envio?.init?.headers).get(CABECERA_CSRF)).toBe('1');
    expect(JSON.parse(String(envio?.init?.body))).toMatchObject({ slug: 'sentinel', tipo: 'producto', grupo: 'satelite', descripcion: 'Monitoreo de FLIT.', ambientes: ['qa', 'pdn'] });
  });

  it('un slug que no existe muestra «Esta página no existe»', async () => {
    apiFalsa({ permisos: ['atlas.catalogo.ver'] });
    window.history.pushState({}, '', '/catalogo/no-existe');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Esta página no existe' })).toBeInTheDocument();
  });
});
