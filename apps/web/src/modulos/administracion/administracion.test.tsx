import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from '../../app/App';
import { resolverNavegacion } from '../../app/modulos/resolver';
import { type Asignacion, motivoNoRetirable, type PersonaEncontrada, rolesMapeables, type RolAtlas } from './api';
import { moduloAdministracion } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

const ROLES: RolAtlas[] = [
  { codigo: 'atlas.superadmin', nombre: 'Superadministrador', tipo: 'interno', descripcion: 'Todo.', asignable: true, permisos: [] },
  { codigo: 'atlas.desarrollador', nombre: 'Desarrollador', tipo: 'interno', descripcion: 'Developers.', asignable: true, permisos: [] },
  { codigo: 'atlas.lector', nombre: 'Lector', tipo: 'interno', descripcion: 'Todo empleado.', asignable: false, permisos: [] },
  { codigo: 'ext.integrador', nombre: 'Integrador', tipo: 'externo', descripcion: 'Cliente técnico.', asignable: true, permisos: [] },
  { codigo: 'atlas.responsable', nombre: 'Responsable', tipo: 'implicito', descripcion: 'Por propiedad.', asignable: false, permisos: [] },
];

const GRUPO = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const PERSONA = '0b9a1c2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d';
const PROPIA = '11111111-2222-4333-8444-555555555555';
const EXTERNA = '22222222-3333-4444-8555-666666666666';

const PERSONAS: PersonaEncontrada[] = [
  {
    id: PERSONA,
    nombre: 'José Demo Álvarez',
    correo: 'jose.demo@ejemplo.com',
    tipo: 'empleado',
    organizacion: { id: 'o', nombre: 'FLIT S.A.S.' },
    estado: 'activa',
    emergencia: false,
    foto: `/api/v1/plataforma/personas/${PERSONA}/foto?v=0123456789abcdef`,
    roles: ['atlas.desarrollador'],
  },
  {
    id: EXTERNA,
    nombre: 'María Externa',
    correo: 'maria@ejemplo.com',
    tipo: 'externo',
    organizacion: { id: 'c', nombre: 'Organismo Demo' },
    estado: 'suspendida',
    emergencia: false,
    foto: null,
    roles: [],
  },
];

/** Como el servidor: sin mayúsculas ni tildes, por nombre o correo, o por id. */
const sinTildes = (t: string) => t.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

const asignacion = (id: string, origen: Asignacion['origen'], rol: string, rolNombre: string): Asignacion => ({
  id,
  personaId: PERSONA,
  rol,
  rolNombre,
  tipoRol: 'interno',
  organizacionId: 'o',
  alcance: { tipo: 'global', id: null },
  vigenteDesde: '2026-10-01T05:00:00Z',
  vigenteHasta: origen === 'manual' ? '2026-12-31T05:00:00Z' : null,
  origen,
  vigente: true,
  asignadoPor: null,
  creadoEn: '2026-10-01T05:00:00Z',
});

interface Opciones {
  permisos: string[];
  proveedor?: string;
  /** Respuesta de las acciones que cambian estado (por defecto, éxito). */
  cambio?: () => Response;
  organizaciones?: { id: string; nombre: string; tipo: string; adminOrganizacionHabilitado: boolean }[];
}

