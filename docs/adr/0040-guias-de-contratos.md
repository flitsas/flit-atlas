---
id: ATLAS-ADR-0040
titulo: Guías de contratos — revisiones inmutables con huella, publicación que aprueba el responsable, Markdown sin HTML sanitizado en la PWA y diff de revisiones en el cliente
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7.1, §7.2 (rol implícito por propiedad), §8.4, §9, §11 y §12, plan 00 §8.1, §8.2 regla 6, §8.4, §8.5 y §8.9, HU 3.7, ATLAS-ADR-0023, ATLAS-ADR-0031, ATLAS-ADR-0035]
---

# ATLAS-ADR-0040 · Guías de contratos

## Contexto y problema

La HU 3.7 pide guías de integración por contrato (y opcionalmente por operación) escritas en Atlas: Markdown con título, cuerpo, versiones a las que aplica, visibilidad (interna o compartible con clientes) y estado (borrador o publicada). Cada guardado es una revisión inmutable con autor, fecha y nota; se comparan dos revisiones línea a línea; restaurar crea una revisión nueva igual a la elegida; todo queda auditado en la misma transacción. Lo compartible lo aprueba solo el responsable principal o secundario (§7.2). El Markdown se muestra sanitizado, sin HTML arbitrario y dentro de la CSP.

Hechos verificados en el código:

- El módulo de contratos (ATLAS-ADR-0035) ya tiene `contratos_contrato`, `contratos_version` (inmutable por trigger) y el índice `contratos_operacion`; `atlas.contratos.guias.editar` se concede al líder técnico y al rol implícito `atlas.responsable`.
- El módulo de equipo entrega `atlas.responsable` con alcance `aplicativo:<id>` por cada responsabilidad vigente (ATLAS-ADR-0031); el catálogo evalúa ese alcance sobre el aplicativo y sus ancestros. Los contratos suelen colgar de un servicio y los responsables, de su producto o módulo.
- La CSP de la PWA es `script-src 'self'; style-src 'self'; img-src 'self' data:`; no hay librería de Markdown ni de diff en la PWA. El historial del reparto (equipo) compara fotos en el servidor; el diff de contratos lo hace oasdiff en atlas-render (ATLAS-ADR-0036).

## Factores de decisión

- «Volver en el tiempo» sin reescribir la historia (plan 00 §8.2, regla 6) y con la base como garantía, como las versiones.
- Que un cliente (F4) nunca vea un texto que el responsable no aprobó.
- Ningún HTML ni script del autor llega al DOM; nada que la CSP tenga que relajar.
- Bundle inicial < 200 KB gzip; el editor, el render y el diff, diferidos.
- Pocas piezas nuevas: librerías maduras y pequeñas, nada de motores propios.

## Opciones consideradas

### Revisiones

1. **Foto completa por revisión** (título, cuerpo, operación, versiones y visibilidad) más la foto de la última en la guía, con una huella SHA-256.
2. Solo el cuerpo en la revisión y los metadatos en la guía.

### Aprobación de lo compartible

1. **Publicar es aprobar**: la guía apunta a su revisión publicada; publicar una compartible exige el rol implícito del responsable sobre el aplicativo o un ancestro.
2. Un flujo aparte de solicitud y aprobación (`en_revision`), como el de documentos de F5.

### Render del Markdown

1. **marked (sin HTML) + DOMPurify con lista blanca**, en el cliente y en un chunk diferido.
2. react-markdown (remark/rehype).
3. Render en el servidor (.NET, Markdig) y HTML sanitizado en la respuesta.

### Comparación de revisiones

1. **En el cliente con jsdiff (`diffLines`)**, sobre los dos cuerpos que da la API, en un chunk diferido.
2. En el servidor (.NET con DiffPlex), con un endpoint de comparación.

## Decisión

Opción 1 en las cuatro.

### Datos (`contratos_*`, migración `Guias`)

| Tabla | Para qué |
|---|---|
| `contratos_guia` | La guía de un contrato (llave foránea): la foto de su última revisión (título, operación, `aplica_desde`, `aplica_hasta`, visibilidad), `estado` (`borrador` o `publicada`), `revision_actual`, `revision_publicada`, quién y cuándo la publicó y, si es compartible, quién la aprobó. Un `CHECK` impide apuntar a una revisión que no existe |
| `contratos_guia_revision` | Una fila **inmutable** por guardado: número único por guía, origen (`creacion`, `edicion`, `restauracion`) con la revisión restaurada, la foto completa, la nota (`@pii`), el autor (persona de Atlas), la fecha (`TimeProvider`) y la huella. Un trigger rechaza todo `UPDATE`; purgar sigue siendo una decisión explícita, sin endpoint |

- Sin `organizacion_id` ni RLS: las guías son internas hasta que un paquete (F4) las comparta; el portal leerá la **revisión publicada**, nunca la última.
- La huella es el SHA-256 de la foto (sin autor, fecha ni nota): restaurar la N da la misma huella que la N; guardar o restaurar sin cambios es 409 `sin_cambios`.
- El plan 00 §8.4 traía `slug` único y guías transversales (sin contrato). No van en esta HU: la guía se identifica por id y siempre es de un contrato **[por verificar con el líder si hacen falta guías transversales]**.
- Las versiones se guardan como semver y el filtro por versión usa la precedencia de SemVer 2.0 (`VersionSemantica.Comparar`). La operación, si se da, debe existir en el índice de alguna versión del contrato.

### API (`/api/v1/contratos`)

