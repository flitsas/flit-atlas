---
id: ATLAS-ADR-0023
titulo: Modelo de roles y permisos — manifiestos en el código, permisos calculados en cada petición, predicado obligatorio y RLS
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §2, §6.2, §6.3, §7, §9, §10 y §12, HU 1.4, ATLAS-ADR-0016, ATLAS-ADR-0022]
---

# ATLAS-ADR-0023 · Modelo de roles y permisos

## Contexto y problema

La HU 1.4 pide: manifiesto de permisos por módulo, los roles internos (§7.2) y externos (§7.3), asignaciones con alcance y vigencia, el mapeo de grupos de Entra a roles (ya existía, HU 1.3), un rol implícito por propiedad (interfaz; se llena en F2), políticas de ASP.NET Core, predicado obligatorio y RLS en las tablas con `organizacion_id`. CA: matriz rol × permiso generada desde el código y visible en Atlas; pruebas de autorización parametrizadas que recorren todos los endpoints con todos los roles; un rol externo vencido pierde el acceso sin intervención.

El prompt maestro (§1, regla 4) marca el modelo de permisos como una decisión costosa de cambiar: por eso este ADR. Hechos verificados en el código: la sesión vive en el servidor (`identidad_sesion`, ATLAS-ADR-0022); `IInvalidadorSesiones` es el punto de extensión para cortar sesiones; el usuario `flit_atlas` es el dueño de las tablas (script de `flit`); las pruebas de la API corren con el superusuario del contenedor y las de Plataforma con `flit_atlas`.

## Factores de decisión

- Una sola fuente del mapeo rol → permisos, revisable en un PR y sin drift con la base.
- Agregar un módulo no obliga a tocar los demás (ATLAS-ADR-0016).
- Un rol vencido o retirado deja de valer sin esperar a nadie (§6.2) y sin meter permisos en la cookie.
- «Seguro por defecto»: filtro en el servidor y en SQL, nunca solo en la interfaz (§2, §12).
- Pocas piezas nuevas y nada de criptografía o motores propios.

## Opciones consideradas

1. **Manifiestos en el código + permisos calculados en cada petición + predicado de EF Core y RLS por variables de sesión.**
2. Roles y permisos editables en la base desde la administración (RBAC dinámico).
3. Permisos como claims en la sesión, renovados al cambiar roles invalidando las sesiones.
4. Un motor de políticas externo (OPA, Cedar, OpenFGA).

## Decisión

Opción elegida: **1**.

### 1. Manifiestos y roles (la fuente)

- Cada módulo declara un `ManifiestoPermisos` (`Flit.Atlas.Plataforma.Permisos`): id del módulo, sus `PermisoAtlas` (`atlas.<modulo>.<accion>`, se admiten más segmentos como `atlas.contratos.guias.editar`) con **los roles que reciben cada permiso** (`ConcedidoA`) y los tipos de alcance que agrega. Se registra con `servicios.AgregarManifiestoPermisos(...)`. `atlas.superadmin` recibe todos sin declararlo.
- Los roles son del núcleo (`RolesAtlas`): los 8 internos del §7.2, los 5 externos del §7.3 y el implícito `atlas.responsable` (§7.2, propiedad). Un módulo no crea roles: dice qué roles reciben sus permisos.
- **La lista base del §7.4 se declara ya** en `ManifiestosNucleo` (catálogo, equipo, soporte, contratos, documentación, diagramas, asistente, gobierno de IA), con las concesiones que salen de la lectura del §7.2 y §7.3. Así la matriz y `/yo.permisos` están completos desde F1 y la PWA filtra la navegación con los mismos códigos que usan sus manifiestos (`atlas.catalogo.ver`, `atlas.soporte.ver`). Cuando llega un módulo, usa esos códigos (son contrato) y declara en su propio manifiesto solo los permisos nuevos. **[por verificar con el líder]** las concesiones de cada rol (ver `docs/permisos/matriz-rol-permiso.md`).
- Se agrega `atlas.roles.ver` (ver roles, asignaciones, mapeos y la matriz; líder técnico y auditor) y el manifiesto de la identidad con `atlas.identidad.administrar` (suspender, reactivar y cerrar todas las sesiones de una persona; solo superadmin), que reemplaza el rol provisional de ATLAS-ADR-0022.
- `CatalogoPermisos` valida al arrancar (módulos y permisos únicos, roles existentes, formato) y arma la matriz. El **migrador** la aplica a la base de forma idempotente (`plataforma_rol`, `plataforma_permiso`, `plataforma_rol_permiso`) para las consultas que filtran en SQL (asistente, F7). La base es una proyección: nadie la edita y una prueba verifica que coincida con el código.
- **Todo empleado activo tiene `atlas.lector`** sin asignarlo (§7.2: «cualquier empleado sin rol específico»); los demás roles internos se suman a él. Así un empleado no pierde lo básico cuando vence su rol específico. **[por verificar con el líder]**.

