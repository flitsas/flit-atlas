---
id: ATLAS-ADR-0028
titulo: Catálogo de aplicativos — jerarquía en una tabla con padre, permiso de edición por aplicativo, semilla idempotente en el migrador y ficha con pestañas que aportan los módulos
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7.2, §7.4, §8.1, §8.9 y §9, plan 00 §2.1 y §7, insumos/Responsables_Productos_FLIT_2026-10-20.md, HU 2.1, ATLAS-ADR-0016, ATLAS-ADR-0023, ATLAS-ADR-0026]
---

# ATLAS-ADR-0028 · Catálogo de aplicativos

## Contexto y problema

La HU 2.1 pide el catálogo: jerarquía producto → módulo → servicio/componente (repo, ruta en el repo, tecnología, ambientes, criticidad, datos personales, estado `planeado | activo | en_retiro | retirado`, etiquetas), CRUD con permisos, la **ficha del aplicativo** como estructura con pestañas que llenan los demás módulos, y una semilla idempotente con el documento de responsables y el inventario del plan 00 §2.1. CA: la semilla se corre dos veces sin duplicar; búsqueda y filtros por grupo, estado y criticidad.

Es el primer módulo funcional con datos (ATLAS-ADR-0026 dejó el ejemplo «hola»). Hechos verificados antes de decidir:

- `atlas.catalogo.ver` y `atlas.catalogo.gestionar` ya estaban en `ManifiestosNucleo` (lista base del §7.4, ATLAS-ADR-0023). Un módulo debe declarar un manifiesto con su mismo id (`ExtensionesModulos.Validar`) y `CatalogoPermisos` rechaza dos manifiestos con el mismo id: la guía de módulos («declara en tu manifiesto solo los nuevos») no se podía cumplir tal cual.
- El rol implícito `atlas.responsable` (alcance `aplicativo:<id>`) existe, pero sus responsables los llena la HU 2.3 con `IProveedorRolesImplicitos`.
- El migrador ya aplica banderas y permisos de forma idempotente; no había un punto para datos iniciales de un módulo.
- `IProveedorBusqueda` existe, pero aún no hay endpoint que combine los proveedores ni búsqueda en el servidor desde la PWA.
- `ModuloAtlas` (PWA) no tenía cómo aportar contenido a la página de otro módulo, y un módulo no puede importar a otro (`arquitectura-modulos.test.ts`).
- El plan 00 §7 propone `catalogo_aplicativo` con `responsable_tecnico` y `responsable_funcional` (correos), `catalogo_organizacion` y `catalogo_auditoria`.

## Factores de decisión

- La ficha es la página a la que enlazan todos los módulos: debe crecer sin que el catálogo dependa de ellos.
- El responsable de un aplicativo edita lo suyo (§7.2, rol implícito) sin poder rehacer la jerarquía.
- La semilla corre en cada despliegue y no puede pisar lo que se edita en Atlas ni revivir lo eliminado.
- Nada de personas en el catálogo: los responsables son la HU 2.3 (minimización, Ley 1581).
- Cambios mínimos en el núcleo; los demás módulos de F2 se hacen en paralelo.

## Opciones consideradas

1. **Una tabla `catalogo_aplicativo` con `tipo` y `padre_id`**, semilla en el migrador con registro de lo aplicado, permiso `editar` por aplicativo y pestañas de la ficha declaradas en el manifiesto de cada módulo.
2. Tres tablas (producto, módulo, servicio) con llaves entre ellas.
3. Semilla al arrancar atlas-api (servicio en segundo plano) o un script SQL aparte.
4. Semilla que actualiza las filas existentes con los datos del archivo (el archivo como fuente de verdad).
5. Pestañas de la ficha registradas por cada módulo en el backend y servidas al frontend.

## Decisión

Opción elegida: **1**.

### Modelo (`catalogo_*` en el esquema `atlas`)

- `catalogo_aplicativo`: `slug` (clave natural única, `^[a-z0-9]+(-[a-z0-9]+)*$`, es la URL de la ficha), `nombre`, `tipo` (`producto | modulo | servicio`), `padre_id` (llave a la misma tabla, `ON DELETE RESTRICT`), `grupo` (`flit2 | flito | satelite | legado | infraestructura`, del plan 00 §2.1), `descripcion`, `estado`, `criticidad` (`alta | media | baja`), `maneja_datos_personales`, `repo` (`dueño/nombre`), `ruta_en_repo`, `tecnologias`, `ambientes` (`dev`, `qa`, `pdn`) y `etiquetas` (`text[]`), `creado_en` y `actualizado_en`. Los enums se guardan como texto snake_case (`ConvertidorEnumSnake`).
- Reglas de la jerarquía: un producto no tiene padre; un módulo cuelga de un producto; un servicio, de un producto o de un módulo (un servicio no tiene hijos). La base garantiza «producto ⇔ sin padre» y el formato del slug con `CHECK`; la API valida el resto, también al cambiar el tipo (los hijos deben seguir siendo válidos). Con tres niveles fijos no hay ciclos posibles.
- El grupo es del producto: módulos y servicios lo heredan al crearse y lo siguen cuando cambia.
- **Sin `organizacion_id` ni RLS**: el catálogo es de FLIT y `atlas.catalogo.ver` no se concede a externos; lo que se comparta con clientes irá por paquetes y documentos (F4). El contexto aplica igual `AplicarPredicadoDeOrganizacion` para que una tabla futura con organización quede filtrada.
- **Sin responsables en el catálogo** (a diferencia del plan 00 §7): los modela la HU 2.3 en `equipo_*`. Tampoco `catalogo_organizacion` (ya existe `plataforma_organizacion`) ni `catalogo_auditoria` (se usa `plataforma_auditoria`). `catalogo_fuente_publicacion` llega con la HU 3.1.
- `catalogo_semilla`: los slugs de la semilla ya aplicados, con la versión del archivo.

