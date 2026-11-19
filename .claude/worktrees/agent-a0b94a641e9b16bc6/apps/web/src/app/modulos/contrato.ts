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
  | 'ejemplo';

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

export interface OfflineModulo {
  /** Recursos de la API que se guardan en IndexedDB si los permisos lo permiten (§11). */
  recursos: string[];
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
  busqueda?: BusquedaModulo;
  offline?: OfflineModulo;
  /** Bandera `modulo.<id>`; el servidor es la autoridad (`GET /api/v1/plataforma/banderas`). */
  bandera: `modulo.${string}`;
  /** Valor de la bandera cuando el servidor no la define. */
  banderaPorDefecto: boolean;
}
