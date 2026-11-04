import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import { MODULOS_REGISTRADOS } from '../../app/modulos/registro';
import { resolverNavegacion } from '../../app/modulos/resolver';
import { vaciarCola } from '../../pwa/cola';
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
  /** El primer turno es de la persona en sesión (para pedir el cambio). */
  turnoPropio?: boolean;
}

const fila = (n: number, accion: 'crear' | 'omitir', extra: Record<string, unknown> = {}) => ({
  fila: n,
  fecha: '2026-06-13',
  textoFecha: '2026-06-13',
  responsable: { texto: 'persona demo uno', clave: 'persona demo uno', coincidencia: 'exacta', personaId: 'p-t1', nombre: 'Persona Demo Uno', nueva: false },
  estado: 'aplicado',
  reemplazo: null,
  compensacion: null,
  observacion: null,
  requiereRevision: false,
  accion,
  motivo: null,
  interpretaciones: ['«persona demo uno» es Persona Demo Uno (sin tildes ni mayúsculas).'],
  ambiguedades: [],
  ...extra,
});

function vistaPreviaImportacion(confirmada: boolean) {
  return {
    equipo: DESARROLLO,
    hoja: 'Rotacion Soporte',
    huella: confirmada ? 'hi2' : 'hi1',
    resumen: {
      filas: 2, importadas: confirmada ? 2 : 1, yaImportadas: 0, omitidas: confirmada ? 0 : 1, turnos: confirmada ? 2 : 1, compensaciones: 0,
      personasInactivas: confirmada ? 1 : 0, participantes: 1, generadosQuitados: 0, interpretaciones: 1, ambiguos: confirmada ? 1 : 0,
    },
    filas: [
      fila(2, 'crear'),
      fila(3, confirmada ? 'crear' : 'omitir', {
        responsable: { texto: 'Persona Ficticia', clave: 'persona ficticia', coincidencia: 'ninguna', personaId: null, nombre: null, nueva: confirmada },
        motivo: confirmada ? null : '«Persona Ficticia» no existe en Atlas: confirma crearla como persona inactiva para importar la fila.',
        interpretaciones: [],
        requiereRevision: confirmada,
        ambiguedades: confirmada ? ['Compensado «lo cubrió alguien» no es una fecha ni dice «reemplazo»: queda en la observación para revisión.'] : [],
      }),
    ],
    personasNuevas: [{ clave: 'persona ficticia', nombre: 'Persona Ficticia', filas: 1, ultimaFecha: '2026-06-20', confirmada }],
    nombresAmbiguos: [],
    participantesNuevos: [{ personaId: 'p-t1', nombre: 'Persona Demo Uno', activo: true }],
    avisos: confirmada ? [] : ['Hay personas del Excel que no existen en Atlas: confirma crearlas inactivas o sus filas no se importan.'],
  };
}

const solicitud = (puede: { responder?: boolean; resolver?: boolean }) => ({
  id: 'sc1',
  turno: turno('t1', SABADO, 'Usuaria Demo'),
  solicitante: { id: 'p-x', nombre: 'Persona Demo Dos', correo: null, cargo: null, telefono: null },
  reemplazo: { id: 'yo', nombre: 'Usuaria Demo', correo: null, cargo: null, telefono: null },
  estado: puede.resolver ? 'pendiente_aprobacion' : 'pendiente_reemplazo',
  motivo: 'Cita médica',
  turnoNuevoId: null,
  resueltaPor: null,
  resueltaEn: null,
  creadaEn: '2026-10-23T15:00:00Z',
  historial: [{ accion: 'solicitada', persona: { id: 'p-x', nombre: 'Persona Demo Dos' }, nota: 'Cita médica', ocurridoEn: '2026-10-23T15:00:00Z', hechaEn: null }],
  puedeResponder: puede.responder ?? false,
  puedeResolver: puede.resolver ?? false,
  puedeCancelar: false,
});

