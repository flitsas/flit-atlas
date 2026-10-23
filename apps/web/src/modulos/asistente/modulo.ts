import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Asistente Atlas (prompt maestro §8.7). Llega en F7; va en la barra inferior del móvil. */
export const moduloAsistente: ModuloAtlas = {
  id: 'asistente',
  nombre: 'Asistente Atlas',
  descripcion: 'Un experto que explica cómo funciona cada aplicativo, con citas, ejemplos y diagramas, según tus permisos.',
  icono: 'asistente',
  estado: 'proximamente',
  rutas: [{ path: '/asistente', permiso: 'atlas.asistente.usar', cargar: () => import('./paginas/Inicio') }],
  navegacion: [{ etiqueta: 'Asistente', ruta: '/asistente', movil: 'barra_inferior', orden: 60 }],
  widgetsInicio: [
    {
      id: 'asistente-pregunta',
      titulo: 'Pregúntale a Atlas',
      orden: 50,
      permiso: 'atlas.asistente.usar',
      cargar: () => import('./widgets/Pregunta'),
    },
  ],
  bandera: 'modulo.asistente',
  banderaPorDefecto: true,
};
