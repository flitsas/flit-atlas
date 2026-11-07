import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { analizarMdx, enlaceSeguro, idDeTitulo, indiceDe, pestanasDe, sinFrontMatter } from './mdx/analizar';
import Documento from './mdx/Documento';
import { moduloDocumentacion } from './modulo';
import Comparar, { compararLineas } from './paginas/Comparar';
import Lector from './paginas/Lector';

function conProveedores(ui: React.ReactNode, ruta = '/') {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[ruta]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

/** API simulada: ruta exacta → [estado, cuerpo]. Lo demás, 404. */
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

// El hub tiene sus propias pruebas (tiempoReal.test.ts): aquí no hay servidor al que conectarse.
vi.mock('./tiempoReal', () => ({ useTiempoReal: () => [] }));

afterEach(() => vi.unstubAllGlobals());

describe('enlaces del MDX', () => {
  it.each([
    ['javascript:alert(1)'],
    ['JaVaScRiPt:alert(1)'],
    ['java\tscript:alert(1)'],
    [' javascript:alert(1)'],
    ['data:text/html,<script>alert(1)</script>'],
    ['vbscript:x'],
    ['http://ejemplo.com'],
    ['//ejemplo.com/x'],
    ['/\\ejemplo.com'],
    ['https://usuario:clave@ejemplo.com'],
    ['ruta/relativa'],
    [''],
  ])('rechaza %j', (url) => expect(enlaceSeguro(url)).toBeNull());

  it('admite https, mailto, rutas internas y anclas; lo externo se marca', () => {
    expect(enlaceSeguro('https://ejemplo.com/x')).toEqual({ href: 'https://ejemplo.com/x', externo: true });
    expect(enlaceSeguro('mailto:persona@ejemplo.com')?.externo).toBe(true);
    expect(enlaceSeguro('/catalogo/flito')).toEqual({ href: '/catalogo/flito', externo: false });
    expect(enlaceSeguro('#Cómo Funciona')).toEqual({ href: '#doc-comofunciona', externo: false });
  });
});

describe('análisis del MDX', () => {
  it('el índice tiene ids únicos, con prefijo, y deja fuera lo de Pestanas', () => {
    const r = analizarMdx('# Uno\n\n## Dos\n\n## Dos\n\n#### Cuatro\n\n<Pestanas>\n\n## Dentro\n\n</Pestanas>');
    if (!r.ok) throw new Error(r.mensaje);
    expect(indiceDe(r.arbol).entradas).toEqual([
      { id: 'doc-uno', texto: 'Uno', nivel: 1 },
      { id: 'doc-dos', texto: 'Dos', nivel: 2 },
      { id: 'doc-dos-2', texto: 'Dos', nivel: 2 },
    ]);
    expect(idDeTitulo('doc-root')).toBe('doc-root');
  });

  it('cada título de Pestanas abre una pestaña', () => {
    const r = analizarMdx('<Pestanas>\n\nintro\n\n## A\n\nuno\n\n## B\n\ndos\n\n</Pestanas>');
    if (!r.ok) throw new Error(r.mensaje);
    expect(pestanasDe(r.arbol.children![0]!).map((p) => p.titulo)).toEqual(['General', 'A', 'B']);
  });

  it('quita el front-matter y deja lo demás', () => {
    expect(sinFrontMatter('---\ntitulo: x\n---\n# Hola')).toBe('# Hola');
    expect(sinFrontMatter('# Sin\n---\nfin')).toBe('# Sin\n---\nfin');
  });
});

describe('renderizador sin evaluar código (ATLAS-ADR-0053 §5)', () => {
  const pintar = (texto: string) => conProveedores(<Documento texto={texto} etiqueta="Prueba" />);

  it('pinta Markdown con tablas, código y títulos', () => {
    pintar('# Título\n\nTexto **fuerte**.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```js\nconst x = 1;\n```');
    expect(screen.getByRole('heading', { level: 1, name: 'Título' })).toHaveAttribute('id', 'doc-titulo');
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText('const x = 1;')).toBeInTheDocument();
  });

  it('no ejecuta ni crea elementos con lo que no está en la lista blanca', () => {
    const { container } = pintar(
      'export const fuga = 1\n\n<Evil onClick="x"><Contrato id="dentro" /></Evil>\n\n<script>alert(1)</script>\n\n<iframe src="https://ejemplo.com" />\n\n<img src="x" onError="alert(1)" />',
    );
    expect(container.querySelectorAll('script, iframe, img, [onclick], [onerror]')).toHaveLength(0);
    expect(screen.getByText('Componente no permitido: Evil.')).toBeInTheDocument();
    expect(screen.getByText('Componente no permitido: script.')).toBeInTheDocument();
    expect(screen.getByText(/export const fuga/)).toBeInTheDocument();
  });

  it('las expresiones quedan como texto inerte y tampoco cuentan como atributos', () => {
    const fetch = api({});
    const { container } = pintar('Hola {window.fuga = 1}\n\n<Alerta tipo={alert(1)} titulo="Ojo">cuerpo</Alerta>');
    expect((window as unknown as { fuga?: number }).fuga).toBeUndefined();
    expect(screen.getByText('{window.fuga = 1}')).toBeInTheDocument();
    expect(within(screen.getByRole('complementary', { name: 'Ojo' })).getByText('cuerpo')).toBeInTheDocument();
    expect(container.querySelector('[tipo]')).toBeNull();
    expect(fetch).toEqual([]);
  });

  it('un enlace peligroso queda como texto; uno externo abre aparte sin opener', () => {
    pintar('[malo](javascript:alert(1)) y [bueno](https://ejemplo.com/a)');
    expect(screen.queryByRole('link', { name: 'malo' })).toBeNull();
    expect(screen.getByText(/malo/)).toBeInTheDocument();
    const bueno = screen.getByRole('link', { name: 'bueno' });
    expect(bueno).toHaveAttribute('target', '_blank');
    expect(bueno).toHaveAttribute('rel', 'noopener noreferrer nofollow');
  });

  it('las imágenes no se cargan: queda su descripción', () => {
    const { container } = pintar('![Mapa del proceso](https://ejemplo.com/rastreador.png)');
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByRole('link', { name: 'Imagen: Mapa del proceso' })).toBeInTheDocument();
  });

  it('un MDX mal formado se muestra como texto, con su aviso', () => {
    pintar('<Alerta>sin cerrar');
    expect(screen.getByRole('alert')).toHaveTextContent('error de MDX');
    expect(screen.getByText('<Alerta>sin cerrar')).toBeInTheDocument();
  });

  it('Pestanas cambia de contenido', () => {
    pintar('<Pestanas>\n\n## Uno\n\nprimera\n\n## Dos\n\nsegunda\n\n</Pestanas>');
    expect(screen.getByText('primera')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Dos' }));
    expect(screen.getByText('segunda')).toBeInTheDocument();
    expect(screen.queryByText('primera')).toBeNull();
  });

  it('el diagrama Mermaid corre aislado en su iframe, sin permisos de origen', () => {
    pintar('```mermaid\ngraph TD; A-->B;\n```');
    const marco = screen.getByTitle('Diagrama del documento');
    expect(marco).toHaveAttribute('sandbox', 'allow-scripts');
    expect(marco).toHaveAttribute('src', '/visor/mermaid.html');
  });
});

