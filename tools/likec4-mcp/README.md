# Servidor MCP de LikeC4 para el equipo

HU 6.9 ([ATLAS-ADR-0056](../../docs/adr/0056-arquitectura-likec4.md) §8). Sirve para preguntarle al modelo de arquitectura desde Claude Code, Cursor u otro cliente MCP: qué servicios tocan un sistema, cómo se llega de A a B o qué elementos llevan cierta etiqueta. Responde con el modelo, no con lo que el asistente recuerde.

Esto es para correr **en tu equipo**. No está desplegado en ningún ambiente y no tiene autenticación: no lo expongas a la red. El uso desde el Asistente Atlas queda para F7.

## Opción 1: por stdio, en tu cliente MCP

Es la más simple: el cliente lo arranca cuando lo necesita. Copia [`mcp.ejemplo.json`](mcp.ejemplo.json) a `.mcp.json` en la raíz del repo (`flit` o `flito`) y ajusta la carpeta del modelo:

| Repo | Carpeta |
|---|---|
| `flit`, modelo de FLIT 2.0 | `docs/arquitectura` |
| `flit`, landscape | `arquitectura/landscape` |
| `flito` | `docs/arquitectura` |

La versión va fijada en `1.59.4`, la misma de la CI de esos repos y del visor de Atlas. Si la cambias en un lado, cámbiala en todos.

## Opción 2: por HTTP, en un contenedor

Sirve si no quieres Node en tu equipo o quieres compartirlo entre varios clientes. El modelo se monta en solo lectura y el puerto queda solo en tu equipo:

```bash
docker build -t atlas-likec4-mcp tools/likec4-mcp
docker run --rm -p 127.0.0.1:33335:33335 -v "$PWD/docs/arquitectura:/modelo:ro" atlas-likec4-mcp
```

El cliente apunta a `http://127.0.0.1:33335/mcp`. El `127.0.0.1` del `-p` importa: sin él, cualquiera en tu red podría consultarlo.

## Las herramientas

Son 20 en 1.59.4. Las que más se usan:

| Herramienta | Para qué |
|---|---|
| `list-projects`, `read-project-summary` | Qué hay en el modelo |
| `search-element`, `read-element` | Buscar y leer un elemento, con sus metadatos |
| `find-relationships`, `find-relationship-paths` | Qué se conecta con qué y por dónde |
| `query-by-tags`, `query-by-metadata` | Por ejemplo, todo lo marcado `#pii` |
| `read-view`, `read-deployment` | Leer una vista o el modelo de despliegue |

Ten presente:

- `apply-semantic-layout` puede cambiar la disposición de una vista. En el contenedor no toca tus archivos porque la carpeta va en solo lectura; **por stdio trabaja sobre tu copia del repo**, así que revisa el diff.
- `render-view` y `preview-view` necesitan un navegador. El contenedor no lo trae: ahí no están disponibles.
- Lo que responde es el modelo de tu carpeta, no lo publicado en Atlas. Cambia de rama y cambian las respuestas.

## Comprobar que funciona

```bash
cd tools/likec4-mcp
npm ci --ignore-scripts
node probar.mjs                 # usa el modelo ficticio de tools/arquitectura-gen/fixtures
node probar.mjs ../../docs/...  # o la carpeta que le digas
```

Hace el saludo del protocolo, lista las herramientas y le pregunta por un elemento.

## Licencia

`likec4` y `@likec4/mcp` son MIT.
