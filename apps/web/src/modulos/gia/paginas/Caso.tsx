import { lazy, Suspense, useState } from 'react';
import { Link, useParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { formatearFechaHora } from '../../../app/ui/formato';
import { DIMENSIONES, TEXTO_DECISION, TEXTO_ESTADO, TEXTO_RIESGO, TEXTO_ROL, useCaso, useEipd, useEnvio, useEvaluaciones, type CasoDetalle, type Eipd, type Evaluacion, type FichaCaso } from '../api';
import { Aviso, Cargando, claseBoton, claseBotonSuave, claseCampo, ErrorEnvio, Opciones, Pagina, vacioANulo } from '../componentes';

const Markdown = lazy(() => import('../Markdown'));

function Dato({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-atlas-texto-suave">{etiqueta}</dt>
      <dd className="text-sm break-words">{valor === null || valor === '' ? 'Sin definir' : valor}</dd>
    </div>
  );
}

const unir = (v: string[]) => (v.length > 0 ? v.join(', ') : null);
const siNo = (v: boolean | null) => (v === null ? null : v ? 'Sí' : 'No');

function Ficha({ ficha }: { ficha: FichaCaso }) {
  return (
    <dl className="grid gap-3 sm:grid-cols-2">
      <Dato etiqueta="Aplicación" valor={ficha.aplicacion} />
      <Dato etiqueta="Propósito" valor={ficha.proposito} />
      <Dato etiqueta="Tipo" valor={ficha.tipo} />
      <Dato etiqueta="Ambientes" valor={unir(ficha.ambientes)} />
      <Dato etiqueta="Usuarios afectados" valor={unir(ficha.usuariosAfectados)} />
      <Dato etiqueta="¿Decide sobre personas?" valor={siNo(ficha.decisionSobrePersonas)} />
      <Dato etiqueta="Humano en el bucle" valor={ficha.humanoEnBucle} />
      <Dato etiqueta="Categorías de datos" valor={unir(ficha.categoriasDato)} />
      <Dato etiqueta="Tipos de PII" valor={unir(ficha.tiposPii)} />
      <Dato etiqueta="Base legal" valor={ficha.baseLegal} />
      <Dato etiqueta="Alias del gateway (propuestos)" valor={unir(ficha.aliasUsados)} />
      <Dato etiqueta="Proveedor actual" valor={ficha.proveedorActual} />
      <Dato etiqueta="Métricas de calidad" valor={ficha.metricasCalidad} />
      <Dato etiqueta="Suite de evals" valor={ficha.evalsRuta} />
      <Dato etiqueta="Controles" valor={ficha.controles} />
      <Dato etiqueta="Aviso al usuario" valor={ficha.avisoUsuario} />
    </dl>
  );
}

function Estados({ detalle }: { detalle: CasoDetalle }) {
  const envio = useEnvio();
  const [motivo, setMotivo] = useState('');
  const id = detalle.caso.id;
  const estado = detalle.caso.estado;
  const cambiar = (destino: string) => envio.mutate({ ruta: `/casos/${id}/estado`, cuerpo: { estado: destino, motivo } });
  return (
    <div className="flex flex-col gap-2">
      {estado === 'borrador' && (
        <div className="flex flex-wrap gap-2">
          <Link to={`/gobierno-ia/casos/${id}/editar`} className={claseBotonSuave}>
            Editar la ficha
          </Link>
          <Link to={`/gobierno-ia/casos/${id}/evaluar`} className={claseBotonSuave}>
            Evaluar el riesgo
          </Link>
          <button type="button" disabled={envio.isPending} onClick={() => envio.mutate({ ruta: `/casos/${id}/envio-revision` })} className={claseBoton}>
            Enviar a revisión
          </button>
        </div>
      )}
      {estado !== 'borrador' && estado !== 'retirado' && (
        <>
          <label className="text-sm font-medium">
            Motivo del cambio de estado
            <input value={motivo} onChange={(e) => setMotivo(e.target.value)} className={claseCampo} />
          </label>
          <div className="flex flex-wrap gap-2">
            {estado === 'aprobado' && <button type="button" disabled={!motivo.trim()} onClick={() => cambiar('en_produccion')} className={claseBotonSuave}>Poner en producción</button>}
            {(estado === 'aprobado' || estado === 'en_produccion') && <button type="button" disabled={!motivo.trim()} onClick={() => cambiar('suspendido')} className={claseBotonSuave}>Suspender</button>}
            <button type="button" disabled={!motivo.trim()} onClick={() => cambiar('borrador')} className={claseBotonSuave}>Reabrir</button>
            <button type="button" disabled={!motivo.trim()} onClick={() => cambiar('retirado')} className={claseBotonSuave}>Retirar</button>
          </div>
          {estado !== 'en_revision' && <p className="text-xs text-atlas-texto-suave">Reabrir quita la aprobación: habrá que volver a aprobar.</p>}
        </>
      )}
      <ErrorEnvio error={envio.error} porDefecto="No pudimos cambiar el estado." />
    </div>
  );
}

function Votar({ casoId, evaluacion }: { casoId: string; evaluacion: Evaluacion }) {
  const envio = useEnvio();
  const [rol, setRol] = useState<string | null>(evaluacion.rolesRequeridos.length === 1 ? evaluacion.rolesRequeridos[0]! : null);
  const [condiciones, setCondiciones] = useState('');
  const [comentario, setComentario] = useState('');
  const votar = (voto: string) =>
    envio.mutate({ ruta: `/casos/${casoId}/evaluaciones/${evaluacion.version}/votos`, cuerpo: { rol, voto, condiciones: vacioANulo(condiciones), comentario: vacioANulo(comentario) } });
  return (
    <div className="mt-2 flex flex-col gap-2 border-t border-border pt-2">
      <Opciones titulo="Votas como" valor={rol} opciones={evaluacion.rolesRequeridos.map((r) => ({ valor: r, texto: TEXTO_ROL[r] ?? r }))} alCambiar={setRol} />
      <label className="text-sm font-medium">
        Condiciones (si apruebas con condiciones)
        <input value={condiciones} onChange={(e) => setCondiciones(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Comentario
        <input value={comentario} onChange={(e) => setComentario(e.target.value)} className={claseCampo} />
      </label>
      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={!rol || envio.isPending} onClick={() => votar('aprobar')} className={claseBoton}>Aprobar</button>
        <button type="button" disabled={!rol || envio.isPending} onClick={() => votar('rechazar')} className={claseBotonSuave}>Rechazar</button>
      </div>
      <ErrorEnvio error={envio.error} porDefecto="No pudimos registrar el voto." />
    </div>
  );
}

function Evaluaciones({ casoId, enRevision, gestiona }: { casoId: string; enRevision: boolean; gestiona: boolean }) {
  const evaluaciones = useEvaluaciones(casoId);
  if (evaluaciones.isPending) return <Cargando etiqueta="Cargando las evaluaciones" />;
  if (evaluaciones.isError) return <p role="alert">No pudimos cargar las evaluaciones.</p>;
  if (evaluaciones.data.length === 0) return <p className="text-sm text-atlas-texto-suave">Este caso no tiene evaluación de riesgo.</p>;
  return (
    <ul className="flex flex-col gap-3">
      {evaluaciones.data.map((e, indice) => (
        <li key={e.id} className="min-w-0 rounded-xl border border-border p-3">
          <p className="flex flex-wrap items-center gap-2 font-medium">
            Versión {e.version}
            <Insignia>{TEXTO_DECISION[e.decision] ?? e.decision}</Insignia>
          </p>
          <p className="mt-1 text-sm">
            Inherente: {TEXTO_RIESGO[e.nivelInherente]} ({e.puntajeInherente}) · Residual: {TEXTO_RIESGO[e.nivelResidual]} ({e.puntajeResidual})
          </p>
          <p className="text-xs text-atlas-texto-suave">
            {e.evaluador.nombre} · {formatearFechaHora(new Date(e.evaluadaEn))}
          </p>
          {e.condiciones && <p className="mt-1 text-sm">Condiciones: {e.condiciones}</p>}
          <details className="mt-2">
            <summary className="min-h-toque cursor-pointer text-sm font-medium">Escenarios ({e.dimensiones.length})</summary>
            <ul className="mt-1 flex flex-col gap-1 text-sm">
              {e.dimensiones.map((d, i) => (
                <li key={`${d.dimension}-${i}`} className="break-words">
                  <strong>{DIMENSIONES.find((x) => x.id === d.dimension)?.nombre ?? d.dimension}:</strong> {d.escenario} (P {d.p} × I {d.i}
                  {d.p2 !== null && d.i2 !== null ? ` → P ${d.p2} × I ${d.i2}` : ''})
                </li>
              ))}
            </ul>
          </details>
          {e.votos.length > 0 && (
            <ul aria-label="Votos" className="mt-2 flex flex-col gap-1 text-sm">
              {e.votos.map((v) => (
                <li key={`${v.persona.id}-${v.rol}`} className="break-words">
                  {v.persona.nombre} ({TEXTO_ROL[v.rol] ?? v.rol}): {v.voto === 'aprobar' ? 'aprobó' : 'rechazó'}
                  {v.comentario ? `. ${v.comentario}` : ''}
                </li>
              ))}
            </ul>
          )}
          {e.decision === 'pendiente' && (
            <div className="mt-2 text-sm">
              <p>Deciden: {e.rolesRequeridos.map((r) => TEXTO_ROL[r] ?? r).join(', ')}.</p>
              {e.requisitos.length > 0 && (
                <>
                  <p className="mt-1 font-medium">Falta para aprobar:</p>
                  <ul className="list-disc pl-5">
                    {e.requisitos.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </>
              )}
              {e.nivelResidual === 'critico' && <Aviso tono="error">Con riesgo residual crítico no se aprueba: hay que reducirlo a alto o menos.</Aviso>}
            </div>
          )}
          {gestiona && enRevision && indice === 0 && e.decision === 'pendiente' && <Votar casoId={casoId} evaluacion={e} />}
        </li>
      ))}
    </ul>
  );
}

const SECCIONES_EIPD: { campo: keyof Pick<Eipd, 'descripcion' | 'necesidad' | 'riesgos' | 'medidas' | 'planAccion' | 'opinionOpd'>; titulo: string }[] = [
  { campo: 'descripcion', titulo: 'Descripción del tratamiento y del flujo de datos' },
  { campo: 'necesidad', titulo: 'Necesidad y proporcionalidad' },
  { campo: 'riesgos', titulo: 'Riesgos para los titulares' },
  { campo: 'medidas', titulo: 'Medidas' },
  { campo: 'planAccion', titulo: 'Plan de acción' },
  { campo: 'opinionOpd', titulo: 'Opinión del OPD' },
];

function SeccionEipd({ casoId, gestiona }: { casoId: string; gestiona: boolean }) {
  const eipd = useEipd(casoId);
  const envio = useEnvio();
  const [borrador, setBorrador] = useState<Record<string, string> | null>(null);
  if (eipd.isPending) return <Cargando etiqueta="Cargando la EIPD" />;
  if (eipd.isError) return <p role="alert">No pudimos cargar la EIPD.</p>;
  const actual = eipd.data;
  const estado = actual?.estado ?? null;
  const editable = gestiona && (estado === null || estado === 'borrador' || estado === 'rechazada' || estado === 'en_revision');
  const valores = borrador ?? Object.fromEntries(SECCIONES_EIPD.map((s) => [s.campo, actual?.[s.campo] ?? '']));
  return (
    <div className="flex flex-col gap-3">
      <p className="flex flex-wrap items-center gap-2 text-sm">
        {estado ? <Insignia>{TEXTO_ESTADO[estado] ?? estado}</Insignia> : 'Este caso aún no tiene EIPD.'}
        {actual?.decididaPor && `Decidió ${actual.decididaPor.nombre}.`}
      </p>
      {SECCIONES_EIPD.map((s) => {
        const soloOpd = estado === 'en_revision' && s.campo !== 'opinionOpd';
        return editable && !soloOpd ? (
          <label key={s.campo} className="text-sm font-medium">
            {s.titulo}
            <textarea rows={3} value={valores[s.campo] ?? ''} onChange={(e) => setBorrador({ ...valores, [s.campo]: e.target.value })} className={`${claseCampo} py-2`} />
          </label>
        ) : (
          <Dato key={s.campo} etiqueta={s.titulo} valor={valores[s.campo] ?? ''} />
        );
      })}
      {gestiona && (
        <div className="flex flex-wrap gap-2">
          {editable && (
            <button type="button" className={claseBotonSuave} disabled={envio.isPending} onClick={() => envio.mutate({ ruta: `/casos/${casoId}/eipd`, metodo: 'PUT', cuerpo: valores }, { onSuccess: () => setBorrador(null) })}>
              Guardar la EIPD
            </button>
          )}
          {estado === 'borrador' && <button type="button" className={claseBotonSuave} onClick={() => envio.mutate({ ruta: `/casos/${casoId}/eipd/envio` })}>Enviar al OPD</button>}
          {estado === 'en_revision' && (
            <>
              <button type="button" className={claseBoton} onClick={() => envio.mutate({ ruta: `/casos/${casoId}/eipd/decision`, cuerpo: { decision: 'aprobada' } })}>Aprobar como OPD</button>
              <button type="button" className={claseBotonSuave} onClick={() => envio.mutate({ ruta: `/casos/${casoId}/eipd/decision`, cuerpo: { decision: 'rechazada' } })}>Rechazar como OPD</button>
            </>
          )}
        </div>
      )}
      <ErrorEnvio error={envio.error} porDefecto="No pudimos guardar la EIPD." />
    </div>
  );
}

/** Ficha de un caso con su evaluación, votos y EIPD (HU 8.1 y 8.2). */
export default function Caso() {
  const { id = '' } = useParams();
  const { acceso } = useSesion();
  const caso = useCaso(id);
  const gestiona = tienePermiso('atlas.gia.gestionar', acceso.permisos);

  if (caso.isPending) return <Pagina titulo="Caso de uso"><Cargando etiqueta="Cargando el caso" alto="h-48" /></Pagina>;
  if (caso.isError) {
    const noExiste = caso.error instanceof ErrorApi && caso.error.estado === 404;
    return <Pagina titulo="Caso de uso"><p role="alert">{noExiste ? 'El caso no existe.' : 'No pudimos cargar el caso. Revisa tu conexión.'}</p></Pagina>;
  }
  const { data } = caso;
  return (
    <Pagina titulo={data.caso.nombre} descripcion={data.caso.codigo}>
      <p className="flex flex-wrap gap-1">
        <Insignia>{TEXTO_ESTADO[data.caso.estado] ?? data.caso.estado}</Insignia>
        {data.caso.nivelRiesgo && <Insignia>{`Riesgo ${TEXTO_RIESGO[data.caso.nivelRiesgo]}`}</Insignia>}
        {data.caso.riesgoPreliminar && <Insignia>{`Preliminar: ${TEXTO_RIESGO[data.caso.riesgoPreliminar]}`}</Insignia>}
        {data.caso.revisionVencida && <Insignia>Revisión vencida</Insignia>}
      </p>
      {data.faltantes.length > 0 && (
        <Aviso tono="info">
          <p className="font-medium">Falta para enviarla a revisión:</p>
          <ul className="mt-1 list-disc pl-5">
            {data.faltantes.map((f) => (
              <li key={f}>{f}</li>
            ))}
          </ul>
        </Aviso>
      )}
      {gestiona && <Estados detalle={data} />}
      <Tarjeta titulo="Ficha">
        <dl className="mb-3 grid gap-3 sm:grid-cols-2">
          <Dato etiqueta="Dueño" valor={data.caso.dueno?.nombre ?? null} />
          <Dato etiqueta="Próxima revisión" valor={data.caso.proximaRevision} />
          <Dato etiqueta="Aprobó" valor={data.aprobadoPor?.nombre ?? null} />
          <Dato etiqueta="Versión de la ficha" valor={data.versionActual} />
        </dl>
        <Ficha ficha={data.ficha} />
      </Tarjeta>
      {data.detallePlan && (
        <Tarjeta titulo="Lo que dice el plan">
          <p className="mb-1 text-xs text-atlas-texto-suave">Texto del plan 02, sin validar: el dueño debe confirmarlo.</p>
          <Suspense fallback={<Cargando etiqueta="Cargando el texto" />}>
            <Markdown texto={data.detallePlan} etiqueta="Ficha del plan" />
          </Suspense>
        </Tarjeta>
      )}
      <Tarjeta titulo="Evaluación de riesgo">
        <Evaluaciones casoId={id} enRevision={data.caso.estado === 'en_revision'} gestiona={gestiona} />
      </Tarjeta>
      <Tarjeta titulo="Evaluación de impacto en privacidad">
        <SeccionEipd casoId={id} gestiona={gestiona} />
      </Tarjeta>
    </Pagina>
  );
}
