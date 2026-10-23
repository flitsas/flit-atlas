import { describe, expect, it } from 'vitest';

import type { ModuloAtlas } from './contrato';
import { MODULOS_REGISTRADOS } from './registro';
import {
  banderaActiva,
  MAXIMO_BARRA_INFERIOR,
  moduloDeRuta,
  resolverNavegacion,
  tienePermiso,
  validarRegistro,
} from './resolver';

const pagina = async () => ({ default: () => null });

function modulo(id: string, parcial: Partial<ModuloAtlas> = {}): ModuloAtlas {
  return {
    id,
    nombre: id,
    descripcion: `Módulo ${id}`,
    icono: 'catalogo',
    estado: 'disponible',
    rutas: [{ path: `/${id}`, permiso: `atlas.${id}.ver`, cargar: pagina }],
    navegacion: [{ etiqueta: id, ruta: `/${id}`, movil: 'mas', orden: 50 }],
    widgetsInicio: [{ id: `${id}-widget`, titulo: id, orden: 20, permiso: `atlas.${id}.ver`, cargar: pagina }],
    bandera: `modulo.${id}`,
    banderaPorDefecto: true,
    ...parcial,
  };
}

describe('banderas', () => {
  it('el servidor manda; si no define la bandera, vale la del manifiesto', () => {
    const m = modulo('soporte', { banderaPorDefecto: true });
    expect(banderaActiva(m, {})).toBe(true);
    expect(banderaActiva(m, { 'modulo.soporte': false })).toBe(false);
    expect(banderaActiva(modulo('x', { banderaPorDefecto: false }), {})).toBe(false);
    expect(banderaActiva(modulo('x', { banderaPorDefecto: false }), { 'modulo.x': true })).toBe(true);
  });

  it('un módulo apagado no aparece en la navegación, ni en Inicio, ni en la barra inferior', () => {
    const soporte = modulo('soporte', { navegacion: [{ etiqueta: 'Soporte', ruta: '/soporte', movil: 'barra_inferior', orden: 30 }] });
    const nav = resolverNavegacion([soporte, modulo('catalogo')], { banderas: { 'modulo.soporte': false } });

    expect(nav.modulos.map((m) => m.id)).toEqual(['catalogo']);
    expect(nav.lateral.map((e) => e.ruta)).not.toContain('/soporte');
    expect(nav.barraInferior.map((e) => e.ruta)).toEqual(['/', '/buscar']);
    expect(nav.widgets.map((w) => w.id)).toEqual(['catalogo-widget']);
  });
});

describe('permisos', () => {
  it('mientras /yo no traiga permisos (HU 1.4), no se filtra por permiso', () => {
    expect(tienePermiso('atlas.soporte.ver', undefined)).toBe(true);
    const nav = resolverNavegacion([modulo('soporte'), modulo('catalogo')], { banderas: {} });
    expect(nav.modulos).toHaveLength(2);
    expect(nav.widgets).toHaveLength(2);
  });

  it('con la lista de permisos, solo se ve lo permitido', () => {
    expect(tienePermiso(undefined, [])).toBe(true);
    expect(tienePermiso('atlas.soporte.ver', [])).toBe(false);

    const nav = resolverNavegacion([modulo('soporte'), modulo('catalogo')], {
      banderas: {},
      permisos: ['atlas.catalogo.ver'],
    });

    expect(nav.modulos.map((m) => m.id)).toEqual(['catalogo']);
    expect(nav.widgets.map((w) => w.id)).toEqual(['catalogo-widget']);
    expect(nav.lateral.map((e) => e.ruta)).toEqual(['/', '/buscar', '/catalogo']);
  });

  it('filtra también las entradas y widgets que piden un permiso propio', () => {
    const m = modulo('soporte', {
      navegacion: [
        { etiqueta: 'Soporte', ruta: '/soporte', movil: 'mas', orden: 30 },
        { etiqueta: 'Aprobar cambios', ruta: '/soporte/aprobaciones', movil: 'mas', orden: 31, permiso: 'atlas.soporte.aprobar' },
      ],
      widgetsInicio: [
        { id: 'sabado', titulo: 'Sábado', orden: 1, cargar: pagina },
        { id: 'pendientes', titulo: 'Pendientes', orden: 2, permiso: 'atlas.soporte.aprobar', cargar: pagina },
      ],
    });

    const lector = resolverNavegacion([m], { banderas: {}, permisos: ['atlas.soporte.ver'] });
    const lider = resolverNavegacion([m], { banderas: {}, permisos: ['atlas.soporte.ver', 'atlas.soporte.aprobar'] });

    expect(lector.mas.map((e) => e.etiqueta)).toEqual(['Soporte']);
    expect(lector.widgets.map((w) => w.id)).toEqual(['sabado']);
    expect(lider.mas.map((e) => e.etiqueta)).toEqual(['Soporte', 'Aprobar cambios']);
    expect(lider.widgets.map((w) => w.id)).toEqual(['sabado', 'pendientes']);
  });
});

