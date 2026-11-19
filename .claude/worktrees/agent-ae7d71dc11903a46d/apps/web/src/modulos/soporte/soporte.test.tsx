import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { resolverNavegacion } from '../../app/modulos/resolver';
import { textoCupo } from './api';
import { cuadriculaDelMes, diaSemana, formatearDiaLargo, hoy, lunesDe, sabadoDesde, sumarDias, sumarMeses } from './fechas';
import { moduloSoporte } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const SABADO = sabadoDesde(sumarDias(hoy(), 1));

const DESARROLLO = { id: 'e1', nombre: 'Desarrollo TI' };
const OPERACIONES = { id: 'e2', nombre: 'Operaciones' };

const equipo = (e: { id: string; nombre: string }, minimo: number, maximo: number, puede = true) => ({
  ...e,
  area: null,
  personasMinimo: minimo,
  personasMaximo: maximo,
  horaInicio: null,
  horaFin: null,
  horarioEfectivo: '08:00 a 12:00',
  activo: true,
  puedeGestionar: puede,
  puedeAprobar: puede,
});

const turno = (id: string, fecha: string, nombre: string, eq = DESARROLLO) => ({
  id,
  equipo: eq,
  fecha,
  persona: { id: `p-${id}`, nombre, correo: `${id}@ejemplo.com`, cargo: 'Desarrollo', telefono: null },
  horaInicio: '08:00',
  horaFin: '12:00',
  estado: 'programado',
  origen: 'generado',
  turnoReemplazadoId: null,
  festivo: null,
  observacion: null,
  requiereRevision: false,
});

interface Opciones {
  permisos: string[];
  tipo?: 'empleado' | 'externo';
}

function apiFalsa(opciones: Opciones) {
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  const turnos = [
    turno('t1', SABADO, 'Persona Demo Uno'),
    turno('t3', SABADO, 'Persona Demo Tres', OPERACIONES),
    turno('t4', SABADO, 'Persona Demo Cuatro', OPERACIONES),
    turno('t2', sumarDias(SABADO, 7), 'Persona Demo Dos'),
  ];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      llamadas.push({ ruta, init });
      const url = new URL(ruta, 'https://atlas.ejemplo.com');
      const ruta0 = url.pathname;
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: 'yo',
          nombre: 'Usuaria Demo',
          tipoPrincipal: opciones.tipo ?? 'empleado',
          organizacionId: 'o',
          proveedor: 'simulado',
          roles: [],
          permisos: opciones.permisos,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta0 === '/api/v1/soporte/equipos') {
        const gestiona = opciones.permisos.includes('atlas.soporte.gestionar');
        return json([equipo(DESARROLLO, 1, 1, gestiona), equipo(OPERACIONES, 1, 2, gestiona)]);
      }
      if (ruta0 === '/api/v1/soporte/este-sabado')
        return json({
          fecha: SABADO,
          festivo: null,
          equipos: [
            { equipo: DESARROLLO, turnos: [turnos[0]], faltan: 0 },
            { equipo: OPERACIONES, turnos: [turnos[1], turnos[2]], faltan: 0 },
          ],
        });
      if (ruta0 === '/api/v1/soporte/turnos') {
        const filtro = url.searchParams.get('equipo');
        return json({
          desde: url.searchParams.get('desde'),
          hasta: url.searchParams.get('hasta'),
          equipoId: filtro,
          turnos: filtro ? turnos.filter((t) => t.equipo.id === filtro) : turnos,
          festivos: [],
        });
      }
      if (ruta0 === '/api/v1/soporte/canal')
        return json({ fecha: SABADO, configurado: true, canal: 'soporte@ejemplo.com', horaInicio: '08:00', horaFin: '12:00', haySoporte: true });
      if (ruta0.endsWith('/participantes'))
        return json([{ id: 'pa1', equipoId: 'e1', personaId: 'p-t1', nombre: 'Persona Demo Uno', correo: 't1@ejemplo.com', orden: 1, activo: true, estadoPersona: 'activa' }]);
      if (ruta0 === '/api/v1/soporte/configuracion')
        return json({ horaInicioPorDefecto: '08:00', horaFinPorDefecto: '12:00', reglaFestivos: 'sin_turno', diasAnticipacionRecordatorio: null, canalExterno: null, actualizadoEn: null });
      if (ruta0 === '/api/v1/soporte/ausencias') return json([]);
      if (ruta0 === '/api/v1/soporte/festivos') return json([]);
      if (ruta0 === '/api/v1/soporte/generacion/vista-previa')
        return json({
          equipo: DESARROLLO,
          desde: SABADO,
          hasta: sumarDias(SABADO, 30),
          huella: 'h1',
          sabados: [
            { fecha: SABADO, decision: 'asignado', personas: [{ personaId: 'p-t1', nombre: 'Persona Demo Uno', nueva: true }], festivo: null, cambia: true, ausenteEnFijo: false, faltan: 0 },
          ],
          turnosPorPersona: [{ personaId: 'p-t1', nombre: 'Persona Demo Uno', turnos: 1 }],
          desbalance: 0,
          nuevos: 1,
          eliminados: 0,
          avisos: [],
        });
      if (ruta0 === '/api/v1/soporte/generacion/confirmar') return json({ generacionId: 'g1', creados: 1, eliminados: 0 });
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

