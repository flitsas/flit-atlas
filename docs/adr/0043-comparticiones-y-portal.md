---
id: ATLAS-ADR-0043
titulo: Comparticiones y portal de clientes — contratos_comparticion con RLS como única puerta, acceso calculado en cada petición por organización, alcance de la invitación y rol, spec podado con caché y pruebas de aislamiento que se exigen por endpoint
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §6.2, §6.3, §7.1, §7.3, §7.4, §8.4, §8.9, §9, §11 y §12, plan 00 §6.2, §8.4, §8.5 y §8.9, HU 4.3, HU 4.4, ATLAS-ADR-0016, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0025, ATLAS-ADR-0026, ATLAS-ADR-0031, ATLAS-ADR-0038, ATLAS-ADR-0039, ATLAS-ADR-0040, ATLAS-ADR-0041]
---

# ATLAS-ADR-0043 · Comparticiones y portal de clientes

## Contexto y problema

La HU 4.3 pide compartir paquetes con organizaciones (vigencia, exportar sí/no, sandbox sí/no, revisión fijada) y el
alcance de los roles externos (§7.3). La HU 4.4 pide el portal: Inicio del cliente (sus aplicativos, paquetes y avisos de
deprecación), el contrato compartido con el spec **podado**, las guías y la experiencia móvil completa. Su criterio de
aceptación bloquea el release (§12): las pruebas de aislamiento pasan en todos los endpoints del portal y de búsqueda.

Hechos verificados en el código el 24/10/2026:

- Los paquetes, sus revisiones (con la llave `(paquete_id, numero)`), los contratos, las versiones y las guías no tienen
  `organizacion_id` ni RLS: son internos (ATLAS-ADR-0041). El predicado obligatorio y RLS por `organizacion_id`
  (`AlcanceDatos`, `plataforma_organizacion_visible`, ATLAS-ADR-0023) ya existen y los módulos los heredan.
- La invitación guarda el alcance (aplicativos, paquetes o documentos; vacío = toda la organización) y los permisos extra
  `exportar` y `sandbox` (ATLAS-ADR-0025); al aceptarla, cada recurso del alcance es una asignación del rol externo con
  ese alcance y su vencimiento. Los permisos se calculan en cada petición con la hora actual.
- `atlas.portal.ver` lo tenían el líder técnico, soporte y los cinco roles externos; `atlas.exportaciones.solicitar` y
  `atlas.sandbox.usar` no los tenía `ext.integrador`, aunque el §7.3 dice que «exporta y usa el sandbox si la compartición
  lo permite».
- Un módulo monta sus endpoints solo en `/api/v1/<id>` y no puede referenciar a la identidad, que es donde vivía
  `RequiereReautenticacion` (§6.3: compartir es una acción sensible).
- No hay un endpoint de búsqueda global todavía (ATLAS-ADR-0026): solo los `IProveedorBusqueda` registrados.
- Los podadores de la HU 4.2 (`Podadores`, ATLAS-ADR-0038) reciben las claves del índice y las URL públicas; no hay
  configuración de URL públicas por contrato.

## Factores de decisión

- Fuga entre organizaciones = el riesgo más alto del programa (§15): filtro en el servidor y en SQL, nunca en la PWA.
- Vencer o revocar corta el acceso sin intervención, como los roles (ATLAS-ADR-0023).
- Que una prueba falle si alguien agrega un endpoint al portal sin probar su aislamiento.
- No mover los paquetes ni los contratos a tablas por organización ni duplicar los specs.
- Que el agente de la HU 4.5 (exportaciones) y el de la HU 4.7 (sandbox) usen la misma decisión de acceso.
- Móvil primero y bundle inicial < 200 KB gzip (§11).

## Opciones consideradas

### Dónde vive el aislamiento

1. **`contratos_comparticion` con `organizacion_id` y RLS como la única puerta del portal**: todo lo del portal parte de
   las comparticiones de la organización de la sesión; los paquetes, versiones y guías se leen solo por los ids que ellas
   dan.
