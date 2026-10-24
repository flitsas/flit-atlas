import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router';

import { Tarjeta } from '../../../app/ui/Tarjeta';
import { Cargando } from '../guias/componentes';
import { usePaquetes } from './api';
import { InsigniasPaquete } from './componentes';

/** Pestaña «Paquetes» de la pantalla del contrato (plan 00 §8.9): los paquetes que lo incluyen en su última revisión o en la vigente. */
export function PaquetesDelContrato({ contratoId }: { contratoId: string }) {
  const lista = usePaquetes({ contrato: contratoId });
  return (
    <Tarjeta titulo="Paquetes que lo usan">
      {lista.isPending && <Cargando etiqueta="Cargando los paquetes" />}
      {lista.isError && <p role="alert">No pudimos cargar los paquetes de este contrato.</p>}
      {lista.data?.elementos.length === 0 && <p className="text-sm text-atlas-texto-suave">Ningún paquete incluye este contrato todavía.</p>}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul aria-label="Paquetes que usan el contrato" className="flex flex-col gap-2">
          {lista.data.elementos.map((p) => (
            <li key={p.id}>
              <Link to={`/contratos/paquetes/${p.id}`} className="flex min-h-toque items-start gap-2 rounded-xl border border-border p-3 hover:bg-atlas-activo">
                <span className="flex min-w-0 flex-1 flex-col gap-2">
                  <span className="font-semibold break-words">{p.nombre}</span>
                  <InsigniasPaquete paquete={p} />
                </span>
                <ChevronRight className="size-4 shrink-0 self-center text-atlas-texto-suave" aria-hidden="true" />
              </Link>
            </li>
          ))}
        </ul>
      )}
      <Link to="/contratos/paquetes" className="mt-3 inline-flex min-h-toque items-center text-sm font-medium text-atlas-tinta hover:underline">
        Todos los paquetes
      </Link>
    </Tarjeta>
  );
}
