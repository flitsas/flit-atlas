import type { AplicativoDeFicha } from '../../../app/modulos/contrato';
import { useDiagramas } from '../api';
import { Cargando, TarjetaDiagrama } from '../componentes';

/** Pestaña «Diagramas» de la ficha (HU 6.6): los suyos y los lienzos que lo mencionan, según lo que la persona puede ver. */
export default function PestanaDiagramas({ aplicativo }: { aplicativo: AplicativoDeFicha }) {
  const lista = useDiagramas({ aplicativo: aplicativo.id });
  if (lista.isPending) return <Cargando etiqueta="Cargando los diagramas" />;
  if (lista.isError) return <p role="alert">No pudimos cargar los diagramas. Revisa tu conexión.</p>;
  if (lista.data.elementos.length === 0) return <p className="text-sm text-atlas-texto-suave">{aplicativo.nombre} todavía no tiene diagramas que puedas ver.</p>;
  return (
    <ul className="grid gap-2 sm:grid-cols-2">
      {lista.data.elementos.map((d) => (
        <li key={d.id} className="min-w-0">
          <TarjetaDiagrama diagrama={d} />
        </li>
      ))}
    </ul>
  );
}
