import { NavLink } from 'react-router';

import type { NombreIcono } from '../modulos/contrato';
import { useSesion } from '../sesion';
import { Icono } from '../ui/Icono';
import { MarcaAtlas } from '../ui/Marca';
import { cn } from '../ui/Tarjeta';

/** Barra lateral de escritorio (≥ 768 px). En el móvil la reemplaza la barra inferior. */
export function BarraLateral({ className }: { className?: string }) {
  const { navegacion } = useSesion();
  const entradas = navegacion.lateral.filter((e) => e.ruta !== '/buscar');

  return (
    <aside
      className={cn(
        'sticky top-0 h-dvh w-64 shrink-0 flex-col bg-atlas-lateral pt-seguro pl-seguro text-atlas-lateral-texto lg:w-72',
        className,
      )}
    >
      <NavLink to="/" className="flex min-h-16 items-center px-5 text-white" aria-label="Flit Atlas, ir al inicio">
        <MarcaAtlas />
      </NavLink>
      <nav aria-label="Principal" className="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto px-3 py-4">
        {entradas.map((entrada) => (
          <Enlace
            key={entrada.ruta}
            ruta={entrada.ruta}
            etiqueta={entrada.etiqueta}
            icono={entrada.icono ?? 'inicio'}
            pronto={entrada.modulo?.estado === 'proximamente'}
          />
        ))}
      </nav>
      <div className="flex flex-col gap-1 border-t border-white/10 px-3 py-3">
        <Enlace ruta="/notificaciones" etiqueta="Notificaciones" icono="notificaciones" />
        <Enlace ruta="/ajustes" etiqueta="Ajustes" icono="ajustes" />
      </div>
    </aside>
  );
}

function Enlace({ ruta, etiqueta, icono, pronto }: { ruta: string; etiqueta: string; icono: NombreIcono; pronto?: boolean }) {
  return (
    <NavLink
      to={ruta}
      end={ruta === '/'}
      className={({ isActive }) =>
        cn(
          'group relative flex min-h-toque items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors',
          isActive ? 'bg-white/12 text-white' : 'hover:bg-white/8 hover:text-white',
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive && <span aria-hidden="true" className="absolute top-2 bottom-2 left-0 w-1 rounded-full bg-degradado-flit" />}
          <Icono nombre={icono} className="size-5 shrink-0" />
          <span className="flex-1 truncate">{etiqueta}</span>
          {pronto && <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-medium text-white/80">Pronto</span>}
        </>
      )}
    </NavLink>
  );
}
