---
id: ATLAS-ADR-0049
titulo: Endurecimiento de la identidad en F4 — contadores propios y atómicos del segundo factor, límite por cuenta, tiempos constantes con cola de correo en memoria, último paso TOTP por usuario, recuperación con aviso y riesgo residual del buzón, enlace de empleados solo por oid, roles de grupos recalculados a diario y proxies confiables obligatorios
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §6 y §12, docs/seguridad/revision-f4.md (F4-SEC-01 a 08, 13, 14, 18, 19 y 21), ATLAS-ADR-0022, ATLAS-ADR-0025, ATLAS-ADR-0027, ATLAS-ADR-0030, ATLAS-ADR-0048]
---

# ATLAS-ADR-0049 · Endurecimiento de la identidad en F4

## Contexto y problema

La revisión de seguridad de F4 (`docs/seguridad/revision-f4.md`, §6) encontró en el módulo de identidad un hallazgo alto
y varios medios que hay que cerrar antes de abrir el acceso a externos en pdn (§12 del prompt maestro). Hechos verificados
en el código el 24/10/2026, antes de este cambio:

- **F4-SEC-01 (alta).** `VerificadorContrasena` reiniciaba el contador de fallos de Identity también con la contraseña
  correcta, y el TOTP sumaba al mismo contador: quien conoce la contraseña probaba 9 códigos, volvía a la contraseña y
  seguía. El paso del TOTP no se invalidaba con los fallos y el límite era solo por IP.
- **F4-SEC-02.** El fallo se sumaba con `AccessFailedAsync` de Identity, que lee, suma y escribe con el sello de
  concurrencia; en una ráfaga concurrente ganaba una escritura y el resultado se ignoraba: 20 fallos no bloqueaban.
- **F4-SEC-03.** Una cuenta solo con passkey sumaba fallos de contraseña en `/entrar/local` y quedaba bloqueada, también
  para su passkey (denegación dirigida hasta 24 h).
- **F4-SEC-04.** `/recuperacion` enviaba el correo dentro de la petición solo si la cuenta existía (cientos de ms de
  diferencia), y `/entrar/local` no calculaba el hash para cuentas bloqueadas o sin contraseña.
- **F4-SEC-05.** La marca anti-reutilización del TOTP (Redis) usaba el texto sin espacios y el verificador quitaba
  además los guiones: «123-456» repetía un código ya usado. El código del registro de la clave no se marcaba.
- **F4-SEC-06 (diseño).** La recuperación por correo reemplaza todos los factores con solo el enlace del buzón y no
  avisaba al titular.
- **F4-SEC-07.** El primer ingreso con Entra enlazaba un `oid` nuevo, **por correo**, con un empleado existente sin `oid`
  y heredaba sus roles manuales; el correo del token (`email`, `preferred_username`, `upn`) cambia y se recicla.
- **F4-SEC-08.** Los roles que vienen de grupos de Entra solo se recalculaban en el ingreso interactivo: quien salía del
  grupo de `atlas.superadmin` conservaba el rol con su sesión (hasta 7 días) y sus refresh (hasta 30 días).
- **F4-SEC-13.** Sin `Atlas__Proxy__RedesConfiables` en qa o pdn atlas-api arrancaba igual: todas las peticiones parecían
  venir del proxy y los límites por IP eran uno solo para todos.
- Bajos: invitaciones sin reautenticación y un `ext.admin_organizacion` que deshacía con «renovar» una suspensión de FLIT
  (F4-SEC-14); la reautenticación con Entra no verificaba `auth_time` (F4-SEC-18); un externo estiraba su sesión de 24 h
  con refresh de 30 días (F4-SEC-19); los dos eventos del comando de emergencia no llegaban a Loki (F4-SEC-21).

## Factores de decisión

- Nada de criptografía propia (A-03): hashing, TOTP, passkeys y tokens siguen siendo de ASP.NET Core Identity y
  OpenIddict.
- ASVS 5.0 nivel 2, V6.3 (bloqueo y límites), V6.4 (recuperación), V6.5 (MFA de un solo uso), V6.8 (proveedor de
  identidad) y V7.4 (terminación de sesión).
- Respuestas indistinguibles (mensaje, estado **y** tiempo) exista o no la cuenta.
- No romper el primer ingreso de los empleados que la sincronización con Graph ya sembró con su `oid` (ATLAS-ADR-0030)
  ni pedir permisos nuevos de Graph.
- El enlace de recuperación no se persiste nunca (ATLAS-ADR-0025): ni en la base ni en una cola durable.