describe('componentes vivos: piden sus datos con la sesión de quien lee', () => {
  const pintar = (texto: string) => conProveedores(<Documento texto={texto} etiqueta="Prueba" />);

  it('sin permiso: «No tienes acceso a este contenido»', async () => {
    api({ '/api/v1/contratos?texto=external-api': [403, {}], '/api/v1/equipo/aplicativos/flito/responsables': [403, {}], '/api/v1/soporte/este-sabado': [403, {}] });
    pintar('<Contrato id="external-api" />\n\n<Responsables aplicativo="flito" />\n\n<SoporteDeTurno />');
    await waitFor(() => expect(screen.getAllByText('No tienes acceso a este contenido.')).toHaveLength(3));
  });

  it('con permiso muestra los datos vigentes', async () => {
    api({
      '/api/v1/contratos?texto=external-api': [200, { elementos: [{ id: 'c1', codigo: 'external-api', nombre: 'API externa', ultimaVersion: { version: '1.2.0' } }] }],
      '/api/v1/equipo/aplicativos/flito/responsables': [200, { aplicativo: { nombre: 'FLITO' }, principal: { persona: { nombre: 'Persona Uno' } }, secundario: null }],
    });
    pintar('<Contrato id="external-api" operacion="GET /x" />\n\n<Responsables aplicativo="flito" />');
    expect(await screen.findByRole('link', { name: 'API externa' })).toHaveAttribute('href', '/contratos/c1');
    expect(await screen.findByText('Principal: Persona Uno')).toBeInTheDocument();
    expect(screen.getByText('Secundario: sin asignar')).toBeInTheDocument();
  });

  it('un atributo no puede cambiar la ruta que se pide', async () => {
    const llamadas = api({});
    pintar('<Responsables aplicativo="../../plataforma/yo" />');
    expect(await screen.findByText(/está mal escrito/)).toBeInTheDocument();
    expect(llamadas).toEqual([]);
  });
});

