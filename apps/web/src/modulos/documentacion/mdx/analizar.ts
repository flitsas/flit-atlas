import remarkGfm from 'remark-gfm';
import remarkMdx from 'remark-mdx';
import remarkParse from 'remark-parse';
import { unified } from 'unified';

/**
 * Análisis del MDX a un árbol, sin compilarlo ni evaluarlo (ATLAS-ADR-0053 §5): el renderizador recorre este árbol y nada
 * del texto llega a ejecutarse. No confía en lo que validó el servidor.
 */
export interface Nodo {
  type: string;
  children?: Nodo[];
  value?: string;
  depth?: number;
  ordered?: boolean | null;
  start?: number | null;
  url?: string;
  title?: string | null;
  alt?: string | null;
  lang?: string | null;
  align?: Array<string | null> | null;
  identifier?: string;
  label?: string | null;
  checked?: boolean | null;
  name?: string | null;
  attributes?: Array<{ type: string; name?: string; value?: unknown }>;
}

export type ResultadoAnalisis = { ok: true; arbol: Nodo } | { ok: false; mensaje: string };

const procesador = unified().use(remarkParse).use(remarkGfm).use(remarkMdx);

/** El árbol del texto; un MDX mal formado no rompe la página: el lector lo muestra como texto. */
export function analizarMdx(texto: string): ResultadoAnalisis {
  try {
    return { ok: true, arbol: procesador.parse(texto) as unknown as Nodo };
  } catch (error) {
    return { ok: false, mensaje: error instanceof Error ? error.message : 'MDX mal formado.' };
  }
}

/** Solo atributos de cadena literal: una expresión (`{...}`) o un spread no cuentan (ATLAS-ADR-0053 §5). */
export function atributosLiterales(nodo: Nodo): Record<string, string> {
  const salida = Object.create(null) as Record<string, string>;
  for (const a of nodo.attributes ?? []) {
    if (a.type === 'mdxJsxAttribute' && a.name && typeof a.value === 'string') salida[a.name] = a.value;
  }
  return salida;
}

export type Enlace = { href: string; externo: boolean };

/** Con prefijo: un título del documento no puede pisar un id de la interfaz. */
export function idDeTitulo(texto: string): string {
  const base = texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/^doc-/, '');
  return `doc-${base || 'seccion'}`;
}

/**
 * Enlaces permitidos: `https`, `mailto`, rutas internas y anclas (ATLAS-ADR-0053 §5). Se descartan los caracteres de control
 * antes de mirar el esquema, porque el navegador también los ignora (`java\tscript:`). `//host` y `/\host` llevan a otro
 * origen: no son rutas internas.
 */
export function enlaceSeguro(url: string | undefined | null): Enlace | null {
  if (!url) return null;
  // eslint-disable-next-line no-control-regex
  const limpia = url.replace(/[\u0000- \u007f-\u009f]/g, '');
  if (limpia.startsWith('#')) return { href: `#${idDeTitulo(limpia.slice(1))}`, externo: false };
  if (limpia.startsWith('/')) {
    return limpia.startsWith('//') || limpia.startsWith('/\\') ? null : { href: limpia, externo: false };
  }
  if (/^https:/i.test(limpia)) {
    try {
      const u = new URL(limpia);
      return u.username || u.password ? null : { href: u.href, externo: true };
    } catch {
      return null;
    }
  }
  return /^mailto:/i.test(limpia) ? { href: limpia, externo: true } : null;
}

/** Texto plano de un nodo. */
export function textoDe(nodo: Nodo): string {
  if (typeof nodo.value === 'string' && !nodo.type.startsWith('mdx')) return nodo.value;
  return (nodo.children ?? []).map(textoDe).join('');
}

export interface EntradaIndice {
  id: string;
  texto: string;
  nivel: number;
}

/**
 * Índice (títulos de nivel 1 a 3 fuera de los componentes, salvo los de `Alerta`), con ids únicos en orden de aparición.
 * Los títulos dentro de `Pestanas` son las pestañas: no entran.
 */
export function indiceDe(arbol: Nodo): { entradas: EntradaIndice[]; ids: Map<Nodo, string> } {
  const entradas: EntradaIndice[] = [];
  const ids = new Map<Nodo, string>();
  const usados = new Map<string, number>();
  const visitar = (nodo: Nodo) => {
    for (const hijo of nodo.children ?? []) {
      if (hijo.type === 'heading') {
        const texto = textoDe(hijo).trim();
        const base = idDeTitulo(texto);
        const veces = usados.get(base) ?? 0;
        usados.set(base, veces + 1);
        const id = veces === 0 ? base : `${base}-${veces + 1}`;
        ids.set(hijo, id);
        if ((hijo.depth ?? 1) <= 3 && texto) entradas.push({ id, texto, nivel: hijo.depth ?? 1 });
      } else if (hijo.type === 'mdxJsxFlowElement' && hijo.name === 'Alerta') {
        visitar(hijo);
      }
    }
  };
  visitar(arbol);
  return { entradas, ids };
}

/** Quita el front-matter de los documentos de repo: el servidor ya lo leyó y no es contenido. */
export function sinFrontMatter(texto: string): string {
  return /^---\r?\n/.test(texto) ? texto.replace(/^---\r?\n[\s\S]*?\r?\n---[ \t]*(?:\r?\n|$)/, '') : texto;
}

/** Trozos de `Pestanas`: cada título abre una pestaña. El validador del servidor no admite un `<Pestana>` hijo. */
export function pestanasDe(nodo: Nodo): Array<{ titulo: string; hijos: Nodo[] }> {
  const pestanas: Array<{ titulo: string; hijos: Nodo[] }> = [];
  for (const hijo of nodo.children ?? []) {
    if (hijo.type === 'heading') pestanas.push({ titulo: textoDe(hijo).trim() || `Pestaña ${pestanas.length + 1}`, hijos: [] });
    else if (pestanas.length > 0) pestanas[pestanas.length - 1]!.hijos.push(hijo);
    else pestanas.push({ titulo: 'General', hijos: [hijo] });
  }
  return pestanas;
}
