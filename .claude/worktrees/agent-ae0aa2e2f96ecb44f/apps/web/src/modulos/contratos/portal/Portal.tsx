import { BookOpen, ChevronRight, FileCode2 } from 'lucide-react';
import { lazy, Suspense, useDeferredValue, useState } from 'react';
import { Link, Route, Routes, useParams } from 'react-router';

import { CargandoPagina } from '../../../app/layout/Shell';
import { NoEncontrada } from '../../../app/paginas/Estados';
import { useSesionOpcional } from '../../../app/sesion';
import { BotonCompartir } from '../../../app/ui/Compartir';
import { DatosGuardados } from '../../../app/ui/DatosGuardados';
import { Tarjeta } from '../../../app/ui/Tarjeta';
import { Buscador, Etiqueta } from '../componentes/ui';
import { BotonFavorito, FavoritosPortal } from '../favoritos/componentes';
import { PERMISO_PORTAL, recurso, rutaGuia, rutaPaquete } from '../favoritos/sinConexion';
import { Cargando, VistaMarkdown } from '../guias/componentes';
import { type DetallePaquetePortal, type PaqueteCompartido, useBusquedaPortal, useGuiaPortal, useInicioPortal, usePaquetePortal } from './api';
import { AccionesPaquete, Avisos, ErrorPortal, fechaCorta, InsigniasContrato, Volver } from './componentes';
import { ExportacionDelPortal } from './ExportacionDelPortal';
import { PanelSandbox, useSandboxDisponible } from './Sandbox';

// La pantalla del contrato lleva el lector de YAML y el visor: va en su propio chunk.
const ContratoPortal = lazy(() => import('./ContratoPortal'));

/**
 * Portal de clientes (HU 4.4, plan 00 §8.9 «Portal», ATLAS-ADR-0043): `/portal` (Inicio del cliente),
 * `/portal/paquetes/<id>` (paquete compartido), `/portal/paquetes/<id>/contratos/<contrato>` (contrato con el spec podado)
 * y `/portal/paquetes/<id>/guias/<guía>`. Lo que se ve lo decide el servidor (organización, alcance, vigencia y rol); la
 * PWA nunca tiene el spec completo. Móvil primero: tarjetas, objetivos de 44 px y sin scroll horizontal.
 */
export default function Portal() {
  return (
    <div className="mx-auto flex max-w-5xl min-w-0 flex-col gap-4">
      <Suspense fallback={<CargandoPagina />}>
        <Routes>
          <Route index element={<InicioCliente />} />
          <Route path="paquetes/:paqueteId" element={<PaquetePortal />} />
          <Route path="paquetes/:paqueteId/contratos/:contratoId" element={<ContratoPortal />} />
          <Route path="paquetes/:paqueteId/guias/:guiaId" element={<GuiaPortal />} />
          <Route path="*" element={<NoEncontrada />} />
        </Routes>
      </Suspense>
      {/* HU 4.5: conecta el botón «Exportar» de los paquetes con la exportación del portal. */}
      <ExportacionDelPortal />
    </div>
  );
}

// --- Inicio del cliente ---

