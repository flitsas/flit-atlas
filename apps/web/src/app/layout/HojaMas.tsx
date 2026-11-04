import { LogOut } from 'lucide-react';
import { Link } from 'react-router';

import type { NombreIcono } from '../modulos/contrato';
import { useSesion } from '../sesion';
import { type Tema, useTema } from '../tema';
import { iniciales, nombreRol } from '../ui/formato';
import { Hoja } from '../ui/Hoja';
import { Icono } from '../ui/Icono';
import { Avatar } from '../ui/Marca';
import { cn, Insignia } from '../ui/Tarjeta';

const TEMAS: { valor: Tema; etiqueta: string }[] = [
  { valor: 'claro', etiqueta: 'Claro' },
  { valor: 'oscuro', etiqueta: 'Oscuro' },
  { valor: 'sistema', etiqueta: 'Sistema' },
];

/** Hoja «Más» del móvil: el resto de módulos, la cuenta, el tema y el cierre de sesión. */
export function HojaMas({ abierta, alCerrar }: { abierta: boolean; alCerrar: () => void }) {
  const { yo, navegacion, cerrarSesion } = useSesion();
  const { tema, cambiar } = useTema();
  const enlaces: { ruta: string; etiqueta: string; icono: NombreIcono; pronto?: boolean }[] = [
    ...navegacion.mas.map((e) => ({
      ruta: e.ruta,
      etiqueta: e.etiqueta,
      icono: e.icono ?? 'inicio',
      pronto: e.modulo?.estado === 'proximamente',
    })),
    { ruta: '/notificaciones', etiqueta: 'Notificaciones', icono: 'notificaciones' },
    { ruta: '/ajustes', etiqueta: 'Ajustes', icono: 'ajustes' },
  ];

  return (
    <Hoja abierta={abierta} alCerrar={alCerrar} titulo="Más">
      <div className="flex flex-col gap-5 px-4 pt-2 pb-5">
        <section aria-label="Tu cuenta" className="flex items-center gap-3 rounded-2xl bg-atlas-activo p-3">
          <Avatar iniciales={iniciales(yo.nombre)} foto={yo.foto} className="size-11" />
          <div className="min-w-0 flex-1">
            <p className="truncate font-semibold">{yo.nombre}</p>
            <div className="mt-1 flex flex-wrap gap-1">
              {yo.roles.length > 0 ? (
                yo.roles.map((rol) => <Insignia key={rol}>{nombreRol(rol)}</Insignia>)
              ) : (
                <Insignia tono="neutral">Sin roles todavía</Insignia>
              )}
            </div>
          </div>
        </section>

        <nav aria-label="Más secciones">
          <ul className="grid grid-cols-3 gap-2">
            {enlaces.map((enlace) => (
              <li key={enlace.ruta}>
                <Link
                  to={enlace.ruta}
                  onClick={alCerrar}
                  className="flex min-h-22 flex-col items-center justify-center gap-1.5 rounded-2xl border border-border px-1 text-center text-xs font-medium hover:bg-atlas-activo"
                >
                  <span className="grid size-10 place-items-center rounded-xl bg-atlas-activo text-atlas-tinta">
                    <Icono nombre={enlace.icono} className="size-5" />
                  </span>
                  <span className="max-w-full truncate">{enlace.etiqueta}</span>
                  {enlace.pronto && <span className="text-[10px] text-atlas-texto-suave">Pronto</span>}
                </Link>
              </li>
            ))}
          </ul>
        </nav>

        <fieldset>
          <legend className="mb-2 text-sm font-semibold">Tema</legend>
          <div className="grid grid-cols-3 gap-1 rounded-xl bg-atlas-activo p-1">
            {TEMAS.map((opcion) => (
              <label
                key={opcion.valor}
                className={cn(
                  'flex min-h-toque cursor-pointer items-center justify-center rounded-lg text-sm font-medium has-focus-visible:outline-2 has-focus-visible:outline-atlas-tinta',
                  tema === opcion.valor ? 'bg-card shadow-sm' : 'text-atlas-texto-suave',
                )}
              >
                <input
                  type="radio"
                  name="tema-mas"
                  value={opcion.valor}
                  checked={tema === opcion.valor}
                  onChange={() => cambiar(opcion.valor)}
                  className="sr-only"
                />
                {opcion.etiqueta}
              </label>
            ))}
          </div>
        </fieldset>

        <button
          type="button"
          onClick={() => {
            alCerrar();
            void cerrarSesion();
          }}
          className="flex min-h-12 items-center justify-center gap-2 rounded-xl border border-border text-sm font-semibold text-[var(--badge-danger-fg)] hover:bg-[var(--badge-danger-bg)]"
        >
          <LogOut className="size-4" aria-hidden="true" />
          Cerrar sesión
        </button>
      </div>
    </Hoja>
  );
}
