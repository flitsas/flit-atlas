---
id: ATLAS-ADR-0026
titulo: Registro de módulos — IModuloAtlas en Plataforma, lista única en Flit.Atlas.Composicion y bandera que hace cumplir el servidor
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §2 (crece por módulos), §5.3, §7.4, §9 y §10, HU 1.9, ATLAS-ADR-0016, ATLAS-ADR-0021, ATLAS-ADR-0023, ATLAS-ADR-0024]
---

# ATLAS-ADR-0026 · Registro de módulos y composición del host

## Contexto y problema

ATLAS-ADR-0016 fijó el contrato de módulo (`IModuloAtlas` y `ModuloAtlas`) y una lista explícita en el host, pero hasta la HU 1.9 no existía ningún módulo funcional ni el código del contrato. La HU 1.9 pide `docs/como-crear-un-modulo.md` con un módulo de ejemplo «hola» que se agrega **sin tocar otros módulos**. Hechos verificados en el código antes de esta decisión:

- atlas-api y el migrador son dos ejecutables; cada uno registraba a mano los `DbContext` del núcleo, y el migrador aplicaba `PermisosIdentidad.ManifiestosDelNucleo`, una lista distinta de la que arma atlas-api por inyección de dependencias (ATLAS-ADR-0023 lo anotó como deuda: «sumar su manifiesto también en el migrador»).
- Las banderas viven en `plataforma_modulo` y `GET /api/v1/plataforma/banderas` las publica, pero ningún endpoint las hacía cumplir: ATLAS-ADR-0016 pide un 404 cuando el módulo está apagado.
- Los scopes de OpenIddict por módulo (HU 1.3c, ATLAS-ADR-0024) se registran con `AgregarScopes(IProveedorScopes)` en `Flit.Atlas.Identidad`, que un módulo no puede referenciar.

## Factores de decisión

- Agregar un módulo = crear su proyecto y su carpeta, **una** referencia y **una** línea en el host, y su bandera (prompt maestro §10).
- Un solo lugar del que salen atlas-api, el migrador y las pruebas: sin listas que se desincronicen.
- Un módulo solo depende del núcleo (`Flit.Atlas.Plataforma`), y una prueba lo hace cumplir.
- El servidor es la autoridad de la bandera; el frontend solo oculta.
- Cambios mínimos en Program.cs, Identidad y Plataforma (se trabaja en paralelo con las HU 1.3c y 1.5).

## Opciones consideradas

1. **`IModuloAtlas` en `Flit.Atlas.Plataforma` y un proyecto de composición (`Flit.Atlas.Composicion`) con la lista única**, que referencian atlas-api y el migrador.
2. La lista en `Flit.Atlas.Api` y el migrador referenciando a atlas-api (o compilando el mismo archivo enlazado).
3. Descubrimiento por reflexión de los ensamblados `Flit.Atlas.Modulos.*`.
4. Un proyecto de abstracciones aparte (`Flit.Atlas.Modulos.Abstracciones`) del que dependan los módulos y la Plataforma.

## Decisión

Opción elegida: **1**.

### Contrato (`Flit.Atlas.Plataforma.Modulos`)

- `IModuloAtlas`: `Id` (snake_case: define ruta `/api/v1/<id>`, bandera `modulo.<id>`, prefijo `<id>_`, historial `__ef_migraciones_<id>`, permisos, eventos y scopes `atlas.<id>.*`), `Nombre` y `Descripcion` (etiqueta del contrato OpenAPI), `ActivoPorDefecto`, `Permisos` (`ManifiestoPermisos`), `ContextoDatos` (un `DbContext` derivado de `ContextoAtlas` o `null`), `EventosPublicados`, `EventosConsumidos`, `TiposNotificacion`, `Scopes` (`ScopeModulo`), `RegistrarDatos` (solo el contexto; lo llaman atlas-api, el migrador y las pruebas), `RegistrarServicios` (solo atlas-api) y `MapearEndpoints(RouteGroupBuilder)`.
- Búsqueda: `IProveedorBusqueda` (`Flit.Atlas.Plataforma.Busqueda`) con `AgregarProveedorBusqueda<T>()`; el endpoint que los combina llega con el primer módulo con datos (F2).
- `ContextoAtlas.ConfigurarOpciones(opciones, cadena, tablaHistorial)`: Npgsql, snake_case, historial propio en `atlas` y el interceptor de RLS, como el núcleo.
- `ExtensionesModulos.Validar` falla al arrancar si dos módulos chocan o si un id, permiso, evento, scope o tipo de notificación no lleva el prefijo del módulo. Ids reservados: `plataforma`, `identidad`, `version`.
- Se mantiene la interfaz de ATLAS-ADR-0016 con estos ajustes: el manifiesto completo en lugar de una lista de permisos, `RegistrarDatos` separado de `RegistrarServicios` (el migrador no debe arrancar los servicios de un módulo), el contexto como tipo en lugar de `ConfigurarModelo` (cada módulo tiene su `DbContext` y su historial, como el núcleo) y los proveedores de búsqueda por inyección de dependencias.

### Composición (`Flit.Atlas.Composicion`)

