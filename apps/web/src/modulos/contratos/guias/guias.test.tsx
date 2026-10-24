import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from '../../../app/api';
import { moduloContratos } from '../modulo';
import type { GuiaDetalle, GuiaResumen, ListadoGuias, RevisionDetalle } from './api';
import { compararLineas } from './Comparacion';
import GuiasDelContrato from './GuiasDelContrato';
import { markdownSeguro } from './Markdown';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const AUTORA = { id: 'p1', nombre: 'Persona Demo Uno' };

const guia = (cambios: Partial<GuiaResumen> = {}): GuiaResumen => ({
  id: 'g1',
  contratoId: 'c1',
  titulo: 'Cómo autenticarse',
  operacion: null,
  aplicaDesde: '1.0.0',
  aplicaHasta: null,
  visibilidad: 'interna',
  estado: 'borrador',
  revisionActual: 2,
  revisionPublicada: null,
  publicadaEn: null,
  publicadaPor: null,
  aprobadaPor: null,
  actualizadaEn: '2026-10-23T15:00:00Z',
  ...cambios,
});

const revision = (numero: number, contenido: string, cambios: Partial<RevisionDetalle> = {}): RevisionDetalle => ({
  numero,
  origen: numero === 1 ? 'creacion' : 'edicion',
  restauradaDe: null,
  titulo: 'Cómo autenticarse',
  contenido,
  operacion: null,
  aplicaDesde: '1.0.0',
  aplicaHasta: null,
  visibilidad: 'interna',
  nota: numero === 1 ? 'Primera versión.' : 'Renovación del token.',
  autor: AUTORA,
  guardadaEn: '2026-10-23T15:00:00Z',
  huella: String(numero).repeat(64),
  publicada: false,
  ...cambios,
});

const UNO = revision(1, '# Autenticación\n\nPide un token.\n');
const DOS = revision(2, '# Autenticación\n\nPide un token y renuévalo cada hora.\n');

interface Escenario {
  listado?: Partial<ListadoGuias>;
  detalle?: Partial<GuiaDetalle>;
}

