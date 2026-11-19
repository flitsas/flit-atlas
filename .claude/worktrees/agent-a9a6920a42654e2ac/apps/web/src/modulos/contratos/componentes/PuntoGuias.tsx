import { BookOpen } from 'lucide-react';
import { type ComponentType, lazy, Suspense } from 'react';

import { Tarjeta } from '../../../app/ui/Tarjeta';

/**
 * Punto de montaje de la pestaña «Guías» (HU 3.6). Las guías con revisiones son la HU 3.7, que deja su componente en
 * `guias/GuiasDelContrato.tsx` con `export default` y estas props. Se busca por nombre de archivo con `import.meta.glob`:
 * si el archivo existe, se carga diferido; si no, queda el aviso «Próximamente». Así las dos HU no se tocan los archivos.
 */
export interface PropsGuiasDelContrato {
  contratoId: string;
  /** La versión que se está viendo. */
  version: string;
  tipo: 'openapi' | 'asyncapi' | 'proto';
}

const cargadores = import.meta.glob<{ default: ComponentType<PropsGuiasDelContrato> }>('../guias/GuiasDelContrato.tsx');
const cargar = Object.values(cargadores)[0];
const GuiasDelContrato = cargar ? lazy(cargar) : null;

export function PuntoGuias(props: PropsGuiasDelContrato) {
  if (!GuiasDelContrato) {
    return (
      <Tarjeta titulo="Guías" icono="documentos">
        <p className="flex items-center gap-2 text-sm text-atlas-texto-suave">
          <BookOpen className="size-4 shrink-0" aria-hidden="true" />
          Próximamente: guías de integración del contrato, con revisiones, comparación y restauración (HU 3.7).
        </p>
      </Tarjeta>
    );
  }
  return (
    <Suspense fallback={<div role="status" aria-label="Cargando las guías" className="h-32 animate-pulse rounded-2xl bg-card" />}>
      <GuiasDelContrato {...props} />
    </Suspense>
  );
}
