import { Bell, Moon, Search, Sun } from 'lucide-react';
import { Link, NavLink } from 'react-router';

import { useBandeja } from '../notificaciones';
import { useSesion } from '../sesion';
import { useTema } from '../tema';
import { iniciales } from '../ui/formato';
import { Avatar, MarcaAtlas } from '../ui/Marca';
import { cn } from '../ui/Tarjeta';
import { MenuUsuario } from './MenuUsuario';

const botonIcono =
  'relative grid size-toque shrink-0 place-items-center rounded-full text-foreground transition-colors hover:bg-atlas-activo';

/** Barra superior: marca y accesos en el móvil; buscador ⌘K, tema, notificaciones y cuenta en escritorio. */
export function Encabezado({ alBuscar, alAbrirMas }: { alBuscar: () => void; alAbrirMas: () => void }) {
  const { yo } = useSesion();
  const { efectivo, cambiar } = useTema();
  const bandeja = useBandeja();
  const noLeidas = bandeja.data?.noLeidas ?? 0;
  const esMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

  return (
    <header className="sticky top-0 z-20 border-b border-border bg-atlas-vidrio pt-seguro pr-seguro pl-seguro backdrop-blur-md">
      <div className="flex min-h-14 items-center gap-2 px-3 md:min-h-16 md:px-6">
        <Link to="/" className="flex min-h-toque items-center md:hidden" aria-label="Flit Atlas, ir al inicio">
          <MarcaAtlas />
        </Link>

        <button
          type="button"
          onClick={alBuscar}
          aria-haspopup="dialog"
          className="hidden min-h-toque w-full max-w-md items-center gap-3 rounded-xl border border-border bg-card px-3 text-sm text-atlas-texto-suave hover:border-atlas-tinta md:flex"
        >
          <Search className="size-4.5" aria-hidden="true" />
          <span className="flex-1 text-left">Buscar en Atlas…</span>
          <kbd className="rounded-md border border-border px-1.5 py-0.5 font-sans text-xs">{esMac ? '⌘' : 'Ctrl'} K</kbd>
        </button>

        <div className="ml-auto flex items-center gap-1">
          <button
            type="button"
            className={cn(botonIcono, 'hidden md:grid')}
            onClick={() => cambiar(efectivo === 'oscuro' ? 'claro' : 'oscuro')}
            aria-label={efectivo === 'oscuro' ? 'Usar el tema claro' : 'Usar el tema oscuro'}
          >
            {efectivo === 'oscuro' ? <Sun className="size-5" aria-hidden="true" /> : <Moon className="size-5" aria-hidden="true" />}
          </button>
          <NavLink
            to="/notificaciones"
            className={({ isActive }) => cn(botonIcono, isActive && 'bg-atlas-activo')}
            aria-label={noLeidas > 0 ? `Notificaciones, ${noLeidas} sin leer` : 'Notificaciones'}
          >
            <Bell className="size-5" aria-hidden="true" />
            {noLeidas > 0 && (
              <span
                aria-hidden="true"
                className="absolute top-1.5 right-1.5 grid min-w-4.5 place-items-center rounded-full bg-[#c2410c] px-1 text-[10px] leading-4.5 font-semibold text-white"
              >
                {noLeidas > 9 ? '9+' : noLeidas}
              </span>
            )}
          </NavLink>
          <button
            type="button"
            onClick={alAbrirMas}
            className={cn(botonIcono, 'md:hidden')}
            aria-haspopup="dialog"
          >
            <Avatar iniciales={iniciales(yo.nombre)} foto={yo.foto} className="size-8 text-xs" />
            <span className="sr-only">Cuenta de {yo.nombre}</span>
          </button>
          <div className="hidden md:block">
            <MenuUsuario />
          </div>
        </div>
      </div>
    </header>
  );
}