- `ModulosAtlas.Registrados`: la lista única. `ModulosAtlas.Manifiestos`: núcleo + módulos, lo que aplica el migrador; `MatrizPermisosTests` verifica que atlas-api tenga exactamente los mismos.
- `AgregarModulosAtlas` (atlas-api): registra cada módulo (manifiesto, datos, servicios) y une sus `ScopeModulo` con el registro de scopes de la identidad (`ScopesDeModulos`, dueño = el módulo). Así el módulo no conoce `Flit.Atlas.Identidad`.
- El migrador migra el núcleo y luego cada `ContextoDatos` (las políticas RLS del módulo usan funciones del núcleo), crea en `plataforma_modulo` la bandera de cada módulo nuevo con `ActivoPorDefecto` **sin tocar las existentes** y aplica la matriz con `ModulosAtlas.Manifiestos`. Desaparece la lista duplicada del migrador.

### Host

- Program.cs: `builder.Services.AgregarModulosAtlas(...)` y `app.MapModulos()`. `MapModulos` monta cada módulo en `/api/v1/<id>` con su etiqueta, sesión obligatoria, `MetadatoModulo` y un filtro que responde **404 si la bandera está apagada** para la organización de la persona (misma regla que `/banderas`; sin fila, `ActivoPorDefecto`). Un rol sin el permiso recibe 403 antes del filtro.
- `ContratoOpenApi` agrega la etiqueta de cada módulo y el 404 de la bandera en sus operaciones.

### Módulo de ejemplo «hola»

- `Flit.Atlas.Modulos.Hola`: tabla `hola_saludo` con `organizacion_id` (predicado obligatorio, RLS forzada con `atlas.plataforma_organizacion_visible`, llave foránea a `plataforma_organizacion`, `texto` marcado `@pii`), permisos `atlas.hola.ver` (lector e integrador) y `atlas.hola.gestionar` (líder técnico y desarrollador), tres endpoints y su historial `__ef_migraciones_hola`.
- **Se compila en las imágenes de qa y pdn, apagado por bandera** (`ActivoPorDefecto = false` y `banderaPorDefecto: false` en la PWA). Así el ejemplo pasa por el mismo camino que un módulo real (migrador, matriz, contrato, imagen) y las pruebas de arquitectura lo verifican en cada PR; en qa y pdn no se ve ni responde (404). Costo aceptado: su tabla vacía existe en cada ambiente y sus dos permisos aparecen en la matriz. Retirarlo es borrar su proyecto, su carpeta, su línea y su referencia.
- Frontend: `apps/web/src/modulos/hola/` (página, widget de Inicio, carga diferida) y la pantalla de la matriz rol × permiso como módulo propio `apps/web/src/modulos/permisos/` (CA de la HU 1.4).

### Pruebas que lo hacen cumplir

- `ArquitecturaModulosTests` (API): cada ensamblado de módulo solo referencia `Flit.Atlas.Plataforma`; ningún ensamblado ni `.csproj` fuera de `Flit.Atlas.Composicion` referencia un módulo; el contexto de cada módulo solo mapea tablas con su prefijo y su historial; sus endpoints viven bajo `/api/v1/<id>` con su etiqueta; toda tabla con `organizacion_id` (núcleo y módulos) tiene RLS forzada.
- `tests/arquitectura-modulos.test.ts` (PWA): ningún módulo importa a otro y el shell solo los conoce por `registro.ts`.

## Consecuencias

### Positivas

- Agregar un módulo toca: su proyecto, su carpeta de la PWA, una referencia y una línea en `Flit.Atlas.Composicion`, una línea en `registro.ts`, el `COPY` de su `.csproj` en el Dockerfile y la solución; además, los artefactos generados (contrato, matriz) y su ícono si es nuevo.
- Una sola lista para atlas-api, el migrador y las pruebas; la deuda de ATLAS-ADR-0023 queda cerrada.
- La bandera es real en el servidor.

### Negativas

- Un proyecto más (`Flit.Atlas.Composicion`) que referencia Identidad y todos los módulos.
- Cada petición a un módulo hace una consulta a `plataforma_modulo` para la bandera (se puede cachear si la carga lo pide).
- El Dockerfile necesita una línea `COPY` por módulo para la restauración en caché.
- Los tipos de notificación de los módulos aún no se suman a las preferencias del centro de notificaciones (`TiposNotificacion.Todos` es estático): se conecta con el primer módulo que notifique (F2) **[por verificar]**.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Lista en atlas-api y migrador que referencia a atlas-api | El migrador arrastraría el host web; el archivo enlazado compila dos veces y se presta a confusión |
| Reflexión | Ya descartada en ATLAS-ADR-0016: difícil de seguir y de probar |
| Proyecto de abstracciones aparte | Los módulos igual necesitan `ContextoAtlas`, `Entidad`, permisos y mensajería de Plataforma; separarlo duplicaría dependencias sin ganancia hoy |

## Más información

- [`docs/como-crear-un-modulo.md`](../como-crear-un-modulo.md), [ATLAS-ADR-0016](0016-contrato-de-modulo.md), [ATLAS-ADR-0023](0023-modelo-de-roles-y-permisos.md), [ATLAS-ADR-0024](0024-servidor-de-tokens-openiddict.md).
- Pendientes **[por verificar]**: pantalla para encender banderas por ambiente y organización (`atlas.plataforma.configurar`; hoy se cambia `plataforma_modulo` en la base); caché de la bandera; endpoint de búsqueda global; tipos de notificación de los módulos en las preferencias; ayuda de auditoría para los módulos.