| Método y ruta | Permiso |
|---|---|
| `GET /{id}/guias?version=` | `ver_internos` |
| `POST /{id}/guias` | `guias.editar` sobre el aplicativo |
| `GET /guias/{guiaId}` | `ver_internos` |
| `PUT /guias/{guiaId}` (con `revisionBase`) | `guias.editar` sobre el aplicativo |
| `GET /guias/{guiaId}/revisiones` · `GET /guias/{guiaId}/revisiones/{numero}` | `ver_internos` |
| `POST /guias/{guiaId}/revisiones/{numero}/restauracion` | `guias.editar` sobre el aplicativo |
| `POST /guias/{guiaId}/publicacion` | `guias.editar`; si es compartible, solo el responsable |

- **Editar** = `atlas.contratos.guias.editar` global (líder técnico) o con alcance al aplicativo del contrato o a uno de sus ancestros (el rol implícito del responsable, como en el catálogo). La política del endpoint exige el permiso en algún alcance; el alcance lo revisa el endpoint (403 `sin_permiso`), siempre después del 404, para no revelar si algo existe.
- **Aprobar** lo compartible = tener `atlas.responsable` con alcance al aplicativo o a un ancestro. Ni el líder técnico ni el superadministrador lo hacen sin ser principal o secundario (§7.2 al pie de la letra) **[por verificar con el líder]**: si un aplicativo no tiene responsables, sus guías compartibles no se pueden publicar hasta asignarlos.
- Guardar o restaurar deja la guía en `borrador` sin tocar la revisión publicada: un cambio de visibilidad a compartible o un texto nuevo no llega a clientes sin una aprobación nueva.
- Concurrencia optimista: `revisionBase` (guardar, restaurar) y `revision` (publicar) deben ser la última; si no, 409 `revision_desactualizada`. La unicidad `(guia, numero)` cubre dos guardados simultáneos.
- Auditoría en `plataforma_auditoria`, en la misma transacción que el cambio: `contratos.guia_creada`, `.guia_revisada`, `.guia_restaurada` y `.guia_publicada` (con `aprobacion` si es compartible), con ids, números, visibilidad y huella; nunca el título, el cuerpo ni la nota.
- Las respuestas traen `puedeEditar` y `puedeAprobar`, calculados en el servidor: la PWA no conoce los alcances.

### PWA

- `GuiasDelContrato` (`apps/web/src/modulos/contratos/guias/GuiasDelContrato.tsx`, export por defecto, `{ contratoId, version?, guiaId? }`): lista, lectura, editor con vista previa (en el móvil se alterna, desde `lg` lado a lado), historial, comparación, restauración y publicación. La pestaña «Guías» de la pantalla del contrato (HU 3.6) lo importa de forma perezosa; la ruta `/contratos/:contratoId/guias/:guiaId` es el enlace directo.
- **Render**: marked 18.1.0 (MIT) con el renderizador de HTML reemplazado por el texto escapado (el HTML del autor se ve como texto) y DOMPurify 3.4.16 (MPL-2.0 o Apache-2.0) con lista blanca de etiquetas de texto, listas, tablas, código y enlaces; sin `style`, `class`, `data-*` ni imágenes; enlaces solo `http(s)`, `mailto` o anclas, con `target="_blank"` y `rel="noopener noreferrer nofollow"`. Va en su propio chunk (≈ 25 KB gzip). Mermaid dentro de las guías (plan 00 §8.9) queda para cuando llegue Mermaid en modo estricto (F5).
- **Diff**: jsdiff 9.0.0 (BSD-3-Clause, `diffLines`) en el cliente, en su propio chunk (≈ 4 KB gzip), sobre los dos cuerpos que da `GET /revisiones/{numero}`; además lista los otros campos que cambiaron. Las revisiones son inmutables, así que la PWA las cachea sin vencimiento.
- El editor va en su propio chunk (≈ 2 KB gzip). El bundle inicial no cambia.

## Consecuencias

### Positivas

- La historia no se reescribe y la base lo garantiza; restaurar es copiar una foto y la huella lo prueba.
- Lo que verán los clientes (la revisión publicada) siempre pasó por el responsable, aunque otros editen después.
- Ningún HTML del autor llega al DOM, sin tocar la CSP.
- Comparar no cuesta nada al servidor ni agrega dependencias .NET.

### Negativas

- Cada revisión guarda el cuerpo completo (hasta 200 mil caracteres); con muchas ediciones, la tabla crece. Es texto pequeño frente a los specs.
- Un cliente que no sea la PWA (MCP, F7) tiene que hacer su propio diff; si hace falta, se agrega `GET /comparacion` en el servidor.
- Sin responsables asignados nadie publica lo compartible.
- Marked y DOMPurify son dos dependencias nuevas que hay que mantener al día (Renovate).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Solo el cuerpo en la revisión | Restaurar no devolvería la visibilidad ni las versiones; comparar «igual a la N» dejaría de ser una huella |
| Flujo aparte de aprobación | Más estados y pantallas para un equipo pequeño; F5 lo trae para documentos y las guías pueden sumarse después |
| react-markdown | Más pesado (unified, remark, rehype) para el mismo resultado; igual habría que filtrar enlaces |
| Render en el servidor | La vista previa del editor necesitaría una ida y vuelta por tecla o un segundo render en el cliente |
| Diff en el servidor | Otra dependencia y otro endpoint para algo que el cliente hace con los datos que ya tiene |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Contratos/Guias/`, `apps/web/src/modulos/contratos/guias/`.
- Pendientes **[por verificar]**: guías transversales y `slug`; si el líder técnico o el superadministrador pueden aprobar lo compartible cuando un aplicativo no tiene responsables; si el responsable que edita puede aprobar su propio cambio o hace falta otra persona; Mermaid en las guías; cómo entra una guía a un paquete (`guiaId@revision`, F4).