function InicioCliente() {
  const sesion = useSesionOpcional();
  const inicio = useInicioPortal();
  const [texto, setTexto] = useState('');
  const buscado = useDeferredValue(texto);

  if (inicio.isPending) return <Cargando etiqueta="Cargando lo que FLIT comparte contigo" alto="h-60" />;
  if (inicio.isError)
    return (
      <>
        {/* Sin conexión, el Inicio no está guardado, pero los favoritos sí (HU 4.9). */}
        <FavoritosPortal />
        <ErrorPortal error={inicio.error} que="el portal" />
      </>
    );
  const { organizacion, aplicativos, paquetes, avisos } = inicio.data;
  const porId = new Map(paquetes.map((p) => [p.id, p]));

  return (
    <>
      <header className="flex min-w-0 flex-col gap-2 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm text-white/85">Portal de integración · {organizacion.nombre}</p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">Lo que FLIT comparte contigo</h1>
        <p className="max-w-3xl text-sm text-white/85">
          {paquetes.length === 0
            ? 'Todavía no hay paquetes compartidos con tu organización.'
            : `${paquetes.length} ${paquetes.length === 1 ? 'paquete compartido' : 'paquetes compartidos'} de ${aplicativos.length} ${aplicativos.length === 1 ? 'aplicativo' : 'aplicativos'}.`}
        </p>
      </header>

      <Avisos avisos={avisos} />

      <FavoritosPortal />

      {paquetes.length > 0 && (
        <section aria-label="Buscar en lo compartido" className="flex flex-col gap-2">
          <Buscador etiqueta="Buscar en lo compartido" placeholder="Busca un paquete, un contrato o una operación" valor={texto} alCambiar={setTexto} />
          <ResultadosBusqueda texto={buscado} />
        </section>
      )}

      {paquetes.length === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          Cuando FLIT comparta contratos con {organizacion.nombre}, los verás aquí{sesion?.yo.tipoPrincipal === 'empleado' ? ' (el portal es para clientes y proveedores)' : ''}.
        </p>
      )}

      {aplicativos.length > 0 && (
        <section aria-labelledby="tus-aplicativos" className="flex flex-col gap-3">
          <h2 id="tus-aplicativos" className="text-base font-semibold">
            Tus aplicativos
          </h2>
          <ul className="grid grid-cols-1 gap-3 md:grid-cols-2">
            {aplicativos.map((a) => (
              <li key={a.aplicativo.id} className="flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
                <div className="flex min-w-0 items-start gap-3">
                  <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-degradado-flit text-[#0b1220]">
                    <FileCode2 className="size-5" aria-hidden="true" />
                  </span>
                  <div className="min-w-0">
                    <h3 className="font-semibold break-words">{a.aplicativo.nombre ?? a.aplicativo.slug ?? 'Aplicativo'}</h3>
                    <p className="text-xs text-atlas-texto-suave">
                      {a.contratos} {a.contratos === 1 ? 'contrato compartido' : 'contratos compartidos'}
                    </p>
                  </div>
                </div>
                <ul aria-label={`Paquetes de ${a.aplicativo.nombre ?? 'este aplicativo'}`} className="flex flex-col gap-1">
                  {a.paquetes.map((id) => porId.get(id)).filter((p): p is PaqueteCompartido => !!p).map((p) => (
                    <li key={p.id}>
                      <EnlacePaquete paquete={p} />
                    </li>
                  ))}
                </ul>
                {a.documentos.length > 0 && (
                  <ul aria-label="Documentación" className="flex flex-col gap-1">
                    {a.documentos.map((d) => (
                      <li key={d.id}>
                        <Link to={d.ruta} className="inline-flex min-h-toque items-center gap-2 text-sm text-atlas-tinta hover:underline">
                          <BookOpen className="size-4" aria-hidden="true" /> {d.titulo}
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}

function EnlacePaquete({ paquete: p }: { paquete: PaqueteCompartido }) {
  return (
    <Link to={`/portal/paquetes/${p.id}`} className="flex min-h-toque w-full items-center gap-2 rounded-xl px-2 hover:bg-atlas-activo">
      <span className="min-w-0 flex-1">
        <span className="block font-medium break-words">{p.nombre}</span>
        <span className="block text-xs text-atlas-texto-suave">
          Revisión {p.revision}
          {p.vigenteHasta ? ` · compartido hasta el ${fechaCorta(p.vigenteHasta)}` : ''}
        </span>
      </span>
      {p.nivel === 'resumen' && <Etiqueta>Resumen</Etiqueta>}
      <ChevronRight className="size-4 shrink-0 text-atlas-texto-suave" aria-hidden="true" />
    </Link>
  );
}

function ResultadosBusqueda({ texto }: { texto: string }) {
  const busqueda = useBusquedaPortal(texto);
  if (texto.trim().length < 2) return null;
  if (busqueda.isPending) return <Cargando etiqueta="Buscando" alto="h-12" />;
  if (busqueda.isError) return <p role="alert" className="text-sm">No pudimos buscar. Inténtalo de nuevo.</p>;
  if (busqueda.data.length === 0) return <p role="status" className="text-sm text-atlas-texto-suave">Sin resultados en lo compartido.</p>;
  return (
    <ul aria-label="Resultados" className="flex flex-col gap-1 rounded-2xl border border-border bg-card p-2">
      {busqueda.data.map((r) => (
        <li key={`${r.tipo}-${r.ruta}`}>
          <Link to={r.ruta} className="flex min-h-toque min-w-0 items-center gap-2 rounded-xl px-2 text-sm hover:bg-atlas-activo">
            <Etiqueta>{r.tipo === 'paquete' ? 'Paquete' : r.tipo === 'contrato' ? 'Contrato' : 'Operación'}</Etiqueta>
            <span className="min-w-0 flex-1 break-words">
              {r.tipo === 'operacion' ? <code>{r.titulo}</code> : r.titulo}
              {r.detalle && <span className="block text-xs text-atlas-texto-suave">{r.detalle}</span>}
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

// --- Paquete compartido ---

function PaquetePortal() {
  const { paqueteId = '' } = useParams();
  const detalle = usePaquetePortal(paqueteId);
  if (detalle.isPending) return <Cargando etiqueta="Cargando el paquete" alto="h-60" />;
  if (detalle.isError) return <ErrorPortal error={detalle.error} que="este paquete" />;
  const { paquete: p, contratos, avisos } = detalle.data;
  return <DetallePaquete paquete={p} contratos={contratos} avisos={avisos} />;
}

function DetallePaquete({ paquete: p, contratos, avisos }: DetallePaquetePortal) {
  const sandboxDisponible = useSandboxDisponible(p);
  return (
    <>
      <Volver a="/portal" texto="Inicio del portal" />
      <header className="flex min-w-0 flex-col gap-2 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm text-white/85">
          Paquete compartido · revisión {p.revision}
          {p.vigenteHasta ? ` · hasta el ${fechaCorta(p.vigenteHasta)}` : ''}
        </p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">{p.nombre}</h1>
        {p.descripcion && <p className="max-w-3xl text-sm break-words text-white/85">{p.descripcion}</p>}
        <BotonFavorito objetivo={{ ambito: 'portal', tipo: 'paquete', paqueteId: p.id }} que="el paquete" />
      </header>
      <DatosGuardados recurso={recurso(rutaPaquete(p.id), PERMISO_PORTAL).clave} sinTelefonos={false} />
      <Avisos avisos={avisos} />
      <BotonCompartir className="self-start" ruta={`/portal/paquetes/${encodeURIComponent(p.id)}`} titulo={p.nombre} que="este paquete" />
      {p.nivel === 'tecnico' ? (
        <AccionesPaquete paquete={p} disponibles={{ sandbox: sandboxDisponible }} />
      ) : (
        <p role="note" className="rounded-xl border border-border bg-card p-3 text-sm text-atlas-texto-suave">
          Tu rol ve el resumen de este paquete. El detalle técnico (operaciones, spec y guías) es para el equipo integrador de tu organización.
        </p>
      )}
      <PanelSandbox paquete={p} />
      <ul aria-label="Contratos del paquete" className="flex flex-col gap-3">
        {contratos.map(({ contrato: c, operaciones, guias }) => (
          <li key={c.contratoId} className="flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
            <div className="flex min-w-0 flex-col gap-2">
              {c.nivel === 'tecnico' && c.estadoVersion !== 'retirada' ? (
                <Link to={`/portal/paquetes/${p.id}/contratos/${c.contratoId}`} className="font-semibold break-words text-atlas-tinta hover:underline">
                  {c.nombre}
                </Link>
              ) : (
                <h2 className="font-semibold break-words">{c.nombre}</h2>
              )}
              <p className="text-xs break-words text-atlas-texto-suave">
                {c.aplicativo.nombre ?? c.aplicativo.slug ?? ''} · {c.codigo}
              </p>
              <InsigniasContrato contrato={c} />
            </div>
            {operaciones.length > 0 && (
              <details className="min-w-0">
                <summary className="flex min-h-toque cursor-pointer items-center text-sm font-medium">
                  {operaciones.length} {operaciones.length === 1 ? 'operación compartida' : 'operaciones compartidas'}
                </summary>
                <ul className="flex flex-col gap-1 pl-2">
                  {operaciones.map((o) => (
                    <li key={o.clave} className="min-w-0 text-sm">
                      <Link
                        to={`/portal/paquetes/${p.id}/contratos/${c.contratoId}?op=${encodeURIComponent(o.clave)}`}
                        className="inline-flex min-h-10 items-center gap-2 break-all text-atlas-tinta hover:underline"
                      >
                        <code>{o.clave}</code>
                      </Link>
                      {o.resumen && <span className="block text-xs text-atlas-texto-suave">{o.resumen}</span>}
                    </li>
                  ))}
                </ul>
              </details>
            )}
            {guias.length > 0 && (
              <ul aria-label={`Guías de ${c.nombre}`} className="flex flex-col gap-1">
                {guias.map((g) => (
                  <li key={g.id}>
                    <Link to={`/portal/paquetes/${p.id}/guias/${g.id}`} className="inline-flex min-h-toque items-center gap-2 text-sm text-atlas-tinta hover:underline">
                      <BookOpen className="size-4" aria-hidden="true" /> {g.titulo}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </>
  );
}

// --- Guía compartida ---

function GuiaPortal() {
  const { paqueteId = '', guiaId = '' } = useParams();
  const guia = useGuiaPortal(paqueteId, guiaId);
  if (guia.isPending) return <Cargando etiqueta="Cargando la guía" alto="h-60" />;
  if (guia.isError) return <ErrorPortal error={guia.error} que="esta guía" />;
  const g = guia.data;
  return (
    <>
      <Volver a={`/portal/paquetes/${paqueteId}`} texto="Paquete" />
      <DatosGuardados recurso={recurso(rutaGuia(paqueteId, guiaId), PERMISO_PORTAL).clave} sinTelefonos={false} />
      <BotonFavorito objetivo={{ ambito: 'portal', tipo: 'guia', paqueteId, guiaId }} que="la guía" enPortada={false} />
      <Tarjeta titulo={g.titulo}>
        <p className="mb-3 text-xs text-atlas-texto-suave">
          Revisión {g.revision}
          {g.operacion ? ` · de la operación ${g.operacion}` : ''}
        </p>
        <VistaMarkdown texto={g.contenido} etiqueta={`Contenido de ${g.titulo}`} />
      </Tarjeta>
      <BotonCompartir className="self-start" ruta={`/portal/paquetes/${encodeURIComponent(paqueteId)}/guias/${encodeURIComponent(guiaId)}`} titulo={g.titulo} que="esta guía" />
    </>
  );
}
