import { NavLink } from 'react-router';

import { useSesion } from '../sesion';
import { Icono } from '../ui/Icono';
import { cn } from '../ui/Tarjeta';

/**
 * Barra inferior del móvil (§11): Inicio, Buscar, Soporte, Asistente y Más. Los módulos eligen estar aquí con
 * `movil: 'barra_inferior'`; si son más de los que caben, los últimos pasan a «Más».
 */
export function BarraInferior({ className, alAbrirMas }: { className?: string; alAbrirMas: () => void }) {
  const { navegacion } = useSesion();
  const clases = 'flex min-h-14 min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-[11px] font-medium';

  return (
    <nav
      aria-label="Barra inferior"
      className={cn(
        'fixed inset-x-0 bottom-0 z-30 border-t border-border bg-atlas-vidrio pr-seguro pb-seguro pl-seguro backdrop-blur-md',
        className,
      )}
    >
      <ul className="mx-auto flex max-w-lg px-1">
        {navegacion.barraInferior.map((entrada) => (
          <li key={entrada.ruta} className="flex min-w-0 flex-1">
            <NavLink
              to={entrada.ruta}
              end={entrada.ruta === '/'}
              className={({ isActive }) => cn(clases, isActive ? 'text-atlas-tinta' : 'text-atlas-texto-suave')}
            >
              {({ isActive }) => (
                <>
                  <span
                    className={cn(
                      'grid h-8 w-14 place-items-center rounded-full transition-colors',
                      isActive && 'bg-atlas-activo',
                    )}
                  >
                    <Icono nombre={entrada.icono ?? 'inicio'} className="size-5" />
                  </span>
                  <span className="max-w-full truncate">{entrada.etiqueta}</span>
                </>
              )}
            </NavLink>
          </li>
        ))}
        <li className="flex min-w-0 flex-1">
          <button type="button" onClick={alAbrirMas} className={cn(clases, 'text-atlas-texto-suave')} aria-haspopup="dialog">
            <span className="grid h-8 w-14 place-items-center rounded-full">
              <Icono nombre="mas" className="size-5" />
            </span>
            Más
          </button>
        </li>
      </ul>
    </nav>
  );
}