function apiFalsa({ permisos, proveedor = 'microsoft', cambio, organizaciones = [] }: Opciones) {
  const mapeos = [{ id: 'm1', grupoId: GRUPO, grupoNombre: 'FLIT-Developers', rol: 'atlas.desarrollador', creadoEn: '2026-10-20T15:00:00Z' }];
  const llamadas: { ruta: string; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (ruta: string, init?: RequestInit) => {
      llamadas.push({ ruta, init });
      const metodo = init?.method ?? 'GET';
      if (ruta === '/api/v1/plataforma/yo')
        return json({
          sub: 's',
          personaId: PROPIA,
          nombre: 'Administradora Demo',
          tipoPrincipal: 'empleado',
          organizacionId: 'o',
          proveedor,
          roles: ['atlas.superadmin'],
          permisos,
        });
      if (ruta === '/api/v1/plataforma/banderas') return json({});
      if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
      if (ruta === '/api/v1/plataforma/roles') return json(ROLES);
      if (metodo !== 'GET' && cambio) return cambio();
      if (ruta === '/api/v1/plataforma/mapeos-grupos-entra' && metodo === 'POST') {
        const s = JSON.parse(String(init!.body)) as { grupoId: string; grupoNombre: string; rol: string };
        const nuevo = { id: 'm2', ...s, creadoEn: new Date().toISOString() };
        mapeos.push(nuevo);
        return json(nuevo, 201);
      }
      if (ruta === '/api/v1/plataforma/mapeos-grupos-entra') return json(mapeos);
      if (ruta.startsWith('/api/v1/plataforma/mapeos-grupos-entra/') && metodo === 'DELETE') {
        mapeos.splice(0, 1);
        return new Response(null, { status: 204 });
      }
      if (ruta.startsWith('/api/v1/plataforma/personas?')) {
        const q = new URLSearchParams(ruta.split('?')[1]).get('q') ?? '';
        return json({ personas: PERSONAS.filter((p) => p.id === q || sinTildes(`${p.nombre} ${p.correo}`).includes(sinTildes(q))), hayMas: false });
      }
      if (ruta === `/api/v1/plataforma/personas/${PERSONA}/asignaciones` && metodo === 'POST')
        return json(asignacion('a9', 'manual', 'atlas.superadmin', 'Superadministrador'), 201);
      if (ruta.startsWith(`/api/v1/plataforma/personas/${PERSONA}/asignaciones`))
        return json([
          asignacion('a1', 'manual', 'atlas.desarrollador', 'Desarrollador'),
          asignacion('a2', 'grupo_entra', 'atlas.soporte', 'Soporte'),
          asignacion('a3', 'emergencia', 'atlas.superadmin', 'Superadministrador'),
        ]);
      if (ruta.startsWith('/api/v1/plataforma/personas/')) return new Response('', { status: 404 });
      if (ruta.startsWith('/api/v1/plataforma/asignaciones/') && metodo === 'DELETE') return new Response(null, { status: 204 });
      if (ruta === '/api/v1/plataforma/invitaciones') return json([]);
      if (ruta === '/api/v1/plataforma/invitaciones/opciones')
        return json({
          organizaciones,
          roles: [{ codigo: 'ext.integrador', nombre: 'Integrador', descripcion: '' }],
          tiposAlcance: ['aplicativo'],
          permisosExtra: ['exportar'],
          vigenciaMaximaDias: 90,
          soloSuOrganizacion: false,
        });
      return new Response('', { status: 404 });
    }),
  );
  return llamadas;
}

const VER = ['atlas.roles.ver'];
const GESTIONAR = ['atlas.roles.ver', 'atlas.roles.gestionar'];
const REAUTENTICAR = () =>
  new Response(JSON.stringify({ title: 'Confirma tu identidad', codigo: 'reautenticacion_requerida' }), {
    status: 401,
    headers: { 'Content-Type': 'application/problem+json' },
  });

beforeEach(() => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});

afterEach(() => vi.unstubAllGlobals());

