import { ArrowLeft, ArrowRight, Check, Save } from 'lucide-react';
import { useMemo, useState } from 'react';

import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, cn, Tarjeta } from '../../../app/ui/Tarjeta';
import { Buscador, Etiqueta, Metodo } from '../componentes/ui';
import { type Aviso, Cargando, claseCampo, MostrarAviso } from '../guias/componentes';
import { type ContratoElegible, useElegibles, useOperacionesParaPaquete } from './api';
import { Casilla, Opciones } from './componentes';
import {
  alternarContrato,
  alternarGuia,
  alternarOperacion,
  aplicaA,
  cambiarVersion,
  type EstadoAsistente,
  quitarTodas,
  type SeleccionContrato,
  seleccionarTodas,
  tieneAlgoQueCompartir,
} from './reglas';

const PASOS = ['Contratos', 'Operaciones', 'Guías', 'Resumen'] as const;

/**
 * Asistente de paquetes (HU 4.1, plan 00 §8.9 «Paquetes»): 1. contratos compartibles y su versión; 2. operaciones
 * con filtro por etiqueta y «solo x-public», seleccionar todo o ninguna; 3. guías compartibles publicadas; y un resumen
 * con el nombre, la nota y «Guardar revisión». Cada guardado es una revisión nueva: publicarla es otro paso. Va en su
 * propio chunk (la lista y el detalle no lo descargan). Móvil primero: una columna, hojas inferiores y objetivos de 44 px.
 */
export default function Asistente({
  inicial,
  titulo,
  guardando,
  aviso,
  alGuardar,
  alCancelar,
}: {
  inicial: EstadoAsistente;
  titulo: string;
  guardando: boolean;
  aviso: Aviso;
  alGuardar: (estado: EstadoAsistente) => void;
  alCancelar: () => void;
}) {
  const [estado, setEstado] = useState(inicial);
  const [paso, setPaso] = useState(0);
  const elegibles = useElegibles();
  const porId = useMemo(() => new Map((elegibles.data ?? []).map((c) => [c.id, c])), [elegibles.data]);
  const valido = estado.nombre.trim().length > 0 && estado.nota.trim().length > 0;

  return (
    <section aria-label={titulo} className="flex min-w-0 flex-col gap-4">
      <ol aria-label="Pasos del asistente" className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {PASOS.map((nombre, i) => (
          <li key={nombre} className="min-w-0">
            <button
              type="button"
              onClick={() => setPaso(i)}
              aria-current={i === paso ? 'step' : undefined}
              className={cn(
                'flex min-h-toque w-full items-center gap-2 rounded-xl border px-3 text-left text-sm',
                i === paso ? 'border-atlas-tinta bg-atlas-activo font-semibold' : 'border-border bg-card',
              )}
            >
              <span className="grid size-6 shrink-0 place-items-center rounded-full bg-card text-xs font-semibold ring-1 ring-border">
                {i < paso ? <Check className="size-3.5" aria-hidden="true" /> : i + 1}
              </span>
              <span className="min-w-0 truncate">{nombre}</span>
            </button>
          </li>
        ))}
      </ol>

      {elegibles.isPending && <Cargando etiqueta="Cargando los contratos compartibles" alto="h-40" />}
      {elegibles.isError && <p role="alert">No pudimos cargar los contratos compartibles. Revisa tu conexión e inténtalo de nuevo.</p>}
      {elegibles.data && (
        <>
          {paso === 0 && <PasoContratos estado={estado} elegibles={elegibles.data} alCambiar={setEstado} />}
          {paso === 1 && <PasoOperaciones estado={estado} porId={porId} alCambiar={setEstado} />}
          {paso === 2 && <PasoGuias estado={estado} porId={porId} alCambiar={setEstado} />}
          {paso === 3 && <PasoResumen estado={estado} porId={porId} alCambiar={setEstado} />}
        </>
      )}

      <MostrarAviso aviso={aviso} />
      <div className="flex flex-wrap items-center gap-2">
        {paso > 0 ? (
          <Boton variante="secundario" onClick={() => setPaso(paso - 1)}>
            <ArrowLeft className="size-4" aria-hidden="true" /> Anterior
          </Boton>
        ) : (
          <Boton variante="fantasma" onClick={alCancelar}>
            Cancelar
          </Boton>
        )}
        {paso < PASOS.length - 1 ? (
          <Boton onClick={() => setPaso(paso + 1)}>
            Siguiente: {PASOS[paso + 1]} <ArrowRight className="size-4" aria-hidden="true" />
          </Boton>
        ) : (
          <Boton disabled={!valido || guardando} onClick={() => alGuardar(estado)}>
            <Save className="size-4" aria-hidden="true" /> {guardando ? 'Guardando…' : 'Guardar revisión'}
          </Boton>
        )}
        {paso > 0 && paso < PASOS.length - 1 && (
          <Boton variante="fantasma" onClick={alCancelar}>
            Cancelar
          </Boton>
        )}
      </div>
    </section>
  );
}

