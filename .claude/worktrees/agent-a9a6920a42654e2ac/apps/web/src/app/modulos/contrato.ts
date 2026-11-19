import type { ComponentType } from 'react';

/**
 * Contrato de módulo del frontend (prompt maestro §10, ATLAS-ADR-0016). Cada `src/modulos/<id>/modulo.ts` exporta un
 * `ModuloAtlas` y el shell lo registra en `registro.ts`: agregar un módulo no obliga a tocar los demás.
 */

/** Íconos que el shell sabe pintar (`iconos.tsx`). Un nombre, no un componente: el módulo no arrastra la librería. */
export type NombreIcono =
  | 'inicio'
  | 'buscar'
  | 'mas'
  | 'catalogo'
  | 'equipo'
  | 'calendario'
  | 'contratos'
  | 'documentos'
  | 'asistente'
  | 'notificaciones'
  | 'ajustes'
  | 'permisos'
  | 'ejemplo'
  | 'administracion';

/** Carga diferida: cada página y cada widget va en su propio chunk. */
export type Cargador = () => Promise<{ default: ComponentType }>;

export interface RutaModulo {
  path: string;
  /** Permiso `atlas.<modulo>.<accion>` (§7.4). Sin permiso, la ruta muestra «No tienes acceso». */
  permiso?: string;
  cargar: Cargador;
}

export interface EntradaNavegacion {
  etiqueta: string;
  ruta: string;
  /** En el móvil: en la barra inferior (máximo cinco con Inicio, Buscar y Más) o dentro de «Más». */
  movil: 'barra_inferior' | 'mas';
  /** Orden en la barra lateral y en la inferior; el núcleo usa 0–9 (Inicio, Buscar) y 90+ (Más). */
  orden: number;
  icono?: NombreIcono;
  permiso?: string;
}

export interface WidgetInicio {
  id: string;
  titulo: string;
  /** Orden en Inicio; los del núcleo usan 0–9. */
  orden: number;
  permiso?: string;
  /** Ancho en la cuadrícula de escritorio. */
  ancho?: 'normal' | 'doble';
  cargar: Cargador;
}

export interface BusquedaModulo {
  /** Proveedor de búsqueda en el servidor (`IModuloAtlas.ProveedoresBusqueda`). */
  proveedor: string;
  tipos: string[];
}

/**
 * Lo que recibe una pestaña de la ficha del aplicativo (ATLAS-ADR-0028): solo la identidad del aplicativo. La pestaña
 * pide a su propio módulo lo que necesite (responsables, contratos…); el catálogo no conoce a los demás módulos.
 */
export interface AplicativoDeFicha {
  id: string;
  slug: string;
  nombre: string;
  tipo: 'producto' | 'modulo' | 'servicio';
  grupo: string;
}

/** Carga diferida de una pestaña de la ficha: va en el chunk del módulo que la aporta. */
export type CargadorPestana = () => Promise<{ default: ComponentType<{ aplicativo: AplicativoDeFicha }> }>;

/**
 * Pestaña que un módulo agrega a la ficha del aplicativo (`/catalogo/<slug>`), la página a la que enlazan todos los
 * módulos (prompt maestro §8.1). Los espacios previstos son `responsables`, `contratos`, `documentos`, `diagramas`,
 * `versiones` (versión por ambiente), `soporte` (soporte de turno) e `ia` (casos de IA): con uno de esos `id`, la
 * pestaña ocupa su espacio (que mientras tanto muestra «Próximamente»); con otro, se agrega después.
 */
export interface PestanaFicha {
  id: string;
  titulo: string;
  /** Orden entre las pestañas que no son espacios previstos; los espacios ya tienen el suyo. */
  orden: number;
  permiso?: string;
  /** Solo para estos tipos de aplicativo; todos si no se dice. */
  tipos?: AplicativoDeFicha['tipo'][];
  cargar: CargadorPestana;
}

/** Un recurso de la API que el shell guarda en el dispositivo para verlo sin conexión (HU 2.12, ATLAS-ADR-0034). */
export interface RecursoSinConexion {
  /** Clave en IndexedDB (`soporte.turnos`): la misma con la que el cliente del módulo lo lee sin red. */
  clave: string;
  /** Ruta de la API; se calcula al sincronizar (p. ej. con la fecha de hoy). */
  ruta: () => string;
  /** Solo se guarda si la persona lo tiene; si lo pierde, se borra. */
  permiso: string;
}

export interface OfflineModulo {
  /** Recursos que se guardan en IndexedDB si los permisos lo permiten (§11); sin teléfonos. */
  recursos: RecursoSinConexion[];
  ttlHoras: number;
}

export interface ModuloAtlas {
  id: string;
  nombre: string;
  /** Una línea para Inicio, la búsqueda y la página «Próximamente». */
  descripcion: string;
  icono: NombreIcono;
  /** `disponible`, o `proximamente` mientras su fase no llega: la navegación ya existe y lleva a una página digna. */
  estado: 'disponible' | 'proximamente';
  rutas: RutaModulo[];
  navegacion: EntradaNavegacion[];
  widgetsInicio?: WidgetInicio[];
  /** Pestañas que el módulo agrega a la ficha del aplicativo del catálogo (ATLAS-ADR-0028). */
  pestanasFicha?: PestanaFicha[];
  busqueda?: BusquedaModulo;
  offline?: OfflineModulo;
  /** Bandera `modulo.<id>`; el servidor es la autoridad (`GET /api/v1/plataforma/banderas`). */
  bandera: `modulo.${string}`;
  /** Valor de la bandera cuando el servidor no la define. */
  banderaPorDefecto: boolean;
}