describe('reglas de la pantalla de roles', () => {
  it('un grupo de Entra solo otorga roles internos asignables (sin atlas.lector, externos ni implícitos)', () => {
    expect(rolesMapeables(ROLES).map((r) => r.codigo)).toEqual(['atlas.superadmin', 'atlas.desarrollador']);
  });

  it('solo las asignaciones manuales se retiran desde aquí; las demás dicen por qué', () => {
    expect(motivoNoRetirable({ origen: 'manual' })).toBeNull();
    expect(motivoNoRetirable({ origen: 'grupo_entra' })).toMatch(/grupo de Entra/);
    expect(motivoNoRetirable({ origen: 'emergencia' })).toMatch(/emergencia/);
    expect(motivoNoRetirable({ origen: 'invitacion' })).toMatch(/revocando la invitación/);
  });

  it('la entrada del menú solo aparece con atlas.roles.ver y la página se carga de forma diferida', () => {
    const sin = resolverNavegacion([moduloAdministracion], { banderas: {}, permisos: ['atlas.lector'] });
    const con = resolverNavegacion([moduloAdministracion], { banderas: {}, permisos: VER });
    expect(sin.mas.map((e) => e.ruta)).not.toContain('/administracion/roles');
    expect(con.mas.map((e) => e.ruta)).toEqual(['/administracion/roles']);
    expect(moduloAdministracion.rutas.find((r) => r.path === '/administracion/roles')?.cargar.toString()).toMatch(/import/);
  });
});