2. RLS también en paquetes, versiones y guías (con una tabla de organizaciones por fila).
3. Copias podadas por organización al compartir.

### Cómo se recorta lo que ve cada persona

1. **Un servicio de acceso (`AccesoPortal`) que calcula la vista en cada petición**: comparticiones vigentes, paquete
   publicado, revisión fijada o vigente, alcance de cada asignación con `atlas.portal.ver` y nivel del rol.
2. Materializar la vista por persona (tabla) y recalcularla con eventos.

### Rutas del portal

1. **Un grupo `/api/v1/portal` que el host monta por módulo** (`IModuloAtlas.MapearEndpointsPortal`, opcional).
2. Las rutas del portal bajo `/api/v1/contratos/portal`.

## Decisión

Opción 1 en las tres.

### Datos (`contratos_comparticion`, migración `Comparticiones`)

| Columna | Para qué |
|---|---|
| `paquete_id`, `organizacion_id` | Única por par. Llave foránea a `plataforma_organizacion` (a mano, como `hola_saludo`) |
| `revision_fijada` | Vacío = sigue la vigente del paquete. Llave foránea compuesta `(paquete_id, revision_fijada)` a la unicidad de las revisiones |
| `puede_exportar`, `puede_sandbox` | La mitad de cada acción; la otra es el permiso de la persona |
| `vigente_desde`, `vigente_hasta` | `timestamptz`; `CHECK` hasta > desde. Vencida, no da acceso desde la siguiente petición |
| `otorgado_por`, `revocado_en`, `revocado_por` | Revocar deja la fila (historia); compartir otra vez la reactiva |

RLS forzada con la política `organizacion_visible` (`USING` y `WITH CHECK`) y el predicado de EF Core por
`OrganizacionId`. Un externo de A, aunque el código olvidara filtrar, no ve ni escribe filas de B; una conexión sin las
variables no ve nada.

### Endpoints internos (`/api/v1/contratos`)

| Método y ruta | Permiso |
|---|---|
| `GET /paquetes/{id}/comparticiones` («Compartido con», con `puedeCompartir`) | `ver_internos` |
| `GET /organizaciones/{id}/comparticiones` (para la página de organizaciones) | `ver_internos` |
| `GET /comparticiones/organizaciones` (clientes y proveedores activos) | `paquetes.gestionar` |
| `POST /paquetes/{id}/comparticiones` · `PUT …/{comparticionId}` · `DELETE …/{comparticionId}` | `paquetes.gestionar` sobre los contratos del paquete + reautenticación |

- Leer con `ver_internos` y gestionar con `paquetes.gestionar` global o sobre los aplicativos de **todos** los contratos
  (de la última revisión y de la que verá la organización), como armar el paquete (ATLAS-ADR-0041).
- Reglas: solo paquetes publicados; solo clientes y proveedores activos (nunca FLIT); con un proveedor, con vencimiento
  (§6.2); la revisión fijada existe y cumple hoy las reglas de publicación (409 `no_publicable`). El estado (`vigente`,
  `programada`, `vencida`, `revocada`) se calcula al leer, con la misma regla que el portal.
- **Reautenticación**: el metadato y la extensión `RequiereReautenticacion` pasan a Plataforma
  (`Flit.Atlas.Plataforma.Permisos`) con la interfaz `IVerificadorReautenticacion`, que la identidad implementa con su
  `Reautenticacion.EsReciente`/`RechazarAsync` de siempre. Sin verificador registrado, rechaza (falla cerrada).
- Auditoría en la misma transacción (`contratos.comparticion_creada|_cambiada|_revocada`) con la **organización destino**
  en `organizacion_id` (la encuentra su página y su auditoría), ids, revisión, permisos y vigencia; nunca nombres.

### Alcance de los roles externos (§7.3)

`AccesoPortal` calcula en cada petición, para la persona de la sesión:

