import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { MATRIZ } from '../../../tests/fixtures/matriz';
import { App } from '../../app/App';
import { resolverNavegacion } from '../../app/modulos/resolver';
import { permisosDelRol, rolesDelGrupo } from './matriz';
import { moduloPermisos } from './modulo';

function json(cuerpo: unknown, status = 200) {
  return new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });
}

function apiFalsa(permisos: string[], matriz: Response | (() => Response) = () => json(MATRIZ)) {
  const fetch = vi.fn(async (ruta: string) => {
    if (ruta === '/api/v1/plataforma/yo')
      return json({
        sub: 's',
        personaId: 'p',
        nombre: 'Auditora Demo',
        tipoPrincipal: 'empleado',
        organizacionId: 'o',
        proveedor: 'simulado',
        roles: ['atlas.auditor'],
        permisos,
      });
    if (ruta === '/api/v1/plataforma/banderas') return json({});
    if (ruta.startsWith('/api/v1/plataforma/notificaciones')) return json({ noLeidas: 0, elementos: [] });
    if (ruta === '/api/v1/plataforma/permisos/matriz') return typeof matriz === 'function' ? matriz() : matriz;
    return new Response('', { status: 404 });
  });
  vi.stubGlobal('fetch', fetch);
  return fetch;
}

beforeEach(() => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
  );
});

afterEach(() => vi.unstubAllGlobals());

describe('manifiesto de Roles y permisos', () => {
  it('solo aparece con atlas.roles.ver, en «Más», y su página se carga de forma diferida', () => {
    const sin = resolverNavegacion([moduloPermisos], { banderas: {}, permisos: ['atlas.lector'] });
    const con = resolverNavegacion([moduloPermisos], { banderas: {}, permisos: ['atlas.roles.ver'] });

    expect(sin.modulos).toEqual([]);
    expect(con.mas.map((e) => e.ruta)).toEqual(['/permisos']);
    expect(con.barraInferior.map((e) => e.ruta)).not.toContain('/permisos');
    expect(moduloPermisos.rutas[0]?.cargar.toString()).toMatch(/import/);
  });

  it('agrupa roles por tipo y los permisos de un rol por módulo', () => {
    expect(rolesDelGrupo(MATRIZ.roles, 'interno')).toHaveLength(8);
    expect(rolesDelGrupo(MATRIZ.roles, 'externo')).toHaveLength(5);
    expect(rolesDelGrupo(MATRIZ.roles, 'todos')).toHaveLength(14);
    expect(permisosDelRol(MATRIZ, 'atlas.auditor').map((m) => [m.id, m.permisos.map((p) => p.codigo)])).toEqual([
      ['plataforma', ['atlas.roles.ver']],
    ]);
    expect(permisosDelRol(MATRIZ, 'atlas.responsable')).toEqual([]);
  });
});

describe('pantalla de la matriz', () => {
  it('sin atlas.roles.ver no se ve ni se pide la matriz', async () => {
    const fetch = apiFalsa(['atlas.lector']);
    window.history.pushState({}, '', '/permisos');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'No tienes acceso' })).toBeInTheDocument();
    expect(fetch.mock.calls.map(([ruta]) => ruta)).not.toContain('/api/v1/plataforma/permisos/matriz');
  });

  it('en escritorio muestra una tabla por módulo con los roles internos como columnas', async () => {
    apiFalsa(['atlas.roles.ver']);
    window.history.pushState({}, '', '/permisos');

    render(<App />);

    const tabla = await screen.findByRole('table', { name: 'Permisos de Plataforma por rol' });
    const columnas = within(tabla).getAllByRole('columnheader').map((c) => c.textContent);
    expect(columnas).toEqual([
      'Permiso',
      'Superadministrador',
      'Líder técnico',
      'Desarrollador',
      'Soporte',
      'Negocio',
      'Gobierno de IA',
      'Auditor',
      'Lector',
    ]);
    const fila = within(tabla).getByRole('row', { name: /atlas\.roles\.ver/ });
    expect(within(fila).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Sí', 'Sí', 'No', 'No', 'No', 'No', 'Sí', 'No']);
    expect(screen.getByRole('table', { name: 'Permisos de Hola (ejemplo) por rol' })).toBeInTheDocument();
  });

  it('cambiar a roles externos cambia las columnas y las tarjetas por módulo', async () => {
    apiFalsa(['atlas.roles.ver']);
    window.history.pushState({}, '', '/permisos');

    render(<App />);
    fireEvent.click(await screen.findByRole('radio', { name: 'Externos (5)' }));

    const tabla = screen.getByRole('table', { name: 'Permisos de Plataforma por rol' });
    expect(within(tabla).getAllByRole('columnheader')).toHaveLength(6);
    expect(within(screen.getByRole('list', { name: 'Roles con atlas.invitaciones.gestionar' })).getByText('Administrador de la organización')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Roles con atlas.roles.ver' })).toBeNull();
  });

  it('en el móvil se puede ver por rol: descripción, total y permisos agrupados', async () => {
    apiFalsa(['atlas.roles.ver']);
    window.history.pushState({}, '', '/permisos');

    render(<App />);
    fireEvent.click(await screen.findByRole('radio', { name: 'Rol' }));
    fireEvent.click(within(screen.getByRole('group', { name: 'Rol' })).getByRole('radio', { name: 'Auditor' }));

    const rol = screen.getByRole('region', { name: 'Auditor' });
    expect(rol).toHaveTextContent('Lectura de auditoría y evidencias.');
    expect(rol).toHaveTextContent('1 permiso');
    expect(screen.getByRole('region', { name: 'Plataforma' })).toHaveTextContent('atlas.roles.ver');
  });

  it('si la API falla lo dice', async () => {
    apiFalsa(['atlas.roles.ver'], () => new Response('', { status: 500 }));
    window.history.pushState({}, '', '/permisos');

    render(<App />);

    // El cliente de consultas reintenta una vez antes de dar el error.
    expect(await screen.findByRole('alert', {}, { timeout: 5000 })).toHaveTextContent('No pudimos cargar la matriz de permisos');
  });
});
