import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Módulo de ejemplo «hola» (HU 1.9, docs/como-crear-un-modulo.md). Viene apagado: el servidor crea su bandera
 * `modulo.hola` en `false` en cada ambiente y, si no la manda, aquí también vale `false`.
 */
export const moduloHola: ModuloAtlas = {
  id: 'hola',
  nombre: 'Hola',
  descripcion: 'Módulo de ejemplo: saludos de tu organización, para mostrar cómo se crea un módulo de Atlas.',
  icono: 'ejemplo',
  estado: 'disponible',
  rutas: [{ path: '/hola', permiso: 'atlas.hola.ver', cargar: () => import('./paginas/Saludos') }],
  navegacion: [{ etiqueta: 'Hola', ruta: '/hola', movil: 'mas', orden: 85 }],
  widgetsInicio: [
    {
      id: 'hola-ultimo-saludo',
      titulo: 'Último saludo',
      orden: 80,
      permiso: 'atlas.hola.ver',
      cargar: () => import('./widgets/UltimoSaludo'),
    },
  ],
  bandera: 'modulo.hola',
  banderaPorDefecto: false,
};
