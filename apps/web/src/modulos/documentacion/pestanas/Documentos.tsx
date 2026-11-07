import type { AplicativoDeFicha } from '../../../app/modulos/contrato';
import { useDocumentos } from '../api';
import { Cargando, TarjetaDocumento } from '../componentes';

/** Pestaña «Documentos» de la ficha del aplicativo (HU 5.4): sus documentos, según lo que la persona puede ver. */
export default function PestanaDocumentos({ aplicativo }: { aplicativo: AplicativoDeFicha }) {
  const lista = useDocumentos({ aplicativo: aplicativo.id });
  if (lista.isPending) return <Cargando etiqueta="Cargando los documentos" />;
  if (lista.isError) return <p role="alert">No pudimos cargar los documentos. Revisa tu conexión.</p>;
  if (lista.data.elementos.length === 0) return <p className="text-sm text-atlas-texto-suave">{aplicativo.nombre} todavía no tiene documentos que puedas ver.</p>;
  return (
    <ul className="grid gap-2 sm:grid-cols-2">
      {lista.data.elementos.map((d) => (
        <li key={d.id} className="min-w-0">
          <TarjetaDocumento documento={d} />
        </li>
      ))}
    </ul>
  );
}
