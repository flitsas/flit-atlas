import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { leerLienzo } from '../../app/diagramas/lienzo';
import { LienzoSvg } from '../../app/diagramas/LienzoSvg';
import { esMensajeVisorBpmn, esMensajeXmlBpmn } from '../../app/diagramas/mensajesBpmn';
import { VistaDiagrama } from '../../app/diagramas/VistaDiagrama';
import { moduloDiagramas } from './modulo';
import Detalle from './paginas/Detalle';

function conProveedores(ui: React.ReactNode, ruta = '/') {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[ruta]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

function api(rutas: Record<string, [number, unknown]>) {
  const llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (entrada: string, opciones?: RequestInit) => {
      llamadas.push({ ruta: entrada, metodo: opciones?.method ?? 'GET', cuerpo: opciones?.body ? JSON.parse(opciones.body as string) : undefined });
      const r = rutas[`${opciones?.method ?? 'GET'} ${entrada}`] ?? rutas[entrada];
      return new Response(JSON.stringify(r?.[1] ?? {}), { status: r?.[0] ?? 404, headers: { 'Content-Type': 'application/json' } });
    }),
  );
  return llamadas;
}

afterEach(() => vi.unstubAllGlobals());

const VERSION = {
  numero: 2,
  origen: 'edicion',
  restauradaDe: null,
  titulo: 'Flujo',
  visibilidad: 'interno_negocio',
  nota: 'Ajuste',
  autor: { id: 'p1', nombre: 'Persona Demo' },
  guardadaEn: '2026-11-06T12:00:00Z',
  huella: 'h2',
};
const DIAGRAMA = { id: 'd1', codigo: 'flujo', aplicativoId: 'a1', tipo: 'mermaid', titulo: 'Flujo', visibilidad: 'interno_negocio', versionActual: 2, actualizadoEn: '2026-11-06T12:00:00Z' };

const pintarDetalle = () =>
  conProveedores(
    <Routes>
      <Route path="/diagramas/:id" element={<Detalle />} />
    </Routes>,
    '/diagramas/d1',
  );

describe('manifiesto', () => {
  it('declara rutas diferidas con el permiso de leer, la bandera encendida y la pestaña de la ficha', () => {
    expect(moduloDiagramas.bandera).toBe('modulo.diagramas');
    expect(moduloDiagramas.banderaPorDefecto).toBe(true);
    expect(moduloDiagramas.rutas.every((r) => r.permiso === 'atlas.docs.ver')).toBe(true);
    expect(moduloDiagramas.pestanasFicha?.[0]?.id).toBe('diagramas');
  });
});

describe('lectura por tipo', () => {
  const LIENZO = JSON.stringify({
    nodos: [
      { id: 'a', tipo: 'sistema', etiqueta: 'Radicación', x: 0, y: 0 },
      { id: 'b', tipo: 'actor', etiqueta: '<img src=x onerror=alert(1)>', x: 300, y: 0 },
    ],
    relaciones: [{ id: 'r', origen: 'a', destino: 'b', etiqueta: 'usa' }],
  });

  it('Mermaid y BPMN corren aislados en su iframe, sin permisos de origen', () => {
    const { unmount } = conProveedores(<VistaDiagrama tipo="mermaid" fuente="graph TD; A-->B;" titulo="Flujo" />);
    expect(screen.getByTitle('Diagrama del documento')).toHaveAttribute('sandbox', 'allow-scripts');
    unmount();
    conProveedores(<VistaDiagrama tipo="bpmn" fuente="<bpmn:definitions/>" titulo="Proceso" />);
    const marco = screen.getByTitle('Proceso');
    expect(marco).toHaveAttribute('sandbox', 'allow-scripts');
    expect(marco).toHaveAttribute('src', '/visor/bpmn.html');
  });

  it('el lienzo se lee como SVG propio y el texto nunca se interpreta como HTML', () => {
    const { container } = conProveedores(<VistaDiagrama tipo="lienzo" fuente={LIENZO} titulo="Lienzo" />);
    expect(screen.getByRole('img', { name: 'Lienzo' })).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
    expect(screen.getAllByText('Radicación').length).toBeGreaterThan(0);
    expect(container.querySelector('[style]')).toBeNull();
  });

  it('una fuente que no es un lienzo se avisa', () => {
    conProveedores(<VistaDiagrama tipo="lienzo" fuente="{no" titulo="Lienzo" />);
    expect(screen.getByText('No pudimos leer este lienzo.')).toBeInTheDocument();
    expect(leerLienzo('{"nodos":1}')).toBeNull();
  });

  it('una relación con un extremo que no existe no rompe el dibujo', () => {
    conProveedores(<LienzoSvg lienzo={{ nodos: [{ id: 'a', tipo: 'nota', etiqueta: 'A', x: 0, y: 0 }], relaciones: [{ id: 'r', origen: 'a', destino: 'z' }] }} titulo="L" />);
    expect(screen.getByRole('img', { name: 'L' })).toBeInTheDocument();
  });
});

describe('mensajes del visor BPMN', () => {
  it('solo acepta las formas conocidas y dentro del tope', () => {
    expect(esMensajeVisorBpmn({ tipo: 'atlas:bpmn-listo' })).toBe(true);
    expect(esMensajeVisorBpmn({ tipo: 'atlas:bpmn-cambio', xml: '<a/>' })).toBe(true);
    expect(esMensajeVisorBpmn({ tipo: 'atlas:bpmn-cambio', xml: 1 })).toBe(false);
    expect(esMensajeVisorBpmn({ tipo: 'otro' })).toBe(false);
    expect(esMensajeVisorBpmn(null)).toBe(false);
    expect(esMensajeXmlBpmn({ tipo: 'atlas:bpmn-xml', xml: 'x'.repeat(1_000_001), editable: false })).toBe(false);
    expect(esMensajeXmlBpmn({ tipo: 'atlas:bpmn-xml', xml: '<a/>', editable: true })).toBe(true);
  });
});

