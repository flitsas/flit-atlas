import { ChevronRight, Search, TriangleAlert } from 'lucide-react';
import { useDeferredValue, useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { formatearFecha } from '../../../app/ui/formato';
import { Icono } from '../../../app/ui/Icono';
import { cn, Insignia } from '../../../app/ui/Tarjeta';
import { TIPOS, type TipoContrato } from '../api';
import {
  type BreakingPendientes,
  type CanalConsolidado,
  type ContratoConsolidado,
  type Desfase,
  NOMBRES_AMBIENTE,
  TEXTO_ESTADO_BREAKING,
  useConsolidado,
} from '../consolidado';
import { moduloContratos } from '../modulo';

const OPCIONES_TIPO: Array<{ valor: TipoContrato | ''; etiqueta: string }> = [
  { valor: '', etiqueta: 'Todos' },
  { valor: 'openapi', etiqueta: TIPOS.openapi },
  { valor: 'asyncapi', etiqueta: TIPOS.asyncapi },
  { valor: 'proto', etiqueta: TIPOS.proto },
];

/**
 * Consolidado de contratos por ambiente (HU 3.4, plan 00 §8.9): qué versión de cada contrato corre en dev, qa y pdn
 * (según los canales que marca el CD de cada repo), los desfases («qa va adelante de pdn») y los cambios que rompen
 * compatibilidad pendientes entre pdn y qa. Móvil primero: tarjetas en el móvil y una tabla desde `lg`, sin scroll
 * horizontal de la página.
 */
export default function Consolidado() {
  const [parametros, setParametros] = useSearchParams();
  const [texto, setTexto] = useState(parametros.get('texto') ?? '');
  const textoDiferido = useDeferredValue(texto.trim());
  const tipo = (parametros.get('tipo') as TipoContrato | null) ?? '';
  const soloDesfases = parametros.get('desfases') === 'true';
  const consolidado = useConsolidado({ texto: textoDiferido || undefined, tipo: tipo || undefined, desfases: soloDesfases || undefined });

  function cambiar(clave: 'texto' | 'tipo' | 'desfases', valor: string) {
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

  const datos = consolidado.data;
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloContratos.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Consolidado de contratos</h1>
          <p className="max-w-3xl text-sm text-atlas-texto-suave">
            Qué versión de cada contrato corre en cada ambiente, según lo que avisa el CD de cada repo al desplegar. Los desfases y los
            cambios que rompen compatibilidad entre pdn y qa salen marcados.{' '}
            <Link to="/contratos" className="font-medium underline underline-offset-2">
              Ver todos los contratos
            </Link>
          </p>
        </div>
      </header>

      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2 text-atlas-texto-suave" aria-hidden="true" />
        <input
          type="search"
          aria-label="Buscar en el consolidado"
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

      <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
        {/* Radios con forma de botón, como la lista de contratos: un <select> viola la CSP en WebKit. */}
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
                  name="tipo-consolidado"
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
        <label
          className={cn(
            'inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm',
            soloDesfases ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
          )}
        >
          <input
            type="checkbox"
            checked={soloDesfases}
            onChange={(e) => cambiar('desfases', e.target.checked ? 'true' : '')}
            className="size-4 accent-[var(--flit-brand-ink)]"
          />
          Solo con desfases
        </label>
      </div>

      {consolidado.isPending && <div role="status" aria-label="Cargando el consolidado" className="h-40 animate-pulse rounded-2xl bg-atlas-activo" />}
      {consolidado.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar el consolidado. Revisa tu conexión e inténtalo de nuevo.
        </p>
      )}
      {datos && (
        <dl aria-label="Resumen" className="grid grid-cols-3 gap-2">
          <Cifra etiqueta={datos.total === 1 ? 'contrato' : 'contratos'} valor={datos.total} />
          <Cifra etiqueta="con desfases" valor={datos.conDesfases} alerta={datos.conDesfases > 0} />
          <Cifra etiqueta="con breaking pendientes" valor={datos.conBreaking} alerta={datos.conBreaking > 0} />
        </dl>
      )}
      {datos?.total === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          {textoDiferido || tipo || soloDesfases
            ? 'Nada coincide con el filtro. Prueba con menos palabras, otro tipo o quita «Solo con desfases».'
            : 'Todavía no hay contratos publicados. Aparecen aquí cuando la CI de un repo publica su primera versión.'}
        </p>
      )}
      {datos && datos.elementos.length > 0 && (
        <>
          <ul aria-label="Contratos por ambiente" className="flex flex-col gap-2 lg:hidden">
            {datos.elementos.map((c) => (
              <li key={c.id}>
                <TarjetaConsolidado contrato={c} />
              </li>
            ))}
          </ul>
          <TablaConsolidado ambientes={datos.ambientes} elementos={datos.elementos} />
        </>
      )}
    </div>
  );
}

