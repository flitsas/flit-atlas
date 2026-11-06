import { useId, useMemo, useState, type ReactNode } from 'react';

import { Boton, cn } from '../../../app/ui/Tarjeta';
import { analizarMdx, atributosLiterales, enlaceSeguro, type EntradaIndice, indiceDe, type Nodo, pestanasDe, sinFrontMatter, textoDe } from './analizar';
import { DiagramaMermaid } from '../../../app/diagramas/DiagramaMermaid';
import { VivoContrato, VivoDiagrama, VivoResponsables, VivoSoporteDeTurno } from './vivos';

/**
 * Renderizador de MDX por árbol (ATLAS-ADR-0053 §5): nunca compila ni evalúa el texto, ni usa HTML crudo. Lo que no conoce
 * se muestra como texto inerte. Cada nodo se convierte en un elemento de React, que escapa todo lo demás.
 */
interface Contexto {
  ids: Map<Nodo, string>;
  definiciones: Map<string, Nodo>;
}

const CLASES =
  'min-w-0 text-base leading-relaxed break-words [&_a]:text-atlas-tinta [&_a]:underline [&_blockquote]:my-3 [&_blockquote]:border-l-4 [&_blockquote]:border-border [&_blockquote]:pl-3 [&_blockquote]:text-atlas-texto-suave [&_code]:rounded [&_code]:bg-atlas-activo [&_code]:px-1 [&_code]:text-[0.85em] [&_h1]:mt-6 [&_h1]:mb-2 [&_h1]:scroll-mt-20 [&_h1]:text-2xl [&_h1]:font-semibold [&_h2]:mt-6 [&_h2]:mb-2 [&_h2]:scroll-mt-20 [&_h2]:text-xl [&_h2]:font-semibold [&_h3]:mt-4 [&_h3]:mb-1 [&_h3]:scroll-mt-20 [&_h3]:text-lg [&_h3]:font-semibold [&_h4]:mt-3 [&_h4]:font-semibold [&_hr]:my-4 [&_hr]:border-border [&_li]:my-1 [&_ol]:list-decimal [&_ol]:pl-6 [&_p]:my-3 [&_pre]:my-3 [&_pre]:overflow-x-auto [&_pre]:rounded-xl [&_pre]:bg-atlas-activo [&_pre]:p-3 [&_pre]:text-sm [&_pre_code]:bg-transparent [&_pre_code]:p-0 [&_td]:border [&_td]:border-border [&_td]:px-2 [&_td]:py-1 [&_th]:border [&_th]:border-border [&_th]:px-2 [&_th]:py-1 [&_th]:text-left [&_ul]:list-disc [&_ul]:pl-6';

const TONOS: Record<string, { clase: string; titulo: string }> = {
  info: { clase: 'border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]', titulo: 'Nota' },
  aviso: { clase: 'border-[var(--badge-neutral-border)] bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]', titulo: 'Atención' },
  peligro: { clase: 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]', titulo: 'Cuidado' },
  exito: { clase: 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]', titulo: 'Bien' },
};

function Pestanas({ nodo, ctx }: { nodo: Nodo; ctx: Contexto }) {
  const pestanas = useMemo(() => pestanasDe(nodo), [nodo]);
  const [activa, setActiva] = useState(0);
  const id = useId();
  if (pestanas.length === 0) return null;
  const actual = pestanas[Math.min(activa, pestanas.length - 1)]!;
  return (
    <section aria-label="Pestañas del documento" className="my-3 min-w-0 rounded-xl border border-border p-3">
      <div role="group" aria-label="Elige una pestaña" className="mb-2 flex flex-wrap gap-2">
        {pestanas.map((p, i) => (
          <Boton key={`${id}-${i}`} variante={p === actual ? 'primario' : 'secundario'} aria-pressed={p === actual} onClick={() => setActiva(i)}>
            {p.titulo}
          </Boton>
        ))}
      </div>
      <div>{hijos(actual.hijos, ctx)}</div>
    </section>
  );
}