const DOCUMENTO = {
  id: 'd1',
  aplicativoId: 'a1',
  origen: 'atlas',
  tipo: 'guia_funcional',
  titulo: 'Guía de trámites',
  visibilidad: 'interno_negocio',
  audiencias: [],
  responsable: 'Área de ejemplo',
  estado: 'borrador',
  ruta: null,
  versionVigente: 1,
  revisionActual: 3,
  actualizadoEn: '2026-11-05T12:00:00Z',
};
const VERSION = {
  numero: 1,
  titulo: 'Guía de trámites',
  visibilidad: 'interno_negocio',
  esBorrador: false,
  sha256: 'a',
  repositorio: null,
  rama: null,
  commitSha: null,
  revisionOrigen: 2,
  publicadoEn: '2026-11-04T12:00:00Z',
};

describe('lector', () => {
  const montar = (ruta = '/documentacion/d1') =>
    conProveedores(
      <Routes>
        <Route path="/documentacion/:id" element={<Lector />} />
      </Routes>,
      ruta,
    );

  it('muestra el documento y envía a revisión con la última revisión', async () => {
    const detalle = { documento: DOCUMENTO, canales: [{ ambiente: 'pdn', version: 1 }], version: { version: VERSION, contenido: '# Hola' }, puedeEditar: true, puedeAprobar: false };
    const llamadas = api({
      '/api/v1/documentacion/documentos/d1': [200, detalle],
      '/api/v1/documentacion/documentos/d1/versiones': [200, [VERSION]],
      'POST /api/v1/documentacion/documentos/d1/envio-revision': [200, detalle],
    });
    montar();
    expect(await screen.findByRole('heading', { level: 1, name: 'Guía de trámites' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Versión 1 · Producción' })).toHaveAttribute('aria-current', 'page');
    fireEvent.click(screen.getByRole('button', { name: 'Enviar a revisión' }));
    expect(await screen.findByText('Enviado a revisión.')).toBeInTheDocument();
    expect(llamadas.find((l) => l.metodo === 'POST')?.cuerpo).toEqual({ revision: 3 });
  });

  it('el detalle de la lista blanca de MDX llega a la persona (422)', async () => {
    const detalle = { documento: DOCUMENTO, canales: [], version: null, puedeEditar: true, puedeAprobar: false };
    api({
      '/api/v1/documentacion/documentos/d1': [200, detalle],
      '/api/v1/documentacion/documentos/d1/versiones': [200, []],
      'POST /api/v1/documentacion/documentos/d1/envio-revision': [422, { codigo: 'mdx_invalido', mensaje: 'El MDX no pasa la lista blanca.', errores: ['Línea 3: la etiqueta <Evil>.'] }],
    });
    montar();
    fireEvent.click(await screen.findByRole('button', { name: 'Enviar a revisión' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Línea 3: la etiqueta <Evil>.');
  });

  it('un documento de repo no ofrece editar; sin permisos no hay acciones', async () => {
    api({
      '/api/v1/documentacion/documentos/d1': [200, { documento: { ...DOCUMENTO, origen: 'repo', estado: 'publicado' }, canales: [], version: { version: VERSION, contenido: 'x' }, puedeEditar: false, puedeAprobar: false }],
      '/api/v1/documentacion/documentos/d1/versiones': [200, [VERSION]],
    });
    montar();
    await screen.findByText('Desde su repo');
    expect(screen.queryByRole('group', { name: 'Acciones del documento' })).toBeNull();
  });

  it('404: no revela si existe', async () => {
    api({});
    montar();
    expect(await screen.findByRole('heading', { name: 'Este documento no está disponible' })).toBeInTheDocument();
  });
});

describe('comparación', () => {
  it('compararLineas marca lo agregado y lo quitado', () => {
    expect(compararLineas('a\nb\n', 'a\nc\n')).toEqual([
      { tipo: 'igual', texto: 'a' },
      { tipo: 'quitada', texto: 'b' },
      { tipo: 'agregada', texto: 'c' },
    ]);
  });

  it('compara por defecto las dos últimas versiones', async () => {
    api({
      '/api/v1/documentacion/documentos/d1/versiones': [200, [{ ...VERSION, numero: 2, sha256: 'b' }, VERSION]],
      '/api/v1/documentacion/documentos/d1/versiones/1': [200, { version: VERSION, contenido: 'uno\n' }],
      '/api/v1/documentacion/documentos/d1/versiones/2': [200, { version: { ...VERSION, numero: 2, sha256: 'b' }, contenido: 'dos\n' }],
    });
    conProveedores(
      <Routes>
        <Route path="/documentacion/:id/comparar" element={<Comparar />} />
      </Routes>,
      '/documentacion/d1/comparar',
    );
    expect(await screen.findByText('1 línea agregada y 1 quitada.')).toBeInTheDocument();
  });
});

describe('manifiesto', () => {
  it('disponible, con su pestaña en la ficha y todo diferido', () => {
    expect(moduloDocumentacion.estado).toBe('disponible');
    expect(moduloDocumentacion.pestanasFicha?.map((p) => p.id)).toEqual(['documentos']);
    expect(moduloDocumentacion.rutas.every((r) => r.cargar.toString().includes('import'))).toBe(true);
  });
});