## Opciones consideradas

1. Contadores: (a) uno solo de Identity sin reiniciarlo con la contraseña; (b) dos contadores, el del segundo factor en
   una tabla propia, con incremento atómico en SQL; (c) candado por usuario en Redis alrededor de `AccessFailedAsync`.
2. Límite por cuenta: (a) partición del `RateLimiter` por correo (exige leer el cuerpo antes del enrutamiento); (b)
   contador en Redis por SHA-256 del correo, en el endpoint.
3. Correo de recuperación fuera de la petición: (a) outbox en la base; (b) `Response.OnCompleted`; (c) cola en memoria
   (`Channel`) con un `BackgroundService`, como `DisparadorAvisos` (ATLAS-ADR-0046).
4. Anti-reutilización del TOTP: (a) marca por texto del código en Redis, con el formato normalizado; (b) último paso de
   tiempo aceptado por usuario en la base (RFC 6238 §5.2).
5. Recuperación: (a) exigir un código de recuperación o un factor vigente; (b) aprobación de un administrador; (c)
   periodo de espera; (d) aceptar el riesgo del buzón con controles compensatorios y aviso al titular.
6. Enlace de empleados con un `oid` nuevo cuyo correo ya es de otra persona: (a) negar con evento y mensaje; (b) crear
   una persona nueva sin roles; (c) enlazar solo si la persona no tiene roles.
7. Roles de grupos: (a) recalcularlos en cada refresh y cada petición; (b) en la sincronización diaria con
   `transitiveMemberOf`, invalidando sesiones si cambian; (c) bajar el tope absoluto de la sesión de empleados.

## Decisión

### 1. Dos contadores, atómicos (F4-SEC-01, 02 y 03)

Opción 1b. Los fallos de **contraseña** siguen en `identidad_usuario.access_failed_count` (Identity) y los del **segundo
factor** (TOTP y códigos de recuperación) van a la tabla nueva `identidad_segundo_factor` (`usuario_id`, `fallos`,
`ultimo_paso_totp`), aparte para que el `UpdateAsync` de Identity, que reescribe toda la fila del usuario, nunca pise
estos valores. Cada fallo suma con un `UPDATE … SET n = n + 1 … RETURNING n` (o `INSERT … ON CONFLICT DO UPDATE …
RETURNING`) y bloquea exactamente quien llega a 10 (`IntentosAntesDelBloqueo`); los que llegan detrás responden
bloqueado sin repetir el bloqueo. Las escrituras sobre `identidad_usuario` rotan `concurrency_stamp`: un `UpdateAsync` de
Identity con la fila vieja falla en lugar de deshacer el bloqueo. El bloqueo sigue siendo el `lockout_end` de Identity,
progresivo (15 min, doble por cada bloqueo de las últimas 24 h, tope 24 h).

**La contraseña correcta no reinicia nada**: los dos contadores vuelven a cero solo cuando el ingreso termina con todos
sus factores (segundo factor o passkey) o al completar la recuperación (`VerificadorContrasena.IngresoCompletadoAsync`).

Una cuenta **sin contraseña** (solo passkey) no suma fallos de contraseña: responde el mismo 401 genérico, con el hash
ficticio, y queda `identidad.inicio_fallido` con `motivo = sin_contrasena`. Así nadie la bloquea escribiendo su correo.

### 2. Paso del segundo factor de 5 intentos y límite por cuenta atacada (F4-SEC-01)

- Tras **5 fallos** del segundo factor sobre el mismo paso (contador en Redis junto al paso, `FallosPorPaso`), el paso se
  borra y se responde `401 paso_vencido` («Demasiados códigos errados»): hay que volver a la contraseña, que no reinicia
  el contador. Con 10 fallos del segundo factor la cuenta se bloquea: a lo sumo dos pasos.
- Opción 2b: `LimiteIngresoPorCuenta` cuenta en Redis (compartido entre réplicas) los intentos de `/entrar/local` y
  `/entrar/local/segundo-factor` por **SHA-256 del correo normalizado** (Redis no guarda el correo), exista o no la
  cuenta, con ventana fija: **20 intentos cada 15 minutos** (`Identidad:LimiteTasa:IngresoPorCuenta` y
  `…:VentanaPorCuenta`). Pasado el tope responde el mismo 429 problem+json con `Retry-After` de los demás límites y
  registra `identidad.limite_tasa` una vez por ventana. Se suma a los límites por IP (ATLAS-ADR-0022).
