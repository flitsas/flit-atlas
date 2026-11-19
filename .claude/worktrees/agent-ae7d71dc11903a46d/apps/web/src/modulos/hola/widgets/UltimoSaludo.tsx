import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { formatearRelativo } from '../../../app/ui/formato';
import { useSaludos } from '../api';

/** Widget de Inicio del módulo de ejemplo: el saludo más reciente de tu organización. */
export default function UltimoSaludo() {
  const saludos = useSaludos();
  const ultimo = saludos.data?.[0];

  return (
    <div className="flex flex-col gap-3 text-sm">
      {saludos.isError && <p role="alert">No pudimos cargar los saludos.</p>}
      {saludos.data && !ultimo && <p className="text-atlas-texto-suave">Todavía no hay saludos en tu organización.</p>}
      {ultimo && (
        <p>
          <span className="block break-words">«{ultimo.texto}»</span>
          <time dateTime={ultimo.creadoEn} className="text-xs text-atlas-texto-suave">
            {formatearRelativo(new Date(ultimo.creadoEn))}
          </time>
        </p>
      )}
      <Link to="/hola" className="inline-flex min-h-toque items-center gap-1 self-start font-medium text-atlas-tinta hover:underline">
        Ver los saludos
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
