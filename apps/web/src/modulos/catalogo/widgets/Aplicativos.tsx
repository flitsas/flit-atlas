import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { type Grupo, useAplicativos } from '../api';
import { GRUPOS, porVerificar } from '../textos';

/** Widget de Inicio: cuántos aplicativos hay por grupo, con acceso al catálogo filtrado. */
export default function Aplicativos() {
  const lista = useAplicativos();
  if (lista.isPending) return <div role="status" aria-label="Cargando el catálogo" className="h-20 animate-pulse rounded-xl bg-atlas-activo" />;
  if (lista.isError) return <p className="text-sm text-atlas-texto-suave">No pudimos cargar el catálogo.</p>;

  const elementos = lista.data.elementos;
  const porGrupo = (Object.keys(GRUPOS) as Grupo[])
    .map((grupo) => ({ grupo, total: elementos.filter((a) => a.grupo === grupo).length }))
    .filter((g) => g.total > 0);
  const productos = elementos.filter((a) => a.tipo === 'producto').length;
  const pendientes = elementos.filter((a) => porVerificar(a.descripcion)).length;

  return (
    <div className="flex h-full flex-col gap-3">
      <p className="text-sm text-atlas-texto-suave">
        {productos} productos y {lista.data.total} aplicativos en total.
        {pendientes > 0 && ` ${pendientes} con datos por verificar.`}
      </p>
      <ul className="grid grid-cols-2 gap-2">
        {porGrupo.map(({ grupo, total }) => (
          <li key={grupo}>
            <Link
              to={`/catalogo?grupo=${grupo}`}
              className="flex min-h-toque items-center justify-between gap-2 rounded-xl border border-border px-3 text-sm hover:bg-atlas-activo"
            >
              <span className="truncate">{GRUPOS[grupo]}</span>
              <span className="font-semibold">{total}</span>
            </Link>
          </li>
        ))}
      </ul>
      <Link
        to="/catalogo"
        className="mt-auto inline-flex min-h-toque items-center gap-1 self-end rounded-lg px-2 text-sm font-medium text-atlas-tinta hover:bg-atlas-activo"
      >
        Ver el catálogo
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
