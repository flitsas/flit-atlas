import { describe, expect, it } from 'vitest';

import { MODULOS_REGISTRADOS } from '../modulos/registro';
import { resolverNavegacion } from '../modulos/resolver';
import { buscar, construirIndice, normalizar, proveedoresDeModulos } from './indice';

const indice = construirIndice(resolverNavegacion(MODULOS_REGISTRADOS, { banderas: {} }));

describe('búsqueda global', () => {
  it('no distingue tildes ni mayúsculas', () => {
    expect(normalizar('  Documentación ')).toBe('documentacion');
    expect(buscar(indice, 'DOCUMENTACION').map((r) => r.titulo)).toContain('Documentación');
  });

  it('sin consulta muestra a dónde ir; con consulta incluye acciones', () => {
    expect(buscar(indice, '').every((r) => r.grupo === 'Ir a')).toBe(true);
    expect(buscar(indice, 'oscuro').map((r) => r.id)).toContain('accion:tema_oscuro');
    expect(buscar(indice, 'cerrar').map((r) => r.id)).toEqual(['accion:cerrar_sesion']);
  });

  it('busca por los tipos que declara cada módulo y pone primero lo que empieza por la consulta', () => {
    expect(buscar(indice, 'turno').map((r) => r.titulo)).toEqual(['Soporte de los sábados']);
    expect(buscar(indice, 'cat')[0]?.titulo).toBe('Catálogo de aplicativos');
  });

  it('no ofrece módulos apagados', () => {
    const sinSoporte = construirIndice(resolverNavegacion(MODULOS_REGISTRADOS, { banderas: { 'modulo.soporte': false } }));
    expect(buscar(sinSoporte, 'soporte')).toEqual([]);
  });

  it('lista los proveedores de búsqueda de los módulos', () => {
    expect(proveedoresDeModulos(MODULOS_REGISTRADOS).map((p) => p.modulo)).toContain('Soporte de los sábados');
  });
});