### Permisos

- El manifiesto `catalogo` **se muda** de `ManifiestosNucleo` a `PermisosCatalogo` en el módulo, con los mismos códigos y concesiones (`ver`: todos los empleados; `gestionar`: líder técnico) y el tipo de alcance `aplicativo`. Se agrega **`atlas.catalogo.editar`** (líder técnico y `atlas.responsable`): cambiar los datos de un aplicativo existente sin tocar la jerarquía. Es la regla para los demás módulos de la lista base (equipo, soporte…): al llegar, mudan su manifiesto; la guía de módulos lo dice ahora. **[por verificar con el líder]** las concesiones de `editar`.
- Por recurso, con `PermisosEfectivos.TieneEn`: editar exige `editar` (o `gestionar`) con alcance global o sobre el aplicativo **o uno de sus ancestros** (el responsable de un producto edita sus módulos y servicios). Cambiar slug, tipo, padre o grupo, crear un hijo y eliminar exigen `gestionar` sobre el aplicativo (y sobre el padre nuevo al moverlo); crear un producto, `gestionar` global. Un 403 de negocio trae `{ codigo: "requiere_permiso", mensaje }`. La ficha devuelve `permisos { editar, gestionar }` para que la PWA muestre solo lo que se puede.
- Eliminar solo si no tiene hijos (409 `tiene_hijos`); un slug repetido es 409 `slug_en_uso`.
- Cada cambio queda en `plataforma_auditoria` (`catalogo.aplicativo_creado | _actualizado | _eliminado`, recurso `catalogo.aplicativo`, con el slug y los **nombres** de los campos cambiados, nunca valores) en la **misma transacción**: el contexto de la plataforma usa la conexión y la transacción del catálogo.

### API (`/api/v1/catalogo`, etiqueta «Catálogo»)

- `GET /aplicativos?texto&grupo&estado&criticidad&tipo&padreId` (cada filtro admite varios valores, repetidos o separados por coma; máximo 500 resultados, ordenados por grupo y por la ruta en la jerarquía, con sus ancestros y su número de hijos). El texto se busca sin tildes ni mayúsculas con `translate` + `lower` (como la búsqueda de personas: `unaccent` aún no está verificada en las VPS) en nombre, slug, descripción y repo, y como etiqueta exacta.
- `GET /aplicativos/{slug}` (ficha), `POST /aplicativos`, `PUT /aplicativos/{id}` (reemplaza todos los campos) y `DELETE /aplicativos/{id}`.
- Búsqueda global: `BusquedaCatalogo` (`IProveedorBusqueda`, proveedor `catalogo`, tipos `producto | modulo | servicio`) queda registrado y probado, con la misma consulta y el permiso `ver`. **El endpoint que combina los proveedores sigue pendiente** (ATLAS-ADR-0026): la paleta de la PWA aún busca solo en la navegación.

### Semilla (`Semilla/aplicativos.json`, recurso incrustado)

- Contenido: los 9 productos y los 17 módulos de FLIT 2 del documento de responsables (versión 5), el producto FLIT 2.0 y sus servicios del plan 00 §2.1 (colgados del módulo cuando es evidente: `core-identity` → Identidad, `core-comparendos` y `frontend-comparendos` → Comparendos…; si no, del producto), FLITO y sus dos apps, los satélites (`flit-firmas`, `flit-scheduler`, `flit-certify`, `flit-infra-studio` con el Panel Vigía, `flit-egress`, `flitsas-maps`), Atlas y sus servicios, Infraestructura (`flit-gitops`) y el producto «FLIT V1 (legados)» con los **27 repos `Back*` y `FrontNextJS`** (lista del manual de migración de `flit-gitops`). Solo datos de la empresa: **ninguna persona ni correo** (una prueba lo verifica).
- **Idempotente por slug con registro de lo aplicado**: una entrada se aplica una sola vez. Si su slug ya existe (creado a mano) se marca aplicada sin tocarla; si ya se aplicó, no se vuelve a crear aunque se haya editado o eliminado; si su padre no existe (lo eliminaron), se omite y se reintenta. El archivo **no** es la fuente de verdad después de sembrar: lo es Atlas. Para corregir un dato sembrado se edita en Atlas; para agregar aplicativos al inventario inicial, una entrada nueva en el archivo.
- **La corre el migrador**, después de migrar el núcleo y todos los módulos, igual que las banderas y la matriz: nuevo método opcional `IModuloAtlas.SembrarAsync(IServiceProvider, CancellationToken)` (por defecto no hace nada) y `ExtensionesModulos.SembrarModulosAsync`, que llaman el migrador y el fixture de las pruebas. No al arrancar atlas-api: varias réplicas competirían y el arranque se haría más lento y frágil.
- Sentinel y People no aparecen en los planes: sus descripciones las dio el líder técnico (23/10/2026). Lo que sigue sin confirmar lleva la marca «[por verificar]» en la descripción (la ficha la resalta y el widget de Inicio la cuenta). Criticidad: `media` en todo; datos personales: solo donde es evidente (identidad, usuarios, comparendos, trámites…). Ambos los confirma el líder **[por verificar]**.

