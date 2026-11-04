import type { ModuloAtlas, NombreIcono } from '../modulos/contrato';
import type { NavegacionResuelta } from '../modulos/resolver';

/**
 * Búsqueda global (§8.9), estructura de la HU 1.6: hoy busca en la navegación y las acciones del shell, en el
 * dispositivo. Cada módulo declara su proveedor (`busqueda` del manifiesto); cuando llegue su fase, sus resultados
 * vendrán del servidor ya filtrados por permisos y se agregan como un grupo más.
 */
export type GrupoResultado = 'Ir a' | 'Acciones';

export interface Resultado {
  id: string;
  grupo: GrupoResultado;
  titulo: string;
  descripcion?: string;
  icono: NombreIcono;
  /** Ruta interna, o la acción que ejecuta el shell. */
  destino: { ruta: string } | { accion: AccionShell };
  palabras?: string;
}

export type AccionShell = 'tema_claro' | 'tema_oscuro' | 'cerrar_sesion';

export const normalizar = (texto: string): string =>
  texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();

const PAGINAS_NUCLEO: Resultado[] = [
  {
    id: 'pagina:notificaciones',
    grupo: 'Ir a',
    titulo: 'Notificaciones',
    descripcion: 'Centro de notificaciones',
    icono: 'notificaciones',
    destino: { ruta: '/notificaciones' },
    palabras: 'avisos alertas push',
  },
  {
    id: 'pagina:ajustes',
    grupo: 'Ir a',
    titulo: 'Ajustes',
    descripcion: 'Tema, notificaciones push y cuenta',
    icono: 'ajustes',
    destino: { ruta: '/ajustes' },
    palabras: 'preferencias configuracion tema oscuro claro cuenta perfil',
  },
];

const ACCIONES: Resultado[] = [
  {
    id: 'accion:tema_oscuro',
    grupo: 'Acciones',
    titulo: 'Usar el tema oscuro',
    icono: 'ajustes',
    destino: { accion: 'tema_oscuro' },
    palabras: 'modo noche',
  },
  {
    id: 'accion:tema_claro',
    grupo: 'Acciones',
    titulo: 'Usar el tema claro',
    icono: 'ajustes',
    destino: { accion: 'tema_claro' },
    palabras: 'modo dia',
  },
  {
    id: 'accion:cerrar_sesion',
    grupo: 'Acciones',
    titulo: 'Cerrar sesión',
    icono: 'ajustes',
    destino: { accion: 'cerrar_sesion' },
    palabras: 'salir logout',
  },
];

export function construirIndice(navegacion: NavegacionResuelta): Resultado[] {
  const deNavegacion: Resultado[] = navegacion.lateral
    .filter((entrada) => entrada.ruta !== '/buscar')
    .map((entrada) => ({
      id: `ruta:${entrada.ruta}`,
      grupo: 'Ir a',
      titulo: entrada.modulo?.nombre ?? entrada.etiqueta,
      descripcion: entrada.modulo?.descripcion,
      icono: entrada.icono ?? 'inicio',
      destino: { ruta: entrada.ruta },
      palabras: [entrada.etiqueta, ...(entrada.modulo?.busqueda?.tipos ?? [])].join(' '),
    }));
  return [...deNavegacion, ...PAGINAS_NUCLEO, ...ACCIONES];
}

/** Coinciden todas las palabras de la consulta; primero lo que empieza por la consulta en el título. */
export function buscar(indice: readonly Resultado[], consulta: string): Resultado[] {
  const q = normalizar(consulta);
  if (!q) return indice.filter((r) => r.grupo === 'Ir a');
  const terminos = q.split(/\s+/);
  return indice
    .map((r) => ({ r, texto: normalizar(`${r.titulo} ${r.descripcion ?? ''} ${r.palabras ?? ''}`), titulo: normalizar(r.titulo) }))
    .filter(({ texto }) => terminos.every((t) => texto.includes(t)))
    .sort((a, b) => Number(b.titulo.startsWith(q)) - Number(a.titulo.startsWith(q)))
    .map(({ r }) => r);
}

/** Proveedores de búsqueda declarados por los módulos visibles, para mostrar en qué se podrá buscar. */
export function proveedoresDeModulos(modulos: readonly ModuloAtlas[]): { modulo: string; tipos: string[] }[] {
  return modulos.filter((m) => m.busqueda).map((m) => ({ modulo: m.nombre, tipos: m.busqueda!.tipos }));
}
