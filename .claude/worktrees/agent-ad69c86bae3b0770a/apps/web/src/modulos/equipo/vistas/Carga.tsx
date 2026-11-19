import { iniciales } from '../../../app/ui/formato';
import { Avatar } from '../../../app/ui/Marca';
import { cn, Tarjeta } from '../../../app/ui/Tarjeta';
import { obtenerCarga, useReparto } from '../apiResponsables';

const NIVEL = {
  alta: { texto: 'Carga alta', clase: 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]' },
  normal: { texto: 'Carga normal', clase: 'border-[var(--badge-neutral-border)] bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]' },
  baja: { texto: 'Carga baja', clase: 'border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)]' },
} as const;

const numero = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 });

/**
 * Mapa de carga (§8.2): cuántas responsabilidades tiene cada persona. Un principal cuenta 1 y una secundaria su
 * dedicación (≈ 20 %); se resalta a quien está más de un 30 % por encima o por debajo de la media. La barra es un
 * `<meter>` (accesible y sin estilos en línea, por la CSP).
 */
export default function Carga() {
  const carga = useReparto(['carga'], obtenerCarga);
  if (carga.isPending) return <p role="status">Cargando el mapa de carga…</p>;
  if (carga.isError || !carga.data) return <p role="alert">No pudimos cargar el mapa de carga. Revisa tu conexión.</p>;
  const { media, umbralAlto, umbralBajo, personas } = carga.data;
  const maximo = Math.max(1, ...personas.map((p) => p.puntaje));

  return (
    <Tarjeta titulo="Mapa de carga">
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Media del equipo: {numero.format(media)}. Carga alta por encima de {numero.format(umbralAlto)}; baja por debajo de {numero.format(umbralBajo)}.
        Un principal cuenta 1 y una secundaria su dedicación.
      </p>
      {personas.length === 0 && <p className="text-sm text-atlas-texto-suave">Aún no hay responsabilidades vigentes.</p>}
      <ul aria-label="Carga por persona" className="flex flex-col gap-3">
        {personas.map((p) => (
          <li key={p.persona.id} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
            <div className="flex min-w-0 flex-wrap items-center gap-3">
              <Avatar iniciales={iniciales(p.persona.nombre)} foto={p.persona.foto} className="size-9" />
              <p className="min-w-0 flex-1 font-medium break-words">{p.persona.nombre}</p>
              <span className={cn('inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium', NIVEL[p.nivel].clase)}>
                {NIVEL[p.nivel].texto}
              </span>
            </div>
            <meter
              min={0}
              max={maximo}
              low={umbralBajo}
              high={umbralAlto}
              optimum={media}
              value={p.puntaje}
              aria-label={`Carga de ${p.persona.nombre}: ${numero.format(p.puntaje)}`}
              className="h-3 w-full"
            />
            <p className="text-sm">
              {p.principales} principal{p.principales === 1 ? '' : 'es'} · {p.secundarias} secundaria{p.secundarias === 1 ? '' : 's'} · puntaje{' '}
              {numero.format(p.puntaje)}
            </p>
          </li>
        ))}
      </ul>
    </Tarjeta>
  );
}