describe('navegación', () => {
  it('ordena el núcleo y los módulos, y la barra inferior se llena hasta el máximo; el resto va a «Más»', () => {
    const enBarra = (id: string, orden: number) =>
      modulo(id, { navegacion: [{ etiqueta: id, ruta: `/${id}`, movil: 'barra_inferior', orden }] });
    const nav = resolverNavegacion([enBarra('c', 40), enBarra('a', 20), enBarra('b', 30), modulo('d')], { banderas: {} });

    expect(nav.lateral.map((e) => e.ruta)).toEqual(['/', '/buscar', '/a', '/b', '/c', '/d']);
    expect(nav.barraInferior).toHaveLength(MAXIMO_BARRA_INFERIOR - 1);
    expect(nav.barraInferior.map((e) => e.ruta)).toEqual(['/', '/buscar', '/a', '/b']);
    expect(nav.mas.map((e) => e.ruta)).toEqual(['/c', '/d']);
  });

  it('la entrada hereda el ícono de su módulo', () => {
    const nav = resolverNavegacion([modulo('equipo', { icono: 'equipo' })], { banderas: {} });
    expect(nav.mas[0]?.icono).toBe('equipo');
  });

  it('encuentra el módulo dueño de una ruta y sus subrutas', () => {
    const modulos = [modulo('soporte'), modulo('catalogo')];
    expect(moduloDeRuta(modulos, '/soporte')?.id).toBe('soporte');
    expect(moduloDeRuta(modulos, '/soporte/turnos/1')?.id).toBe('soporte');
    expect(moduloDeRuta(modulos, '/soportex')).toBeUndefined();
  });
});

describe('registro', () => {
  it('rechaza ids repetidos, banderas mal nombradas y rutas en uso', () => {
    expect(() => validarRegistro([modulo('a'), modulo('a')])).toThrow(/dos veces/);
    expect(() => validarRegistro([modulo('a', { bandera: 'modulo.b' })])).toThrow(/modulo\.a/);
    expect(() => validarRegistro([modulo('a', { rutas: [{ path: '/buscar', cargar: pagina }] })])).toThrow(/en uso/);
  });

  it('los módulos del MVP quedan en la barra inferior del móvil: Inicio, Buscar, Soporte y Asistente (más «Más»)', () => {
    const nav = resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {} });
    expect(nav.barraInferior.map((e) => e.etiqueta)).toEqual(['Inicio', 'Buscar', 'Soporte', 'Asistente']);
    // «Roles y permisos» (HU 1.4) también va en «Más»; «hola» (HU 1.9) está apagado por defecto.
    expect(nav.mas.map((e) => e.etiqueta)).toEqual(['Catálogo', 'Equipo', 'Contratos', 'Documentación', 'Roles y permisos']);
    const mvp = MODULOS_REGISTRADOS.filter((m) => !['permisos', 'hola'].includes(m.id));
    expect(mvp.every((m) => m.estado === 'proximamente' && m.banderaPorDefecto)).toBe(true);
  });

  it('cada página y cada widget se carga de forma diferida', () => {
    for (const m of MODULOS_REGISTRADOS) {
      for (const r of m.rutas) expect(r.cargar.toString()).toMatch(/import/);
      for (const w of m.widgetsInicio ?? []) expect(w.cargar.toString()).toMatch(/import/);
    }
  });
});
