import { CloudOff } from 'lucide-react';

import { useDatosGuardados } from '../../pwa/sinConexion';
import { formatearFechaHora } from './formato';

/**
 * Aviso «Sin conexión: datos del <fecha>» cuando lo que muestra la página salió del dispositivo (HU 2.12,
 * ATLAS-ADR-0034). Solo lo usan las páginas de los módulos (va en su chunk). Sin nada guardado en uso, no muestra nada.
 */
export function DatosGuardados({ recurso }: { recurso: string }) {
  const guardadoEn = useDatosGuardados(recurso);
  if (!guardadoEn) return null;
  return (
    <p
      role="status"
      className="flex min-w-0 items-center gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] px-3 py-2 text-sm text-[var(--badge-warning-fg)]"
    >
      <CloudOff className="size-4 shrink-0" aria-hidden="true" />
      <span className="min-w-0">Sin conexión: datos del {formatearFechaHora(guardadoEn)} (sin teléfonos).</span>
    </p>
  );
}
