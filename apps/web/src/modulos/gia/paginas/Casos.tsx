import { Link } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia } from '../../../app/ui/Tarjeta';
import { TEXTO_ESTADO, TEXTO_RIESGO, useCasos } from '../api';
import { Cargando, claseBoton, Pagina } from '../componentes';

/** Inventario de casos de uso (HU 8.1). En móvil, tarjetas. */
export default function Casos() {
  const { acceso } = useSesion();
  const casos = useCasos();
  return (
    <Pagina
      titulo="Casos de uso de IA"
      descripcion="Lo cargado desde el plan está en borrador: el dueño de cada caso debe validarlo."
      accion={
        tienePermiso('atlas.gia.gestionar', acceso.permisos) && (
          <Link to="/gobierno-ia/casos/nuevo" className={claseBoton}>
            Nuevo caso
          </Link>
        )
      }
    >
      {casos.isPending && <Cargando etiqueta="Cargando los casos" />}
      {casos.isError && <p role="alert">No pudimos cargar los casos. Revisa tu conexión.</p>}
      {casos.data?.elementos.length === 0 && <p className="text-sm text-atlas-texto-suave">Todavía no hay casos.</p>}
      {casos.data && casos.data.elementos.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {casos.data.elementos.map((c) => (
            <li key={c.id} className="min-w-0">
              <Link to={`/gobierno-ia/casos/${c.id}`} className="flex h-full min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3">
                <span className="font-medium break-words">{c.nombre}</span>
                <span className="text-xs break-words text-atlas-texto-suave">{c.codigo}</span>
                <span className="mt-1 flex flex-wrap gap-1">
                  <Insignia>{TEXTO_ESTADO[c.estado] ?? c.estado}</Insignia>
                  {c.nivelRiesgo ? (
                    <Insignia>{`Riesgo ${TEXTO_RIESGO[c.nivelRiesgo] ?? c.nivelRiesgo}`}</Insignia>
                  ) : (
                    c.riesgoPreliminar && <Insignia>{`Preliminar: ${TEXTO_RIESGO[c.riesgoPreliminar] ?? c.riesgoPreliminar}`}</Insignia>
                  )}
                  {c.revisionVencida && <Insignia>Revisión vencida</Insignia>}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Pagina>
  );
}