describe('pantalla de asignación de roles', () => {
  it('sin atlas.roles.ver no se ve ni se piden los mapeos', async () => {
    const llamadas = apiFalsa({ permisos: ['atlas.lector'] });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'No tienes acceso' })).toBeInTheDocument();
    expect(llamadas.map((l) => l.ruta)).not.toContain('/api/v1/plataforma/mapeos-grupos-entra');
  });

  it('con solo atlas.roles.ver lista los mapeos sin botones para cambiarlos', async () => {
    apiFalsa({ permisos: VER });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);

    const lista = await screen.findByRole('list', { name: 'Mapeos de grupos de Entra' });
    expect(within(lista).getByText('FLIT-Developers')).toBeInTheDocument();
    expect(within(lista).getByText(GRUPO)).toBeInTheDocument();
    expect(await within(lista).findByText('Desarrollador')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nuevo mapeo' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Eliminar el mapeo/ })).toBeNull();
  });

  it('crea un mapeo: valida el object id, ofrece solo roles mapeables y envía la solicitud', async () => {
    const llamadas = apiFalsa({ permisos: GESTIONAR });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Nuevo mapeo' }));
    const hoja = screen.getByRole('dialog', { name: 'Nuevo mapeo de grupo' });
    const roles = await within(hoja).findByRole('group', { name: 'Rol que otorga' });
    expect(within(roles).getAllByRole('radio').map((r) => (r as HTMLInputElement).value)).toEqual(['atlas.superadmin', 'atlas.desarrollador']);

    fireEvent.change(within(hoja).getByLabelText(/Object id del grupo/), { target: { value: 'grupo-de-admins' } });
    fireEvent.change(within(hoja).getByLabelText('Nombre del grupo'), { target: { value: 'FLIT-Atlas-Admins' } });
    fireEvent.click(within(roles).getByRole('radio', { name: /Superadministrador/ }));
    expect(within(hoja).getByText(/recibe todos los permisos/)).toBeInTheDocument();
    fireEvent.submit(within(hoja).getByRole('form', { name: 'Datos del mapeo' }));
    expect(within(hoja).getByRole('alert')).toHaveTextContent('GUID');
    expect(llamadas.some((l) => l.init?.method === 'POST')).toBe(false);

    const otro = 'A1B2C3D4-0000-4000-8000-00000000AB12';
    fireEvent.change(within(hoja).getByLabelText(/Object id del grupo/), { target: { value: ` ${otro} ` } });
    fireEvent.submit(within(hoja).getByRole('form', { name: 'Datos del mapeo' }));

    expect(await screen.findByRole('status', {}, { timeout: 3000 })).toHaveTextContent('FLIT-Atlas-Admins otorga ahora Superadministrador.');
    const post = llamadas.find((l) => l.init?.method === 'POST')!;
    expect(post.ruta).toBe('/api/v1/plataforma/mapeos-grupos-entra');
    expect(JSON.parse(String(post.init!.body))).toEqual({ grupoId: otro.toLowerCase(), grupoNombre: 'FLIT-Atlas-Admins', rol: 'atlas.superadmin' });
    expect(await screen.findByText('FLIT-Atlas-Admins')).toBeInTheDocument();
  });

  it('elimina un mapeo solo después de confirmar', async () => {
    const llamadas = apiFalsa({ permisos: GESTIONAR });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: /Eliminar el mapeo de FLIT-Developers/ }));
    const dialogo = screen.getByRole('dialog', { name: 'Eliminar mapeo' });
    expect(dialogo).toHaveTextContent('se cierran sus sesiones');
    expect(llamadas.some((l) => l.init?.method === 'DELETE')).toBe(false);
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Eliminar' }));

    expect(await screen.findByRole('status')).toHaveTextContent('FLIT-Developers ya no otorga Desarrollador.');
    expect(llamadas.find((l) => l.init?.method === 'DELETE')?.ruta).toBe('/api/v1/plataforma/mapeos-grupos-entra/m1');
  });

  it('si el servidor pide reautenticación: con Microsoft, el enlace; con una cuenta local, salir y volver a entrar', async () => {
    apiFalsa({ permisos: GESTIONAR, cambio: REAUTENTICAR });
    window.history.pushState({}, '', '/administracion/roles');
    const { unmount } = render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: /Eliminar el mapeo de FLIT-Developers/ }));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Eliminar mapeo' })).getByRole('button', { name: 'Eliminar' }));
    expect(await screen.findByRole('link', { name: 'Confirmar con Microsoft' })).toHaveAttribute('href', expect.stringContaining('reautenticar=true'));
    unmount();

    apiFalsa({ permisos: GESTIONAR, proveedor: 'emergencia', cambio: REAUTENTICAR });
    window.history.pushState({}, '', '/administracion/roles');
    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: /Eliminar el mapeo de FLIT-Developers/ }));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Eliminar mapeo' })).getByRole('button', { name: 'Eliminar' }));
    expect(await screen.findByRole('button', { name: 'Salir y volver a entrar' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Confirmar con Microsoft' })).toBeNull();
  });

  it('muestra las asignaciones de la persona con origen y vigencia, y retira solo las manuales', async () => {
    const llamadas = apiFalsa({ permisos: GESTIONAR });
    window.history.pushState({}, '', `/administracion/roles?persona=${PERSONA}`);

    render(<App />);

    const lista = await screen.findByRole('list', { name: 'Asignaciones' });
    const tarjetas = within(lista).getAllByRole('listitem');
    expect(tarjetas).toHaveLength(3);
    expect(tarjetas[0]).toHaveTextContent('Manual');
    expect(tarjetas[0]).toHaveTextContent('31/12/2026');
    expect(tarjetas[1]).toHaveTextContent('Grupo de Entra');
    expect(tarjetas[1]).toHaveTextContent('Sin vencimiento');
    expect(tarjetas[1]).toHaveTextContent(/se quita sacando a la persona del grupo/);
    expect(tarjetas[2]).toHaveTextContent('Emergencia');
    expect(tarjetas[2]).toHaveTextContent(/comando de consola/);
    expect(within(lista).getAllByRole('button', { name: /^Retirar/ })).toHaveLength(1);

    fireEvent.click(within(tarjetas[0]!).getByRole('button', { name: 'Retirar Desarrollador' }));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Retirar asignación' })).getByRole('button', { name: 'Retirar' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Se retiró Desarrollador.');
    expect(llamadas.find((l) => l.init?.method === 'DELETE')?.ruta).toBe('/api/v1/plataforma/asignaciones/a1');
  });

  it('«Las mías» consulta la persona de la sesión y una persona inexistente se explica', async () => {
    const llamadas = apiFalsa({ permisos: VER });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Las mías' }));

    expect(await screen.findByRole('alert', {}, { timeout: 5000 })).toHaveTextContent('No hay ninguna persona con ese id.');
    expect(llamadas.some((l) => l.ruta === `/api/v1/plataforma/personas/${PROPIA}/asignaciones`)).toBe(true);
    expect(window.location.search).toBe(`?persona=${PROPIA}`);
  });
});

