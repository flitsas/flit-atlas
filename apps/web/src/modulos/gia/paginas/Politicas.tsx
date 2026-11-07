import { Link } from 'react-router';

import { Insignia } from '../../../app/ui/Tarjeta';
import { TEXTO_ESTADO, usePoliticas } from '../api';
import { Aviso, Cargando, Pagina } from '../componentes';

/** Políticas versionadas (HU 8.3). Las sembradas son borradores sin vigencia. */
export default function Politicas() {
  const politicas = usePoliticas();
  const pendientes = politicas.data?.filter((p) => p.pendienteDeRedaccion).length ?? 0;
  return (
    <Pagina titulo="Políticas de IA" descripcion="Versionadas, con registro de aceptación.">
      {pendientes > 0 && <Aviso tono="info">{pendientes === 1 ? 'Una política está pendiente de redacción' : `${pendientes} políticas están pendientes de redacción`} y no tiene vigencia.</Aviso>}
      {politicas.isPending && <Cargando etiqueta="Cargando las políticas" />}
      {politicas.isError && <p role="alert">No pudimos cargar las políticas. Revisa tu conexión.</p>}
      {politicas.data && (
        <ul className="grid gap-2 sm:grid-cols-2">
          {politicas.data.map((p) => (
            <li key={p.id} className="min-w-0">
              <Link to={`/gobierno-ia/politicas/${p.id}`} className="flex h-full min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3">
                <span className="font-medium break-words">{p.titulo}</span>
                <span className="text-xs text-atlas-texto-suave">
                  {p.codigo} · versión {p.version}
                </span>
                <span className="mt-1 flex flex-wrap gap-1">
                  <Insignia>{TEXTO_ESTADO[p.estado] ?? p.estado}</Insignia>
                  {p.pendienteDeRedaccion && <Insignia>Pendiente de redacción</Insignia>}
                  {p.aceptadaPorMi && <Insignia>Aceptada</Insignia>}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Pagina>
  );
}
