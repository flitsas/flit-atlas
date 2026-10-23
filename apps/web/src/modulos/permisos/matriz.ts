import type { MatrizPermisos, ModuloMatriz, RolMatriz } from './api';

/** Qué roles se muestran como columnas (escritorio) y en las tarjetas por módulo (móvil). */
export type Grupo = 'interno' | 'externo' | 'todos';

/** Los implícitos (responsable del aplicativo) solo aparecen en «Todos». */
export const rolesDelGrupo = (roles: readonly RolMatriz[], grupo: Grupo): RolMatriz[] =>
  grupo === 'todos' ? [...roles] : roles.filter((r) => r.tipo === grupo);

/** Permisos de un rol, agrupados por módulo (solo los módulos donde tiene alguno). */
export function permisosDelRol(matriz: MatrizPermisos, rol: string): ModuloMatriz[] {
  return matriz.modulos
    .map((m) => ({ ...m, permisos: m.permisos.filter((p) => p.roles.includes(rol)) }))
    .filter((m) => m.permisos.length > 0);
}