### 2. Asignaciones: alcance, vigencia y origen

- `plataforma_asignacion_rol`: persona, rol, organización, alcance (`alcance_tipo` texto + `alcance_id`), `vigente_desde`, `vigente_hasta`, origen (`manual`, `grupo_entra`, `invitacion`) y quién asignó. El tipo de alcance pasa de enum a texto validado contra el catálogo: el núcleo trae `global`, `organizacion` y `ambiente`, y cada manifiesto agrega los suyos (`aplicativo`, `paquete`, `documento`) sin tocar Plataforma.
- Reglas: un rol externo solo para una persona externa, en **su** organización y con vencimiento obligatorio de máximo 90 días (§6.2, configurable más adelante); un rol interno solo para un empleado; `atlas.lector` y los implícitos no se asignan. La calculadora repite estas reglas al leer (defensa ante datos sembrados a mano).
- Los grupos de Entra (`plataforma_mapeo_grupo_entra`) solo otorgan roles internos; sus asignaciones se sincronizan en cada ingreso y se **suman** a las manuales. Toda alta y baja (manual, de grupo, por vencimiento) queda en `plataforma_auditoria` (`plataforma.asignacion_creada`, `…_retirada`, `…_vencida`, `plataforma.mapeo_grupo_entra_creado`, `…_eliminado`) en la misma transacción que el cambio.
- `PermisosEfectivos.TieneEn(permiso, alcance, organizacion)` resuelve el alcance por recurso (global, mismo tipo e id, o la organización del recurso) para las consultas de los módulos. Las políticas de endpoint exigen el permiso en algún alcance.

### 3. Permisos efectivos: en el servidor, en cada petición

- `IPermisosDeSesion` calcula, **una vez por petición y con la hora actual** (`TimeProvider`), los roles y permisos de la persona de la sesión: asignaciones vigentes + `atlas.lector` de empleado + roles implícitos de `IProveedorRolesImplicitos`. Una persona inactiva o suspendida, o de una organización suspendida, no tiene permisos. La cookie y el ticket no llevan permisos.
- Por eso **un rol vencido deja de contar en la primera petición después de su vencimiento**, sin trabajo programado ni intervención (CA). El costo son dos consultas por petición autenticada que exige permisos; si hace falta, se cachea con invalidación por sello.
- Invalidación de sesiones (ATLAS-ADR-0022), siempre **después** de guardar: al **retirar** una asignación, al eliminar un mapeo que quita un rol y al vencer (el `VigilanteVencimientos` de cada réplica, cada minuto, con `FOR UPDATE SKIP LOCKED`, audita el vencimiento e invalida las sesiones para que la PWA cierre y borre lo guardado sin conexión, §11). **Sumar** un rol no invalida sesiones: no quita nada y el permiso nuevo ya vale en la siguiente petición. Esto precisa el §6.3 («cambiar roles invalida sus sesiones») **[por verificar con el líder]**.
- `GET /api/v1/plataforma/yo` devuelve `roles` y `permisos` (listas ordenadas de códigos) calculados así.

### 4. Políticas de ASP.NET Core

- Cada permiso del catálogo es una política con su mismo nombre (`ProveedorPoliticasPermiso`): sesión + `RequisitoPermiso`. En los endpoints: `.RequierePermiso("atlas.roles.gestionar")`. Las políticas con nombre propio siguen existiendo (`PoliticasIdentidad.Administrar` = `atlas.identidad.administrar`).
- El contrato OpenAPI declara en cada operación `x-atlas-permisos` y el 403 nombra el permiso.
- Prueba parametrizada: descubre **todos** los endpoints desde `EndpointDataSource` (en la configuración de pruebas y en la de qa), los llama con **cada rol** asignable y comprueba que entra exactamente a los de sus permisos; falla si un endpoint no declara permiso salvo que esté en la lista explícita de públicos o de «solo sesión» (datos de la propia persona).