function Cifra({ etiqueta, valor, alerta = false }: { etiqueta: string; valor: number; alerta?: boolean }) {
  return (
    <div className="flex min-w-0 flex-col-reverse rounded-2xl border border-border bg-card p-3 shadow-tarjeta">
      <dt className="text-xs break-words text-atlas-texto-suave">{etiqueta}</dt>
      <dd className={cn('text-2xl font-semibold', alerta && 'text-atlas-alerta')}>{valor}</dd>
    </div>
  );
}

function nombreAplicativo(c: ContratoConsolidado) {
  return c.aplicativo.nombre ?? c.aplicativo.slug ?? 'Aplicativo fuera del catálogo';
}

function VersionCanal({ canal }: { canal: CanalConsolidado }) {
  if (!canal.version) return <span className="text-atlas-texto-suave">—</span>;
  return (
    <span className="flex min-w-0 flex-col items-start gap-0.5">
      <Insignia tono={canal.esEstable ? 'exito' : 'info'}>
        <span className="break-all">{canal.version}</span>
      </Insignia>
      {canal.desplegadoEn && (
        <span className="text-xs text-atlas-texto-suave">
          <time dateTime={canal.desplegadoEn}>{formatearFecha(new Date(canal.desplegadoEn))}</time>
        </span>
      )}
    </span>
  );
}

function Desfases({ desfases }: { desfases: Desfase[] }) {
  if (desfases.length === 0) return <span className="text-sm text-atlas-texto-suave">Sin desfases</span>;
  return (
    <ul aria-label="Desfases" className="flex flex-col gap-1">
      {desfases.map((d) => (
        <li key={`${d.ambiente}-${d.respectoDe}-${d.tipo}`} className="flex items-start gap-1.5 text-sm text-atlas-alerta">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">{d.mensaje}</span>
        </li>
      ))}
    </ul>
  );
}

function Breaking({ breaking }: { breaking: BreakingPendientes | null }) {
  if (!breaking) return <span className="text-sm text-atlas-texto-suave">—</span>;
  if (breaking.estado !== 'con_breaking') {
    return (
      <p className={cn('text-sm break-words', breaking.estado === 'sin_breaking' ? 'text-atlas-texto-suave' : 'text-atlas-alerta')}>
        {TEXTO_ESTADO_BREAKING[breaking.estado]}
      </p>
    );
  }
  const n = breaking.cambios.length;
  return (
    <details className="group min-w-0">
      <summary className="flex min-h-toque cursor-pointer list-none items-center gap-1.5 text-sm font-medium text-atlas-alerta">
        <ChevronRight className="size-4 shrink-0 transition-transform group-open:rotate-90" aria-hidden="true" />
        <span className="min-w-0 break-words">
          {n === 1 ? '1 cambio rompe compatibilidad' : `${n} cambios rompen compatibilidad`} ({breaking.desde} → {breaking.hasta})
        </span>
      </summary>
      <ul aria-label={`Breaking pendientes de ${breaking.desde} a ${breaking.hasta}`} className="mt-1 flex flex-col divide-y divide-border">
        {breaking.cambios.map((c, i) => (
          <li key={`${c.regla}-${i}`} className="flex flex-col gap-0.5 py-1.5 text-sm">
            <code className="text-xs font-medium break-all">{c.regla}</code>
            {(c.metodo || c.ruta) && (
              <code className="text-xs break-all text-atlas-texto-suave">{[c.metodo, c.ruta].filter(Boolean).join(' ')}</code>
            )}
            <span className="break-words">{c.texto}</span>
          </li>
        ))}
      </ul>
    </details>
  );
}

