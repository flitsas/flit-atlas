import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CABECERA_CSRF } from '../../app/api';
import { App } from '../../app/App';
import type { AQuienLlamo, MapaCarga, PersonaEquipo, Reparto, Responsable, ResponsablesAplicativo, VistaPrevia } from './apiResponsables';
import { moduloEquipo } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const persona = (id: string, nombre: string, telefono: string | null = null): PersonaEquipo => ({
  id,
  nombre,
  correo: `${id}@ejemplo.com`,
  cargo: 'Desarrollo',
  foto: null,
  telefono,
  usuarioGitHub: null,
  activa: true,
});

const UNO = persona('p1', 'Persona Demo Uno', '+57 300 000 0001');
const TRES = persona('p3', 'Persona Demo Tres');

const responsable = (id: string, tipo: Responsable['tipo'], p: PersonaEquipo, estado: Responsable['estadoTraspaso'] = 'habilitado'): Responsable => ({
  id,
  tipo,
  persona: p,
  dedicacion: tipo === 'secundario' ? 20 : null,
  vigenteDesde: '2026-10-20',
  vigenteHasta: null,
  vigente: true,
  estadoTraspaso: estado,
});

const VIGIA = { id: 'a1', slug: 'vigia', nombre: 'Vigía', tipo: 'producto' as const };
const DEFINICIONES = [
  { rol: 'principal' as const, titulo: 'Responsable principal', puntos: ['Revisa (o delega la revisión de) todo PR que toque su módulo.'] },
  { rol: 'secundario' as const, titulo: 'Responsable secundario', puntos: ['Dedicación orientativa: ~20 % de su tiempo en ese módulo.'] },
];

const REPARTO: Reparto = {
  version: {
    numero: 5,
    origen: 'importacion',
    restauradaDe: null,
    fechaDocumento: '2026-10-20',
    definidoPor: 'Persona Demo Uno (Líder técnico)',
    autor: 'Líder Demo',
    nota: 'Importación del documento',
    creadaEn: '2026-10-23T15:00:00Z',
    huella: 'a'.repeat(64),
    responsabilidades: 2,
  },
  definiciones: DEFINICIONES,
  porPersona: [
    { persona: TRES, principalDe: [VIGIA], secundarioDe: [] },
    { persona: UNO, principalDe: [], secundarioDe: [VIGIA] },
  ],
  productos: [{ aplicativo: VIGIA, principal: responsable('r1', 'principal', TRES), secundario: responsable('r2', 'secundario', UNO, 'en_acompanamiento') }],
  modulos: [],
  servicios: [],
  huerfanas: 0,
  veContacto: false,
  puedeGestionar: false,
};

const CARGA: MapaCarga = {
  media: 1.1,
  umbralAlto: 1.43,
  umbralBajo: 0.77,
  personas: [
    { persona: TRES, principales: 1, secundarias: 0, puntaje: 1, nivel: 'normal' },
    { persona: UNO, principales: 0, secundarias: 1, puntaje: 0.2, nivel: 'baja' },
  ],
};

const VISTA: VistaPrevia = {
  version: 5,
  fecha: '2026-10-20',
  definidoPor: 'Persona Demo Uno (Líder técnico)',
  descripcionVersion: 'reparto definido por el líder técnico',
  dedicacionSecundario: 20,
  proximaRevision: '2027-01-20',
  definiciones: DEFINICIONES,
  filas: [{ aplicativo: VIGIA, tipo: 'principal', personaId: 'p3', persona: 'Persona Demo Tres', dedicacion: null }],
  pendientes: [],
  inconsistencias: [],
  avisos: [],
  errores: [],
  versionActual: null,
  puedeConfirmar: true,
  huella: 'b'.repeat(64),
};

