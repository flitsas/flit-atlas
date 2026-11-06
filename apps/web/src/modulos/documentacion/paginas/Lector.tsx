import { ArrowLeft, GitCompare, Pencil } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { formatearFecha } from '../../../app/ui/formato';
import { Boton, cn, Insignia } from '../../../app/ui/Tarjeta';
import {
  type AccionEstado,
  cambiarEstado,
  mensajeDeError,
  TEXTO_ESTADO,
  TEXTO_TIPO,
  TEXTO_VISIBILIDAD,
  useCambioDocumento,
  useDocumento,
  useVersiones,
} from '../api';
import { Aviso, Cargando, VistaDocumento } from '../componentes';

const AMBIENTES: Record<string, string> = { dev: 'Desarrollo', qa: 'QA', pdn: 'Producción' };

/** Lector (HU 5.4, ATLAS-ADR-0053): versión seleccionable, índice, comparación y, para quien puede, el flujo de estados. */
export default function Lector() {
  const { id = '' } = useParams();
  const [parametros] = useSearchParams();
  const navegar = useNavigate();
  const leida = Number(parametros.get('version'));
  const version = Number.isInteger(leida) && leida > 0 ? leida : undefined;
  const consulta = useDocumento(id, version);
  const versiones = useVersiones(id);
  const [aviso, setAviso] = useState<{ tono: 'error' | 'exito'; texto: string } | null>(null);
  const estado = useCambioDocumento(({ accion, revision }: { accion: AccionEstado; revision: number }) => cambiarEstado(id, accion, revision));

  if (consulta.isPending) return <Cargando etiqueta="Cargando el documento" alto="h-64" />;
  if (consulta.isError) {
    const noEsta = consulta.error instanceof ErrorApi && consulta.error.estado === 404;
    return (
      <div className="mx-auto flex max-w-3xl flex-col gap-3">
        <h1 className="text-2xl font-semibold">{noEsta ? 'Este documento no está disponible' : 'No pudimos cargar el documento'}</h1>
        <p>{noEsta ? 'No existe o no tienes acceso a esta versión.' : 'Revisa tu conexión y vuelve a intentarlo.'}</p>
        <Link to="/documentacion" className="min-h-toque self-start underline">
          Volver a la documentación
        </Link>
      </div>
    );
  }

  const { documento: d, canales, version: actual, puedeEditar, puedeAprobar } = consulta.data;
  const actuar = (accion: AccionEstado, exito: string) =>
    estado.mutate(
      { accion, revision: d.revisionActual },
      {
        onSuccess: () => setAviso({ tono: 'exito', texto: exito }),
        onError: (e) => setAviso({ tono: 'error', texto: mensajeDeError(e, 'No se pudo cambiar el estado.') }),
      },
    );

  return (
    <div className="mx-auto flex max-w-6xl min-w-0 flex-col gap-4">
      <Link to="/documentacion" className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
        <ArrowLeft className="size-4" aria-hidden="true" /> Documentación
      </Link>
      <header className="flex min-w-0 flex-col gap-2">
        <h1 className="text-2xl font-semibold break-words">{actual?.version.titulo ?? d.titulo}</h1>
        <p className="text-sm text-atlas-texto-suave">
          {TEXTO_TIPO[d.tipo] ?? d.tipo} · Responsable: {d.responsable}
          {actual && ` · Versión ${actual.version.numero} del ${formatearFecha(new Date(actual.version.publicadoEn))}`}
        </p>
        <div className="flex flex-wrap gap-1">
          <Insignia tono="neutral">{TEXTO_VISIBILIDAD[d.visibilidad] ?? d.visibilidad}</Insignia>
          <Insignia tono={d.estado === 'publicado' ? 'exito' : 'info'}>{TEXTO_ESTADO[d.estado] ?? d.estado}</Insignia>
          {d.origen === 'repo' && <Insignia tono="neutral">Desde su repo</Insignia>}
          {actual?.version.esBorrador && <Insignia>Versión sin aprobar</Insignia>}
        </div>
      </header>

      {aviso && <Aviso tono={aviso.tono}>{aviso.texto}</Aviso>}

      {(puedeEditar || puedeAprobar) && (
        <div role="group" aria-label="Acciones del documento" className="flex flex-wrap gap-2">
          {puedeEditar && d.origen === 'atlas' && d.estado !== 'archivado' && (
            <Boton variante="secundario" onClick={() => void navegar(`/documentacion/${d.id}/editar`)}>
              <Pencil className="size-4" aria-hidden="true" /> Editar
            </Boton>
          )}
          {puedeEditar && d.estado === 'borrador' && d.origen === 'atlas' && (
            <Boton disabled={estado.isPending} onClick={() => actuar('envio-revision', 'Enviado a revisión.')}>
              Enviar a revisión
            </Boton>
          )}
          {puedeAprobar && d.estado === 'en_revision' && (
            <>
              <Boton disabled={estado.isPending} onClick={() => actuar('publicacion', 'Documento publicado.')}>
                Aprobar y publicar
              </Boton>
              <Boton variante="secundario" disabled={estado.isPending} onClick={() => actuar('devolucion', 'Devuelto a borrador.')}>
                Devolver a borrador
              </Boton>
            </>
          )}
          {puedeAprobar && d.estado === 'publicado' && d.origen === 'atlas' && (
            <Boton variante="secundario" disabled={estado.isPending} onClick={() => actuar('archivo', 'Documento archivado.')}>
              Archivar
            </Boton>
          )}
        </div>
      )}

      {versiones.data && versiones.data.length > 0 && (
        <nav aria-label="Versiones del documento" className="flex min-w-0 flex-col gap-2">
          <ul className="flex flex-wrap gap-2">
            {versiones.data.map((v) => {
              const esta = v.numero === actual?.version.numero;
              const canal = canales.filter((c) => c.version === v.numero).map((c) => AMBIENTES[c.ambiente] ?? c.ambiente);
              return (
                <li key={v.numero}>
                  <Link
                    to={`/documentacion/${d.id}?version=${v.numero}`}
                    aria-current={esta ? 'page' : undefined}
                    className={cn(
                      'inline-flex min-h-toque items-center rounded-xl border px-3 text-sm',
                      esta ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
                    )}
                  >
                    Versión {v.numero}
                    {canal.length > 0 && ` · ${canal.join(', ')}`}
                  </Link>
                </li>
              );
            })}
          </ul>
          {versiones.data.length > 1 && (
            <Link to={`/documentacion/${d.id}/comparar`} className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
              <GitCompare className="size-4" aria-hidden="true" /> Comparar versiones
            </Link>
          )}
        </nav>
      )}

      {actual ? (
        <VistaDocumento texto={actual.contenido} etiqueta={actual.version.titulo} />
      ) : (
        <p className="text-sm text-atlas-texto-suave">
          Este documento aún no tiene una versión publicada.
          {puedeEditar && ' Puedes seguir trabajándolo en el editor.'}
        </p>
      )}
    </div>
  );
}