function TarjetaConsolidado({ contrato: c }: { contrato: ContratoConsolidado }) {
  return (
    <article aria-label={c.nombre} className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <div className="min-w-0">
        <span className="block truncate text-xs text-atlas-texto-suave">{nombreAplicativo(c)}</span>
        <h2 className="font-semibold break-words">{c.nombre}</h2>
        <span className="mt-1 flex flex-wrap gap-1.5">
          <Insignia tono="neutral">{TIPOS[c.tipo]}</Insignia>
          <Insignia tono="neutral">{c.codigo}</Insignia>
        </span>
      </div>
      <dl className="grid grid-cols-3 gap-2">
        {c.canales.map((canal) => (
          <div key={canal.ambiente} className="flex min-w-0 flex-col gap-1">
            <dt className="text-xs font-medium text-atlas-texto-suave uppercase">{NOMBRES_AMBIENTE[canal.ambiente] ?? canal.ambiente}</dt>
            <dd className="min-w-0">
              <VersionCanal canal={canal} />
            </dd>
          </div>
        ))}
      </dl>
      {c.desfases.length > 0 && <Desfases desfases={c.desfases} />}
      {c.breakingPendientes && <Breaking breaking={c.breakingPendientes} />}
    </article>
  );
}

function TablaConsolidado({ ambientes, elementos }: { ambientes: string[]; elementos: ContratoConsolidado[] }) {
  return (
    // Región enfocable: si la tabla no cabe, se desplaza aquí dentro, nunca la página.
    <div
      role="region"
      aria-label="Tabla del consolidado"
      tabIndex={0}
      className="relative hidden overflow-x-auto rounded-2xl border border-border bg-card shadow-tarjeta outline-none focus-visible:ring-2 focus-visible:ring-atlas-tinta lg:block"
    >
      <table className="w-full border-separate border-spacing-0 text-sm">
        <caption className="sr-only">Versión de cada contrato por ambiente, desfases y breaking pendientes</caption>
        <thead>
          <tr>
            <th scope="col" className="px-3 py-2 text-left font-medium text-atlas-texto-suave">
              Contrato
            </th>
            {ambientes.map((a) => (
              <th key={a} scope="col" className="px-3 py-2 text-left font-medium text-atlas-texto-suave uppercase">
                {NOMBRES_AMBIENTE[a] ?? a}
              </th>
            ))}
            <th scope="col" className="px-3 py-2 text-left font-medium text-atlas-texto-suave">
              Desfases
            </th>
            <th scope="col" className="px-3 py-2 text-left font-medium text-atlas-texto-suave">
              Breaking pendientes
            </th>
          </tr>
        </thead>
        <tbody>
          {elementos.map((c) => (
            <tr key={c.id} className="align-top">
              <th scope="row" className="max-w-64 border-t border-border px-3 py-2 text-left font-normal">
                <span className="block text-xs text-atlas-texto-suave">{nombreAplicativo(c)}</span>
                <span className="block font-semibold break-words">{c.nombre}</span>
                <span className="mt-1 flex flex-wrap gap-1">
                  <Insignia tono="neutral">{TIPOS[c.tipo]}</Insignia>
                  <Insignia tono="neutral">{c.codigo}</Insignia>
                </span>
              </th>
              {ambientes.map((a) => (
                <td key={a} className="border-t border-border px-3 py-2">
                  <VersionCanal canal={c.canales.find((x) => x.ambiente === a) ?? { ambiente: a, version: null, esEstable: null, desplegadoEn: null }} />
                </td>
              ))}
              <td className="max-w-56 border-t border-border px-3 py-2">
                <Desfases desfases={c.desfases} />
              </td>
              <td className="max-w-72 border-t border-border px-3 py-2">
                <Breaking breaking={c.breakingPendientes} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