### Ficha del aplicativo (PWA)

- `/catalogo` (listado) y `/catalogo/<slug>` (ficha) dentro de una sola ruta del manifiesto, cada pantalla en su chunk. Filtros (grupo, estado, criticidad, tipo) como radios con forma de botón (sin `<select>`, por la CSP en WebKit) y guardados en la URL; resultados en tarjetas agrupadas por grupo; pestañas que se acomodan en varias líneas en el móvil (sin scroll horizontal a 360 px).
- **Punto de extensión**: `ModuloAtlas.pestanasFicha?: PestanaFicha[]` en el contrato del shell (`id`, `titulo`, `orden`, `permiso?`, `tipos?`, `cargar` diferido que recibe `{ aplicativo: { id, slug, nombre, tipo, grupo } }`) y `resolverPestanasFicha(modulos, acceso, tipo)` en el resolver (bandera, permiso y tipo; un id repetido lo gana el primer módulo registrado). El catálogo pinta «Resumen» y siete espacios con ids conocidos: `responsables`, `soporte`, `contratos`, `versiones`, `documentos`, `diagramas` e `ia`. Un módulo que declara una pestaña con uno de esos ids ocupa su espacio; con otro id, se agrega al final. Mientras nadie lo llena, el espacio dice qué llega y en qué fase; si el módulo existe pero la persona no tiene su permiso, el espacio no se muestra. La pestaña pide sus datos a su propio módulo: el catálogo no conoce a ninguno.

## Consecuencias

### Positivas

- Los demás módulos de F2 a F8 se cuelgan de la ficha con unas líneas en su manifiesto, sin tocar el catálogo ni importarlo.
- El responsable podrá editar su aplicativo en cuanto la HU 2.3 entregue el rol implícito, sin cambios aquí.
- Correr la semilla en cada despliegue es seguro: no duplica, no pisa, no revive.
- La auditoría y el cambio son atómicos.

### Negativas

- El archivo de la semilla y la base divergen con el tiempo (es lo buscado); el archivo solo sirve para ambientes nuevos.
- Mudar los manifiestos de la lista base cambia el orden de la matriz generada y toca `ManifiestosNucleo` en cada módulo que llega (conflictos de integración previsibles con soporte y equipo).
- El listado calcula la ruta de cada aplicativo cargando la jerarquía completa (unos cientos de filas): suficiente para el catálogo; si creciera mucho, se pasa a una consulta recursiva.
- Compartir la conexión entre dos contextos (`SetDbConnection`) es un detalle de EF Core que cualquier cambio en el registro del `PlataformaDbContext` debe respetar (lo cubre `ModuloCatalogoTests` de la API).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Tres tablas por nivel | Tres llaves distintas para los demás módulos (contratos, documentos, responsables) y consultas de ruta más largas, sin ganancia |
| Semilla al arrancar o en SQL suelto | Varias réplicas compiten; un SQL suelto no fija el alcance de RLS ni reutiliza la validación |
| Semilla que actualiza lo existente | Pisaría lo editado en Atlas en cada despliegue y reviviría lo eliminado |
| Pestañas registradas en el backend | El contenido de cada pestaña es de la PWA de cada módulo; el backend no tiene qué servir y obligaría a un endpoint más |

## Más información

- [`docs/como-crear-un-modulo.md`](../como-crear-un-modulo.md) (mudanza del manifiesto, `SembrarAsync` y `pestanasFicha`), [ATLAS-ADR-0016](0016-contrato-de-modulo.md), [ATLAS-ADR-0023](0023-modelo-de-roles-y-permisos.md), [ATLAS-ADR-0026](0026-registro-de-modulos-y-composicion.md).
- Pendientes **[por verificar]**: repo, tecnología y estado de Sentinel y People; criticidad y datos personales de cada aplicativo; a qué producto pertenecen `flit-egress` y `flitsas-maps` (este último está en un remoto personal); el nombre exacto en GitHub de los repos legados (se tomó el de la carpeta local, p. ej. `BackCrudTransfer_master`); ambientes de los legados y de los satélites; concesiones de `atlas.catalogo.editar`; endpoint de búsqueda global; offline del catálogo (HU 2.12).
