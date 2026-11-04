import DOMPurify from 'dompurify';
import { Marked } from 'marked';
import { useMemo } from 'react';

/**
 * Markdown de las guías, renderizado seguro (HU 3.7, ATLAS-ADR-0040). Va en su propio chunk (marked + DOMPurify) y lo
 * cargan la vista de la guía, la vista previa del editor y nadie más.
 *
 * Dos capas: marked no interpreta HTML (los bloques y etiquetas HTML del texto salen escapados, como texto) y DOMPurify
 * deja solo una lista blanca de etiquetas y atributos, sin `style` ni `class` (la CSP no permite estilos en línea y una
 * clase de Tailwind podría tapar la interfaz), sin imágenes (la CSP solo deja `img-src 'self' data:`) y con enlaces
 * http(s), mailto o anclas, que abren aparte y sin `Referer` ni `opener`.
 */
const ETIQUETAS = [
  'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'p', 'br', 'hr', 'strong', 'em', 'del', 's', 'code', 'pre', 'blockquote',
  'ul', 'ol', 'li', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
];
const ATRIBUTOS = ['href', 'title', 'align', 'start'];
const ENLACES_PERMITIDOS = /^(?:https?:|mailto:|#)/i;

const escapar = (texto: string) =>
  texto.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');

const marcado = new Marked({
  gfm: true,
  async: false,
  // El HTML del texto se muestra como texto: nunca se interpreta.
  renderer: { html: ({ text }) => escapar(text) },
});

let ganchoInstalado = false;

function instalarGancho() {
  if (ganchoInstalado) return;
  ganchoInstalado = true;
  DOMPurify.addHook('afterSanitizeAttributes', (nodo) => {
    if (nodo.tagName !== 'A') return;
    const href = nodo.getAttribute('href') ?? '';
    if (!ENLACES_PERMITIDOS.test(href)) {
      nodo.removeAttribute('href');
      return;
    }
    if (!href.startsWith('#')) {
      nodo.setAttribute('target', '_blank');
      nodo.setAttribute('rel', 'noopener noreferrer nofollow');
    }
  });
}

/** HTML sanitizado de un texto Markdown. Exportado para las pruebas. */
export function markdownSeguro(texto: string): string {
  instalarGancho();
  const html = marcado.parse(texto, { async: false });
  return DOMPurify.sanitize(html, {
    ALLOWED_TAGS: ETIQUETAS,
    ALLOWED_ATTR: ATRIBUTOS,
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
    ADD_ATTR: ['target', 'rel'],
  });
}

const CLASES =
  'min-w-0 text-sm leading-relaxed break-words [&_a]:text-atlas-tinta [&_a]:underline [&_blockquote]:border-l-4 [&_blockquote]:border-border [&_blockquote]:pl-3 [&_blockquote]:text-atlas-texto-suave [&_code]:rounded [&_code]:bg-atlas-activo [&_code]:px-1 [&_code]:text-[0.85em] [&_h1]:mt-4 [&_h1]:mb-2 [&_h1]:text-xl [&_h1]:font-semibold [&_h2]:mt-4 [&_h2]:mb-2 [&_h2]:text-lg [&_h2]:font-semibold [&_h3]:mt-3 [&_h3]:mb-1 [&_h3]:font-semibold [&_h4]:font-semibold [&_hr]:my-4 [&_hr]:border-border [&_li]:my-1 [&_ol]:list-decimal [&_ol]:pl-6 [&_p]:my-2 [&_pre]:my-2 [&_pre]:overflow-x-auto [&_pre]:rounded-xl [&_pre]:bg-atlas-activo [&_pre]:p-3 [&_pre_code]:bg-transparent [&_pre_code]:p-0 [&_table]:my-2 [&_table]:block [&_table]:overflow-x-auto [&_td]:border [&_td]:border-border [&_td]:px-2 [&_td]:py-1 [&_th]:border [&_th]:border-border [&_th]:px-2 [&_th]:py-1 [&_th]:text-left [&_ul]:list-disc [&_ul]:pl-6';

export default function Markdown({ texto, etiqueta }: { texto: string; etiqueta?: string }) {
  const html = useMemo(() => markdownSeguro(texto), [texto]);
  // HTML sanitizado arriba con lista blanca (marked sin HTML + DOMPurify).
  return <article aria-label={etiqueta} className={CLASES} dangerouslySetInnerHTML={{ __html: html }} />;
}
