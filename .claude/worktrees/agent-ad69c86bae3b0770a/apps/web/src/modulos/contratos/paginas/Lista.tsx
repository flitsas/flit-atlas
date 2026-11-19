import { ArrowRight, ChevronRight, Search } from 'lucide-react';
import { useDeferredValue, useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { formatearFechaHora } from '../../../app/ui/formato';
import { Icono } from '../../../app/ui/Icono';
import { cn, Insignia } from '../../../app/ui/Tarjeta';
import { type ContratoResumen, TIPOS, type TipoContrato, useContratos, useVersiones, type VersionResumen } from '../api';
import { moduloContratos } from '../modulo';

const OPCIONES_TIPO: Array<{ valor: TipoContrato | ''; etiqueta: string }> = [
  { valor: '', etiqueta: 'Todos' },
  { valor: 'openapi', etiqueta: TIPOS.openapi },
  { valor: 'asyncapi', etiqueta: TIPOS.asyncapi },
  { valor: 'proto', etiqueta: TIPOS.proto },
];

/**
 * Lista de contratos y versiones (HU 3.2): lo que la CI de cada repo publicó, con su última versión y, al abrir
 * un contrato, su historial con la procedencia (repo, rama, commit). La pantalla completa del contrato (visor, diff,
 * guías, canales) es la HU 3.6 (`Contrato.tsx`). En el móvil, cada contrato es una tarjeta que se despliega.
 */
export default function Lista() {
  const [parametros, setParametros] = useSearchParams();
  const [texto, setTexto] = useState(parametros.get('texto') ?? '');
  const textoDiferido = useDeferredValue(texto.trim());
  const tipo = (parametros.get('tipo') as TipoContrato | null) ?? '';
  const lista = useContratos({ texto: textoDiferido || undefined, tipo: tipo || undefined });
  const abierto = parametros.get('contrato');

  function cambiar(clave: 'texto' | 'tipo', valor: string) {
    setParametros(
      (actual) => {
        const siguiente = new URLSearchParams(actual);
        if (valor) siguiente.set(clave, valor);
        else siguiente.delete(clave);
        return siguiente;
      },
      { replace: true },
    );
  }

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloContratos.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">{moduloContratos.nombre}</h1>
          <p className="max-w-3xl text-sm text-atlas-texto-suave">
            Contratos publicados desde la CI de cada repo, con sus versiones inmutables. Las versiones nuevas llegan solas: no se
            editan en Atlas.
          </p>
        </div>
      </header>

      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2 text-atlas-texto-suave" aria-hidden="true" />
        <input
          type="search"
          aria-label="Buscar contratos"
          placeholder="Busca por nombre, código o descripción"
          enterKeyHint="search"
          maxLength={200}
          value={texto}
          onChange={(e) => {
            setTexto(e.target.value);
            cambiar('texto', e.target.value.trim());
          }}
          className="min-h-12 w-full rounded-xl border border-border bg-card pr-4 pl-12 text-base outline-none placeholder:text-atlas-texto-suave focus-visible:border-atlas-tinta"
        />
      </div>

      {/* Radios con forma de botón, como el catálogo: un <select> viola la CSP en WebKit (docs/como-crear-un-modulo.md §2.3). */}
      <fieldset className="min-w-0">
        <legend className="mb-1 text-sm font-medium">Tipo</legend>
        <div className="flex flex-wrap gap-2">
          {OPCIONES_TIPO.map((o) => (
            <label
              key={o.valor || 'todos'}
              className={cn(
                'inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm',
                o.valor === tipo ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
              )}
            >
              <input
                type="radio"
                name="tipo-contrato"
                value={o.valor}
                checked={o.valor === tipo}
                onChange={() => cambiar('tipo', o.valor)}
                className="size-4 accent-[var(--flit-brand-ink)]"
              />
              {o.etiqueta}
            </label>
          ))}
        </div>
      </fieldset>

      {lista.isPending && <div role="status" aria-label="Cargando los contratos" className="h-40 animate-pulse rounded-2xl bg-atlas-activo" />}
      {lista.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar los contratos. Revisa tu conexión e inténtalo de nuevo.
        </p>
      )}
      {lista.data && (
        <p role="status" className="text-sm text-atlas-texto-suave">
          {lista.data.total === 1 ? '1 contrato' : `${lista.data.total} contratos`}
          {lista.data.total > lista.data.elementos.length && ` (se muestran ${lista.data.elementos.length}; afina la búsqueda)`}
        </p>
      )}
      {lista.data?.total === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          {textoDiferido || tipo
            ? 'Nada coincide con la búsqueda. Prueba con menos palabras u otro tipo.'
            : 'Todavía no hay contratos publicados. Llegan cuando la CI de un repo de la lista blanca publica su primera versión.'}
        </p>
      )}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul className="flex flex-col gap-2">
          {lista.data.elementos.map((c) => (
            <li key={c.id}>
              <TarjetaContrato contrato={c} abiertoAlCargar={abierto === c.id} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function TarjetaContrato({ contrato: c, abiertoAlCargar }: { contrato: ContratoResumen; abiertoAlCargar: boolean }) {
  const [abierto, setAbierto] = useState(abiertoAlCargar);
  const ultima = c.ultimaVersion;
  return (
    <details
      className="group rounded-2xl border border-border bg-card shadow-tarjeta"
      open={abiertoAlCargar || undefined}
      onToggle={(e) => setAbierto(e.currentTarget.open)}
    >
      <summary className="flex min-h-toque cursor-pointer list-none items-start gap-2 p-4">
        <span className="min-w-0 flex-1">
          <span className="block truncate text-xs text-atlas-texto-suave">{c.aplicativo.nombre ?? c.aplicativo.slug ?? 'Aplicativo fuera del catálogo'}</span>
          <span className="block font-semibold break-words">{c.nombre}</span>
          <span className="mt-2 flex flex-wrap gap-1.5">
            <Insignia tono="neutral">{TIPOS[c.tipo]}</Insignia>
            <Insignia tono="neutral">{c.codigo}</Insignia>
            {ultima && <Insignia tono={ultima.esEstable ? 'exito' : 'info'}>{ultima.version}</Insignia>}
            {c.ultimaEstable && c.ultimaEstable.id !== ultima?.id && <Insignia tono="exito">Estable {c.ultimaEstable.version}</Insignia>}
          </span>
        </span>
        <span className="shrink-0 text-xs text-atlas-texto-suave">{c.versiones === 1 ? '1 versión' : `${c.versiones} versiones`}</span>
        <ChevronRight className="size-4 shrink-0 self-center text-atlas-texto-suave transition-transform group-open:rotate-90" aria-hidden="true" />
      </summary>
      <div className="border-t border-border px-4 pt-3 pb-4">
        {c.descripcion && <p className="mb-3 text-sm text-atlas-texto-suave">{c.descripcion}</p>}
        <Link
          to={`/contratos/${c.id}`}
          aria-label={`Abrir el contrato ${c.nombre}`}
          className="mb-2 inline-flex min-h-toque items-center gap-1 rounded-lg text-sm font-medium text-atlas-tinta hover:underline"
        >
          Abrir el contrato
          <ArrowRight className="size-4" aria-hidden="true" />
        </Link>
        <Versiones contratoId={c.id} habilitado={abierto} />
      </div>
    </details>
  );
}

function Versiones({ contratoId, habilitado }: { contratoId: string; habilitado: boolean }) {
  const versiones = useVersiones(contratoId, habilitado);
  if (!habilitado) return null;
  if (versiones.isPending) return <div role="status" aria-label="Cargando las versiones" className="h-16 animate-pulse rounded-xl bg-atlas-activo" />;
  if (versiones.isError) {
    return (
      <p role="alert" className="text-sm">
        No pudimos cargar las versiones.
      </p>
    );
  }
  return (
    <ol aria-label="Versiones" className="flex flex-col divide-y divide-border">
      {versiones.data.map((v) => (
        <FilaVersion key={v.id} contratoId={contratoId} version={v} />
      ))}
    </ol>
  );
}

function FilaVersion({ contratoId, version: v }: { contratoId: string; version: VersionResumen }) {
  return (
    <li className="flex flex-col gap-1 py-2 text-sm">
      <span className="flex flex-wrap items-center gap-2">
        <Link to={`/contratos/${contratoId}/v/${encodeURIComponent(v.version)}`} className="font-semibold break-all text-atlas-tinta hover:underline">
          {v.version}
        </Link>
        {v.esEstable ? <Insignia tono="exito">Estable</Insignia> : <Insignia tono="info">Prerelease</Insignia>}
        {v.tieneBreaking && <Insignia tono="neutral">Rompe compatibilidad</Insignia>}
        {v.estado !== 'vigente' && <Insignia tono="neutral">{v.estado === 'deprecada' ? 'Deprecada' : 'Retirada'}</Insignia>}
      </span>
      <span className="text-xs break-all text-atlas-texto-suave">
        <time dateTime={v.publicadoEn}>{formatearFechaHora(new Date(v.publicadoEn))}</time> · {v.repositorio}@{v.rama} · commit{' '}
        <code>{v.commit.slice(0, 7)}</code> · sha256 <code>{v.sha256.slice(0, 12)}</code>
      </span>
    </li>
  );
}
