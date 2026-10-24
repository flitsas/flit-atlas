---
id: ATLAS-ADR-0038
titulo: Poda de specs — recorrido genérico del árbol JSON para OpenAPI 3.0/3.1 y AsyncAPI 2.x/3.x, cierre transitivo por componentes, fail-closed con verificación final y pruebas de propiedades sobre specs reales
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §5.2, §7.3, §8.4 y §15, plan 00 §6.2 regla 3 y §8.6, HU 4.2, ATLAS-ADR-0035, ATLAS-ADR-0037, ATLAS-ADR-0039]
---

# ATLAS-ADR-0038 · Poda de specs

## Contexto y problema

El plan 00 (§6.2, regla 3) exige que todo spec que llega a un cliente —para verlo en el portal, exportarlo o llevarlo a Microcks— salga **podado**: solo las operaciones del paquete y lo que ellas referencian, sin nada interno y con los servidores públicos del ambiente. Es el núcleo de seguridad del portal (§8.6): un error aquí filtra rutas, schemas o hosts internos a otra organización. La HU 4.2 pide el podador de OpenAPI y el de AsyncAPI, con pruebas de propiedades sobre specs reales como criterio de aceptación.

Hechos verificados el 24/10/2026:

- Los specs se guardan tal como llegan (`contratos_version.contenido`, ATLAS-ADR-0035) y el índice `contratos_operacion` lo arma `AnalizadorSpec`: `GET /ruta`, `POST webhook:nombre`, `publish canal` / `subscribe canal` (AsyncAPI 2), `send canal` / `receive canal` (AsyncAPI 3) y, si una clave se repite, `clave (operationId)` o `clave #n`.
- Specs que hay que soportar: `atlas.v1.yaml` (OpenAPI 3.1.1, 470 KB, generado por el código), `core-ict.enriquecido.v1.yaml` (3.1.0, con `x-public`, `x-internal`, `x-flit-obligatoriedad` y `x-enum-descriptions`, ATLAS-ADR-0037), `external-api.v1.json` (3.0.3, JSON, `security` global) y `core-api.v1.yaml` (3.0.3, 871 KB) de FLIT 2, `atlas-events.v1.yaml` (AsyncAPI 3.0) y los AsyncAPI 2.6 de FLIT 2, varios con `$ref` a `./comun/sobre.v1.yaml` (otro archivo).
- `Microsoft.OpenApi` 2.12.2 ya está en el repo (lo usa `AnalizadorSpec`) y lee 3.0 y 3.1. Para AsyncAPI no hay una librería madura y libre en .NET (ATLAS-ADR-0035).
- `atlas.v1.yaml` no pasa hoy la validación de `Microsoft.OpenApi` (dos rutas con la misma firma, `/api/v1/catalogo/aplicativos/{id}` y `{codigo}`): la poda no lo arregla ni lo empeora.

## Factores de decisión

- **Fidelidad**: el resultado conserva todo lo que el spec trae salvo lo interno (ejemplos, las extensiones `x-*` de la lista blanca, orden de las claves, `nullable` de 3.0, `$ref` con hermanos de 3.1) y sale en el mismo formato (JSON o YAML).
- **Fail-closed**: ante una referencia externa o rota, una clave desconocida o una operación interna, error explícito; nunca el spec completo ni uno a medio podar.
- Un solo motor para OpenAPI y AsyncAPI, fácil de probar con propiedades.
- Que la selección use las mismas claves de `contratos_operacion` (las que guarda el paquete, HU 4.1).

## Opciones consideradas

1. **Recorrido genérico del árbol JSON** (`System.Text.Json.Nodes`) para OpenAPI y AsyncAPI, con conocimiento estructural mínimo de cada formato (dónde están las operaciones, los servidores y las etiquetas).
2. **`Microsoft.OpenApi` 2.x** para OpenAPI (cargar el modelo, quitar rutas, recolectar referencias, serializar) y recorrido genérico solo para AsyncAPI.
3. Delegar la poda a un servicio Go o Python (`atlas-render` o `atlas-docs`) con una librería de esos ecosistemas.

## Decisión

Opción elegida: **1**, en `services/api/src/Flit.Atlas.Modulos.Contratos/Poda/`.