- **No** se agrupa IPv6 por /64 en esta entrega: el límite por cuenta ya frena el ataque repartido entre IPs, y agrupar
  cambiaría el límite de oficinas con IPv6 compartido **[por decidir con el líder]**.

### 3. Tiempos constantes (F4-SEC-04)

- Opción 3c: la recuperación pedida por la persona **encola** el correo en `ColaCorreosIdentidad` (`Channel` acotado a
  1000, en memoria) y responde 202; `TrabajoCorreosIdentidad` lo envía y, si no sale, registra
  `identidad.correo_no_enviado`. El enlace vive solo en memoria de la réplica: si se cae antes de enviarlo, la persona lo
  pide otra vez. La outbox (3a) persistiría el enlace; `OnCompleted` (3b) ata el envío al ciclo de la petición y no
  sobrevive a un error del envío sin registrarlo. El restablecimiento forzado por un administrador sigue enviando en la
  petición (responde si el correo salió) y no es enumerable.
- `/entrar/local` calcula el PBKDF2 ficticio (`HashFicticio`, con el hasher y el costo reales) siempre que no haya una
  contraseña que comparar: correo desconocido, empleado, cuenta bloqueada o sin contraseña.
- La prueba compara medianas de 5 peticiones intercaladas con un margen de 200 ms frente a un envío simulado de 800 ms.

### 4. Último paso TOTP aceptado por usuario (F4-SEC-05)

Opción 4b. El código se normaliza **una sola vez** (`CodigosTotp.Normalizar`: sin espacios y exactamente 6 dígitos
ASCII; otro formato, como `123-456`, es un fallo). Lo valida Identity (`VerifyTwoFactorTokenAsync`, ±2 pasos de 30 s);
si lo acepta, se ubica su paso de tiempo y se guarda en `identidad_segundo_factor.ultimo_paso_totp` con un `INSERT … ON
CONFLICT DO UPDATE … WHERE ultimo_paso_totp < nuevo`: un código igual **o de un paso anterior** al último aceptado no
sirve (RFC 6238 §5.2), en todos los caminos: el ingreso, la aceptación de la invitación, la recuperación y la activación
de la cuenta de emergencia (el código con que se confirma la clave tampoco sirve después para entrar; al registrar una
clave nueva el paso se reemplaza). Reemplaza la marca en Redis por texto.

Ubicar el paso exige recalcular el HOTP de RFC 4226 con HMAC-SHA1 de la BCL (`CodigosTotp.Paso`), porque el
`AuthenticatorTokenProvider` de Identity no lo expone. **No es un verificador propio**: solo se consulta después de que
Identity aceptó el código, con la misma primitiva y el mismo algoritmo que Identity usa por dentro, comparación en tiempo
constante y falla cerrada (si no encuentra el paso en ±3, el código se rechaza). Se pide al líder aceptarlo como
excepción acotada a A-03 o, si no, volver a 4a (marca por texto normalizado), que cierra F4-SEC-05 pero no los códigos de
un paso anterior.

### 5. Recuperación: riesgo residual del buzón, aceptado con controles (F4-SEC-06)

Opción 5d, **que el líder debe aceptar**. Se mantiene que el enlace del buzón basta para recuperar la cuenta, con estos
controles (verificados en el código y en `Pentest_recuperar_la_cuenta_con_el_buzon_avisa_al_titular…`):

- **Sin factor nuevo no hay sesión**: completar exige registrar una passkey o contraseña más TOTP (con el código
  confirmado); sin él, 400 y sin cookie. Ya era así.
- Se **retiran todos los factores** anteriores (passkeys, contraseña, clave TOTP y códigos) y se generan códigos nuevos;
  se quita el bloqueo y se reinician los contadores.
- Se **cierran todas las sesiones** del BFF y se **revocan los tokens y autorizaciones** de OpenIddict de la persona
  (`IInvalidadorSesiones`, sello de seguridad); el enlace es de un solo uso (ligado al sello), vence en 30 min y hay a lo
  sumo 3 por hora.
- **Aviso al titular** («Se recuperó tu cuenta de Flit Atlas»): cuándo, qué factor nuevo y que se cerraron las sesiones,
  y qué hacer si no fue él. **Sin botón ni enlaces**: nada que se deshaga con un GET ni un enlace que imitar en un correo
  falso. Sale por la misma cola en memoria.

