"""Markdown de las guías y descripciones a HTML seguro, sin recursos remotos.

markdown-it-py en modo CommonMark con el HTML crudo apagado (se escapa) y tablas. Para que el HTML exportado no
cargue ni enlace nada de afuera:

- un enlace a un ancla interna (`#...`) se conserva; uno externo pierde el `href` y su dirección queda escrita como
  texto, entre paréntesis;
- una imagen se reemplaza por su texto alternativo.
"""

from collections.abc import Sequence
from typing import Any

from markdown_it import MarkdownIt
from markdown_it.renderer import RendererHTML
from markdown_it.token import Token
from markdown_it.utils import EnvType, OptionsDict
from markupsafe import Markup, escape


def _abrir_enlace(self: RendererHTML, tokens: Sequence[Token], idx: int, options: OptionsDict, env: EnvType) -> str:
    destino = str(tokens[idx].attrGet("href") or "")
    if destino.startswith("#"):
        return f'<a href="{escape(destino)}">'
    pila: list[str] = env.setdefault("enlaces", [])
    pila.append(destino)
    return '<span class="enlace-externo">'


def _cerrar_enlace(self: RendererHTML, tokens: Sequence[Token], idx: int, options: OptionsDict, env: EnvType) -> str:
    # Se cierra el enlace abierto más reciente (los enlaces no se anidan en CommonMark).
    pila: list[str] = env.get("enlaces") or []
    if not pila:
        return "</a>"
    destino = pila.pop()
    return f"</span> ({escape(destino)})" if destino else "</span>"


def _imagen(self: RendererHTML, tokens: Sequence[Token], idx: int, options: OptionsDict, env: EnvType) -> str:
    alternativo = self.renderInlineAsText(tokens[idx].children or [], options, env)
    return f'<span class="imagen-omitida">[imagen: {escape(alternativo or "sin descripción")}]</span>'


def _crear() -> MarkdownIt:
    md = MarkdownIt("commonmark", {"html": False, "linkify": False, "typographer": False}).enable("table")
    md.add_render_rule("link_open", _abrir_enlace)
    md.add_render_rule("link_close", _cerrar_enlace)
    md.add_render_rule("image", _imagen)
    return md


_MD = _crear()


def a_html(texto: str, desplazar: int = 3) -> Markup:
    """HTML seguro para insertar en la plantilla (el HTML crudo del Markdown sale escapado).

    Los títulos bajan `desplazar` niveles (un `#` de una guía queda debajo del título de la guía, como `h4`).
    """
    env: dict[str, Any] = {}
    bloques = _MD.parse(texto or "", env)
    for t in bloques:
        if t.type in ("heading_open", "heading_close"):
            t.tag = f"h{min(6, int(t.tag[1]) + desplazar)}"
    return Markup(_MD.renderer.render(bloques, _MD.options, env))  # noqa: S704 — renderizador con html=False


def tokens(texto: str) -> list[Token]:
    """Tokens de bloque para quien arma otro formato (Word)."""
    return _MD.parse(texto or "", {})


def texto_plano(nodos: Sequence[Any]) -> str:
    """Texto de los hijos de un token inline (para celdas y títulos)."""
    partes: list[str] = []
    for n in nodos:
        if n.type in ("text", "code_inline"):
            partes.append(n.content)
        elif n.type in ("softbreak", "hardbreak"):
            partes.append(" ")
        elif n.type == "image":
            partes.append(f"[imagen: {n.content}]")
    return "".join(partes)
