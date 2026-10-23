import * as Menu from '@radix-ui/react-dropdown-menu';
import { ChevronDown, LogOut, Settings } from 'lucide-react';
import { useNavigate } from 'react-router';

import { useSesion } from '../sesion';
import { iniciales, nombreRol } from '../ui/formato';
import { Avatar } from '../ui/Marca';
import { Insignia } from '../ui/Tarjeta';

const item =
  'flex min-h-toque cursor-pointer items-center gap-3 rounded-lg px-3 text-sm outline-none data-[highlighted]:bg-atlas-activo';

/**
 * Menú de la cuenta en escritorio (Radix). Va con `modal={false}`: el modo modal de Radix bloquea el scroll
 * inyectando un `<style>`, y la CSP de Atlas no lo permite (ATLAS-ADR-0020).
 */
export function MenuUsuario() {
  const { yo, cerrarSesion } = useSesion();
  const navegar = useNavigate();

  return (
    <Menu.Root modal={false}>
      <Menu.Trigger
        className="flex min-h-toque items-center gap-2 rounded-full py-1 pr-2 pl-1 hover:bg-atlas-activo"
      >
        <Avatar iniciales={iniciales(yo.nombre)} />
        <span className="sr-only">Cuenta de</span>{' '}
        <span className="max-w-40 truncate text-sm font-medium max-lg:sr-only">{yo.nombre}</span>
        <ChevronDown className="size-4 text-atlas-texto-suave" aria-hidden="true" />
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          align="end"
          sideOffset={8}
          className="z-50 w-72 animate-entrar rounded-2xl border border-border bg-popover p-2 text-popover-foreground shadow-xl"
        >
          <div className="flex items-center gap-3 px-3 py-2">
            <Avatar iniciales={iniciales(yo.nombre)} className="size-10" />
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold">{yo.nombre}</p>
              <p className="text-xs text-atlas-texto-suave">{yo.tipoPrincipal === 'empleado' ? 'Empleado de FLIT' : 'Externo'}</p>
            </div>
          </div>
          <div className="flex flex-wrap gap-1.5 px-3 pb-2">
            {yo.roles.length > 0 ? (
              yo.roles.map((rol) => <Insignia key={rol}>{nombreRol(rol)}</Insignia>)
            ) : (
              <Insignia tono="neutral">Sin roles todavía</Insignia>
            )}
          </div>
          <Menu.Separator className="my-1 h-px bg-border" />
          <Menu.Item className={item} onSelect={() => void navegar('/ajustes')}>
            <Settings className="size-4" aria-hidden="true" />
            Ajustes
          </Menu.Item>
          <Menu.Item className={item} onSelect={() => void cerrarSesion()}>
            <LogOut className="size-4" aria-hidden="true" />
            Cerrar sesión
          </Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}