1. Las comparticiones **de su organización** (filtro explícito, además del predicado y RLS), vigentes y no revocadas.
2. Solo paquetes `publicado` (ni borrador ni archivado) en la revisión fijada o vigente.
3. Cada contrato de la revisión, si sigue `compartible` (un contrato que volvió a interno deja de verse: cierra el
   pendiente de ATLAS-ADR-0041) y si alguna asignación de la persona con `atlas.portal.ver` lo cubre: global u
   organización (toda la organización), `paquete:<id>` o `aplicativo:<id>` (los contratos de ese aplicativo o de sus hijos
   dentro del paquete). Cada guía, si sigue compartible y publicada y su revisión fijada es compartible.
4. El nivel de cada contrato es el mayor que dan sus asignaciones según el rol:

   | Rol | Toda la organización | Paquete o aplicativo nombrado en la invitación |
   |---|---|---|
   | `ext.integrador` | técnico | técnico |
   | `ext.proveedor` | nada | técnico |
   | `ext.cliente_funcional` | resumen | técnico solo si nombra el paquete («salvo que se compartan»); con aplicativo, resumen |
   | `ext.asesor_comercial` | resumen | resumen |
   | `ext.admin_organizacion` | resumen | resumen |
   | interno con `portal.ver` | técnico (FLIT no tiene comparticiones: queda vacío) | técnico |

   *Resumen* = nombre, descripción, contratos, versiones y avisos; *técnico* = además operaciones, spec podado, guías y
   cambios. Un rol con `atlas.portal.ver` que no esté en la tabla no ve nada, y una prueba exige que todos tengan su fila.
   La documentación funcional (manuales, fichas comerciales) llega en F5 por el punto de extensión
   `IDocumentacionDelPortal` de Plataforma (vacío por defecto): el Inicio del cliente ya la pinta por aplicativo.
5. **Exportar** = compartición con `puede_exportar` + nivel técnico + (`atlas.exportaciones.solicitar` con una
   asignación que cubra el paquete **o** el permiso extra `exportar` de su invitación aceptada y vigente). **Sandbox**,
   igual con `puede_sandbox`, `atlas.sandbox.usar` y `sandbox`.

La matriz cambia en una sola cosa: `ext.integrador` recibe `atlas.exportaciones.solicitar` y `atlas.sandbox.usar` (el
§7.3 lo dice; la compartición sigue decidiendo). Proveedor y cliente funcional solo con el permiso extra de su invitación.

**Consulta para las HU 4.5 y 4.7**: `AccesoPortal.PuedeExportarAsync(usuario, paqueteId)` y `PuedeUsarSandboxAsync`
devuelven `DecisionPortal` (`Permitida`, `Codigo`: `no_encontrado`, `solo_resumen`, `no_permitido_por_comparticion`,
`sin_permiso`; y el `PaqueteDelPortal` con la revisión, los contratos y la compartición). El spec podado de un contrato lo
da `SpecsDelPortal.PodarAsync(paquete, contrato, ambiente)`.

### Portal (`/api/v1/portal`, etiqueta «Portal de clientes»)

| Método y ruta | Qué |
|---|---|
| `GET /inicio` | Organización, aplicativos con sus paquetes (y documentos, F5), paquetes y avisos de deprecación o retiro |
| `GET /paquetes/{id}` | Revisión que ve la organización, contratos y, con nivel técnico, operaciones y guías; `puedeExportar` y `puedeSandbox` |
| `GET /paquetes/{id}/contratos/{contratoId}/spec?ambiente=` | Spec podado (ETag = su SHA-256, `Cache-Control: private, no-cache`) |
| `GET /paquetes/{id}/contratos/{contratoId}/cambios` | Del changelog de la CI, solo los cambios cuya operación (método y ruta) está compartida |
| `GET /paquetes/{id}/guias/{guiaId}` | La revisión de la guía que fijó el paquete |
| `GET /buscar?q=` | Paquetes, contratos y operaciones compartidas que coinciden; nunca fuera de la vista |
| `GET /favoritos` · `PUT`/`DELETE /favoritos/paquetes/{id}[/contratos/{c} \| /guias/{g}]` | HU 4.9: favoritos para ver sin conexión (ATLAS-ADR-0034); solo lo que la persona ve |