### 5. Administración

- Bajo `/api/v1/plataforma` (etiqueta «Permisos»): `GET /roles`, `GET /permisos/matriz`, `GET|POST /personas/{personaId}/asignaciones`, `DELETE /asignaciones/{id}`, `GET|POST /mapeos-grupos-entra`, `DELETE /mapeos-grupos-entra/{id}`. Leer exige `atlas.roles.ver`; cambiar, `atlas.roles.gestionar` y reautenticación (§6.3). Viven en `Flit.Atlas.Identidad` porque usan la reautenticación y la invalidación de sesiones; la lógica está en `AdministracionRoles`, que la HU 1.5 reutiliza al aceptar una invitación (`OrigenAsignacion.Invitacion`).

### 6. Aislamiento por organización: predicado obligatorio y RLS

- **Predicado obligatorio**: `ContextoAtlas.AplicarPredicadoDeOrganizacion` agrega un filtro global de EF Core a toda entidad con `OrganizacionId` (y a la organización por su id) según `IAlcanceDatos` de la petición: un externo ve solo su organización; un empleado, el sistema y los procesos sin sesión (ingreso, trabajos) ven todas. Las filas sin organización solo las ve un alcance total. Los módulos heredan el filtro llamando al mismo método.
- **RLS** (segunda capa): `InterceptorAlcanceDatos` fija `atlas.alcance_total` y `atlas.organizaciones` con `set_config` al abrir cada conexión; las políticas `organizacion_visible` usan `atlas.plataforma_organizacion_visible(uuid)`. Tablas: `plataforma_organizacion`, `plataforma_persona`, `plataforma_asignacion_rol`, `plataforma_invitacion` (lectura y escritura) y `plataforma_auditoria` (lectura filtrada; la inserción siempre se permite y sigue siendo de solo inserción).
- **`FORCE ROW LEVEL SECURITY`**: `flit_atlas` es el dueño de las tablas y, sin `FORCE`, el dueño se salta RLS. Una conexión sin las variables no ve ninguna fila (niega por defecto). Un superusuario o un rol con `BYPASSRLS` siempre se la salta: por eso la prueba de aislamiento corre con `flit_atlas`.
- Una prueba de arquitectura exige que toda tabla del esquema con `organizacion_id` tenga RLS forzada y una política; las migraciones de los módulos deben hacer lo mismo con la función del núcleo.

## Consecuencias

### Positivas

- La matriz es código revisable en el PR; `docs/permisos/matriz-rol-permiso.md`, el endpoint y la base salen de él y una prueba lo verifica.
- Vencer, retirar o suspender corta el acceso en la petición siguiente; la invalidación de sesiones es una capa más, no la garantía.
- Dos capas de aislamiento independientes, cada una con su prueba.

### Negativas

- Cambiar a qué rol se concede un permiso exige un despliegue (no hay edición en caliente). Es lo buscado; si el negocio pide roles personalizados, se revisa este ADR.
- Cada conexión abierta por EF Core hace una ida y vuelta más (`set_config`), y cada petición con permiso, dos consultas.
- Las migraciones de datos sobre tablas con RLS deben fijar `atlas.alcance_total` (el migrador lo hace al usar `PlataformaDbContext`; un script SQL suelto, no).
- Los módulos futuros que agreguen permisos deben sumar su manifiesto también en el migrador (hoy `PermisosIdentidad.ManifiestosDelNucleo`; con `IModuloAtlas`, desde el mismo registro).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| RBAC editable en la base | Pierde la revisión en PR y la matriz como código; abre una superficie de escalamiento de privilegios en la administración |
| Permisos en la sesión | La cookie o el ticket crecen y un vencimiento exigiría un trabajo programado para no dejar permisos viejos vivos |
| Motor de políticas externo | Otra pieza que operar para 1–2 desarrolladores; el modelo cabe en RBAC + alcance + propiedad |

## Más información

- `docs/permisos/matriz-rol-permiso.md` (generado), `services/api/README.md` (cómo declarar permisos en un módulo).
- Pendientes **[por verificar]**: concesiones por rol; `atlas.lector` implícito; invalidar solo al quitar acceso; vigencia máxima de externos configurable (HU 1.5); niveles de visibilidad del §7.1 (F5); pruebas de aislamiento sobre los endpoints del portal y de la búsqueda (F4, §12); caché de permisos si la carga lo pide.