describe('buscador de personas', () => {
  const busquedas = (llamadas: { ruta: string }[]) => llamadas.filter((l) => l.ruta.startsWith('/api/v1/plataforma/personas?'));

  it('busca con retraso (no con una sola letra), sin tildes, y al elegir una persona abre sus asignaciones', async () => {
    const llamadas = apiFalsa({ permisos: VER });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);
    const campo = await screen.findByRole('searchbox', { name: /Nombre, correo o id/ });
    fireEvent.change(campo, { target: { value: 'j' } });
    await new Promise((r) => setTimeout(r, 400));
    expect(busquedas(llamadas)).toHaveLength(0);

    for (const valor of ['jo', 'jos', 'jose']) fireEvent.change(campo, { target: { value: valor } });
    const lista = await screen.findByRole('list', { name: 'Personas encontradas' });
    expect(busquedas(llamadas).map((l) => l.ruta)).toEqual(['/api/v1/plataforma/personas?q=jose&limite=20']);
    const resultado = within(lista).getByRole('button', { name: /José Demo Álvarez/ });
    expect(resultado).toHaveTextContent('jose.demo@ejemplo.com');
    expect(resultado).toHaveTextContent('Empleado');
    expect(await within(lista).findByText('Desarrollador')).toBeInTheDocument();
    // Avatar con la foto de Graph (ATLAS-ADR-0030): decorativo, el nombre ya está en el botón.
    expect(resultado.querySelector('img')).toHaveAttribute('src', PERSONAS[0]!.foto);

    fireEvent.click(resultado);

    expect(window.location.search).toBe(`?persona=${PERSONA}`);
    expect(await screen.findByRole('list', { name: 'Asignaciones' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'José Demo Álvarez' })).toBeInTheDocument();
    expect(campo).toHaveValue('');
    expect(screen.queryByRole('list', { name: 'Personas encontradas' })).toBeNull();
    // Con solo atlas.roles.ver no se asigna.
    expect(screen.queryByRole('button', { name: 'Asignar rol' })).toBeNull();
  });

  it('dice cuando nadie coincide y abre directo un id pegado', async () => {
    apiFalsa({ permisos: VER });
    window.history.pushState({}, '', '/administracion/roles');

    render(<App />);
    const campo = await screen.findByRole('searchbox', { name: /Nombre, correo o id/ });
    fireEvent.change(campo, { target: { value: 'nadie' } });
    expect(await screen.findByText('Nadie coincide con «nadie».')).toBeInTheDocument();

    fireEvent.change(campo, { target: { value: ` ${EXTERNA.toUpperCase()} ` } });
    fireEvent.submit(screen.getByRole('search', { name: 'Buscar personas' }));

    expect(window.location.search).toBe(`?persona=${EXTERNA}`);
    expect(await screen.findByRole('heading', { name: 'María Externa' })).toBeInTheDocument();
    expect(screen.getByText('Externo · Organismo Demo')).toBeInTheDocument();
    expect(screen.getByText('Suspendida')).toBeInTheDocument();
  });

  it('asigna un rol interno a un empleado con vencimiento opcional; a un externo no se le ofrece', async () => {
    const llamadas = apiFalsa({ permisos: GESTIONAR });
    window.history.pushState({}, '', `/administracion/roles?persona=${PERSONA}`);

    const { unmount } = render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Asignar rol' }));
    const hoja = screen.getByRole('dialog', { name: 'Asignar rol' });
    const roles = await within(hoja).findByRole('group', { name: 'Rol' });
    expect(within(roles).getAllByRole('radio').map((r) => (r as HTMLInputElement).value)).toEqual(['atlas.superadmin', 'atlas.desarrollador']);
    fireEvent.submit(within(hoja).getByRole('form', { name: 'Datos de la asignación' }));
    expect(within(hoja).getByRole('alert')).toHaveTextContent('Elige el rol');
    expect(llamadas.some((l) => l.init?.method === 'POST')).toBe(false);

    fireEvent.click(within(roles).getByRole('radio', { name: /Superadministrador/ }));
    fireEvent.change(within(hoja).getByLabelText(/Vence/), { target: { value: '2026-12-31' } });
    fireEvent.submit(within(hoja).getByRole('form', { name: 'Datos de la asignación' }));

    expect(await screen.findByText('José Demo Álvarez tiene ahora Superadministrador.')).toBeInTheDocument();
    const post = llamadas.find((l) => l.init?.method === 'POST')!;
    expect(post.ruta).toBe(`/api/v1/plataforma/personas/${PERSONA}/asignaciones`);
    expect(JSON.parse(String(post.init!.body))).toEqual({ rol: 'atlas.superadmin', vigenteHasta: '2026-12-31T23:59:00-05:00' });
    unmount();

    apiFalsa({ permisos: GESTIONAR });
    window.history.pushState({}, '', `/administracion/roles?persona=${EXTERNA}`);
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'María Externa' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Asignar rol' })).toBeNull();
  });

  it('si el servidor pide reautenticación al asignar, lo dice dentro de la hoja', async () => {
    apiFalsa({ permisos: GESTIONAR, cambio: REAUTENTICAR });
    window.history.pushState({}, '', `/administracion/roles?persona=${PERSONA}`);

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Asignar rol' }));
    const hoja = screen.getByRole('dialog', { name: 'Asignar rol' });
    fireEvent.click(await within(hoja).findByRole('radio', { name: /Desarrollador/ }));
    fireEvent.submit(within(hoja).getByRole('form', { name: 'Datos de la asignación' }));

    expect(await within(hoja).findByRole('link', { name: 'Confirmar con Microsoft' })).toBeInTheDocument();
  });
});