function apiFalsa(opciones: { permisos: string[]; puedeGestionar?: boolean; sabado?: boolean }) {
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      llamadas.push({ ruta, init });
      const metodo = init?.method ?? 'GET';
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's', personaId: 'p1', nombre: 'Persona Demo Uno', tipoPrincipal: 'empleado', organizacionId: 'o', proveedor: 'entra',
          roles: ['atlas.lector'], permisos: opciones.permisos, foto: null,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta.startsWith('/api/v1/plataforma/directorio?'))
        return json({
          personas: [{ id: 'p9', nombre: 'Persona Demo Nueve', correo: 'p9@ejemplo.com', cargo: null, area: null, estado: 'activa', foto: null, telefono: null, sincronizadaEn: null }],
          hayMas: false,
          veContacto: false,
        });
      if (ruta === '/api/v1/equipo/responsabilidades' && metodo === 'GET') return json({ ...REPARTO, puedeGestionar: opciones.puedeGestionar ?? false });
      if (ruta === '/api/v1/equipo/responsabilidades' && metodo === 'PUT') return json({ ...REPARTO.version, numero: 6, origen: 'cambio' });
      if (ruta === '/api/v1/equipo/revisiones')
        return json({ proxima: { id: 'v1', fecha: '2027-01-20', estado: 'pendiente', recordatorioEnviadoEn: null, hechaEn: null, hechaPor: null, nota: null }, historial: [], diasAnticipacion: 7 });
      if (ruta === '/api/v1/equipo/carga') return json(CARGA);
      if (ruta === '/api/v1/equipo/importacion/vista-previa') return json(VISTA);
      if (ruta === '/api/v1/equipo/importacion') return json({ ...REPARTO.version }, 201);
      if (ruta === '/api/v1/equipo/yo/responsabilidades')
        return json({ persona: UNO, principalDe: [], secundarioDe: [{ aplicativo: VIGIA, responsable: responsable('r2', 'secundario', UNO, 'en_acompanamiento') }] });
      if (ruta === '/api/v1/equipo/aplicativos/vigia/responsables') {
        const datos: ResponsablesAplicativo = {
          aplicativo: VIGIA, principal: REPARTO.productos[0]!.principal, secundario: REPARTO.productos[0]!.secundario, heredadoDe: null,
          definiciones: DEFINICIONES, traspaso: null, version: 5, puedeGestionar: false, veContacto: true,
        };
        return json(datos);
      }
      if (ruta === '/api/v1/equipo/aplicativos/vigia/a-quien-llamo') {
        const datos: AQuienLlamo = {
          aplicativo: VIGIA, hoy: '2026-10-24', esSabado: opciones.sabado ?? false,
          soporte: opciones.sabado
            ? { fecha: '2026-10-24', festivo: null, equipos: [{ id: 'e1', nombre: 'Desarrollo TI', area: 'TI', horaInicio: '08:00', horaFin: '12:00', personas: [UNO] }] }
            : null,
          principal: REPARTO.productos[0]!.principal, secundario: REPARTO.productos[0]!.secundario, heredadoDe: null, veContacto: true,
        };
        return json(datos);
      }
      if (ruta === '/api/v1/catalogo/aplicativos/vigia')
        return json({
          id: 'a1', slug: 'vigia', nombre: 'Vigía', tipo: 'producto', grupo: 'satelite', estado: 'activo', criticidad: 'media', manejaDatosPersonales: false,
          padreId: null, descripcion: null, repo: null, rutaEnRepo: null, tecnologias: [], ambientes: [], etiquetas: [], ancestros: [], hijos: [],
          permisos: { editar: false, gestionar: false }, creadoEn: '2026-10-23T12:00:00Z', actualizadoEn: '2026-10-23T12:00:00Z',
        });
      return new Response(null, { status: 404 });
    }),
  );
  return llamadas;
}