**Riesgo residual:** quien controle el buzón del titular controla la cuenta, y el aviso llega a ese mismo buzón (el
atacante puede borrarlo). Se acepta porque la recuperación existe justamente para quien perdió sus factores (5a la
anularía o la llevaría a soporte manual), la aprobación de un administrador (5b) no escala con clientes y proveedores y
deja a la persona sin acceso por días, y un periodo de espera (5c) sin un segundo canal no agrega una verificación real.
Mitigan además: el acceso de un externo está limitado a su organización, alcance y vigencia; la auditoría registra
`recuperacion_completada`, `factores_retirados` y lo que haga la sesión; el runbook tiene el procedimiento si el titular
avisa. Pendiente **[por decidir con el líder]**: avisar también al administrador de la organización o a FLIT en cada
recuperación de un proveedor.

### 6. Empleados de Entra enlazados solo por `oid` (F4-SEC-07)

- El ingreso con Microsoft busca la persona **solo por `oid`** (el `tid` ya se valida en `ValidadorCuentaEntra`). Si el
  `oid` es nuevo y el correo del token ya es de otra persona (con o sin `oid`), opción 6a: **se niega**, queda
  `identidad.vinculo_por_correo_rechazado` (sobre la persona cuyo correo traía la cuenta) y la PWA muestra «Tu cuenta de
  Microsoft aún no está vinculada a tu perfil de Atlas» (`/?error=cuenta_por_vincular`). 6b no es posible sin cambiar el
  modelo: `plataforma_persona.correo` es único; con otro correo crearía un duplicado confuso. Negar falla cerrado.
- Quien enlaza por correo a las personas precargadas es la **sincronización con Graph** (ATLAS-ADR-0030, origen `grupo`
  o `tenant`), con el token de aplicación y antes del primer ingreso; así el primer ingreso de una persona sembrada ya
  la encuentra por `oid` (prueba en `Pentest_un_oid_nuevo_de_Entra_no_hereda…`). La sincronización **no** enlaza por
  correo a una persona que ya tiene roles asignados (6c): la omite y registra el mismo evento, para que un administrador
  confirme a quién pertenece la cuenta (procedimiento en el runbook).

### 7. Roles de grupos recalculados a diario (F4-SEC-08)

Opción 7b. La sincronización diaria lee `GET /users/{oid}/transitiveMemberOf/microsoft.graph.group` (la misma consulta
del ingreso, con el permiso de aplicación **`GroupMember.Read.All` que ya se usa**: no hace falta uno nuevo) para cada
empleado enlazado y habilitado, siempre que algún grupo dé roles o alguien tenga roles de un grupo; recalcula las
asignaciones `grupo_entra` con la misma lógica del ingreso (`RolesDeGruposEntra`, con su auditoría) y, si cambian,
**invalida las sesiones** de esa persona (sello de seguridad: sesiones, Bearer y refresh). Si Graph falla, la corrida
falla entera y se reintenta, sin cambios a medias. El desfase máximo pasa a ser el de la corrida diaria (más el reintento),
en lugar de los 7 días de la sesión o los 30 del refresh. 7a multiplicaría las llamadas a Graph y ataría el ingreso a
su disponibilidad; 7c (bajar el tope de 7 días) queda **[por decidir con el líder]**.

### 8. Proxies confiables obligatorios en qa y pdn (F4-SEC-13)

atlas-api no arranca en qa ni pdn sin `Atlas__Proxy__RedesConfiables`; el error nombra la variable y explica el efecto.
En local y dev basta el loopback. Documentado en `deploy/runbook-atlas.md` §4.

### 9. Bajos de identidad

- **F4-SEC-14.** Crear, suspender, renovar y revocar invitaciones exigen reautenticación (como el resto de acciones
  sensibles, ATLAS-ADR-0022). Un `ext.admin_organizacion` no reactiva con «renovar» una invitación que suspendió alguien
  de otra organización (FLIT) ni a una persona suspendida desde la administración: 403 `suspension_de_flit`.
- **F4-SEC-18.** La reautenticación con Entra pide `prompt=login` **y `max_age=0`** (OIDC obliga entonces a devolver
  `auth_time`) y el aprovisionador exige `auth_time` presente y de hace a lo sumo 5 minutos; si no, el ingreso se niega
  (`/?error=reautenticacion_fallida`, motivo `reautenticacion_no_verificada`). Requiere que el registro de la app de cada
  ambiente emita `auth_time` (ya listado en el runbook) **[por verificar en Entra]**.
- **F4-SEC-19.** El refresh de un externo no pasa del tope absoluto de su sesión (`Identidad:Sesion:AbsolutaExternos`,
  24 h): la concesión vence (`concesion_vencida`); los empleados conservan los 30 días.
