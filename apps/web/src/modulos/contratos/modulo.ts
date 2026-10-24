import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Contratos de integración (prompt maestro §8.4, ATLAS-ADR-0035). Desde la HU 3.2: la lista de contratos publicados
 * desde la CI y sus versiones; desde la HU 3.4, el consolidado por ambiente (versión en dev, qa y pdn, desfases y
 * breaking pendientes); desde la HU 3.6 y 3.9, la pantalla del contrato (`/contratos/<id>`, con operaciones, matriz de
 * obligatoriedad, tablas de referencia, versiones con diff, guías y canales); desde la HU 3.7, el enlace directo a una
 * guía; desde la HU 4.1, los paquetes con revisiones (`/contratos/paquetes`); desde las HU 4.3 y 4.4, «Compartido con» en
 * el paquete y el portal de clientes (`/portal`, ATLAS-ADR-0043): Inicio del cliente, paquete y contrato compartidos con
 * el spec podado, guías y cambios. Un externo solo ve la entrada «Portal»: las demás exigen ver_internos. Desde la HU 4.9, los favoritos
 * se ven sin conexión; desde la HU 4.8, deprecar una versión con su fecha de retiro (ATLAS-ADR-0046).
 */
export const moduloContratos: ModuloAtlas = {
  id: 'contratos',
  nombre: 'Contratos de integración',
  descripcion: 'OpenAPI, AsyncAPI y proto versionados desde los repos, con diff, guías, paquetes y exportación.',
  icono: 'contratos',
  estado: 'disponible',
  rutas: [
    { path: '/contratos', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Contratos') },
    // Las rutas estáticas van antes que `/contratos/:id` (también en paginas/Contratos.tsx). HU 3.7: enlace directo a una
    // guía; la pestaña «Guías» de la pantalla del contrato (HU 3.6) monta el mismo componente.
    { path: '/contratos/:contratoId/guias/:guiaId', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Guia') },
    { path: '/contratos/consolidado', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Consolidado') },
    // HU 4.1: leer paquetes exige ver_internos; armarlos, atlas.paquetes.gestionar (el servidor dice si puede con `puedeCrear`).
    { path: '/contratos/paquetes', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paquetes/Paquetes') },
    // HU 4.4: el portal de clientes; lo que ve cada persona lo recorta el servidor (organización, alcance y vigencia).
    { path: '/portal', permiso: 'atlas.portal.ver', cargar: () => import('./portal/Portal') },
  ],
  navegacion: [
    { etiqueta: 'Contratos', ruta: '/contratos', movil: 'mas', orden: 40, permiso: 'atlas.contratos.ver_internos' },
    { etiqueta: 'Consolidado', ruta: '/contratos/consolidado', movil: 'mas', orden: 41, permiso: 'atlas.contratos.ver_internos' },
    { etiqueta: 'Paquetes', ruta: '/contratos/paquetes', movil: 'mas', orden: 42, permiso: 'atlas.contratos.ver_internos' },
    // Solo para externos: en el móvil va en la barra inferior (es su pantalla principal).
    { etiqueta: 'Portal', ruta: '/portal', movil: 'barra_inferior', orden: 20, icono: 'portal', permiso: 'atlas.portal.ver', audiencia: 'externos' },
  ],
  widgetsInicio: [
    {
      id: 'contratos-cambios',
      titulo: 'Contratos publicados',
      orden: 30,
      permiso: 'atlas.contratos.ver_internos',
      cargar: () => import('./widgets/Cambios'),
    },
    {
      id: 'portal-inicio-cliente',
      titulo: 'Lo que FLIT comparte contigo',
      orden: 5,
      permiso: 'atlas.portal.ver',
      audiencia: 'externos',
      ancho: 'doble',
      cargar: () => import('./portal/WidgetPortal'),
    },
  ],
  // El proveedor `contratos` del servidor (BusquedaContratos) entrega estos tipos.
  busqueda: { proveedor: 'contratos', tipos: ['contrato', 'operacion'] },
  // HU 4.9 (ATLAS-ADR-0034): los favoritos (contratos internos y lo compartido en el portal) se guardan sin conexión con
  // el mismo TTL; su sincronizador va en el chunk de favoritos.
  offline: { recursos: [], ttlHoras: 72, cargarSincronizador: () => import('./favoritos/sinConexion') },
  bandera: 'modulo.contratos',
  banderaPorDefecto: true,
};