function apiFalsa({ listado = {}, detalle = {} }: Escenario = {}) {
  const llamadas: Array<{ metodo: string; ruta: string; cuerpo: unknown; csrf: string | null }> = [];
  const lista: ListadoGuias = { contratoId: 'c1', puedeEditar: true, puedeAprobar: false, elementos: [guia()], ...listado };
  const unaGuia: GuiaDetalle = { guia: guia(), revision: DOS, puedeEditar: lista.puedeEditar, puedeAprobar: lista.puedeAprobar, ...detalle };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, opciones: RequestInit = {}) => {
      const metodo = (opciones.method ?? 'GET').toUpperCase();
      const cuerpo = typeof opciones.body === 'string' ? JSON.parse(opciones.body) : undefined;
      llamadas.push({ metodo, ruta, cuerpo, csrf: new Headers(opciones.headers).get(CABECERA_CSRF) });
      if (metodo === 'GET' && ruta.startsWith('/api/v1/contratos/c1/guias')) return json(lista);
      if (metodo === 'GET' && ruta === '/api/v1/contratos/guias/g1') return json(unaGuia);
      if (metodo === 'GET' && ruta === '/api/v1/contratos/guias/g1/revisiones') return json([DOS, UNO]);
      if (metodo === 'GET' && ruta === '/api/v1/contratos/guias/g1/revisiones/1') return json(UNO);
      if (metodo === 'GET' && ruta === '/api/v1/contratos/guias/g1/revisiones/2') return json(DOS);
      if (metodo === 'POST' && ruta === '/api/v1/contratos/c1/guias') return json({ ...unaGuia, guia: guia({ id: 'g1', revisionActual: 1 }), revision: UNO }, 201);
      if (metodo === 'POST' && ruta.endsWith('/restauracion'))
        return json({ ...unaGuia, guia: guia({ revisionActual: 3 }), revision: revision(3, UNO.contenido, { origen: 'restauracion', restauradaDe: 1 }) }, 201);
      if (metodo === 'POST' && ruta.endsWith('/publicacion')) return json({ ...unaGuia, guia: guia({ estado: 'publicada', revisionPublicada: 2 }) });
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

function montar(props: { contratoId: string; version?: string; guiaId?: string }) {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={cliente}>
      <GuiasDelContrato {...props} />
    </QueryClientProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('Markdown de las guías', () => {
  it('no interpreta HTML, quita scripts, eventos, imágenes y enlaces javascript:, y aísla los enlaces externos', () => {
    const html = markdownSeguro(
      [
        '# Título',
        '<script>alert(1)</script>',
        '<div onclick="alert(2)" class="fixed inset-0">capa</div>',
        '![img](https://ejemplo.com/rastreo.png)',
        '[malo](javascript:alert(3)) [bueno](https://ejemplo.com/docs) [ancla](#paso-2)',
        '| a | b |\n|---|---|\n| 1 | 2 |',
      ].join('\n\n'),
    );
    const contenedor = document.createElement('div');
    contenedor.innerHTML = html;

    expect(contenedor.querySelector('h1')?.textContent).toBe('Título');
    expect(contenedor.querySelector('script, img, div, [onclick], [class], [style]')).toBeNull();
    // El HTML del texto queda como texto visible, no como elementos.
    expect(contenedor.textContent).toContain('<script>alert(1)</script>');
    const enlaces = [...contenedor.querySelectorAll('a')];
    expect(enlaces.find((a) => a.textContent === 'malo')?.hasAttribute('href')).toBe(false);
    const bueno = enlaces.find((a) => a.textContent === 'bueno')!;
    expect([bueno.getAttribute('href'), bueno.getAttribute('target'), bueno.getAttribute('rel')]).toEqual([
      'https://ejemplo.com/docs',
      '_blank',
      'noopener noreferrer nofollow',
    ]);
    expect(enlaces.find((a) => a.textContent === 'ancla')?.hasAttribute('target')).toBe(false);
    expect(contenedor.querySelectorAll('td')).toHaveLength(2);
  });
});

describe('comparación de revisiones', () => {
  it('marca las líneas agregadas y quitadas', () => {
    expect(compararLineas('a\nb\nc\n', 'a\nB\nc\nd\n')).toEqual([
      { tipo: 'igual', texto: 'a' },
      { tipo: 'quitada', texto: 'b' },
      { tipo: 'agregada', texto: 'B' },
      { tipo: 'igual', texto: 'c' },
      { tipo: 'agregada', texto: 'd' },
    ]);
  });
});

describe('GuiasDelContrato', () => {
  it('es el export por defecto de la ruta que monta la pestaña «Guías» y tiene su enlace directo', async () => {
    const modulo = await import('./GuiasDelContrato');
    expect(modulo.default).toBe(GuiasDelContrato);
    expect(moduloContratos.rutas.map((r) => r.path)).toContain('/contratos/:contratoId/guias/:guiaId');
  });

  it('lista las guías (filtradas por versión) y abre una con su Markdown renderizado', async () => {
    const llamadas = apiFalsa();
    montar({ contratoId: 'c1', version: '1.2.0' });

    fireEvent.click(await screen.findByRole('button', { name: /Cómo autenticarse/ }));
    expect(llamadas.some((l) => l.ruta === '/api/v1/contratos/c1/guias?version=1.2.0')).toBe(true);
    const contenido = await screen.findByRole('article', { name: 'Contenido de Cómo autenticarse' });
    expect(within(contenido).getByRole('heading', { name: 'Autenticación' })).toBeInTheDocument();
    expect(within(contenido).getByText('Pide un token y renuévalo cada hora.')).toBeInTheDocument();
    // Interna y quien edita: puede publicarla.
    expect(screen.getByRole('button', { name: 'Publicar' })).toBeInTheDocument();
  });

  it('crea una guía con el editor y su vista previa, con la cabecera antifalsificación', async () => {
    const llamadas = apiFalsa();
    montar({ contratoId: 'c1' });

    fireEvent.click(await screen.findByRole('button', { name: 'Nueva guía' }));
    const formulario = await screen.findByRole('form', { name: 'Nueva guía' });
    fireEvent.change(within(formulario).getByLabelText('Título'), { target: { value: 'Firmas de webhooks' } });
    fireEvent.change(within(formulario).getByLabelText(/Cuerpo en Markdown/), { target: { value: '## Firma\n\nValida el HMAC.' } });
    fireEvent.click(within(formulario).getByRole('radio', { name: 'Compartible con clientes' }));
    fireEvent.change(within(formulario).getByLabelText('Nota del cambio'), { target: { value: 'Primera versión.' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Vista previa' }));
    expect(await within(formulario).findByRole('heading', { name: 'Firma' })).toBeInTheDocument();
    fireEvent.click(within(formulario).getByRole('button', { name: 'Guardar revisión' }));

    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST')).toBe(true));
    const post = llamadas.find((l) => l.metodo === 'POST')!;
    expect(post.ruta).toBe('/api/v1/contratos/c1/guias');
    expect(post.csrf).toBe('1');
    expect(post.cuerpo).toEqual({
      titulo: 'Firmas de webhooks',
      contenido: '## Firma\n\nValida el HMAC.',
      operacion: null,
      aplicaDesde: null,
      aplicaHasta: null,
      visibilidad: 'compartible',
      nota: 'Primera versión.',
    });
    expect(await screen.findByText('Guía creada en borrador (revisión 1).')).toBeInTheDocument();
  });

  it('sin permiso de edición no ofrece crear, editar ni publicar', async () => {
    apiFalsa({ listado: { puedeEditar: false } });
    montar({ contratoId: 'c1', guiaId: 'g1' });

    expect(await screen.findByRole('heading', { level: 2, name: 'Cómo autenticarse' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Publicar' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /Todas las guías/ }));
    await screen.findByRole('button', { name: /Cómo autenticarse/ });
    expect(screen.queryByRole('button', { name: 'Nueva guía' })).toBeNull();
  });

  it('una compartible solo la aprueba el responsable; a los demás les dice que está pendiente', async () => {
    const compartible = { guia: guia({ visibilidad: 'compartible' }), revision: revision(2, DOS.contenido, { visibilidad: 'compartible' }) };
    apiFalsa({ detalle: { ...compartible, puedeEditar: true, puedeAprobar: false } });
    const { unmount } = montar({ contratoId: 'c1', guiaId: 'g1' });
    expect(await screen.findByText(/Pendiente de aprobación/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aprobar y publicar' })).toBeNull();
    unmount();

    const llamadas = apiFalsa({ detalle: { ...compartible, puedeEditar: true, puedeAprobar: true } });
    montar({ contratoId: 'c1', guiaId: 'g1' });
    fireEvent.click(await screen.findByRole('button', { name: 'Aprobar y publicar' }));
    await waitFor(() => expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta === '/api/v1/contratos/guias/g1/publicacion')).toBe(true));
    expect(llamadas.find((l) => l.metodo === 'POST')!.cuerpo).toEqual({ revision: 2 });
  });

  it('el historial compara dos revisiones línea a línea y restaura una creando otra', async () => {
    const llamadas = apiFalsa();
    montar({ contratoId: 'c1', guiaId: 'g1' });

    fireEvent.click(await screen.findByRole('button', { name: 'Historial' }));
    const revisiones = await screen.findByRole('list', { name: 'Revisiones de la guía' });
    expect(within(revisiones).getAllByRole('listitem')).toHaveLength(2);
    expect(within(revisiones).getByText('Renovación del token.')).toBeInTheDocument();

    fireEvent.click(within(revisiones).getByRole('button', { name: 'Comparar con la 1' }));
    const cambios = await screen.findByRole('list', { name: 'Cambios línea a línea' });
    expect(within(cambios).getByText('Pide un token.')).toBeInTheDocument();
    expect(within(cambios).getByText('Pide un token y renuévalo cada hora.')).toBeInTheDocument();
    expect(screen.getByText(/1 línea agregada y 1 quitada/)).toBeInTheDocument();

    fireEvent.click(within(revisiones).getByRole('button', { name: 'Restaurar' }));
    const formulario = screen.getByRole('form', { name: 'Restaurar la revisión 1' });
    fireEvent.change(within(formulario).getByLabelText('Nota de la restauración'), { target: { value: 'Volvemos a la primera.' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Restaurar la revisión 1' }));

    await waitFor(() => expect(llamadas.some((l) => l.ruta === '/api/v1/contratos/guias/g1/revisiones/1/restauracion')).toBe(true));
    expect(llamadas.find((l) => l.ruta.endsWith('/restauracion'))!.cuerpo).toEqual({ nota: 'Volvemos a la primera.', revisionBase: 2 });
    expect(await screen.findByText('Restaurada: la revisión 3 es igual a la 1.')).toBeInTheDocument();
  });
});