function apiFalsa(opciones: Opciones) {
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  const propio = { ...turno('t1', SABADO, 'Usuaria Demo'), persona: { id: 'yo', nombre: 'Usuaria Demo', correo: 'yo@ejemplo.com', cargo: null, telefono: null } };
  const turnos = [
    opciones.turnoPropio ? propio : turno('t1', SABADO, 'Persona Demo Uno'),
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
        return json([
          { id: 'pa1', equipoId: 'e1', personaId: 'p-t1', nombre: 'Persona Demo Uno', correo: 't1@ejemplo.com', orden: 1, activo: true, estadoPersona: 'activa' },
          { id: 'pa2', equipoId: 'e1', personaId: 'yo', nombre: 'Usuaria Demo', correo: 'yo@ejemplo.com', orden: 2, activo: true, estadoPersona: 'activa' },
        ]);
      if (ruta0 === '/api/v1/soporte/importacion/vista-previa') {
        const cuerpo = JSON.parse(String(init?.body)) as { crearInactivas: string[] };
        return json(vistaPreviaImportacion(cuerpo.crearInactivas.length > 0));
      }
      if (ruta0 === '/api/v1/soporte/importacion/confirmar') return json({ importacionId: 'i1', informe: vistaPreviaImportacion(true) });
      if (ruta0 === '/api/v1/soporte/solicitudes-cambio' && init?.method === 'POST') return json(solicitud({}), 201);
      if (ruta0 === '/api/v1/soporte/solicitudes-cambio')
        return json(opciones.permisos.includes('atlas.soporte.aprobar') ? [solicitud({ resolver: true })] : [solicitud({ responder: true })]);
      if (ruta0.startsWith('/api/v1/soporte/solicitudes-cambio/')) return json({ ...solicitud({}), estado: 'pendiente_aprobacion' });
      if (ruta0 === '/api/v1/soporte/compensaciones/pendientes') return json({ equipoId: null, total: 0, personas: [] });
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
      // HU 2.10: Microsoft 365 apagado en este ambiente y feeds .ics.
      if (ruta0 === '/api/v1/soporte/microsoft-365')
        return json({
          calendario: {
            activo: false,
            motivo: 'Este ambiente no tiene Entra ID configurado: no hay acceso a Microsoft Graph.',
            nombreCalendario: 'Soporte TI sábados',
            pendientes: 0,
            conErrores: 0,
            ultimaSincronizacion: null,
          },
          teams: { activo: false, motivo: 'Falta la URL del flujo de Workflows del canal de Teams (Plataforma__Teams__Canales__soporte).', pendientes: 0 },
        });
      if (ruta0 === '/api/v1/soporte/feeds' && init?.method === 'POST')
        return json(
          {
            id: 'f1',
            tipo: 'personal',
            equipo: null,
            url: 'https://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio.ics',
            urlWebcal: 'webcal://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio.ics',
            creadoEn: '2026-10-23T15:00:00Z',
          },
          201,
        );
      if (ruta0 === '/api/v1/soporte/feeds')
        return json([{ id: 'f0', tipo: 'equipo', equipo: DESARROLLO, creadoEn: '2026-10-22T15:00:00Z', ultimoUsoEn: null }]);
      if (ruta0.startsWith('/api/v1/soporte/feeds/') && init?.method === 'DELETE') return new Response(null, { status: 204 });
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

afterEach(async () => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  await vaciarCola();
});

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

  it('el importador muestra la vista previa con el informe, confirma la persona inactiva y luego importa con la huella', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.soporte.ver', 'atlas.soporte.gestionar'] });
    window.history.pushState({}, '', '/soporte/importar');

    render(<App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Importar el Excel de la rotación' })).toBeInTheDocument();
    const archivo = new File([new Uint8Array([80, 75, 3, 4])], 'rotacion.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    fireEvent.change(screen.getByLabelText(/Archivo .xlsx/), { target: { files: [archivo] } });
    const ver = screen.getByRole('button', { name: 'Ver vista previa' });
    await vi.waitFor(() => expect(ver).toBeEnabled());
    fireEvent.click(ver);

    const previa = await screen.findByRole('region', { name: 'Vista previa de la importación' });
    expect(within(previa).getByText(/no existen en Atlas: confirma crearlas inactivas/)).toBeInTheDocument();
    expect(within(previa).getByText('«persona demo uno» es Persona Demo Uno (sin tildes ni mayúsculas).')).toBeInTheDocument();
    expect(within(previa).getByRole('article', { name: 'Fila 3' })).toHaveTextContent('Se omite');
    const primera = JSON.parse(String(llamadas.find((l) => l.ruta.endsWith('/importacion/vista-previa'))?.init?.body));
    expect(primera).toMatchObject({ equipoId: 'e1', contenido: 'UEsDBA==', crearInactivas: [], agregarParticipantes: true });

    fireEvent.click(within(previa).getByRole('checkbox', { name: /Crear a Persona Ficticia como persona inactiva/ }));
    expect(await within(previa).findByRole('button', { name: 'Importar 2 filas' })).toBeInTheDocument();
    expect(within(previa).getByRole('list', { name: 'Casos ambiguos' })).toBeInTheDocument();
    fireEvent.click(within(previa).getByRole('button', { name: 'Importar 2 filas' }));

    expect(await screen.findByText(/Se importaron 2 filas en Desarrollo TI: 2 turnos, 0 compensaciones y 1 personas inactivas nuevas/)).toBeInTheDocument();
    const confirmacion = JSON.parse(String(llamadas.find((l) => l.ruta.endsWith('/importacion/confirmar'))?.init?.body));
    expect(confirmacion).toMatchObject({ huella: 'hi2', crearInactivas: ['persona ficticia'] });
  });

  it('sin conexión, la solicitud de cambio queda pendiente de enviar y se envía sola al reconectar con su llave', async () => {
    movil = true;
    const llamadas = apiFalsa({ permisos: ['atlas.soporte.ver', 'atlas.soporte.solicitar_cambio'], turnoPropio: true });
    window.history.pushState({}, '', '/soporte');
    render(<App />);

    fireEvent.click(await screen.findByRole('button', { name: /Solicitar el cambio del turno del/ }));
    const hoja = await screen.findByRole('form', { name: 'Solicitud de cambio' });
    fireEvent.click(await within(hoja).findByRole('radio', { name: 'Persona Demo Uno' }));
    const enLinea = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    fireEvent.click(within(hoja).getByRole('button', { name: 'Solicitar el cambio' }));

    expect(await screen.findByText(/Sin conexión: quedó pendiente de enviar/)).toBeInTheDocument();
    const pendientes = await screen.findByRole('region', { name: 'Pendientes de enviar' });
    expect(within(pendientes).getByText('1 acción pendiente de enviar')).toBeInTheDocument();
    expect(await screen.findByText('Cambio pendiente de enviar')).toBeInTheDocument();
    expect(llamadas.some((l) => l.ruta === '/api/v1/soporte/solicitudes-cambio' && l.init?.method === 'POST')).toBe(false);

    enLinea.mockReturnValue(true);
    await act(async () => {
      window.dispatchEvent(new Event('online'));
    });
    await vi.waitFor(() => expect(screen.queryByRole('region', { name: 'Pendientes de enviar' })).toBeNull());
    const enviada = llamadas.find((l) => l.ruta === '/api/v1/soporte/solicitudes-cambio' && l.init?.method === 'POST');
    const cuerpo = JSON.parse(String(enviada?.init?.body)) as Record<string, string>;
    expect(cuerpo).toMatchObject({ turnoId: 't1', reemplazoPersonaId: 'p-t1' });
    expect(cuerpo.llaveIdempotencia).toMatch(/^[0-9a-f-]{36}$/);
    expect(cuerpo.hechaEn).toBeTruthy();
  });

  it('Microsoft 365 apagado lo dice con su motivo y el feed .ics muestra su dirección una sola vez', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.soporte.ver'] });
    window.history.pushState({}, '', '/soporte/microsoft-365');
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Outlook, Teams y calendarios' })).toBeInTheDocument();
    const calendario = await screen.findByRole('region', { name: /Calendario compartido de Outlook/ });
    expect(within(calendario).getByText('Apagado')).toBeInTheDocument();
    expect(within(calendario).getByRole('note')).toHaveTextContent('no tiene Entra ID');
    expect(screen.getByRole('region', { name: /Avisos en el canal de Teams/ })).toHaveTextContent('Plataforma__Teams__Canales__soporte');

    const mios = await screen.findByRole('list', { name: 'Mis feeds' });
    expect(mios).toHaveTextContent('Equipo Desarrollo TI');
    fireEvent.click(screen.getByRole('button', { name: 'Feed de mis turnos' }));
    expect(await screen.findByDisplayValue('https://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio.ics')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir en la app de calendario' })).toHaveAttribute('href', 'webcal://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio.ics');
    const creado = llamadas.find((l) => l.ruta === '/api/v1/soporte/feeds' && l.init?.method === 'POST');
    expect(JSON.parse(String(creado?.init?.body))).toEqual({ tipo: 'personal', equipoId: null });

    fireEvent.click(within(mios).getByRole('button', { name: 'Revocar' }));
    expect(await screen.findByText('Feed revocado: su dirección ya no funciona.')).toBeInTheDocument();
    expect(llamadas.some((l) => l.ruta === '/api/v1/soporte/feeds/f0' && l.init?.method === 'DELETE')).toBe(true);
  });

  it('el reemplazo acepta desde Cambios de turno y quien aprueba ve la solicitud por aprobar', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.soporte.ver', 'atlas.soporte.solicitar_cambio'] });
    window.history.pushState({}, '', '/soporte/cambios');
    render(<App />);

    const tarjeta = await screen.findByRole('article', { name: /Cambio del turno del/ });
    expect(within(tarjeta).getByText('Espera al reemplazo')).toBeInTheDocument();
    expect(within(tarjeta).getByRole('list', { name: 'Historial' })).toHaveTextContent('Solicitó el cambio');
    fireEvent.click(within(tarjeta).getByRole('button', { name: 'Aceptar el reemplazo' }));
    expect(await screen.findByText('Aceptaste el reemplazo.')).toBeInTheDocument();
    const respuesta = llamadas.find((l) => l.ruta === '/api/v1/soporte/solicitudes-cambio/sc1/respuesta');
    expect(JSON.parse(String(respuesta?.init?.body))).toMatchObject({ acepta: true });
    expect(screen.getByText('No hay compensaciones pendientes.')).toBeInTheDocument();
  });
});
