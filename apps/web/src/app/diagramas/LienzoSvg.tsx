import { ALTO_NODO, ANCHO_NODO, type Lienzo, TEXTO_TIPO_NODO } from './lienzo';

const MARGEN = 24;

/** Corta el texto a lo que cabe en un nodo: el SVG no parte líneas solo. El texto completo queda en `<title>`. */
function cortar(texto: string, largo: number): string {
  return texto.length > largo ? `${texto.slice(0, largo - 1)}…` : texto;
}

/** Punto del borde del nodo centrado en `desde` por donde sale la línea hacia `hacia`: así no cruza el texto ni tapa la flecha. */
function bordeHacia(desde: { x: number; y: number }, hacia: { x: number; y: number }) {
  const dx = hacia.x - desde.x;
  const dy = hacia.y - desde.y;
  if (dx === 0 && dy === 0) return desde;
  const escala = Math.min(dx === 0 ? Infinity : ANCHO_NODO / 2 / Math.abs(dx), dy === 0 ? Infinity : ALTO_NODO / 2 / Math.abs(dy));
  return { x: desde.x + dx * escala, y: desde.y + dy * escala };
}

/**
 * Lectura de un lienzo (ATLAS-ADR-0054 §5): un SVG propio, sin React Flow ni estilos en línea. Los textos van como nodos de
 * texto de React, nunca como HTML. La lista que sigue da el mismo contenido a los lectores de pantalla.
 */
export function LienzoSvg({ lienzo, titulo }: { lienzo: Lienzo; titulo: string }) {
  const { nodos, relaciones } = lienzo;
  if (nodos.length === 0) return <p className="my-3 text-sm text-atlas-texto-suave">Este lienzo no tiene nodos.</p>;
  const minX = Math.min(...nodos.map((n) => n.x));
  const minY = Math.min(...nodos.map((n) => n.y));
  const ancho = Math.max(...nodos.map((n) => n.x)) - minX + ANCHO_NODO + MARGEN * 2;
  const alto = Math.max(...nodos.map((n) => n.y)) - minY + ALTO_NODO + MARGEN * 2;
  const porId = new Map(nodos.map((n) => [n.id, n]));
  const x = (valor: number) => valor - minX + MARGEN;
  const y = (valor: number) => valor - minY + MARGEN;

  return (
    <figure className="my-3 min-w-0">
      <div className="overflow-auto rounded-xl border border-border bg-card">
        <svg role="img" aria-label={titulo} viewBox={`0 0 ${ancho} ${alto}`} className="h-auto min-w-[320px] w-full" preserveAspectRatio="xMidYMin meet">
          <defs>
            <marker id="atlas-flecha" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M 0 0 L 10 5 L 0 10 z" className="fill-atlas-texto-suave" />
            </marker>
          </defs>
          {relaciones.map((r) => {
            const a = porId.get(r.origen);
            const b = porId.get(r.destino);
            if (!a || !b) return null;
            const centroA = { x: x(a.x) + ANCHO_NODO / 2, y: y(a.y) + ALTO_NODO / 2 };
            const centroB = { x: x(b.x) + ANCHO_NODO / 2, y: y(b.y) + ALTO_NODO / 2 };
            const { x: x1, y: y1 } = bordeHacia(centroA, centroB);
            const { x: x2, y: y2 } = bordeHacia(centroB, centroA);
            return (
              <g key={r.id}>
                <line x1={x1} y1={y1} x2={x2} y2={y2} markerEnd="url(#atlas-flecha)" className="stroke-atlas-texto-suave" strokeWidth={1.5} />
                {r.etiqueta && (
                  <text x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 6} textAnchor="middle" fontSize={12} className="fill-atlas-texto-suave">
                    {cortar(r.etiqueta, 30)}
                  </text>
                )}
              </g>
            );
          })}
          {nodos.map((n) => (
            <g key={n.id}>
              <title>{`${TEXTO_TIPO_NODO[n.tipo] ?? n.tipo}: ${n.etiqueta}`}</title>
              <rect x={x(n.x)} y={y(n.y)} width={ANCHO_NODO} height={ALTO_NODO} rx={n.tipo === 'actor' ? 32 : 10} className="fill-atlas-activo stroke-border" strokeWidth={1.5} />
              <text x={x(n.x) + ANCHO_NODO / 2} y={y(n.y) + 28} textAnchor="middle" fontSize={13} fontWeight={600} className="fill-current">
                {cortar(n.etiqueta, 22)}
              </text>
              <text x={x(n.x) + ANCHO_NODO / 2} y={y(n.y) + 46} textAnchor="middle" fontSize={11} className="fill-atlas-texto-suave">
                {TEXTO_TIPO_NODO[n.tipo] ?? n.tipo}
              </text>
            </g>
          ))}
        </svg>
      </div>
      <details className="mt-2 text-sm">
        <summary className="min-h-toque cursor-pointer py-2">Ver el contenido como lista</summary>
        <ul className="list-disc pl-5">
          {nodos.map((n) => (
            <li key={n.id} className="break-words">
              {TEXTO_TIPO_NODO[n.tipo] ?? n.tipo}: {n.etiqueta}
            </li>
          ))}
          {relaciones.map((r) => (
            <li key={r.id} className="break-words">
              {porId.get(r.origen)?.etiqueta ?? r.origen} → {porId.get(r.destino)?.etiqueta ?? r.destino}
              {r.etiqueta ? ` (${r.etiqueta})` : ''}
            </li>
          ))}
        </ul>
      </details>
    </figure>
  );
}
