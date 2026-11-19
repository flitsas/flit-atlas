---
id: ATLAS-ADR-0041
titulo: Paquetes de contratos — revisiones inmutables con huella, revisión vigente que se publica o se fija, reglas revisadas al guardar y al publicar, rol implícito sobre todos sus contratos y visibilidad declarada en el spec
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7.1, §7.2 (rol implícito por propiedad), §7.4, §8.4, §8.9, §9, §11 y §12, plan 00 §6, §8.1, §8.2 reglas 3 y 6, §8.4, §8.5 y §8.9, HU 4.1, ATLAS-ADR-0023, ATLAS-ADR-0026, ATLAS-ADR-0031, ATLAS-ADR-0035, ATLAS-ADR-0039, ATLAS-ADR-0040]
---

# ATLAS-ADR-0041 · Paquetes de contratos

## Contexto y problema

La HU 4.1 pide paquetes con revisiones: un asistente de 3 pasos (contratos y versiones estables; operaciones con filtro por etiqueta o `x-public`; guías), publicar y fijar una revisión anterior, todo auditado. Un paquete es la selección curada que después se comparte con organizaciones (HU 4.3), se poda (HU 4.2, ATLAS-ADR-0038) y se exporta (HU 4.5). Esta HU solo guarda la selección: no poda ni comparte.

Hechos verificados en el código:

- El módulo de contratos (ATLAS-ADR-0035) tiene `contratos_contrato` (con `visibilidad` `interna | compartible`), `contratos_version` (inmutable, `es_estable`, `estado` `vigente | deprecada | retirada`), el índice `contratos_operacion` (con `etiquetas` y `publica`) y las guías con revisiones inmutables y revisión publicada aprobada por el responsable (ATLAS-ADR-0040). El prefijo de tabla es `contratos_` (el plan decía `contrato_`).
- `atlas.paquetes.gestionar` ya existe en el manifiesto `portal` del núcleo (`ManifiestosNucleo.Portal`) y lo tienen el líder técnico y el rol implícito `atlas.responsable` (ATLAS-ADR-0031). No lleva el prefijo del módulo, así que el módulo no lo puede declarar (ATLAS-ADR-0035): solo lo exige.
- **Nada ponía un contrato en `compartible`**: la columna existía, pero la publicación no la tocaba. La CI ya elige las reglas de vacuum de lo compartible cuando el spec declara `info.x-flit-visibilidad: compartible` (ATLAS-ADR-0035 y ATLAS-ADR-0037), y el contrato de ICT ya la declara.
- No hay guías transversales: `contratos_guia.contrato_id` es obligatorio (ATLAS-ADR-0040).

## Factores de decisión

- «Volver en el tiempo» sin reescribir la historia, como las guías y el reparto (plan 00 §8.2, regla 6).
- Que un cliente nunca reciba algo que hoy no se pueda compartir, aunque valiera cuando se guardó.
- Que el responsable arme paquetes **de sus contratos** (§7.2) y no pueda tocar los de otros.
- Que una compartición (HU 4.3) pueda fijar una revisión sin cambios en el modelo.
- Git es la fuente de verdad de lo que vive en el código; Atlas no edita contratos (§2, §8.4).
- Bundle inicial < 200 KB gzip; móvil primero.

## Opciones consideradas

### Revisiones y vigente

1. **Foto completa por revisión** (nombre, descripción y definición en `jsonb`, normalizada y con huella SHA-256) y, en el paquete, `revision_actual` y `revision_publicada`; publicar o fijar solo mueve `revision_publicada`.
2. Fijar copiando la revisión vieja a una nueva (como «restaurar» en las guías).
3. Tablas relacionales por contrato, operación y guía de cada revisión.

### Quién arma un paquete

1. **Gestor global o responsable de los aplicativos de todos sus contratos** (antes y después de cada cambio).
2. Responsable de al menos uno de sus contratos.
3. Solo el líder técnico.

### Cómo llega un contrato a `compartible`

1. **Lo declara el spec** (`info.x-flit-visibilidad`), y la sigue la última versión **estable** publicada.
2. Un endpoint de Atlas para cambiar la visibilidad.

### Lectura interna de los paquetes

1. **`atlas.contratos.ver_internos`** (líder técnico, desarrollo y soporte), como los contratos y las guías.
2. Solo quien tiene `atlas.paquetes.gestionar`.
3. Un permiso nuevo `atlas.paquetes.ver`.

## Decisión

Opción 1 en las cuatro.

### Datos (`contratos_*`, migración `Paquetes`)

