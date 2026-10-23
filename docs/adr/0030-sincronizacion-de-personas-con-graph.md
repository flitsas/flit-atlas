---
id: ATLAS-ADR-0030
titulo: Sincronización de personas con Microsoft Graph — trabajo diario con candado, origen configurable (enlazadas por defecto), inactivación con cierre de sesiones, foto en la base con caché y directorio interno
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §6.1, §7.3, §7.4, §8.2, §9, §12 y §13, HU 2.2, ATLAS-ADR-0004 §4, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0026, ATLAS-ADR-0027]
---

# ATLAS-ADR-0030 · Sincronización de personas con Microsoft Graph

## Contexto y problema

La HU 2.2 pide sincronizar a diario con Microsoft Graph a las personas empleadas: nombre, cargo, área, foto, estado (cuenta habilitada o deshabilitada en Entra) y teléfono; el teléfono solo se ve con `atlas.equipo.ver_contacto` (§8.2). ATLAS-ADR-0004 §4 ya fijó que una cuenta deshabilitada en Entra deja a la persona inactiva y le revoca las sesiones, sin decir cómo ni qué personas se traen.

Hechos verificados en el código:

- `plataforma_persona` ya tiene `cargo`, `area`, `telefono` (`@pii`) y `estado` (`activa`, `inactiva`, `suspendida`); el ingreso JIT (`AprovisionadorEmpleados`) refresca nombre, cargo y área en cada ingreso y rechaza a una persona inactiva.
- `PerfilGraph` lee el perfil y los grupos con un **token de aplicación** (`User.Read.All` y `GroupMember.Read.All`, F0.4 ítem 5.4). Esos dos permisos alcanzan para leer `accountEnabled`, `mobilePhone`, `businessPhones`, la foto de otro usuario y los miembros de un grupo.
- `IInvalidadorSesiones` cambia el sello y revoca sesiones y tokens (ATLAS-ADR-0022). `SincronizadorClientes` ya usa un candado consultivo de PostgreSQL para que varias réplicas no hagan lo mismo a la vez.
- El AppHost levanta un S3 local, pero atlas-api no lo usa: solo `atlas-docs` (F4). No hay cliente S3 en la API.
- `atlas.equipo.ver` (todo empleado, por `atlas.lector`), `atlas.equipo.gestionar` y `atlas.equipo.ver_contacto` (líder técnico, desarrollador y soporte) ya están en la matriz desde la HU 1.4: no hace falta un permiso nuevo.
- El buscador de personas de la administración (`GET /api/v1/plataforma/personas`) exige `atlas.roles.ver` y devuelve correos de externos: no sirve como directorio para todo empleado.

## Factores de decisión

- Seguro por defecto: una configuración vacía no debe traer cuentas de servicio, buzones compartidos, salas ni invitados B2B, y un error de configuración no debe dejar inactivo a todo el equipo.
- Ningún permiso de Graph nuevo y ninguna dependencia nueva (ni paquete ni servicio).
- Una sola corrida a la vez en todas las réplicas; tolerante a que Graph o la base no respondan.
- Nunca tocar externos ni la cuenta de emergencia (ATLAS-ADR-0027); no deshacer una suspensión hecha por un administrador.
- Minimización de datos (Ley 1581, §12): la auditoría y los logs llevan conteos, no nombres, correos ni teléfonos.

## Opciones consideradas

1. **Trabajo programado en atlas-api** (`BackgroundService` con `TimeProvider`, hora de Bogotá configurable) con candado consultivo, origen configurable (por defecto solo las personas ya enlazadas), foto en una tabla propia con tope de tamaño y servida con ETag, y directorio con `atlas.equipo.ver`.
2. Lo mismo, pero trayendo por defecto a todos los usuarios miembro del tenant.
3. Suscripciones de cambio de Graph (webhooks de `users`) en lugar de una corrida diaria.
4. Guardar las fotos en S3 (MinIO en qa, Contabo en pdn).

## Decisión

Opción elegida: **1**.

### 1. Qué personas se traen (`Identidad:Personas:Origen`)

| Origen | Qué hace | Cuándo |
|---|---|---|
| `enlazadas` (**por defecto**) | Refresca solo a los empleados que Atlas ya tiene enlazados a Entra (`entra_oid`, es decir, que entraron alguna vez). No crea personas | Sin configurar nada; el directorio crece con los ingresos |
| `grupo` | Los miembros transitivos del grupo `Identidad:Personas:GrupoEmpleados` (object id). Crea a los nuevos, enlaza por correo a un empleado sin `oid` y refresca a todos | **Recomendado para qa y pdn**: un grupo de Entra con los empleados (p. ej. el de la licencia de Microsoft 365) |
| `tenant` | Todos los usuarios con `userType = Member` | Solo si el tenant no tiene cuentas técnicas con buzón |

