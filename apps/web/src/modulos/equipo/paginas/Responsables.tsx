import { CalendarClock } from 'lucide-react';
import { lazy, Suspense, useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { CargandoPagina } from '../../../app/layout/Shell';
import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { DatosGuardados } from '../../../app/ui/DatosGuardados';
import { Icono } from '../../../app/ui/Icono';
import { Boton, cn } from '../../../app/ui/Tarjeta';
import {
  cerrarRevision,
  fechaCorta,
  obtenerReparto,
  obtenerRevisiones,
  programarRevision,
  useCambioReparto,
  useReparto,
} from '../apiResponsables';
import { Advertencia, type Aviso, avisoDeError, claseCampo, MostrarAviso } from '../componentes';
import { RECURSO_REPARTO } from '../recursos';
import { moduloEquipo } from '../modulo';

// Cada vista en su chunk: abrir el reparto no descarga el importador ni el CODEOWNERS.
const PorAplicativo = lazy(() => import('../vistas/PorAplicativo'));
const PorPersona = lazy(() => import('../vistas/PorPersona'));
const Carga = lazy(() => import('../vistas/Carga'));
const Historial = lazy(() => import('../vistas/Historial'));
const Traspasos = lazy(() => import('../vistas/Traspasos'));
const CodeownersVista = lazy(() => import('../vistas/Codeowners'));
const Importar = lazy(() => import('../vistas/Importar'));

const VISTAS = [
  { id: 'aplicativos', titulo: 'Por producto y módulo' },
  { id: 'personas', titulo: 'Por persona' },
  { id: 'carga', titulo: 'Mapa de carga' },
  { id: 'historial', titulo: 'Historial' },
  { id: 'traspasos', titulo: 'Traspasos' },
  { id: 'codeowners', titulo: 'CODEOWNERS' },
  { id: 'importar', titulo: 'Importar', gestionar: true },
] as const;

type IdVista = (typeof VISTAS)[number]['id'];

/**
 * Responsables (HU 2.3, 2.4 y 2.5, ATLAS-ADR-0031): el reparto de principal y secundario por producto y módulo, por
 * persona y en un mapa de carga; el historial versionado con comparación y restauración; los traspasos acompañados; el
 * CODEOWNERS sugerido y la importación del documento. La vista va en la URL (`?vista=`) para compartirla.
 */
export default function Responsables() {
  const { acceso } = useSesion();
  const gestiona = tienePermiso('atlas.equipo.gestionar', acceso.permisos);
  const [parametros] = useSearchParams();
  const pedida = parametros.get('vista');
  const vistas = VISTAS.filter((v) => !('gestionar' in v) || gestiona);
  const vista: IdVista = vistas.find((v) => v.id === pedida)?.id ?? 'aplicativos';
  const reparto = useReparto(['vigente'], obtenerReparto);

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloEquipo.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Responsables</h1>
          <p className="text-sm text-atlas-texto-suave">
            {reparto.data?.version
              ? `Reparto versión ${reparto.data.version.numero}${reparto.data.version.fechaDocumento ? ` del ${fechaCorta(reparto.data.version.fechaDocumento)}` : ''}: principal y secundario de cada producto y módulo.`
              : 'Principal y secundario de cada producto y módulo de FLIT.'}{' '}
            <Link to="/equipo" className="font-medium text-atlas-tinta underline-offset-2 hover:underline">
              Ver el directorio
            </Link>
          </p>
        </div>
      </header>

      <DatosGuardados recurso={RECURSO_REPARTO.clave} />
      <RevisionTrimestral gestiona={gestiona} />

      <nav aria-label="Vistas de los responsables">
        <ul className="flex flex-wrap gap-2">
          {vistas.map((v) => (
            <li key={v.id}>
              <Link
                to={v.id === 'aplicativos' ? '?' : `?vista=${v.id}`}
                replace
                aria-current={v.id === vista ? 'page' : undefined}
                className={cn(
                  'inline-flex min-h-toque items-center rounded-xl border px-3 text-sm font-medium',
                  v.id === vista ? 'border-atlas-tinta bg-atlas-activo text-atlas-tinta' : 'border-border bg-card hover:bg-atlas-activo',
                )}
              >
                {v.titulo}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      {reparto.data && !reparto.data.version && vista !== 'importar' && (
        <Advertencia>
          Aún no hay reparto en Atlas.{' '}
          {gestiona ? (
            <Link to="?vista=importar" className="font-semibold underline">
              Importa el documento de responsables
            </Link>
          ) : (
            'Quien gestiona el equipo debe importar el documento de responsables.'
          )}
        </Advertencia>
      )}
      {reparto.data && reparto.data.huerfanas > 0 && (
        <Advertencia>
          {reparto.data.huerfanas} responsabilidades son de aplicativos que ya no están en el catálogo: desaparecen con el próximo cambio del reparto.
        </Advertencia>
      )}

      <Suspense fallback={<CargandoPagina />}>
        {vista === 'aplicativos' && <PorAplicativo />}
        {vista === 'personas' && <PorPersona />}
        {vista === 'carga' && <Carga />}
        {vista === 'historial' && <Historial />}
        {vista === 'traspasos' && <Traspasos />}
        {vista === 'codeowners' && <CodeownersVista />}
        {vista === 'importar' && <Importar />}
      </Suspense>
    </div>
  );
}

/** Próxima revisión trimestral del reparto (§8.2) con su recordatorio; quien gestiona la cierra o la mueve. */
function RevisionTrimestral({ gestiona }: { gestiona: boolean }) {
  const revisiones = useReparto(['revisiones'], obtenerRevisiones);
  const cerrar = useCambioReparto(cerrarRevision);
  const programar = useCambioReparto(programarRevision);
  const [editando, setEditando] = useState(false);
  const [fecha, setFecha] = useState('');
  const [aviso, setAviso] = useState<Aviso>(null);
  const proxima = revisiones.data?.proxima;
  if (!revisiones.data || (!proxima && !gestiona)) return null;

  return (
    <section aria-label="Revisión trimestral" className="flex flex-col gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-2 text-sm">
          <CalendarClock className="size-5 shrink-0 text-atlas-tinta" aria-hidden="true" />
          {proxima ? (
            <span>
              Próxima revisión trimestral del reparto: <strong>{fechaCorta(proxima.fecha)}</strong>. El recordatorio sale{' '}
              {revisiones.data.diasAnticipacion} días antes{proxima.recordatorioEnviadoEn ? ' (ya salió)' : ''}.
            </span>
          ) : (
            <span>No hay una revisión trimestral programada.</span>
          )}
        </p>
        {gestiona && (
          <div className="flex flex-wrap gap-2">
            {proxima && (
              <Boton
                variante="secundario"
                disabled={cerrar.isPending}
                onClick={() =>
                  cerrar.mutate(
                    { id: proxima.id, nota: null },
                    {
                      onSuccess: (r) => setAviso({ tono: 'exito', texto: `Revisión cerrada. La siguiente es el ${r.proxima ? fechaCorta(r.proxima.fecha) : '—'}.` }),
                      onError: (e) => setAviso(avisoDeError(e, 'No se pudo cerrar la revisión.')),
                    },
                  )
                }
              >
                Marcar como hecha
              </Boton>
            )}
            <Boton variante="fantasma" onClick={() => setEditando((v) => !v)}>
              {proxima ? 'Cambiar fecha' : 'Programar'}
            </Boton>
          </div>
        )}
      </div>
      {editando && (
        <form
          aria-label="Fecha de la revisión"
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            programar.mutate(fecha, {
              onSuccess: () => {
                setEditando(false);
                setAviso({ tono: 'exito', texto: 'Revisión programada.' });
              },
              onError: (error) => setAviso(avisoDeError(error, 'No se pudo programar la revisión.')),
            });
          }}
        >
          <label className="text-sm font-medium">
            Día de la revisión
            <input type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} className={claseCampo} />
          </label>
          <Boton type="submit" disabled={!fecha || programar.isPending}>
            Guardar
          </Boton>
        </form>
      )}
      <MostrarAviso aviso={aviso} />
    </section>
  );
}
