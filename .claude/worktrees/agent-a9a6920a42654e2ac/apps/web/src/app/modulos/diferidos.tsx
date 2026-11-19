import { type ComponentType, lazy, type LazyExoticComponent } from 'react';

import type { Cargador } from './contrato';

/**
 * Un `React.lazy` por cargador, creado una sola vez: así el chunk de una página o un widget se descarga solo cuando se
 * pinta por primera vez, y un módulo apagado o sin permiso nunca se descarga.
 */
const cache = new Map<Cargador, LazyExoticComponent<ComponentType>>();

export function diferido(cargar: Cargador): LazyExoticComponent<ComponentType> {
  let componente = cache.get(cargar);
  if (!componente) {
    componente = lazy(cargar);
    cache.set(cargar, componente);
  }
  return componente;
}