**Interfaz común** `IPodadorSpec` (`PodadorOpenApi`, `PodadorAsyncApi`; `Podadores.Para(tipo)`): `Operaciones(contenido)` devuelve las claves seleccionables con su marca de interna, y `Podar(SolicitudPoda)` recibe el spec completo, las claves y el destino (`sandbox`, `qa` o `pdn` con sus URL públicas) y devuelve `SpecPodado`: el texto podado en el mismo formato y un `ResumenPoda` (operaciones, componentes conservados y eliminados, etiquetas quitadas, nodos internos quitados, servidores y advertencias). Todo error es `PodaException` con un motivo y un código estable (`spec_invalida`, `seleccion_vacia`, `claves_desconocidas`, `operacion_interna`, `referencia_externa`, `referencia_rota`, `destino_invalido`, `verificacion_fallida`) y sus detalles (las claves o referencias culpables).

**Lectura y escritura** (`TextoSpec`): JSON con `System.Text.Json` (los números conservan su texto); YAML con SharpYaml, el mismo lector de `Microsoft.OpenApi.YamlReader`, resolviendo los escalares sin comillas con el esquema core de YAML 1.2. Al escribir YAML, una cadena va sin comillas solo si ningún lector (YAML 1.1 ni 1.2: `yes`, `on`, fechas, octales, `1:20`…) la puede leer como otro tipo; si no, entre comillas dobles o en bloque literal. Topes de tamaño (el de publicación, 5 MiB), profundidad (128) y nodos (2 millones, contra alias que se multiplican); se rechazan las llaves repetidas y las de mezcla (`<<`).

**Pasos de la poda**, sobre una copia del árbol:

1. **Selección.** Se arma el índice con la misma regla de claves que `AnalizadorSpec` (la lógica de claves únicas se movió a `AnalizadorSpec.ClavesUnicas` y la usan los dos). Selección vacía, una clave que no está en el spec o una operación marcada `x-internal` (ella, su ruta o su canal) son error. En AsyncAPI también se acepta `clave (operationId)` para una operación cuya clave dejó de repetirse, así podar lo podado da lo mismo.
2. **Operaciones.** OpenAPI: solo los métodos elegidos de `paths` y de `webhooks` (3.1; en 3.0 `webhooks` no existe y se quita con advertencia). Una ruta que es `$ref` a `components/pathItems` se copia en línea antes de podarla. AsyncAPI 2: solo los canales con `publish`/`subscribe` elegidos y solo esas acciones. AsyncAPI 3: solo las `operations` elegidas; los canales pasan a ser unidades del cierre y, de un canal que usan las operaciones elegidas, solo quedan los mensajes que algo fuera de `channels` nombra (todos, si alguna operación no limita sus `messages`).
3. **Servidores.** Los `servers` de la raíz se reemplazan en su mismo lugar por las URL públicas del destino (OpenAPI: `url` y `description`; AsyncAPI 3: `host`, `protocol` y `pathname`; AsyncAPI 2: `url` y `protocol`; el protocolo es el esquema de la URL). **Sin URL pública configurada no hay `servers`, nunca los internos.** Se quitan los `servers` de rutas, operaciones y canales y el `server` de los links. En AsyncAPI, si todos los servidores originales declaran la misma `security`, los públicos la heredan; si difieren, quedan sin ella y se avisa. Las URL deben ser absolutas, con host DNS público (ni IP, ni `localhost`, ni `.svc`, `.local`, `.internal`…), sin credenciales, consulta ni fragmento; en OpenAPI, solo `https`.
4. **Lo interno.** Se quita todo nodo con `x-internal: true` o `x-interno: true` (operaciones, parámetros, propiedades, ejemplos, componentes…); si era una propiedad de un schema, también sale de su `required`. Después se aplica la **lista blanca de extensiones** (`ExtensionesPoda`, hallazgo F4-SEC-09 de la revisión de seguridad): toda `x-*` que no esté en ella se quita en cualquier nivel, incluidos `info`, `paths`, `webhooks`, `channels`, `responses` y `components/<tipo>` (ver «Extensiones que salen»). Un link a una operación que no quedó se quita (revelaría su id o su ruta).
5. **Cierre transitivo.** Desde todo lo que no es `components` (ni `channels` en AsyncAPI 3) se siguen todas las `$ref`, los nombres de `discriminator.mapping` y los `securitySchemes` nombrados en `security` (global, de operación o de servidor). La unidad es `components/<tipo>/<nombre>` (schemas, parameters, responses, requestBodies, headers, examples, links, callbacks, pathItems, securitySchemes, messages, operationTraits…), así una referencia a un subnodo conserva su componente. Una `$ref` alcanzable que no es local es `referencia_externa`; una que no resuelve en el spec podado (porque no existe, era interna o apuntaba a una operación no compartida) es `referencia_rota`; un `security` que nombra un esquema interno, también. Todo componente no alcanzado se elimina, y luego las etiquetas raíz que ninguna operación, canal o mensaje usa (en AsyncAPI 3 un segundo cierre limpia los `components/tags` que quedan huérfanos).
6. **Verificación final**, independiente de los pasos anteriores: toda `$ref` del resultado es local y resuelve, no queda ningún nodo interno ni ninguna extensión fuera de la lista blanca o sin su forma saneada (falla cerrado), y el índice del resultado tiene exactamente las operaciones elegidas. Si algo falla es `verificacion_fallida` y no hay resultado.