function textoContrato(c: ContratoElegible) {
  return `${c.aplicativo.nombre ?? c.aplicativo.slug ?? 'Aplicativo'} · ${c.codigo}`;
}

// --- Paso 1 ---

function PasoContratos({ estado, elegibles, alCambiar }: { estado: EstadoAsistente; elegibles: ContratoElegible[]; alCambiar: (e: EstadoAsistente) => void }) {
  const [texto, setTexto] = useState('');
  const [eligiendo, setEligiendo] = useState<ContratoElegible | null>(null);
  const normalizar = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const filtro = normalizar(texto.trim());
  const visibles = elegibles.filter((c) => !filtro || normalizar(`${c.nombre} ${c.codigo} ${c.aplicativo.nombre ?? ''} ${c.aplicativo.slug ?? ''}`).includes(filtro));
  const ajenos = estado.contratos.filter((s) => !elegibles.some((c) => c.id === s.contratoId));
  const seleccion = eligiendo ? estado.contratos.find((s) => s.contratoId === eligiendo.id) : undefined;

  return (
    <Tarjeta titulo="1. Contratos y versiones">
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Solo los contratos compartibles con clientes y sus versiones estables, vigentes o deprecadas. En QA también
        entran las candidatas de staging (-rc), para probar antes de producción.
      </p>
      {elegibles.length === 0 ? (
        <p className="rounded-xl border border-border p-4 text-sm">
          No hay contratos compartibles que puedas usar. Un contrato es compartible cuando su última versión estable (o candidata, en QA) declara{' '}
          <code className="break-all">info.x-flit-visibilidad: compartible</code>.
        </p>
      ) : (
        <div className="flex min-w-0 flex-col gap-3">
          <Buscador etiqueta="Buscar contratos" valor={texto} alCambiar={setTexto} placeholder="Nombre, código o aplicativo" />
          <ul aria-label="Contratos compartibles" className="flex flex-col gap-2">
            {visibles.map((c) => {
              const s = estado.contratos.find((x) => x.contratoId === c.id);
              const version = s ? c.versiones.find((v) => v.id === s.versionId) : undefined;
              return (
                <li key={c.id} className="flex min-w-0 flex-col gap-2">
                  <Casilla marcada={!!s} alCambiar={() => alCambiar(alternarContrato(estado, c))}>
                    <span className="block font-semibold">{c.nombre}</span>
                    <span className="block text-xs text-atlas-texto-suave">{textoContrato(c)}</span>
                  </Casilla>
                  {s && (
                    <div className="flex flex-wrap items-center gap-2 pl-3">
                      <span className="text-sm">
                        Versión <strong className="break-all">{version?.version ?? 'no elegible'}</strong>
                      </span>
                      {version?.estado === 'deprecada' && <Etiqueta tono="alerta">Deprecada</Etiqueta>}
                      <Boton variante="secundario" onClick={() => setEligiendo(c)} aria-label={`Cambiar la versión de ${c.nombre}`}>
                        Cambiar versión
                      </Boton>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
          {visibles.length === 0 && <p className="text-sm">Ningún contrato coincide con «{texto}».</p>}
        </div>
      )}
      {ajenos.length > 0 && (
        <div role="note" className="mt-3 flex flex-col gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm">
          <p>
            {ajenos.length === 1 ? 'Un contrato del paquete ya no es elegible' : `${ajenos.length} contratos del paquete ya no son elegibles`} (dejó de ser
            compartible o no es de tus aplicativos). Quítalo para guardar.
          </p>
          <Boton variante="secundario" className="self-start" onClick={() => alCambiar({ ...estado, contratos: estado.contratos.filter((s) => !ajenos.includes(s)) })}>
            Quitar los no elegibles
          </Boton>
        </div>
      )}

      <Hoja abierta={eligiendo !== null} alCerrar={() => setEligiendo(null)} titulo={eligiendo ? `Versión de ${eligiendo.nombre}` : 'Versión'}>
        {eligiendo && (
          <fieldset className="flex flex-col gap-1 px-4 pt-2 pb-5">
            <legend className="sr-only">Versiones</legend>
            {eligiendo.versiones.map((v) => (
              <label
                key={v.id}
                className={cn(
                  'flex min-h-toque cursor-pointer flex-wrap items-center gap-2 rounded-xl px-3 text-sm hover:bg-atlas-activo has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[var(--flit-brand-ink)]',
                  seleccion?.versionId === v.id && 'bg-atlas-activo font-medium',
                )}
              >
                <input
                  type="radio"
                  name="version-paquete"
                  className="size-5 accent-[var(--flit-brand-ink)]"
                  checked={seleccion?.versionId === v.id}
                  onChange={() => alCambiar(cambiarVersion(estado, eligiendo, v.id))}
                  // El clic cierra la hoja también sobre la versión que ya estaba elegida (ahí no hay change).
                  onClick={() => setEligiendo(null)}
                />
                <span className="break-all">{v.version}</span>
                {v.estado === 'deprecada' && <Etiqueta tono="alerta">Deprecada{v.retiroEn ? ` · retiro ${formatearFecha(new Date(`${v.retiroEn}T12:00:00-05:00`))}` : ''}</Etiqueta>}
                <span className="text-xs text-atlas-texto-suave">
                  {v.operaciones} operaciones · {v.publicas} x-public · {formatearFecha(new Date(v.publicadoEn))}
                </span>
              </label>
            ))}
            <p className="mt-2 text-xs text-atlas-texto-suave">Al cambiar de versión se vacían las operaciones elegidas de este contrato.</p>
          </fieldset>
        )}
      </Hoja>
    </Tarjeta>
  );
}

// --- Paso 2 ---

function PasoOperaciones({ estado, porId, alCambiar }: { estado: EstadoAsistente; porId: Map<string, ContratoElegible>; alCambiar: (e: EstadoAsistente) => void }) {
  const elegidos = estado.contratos.filter((s) => porId.has(s.contratoId));
  if (elegidos.length === 0) {
    return (
      <Tarjeta titulo="2. Operaciones">
        <p className="text-sm">Elige al menos un contrato en el paso 1.</p>
      </Tarjeta>
    );
  }
  return (
    <div className="flex min-w-0 flex-col gap-4">
      {elegidos.map((s) => (
        <OperacionesDeContrato key={s.contratoId} contrato={porId.get(s.contratoId)!} seleccion={s} estado={estado} alCambiar={alCambiar} />
      ))}
    </div>
  );
}

function OperacionesDeContrato({
  contrato,
  seleccion,
  estado,
  alCambiar,
}: {
  contrato: ContratoElegible;
  seleccion: SeleccionContrato;
  estado: EstadoAsistente;
  alCambiar: (e: EstadoAsistente) => void;
}) {
  const [etiqueta, setEtiqueta] = useState('');
  const [soloPublicas, setSoloPublicas] = useState(false);
  const operaciones = useOperacionesParaPaquete(seleccion.versionId, { etiqueta: etiqueta || undefined, soloPublicas });
  const version = contrato.versiones.find((v) => v.id === seleccion.versionId);
  const visibles = operaciones.data?.elementos ?? [];
  const claves = visibles.map((o) => o.clave);
  const elegidas = new Set(seleccion.operaciones);

  return (
    <Tarjeta titulo={`2. Operaciones · ${contrato.nombre}`}>
      <p className="mb-3 text-sm text-atlas-texto-suave">
        {textoContrato(contrato)} · versión <span className="break-all">{version?.version}</span> · {seleccion.operaciones.length} elegidas
        {operaciones.data ? ` de ${operaciones.data.total}` : ''}
      </p>
      <div className="flex min-w-0 flex-col gap-3">
        {operaciones.data && operaciones.data.etiquetas.length > 0 && (
          <Opciones
            leyenda="Etiqueta"
            nombre={`etiqueta-${contrato.id}`}
            valor={etiqueta}
            alCambiar={setEtiqueta}
            opciones={[{ valor: '', etiqueta: 'Todas' }, ...operaciones.data.etiquetas.map((e) => ({ valor: e, etiqueta: e }))]}
          />
        )}
        <Casilla marcada={soloPublicas} alCambiar={() => setSoloPublicas(!soloPublicas)}>
          Solo las públicas (<code>x-public: true</code> o la etiqueta <code>public</code>)
        </Casilla>
        <div className="flex flex-wrap gap-2" role="group" aria-label={`Selección rápida de ${contrato.nombre}`}>
          <Boton variante="secundario" disabled={visibles.length === 0} onClick={() => alCambiar(seleccionarTodas(estado, contrato.id, claves))}>
            Seleccionar todo ({visibles.length})
          </Boton>
          <Boton variante="secundario" disabled={!claves.some((c) => elegidas.has(c))} onClick={() => alCambiar(quitarTodas(estado, contrato.id, claves))}>
            Ninguna
          </Boton>
        </div>
        {operaciones.isPending && <Cargando etiqueta="Cargando las operaciones" />}
        {operaciones.isError && <p role="alert">No pudimos cargar las operaciones de esta versión.</p>}
        {operaciones.data && visibles.length === 0 && <p className="text-sm">Ninguna operación cumple el filtro.</p>}
        {visibles.length > 0 && (
          <ul aria-label={`Operaciones de ${contrato.nombre}`} className="flex flex-col gap-2">
            {visibles.map((o) => (
              <li key={o.clave}>
                <Casilla marcada={elegidas.has(o.clave)} alCambiar={() => alCambiar(alternarOperacion(estado, contrato.id, o.clave))}>
                  <span className="flex min-w-0 flex-wrap items-center gap-2">
                    {o.metodo && <Metodo metodo={o.metodo} />}
                    <code className="min-w-0 text-sm break-all">{o.ruta ?? o.clave}</code>
                    {o.publica && <Etiqueta tono="exito">x-public</Etiqueta>}
                  </span>
                  {o.resumen && <span className="mt-1 block text-sm text-atlas-texto-suave">{o.resumen}</span>}
                  {o.etiquetas.length > 0 && (
                    <span className="mt-1 flex flex-wrap gap-1">
                      {o.etiquetas.map((e) => (
                        <Etiqueta key={e}>{e}</Etiqueta>
                      ))}
                    </span>
                  )}
                </Casilla>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Tarjeta>
  );
}

// --- Paso 3 ---

function PasoGuias({ estado, porId, alCambiar }: { estado: EstadoAsistente; porId: Map<string, ContratoElegible>; alCambiar: (e: EstadoAsistente) => void }) {
  const elegidos = estado.contratos.filter((s) => porId.has(s.contratoId));
  return (
    <Tarjeta titulo="3. Guías">
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Solo las guías compartibles con clientes, publicadas (aprobadas por el responsable) y que aplican a la versión elegida. Se comparte su revisión
        publicada.
      </p>
      {elegidos.length === 0 && <p className="text-sm">Elige al menos un contrato en el paso 1.</p>}
      <div className="flex min-w-0 flex-col gap-4">
        {elegidos.map((s) => {
          const c = porId.get(s.contratoId)!;
          const version = c.versiones.find((v) => v.id === s.versionId)?.version ?? '';
          const guias = c.guias.filter((g) => aplicaA(g, version));
          return (
            <section key={c.id} aria-label={`Guías de ${c.nombre}`} className="flex min-w-0 flex-col gap-2">
              <h3 className="text-sm font-semibold">
                {c.nombre} <span className="font-normal text-atlas-texto-suave">· {version}</span>
              </h3>
              {guias.length === 0 ? (
                <p className="text-sm text-atlas-texto-suave">Este contrato no tiene guías compartibles publicadas para esta versión.</p>
              ) : (
                <ul className="flex flex-col gap-2">
                  {guias.map((g) => (
                    <li key={g.id}>
                      <Casilla marcada={s.guias.some((x) => x.guiaId === g.id)} alCambiar={() => alCambiar(alternarGuia(estado, c.id, g))}>
                        <span className="block font-medium">{g.titulo}</span>
                        <span className="block text-xs text-atlas-texto-suave">
                          Revisión publicada {g.revisionPublicada}
                          {g.operacion ? ` · ${g.operacion}` : ' · todo el contrato'}
                        </span>
                      </Casilla>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          );
        })}
      </div>
    </Tarjeta>
  );
}

// --- Resumen ---

function PasoResumen({ estado, porId, alCambiar }: { estado: EstadoAsistente; porId: Map<string, ContratoElegible>; alCambiar: (e: EstadoAsistente) => void }) {
  const publicable = tieneAlgoQueCompartir(estado.contratos);
  return (
    <Tarjeta titulo="Resumen">
      <div className="flex min-w-0 flex-col gap-4">
        <label className="text-sm font-medium">
          Nombre del paquete
          <input
            required
            maxLength={200}
            value={estado.nombre}
            onChange={(e) => alCambiar({ ...estado, nombre: e.target.value })}
            className={claseCampo}
            placeholder="Paquete Integradores Trámites 2026-10"
          />
        </label>
        <label className="text-sm font-medium">
          Descripción (opcional)
          <textarea maxLength={2000} rows={2} value={estado.descripcion} onChange={(e) => alCambiar({ ...estado, descripcion: e.target.value })} className={`${claseCampo} py-2`} />
        </label>

        {estado.contratos.length === 0 ? (
          <p className="text-sm">El paquete no tiene contratos todavía: puedes guardarlo como borrador.</p>
        ) : (
          <ul aria-label="Contratos del paquete" className="flex flex-col gap-2">
            {estado.contratos.map((s) => {
              const c = porId.get(s.contratoId);
              const version = c?.versiones.find((v) => v.id === s.versionId)?.version;
              return (
                <li key={s.contratoId} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border p-3">
                  <p className="font-semibold break-words">{c?.nombre ?? 'Contrato no elegible'}</p>
                  <p className="text-sm break-words text-atlas-texto-suave">
                    {c ? textoContrato(c) : s.contratoId} · versión {version ?? '—'}
                  </p>
                  <p className="text-sm">
                    {s.operaciones.length} {s.operaciones.length === 1 ? 'operación' : 'operaciones'} · {s.guias.length} {s.guias.length === 1 ? 'guía' : 'guías'}
                  </p>
                  {s.operaciones.length === 0 && <Etiqueta tono="alerta" className="self-start">Sin operaciones</Etiqueta>}
                </li>
              );
            })}
          </ul>
        )}
        {!publicable && (
          <p role="note" className="rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm">
            Para publicar el paquete necesita al menos un contrato con una operación. Puedes guardar la revisión y completarla después.
          </p>
        )}

        <label className="text-sm font-medium">
          Nota del cambio
          <textarea
            required
            maxLength={500}
            rows={2}
            value={estado.nota}
            onChange={(e) => alCambiar({ ...estado, nota: e.target.value })}
            className={`${claseCampo} py-2`}
            placeholder="Qué cambió en esta revisión"
          />
        </label>
      </div>
    </Tarjeta>
  );
}