| Tabla | Para qué |
|---|---|
| `contratos_paquete` | Nombre y descripción (foto de la última revisión), `estado` (`borrador` nunca publicado, `publicado`, `archivado`), `revision_actual`, `revision_publicada` (la **vigente**), quién y cuándo la publicó o fijó, quién lo creó y quién y cuándo lo archivó. `CHECK`: la vigente existe (entre 1 y la actual), `publicado` tiene vigente, `borrador` no, y `archivado` lleva fecha |
| `contratos_paquete_revision` | Una fila **inmutable** por guardado: número único por paquete, nombre, descripción, `definicion` `jsonb` (`[{contratoId, versionId, operaciones:[clave…], guias:[{guiaId, revision}]}]`, `CHECK` de arreglo), nota (`@pii`), autor, fecha (`TimeProvider`) y huella. Un trigger rechaza todo `UPDATE` |

- La definición se **normaliza** antes de guardarla: contratos ordenados por id, operaciones sin repetir y ordenadas, guías ordenadas. La huella (SHA-256 del nombre, la descripción y la definición, sin autor, fecha ni nota) detecta «sin cambios» aunque llegue en otro orden.
- Sin `organizacion_id` ni RLS: el paquete es interno. La unicidad `(paquete_id, numero)` es la llave a la que apuntará `contratos_comparticion.revision_fijada` en la HU 4.3 (vacío = sigue la vigente), así que compartir no cambia este modelo.
- `contratos_comparticion` **no** se crea aquí (HU 4.3).

### Reglas

Al **guardar** (400 por campo, `definicion[i].…`):

- Hasta 50 contratos, uno por contrato (una sola versión fijada), hasta 2.000 operaciones y 50 guías por contrato.
- El contrato es `compartible`; la versión es de ese contrato, **estable** (sin prerelease) y no está `retirada` (una `deprecada` sí entra: el portal avisará, HU 4.8).
- Cada operación existe en el índice de **esa** versión.
- Cada guía es **del mismo contrato** (no hay transversales), es `compartible`, tiene revisión publicada (aprobada por el responsable, ATLAS-ADR-0040), y la revisión que entra es **la publicada** (sin número, el servidor la completa); además esa revisión es compartible y aplica a la versión elegida.
- Un borrador puede ir vacío o sin operaciones.

Al **publicar** (la última revisión) o **fijar** (una anterior a la última) se revisan otra vez las reglas sobre la revisión que queda vigente —lo que valía al guardar pudo cambiar: una versión retirada, un contrato o una guía que volvió a interna— más **al menos un contrato con una operación**. Si algo falla, 409 `no_publicable` con el detalle en frases. Para una guía basta con que siga compartible y publicada y que su revisión guardada siga siendo compartible: la que se aprobó cuando se guardó el paquete sigue valiendo.

- Fijar no crea revisiones ni borra las nuevas: el paquete queda con «cambios sin publicar» y se puede publicar la última cuando se quiera. Fijar la última es 409 `es_la_ultima` (se publica); fijar la vigente, 409 `ya_vigente`.
- Archivar deja el paquete sin revisiones ni publicaciones nuevas (409 `archivado`); la historia queda. Desarchivar no está en esta HU.
- Concurrencia optimista: `revisionBase` (guardar) y `revision` (publicar) deben ser la última, si no 409 `revision_desactualizada`; la unicidad `(paquete, numero)` cubre dos guardados a la vez.
- Auditoría en `plataforma_auditoria`, en la misma transacción: `contratos.paquete_creado`, `.paquete_revisado`, `.paquete_publicado`, `.paquete_fijado` (con la anterior) y `.paquete_archivado`, con ids de contratos y versiones, conteos y huella; nunca el nombre, la descripción ni la nota.

### Permisos

- **Armar** = `atlas.paquetes.gestionar` global (líder técnico) o con alcance al aplicativo de **cada** contrato del paquete o a un ancestro (el rol implícito del responsable, como en el catálogo y las guías). Se exige sobre los contratos de antes **y** de después de cada revisión (un responsable no quita ni agrega contratos ajenos) y sobre los de la última revisión y la que se fija. Un paquete sin contratos solo lo toca quien lo creó (o el gestor global). La política del endpoint exige el permiso en algún alcance; el alcance lo revisa el endpoint (403 `sin_permiso`), siempre después del 404.
- **Leer** = `atlas.contratos.ver_internos`: un paquete es una selección de contratos internos y no revela nada que quien lee contratos no vea ya; soporte necesita saber qué tiene cada cliente. Los externos nunca leen estos endpoints: verán lo compartido por el portal (HU 4.4). Un responsable sin `ver_internos` (hoy todos son desarrolladores) arma paquetes desde los endpoints de gestión, pero no ve la lista **[por verificar con el líder si hay responsables funcionales, P-03]**.
- La matriz rol × permiso no cambia: el permiso y sus concesiones ya estaban.