describe('invitaciones sin organizaciones externas', () => {
  it('en vez del formulario, explica y enlaza a Organizaciones si se pueden crear', async () => {
    apiFalsa({ permisos: ['atlas.invitaciones.gestionar', 'atlas.organizaciones.gestionar'] });
    window.history.pushState({}, '', '/administracion/invitaciones');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Invitar' }));
    const hoja = screen.getByRole('dialog', { name: 'Nueva invitación' });

    expect(await within(hoja).findByText('Aún no hay a quién invitar')).toBeInTheDocument();
    expect(within(hoja).getByRole('link', { name: 'Crear una organización' })).toHaveAttribute('href', '/administracion/organizaciones');
    expect(within(hoja).queryByRole('form')).toBeNull();
  });

  it('sin permiso para crear organizaciones, dice a quién pedírsela', async () => {
    apiFalsa({ permisos: ['atlas.invitaciones.gestionar'] });
    window.history.pushState({}, '', '/administracion/invitaciones');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Invitar' }));
    const hoja = screen.getByRole('dialog', { name: 'Nueva invitación' });

    expect(await within(hoja).findByText(/Pídele a quien administra las organizaciones/)).toBeInTheDocument();
    expect(within(hoja).queryByRole('link', { name: 'Crear una organización' })).toBeNull();
  });

  it('con una organización cliente muestra el formulario', async () => {
    apiFalsa({
      permisos: ['atlas.invitaciones.gestionar'],
      organizaciones: [{ id: 'c1', nombre: 'Organismo Demo', tipo: 'cliente', adminOrganizacionHabilitado: false }],
    });
    window.history.pushState({}, '', '/administracion/invitaciones');

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Invitar' }));

    expect(await screen.findByRole('form', { name: 'Datos de la invitación' })).toBeInTheDocument();
    expect(screen.queryByText('Aún no hay a quién invitar')).toBeNull();
  });
});