**Extensiones que salen** (lista blanca; todas las demás se quitan). Una clave `x-…` solo es extensión donde el objeto es de la especificación: en los mapas de nombres del autor (`properties`, cabeceras de OpenAPI, `content`, `variables`, `mapping`…) es un nombre (la cabecera `x-request-id` se conserva) y dentro de los datos (`example`, `examples` de JSON Schema, `default`, `enum`, `const`, `value`) no se mira. Ante la duda se trata como extensión y se quita.

| Extensión | Para qué la usa el portal o la exportación | Qué sale |
|---|---|---|
| `x-enum-descriptions` | Descripción de cada valor de un `enum` (tablas de referencia) | Lista de cadenas o mapa valor → cadena |
| `x-flit-obligatoriedad` | Matriz de obligatoriedad por tipo de trámite (HU 3.9) | Solo `propiedad`, `tipos[{clave, nombre, valor}]` y las filas de `campos` o `documentos` (campo, valor, nombre, condición y una celda por tipo). Se quitan `discriminador` (nota de diseño interna: «comparte tabla con otros trámites») y `columna_excel` (encabezados del Excel de origen) |
| `x-flit-tabla` | Título y filas de una tabla de referencia | `titulo`, `columnas` y `filas` (solo las columnas declaradas); sin `hoja` |
| `x-flit-decimales` | Restricción «N decimales» de un número | Entero de 0 a 18 |
| `x-public` | Marca «pública» de la operación en el visor | Booleano |

**Lo que se quita** (no exhaustivo): `x-flit-fuente`, `x-flit-origen`, `x-flit-hoja-excel`, `info.x-flit-changelog`, `x-flit-ambiente` y `x-flit-ambientes` (los servidores ya son los del destino), `x-flit-visibilidad` (gobierno de Atlas, no del cliente), `x-flit-sin-autenticacion`, `x-internal`/`x-interno` en falso, `x-atlas-*`, `x-pii` y cualquier extensión de terceros o escrita en mayúsculas (`X-…`), en cualquier nivel.

**Pruebas** (criterio de aceptación), en `Flit.Atlas.Modulos.Contratos.Tests/Poda/`, sin librerías nuevas: generadores propios con semilla fija (4202 combinada con la huella FNV-1a de la ruta del spec, que sale en cada mensaje de fallo). Por spec, 40 iteraciones (12 en los de más de 400 KB): la selección completa, una sola operación, todas las de una etiqueta y subconjuntos al azar, con ambiente y 0 a 3 URL públicas al azar. Specs: `atlas.v1.yaml`, `core-ict.enriquecido.v1.yaml`, `atlas-events.v1.yaml` y copias de FLIT 2 en `Poda/Specs/` (`external-api.v1.json`, `core-api.v1.yaml` y `domain-events.v1.yaml`; `tramites-events.v1.yaml` sirve para el caso de referencia a otro archivo). Las copias se revisaron: sin secretos, IP ni datos personales; en `core-api` se reemplazaron dos dominios de ejemplo por `ejemplo.com`. Propiedades, con un oráculo escrito aparte del podador: ninguna operación fuera de la selección (índice de `AnalizadorSpec`, que lee con otro lector y no debe encontrar errores nuevos), ninguna `$ref` rota, ningún componente huérfano, ningún `security` sin su esquema, ningún nodo interno ni extensión fuera de la lista blanca (el oráculo tiene su propia copia de la lista), servidores solo los públicos dados, idempotencia (podar lo podado con la misma selección da el mismo texto), el resultado es subconjunto estructural del original (salvo `servers`) y la selección completa conserva todas las operaciones compartibles. Además, el índice del podador coincide con el de `contratos_operacion` en cada spec, casos puntuales de cada regla y de cada error, el ida y vuelta de YAML con cadenas engañosas y una prueba de que el oráculo sí detecta fallas.