describe('detalle', () => {
  const rutas = (puedeEditar: boolean): Record<string, [number, unknown]> => ({
    '/api/v1/diagramas/d1': [200, { diagrama: DIAGRAMA, version: { version: VERSION, fuente: 'graph TD; A-->B;' }, referencias: [{ aplicativoId: 'a2', slug: 'flito', nombre: 'FLITO' }], puedeEditar }],
    '/api/v1/diagramas/d1/versiones': [200, [VERSION, { ...VERSION, numero: 1, origen: 'creacion', nota: 'Primera', huella: 'h1' }]],
    '/api/v1/diagramas/d1/versiones/1': [200, { version: { ...VERSION, numero: 1, origen: 'creacion' }, fuente: 'graph TD; C-->D;' }],
    '/api/v1/diagramas/d1/comentarios': [200, [{ id: 'c1', versionNumero: 2, nodoClave: null, persona: { id: 'p1', nombre: 'Persona Demo' }, texto: 'Falta un paso', escritoEn: '2026-11-06T12:00:00Z' }]],
  });

  it('solo el lienzo ofrece el SVG; si atlas-render no está disponible lo explica y el diagrama sigue ahí', async () => {
    const lienzo = JSON.stringify({ nodos: [{ id: 'a', tipo: 'sistema', etiqueta: 'Trámites', x: 0, y: 0 }], relaciones: [] });
    const base = rutas(false);
    const llamadas = api({
      ...base,
      '/api/v1/diagramas/d1': [200, { diagrama: { ...DIAGRAMA, tipo: 'lienzo' }, version: { version: VERSION, fuente: lienzo }, referencias: [], puedeEditar: false }],
      '/api/v1/diagramas/d1/versiones/2/svg': [503, { codigo: 'render_no_disponible', mensaje: 'La exportación a SVG no está disponible en este momento.' }],
    });
    pintarDetalle();

    fireEvent.click(await screen.findByRole('button', { name: 'Descargar SVG' }));

    expect(await screen.findByText('La exportación a SVG no está disponible en este momento.')).toBeInTheDocument();
    expect(llamadas.some((l) => l.ruta === '/api/v1/diagramas/d1/versiones/2/svg')).toBe(true);
    expect(screen.getByRole('img', { name: 'Flujo' })).toBeInTheDocument();
  });

  it('un Mermaid no ofrece el SVG', async () => {
    api(rutas(false));
    pintarDetalle();
    await screen.findByText('Falta un paso');
    expect(screen.queryByRole('button', { name: 'Descargar SVG' })).toBeNull();
  });

  it('muestra versiones, comentarios, referencias y el código para citarlo; sin permiso no ofrece editar ni restaurar', async () => {
    api(rutas(false));
    pintarDetalle();
    expect(await screen.findByRole('heading', { name: 'Flujo' })).toBeInTheDocument();
    expect(screen.getByText('<Diagrama id="flujo" />')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'FLITO' })).toHaveAttribute('href', '/catalogo/flito');
    expect(await screen.findByText('Falta un paso')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Editar' })).toBeNull();
    fireEvent.click(await screen.findByRole('button', { name: 'Ver la 1' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ver la 1' })).toHaveAttribute('aria-pressed', 'true'));
    expect(screen.queryByRole('form', { name: /Restaurar/ })).toBeNull();
  });

  it('con permiso restaura pidiendo la nota y mandando la última versión que vio', async () => {
    const llamadas = api({ ...rutas(true), 'POST /api/v1/diagramas/d1/versiones/1/restauracion': [201, {}] });
    pintarDetalle();
    expect(await screen.findByRole('link', { name: 'Editar' })).toHaveAttribute('href', '/diagramas/d1/editar');
    fireEvent.click(await screen.findByRole('button', { name: 'Ver la 1' }));
    const formulario = await screen.findByRole('form', { name: 'Restaurar la versión 1' });
    fireEvent.change(screen.getByLabelText('Nota del cambio'), { target: { value: 'Volver' } });
    fireEvent.submit(formulario);
    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta.endsWith('/restauracion'))).toBe(true));
    expect(llamadas.find((l) => l.ruta.endsWith('/restauracion'))?.cuerpo).toEqual({ nota: 'Volver', versionBase: 2 });
  });

  it('lo que no existe o no se puede ver se dice igual', async () => {
    api({ '/api/v1/diagramas/d1': [404, { codigo: 'no_encontrado' }] });
    pintarDetalle();
    expect(await screen.findByRole('alert')).toHaveTextContent('no existe o no tienes acceso');
  });

  it('comentar manda la versión que se está viendo', async () => {
    const llamadas = api({ ...rutas(false), 'POST /api/v1/diagramas/d1/comentarios': [201, {}] });
    pintarDetalle();
    const formulario = await screen.findByRole('form', { name: 'Escribir un comentario' });
    fireEvent.change(screen.getByLabelText(/Comentario sobre la versión 2/), { target: { value: 'Revisar' } });
    fireEvent.submit(formulario);
    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta.endsWith('/comentarios'))).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')?.cuerpo).toEqual({ versionNumero: 2, texto: 'Revisar' });
  });
});
