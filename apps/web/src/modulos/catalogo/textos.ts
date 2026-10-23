import type { Ambiente, Criticidad, Estado, Grupo, Tipo } from './api';

/** Nombres para mostrar de los valores del catálogo, en el orden en que se ofrecen. */
export const GRUPOS: Record<Grupo, string> = {
  flit2: 'FLIT 2.0',
  flito: 'FLITO',
  satelite: 'Satélites',
  legado: 'Legados',
  infraestructura: 'Infraestructura',
};

export const ESTADOS: Record<Estado, string> = {
  planeado: 'Planeado',
  activo: 'Activo',
  en_retiro: 'En retiro',
  retirado: 'Retirado',
};

export const CRITICIDADES: Record<Criticidad, string> = { alta: 'Alta', media: 'Media', baja: 'Baja' };

export const TIPOS: Record<Tipo, string> = { producto: 'Producto', modulo: 'Módulo', servicio: 'Servicio' };

export const AMBIENTES: Record<Ambiente, string> = { dev: 'dev', qa: 'qa', pdn: 'pdn' };

/** Marca de lo que falta confirmar (prompt maestro §1); la ficha la resalta. */
export const POR_VERIFICAR = '[por verificar]';

export const porVerificar = (texto: string | null | undefined): boolean => !!texto?.includes(POR_VERIFICAR);

export const opciones = <T extends string>(valores: Record<T, string>) =>
  (Object.entries(valores) as [T, string][]).map(([valor, etiqueta]) => ({ valor, etiqueta }));