En los tres casos: los invitados B2B (`userType = Guest`) nunca entran; solo se crea una persona si la cuenta está habilitada y tiene buzón (`mail`), y si su correo no es de otra persona de Atlas (p. ej. una externa); el enlace por correo sigue la regla del ingreso JIT (solo un empleado sin `oid` y nunca la cuenta de emergencia). Las enlazadas que el listado no trae se consultan una por una: un 404 es una cuenta borrada en Entra y cuenta como deshabilitada. Salir del grupo, por sí solo, no inactiva a nadie (un grupo mal editado no debe dejar a alguien por fuera); lo que inactiva es `accountEnabled = false` o la cuenta borrada.

### 2. La corrida (`SincronizadorPersonas`)

- **Candado**: `pg_try_advisory_lock` de sesión con su propia llave. Si otra réplica lo tiene, esta no espera ni reintenta. Una corrida programada no se repite si una exitosa ya empezó en la misma franja (10 min de margen por la deriva de los relojes); el estado de la última corrida queda en `plataforma_configuracion` (`identidad.sincronizacion_personas`, solo conteos).
- **Primero Graph, después la base**: se lee todo y se aplica en una transacción; si Graph falla a mitad, no queda nada a medias. Después de guardar, se cierran las sesiones de las que quedaron inactivas (orden de ATLAS-ADR-0022).
- **Datos**: nombre (`displayName`), cargo (`jobTitle`), área (`department`) y teléfono (`mobilePhone`, o el primero de `businessPhones`). Entra manda: lo que viene vacío queda vacío. El correo no se cambia (es la llave del enlace).
- **Estado**: una cuenta deshabilitada o borrada deja la persona `inactiva` con la marca nueva `inactiva_por_entra` y llama a `IInvalidadorSesiones` con el motivo `cuenta_deshabilitada_en_entra`. Al rehabilitarse, vuelve a `activa` **solo si** la inactivó la sincronización (una persona inactiva por otro motivo, p. ej. creada así por el importador de la HU 2.7, sigue igual). Una persona `suspendida` desde la administración nunca cambia de estado.
- **Freno de seguridad**: si una corrida fuera a deshabilitar a más de la mitad de las activas enlazadas (`FraccionMaximaDeshabilitadas`, y más de `MinimoFreno` = 3), no deshabilita a nadie, lo deja como `retenidas` en la auditoría y avisa en el log. Protege contra un registro de la app que apunta a otro tenant o un grupo vaciado por error.
- **Exclusiones**: la cuenta de emergencia (por su asignación `emergencia`) y los externos no entran en la consulta.

### 3. El trabajo (`TrabajoSincronizacionPersonas`)

- Corre cada día a `Identidad:Personas:Hora` (05:00 por defecto) en la hora de America/Bogotá, con `TimeProvider` (probado sin depender del reloj). `Identidad:Personas:Habilitada=false` apaga solo la programación. Sin Entra configurado no hace nada.
- Si Graph, la red, el token de aplicación (MSAL) o la base fallan, reintenta con esperas que se duplican (`Reintentos` = 4, desde `EsperaReintento` = 2 min: 2, 4, 8 y 16 min). Además, el cliente HTTP ya trae el manejador de resiliencia de ServiceDefaults (429 y 5xx con `Retry-After`). Si se acaban los intentos, queda la corrida `fallida` (solo el tipo de la excepción) y la siguiente es la del día siguiente o el botón.
- **«Sincronizar ahora»**: `POST /api/v1/plataforma/directorio/sincronizacion` (202) despierta el trabajo de la réplica que recibe la petición; corre en segundo plano (§5.1: más de ~1 s sale de la petición) y su resultado se lee con el `GET`. Exige `atlas.identidad.administrar` (solo superadmin; se amplió su descripción). No pide reautenticación: no cambia nada que Entra no diga ya. No se agrega un comando de consola: quien tiene `kubectl exec` puede usar la cuenta de emergencia y el botón.
- **Observabilidad**: un evento de auditoría por corrida (`identidad.personas_sincronizadas`, recurso `sincronizacion_personas`, actor sistema o la persona que la pidió) con disparo, origen, creadas, actualizadas, deshabilitadas, reactivadas, sin cambios, omitidas, retenidas y duración, sin datos personales; además el evento `identidad.sesiones_invalidadas` de cada persona inactivada. Métricas del medidor `Flit.Atlas.Identidad` (ServiceDefaults registra `Flit.Atlas.*`): `atlas.personas.sincronizacion.corridas` (por resultado), `.cambios` (por tipo) y `.duracion`. Log informativo con los conteos y advertencias en los reintentos y el freno.

### 4. La foto

- Se pide `photos/120x120/$value` (alcanza para los avatares y pesa pocos KB) y, si ese tamaño no existe, la original. Solo JPEG, PNG, GIF o WebP y con tope de `TamanoMaximoFoto` (100 KB por defecto, leído sin confiar en `Content-Length`); si no cumple, queda el avatar con iniciales.
- Se guarda **en la base**, en `plataforma_persona_foto` (`persona_id` PK con cascada, `contenido` bytea `@pii`, `tipo_contenido`, `hash` SHA-256, `actualizada_en`), aparte de la persona para que sus consultas no la carguen. `plataforma_persona.foto_hash` versiona la URL (`/api/v1/plataforma/personas/{id}/foto?v=<16 hex>`) y evita reescribirla si no cambió. Unas decenas de fotos de pocos KB no justifican un cliente S3 en la API (opción 4).
- `GET /api/v1/plataforma/personas/{personaId}/foto`: `atlas.equipo.ver`, solo empleados, `Cache-Control: private, max-age=604800` y ETag (el hash); con `If-None-Match` igual responde 304. Mismo origen: la CSP (`img-src 'self'`) no cambia.