let movil = false;

beforeEach(() => {
  movil = false;
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: movil && query.includes('max-width'), media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});

afterEach(() => vi.unstubAllGlobals());

describe('fechas del soporte', () => {
  it('calcula sábados, lunes y la cuadrícula del mes sin depender del huso del dispositivo', () => {
    expect(sabadoDesde('2026-10-21')).toBe('2026-10-24');
    expect(sabadoDesde('2026-10-24')).toBe('2026-10-24');
    expect(lunesDe('2026-10-24')).toBe('2026-10-19');
    expect(sumarMeses('2026-12-15', 1)).toBe('2027-01-01');
    expect(diaSemana('2027-05-01')).toBe(6);
    const cuadricula = cuadriculaDelMes('2026-10-10');
    expect(cuadricula[0]).toBe('2026-09-28');
    expect(cuadricula.length % 7).toBe(0);
    expect(formatearDiaLargo('2026-10-24')).toBe('sábado 24/10/2026');
  });

  it('describe el cupo del equipo', () => {
    expect(textoCupo({ personasMinimo: 1, personasMaximo: 1 })).toBe('1 persona por sábado');
    expect(textoCupo({ personasMinimo: 1, personasMaximo: 2 })).toBe('1 a 2 personas por sábado');
  });
});

describe('manifiesto de soporte', () => {
  it('está disponible, en la barra inferior, con páginas y widgets diferidos', () => {
    expect(moduloSoporte.estado).toBe('disponible');
    expect(moduloSoporte.rutas.every((r) => r.cargar.toString().includes('import'))).toBe(true);
    const lector = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.soporte.ver'] });
    expect(lector.barraInferior.map((e) => e.ruta)).toContain('/soporte');
    expect(lector.widgets.map((w) => w.id)).toContain('soporte-este-sabado');
    expect(lector.widgets.map((w) => w.id)).not.toContain('soporte-canal');
  });

  it('un externo con atlas.soporte.ver_canal no ve el calendario, solo el widget del canal', () => {
    const externo = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {}, permisos: ['atlas.soporte.ver_canal'] });
    expect(externo.lateral.map((e) => e.ruta)).not.toContain('/soporte');
    expect(externo.widgets.map((w) => w.id)).toEqual(expect.arrayContaining(['soporte-canal']));
    expect(externo.widgets.map((w) => w.id)).not.toContain('soporte-este-sabado');
  });
});