**Sin endpoint nuevo** en esta HU: el portal y la vista previa del asistente de paquetes llegan en la ola 2 (HU 4.1 y 4.4) y usarán `Podadores` directamente.

## Consecuencias

### Positivas

- El resultado es el mismo árbol del spec menos lo quitado: nada se normaliza, se reescribe ni se pierde (lo comprueba la propiedad de subconjunto).
- Un solo motor y un solo oráculo para los cuatro formatos; las pruebas corren en unos 12 s sobre specs reales, sin red ni base.
- Fail-closed en tres capas: validación de la selección y del destino, errores del cierre transitivo y verificación final del resultado.
- La misma regla de claves que el índice, en un solo lugar.

### Negativas

- El podador conoce a mano la estructura mínima de cada formato (dónde están operaciones, servidores, etiquetas y links); una versión nueva (OpenAPI 3.2, AsyncAPI 4) se rechaza hasta revisarla.
- El recorrido es conservador: toda clave `$ref` cuenta como referencia (también dentro de un ejemplo) y todo arreglo `servers` de objetos con `url` fuera de la raíz se quita; en casos raros eso puede conservar un componente de más o dar un error que obliga a corregir el spec, nunca filtrar algo.
- Los specs que referencian otros archivos (los AsyncAPI 2.6 de FLIT 2 con `./comun/sobre.v1.yaml`) no se pueden compartir hasta que se publiquen con las referencias resueltas.
- Las cadenas YAML se reescriben con el estilo de Atlas (comillas donde haga falta, bloques literales), no con el del autor.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. `Microsoft.OpenApi` 2.x para OpenAPI | Su modelo no es un ida y vuelta fiel: serializa en la versión que se le pida (3.0 o 3.1, reescribiendo `nullable`, `example` y los tipos), reordena y normaliza campos y descarta lo que no modela. Comprobar que no se perdió `x-flit-obligatoriedad` ni un ejemplo exige un recorrido genérico de todas formas, y AsyncAPI necesitaría otro motor y otro oráculo. Se sigue usando para validar (en `AnalizadorSpec` y en el oráculo de las pruebas). |
| 3. Poda en Go o Python | Agrega una llamada de red y un punto de falla al camino de cada vista del portal; la poda es CPU pura de milisegundos (unos 200 ms para `core-api`, de 871 KB) y debe vivir junto a los permisos, en `atlas-api`. `atlas-docs` recibirá el spec ya podado para exportar (HU 4.5). |

## Más información

- [ATLAS-ADR-0035](0035-publicacion-de-contratos.md): versiones inmutables e índice `contratos_operacion`.
- [ATLAS-ADR-0037](0037-enriquecimiento-contrato-ict-flit2.md): `x-public` y `x-internal` del contrato de ICT.
- [ATLAS-ADR-0039](0039-vista-de-contratos.md): vista de contratos; el portal mostrará el spec podado.
- 24/10/2026: la lista negra inicial (`x-flit-fuente`, `x-flit-ambientes`) dejaba salir `x-flit-origen` con archivos y clases de core-ict, `x-flit-hoja-excel`, `x-flit-tabla.hoja` e `info.x-flit-changelog` (F4-SEC-09, `docs/seguridad/revision-f4.md`); se cambió a lista blanca. Una extensión nueva que el portal necesite se agrega a `ExtensionesPoda.Permitidas` **con su saneo**, a la copia del oráculo (`OraculoPoda`) y a esta lista.
- **[por verificar]** Las descripciones del contrato del ICT (no son extensiones, la poda no las toca) aún nombran piezas internas: `info.description` (`services/core-ict`, la HU de origen, `python -m atlas_docs.ict`, rutas de `contracts/` del repo `flit`), la del webhook (`Flit.Ict.Infrastructure/Jobs/WebhookNotificationJob.cs`) y «Esquema `X` de core-ict». Se corrigen en el generador (`atlas_docs.ict`) y en el contrato de `flit`, no en la poda.
- **[por verificar]** Las URL públicas por ambiente y contrato (sandbox, qa, pdn) se configurarán con los paquetes y el sandbox (HU 4.1, 4.7); el ICT aún tiene hosts por confirmar (ATLAS-ADR-0037).