- **F4-SEC-21.** El comando de emergencia ya no quita todos los logs: deja solo el proveedor de OpenTelemetry (OTLP al
  colector y de ahí a Loki) filtrado a las advertencias de `RegistroSeguridad`, que son los eventos
  `identidad.emergencia.*` con su tipo y su id, y vacía el exportador al terminar. La consola sigue mostrando solo la
  salida del comando (el enlace).

## Consecuencias

### Positivas

- La fuerza bruta del segundo factor queda acotada a 10 intentos por bloqueo (como la contraseña), los fallos
  concurrentes no se pierden y una cuenta con passkey no se bloquea desde fuera.
- Ingreso y recuperación responden igual en mensaje, estado y tiempo exista o no la cuenta.
- Un código TOTP no se acepta dos veces en ningún camino ni con otro formato.
- Salir de un grupo de Entra o el reciclaje de un correo ya no dejan roles altos vivos.
- Los cambios no tocan el contrato de los DTO; solo descripciones del OpenAPI y la reautenticación de cuatro endpoints.

### Negativas

- Una migración nueva (`identidad_segundo_factor`) y SQL a mano sobre `identidad_usuario` (columnas de Identity): si
  una versión futura de Identity cambia el esquema, hay que revisarlo (lo cubren `SeguridadTests` y las pruebas de
  penetración).
- Un correo de recuperación encolado se pierde si la réplica se cae antes de enviarlo (la persona lo pide de nuevo).
- La sincronización diaria hace una consulta más a Graph por empleado enlazado cuando hay grupos con roles.
- Un empleado precargado con roles y sin `oid` no entra hasta que un administrador lo vincule.
- Un usuario legítimo que pasa el límite por cuenta (20 en 15 min) o cuyo correo ataca alguien espera la ventana; es
  el mismo efecto que ya tenía el bloqueo.
- `CodigosTotp.Paso` recalcula el HOTP con la BCL (ver §4): excepción acotada a A-03 que el líder debe aceptar.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 1a. Un solo contador sin reinicio con la contraseña | Sigue mezclando contraseña y segundo factor y no corrige la escritura no atómica de Identity |
| 1c. Candado en Redis alrededor de `AccessFailedAsync` | Otra pieza que falla aparte; el `UPDATE … RETURNING` es atómico en la propia fila |
| 2a. Partición del limitador por correo | El limitador corre antes de leer el cuerpo; leerlo ahí duplica el parseo y adelanta trabajo a peticiones que se rechazan |
| 3a. Outbox en la base | Persistiría el enlace de un solo uso (ATLAS-ADR-0025) |
| 3b. `Response.OnCompleted` | El fallo del envío no queda registrado de forma fiable y retiene recursos de la petición |
| 4a. Marca por texto normalizado | No rechaza un código de un paso anterior aún vigente |
| 5a. Código de recuperación o factor vigente | Anula la recuperación para quien perdió sus factores |
| 5b. Aprobación de un administrador | No escala y deja a la persona sin acceso por días |
| 5c. Periodo de espera | Sin un segundo canal no verifica nada |
| 6b. Persona nueva sin roles | El correo es único en `plataforma_persona` |
| 7a. Recalcular grupos en cada refresh o petición | Ata el uso a la disponibilidad de Graph y multiplica las llamadas |

## Más información

- Pruebas: `services/api/tests/Flit.Atlas.Api.Tests/Seguridad/PentestCuentasExternasTests.cs` (F4-SEC-01 a 06),
  `PentestSesionesTests.cs` (07 y 18), `SincronizacionPersonasTests.Recalcula_los_roles_de_los_grupos…` (08 y 07 en la
  sincronización), `ProxyTests` (13), `CuentasExternasTests.Un_admin_de_organizacion_no_reactiva…` (14),
  `TokensTests.El_refresh_de_un_externo…` (19), `EmergenciaTests.Los_logs_del_comando…` (21), `SeguridadTests`.
- Sin hacer aquí (otros hallazgos bajos de identidad): F4-SEC-15 (409 distinguibles para el admin de organización),
  F4-SEC-16 (`FOR UPDATE` al revocar), F4-SEC-17 (`TomarAsync` en los pasos), F4-SEC-20 (consentimiento implícito) y la
  agrupación de IPv6 por /64.
- ATLAS-ADR-0022 (bloqueo y límites), ATLAS-ADR-0025 (cuentas externas y recuperación), ATLAS-ADR-0027 (emergencia),
  ATLAS-ADR-0030 (sincronización con Graph), ATLAS-ADR-0048 (endurecimiento de F4).
