import { X } from 'lucide-react';
import { type ReactNode, useEffect, useRef } from 'react';

import { cn } from './Tarjeta';

/**
 * Hoja inferior en el móvil y diálogo centrado en escritorio (§11: hojas inferiores en lugar de modales). Usa el
 * `<dialog>` nativo: foco atrapado, Escape y fondo inerte sin librerías que inyecten `<style>` (la CSP no lo permite).
 */
export function Hoja({
  abierta,
  alCerrar,
  titulo,
  ocultarTitulo = false,
  posicion = 'inferior',
  children,
  className,
}: {
  abierta: boolean;
  alCerrar: () => void;
  titulo: string;
  ocultarTitulo?: boolean;
  /** `inferior`: hoja inferior en móvil y diálogo en escritorio. `arriba`: paleta de comandos. */
  posicion?: 'inferior' | 'arriba';
  children: ReactNode;
  className?: string;
}) {
  const dialogo = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const elemento = dialogo.current;
    if (!elemento) return;
    // jsdom (pruebas) no implementa showModal.
    if (abierta && !elemento.open) {
      if (typeof elemento.showModal === 'function') elemento.showModal();
      else elemento.setAttribute('open', '');
    }
    if (!abierta && elemento.open) {
      if (typeof elemento.close === 'function') elemento.close();
      else elemento.removeAttribute('open');
    }
  }, [abierta]);

  return (
    <dialog
      ref={dialogo}
      aria-label={titulo}
      onClose={alCerrar}
      // Clic en el fondo (fuera del contenido) cierra.
      onClick={(evento) => evento.target === dialogo.current && alCerrar()}
      className={cn(
        'm-0 max-h-[85dvh] w-full max-w-none overflow-hidden bg-transparent p-0 text-foreground',
        posicion === 'inferior'
          ? 'mt-auto md:m-auto md:max-w-lg'
          : 'mx-auto mt-[8vh] max-w-[calc(100%-2rem)] md:max-w-xl',
      )}
    >
      <div
        className={cn(
          'flex max-h-[85dvh] flex-col bg-card shadow-2xl',
          posicion === 'inferior'
            ? 'animate-subir rounded-t-3xl pb-seguro md:animate-entrar md:rounded-3xl md:pb-0'
            : 'animate-entrar rounded-2xl',
          className,
        )}
      >
        {posicion === 'inferior' && (
          <div aria-hidden="true" className="mx-auto mt-2 h-1.5 w-10 shrink-0 rounded-full bg-border md:hidden" />
        )}
        <header className={cn('flex items-center justify-between gap-2 px-4 pt-2', ocultarTitulo && 'sr-only')}>
          <h2 className="text-base font-semibold">{titulo}</h2>
          <button
            type="button"
            onClick={alCerrar}
            className="grid size-toque place-items-center rounded-full text-atlas-texto-suave hover:bg-atlas-activo"
            aria-label="Cerrar"
          >
            <X className="size-5" aria-hidden="true" />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>
      </div>
    </dialog>
  );
}
