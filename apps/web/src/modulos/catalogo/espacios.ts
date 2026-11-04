import type { Fase } from '../../app/ui/Proximamente';

/**
 * Espacios de la ficha del aplicativo (prompt maestro §8.1, ATLAS-ADR-0028), en el orden en que se muestran. El catálogo
 * pinta «Resumen»; los demás los llena el módulo que registre en su manifiesto una pestaña (`pestanasFicha`) con el
 * mismo `id`. Mientras ninguno lo haga, el espacio muestra qué llega y en qué fase. El catálogo no importa a esos
 * módulos: solo conoce los ids.
 */
export interface EspacioFicha {
  id: string;
  titulo: string;
  /** Quién lo llena y cuándo (01-prompts-por-fase). */
  modulo: string;
  fase: Fase;
  descripcion: string;
}

export const ID_RESUMEN = 'resumen';

export const ESPACIOS_FICHA: readonly EspacioFicha[] = [
  {
    id: 'responsables',
    titulo: 'Responsables',
    modulo: 'Equipo y responsables',
    fase: 'F2',
    descripcion: 'Responsable principal y secundario, con su traspaso y el botón «¿A quién llamo?».',
  },
  {
    id: 'soporte',
    titulo: 'Soporte de turno',
    modulo: 'Soporte de los sábados',
    fase: 'F2',
    descripcion: 'Quién está de soporte este sábado y cómo contactarlo.',
  },
  {
    id: 'contratos',
    titulo: 'Contratos',
    modulo: 'Contratos de integración',
    fase: 'F3',
    descripcion: 'OpenAPI, AsyncAPI y proto versionados del aplicativo, con su historial y sus cambios.',
  },
  {
    id: 'versiones',
    titulo: 'Versión por ambiente',
    modulo: 'Contratos y diagramas',
    fase: 'F3',
    descripcion: 'Qué versión corre en dev, qa y pdn, publicada desde el CI.',
  },
  {
    id: 'documentos',
    titulo: 'Documentos',
    modulo: 'Documentación',
    fase: 'F5',
    descripcion: 'Manuales, guías funcionales y de integración, runbooks y notas de versión.',
  },
  {
    id: 'diagramas',
    titulo: 'Diagramas',
    modulo: 'Diagramas',
    fase: 'F6',
    descripcion: 'Arquitectura en LikeC4 por ambiente, drift y diagramas funcionales.',
  },
  {
    id: 'ia',
    titulo: 'Casos de IA',
    modulo: 'Gobierno de IA',
    fase: 'F8',
    descripcion: 'Casos de uso de IA asociados, con su ficha y su evaluación de riesgo.',
  },
];
