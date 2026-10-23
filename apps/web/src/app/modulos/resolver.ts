import type { AplicativoDeFicha, EntradaNavegacion, ModuloAtlas, PestanaFicha, WidgetInicio } from './contrato';

/**
 * Qué ve cada persona: módulos activos por bandera y entradas filtradas por permiso. Funciones puras, para que el
 * shell, la búsqueda y las pruebas usen exactamente la misma regla.
 *
 * El servidor es la autoridad (ATLAS-ADR-0016): un módulo apagado responde 404 y un permiso que falta responde 403.
 * Aquí solo se decide qué se muestra.
 */
export interface AccesoUsuario {
  /** Banderas que entrega el servidor (`modulo.<id>` → activo). Las que no vengan usan `banderaPorDefecto`. */
  banderas: Readonly<Record<string, boolean>>;
  /**
   * Permisos `atlas.<modulo>.<accion>` de la persona. Llegan en `/yo` con la HU 1.4; mientras `/yo` no los traiga
   * (`undefined`), no se filtra por permiso y los módulos se muestran solo según su bandera.
   */
  permisos?: readonly string[];
}

/** Entradas del núcleo (no son módulos: siempre están). */
export const NAVEGACION_NUCLEO: readonly EntradaNavegacion[] = [
  { etiqueta: 'Inicio', ruta: '/', movil: 'barra_inferior', orden: 0, icono: 'inicio' },
  { etiqueta: 'Buscar', ruta: '/buscar', movil: 'barra_inferior', orden: 5, icono: 'buscar' },
];

/** La barra inferior lleva a lo sumo cinco botones, el último siempre «Más» (§11). */
export const MAXIMO_BARRA_INFERIOR = 5;

export function banderaActiva(modulo: ModuloAtlas, banderas: AccesoUsuario['banderas']): boolean {
  return banderas[modulo.bandera] ?? modulo.banderaPorDefecto;
}

export function tienePermiso(permiso: string | undefined, permisos: AccesoUsuario['permisos']): boolean {
  if (!permiso || permisos === undefined) return true;
  return permisos.includes(permiso);
}

export interface EntradaResuelta extends EntradaNavegacion {
  /** Módulo dueño de la entrada; `undefined` en las del núcleo. */
  modulo?: ModuloAtlas;
}

export interface WidgetResuelto extends WidgetInicio {
  modulo?: ModuloAtlas;
}

export interface NavegacionResuelta {
  modulos: ModuloAtlas[];
  /** Barra lateral de escritorio: núcleo y módulos, en orden. */
  lateral: EntradaResuelta[];
  /** Barra inferior del móvil, sin «Más» (lo agrega el shell al final). */
  barraInferior: EntradaResuelta[];
  /** Lo que va en la hoja «Más» del móvil. */
  mas: EntradaResuelta[];
  widgets: WidgetResuelto[];
}

const porOrden = <T extends { orden: number }>(a: T, b: T) => a.orden - b.orden;

export function modulosVisibles(modulos: readonly ModuloAtlas[], acceso: AccesoUsuario): ModuloAtlas[] {
  return modulos.filter(
    (m) =>
      banderaActiva(m, acceso.banderas) &&
      // Un módulo sin ninguna ruta permitida no aparece en la navegación.
      (m.rutas.length === 0 || m.rutas.some((r) => tienePermiso(r.permiso, acceso.permisos))),
  );
}

export function resolverNavegacion(modulos: readonly ModuloAtlas[], acceso: AccesoUsuario): NavegacionResuelta {
  const activos = modulosVisibles(modulos, acceso);
  const deModulos: EntradaResuelta[] = activos.flatMap((m) =>
    m.navegacion
      .filter((n) => tienePermiso(n.permiso, acceso.permisos))
      .map((n) => ({ ...n, icono: n.icono ?? m.icono, modulo: m })),
  );
  const todas = [...NAVEGACION_NUCLEO, ...deModulos].sort(porOrden);

  const candidatas = todas.filter((n) => n.movil === 'barra_inferior');
  const barraInferior = candidatas.slice(0, MAXIMO_BARRA_INFERIOR - 1);
  const desbordadas = new Set(candidatas.slice(MAXIMO_BARRA_INFERIOR - 1));
  const mas = todas.filter((n) => n.movil === 'mas' || desbordadas.has(n));

  const widgets: WidgetResuelto[] = activos
    .flatMap((m) => (m.widgetsInicio ?? []).map((w) => ({ ...w, modulo: m })))
    .filter((w) => tienePermiso(w.permiso, acceso.permisos))
    .sort(porOrden);

  return { modulos: activos, lateral: todas, barraInferior, mas, widgets };
}

export interface PestanaResuelta extends PestanaFicha {
  modulo: ModuloAtlas;
}

export interface PestanasFichaResueltas {
  /** Las que se muestran: módulo con la bandera activa, permiso de la persona y tipo del aplicativo. */
  visibles: PestanaResuelta[];
  /** Ids que aporta un módulo activo pero la persona no tiene el permiso: su espacio no se muestra. */
  sinPermiso: Set<string>;
}

/**
 * Pestañas de la ficha del aplicativo (ATLAS-ADR-0028) que aportan los módulos: el catálogo las pinta sin importar a
 * ningún módulo. Si dos módulos aportan el mismo id, gana el primero registrado.
 */
export function resolverPestanasFicha(
  modulos: readonly ModuloAtlas[],
  acceso: AccesoUsuario,
  tipo?: AplicativoDeFicha['tipo'],
): PestanasFichaResueltas {
  const visibles: PestanaResuelta[] = [];
  const sinPermiso = new Set<string>();
  const vistos = new Set<string>();
  for (const modulo of modulos.filter((m) => banderaActiva(m, acceso.banderas))) {
    for (const pestana of modulo.pestanasFicha ?? []) {
      if (vistos.has(pestana.id) || (tipo && pestana.tipos && !pestana.tipos.includes(tipo))) continue;
      vistos.add(pestana.id);
      if (tienePermiso(pestana.permiso, acceso.permisos)) visibles.push({ ...pestana, modulo });
      else sinPermiso.add(pestana.id);
    }
  }
  return { visibles: visibles.sort(porOrden), sinPermiso };
}

/** Módulo dueño de una ruta (`/soporte/turnos` → soporte), aunque esté apagado. */
export function moduloDeRuta(modulos: readonly ModuloAtlas[], ruta: string): ModuloAtlas | undefined {
  return modulos.find((m) =>
    m.rutas.some((r) => ruta === r.path || ruta.startsWith(r.path.endsWith('/') ? r.path : `${r.path}/`)),
  );
}

/** Revisa que los módulos registrados no choquen entre sí; el registro lo llama al cargar. */
export function validarRegistro(modulos: readonly ModuloAtlas[]): void {
  const ids = new Set<string>();
  const rutas = new Set<string>(NAVEGACION_NUCLEO.map((n) => n.ruta));
  for (const m of modulos) {
    if (ids.has(m.id)) throw new Error(`Módulo registrado dos veces: «${m.id}».`);
    ids.add(m.id);
    if (m.bandera !== `modulo.${m.id}`) throw new Error(`La bandera de «${m.id}» debe ser «modulo.${m.id}».`);
    for (const r of m.rutas) {
      if (rutas.has(r.path)) throw new Error(`La ruta «${r.path}» de «${m.id}» ya está en uso.`);
      rutas.add(r.path);
    }
  }
}