function Componente({ nodo, ctx }: { nodo: Nodo; ctx: Contexto }) {
  const a = atributosLiterales(nodo);
  switch (nodo.name) {
    case 'Contrato':
      return <VivoContrato id={a.id} operacion={a.operacion} />;
    case 'Diagrama':
      return <VivoDiagrama id={a.id} />;
    case 'Responsables':
      return <VivoResponsables aplicativo={a.aplicativo} />;
    case 'SoporteDeTurno':
      return <VivoSoporteDeTurno />;
    case 'Alerta': {
      const tono = TONOS[a.tipo ?? 'info'] ?? TONOS.info!;
      return (
        <aside aria-label={a.titulo ?? tono.titulo} className={cn('my-3 rounded-xl border p-3 text-sm [&_p]:my-1', tono.clase)}>
          <p className="font-semibold">{a.titulo ?? tono.titulo}</p>
          {hijos(nodo.children, ctx)}
        </aside>
      );
    }
    case 'Pestanas':
      return <Pestanas nodo={nodo} ctx={ctx} />;
    default:
      // Fuera de la lista blanca: un aviso, sin ejecutarlo. Su contenido no se pinta como componente.
      return (
        <p role="note" className="my-3 rounded-xl border border-border p-3 text-sm text-atlas-texto-suave">
          Componente no permitido: {nodo.name ?? 'sin nombre'}.
        </p>
      );
  }
}

function hijos(nodos: Nodo[] | undefined, ctx: Contexto): ReactNode {
  return (nodos ?? []).map((n, i) => <Fragmento key={i} nodo={n} ctx={ctx} />);
}

function Enlace({ url, titulo, children }: { url?: string | null; titulo?: string | null; children: ReactNode }) {
  const enlace = enlaceSeguro(url);
  if (!enlace) return <>{children}</>;
  return (
    <a href={enlace.href} title={titulo ?? undefined} {...(enlace.externo ? { target: '_blank', rel: 'noopener noreferrer nofollow' } : {})}>
      {children}
    </a>
  );
}

/** Las imágenes no se cargan (la CSP solo deja `img-src 'self' data:` y una imagen ajena revelaría quién lee): queda su texto y su enlace. */
function Imagen({ url, alt }: { url?: string | null; alt?: string | null }) {
  const texto = `Imagen: ${alt || 'sin descripción'}`;
  return enlaceSeguro(url) ? <Enlace url={url}>{texto}</Enlace> : <span>{texto}</span>;
}

