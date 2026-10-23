import type { ReactNode } from 'react';

import { ErrorApi, urlEntrarMicrosoft } from '../../app/api';
import { useSesionOpcional } from '../../app/sesion';
import { Hoja } from '../../app/ui/Hoja';
import { Boton, cn } from '../../app/ui/Tarjeta';

export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

/** Resultado de una acción, para mostrarlo a la persona. */
export type Aviso = { tono: 'exito' | 'error'; texto: string; reautenticar?: boolean } | null;

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    if (error.codigo === 'reautenticacion_requerida') {
      return { tono: 'error', texto: 'Por seguridad, confirma tu identidad antes de este cambio.', reautenticar: true };
    }
    if (error.estado === 403) return { tono: 'error', texto: error.mensajePara('No tienes permiso para esta acción.') };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

export function MostrarAviso({ aviso }: { aviso: Aviso }) {
  const sesion = useSesionOpcional();
  if (!aviso) return null;
  // Las cuentas locales (la de emergencia, ATLAS-ADR-0027) no pasan por Microsoft: se confirma volviendo a entrar.
  const local = sesion?.yo.proveedor === 'emergencia' || sesion?.yo.proveedor === 'local';
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={cn(
        'rounded-xl border p-3 text-sm',
        aviso.tono === 'error'
          ? 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]'
          : 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
      )}
    >
      {aviso.texto}{' '}
      {aviso.reautenticar && !local && (
        <a className="font-semibold underline" href={`${urlEntrarMicrosoft(window.location.pathname)}&reautenticar=true`}>
          Confirmar con Microsoft
        </a>
      )}
      {aviso.reautenticar && local && (
        <button type="button" className="font-semibold underline" onClick={() => void sesion?.cerrarSesion()}>
          Salir y volver a entrar
        </button>
      )}
    </div>
  );
}

/** Confirmación de una acción delicada, en hoja inferior (móvil) o diálogo (escritorio). */
export function Confirmar({
  abierta,
  titulo,
  children,
  textoAccion,
  alConfirmar,
  alCerrar,
}: {
  abierta: boolean;
  titulo: string;
  children: ReactNode;
  textoAccion: string;
  alConfirmar: () => void;
  alCerrar: () => void;
}) {
  return (
    <Hoja abierta={abierta} alCerrar={alCerrar} titulo={titulo}>
      <div className="flex flex-col gap-4 px-4 pt-2 pb-5 text-sm">
        {children}
        <div className="flex flex-wrap justify-end gap-2">
          <Boton variante="secundario" onClick={alCerrar}>
            Cancelar
          </Boton>
          <Boton onClick={alConfirmar}>{textoAccion}</Boton>
        </div>
      </div>
    </Hoja>
  );
}
