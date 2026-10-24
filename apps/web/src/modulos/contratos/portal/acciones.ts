/**
 * Ganchos de las acciones del portal (ATLAS-ADR-0043): exportar (HU 4.5) y sandbox (HU 4.7). Cada HU conecta la suya con
 * `conectarAccionPortal` (por ejemplo, al cargar su chunk); mientras no esté, el botón sale deshabilitado. Que la persona
 * pueda hacerlo lo decide el servidor (`puedeExportar` / `puedeSandbox` del paquete, compartición + permiso): aquí solo se
 * muestra.
 */
export type TipoAccionPortal = 'exportar' | 'sandbox';

export type EjecutarAccionPortal = (paqueteId: string) => Promise<void> | void;

const conectadas = new Map<TipoAccionPortal, EjecutarAccionPortal>();

export function conectarAccionPortal(tipo: TipoAccionPortal, ejecutar: EjecutarAccionPortal): () => void {
  conectadas.set(tipo, ejecutar);
  return () => {
    if (conectadas.get(tipo) === ejecutar) conectadas.delete(tipo);
  };
}

const NO_PERMITIDA: Record<TipoAccionPortal, string> = {
  exportar: 'la compartición con tu organización o tu rol no incluyen exportar este paquete.',
  sandbox: 'la compartición con tu organización o tu rol no incluyen el sandbox de este paquete.',
};

const PRONTO: Record<TipoAccionPortal, string> = {
  exportar: 'pronto podrás exportar el paquete (OpenAPI, Postman, PDF, Word, Excel y ZIP) desde aquí.',
  sandbox: 'pronto podrás probar el paquete en el sandbox desde aquí.',
};

/** El ambiente no tiene la pieza (p. ej. Microcks para el sandbox, HU 4.7): la frase va sola, sin el nombre de la acción delante. */
const NO_DISPONIBLE: Record<TipoAccionPortal, string> = {
  exportar: 'La exportación no está disponible.',
  sandbox: 'El sandbox no está disponible.',
};

export interface EstadoAccionPortal {
  habilitada: boolean;
  /** Por qué está deshabilitada; `null` si está habilitada. */
  motivo: string | null;
  /** El motivo es una frase completa (no se le antepone el nombre de la acción). */
  completo?: boolean;
  ejecutar?: EjecutarAccionPortal;
}

/** `disponible = false`: el servidor dice que el ambiente no tiene con qué (el sandbox sin Microcks responde 503). */
export function estadoAccion(tipo: TipoAccionPortal, permitida: boolean, disponible = true): EstadoAccionPortal {
  if (!permitida) return { habilitada: false, motivo: NO_PERMITIDA[tipo] };
  if (!disponible) return { habilitada: false, motivo: NO_DISPONIBLE[tipo], completo: true };
  const ejecutar = conectadas.get(tipo);
  return ejecutar ? { habilitada: true, motivo: null, ejecutar } : { habilitada: false, motivo: PRONTO[tipo] };
}
