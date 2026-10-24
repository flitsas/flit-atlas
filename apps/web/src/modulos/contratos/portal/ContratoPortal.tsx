import { BookOpen } from 'lucide-react';
import { lazy, type ReactNode, Suspense, useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { BotonCompartir } from '../../../app/ui/Compartir';
import { Operaciones } from '../componentes/Operaciones';
import { Etiqueta, type Pestana, Pestanas, useEscritorio } from '../componentes/ui';
import { VistaAsyncApi } from '../componentes/VistaAsyncApi';
import { Cargando } from '../guias/componentes';
import { documentoAsync } from '../spec/asyncapi';
import { leerSpec } from '../spec/documento';
import { operacionesOpenApi } from '../spec/openapi';
import { useCambiosPortal, usePaquetePortal, useSpecPodado } from './api';
import { AccionesPaquete, Avisos, ErrorPortal, InsigniasContrato, Volver } from './componentes';
import { PanelSandbox, useSandboxDisponible } from './Sandbox';

// Scalar en su iframe aislado (ATLAS-ADR-0039), solo en escritorio y en su propio chunk.
const VisorScalar = lazy(() => import('../componentes/VisorScalar').then((m) => ({ default: m.VisorScalar })));

/**
 * Contrato compartido (HU 4.4, plan 00 §8.9 «Contrato compartido»): el spec <b>podado</b> que entrega el servidor (solo
 * las operaciones del paquete, sin nada interno y con las URL públicas), en la vista propia de operaciones (móvil y
 * escritorio) y, en escritorio, en Scalar dentro de su iframe con sandbox y CSP (ATLAS-ADR-0039). Además, las guías y los
 * cambios de la versión que tocan lo compartido, y Exportar y Sandbox según la compartición.
 */
export default function ContratoPortal() {
  const { paqueteId = '', contratoId = '' } = useParams();
  const detalle = usePaquetePortal(paqueteId);
  const entrada = detalle.data?.contratos.find((c) => c.contrato.contratoId === contratoId);
  const tecnico = !!entrada && entrada.contrato.nivel === 'tecnico' && entrada.contrato.estadoVersion !== 'retirada';
  const spec = useSpecPodado(paqueteId, contratoId, tecnico);
  const cambios = useCambiosPortal(paqueteId, contratoId, tecnico && entrada?.contrato.tipo === 'openapi');
  const escritorio = useEscritorio();
  const sandboxDisponible = useSandboxDisponible(detalle.data?.paquete);
  const [parametros, setParametros] = useSearchParams();

  const raiz = useMemo(() => (spec.data === undefined ? null : leerSpec(spec.data)), [spec.data]);
  const operaciones = useMemo(() => (raiz && entrada?.contrato.tipo === 'openapi' ? operacionesOpenApi(raiz) : null), [raiz, entrada?.contrato.tipo]);
  const asincrono = useMemo(() => (raiz && entrada?.contrato.tipo === 'asyncapi' ? documentoAsync(raiz) : null), [raiz, entrada?.contrato.tipo]);

  if (detalle.isPending) return <Cargando etiqueta="Cargando el contrato" alto="h-60" />;
  if (detalle.isError) return <ErrorPortal error={detalle.error} que="este paquete" />;
  if (!entrada) return <ErrorPortal error={new ErrorApi(404)} que="este contrato" />;
  const { paquete: p, avisos } = detalle.data;
  const c = entrada.contrato;

  const pestanas: Pestana[] = [
    { id: 'operaciones', titulo: c.tipo === 'openapi' ? 'Operaciones' : 'Documentación' },
    ...(c.tipo === 'openapi' && escritorio ? [{ id: 'referencia', titulo: 'Referencia interactiva' }] : []),
    { id: 'guias', titulo: 'Guías', marca: <Contador n={entrada.guias.length} /> },
    ...(c.tipo === 'openapi' ? [{ id: 'cambios', titulo: 'Cambios' }] : []),
  ];
  const activa = pestanas.find((x) => x.id === parametros.get('pestana'))?.id ?? 'operaciones';
  const cambiar = (cambios: Record<string, string | null>) =>
    setParametros(
      (actual) => {
        const siguiente = new URLSearchParams(actual);
        for (const [clave, valor] of Object.entries(cambios)) {
          if (valor) siguiente.set(clave, valor);
          else siguiente.delete(clave);
        }
        return siguiente;
      },
      { replace: true },
    );

  return (
    <>
      <Volver a={`/portal/paquetes/${p.id}`} texto={p.nombre} />
      <header className="flex min-w-0 flex-col gap-2 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm break-words text-white/85">
          {c.aplicativo.nombre ?? c.aplicativo.slug ?? 'Aplicativo'} · <span className="break-all">{c.codigo}</span>
        </p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">{c.nombre}</h1>
        <InsigniasContrato contrato={c} />
      </header>
      <Avisos avisos={avisos.filter((a) => a.contratoId === c.contratoId)} />
      {!tecnico ? (
        <ErrorPortal error={new ErrorApi(403, { codigo: 'solo_resumen' })} que="este contrato" />
      ) : (
        <>
          <AccionesPaquete paquete={p} disponibles={{ sandbox: sandboxDisponible }} />
          {/* HU 4.6: solo el enlace del portal, que el externo y sus colegas con acceso pueden abrir. */}
          <BotonCompartir className="self-start" ruta={`/portal/paquetes/${encodeURIComponent(p.id)}/contratos/${encodeURIComponent(c.contratoId)}`} titulo={c.nombre} que="este contrato" />
          {c.tipo === 'openapi' && <PanelSandbox paquete={p} contratoId={c.contratoId} />}
          <Pestanas etiqueta="Secciones del contrato" pestanas={pestanas} activa={activa} alElegir={(x) => cambiar({ pestana: x === 'operaciones' ? null : x, op: null })}>
            {spec.isPending && <Cargando etiqueta="Cargando el spec" alto="h-60" />}
            {spec.isError && <ErrorPortal error={spec.error} que="el spec de este contrato" />}
            {spec.data !== undefined && !raiz && <Mensaje>El spec no se pudo leer en el navegador.</Mensaje>}
            {activa === 'operaciones' && operaciones && <Operaciones operaciones={operaciones} elegida={parametros.get('op')} alElegir={(op) => cambiar({ op })} />}
            {activa === 'operaciones' && asincrono && <VistaAsyncApi documento={asincrono} />}
            {activa === 'referencia' && raiz && spec.data !== undefined && (
              <Suspense fallback={<Cargando etiqueta="Cargando el visor" alto="h-60" />}>
                <VisorScalar contenido={spec.data} servidores={raiz.servers} titulo={c.nombre} />
              </Suspense>
            )}
            {activa === 'guias' &&
              (entrada.guias.length === 0 ? (
                <Mensaje>Este paquete no comparte guías de este contrato.</Mensaje>
              ) : (
                <ul aria-label="Guías del contrato" className="flex flex-col gap-1">
                  {entrada.guias.map((g) => (
                    <li key={g.id}>
                      <Link to={`/portal/paquetes/${p.id}/guias/${g.id}`} className="inline-flex min-h-toque items-center gap-2 text-sm text-atlas-tinta hover:underline">
                        <BookOpen className="size-4" aria-hidden="true" /> {g.titulo}
                      </Link>
                    </li>
                  ))}
                </ul>
              ))}
            {activa === 'cambios' && (
              <>
                {cambios.isPending && <Cargando etiqueta="Cargando los cambios" />}
                {cambios.isError && <ErrorPortal error={cambios.error} que="los cambios" />}
                {cambios.data && cambios.data.elementos.length === 0 && <Mensaje>Sin cambios registrados en lo que se te comparte de la versión {c.version}.</Mensaje>}
                {cambios.data && cambios.data.elementos.length > 0 && (
                  <ul aria-label={`Cambios de la versión ${c.version}`} className="flex flex-col gap-2">
                    {cambios.data.elementos.map((x) => (
                      <li key={`${x.id}-${x.metodo}-${x.ruta}`} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 text-sm">
                        <span className="flex flex-wrap items-center gap-2">
                          <Etiqueta tono={x.nivel >= 3 ? 'peligro' : x.nivel === 2 ? 'alerta' : 'info'}>
                            {x.nivel >= 3 ? 'Rompe compatibilidad' : x.nivel === 2 ? 'Advertencia' : 'Información'}
                          </Etiqueta>
                          <code className="break-all">
                            {x.metodo} {x.ruta}
                          </code>
                        </span>
                        <span className="break-words">{x.texto}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </Pestanas>
        </>
      )}
    </>
  );
}

function Mensaje({ children }: { children: ReactNode }) {
  return <p className="rounded-xl border border-border bg-card p-4 text-sm text-atlas-texto-suave">{children}</p>;
}

function Contador({ n }: { n: number }) {
  return <span className="rounded-full bg-atlas-activo px-1.5 text-xs">{n}</span>;
}
