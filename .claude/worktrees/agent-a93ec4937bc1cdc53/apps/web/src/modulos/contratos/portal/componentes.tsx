import { ArrowLeft, Download, FlaskConical, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { cn } from '../../../app/ui/Tarjeta';
import { Etiqueta } from '../componentes/ui';
import { estadoAccion, type TipoAccionPortal } from './acciones';
import type { AvisoDelPortal, ContratoCompartido, PaqueteCompartido } from './api';

/** Piezas del portal de clientes (HU 4.4): avisos de deprecación, insignias, acciones y la vuelta atrás. */

/** `2027-06-30` → `30/06/2027`, sin pasar por la zona horaria (es una fecha, no un instante). */
export function fechaCorta(iso: string | null | undefined): string {
  if (!iso) return '';
  const [anio, mes, dia] = iso.slice(0, 10).split('-');
  return anio && mes && dia ? `${dia}/${mes}/${anio}` : iso;
}

export function Volver({ a, texto }: { a: string; texto: string }) {
  return (
    <Link to={a} className="inline-flex min-h-toque items-center gap-2 self-start text-sm font-medium text-atlas-tinta hover:underline">
      <ArrowLeft className="size-4" aria-hidden="true" /> {texto}
    </Link>
  );
}

export function Avisos({ avisos, titulo = 'Avisos de deprecación' }: { avisos: AvisoDelPortal[]; titulo?: string }) {
  if (avisos.length === 0) return null;
  return (
    <section aria-label={titulo} className="flex flex-col gap-2">
      {avisos.map((a) => (
        <p
          key={`${a.paqueteId}-${a.contratoId}-${a.version}`}
          role="note"
          className={cn(
            'flex min-w-0 items-start gap-2 rounded-xl border p-3 text-sm break-words',
            a.tipo === 'retirada'
              ? 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)]'
              : 'border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)]',
          )}
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0">
            <strong>{a.tipo === 'retirada' ? 'Versión retirada' : 'Versión deprecada'}:</strong> {a.mensaje}{' '}
            <span className="text-atlas-texto-suave">({a.paquete})</span>
          </span>
        </p>
      ))}
    </section>
  );
}

export function InsigniasContrato({ contrato: c }: { contrato: ContratoCompartido }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      <Etiqueta>{c.tipo === 'openapi' ? 'OpenAPI' : c.tipo === 'asyncapi' ? 'AsyncAPI' : 'Proto'}</Etiqueta>
      <Etiqueta>Versión {c.version}</Etiqueta>
      {c.estadoVersion === 'deprecada' && <Etiqueta tono="alerta">Deprecada{c.retiroEn ? ` · se retira el ${fechaCorta(c.retiroEn)}` : ''}</Etiqueta>}
      {c.estadoVersion === 'retirada' && <Etiqueta tono="peligro">Retirada</Etiqueta>}
      {c.nivel === 'tecnico' && (
        <Etiqueta tono="info">
          {c.operaciones} {c.operaciones === 1 ? 'operación' : 'operaciones'} · {c.guias} {c.guias === 1 ? 'guía' : 'guías'}
        </Etiqueta>
      )}
    </span>
  );
}

const TEXTOS: Record<TipoAccionPortal, { etiqueta: string; icono: ReactNode }> = {
  exportar: { etiqueta: 'Exportar', icono: <Download className="size-4" aria-hidden="true" /> },
  sandbox: { etiqueta: 'Sandbox', icono: <FlaskConical className="size-4" aria-hidden="true" /> },
};

/**
 * Exportar y Sandbox del paquete (HU 4.5 y 4.7): habilitados solo si la compartición y el permiso lo dejan
 * (`puedeExportar`, `puedeSandbox`, que calcula el servidor) y la acción ya está conectada (`acciones.ts`). Deshabilitados,
 * dicen por qué.
 */
export function AccionesPaquete({ paquete }: { paquete: PaqueteCompartido }) {
  const acciones: Array<{ tipo: TipoAccionPortal; permitida: boolean }> = [
    { tipo: 'exportar', permitida: paquete.puedeExportar },
    { tipo: 'sandbox', permitida: paquete.puedeSandbox },
  ];
  return (
    <div role="group" aria-label="Acciones del paquete" className="flex flex-col gap-2">
      <div className="flex flex-wrap gap-2">
        {acciones.map(({ tipo, permitida }) => {
          const estado = estadoAccion(tipo, permitida);
          return (
            <button
              key={tipo}
              type="button"
              disabled={!estado.habilitada}
              aria-describedby={estado.motivo ? `motivo-${tipo}` : undefined}
              onClick={() => void estado.ejecutar?.(paquete.id)}
              className="inline-flex min-h-toque items-center justify-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo disabled:cursor-not-allowed disabled:opacity-60"
            >
              {TEXTOS[tipo].icono}
              {TEXTOS[tipo].etiqueta}
            </button>
          );
        })}
      </div>
      {acciones.map(({ tipo, permitida }) => {
        const { motivo } = estadoAccion(tipo, permitida);
        return motivo ? (
          <p key={tipo} id={`motivo-${tipo}`} className="text-xs text-atlas-texto-suave">
            {TEXTOS[tipo].etiqueta}: {motivo}
          </p>
        ) : null;
      })}
    </div>
  );
}

/** Mensaje de error de una pantalla del portal: un 404 no dice si el paquete existe (puede ser de otra organización). */
export function ErrorPortal({ error, que }: { error: unknown; que: string }) {
  const noEsta = error instanceof ErrorApi && error.estado === 404;
  const soloResumen = error instanceof ErrorApi && error.codigo === 'solo_resumen';
  return (
    <div role="alert" className="flex flex-col gap-2 rounded-2xl border border-border bg-card p-5 text-sm">
      <p className="font-semibold">
        {noEsta ? `No tienes ${que} compartido.` : soloResumen ? 'Tu rol ve el resumen de este paquete.' : `No pudimos cargar ${que}.`}
      </p>
      <p className="text-atlas-texto-suave">
        {noEsta
          ? 'Puede que la compartición haya vencido o se haya retirado. Si crees que es un error, escríbele a tu contacto en FLIT.'
          : soloResumen
            ? 'El detalle técnico (operaciones, spec y guías) es para el equipo integrador de tu organización.'
            : 'Revisa tu conexión e inténtalo de nuevo.'}
      </p>
      <Link to="/portal" className="inline-flex min-h-toque items-center font-medium text-atlas-tinta hover:underline">
        Ir al inicio del portal
      </Link>
    </div>
  );
}