### 5. Directorio y web

- `GET /api/v1/plataforma/directorio` (`q`, `inactivas`, `limite` ≤ 200) y `GET /api/v1/plataforma/directorio/{personaId}`, etiqueta «Directorio», con `atlas.equipo.ver`: nombre, correo, cargo, área, estado, foto y última sincronización de los empleados (nunca externos ni la cuenta de emergencia). El teléfono solo sale con `atlas.equipo.ver_contacto`; sin él viene vacío y `veContacto = false`. Viven bajo `/api/v1/plataforma` porque el módulo `equipo` del servidor aún no existe (llega con la HU 2.3 y montará `/api/v1/equipo`).
- `GET /api/v1/plataforma/yo` agrega `foto` y el buscador de la administración agrega `foto` a cada persona: el avatar del shell (encabezado, menú de la cuenta, hoja «Más») y el de «Asignación de roles» muestran la foto, con las iniciales como respaldo.
- PWA: la página `/equipo` pasa de «Próximamente» al directorio (búsqueda sin tildes, «Incluir inactivas», correo y teléfono como enlaces) y, con `atlas.identidad.administrar`, la tarjeta «Sincronización con Microsoft 365» con la última corrida, la próxima y el botón «Sincronizar ahora». Los responsables siguen pendientes (HU 2.3).

### 6. Datos y migración

Migración `SincronizacionPersonas` del `PlataformaDbContext`: columnas `plataforma_persona.inactiva_por_entra` (bool, falso), `foto_hash` (64) y `sincronizada_en` (timestamptz), y la tabla `plataforma_persona_foto`. La foto no lleva `organizacion_id` (solo hay fotos de empleados de la organización interna) y la lectura exige el permiso y que la persona sea un empleado visible por el predicado de organización.

## Consecuencias

### Positivas

- Ningún permiso de Graph nuevo, ningún paquete nuevo y nada que configurar para que funcione (origen `enlazadas`).
- Una cuenta deshabilitada en Entra pierde Atlas a más tardar en la corrida del día (o al pulsar el botón), con sus sesiones y tokens cortados.
- El freno y la regla «salir del grupo no inactiva» evitan que un error de configuración deje al equipo por fuera.

### Negativas

- Con el origen por defecto, el directorio solo muestra a quien ya entró: el líder debe configurar el grupo para que aparezca todo el equipo.
- Entre una deshabilitación en Entra y la corrida siguiente pueden pasar hasta 24 h; Entra ya impide un ingreso nuevo, pero una sesión abierta de Atlas sigue viva hasta la corrida (el refresco en cada petición del §6.1 no consulta Graph).
- Una persona rehabilitada en Entra no puede entrar hasta la siguiente corrida o el botón (el ingreso rechaza a las inactivas).
- Las fotos ocupan la base (≈ 5 KB × personas) y viajan en las copias de seguridad.
- El freno puede retener una baja masiva legítima; alguien debe revisarla y, si es real, ajustar `FraccionMaximaDeshabilitadas` o suspender a mano.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. Todo el tenant por defecto | Trae cuentas técnicas, buzones compartidos y salas con buzón como «empleados»; mejor que el líder lo elija a sabiendas |
| 3. Suscripciones de cambio de Graph | Exigen un endpoint público para las notificaciones, renovar la suscripción cada pocos días y validar su origen; no evitan la corrida diaria de reconciliación. Se puede agregar después para bajar la latencia de la inactivación |
| 4. Fotos en S3 | atlas-api no tiene cliente S3 ni buckets propios hasta F4; para decenas de fotos pequeñas sería una dependencia nueva sin ganancia. Si el volumen crece, se mueven a S3 sin cambiar la URL |

## Más información

- Configuración: `services/api/README.md` («Sincronización de personas») y `deploy/README.md` (pasos del líder).
- **Pasos del líder** (no requieren cambios de código): confirmar en el registro de la app de cada ambiente que `User.Read.All` y `GroupMember.Read.All` son permisos **de aplicación** con consentimiento de administrador (ya usados por `PerfilGraph`); para qa y pdn, elegir el grupo de Entra de los empleados y fijar en `flit-gitops` `Identidad__Personas__Origen=grupo` e `Identidad__Personas__GrupoEmpleados=<object id>` (opcional `Identidad__Personas__Hora`).
- Pendientes **[por verificar]**: si el refresco de la sesión debe consultar `accountEnabled` en Graph (latencia contra costo, §6.1); el grupo de empleados de cada ambiente; retención de las fotos de personas inactivas (Ley 1581); offline del directorio (HU 2.12).
- Relacionados: ATLAS-ADR-0004, ATLAS-ADR-0022, ATLAS-ADR-0023, ATLAS-ADR-0026, ATLAS-ADR-0027.
