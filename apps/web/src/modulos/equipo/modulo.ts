import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Equipo y responsables (prompt maestro §8.2). Desde la HU 2.2 trae el directorio interno sincronizado con Graph
 * (ATLAS-ADR-0030) y desde la HU 2.3 los responsables (ATLAS-ADR-0031): reparto, carga, historial, traspasos,
 * CODEOWNERS y la pestaña «Responsables» de la ficha de cada aplicativo con «¿A quién llamo?».
 */
export const moduloEquipo: ModuloAtlas = {
  id: 'equipo',
  nombre: 'Equipo y responsables',
  descripcion: 'Quién es responsable principal y secundario de cada producto y módulo, su carga, su historial y su traspaso.',
  icono: 'equipo',
  estado: 'disponible',
  rutas: [
    { path: '/equipo', permiso: 'atlas.equipo.ver', cargar: () => import('./paginas/Equipo') },
    { path: '/equipo/responsables', permiso: 'atlas.equipo.ver', cargar: () => import('./paginas/Responsables') },
  ],
  navegacion: [
    { etiqueta: 'Equipo', ruta: '/equipo', movil: 'mas', orden: 25 },
    { etiqueta: 'Responsables', ruta: '/equipo/responsables', movil: 'mas', orden: 26, permiso: 'atlas.equipo.ver' },
  ],
  widgetsInicio: [
    {
      id: 'equipo-mis-responsabilidades',
      titulo: 'Mis responsabilidades',
      orden: 20,
      permiso: 'atlas.equipo.ver',
      cargar: () => import('./widgets/MisResponsabilidades'),
    },
  ],
  // Ocupa el espacio «responsables» de la ficha del catálogo (ATLAS-ADR-0028) sin importar el catálogo.
  pestanasFicha: [
    { id: 'responsables', titulo: 'Responsables', orden: 10, permiso: 'atlas.equipo.ver', cargar: () => import('./pestanas/Responsables') },
  ],
  busqueda: { proveedor: 'equipo', tipos: ['persona', 'responsabilidad'] },
  // HU 2.12: se guarda sin conexión (aún no lo usa el shell).
  offline: { recursos: ['/api/v1/equipo/responsabilidades'], ttlHoras: 24 },
  bandera: 'modulo.equipo',
  banderaPorDefecto: true,
};
