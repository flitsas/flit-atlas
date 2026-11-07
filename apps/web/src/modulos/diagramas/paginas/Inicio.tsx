import { useState } from 'react';
import { Link } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { useDiagramas } from '../api';
import { Cargando, claseCampo, TarjetaDiagrama } from '../componentes';

/** Lista de los diagramas que la persona puede ver (HU 6.6). La visibilidad la aplica el servidor. */
export default function InicioDiagramas() {
  const { acceso } = useSesion();
  const [texto, setTexto] = useState('');
  const lista = useDiagramas({ texto: texto.trim() || undefined });

  return (
    <section aria-labelledby="titulo-diagramas" className="flex min-w-0 flex-col gap-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h1 id="titulo-diagramas" className="text-2xl font-semibold">
            Diagramas
          </h1>
          <p className="text-sm text-atlas-texto-suave">Lienzos, flujos en Mermaid y procesos BPMN con sus versiones.</p>
        </div>
        {tienePermiso('atlas.diagramas.editar', acceso.permisos) && (
          <Link to="/diagramas/nuevo" className="inline-flex min-h-toque items-center rounded-xl bg-atlas-tinta px-4 font-medium text-white">
            Nuevo diagrama
          </Link>
        )}
      </header>
      <label className="text-sm font-medium">
        Buscar por título o código
        <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} className={claseCampo} />
      </label>
      {lista.isPending && <Cargando etiqueta="Cargando los diagramas" />}
      {lista.isError && <p role="alert">No pudimos cargar los diagramas. Revisa tu conexión.</p>}
      {lista.data?.elementos.length === 0 && <p className="text-sm text-atlas-texto-suave">No hay diagramas que puedas ver con este filtro.</p>}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {lista.data.elementos.map((d) => (
            <li key={d.id} className="min-w-0">
              <TarjetaDiagrama diagrama={d} />
            </li>
          ))}
        </ul>
      )}
      {lista.data && lista.data.total > lista.data.elementos.length && (
        <p className="text-sm text-atlas-texto-suave">
          Se muestran {lista.data.elementos.length} de {lista.data.total}. Busca por título para encontrar otros.
        </p>
      )}
    </section>
  );
}
