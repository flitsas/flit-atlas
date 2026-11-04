import { useState } from 'react';
import { Link } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { fechaCorta, habilitarSecundario, obtenerTraspasos, registrarActividad, type Traspaso, useCambioReparto, useReparto } from '../apiResponsables';
import { type Aviso, avisoDeError, MostrarAviso } from '../componentes';

/**
 * Traspaso acompañado (§8.2): cada secundario nuevo necesita al menos un despliegue y una guardia con el principal antes
 * de quedar habilitado como respaldo.
 */
export default function Traspasos() {
  const traspasos = useReparto(['traspasos'], obtenerTraspasos);
  if (traspasos.isPending) return <p role="status">Cargando los traspasos…</p>;
  if (traspasos.isError || !traspasos.data) return <p role="alert">No pudimos cargar los traspasos. Revisa tu conexión.</p>;
  return (
    <Tarjeta titulo="Traspasos en acompañamiento">
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Antes de quedar como respaldo, cada secundario nuevo hace al menos un despliegue y una guardia con el principal.
      </p>
      {traspasos.data.length === 0 && <p className="text-sm">No hay secundarios en acompañamiento.</p>}
      <ul aria-label="Traspasos" className="grid gap-3 lg:grid-cols-2">
        {traspasos.data.map((t) => (
          <TarjetaTraspaso key={t.responsabilidadId} traspaso={t} />
        ))}
      </ul>
    </Tarjeta>
  );
}

export function TarjetaTraspaso({ traspaso, comoItem = true }: { traspaso: Traspaso; comoItem?: boolean }) {
  const { acceso } = useSesion();
  const gestiona = tienePermiso('atlas.equipo.gestionar', acceso.permisos);
  const registrar = useCambioReparto(registrarActividad);
  const habilitar = useCambioReparto(habilitarSecundario);
  const [aviso, setAviso] = useState<Aviso>(null);
  const Contenedor = comoItem ? 'li' : 'div';
  const enCurso = traspaso.estado === 'en_acompanamiento';

  return (
    <Contenedor className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
      <div className="flex flex-wrap items-center gap-2">
        <Link to={`/catalogo/${traspaso.aplicativo.slug}`} className="font-semibold break-words text-atlas-tinta hover:underline">
          {traspaso.aplicativo.nombre}
        </Link>
        <Insignia tono={enCurso ? 'info' : 'exito'}>{enCurso ? 'En acompañamiento' : 'Habilitado'}</Insignia>
      </div>
      <p className="text-sm break-words">
        Secundario: <strong>{traspaso.secundario.nombre}</strong>
        {traspaso.principal && <> · acompaña {traspaso.principal.nombre}</>}
      </p>
      <ul aria-label={`Checklist de ${traspaso.aplicativo.nombre}`} className="flex flex-col gap-1 text-sm">
        <li>
          {traspaso.despliegues >= 1 ? '✔' : '○'} Al menos un despliegue con el principal ({traspaso.despliegues})
        </li>
        <li>
          {traspaso.guardias >= 1 ? '✔' : '○'} Al menos una guardia con el principal ({traspaso.guardias})
        </li>
      </ul>
      {traspaso.actividades.length > 0 && (
        <p className="text-xs text-atlas-texto-suave">
          Últimas: {traspaso.actividades.slice(0, 3).map((a) => `${a.tipo} del ${fechaCorta(a.fecha)}`).join(' · ')}
        </p>
      )}
      {gestiona && enCurso && (
        <div className="flex flex-wrap gap-2">
          {(['despliegue', 'guardia'] as const).map((tipo) => (
            <Boton
              key={tipo}
              variante="secundario"
              disabled={registrar.isPending}
              onClick={() =>
                registrar.mutate(
                  { id: traspaso.responsabilidadId, tipo },
                  {
                    onSuccess: () => setAviso({ tono: 'exito', texto: tipo === 'despliegue' ? 'Despliegue registrado.' : 'Guardia registrada.' }),
                    onError: (e) => setAviso(avisoDeError(e, 'No se pudo registrar la actividad.')),
                  },
                )
              }
            >
              {tipo === 'despliegue' ? 'Registrar despliegue' : 'Registrar guardia'}
            </Boton>
          ))}
          <Boton
            disabled={!traspaso.completo || habilitar.isPending}
            onClick={() =>
              habilitar.mutate(traspaso.responsabilidadId, {
                onSuccess: () => setAviso({ tono: 'exito', texto: `${traspaso.secundario.nombre} quedó habilitado.` }),
                onError: (e) => setAviso(avisoDeError(e, 'No se pudo habilitar.')),
              })
            }
          >
            Habilitar
          </Boton>
        </div>
      )}
      <MostrarAviso aviso={aviso} />
    </Contenedor>
  );
}