- Todo con `atlas.portal.ver`. Un paquete fuera de la vista (de otra organización, vencido, revocado, archivado o fuera
  del alcance) responde **404** `no_encontrado`, sin revelar si existe; lo técnico con nivel resumen, 403 `solo_resumen`.
- **Spec podado**: los podadores de la HU 4.2 con las claves de la revisión y las URL públicas de
  `Contratos:Portal:Servidores` (`<slug del aplicativo>/<código>`, ambiente y URL); sin URL configurada, sin `servers`.
  El ambiente por defecto es `Contratos:Portal:Ambiente` (`pdn`). Nunca el spec completo; además de la verificación del
  podador, el endpoint comprueba que no sobre ninguna operación. Caché en memoria del proceso por (paquete, revisión,
  contrato, versión, ambiente, URL), con tope de tamaño (`Contratos:Portal:CacheMiB`, 64) y expiración deslizante de 6 h:
  las versiones y las revisiones son inmutables, así que la clave fija el resultado. El acceso se revisa antes de leer la
  caché.
- **Changelog**: los cambios que no nombran una operación (componentes, seguridad global) no se muestran: podrían hablar
  de lo que no se comparte.
- **Auditoría** (§8.9): toda vista, búsqueda (solo el largo del texto, no el texto) y acceso negado queda en
  `plataforma_auditoria` con la persona, la organización, la IP, el paquete y la revisión (`contratos.portal_*`).
- **Rutas**: `IModuloAtlas.MapearEndpointsPortal(RouteGroupBuilder)` (por defecto, nada); el host monta, por módulo, un
  grupo en `/api/v1/portal` con sesión, la etiqueta «Portal de clientes» y la bandera del módulo. `portal` queda reservado
  como id de módulo.

### Pruebas de aislamiento (CA bloqueante)

- `AislamientoPortalTests` (atlas-api completo, Testcontainers): dos organizaciones, tres paquetes (uno de B, dos de A),
  personas de A con alcance a un paquete, con toda la organización y con un aplicativo de B, y una de B. Cada endpoint del
  portal tiene un **caso** que pide lo propio, lo ajeno y las mezclas (paquete propio con contrato o guía ajenos y al
  revés); en cada respuesta se buscan las marcas de lo ajeno (ids, nombres, rutas) y de lo nunca compartido (operaciones
  internas, sus esquemas, servidores internos). Se repite tras **revocar** la compartición y tras **vencer** la de B y la
  asignación de una persona. `Todo_endpoint_del_portal_tiene_su_caso_de_aislamiento` recorre los endpoints registrados con
  prefijo `/api/v1/portal` (en las dos configuraciones) y falla si uno no tiene caso. Se comprobó que la prueba detecta
  una regresión (ignorar el id del paquete del alcance la hace fallar).
- Búsqueda: los `IProveedorBusqueda` registrados, con la sesión de un externo de A, no devuelven nada; y `/portal/buscar`
  no trae lo de B.
- RLS en SQL directo (`PortalTests` del módulo, con el usuario del servicio `flit_atlas`, que no se salta RLS): con las
  variables de A, la consulta del portal (compartición → paquete → revisión) solo trae lo de A; sin variables, nada; no se
  puede escribir una compartición de B; y el acceso del portal con una organización equivocada por un error del código no
  ve nada porque RLS lo ataja.

### PWA

- Área portal dentro del módulo de contratos (comparte el visor de Scalar, la vista de operaciones y el Markdown seguro;
  un módulo no importa a otro): `/portal` (Inicio del cliente con búsqueda en lo compartido), `/portal/paquetes/<id>`,
  `/portal/paquetes/<id>/contratos/<contrato>` (operaciones del spec podado; en escritorio, Scalar en su iframe con
  sandbox y CSP, ATLAS-ADR-0039; guías y cambios) y `/portal/paquetes/<id>/guias/<guía>`. Cada pantalla en su chunk.