function Fragmento({ nodo, ctx }: { nodo: Nodo; ctx: Contexto }): ReactNode {
  switch (nodo.type) {
    case 'paragraph':
      return <p>{hijos(nodo.children, ctx)}</p>;
    case 'heading': {
      const Etiqueta = `h${Math.min(Math.max(nodo.depth ?? 2, 1), 6)}` as 'h1';
      return <Etiqueta id={ctx.ids.get(nodo)}>{hijos(nodo.children, ctx)}</Etiqueta>;
    }
    case 'text':
      return nodo.value;
    case 'emphasis':
      return <em>{hijos(nodo.children, ctx)}</em>;
    case 'strong':
      return <strong>{hijos(nodo.children, ctx)}</strong>;
    case 'delete':
      return <del>{hijos(nodo.children, ctx)}</del>;
    case 'inlineCode':
      return <code>{nodo.value}</code>;
    case 'break':
      return <br />;
    case 'thematicBreak':
      return <hr />;
    case 'blockquote':
      return <blockquote>{hijos(nodo.children, ctx)}</blockquote>;
    case 'list': {
      if (nodo.ordered) return <ol start={nodo.start ?? undefined}>{hijos(nodo.children, ctx)}</ol>;
      return <ul>{hijos(nodo.children, ctx)}</ul>;
    }
    case 'listItem':
      return (
        <li>
          {typeof nodo.checked === 'boolean' && <span aria-label={nodo.checked ? 'Hecho' : 'Pendiente'} role="img">{nodo.checked ? '☑ ' : '☐ '}</span>}
          {hijos(nodo.children, ctx)}
        </li>
      );
    case 'code':
      if (nodo.lang === 'mermaid') return <DiagramaMermaid fuente={nodo.value ?? ''} />;
      return (
        <pre tabIndex={0}>
          <code>{nodo.value}</code>
        </pre>
      );
    case 'link':
      return (
        <Enlace url={nodo.url} titulo={nodo.title}>
          {hijos(nodo.children, ctx)}
        </Enlace>
      );
    case 'linkReference': {
      const d = nodo.identifier ? ctx.definiciones.get(nodo.identifier) : undefined;
      return (
        <Enlace url={d?.url} titulo={d?.title}>
          {hijos(nodo.children, ctx)}
        </Enlace>
      );
    }
    case 'image':
      return <Imagen url={nodo.url} alt={nodo.alt} />;
    case 'imageReference': {
      const d = nodo.identifier ? ctx.definiciones.get(nodo.identifier) : undefined;
      return <Imagen url={d?.url} alt={nodo.alt} />;
    }
    case 'table': {
      const [encabezado, ...filas] = nodo.children ?? [];
      return (
        <div tabIndex={0} role="region" aria-label="Tabla" className="my-3 overflow-x-auto">
          <table>
            {encabezado && (
              <thead>
                <tr>{(encabezado.children ?? []).map((c, i) => <th key={i}>{hijos(c.children, ctx)}</th>)}</tr>
              </thead>
            )}
            <tbody>
              {filas.map((f, i) => (
                <tr key={i}>{(f.children ?? []).map((c, j) => <td key={j}>{hijos(c.children, ctx)}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    }
    case 'definition':
    case 'footnoteDefinition':
      return null;
    case 'footnoteReference':
      return <sup>[{nodo.label ?? nodo.identifier}]</sup>;
    case 'mdxJsxFlowElement':
    case 'mdxJsxTextElement':
      return <Componente nodo={nodo} ctx={ctx} />;
    // Inertes: expresiones, import y export, y HTML crudo se muestran como texto, sin ejecutarlos.
    case 'mdxFlowExpression':
    case 'mdxjsEsm':
    case 'html':
      return <p className="font-mono text-sm text-atlas-texto-suave">{nodo.type === 'mdxFlowExpression' ? `{${nodo.value ?? ''}}` : nodo.value}</p>;
    case 'mdxTextExpression':
      return <code>{`{${nodo.value ?? ''}}`}</code>;
    default:
      return textoDe(nodo);
  }
}

function definicionesDe(arbol: Nodo, salida = new Map<string, Nodo>()): Map<string, Nodo> {
  for (const hijo of arbol.children ?? []) {
    if (hijo.type === 'definition' && hijo.identifier) salida.set(hijo.identifier, hijo);
    else definicionesDe(hijo, salida);
  }
  return salida;
}

export function IndiceDocumento({ entradas }: { entradas: EntradaIndice[] }) {
  if (entradas.length < 2) return null;
  return (
    <nav aria-label="Índice del documento" className="rounded-xl border border-border bg-card p-3 text-sm">
      <p className="mb-1 font-medium">En este documento</p>
      <ul className="flex flex-col">
        {entradas.map((e) => (
          <li key={e.id} className={cn(e.nivel === 2 && 'pl-2', e.nivel === 3 && 'pl-5')}>
            <a href={`#${e.id}`} className="flex min-h-toque items-center text-atlas-tinta hover:underline">
              {e.texto}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** El documento con su índice: el índice queda a un lado en escritorio y arriba en el móvil. */
export default function Documento({ texto, etiqueta, conIndice = true }: { texto: string; etiqueta: string; conIndice?: boolean }) {
  const analisis = useMemo(() => {
    const r = analizarMdx(sinFrontMatter(texto));
    if (!r.ok) return r;
    const { entradas, ids } = indiceDe(r.arbol);
    return { ...r, entradas, ctx: { ids, definiciones: definicionesDe(r.arbol) } satisfies Contexto };
  }, [texto]);

  if (!analisis.ok) {
    return (
      <article aria-label={etiqueta} className="flex min-w-0 flex-col gap-2">
        <p role="alert" className="rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]">
          Este texto tiene un error de MDX y se muestra sin formato: {analisis.mensaje}
        </p>
        <pre tabIndex={0} className="overflow-x-auto rounded-xl bg-atlas-activo p-3 text-sm whitespace-pre-wrap">
          {texto}
        </pre>
      </article>
    );
  }
  const cuerpo = (
    <article aria-label={etiqueta} className={CLASES}>
      {hijos(analisis.arbol.children, analisis.ctx)}
    </article>
  );
  if (!conIndice || analisis.entradas.length < 2) return cuerpo;
  return (
    <div className="grid min-w-0 gap-4 lg:grid-cols-[minmax(0,1fr)_16rem]">
      <div className="lg:order-2">
        <div className="lg:sticky lg:top-20">
          <IndiceDocumento entradas={analisis.entradas} />
        </div>
      </div>
      <div className="min-w-0 lg:order-1">{cuerpo}</div>
    </div>
  );
}