### Visibilidad del contrato

La publicación desde la CI lee `info.x-flit-visibilidad` (OpenAPI y AsyncAPI) y, **solo en una versión estable**, la copia al contrato (sin la marca, `interna`). Una prerelease de `develop` no cambia lo que puede ir en paquetes. Proto no la declara todavía (queda interno).

### API (`/api/v1/contratos/paquetes`)

| Método y ruta | Permiso |
|---|---|
| `GET /paquetes?estado=&contrato=` | `ver_internos` |
| `POST /paquetes` | `paquetes.gestionar` sobre sus contratos |
| `GET /paquetes/elegibles` (pasos 1 y 3: contratos compartibles de la persona, versiones estables no retiradas, guías compartibles publicadas) | `paquetes.gestionar` |
| `GET /paquetes/versiones/{versionId}/operaciones?etiqueta=&soloPublicas=` (paso 2: índice con `publica` y etiquetas) | `paquetes.gestionar` sobre el contrato |
| `GET /paquetes/{id}` (última revisión con nombres resueltos e historial) · `GET /paquetes/{id}/revisiones/{n}` | `ver_internos` |
| `POST /paquetes/{id}/revisiones` | `paquetes.gestionar` |
| `POST /paquetes/{id}/publicacion` · `POST /paquetes/{id}/revisiones/{n}/fijacion` · `POST /paquetes/{id}/archivo` | `paquetes.gestionar` |

Las respuestas traen `puedeCrear` y `puedeGestionar`, calculados en el servidor.

### PWA

- `/contratos/paquetes` (lista, «Más» en el móvil), `/contratos/paquetes/nuevo` y `/contratos/paquetes/<id>` (detalle con la última revisión, «Publicar la revisión N», «Editar (nueva revisión)», historial con «Ver» y «Fijar esta», y archivar con confirmación en una hoja inferior). La pestaña «Paquetes» de la pantalla del contrato lista los que lo incluyen.
- El asistente va en su propio chunk: 1. contratos (buscador) y versión en una **hoja inferior**; 2. operaciones con chips de etiqueta, «solo x-public», «Seleccionar todo» y «Ninguna» sobre lo que se ve (el filtro va al servidor); 3. guías que aplican a la versión; resumen con nombre, descripción y nota, y «Guardar revisión». Casillas y opciones de 44 px, tokens de color del shell, sin `<select>` (CSP de WebKit). El bundle inicial no cambia.

## Consecuencias

### Positivas

- La historia no se reescribe y la base lo garantiza; volver a una revisión es mover un número, y la huella prueba «sin cambios».
- Nunca queda vigente algo que hoy no se puede compartir.
- La HU 4.3 fija una revisión con una llave que ya existe; la HU 4.2 recibe la selección tal cual (`contratoId`, `versionId`, claves del índice).
- La visibilidad vive en git, con el mismo control que el spec y la misma marca que usa la CI.

### Negativas

- La definición en `jsonb` no tiene llaves foráneas: un contrato, una versión o una guía purgados quedan como referencias vacías (la API los muestra como «ya no existe» y publicar falla).
- Buscar «paquetes que usan un contrato» lee las definiciones en memoria; con cientos de paquetes habrá que indexar `jsonb` o agregar una tabla de apoyo.
- Un contrato que pasa a interno en una versión estable deja sus paquetes sin poder publicarse; lo ya vigente sigue apuntando a él hasta que el portal (HU 4.4) lo filtre **[por verificar en la HU 4.4]**.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Fijar copiando a una revisión nueva | Borra la diferencia entre «lo que se guardó» y «lo que está vigente», y el plan pide volver fijando, no reescribiendo |
| Tablas relacionales por revisión | Más tablas y escrituras para una foto que no cambia; la definición se lee entera |
| Responsable de al menos un contrato | Permitiría compartir contratos de otros aplicativos sin su responsable |
| Solo el líder técnico | Contradice el §7.2 (el responsable arma paquetes de sus contratos) |
| Endpoint para cambiar la visibilidad | Atlas no edita contratos; dos fuentes para la misma marca |
| Leer solo con `paquetes.gestionar` | Soporte y desarrollo no sabrían qué tiene cada cliente |
| Permiso nuevo `atlas.paquetes.ver` | Cambia el modelo de permisos sin una necesidad que no cubra `ver_internos` |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Contratos/Paquetes/`, `apps/web/src/modulos/contratos/paquetes/`.
- Pendientes **[por verificar]**: guías transversales (ATLAS-ADR-0040); responsables sin `ver_internos`; desarchivar; si un contrato que vuelve a interno debe retirar lo ya vigente (HU 4.4); visibilidad en proto; tiempo real del paquete abierto (SignalR, §8.9).