- El contrato del frontend suma `audiencia` (`externos` | `empleados`) a las entradas de navegación y a los widgets: la
  entrada «Portal» (en la barra inferior del móvil) y el widget «Lo que FLIT comparte contigo» son solo para externos; las
  entradas internas de contratos exigen `ver_internos`; los accesos rápidos de Inicio van a la primera entrada visible de
  cada módulo. Un externo no ve catálogo, equipo, soporte ni contratos internos.
- Exportar y Sandbox: botones deshabilitados con su motivo según `puedeExportar` / `puedeSandbox`, y un gancho
  (`portal/acciones.ts`, `conectarAccionPortal`) para que las HU 4.5 y 4.7 conecten la acción.
- Interno: «Compartido con» en el detalle del paquete (organización, vigencia, exportar, sandbox, revisión fijada;
  compartir, cambiar y revocar en hojas inferiores, con el aviso de reautenticación) y «Paquetes compartidos» en cada
  organización de la administración.

## Consecuencias

### Positivas

- Una sola tabla con RLS es la puerta: no hay que proteger cada tabla de contratos ni copiar specs por organización.
- Revocar, vencer, archivar, volver interno un contrato o perder una asignación corta el acceso en la siguiente petición.
- La suite falla si llega un endpoint al portal sin su prueba de aislamiento.
- Exportar y sandbox usarán exactamente la misma decisión de acceso que las pantallas.

### Negativas

- La vista se calcula en cada petición (unas seis consultas pequeñas); con muchos paquetes por organización habrá que
  cachearla por persona con invalidación.
- La caché de specs podados es del proceso: cada réplica poda su primera vez (unos 200 ms con el contrato más grande).
- Los paquetes, contratos y guías siguen sin RLS: el aislamiento en SQL depende de entrar siempre por
  `contratos_comparticion` (lo hace `AccesoPortal`; una prueba recorre todo el portal).
- El portal muestra el spec en el ambiente por defecto; las URL públicas por contrato son configuración que hay que
  mantener.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| RLS en paquetes, versiones y guías | Son internos y se comparten con varias organizaciones: haría falta una tabla de organizaciones por fila y políticas con subconsultas, para la misma garantía |
| Copias podadas por organización | Duplicaría specs grandes y obligaría a recopiar al fijar, publicar o deprecar |
| Vista materializada por persona | Otra pieza que invalidar (vencimientos, revocaciones); el cálculo por petición ya es barato |
| `/api/v1/contratos/portal` | El plan 00 §8.5 define `/portal/...` y la búsqueda del portal es transversal a los módulos que compartan con clientes (documentación en F5) |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Contratos/Comparticiones/`, `…/Portal/`, `apps/web/src/modulos/contratos/portal/`,
  `apps/web/src/modulos/contratos/paquetes/Comparticiones.tsx`, `services/api/tests/Flit.Atlas.Api.Tests/AislamientoPortalTests.cs`.
- Pendientes **[por verificar con el líder]**: la tabla de niveles por rol (sobre todo `ext.cliente_funcional` con
  alcance de aplicativo y `ext.admin_organizacion`); que un empleado con `atlas.portal.ver` pueda ver el portal «como» una
  organización (hoy ve el de FLIT, vacío); las URL públicas por contrato y ambiente y el ambiente por defecto del spec;
  si compartir con un proveedor debe tener un vencimiento máximo; que la búsqueda global (cuando exista, ATLAS-ADR-0026)
  incluya los resultados del portal para externos.
- HU 4.8 (ATLAS-ADR-0046): los avisos de deprecación usan `AccesoPortal.VistaDeAsync` (la misma vista, sin sesión) para
  decidir a quién le llegan, y el aviso del Inicio suma `reemplazo` con el mismo texto que el correo.