describe('pantallas de soporte', () => {
  it('el widget de Inicio muestra a quién llamar en cada equipo', async () => {
    apiFalsa({ permisos: ['atlas.soporte.ver'] });
    window.history.pushState({}, '', '/');

    render(<App />);

    const widget = await screen.findByRole('region', { name: 'Soporte de este sábado' });
    expect(await within(widget).findByText('Persona Demo Uno')).toBeInTheDocument();
    expect(within(widget).getByText('Desarrollo TI')).toBeInTheDocument();
    expect(within(widget).getByText('Operaciones')).toBeInTheDocument();
    expect(within(widget).getByText('Persona Demo Cuatro')).toBeInTheDocument();
  });

  it('en el móvil el calendario abre en lista, con todos los equipos y filtrable por equipo', async () => {
    movil = true;
    apiFalsa({ permisos: ['atlas.soporte.ver'] });
    window.history.pushState({}, '', '/soporte');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Soporte de los sábados' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Lista' })).toBeChecked();
    expect(await screen.findByRole('radio', { name: 'Todos' })).toBeChecked();
    expect(within(await screen.findByRole('list', { name: 'Turnos' })).getAllByRole('article')).toHaveLength(4);
    fireEvent.click(screen.getByRole('radio', { name: 'Operaciones' }));
    expect(await screen.findByText('Persona Demo Tres')).toBeInTheDocument();
    await vi.waitFor(() => expect(within(screen.getByRole('list', { name: 'Turnos' })).getAllByRole('article')).toHaveLength(2));
    // Sin atlas.soporte.gestionar no hay acciones.
    expect(screen.queryByRole('button', { name: 'Nuevo turno' })).toBeNull();
  });

  it('en escritorio abre en mes y se puede cambiar a semana', async () => {
    apiFalsa({ permisos: ['atlas.soporte.ver'] });
    window.history.pushState({}, '', '/soporte');

    render(<App />);

    expect(await screen.findByRole('radio', { name: 'Mes' })).toBeChecked();
    expect(await screen.findByRole('grid')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: 'Semana' }));
    expect(await screen.findByRole('list', { name: 'Días de la semana' })).toBeInTheDocument();
  });

  it('quien gestiona ve la configuración por defecto, genera la vista previa del equipo y la publica con su huella', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.soporte.ver', 'atlas.soporte.gestionar', 'atlas.soporte.aprobar'] });
    window.history.pushState({}, '', '/soporte/gestion');

    render(<App />);

    const configuracion = await screen.findByRole('form', { name: 'Configuración del soporte' });
    expect(within(configuracion).getByLabelText('Inicio')).toHaveValue('08:00');
    expect(within(configuracion).getByRole('radio', { name: /Se saltan/ })).toBeChecked();
    expect(screen.queryByText(/Falta definir/)).toBeNull();
    fireEvent.click(await screen.findByRole('button', { name: 'Ver vista previa' }));
    const previa = await screen.findByRole('region', { name: 'Vista previa' });
    expect(within(previa).getByText('Persona Demo Uno')).toBeInTheDocument();
    fireEvent.click(within(previa).getByRole('button', { name: 'Publicar esta rotación' }));

    expect(await screen.findByText(/Se publicó la rotación de Desarrollo TI: 1 turnos nuevos/)).toBeInTheDocument();
    const confirmacion = llamadas.find((l) => l.ruta === '/api/v1/soporte/generacion/confirmar');
    expect(JSON.parse(String(confirmacion?.init?.body))).toEqual({ equipoId: 'e1', meses: 3, desde: SABADO, huella: 'h1' });
  });

  it('un externo ve el canal del sábado sin nombres de personas', async () => {
    apiFalsa({ permisos: ['atlas.soporte.ver_canal'], tipo: 'externo' });
    window.history.pushState({}, '', '/soporte/canal');

    render(<App />);

    expect(await screen.findByText('soporte@ejemplo.com')).toBeInTheDocument();
    expect(screen.queryByText(/Persona Demo/)).toBeNull();
  });
});