describe('responsables (HU 2.3 a 2.5)', () => {
  beforeEach(() => window.history.pushState({}, '', '/'));
  afterEach(() => vi.unstubAllGlobals());

  it('el manifiesto aporta la página, la navegación y la pestaña «responsables» de la ficha', () => {
    expect(moduloEquipo.rutas.map((r) => r.path)).toEqual(['/equipo', '/equipo/responsables']);
    expect(moduloEquipo.pestanasFicha?.map((p) => [p.id, p.permiso])).toEqual([['responsables', 'atlas.equipo.ver']]);
    expect(moduloEquipo.pestanasFicha?.[0]?.cargar.toString()).toMatch(/import/);
  });

  it('muestra las definiciones tal cual, el reparto por producto y la próxima revisión, sin acciones para un lector', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver'] });
    window.history.pushState({}, '', '/equipo/responsables');
    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Responsables' })).toBeInTheDocument();
    expect(await screen.findByText('Revisa (o delega la revisión de) todo PR que toque su módulo.')).toBeInTheDocument();
    const productos = await screen.findByRole('list', { name: 'Productos' });
    expect(within(productos).getByRole('link', { name: 'Vigía' })).toHaveAttribute('href', '/catalogo/vigia');
    expect(within(productos).getByText('Persona Demo Tres')).toBeInTheDocument();
    expect(within(productos).getByText('En acompañamiento')).toBeInTheDocument();
    expect(within(productos).getByText('~20 %')).toBeInTheDocument();
    expect(screen.getByText('20/01/2027')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar/ })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Importar' })).toBeNull();
  });

  it('el mapa de carga resalta a quien se sale de la media con un medidor accesible', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver'] });
    window.history.pushState({}, '', '/equipo/responsables?vista=carga');
    render(<App />);

    const lista = await screen.findByRole('list', { name: 'Carga por persona' });
    expect(within(lista).getByText('Carga baja')).toBeInTheDocument();
    expect(within(lista).getByRole('meter', { name: /Carga de Persona Demo Uno/ })).toHaveAttribute('value', '0.2');
  });

  it('quien gestiona cambia un responsable con nota y la cabecera antifalsificación', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.equipo.ver', 'atlas.equipo.gestionar'], puedeGestionar: true });
    window.history.pushState({}, '', '/equipo/responsables');
    render(<App />);

    fireEvent.click(await screen.findByRole('button', { name: 'Cambiar secundario de Vigía' }));
    const formulario = await screen.findByRole('form', { name: 'Cambio de responsable' });
    fireEvent.change(within(formulario).getByLabelText('Busca a la persona'), { target: { value: 'nueve' } });
    fireEvent.click(await within(formulario).findByRole('radio', { name: /Persona Demo Nueve/ }));
    fireEvent.change(within(formulario).getByLabelText('Nota del cambio'), { target: { value: 'Respaldo nuevo.' } });
    fireEvent.click(within(formulario).getByRole('button', { name: 'Guardar cambio' }));

    expect(await screen.findByText('Listo: el reparto va en la versión 6.')).toBeInTheDocument();
    const put = llamadas.find((l) => l.init?.method === 'PUT')!;
    expect(new Headers(put.init!.headers).get(CABECERA_CSRF)).toBe('1');
    expect(JSON.parse(String(put.init!.body))).toEqual({ aplicativoId: 'a1', tipo: 'secundario', personaId: 'p9', dedicacion: 20, nota: 'Respaldo nuevo.' });
  });

  it('importa el documento: vista previa y confirmación con su huella', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.equipo.ver', 'atlas.equipo.gestionar'], puedeGestionar: true });
    window.history.pushState({}, '', '/equipo/responsables?vista=importar');
    render(<App />);

    fireEvent.change(await screen.findByLabelText('Contenido'), { target: { value: '# Responsables por producto y por módulo' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ver la vista previa' }));
    expect(await screen.findByText(/1 responsabilidades enlazadas/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: /En acompañamiento/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar la importación' }));

    expect(await screen.findByText('Importado: el reparto va en la versión 5.')).toBeInTheDocument();
    const importacion = llamadas.find((l) => l.ruta === '/api/v1/equipo/importacion')!;
    expect(JSON.parse(String(importacion.init!.body))).toEqual({ markdown: '# Responsables por producto y por módulo', huella: 'b'.repeat(64), traspaso: 'en_acompanamiento' });
  });

  it('en la ficha del aplicativo, «¿A quién llamo?» un sábado muestra el soporte de turno y el contacto', async () => {
    apiFalsa({ permisos: ['atlas.catalogo.ver', 'atlas.equipo.ver', 'atlas.equipo.ver_contacto'], sabado: true });
    window.history.pushState({}, '', '/catalogo/vigia?pestana=responsables');
    render(<App />);

    expect(await screen.findByText('Persona Demo Tres')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /¿A quién llamo\?/ }));
    const llamar = await screen.findByRole('region', { name: '¿A quién llamo?' });
    const soporte = await within(llamar).findByRole('list', { name: 'Soporte de turno por equipo' });
    expect(within(soporte).getByText(/Desarrollo TI · 08:00 a 12:00/)).toBeInTheDocument();
    expect(within(soporte).getByRole('link', { name: /\+57 300 000 0001/ })).toHaveAttribute('href', 'tel:+573000000001');
  });

  it('el widget de Inicio lista mis responsabilidades con su traspaso', async () => {
    apiFalsa({ permisos: ['atlas.equipo.ver'] });
    render(<App />);

    const lista = await screen.findByRole('list', { name: 'Secundario de' });
    await waitFor(() => expect(within(lista).getByRole('link', { name: 'Vigía' })).toHaveAttribute('href', '/catalogo/vigia?pestana=responsables'));
    expect(within(lista).getByText('En acompañamiento')).toBeInTheDocument();
  });
});
