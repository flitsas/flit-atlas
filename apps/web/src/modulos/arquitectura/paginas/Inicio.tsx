import { Link } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia } from '../../../app/ui/Tarjeta';
import { formatearRelativo } from '../../../app/ui/formato';
import { type Proyecto, TEXTO_CANAL, useProyectos } from '../api';

function TarjetaProyecto({ proyecto }: { proyecto: Proyecto }) {
  return (
    <Link to={`/arquitectura/proyectos/${encodeURIComponent(proyecto.codigo)}`} className="block min-h-toque min-w-0 rounded-xl border border-border bg-card p-3 hover:bg-atlas-activo">
      <p className="truncate font-medium">{proyecto.aplicativo?.nombre ?? proyecto.codigo}</p>
      <p className="truncate text-xs text-atlas-texto-suave">{proyecto.codigo}</p>
      <ul className="mt-2 flex flex-wrap gap-1">
        {proyecto.canales.map(({ canal, publicacion }) => (
          <li key={canal}>
            <Insignia tono={publicacion.estado === 'publicado' ? 'exito' : 'neutral'}>
              {TEXTO_CANAL[canal]} · {publicacion.commitSha.slice(0, 7)} · {formatearRelativo(new Date(publicacion.publicadoEn))}
            </Insignia>
          </li>
        ))}
      </ul>
    </Link>
  );
}

/** Los proyectos que la persona puede ver (HU 6.3). La visibilidad la aplica el servidor. */
export default function InicioArquitectura() {
  const { acceso } = useSesion();
  const lista = useProyectos();
  const veDespliegue = tienePermiso('atlas.arquitectura.ver_despliegue', acceso.permisos);

  return (
    <section aria-labelledby="titulo-arquitectura" className="flex min-w-0 flex-col gap-4">
      <header className="min-w-0">
        <h1 id="titulo-arquitectura" className="text-2xl font-semibold">
          Arquitectura
        </h1>
        <p className="text-sm text-atlas-texto-suave">Modelos de LikeC4 publicados desde cada repo, por commit y por ambiente.</p>
      </header>
      {veDespliegue && (
        <nav aria-label="Despliegue y drift" className="flex flex-wrap gap-2">
          <Link to="/arquitectura/despliegue" className="inline-flex min-h-toque items-center rounded-xl border border-border bg-card px-4 text-sm font-medium">
            Despliegue por ambiente
          </Link>
          <Link to="/arquitectura/drift" className="inline-flex min-h-toque items-center rounded-xl border border-border bg-card px-4 text-sm font-medium">
            Panel de drift
          </Link>
        </nav>
      )}
      {lista.isPending && <div role="status" aria-label="Cargando los proyectos" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />}
      {lista.isError && <p role="alert">No pudimos cargar los proyectos. Revisa tu conexión.</p>}
      {lista.data?.length === 0 && <p className="text-sm text-atlas-texto-suave">Todavía no hay modelos publicados que puedas ver.</p>}
      {lista.data && lista.data.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {lista.data.map((p) => (
            <li key={p.codigo} className="min-w-0">
              <TarjetaProyecto proyecto={p} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
