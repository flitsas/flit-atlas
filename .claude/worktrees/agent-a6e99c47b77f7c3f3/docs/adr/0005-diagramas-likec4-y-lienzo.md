---
id: ATLAS-ADR-0005
titulo: Diagramas — LikeC4 como código, lienzo propio, Mermaid, BPMN y D2
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-04 y §8.6, plan 00 D-10, plan 01 §4, herramientas-y-lenguajes.md caso 1 y caso 2]
---

# ATLAS-ADR-0005 · Diagramas — LikeC4 como código, lienzo propio, Mermaid, BPMN y D2

## Contexto y problema

Hay dos necesidades distintas: (a) la **arquitectura oficial**, que debe vivir junto al código, salir por ambiente desde `flit-gitops` y detectar drift (plan 01), y (b) los **diagramas hechos a mano** por personas que no editan repos (procesos, bocetos, explicaciones para clientes). El plan 01 resuelve (a) con LikeC4. El documento de herramientas propone para (b) un modelo propio en PostgreSQL, editado en React Flow y renderizado con D2.

## Factores de decisión

- Una sola verdad para la arquitectura oficial: el código.
- Que negocio pueda dibujar sin git.
- Render de alta calidad para exportes (PDF, manuales).
- Seguridad del contenido renderizado (SVG sin scripts, Mermaid estricto, BPMN validado).

## Opciones consideradas

1. **Ambos enfoques**: LikeC4 en cada repo para lo oficial; lienzo React Flow + ELK.js con modelo propio para lo manual; Mermaid y bpmn-js para lo funcional; D2 (Go) para exportar.
2. Solo LikeC4.
3. Solo el modelo C4 propio en PostgreSQL (caso 1 del documento de herramientas).
4. Structurizr.

## Decisión

Opción elegida: **1**.

- **Arquitectura oficial:** LikeC4, con versión fijada (plan 01 §4: línea 1.59.x; al 21/10/2026 la última es la 1.60.0 del 15/10/2026, con licencia MIT. **[Decidir en F6 entre 1.59.x y 1.60.x, respetando el mínimo de dos semanas de publicada]**). Modelo por repo, vistas por ambiente generadas desde `flit-gitops` y drift en tres capas. Se publica en Atlas por CI (ATLAS-ADR-0009). Tablas `arq_*`.
- **Lienzo de Atlas:** React Flow + ELK.js, con modelo propio (`dgm_diagrama`, `dgm_version`, `dgm_nodo`, `dgm_relacion`, `dgm_comentario`), versiones y comentarios. Sus nodos pueden **referenciar elementos LikeC4 por id, en solo lectura**, y aplicativos del catálogo.
- **Funcionales:** Mermaid (`securityLevel: 'strict'`) y bpmn-js (manteniendo visible la marca bpmn.io), con XML BPMN validado y limpiado en el servidor.
- **Exportes:** `atlas-render` (Go) renderiza con D2 a SVG y PNG, y sanitiza el SVG (sin `<script>`, sin `foreignObject` ni enlaces externos). Caché por hash del modelo.

## Consecuencias

### Positivas

- Lo oficial no se puede editar en Atlas, así que no hay dos verdades. Lo manual no exige git.
- Los exportes tienen una calidad uniforme, con un único motor (D2).

### Negativas

- Dos modelos de diagramas que mantener, con una regla clara: si describe código desplegado, va en LikeC4.
- Traducir del lienzo a D2 o a Mermaid es trabajo propio (y solo «cuando sea posible» para Mermaid).
- Licencias: bpmn-js exige mantener visible la marca bpmn.io. D2 es **MPL-2.0** (verificado): copyleft por archivo, que obliga a publicar solo los archivos de D2 que se modifiquen; usarlo como librería no obliga a abrir el código de Atlas. El módulo Go se movió a `github.com/d2lang/d2` (`oss.terrastruct.com/d2` está deprecado).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Solo LikeC4 | Negocio no edita repos; los diagramas manuales se irían a herramientas sueltas |
| Solo el modelo propio | Duplica lo que ya vive en el código y se desactualiza (justo el problema que LikeC4 resuelve) |
| Structurizr | Lite es para un solo usuario y es Java; incertidumbre sobre su versión en la nube (plan 00 D-10) |

## Más información

- Plan 01 completo (fase F6). Prompt maestro §8.6.
