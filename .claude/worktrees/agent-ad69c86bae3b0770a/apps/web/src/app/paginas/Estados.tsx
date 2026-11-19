import { Compass, ShieldAlert, TriangleAlert } from 'lucide-react';
import { type ReactNode, useEffect, useState } from 'react';
import { isRouteErrorResponse, Link, useRouteError } from 'react-router';

import { debeRecargarPorCarga, esErrorDeCarga, pagina, referenciaCorta, reportarError, yaSeIntentoRecarga } from '../errores';

function Estado({ icono, titulo, children }: { icono: ReactNode; titulo: string; children: ReactNode }) {
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 py-16 text-center">
      <span className="grid size-16 place-items-center rounded-2xl bg-atlas-activo text-atlas-tinta">{icono}</span>
      <h1 className="text-2xl font-semibold">{titulo}</h1>
      <div className="text-atlas-texto-suave">{children}</div>
      <Link
        to="/"
        className="inline-flex min-h-toque items-center rounded-xl bg-flit-primary px-5 text-sm font-medium text-white dark:bg-flit-brand dark:text-[#0b1220]"
      >
        Ir al inicio
      </Link>
    </div>
  );
}

export function NoEncontrada() {
  return (
    <Estado icono={<Compass className="size-8" aria-hidden="true" />} titulo="Esta página no existe">
      Revisa la dirección o vuelve al inicio para seguir navegando.
    </Estado>
  );
}

export function SinAcceso({ modulo }: { modulo: string }) {
  return (
    <Estado icono={<ShieldAlert className="size-8" aria-hidden="true" />} titulo="No tienes acceso">
      Tu rol no incluye {modulo}. Si lo necesitas, pídeselo al líder técnico.
    </Estado>
  );
}

/**
 * Error inesperado al pintar una ruta. Si es un chunk que no se descargó (lo normal tras publicar una versión nueva),
 * recarga sola una vez; si ya lo intentó, ofrece recargar. Cualquier otro error se reporta a atlas-api si hay sesión y
 * la persona ve una referencia para citarla, con sesión o sin ella (`errores.ts`).
 */
export function ErrorDeRuta() {
  const error = useRouteError();
  const carga = esErrorDeCarga(error);
  const [recargando, setRecargando] = useState(() => carga && !yaSeIntentoRecarga());
  const [referencia] = useState(referenciaCorta);
  const detalle = isRouteErrorResponse(error) ? `${error.status} ${error.statusText}` : null;

  useEffect(() => {
    if (!carga) reportarError(error, referencia);
    else if (debeRecargarPorCarga()) pagina.recargar();
    else setRecargando(false);
  }, [carga, error, referencia]);

  if (recargando) {
    return (
      <main className="px-4">
        <p role="status" className="py-16 text-center text-atlas-texto-suave">
          Hay una versión nueva de Atlas. Recargando…
        </p>
      </main>
    );
  }
  return (
    <main className="px-4">
      <Estado icono={<TriangleAlert className="size-8" aria-hidden="true" />} titulo="Algo salió mal">
        <p>
          {carga
            ? 'No pudimos descargar esta pantalla. Puede que se haya publicado una versión nueva o que no tengas conexión: recarga la página.'
            : 'No pudimos mostrar esta pantalla. Recarga la página; si sigue pasando, avísale al equipo de Atlas con esta referencia.'}
        </p>
        {detalle && <p className="mt-2 text-sm">{detalle}</p>}
        {!carga && (
          <p className="mt-3 text-sm">
            Referencia: <code className="font-semibold text-foreground select-all">{referencia}</code>
          </p>
        )}
        <button
          type="button"
          onClick={() => pagina.recargar()}
          className="mt-4 inline-flex min-h-toque items-center rounded-xl border border-border bg-card px-5 text-sm font-medium text-foreground hover:bg-atlas-activo"
        >
          Recargar la página
        </button>
      </Estado>
    </main>
  );
}
