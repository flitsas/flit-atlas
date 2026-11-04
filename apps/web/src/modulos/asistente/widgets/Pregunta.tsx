import { Sparkles } from 'lucide-react';

import { WidgetProximamente } from '../../../app/ui/Proximamente';

export default function Pregunta() {
  return (
    <WidgetProximamente
      ruta="/asistente"
      sobre="el asistente"
      fase="F7"
      destacado={
        <p className="flex items-center gap-2 rounded-xl border border-dashed border-border px-3 py-2.5 text-sm text-atlas-texto-suave">
          <Sparkles className="size-4 shrink-0 text-atlas-tinta" aria-hidden="true" />
          «¿Cómo se radica un traspaso en FLIT 2.0?»
        </p>
      }
    >
      Pronto podrás preguntarle a Atlas sobre cualquier aplicativo y recibir respuestas con citas.
    </WidgetProximamente>
  );
}
