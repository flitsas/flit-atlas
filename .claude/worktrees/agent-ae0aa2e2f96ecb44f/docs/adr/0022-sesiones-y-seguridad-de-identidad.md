---
id: ATLAS-ADR-0022
titulo: Sesiones y seguridad de la identidad — sello de seguridad, reautenticación, bloqueo progresivo, límite de tasa y eventos de seguridad
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-03, §6, §6.3, §9 y §12, HU 1.3b, ATLAS-ADR-0004 §2, §6, §9 y §10]
---

# ATLAS-ADR-0022 · Sesiones y seguridad de la identidad

## Contexto y problema

La HU 1.3 dejó la sesión del BFF en el servidor (`identidad_sesion`, cookie `__Host-atlas` con solo el id cifrado). La HU 1.3b pide, sobre esa base: «dispositivos conectados» con cierre remoto, invalidar las sesiones al cambiar roles, suspender o vencer un acceso (CA: suspender corta la sesión en menos de 1 minuto), reautenticación para acciones sensibles, bloqueo progresivo (CA: 10 intentos fallidos bloquean la cuenta), limitación de tasa en todos los endpoints de identidad y eventos de seguridad en la auditoría (CA: cada evento queda auditado). ATLAS-ADR-0004 fijó el qué; este ADR decide el cómo y los puntos que ATLAS-ADR-0004 dejaba abiertos.

Hechos verificados en el código: los empleados entran solo por Entra ID (sin contraseña en Atlas); las cuentas locales de los externos llegan en la HU 1.5; los permisos finos llegan en la HU 1.4 (en otra rama); el manejador de cookies de ASP.NET Core, cuando el navegador ya trae una sesión válida, **renueva** esa misma sesión al volver a entrar en lugar de crear otra.

## Factores de decisión

- Nada de criptografía propia (A-03): ASP.NET Core Identity, Data Protection y `Microsoft.AspNetCore.RateLimiting`.
- OWASP ASVS nivel 2: V2.2 (límite y bloqueo), V3.2.1 (sesión nueva al autenticarse), V3.3 (invalidación en el servidor), V3.7 (reautenticación).
- Revocación inmediata y comprobable con una prueba automatizada.
- No crear un ingreso con contraseña público antes de la HU 1.5.
- Tocar lo mínimo de los archivos compartidos con las HU paralelas (`Program.cs`, `Directory.Packages.props`, el modelo de Plataforma).

## Opciones consideradas

1. **Sello de seguridad de Identity guardado en cada sesión** y comparado en cada petición, más revocación explícita de las filas; reautenticación por `auth_time` del principal; bloqueo de Identity con retroceso progresivo; límite de tasa por IP y por usuario en memoria; eventos en `identidad_evento_seguridad` y en `plataforma_auditoria` en una sola transacción.
2. Solo revocar filas de `identidad_sesion` (sin sello).
3. Validar el sello con `SecurityStampValidator` de Identity (intervalo configurable) y la cookie de Identity.
4. Límite de tasa distribuido en Redis con una librería de terceros.

## Decisión

Opción elegida: **1**.

### 1. Sello de seguridad

- `identidad_sesion` guarda `sello_seguridad` (el `SecurityStamp` del usuario al entrar) e `ip` (`inet`, `@pii`, nunca en los logs). `AlmacenSesiones.RetrieveAsync` lo compara con el vigente en **cada** petición (una consulta con `JOIN` al usuario). Si difiere, revoca la fila (`sello_de_seguridad`) y responde como sin sesión: el corte es inmediato, no cada N minutos (por eso se descarta la opción 3). La caché de 60 s que mencionaba ATLAS-ADR-0004 §6.3 no se agrega: hoy no hace falta y retrasaría el corte.
- Punto de extensión: **`IInvalidadorSesiones.InvalidarSesionesAsync(personaId, motivo, actorPersonaId, ct)`** (`Flit.Atlas.Identidad.Seguridad`, registrado como scoped). Cambia el sello con `UserManager.UpdateSecurityStampAsync`, revoca las filas activas y registra `identidad.sesiones_invalidadas`. Motivos en `MotivosInvalidacion` (`cambio_de_roles`, `suspension`, `vencimiento`, `cierre_administrativo`, `cambio_de_credenciales`). La HU 1.4 lo llama **después** de guardar un cambio de roles o al vencer una asignación.
- Orden contra carreras: el ingreso lee el sello **antes** de la última comprobación del estado de la persona; la suspensión guarda el estado **antes** de cambiar el sello. Así, un ingreso simultáneo o ve la persona suspendida o queda con el sello viejo. Hay una prueba con ocho ingresos y una suspensión a la vez.
- Se descarta la opción 2: el sello también cubre sesiones que se creen mientras se revocan las filas.

### 2. Sesión nueva en cada ingreso

En las rutas que inician sesión (retorno de Entra y proveedor simulado) un middleware aparta la cookie de la petición antes de la autenticación, para que el ingreso cree una fila nueva; la anterior se revoca como `reemplazada_por_ingreso`. Sin esto, una cookie robada seguiría valiendo después de que su dueño vuelva a entrar (ASVS V3.2.1). La cookie se lee con el `TicketDataFormat` del propio esquema (Data Protection).

### 3. Dispositivos conectados y administración

- `GET /api/v1/identidad/sesiones` (marca la actual), `DELETE /api/v1/identidad/sesiones/{id}` (la actual equivale a salir) y `POST /api/v1/identidad/sesiones/cerrar-otras`.
- `POST /api/v1/identidad/admin/personas/{personaId}/sesiones/cerrar`, `…/suspender` y `…/reactivar`.
- **Autorización provisional**: la política `PoliticasIdentidad.Administrar` (`identidad.administrar`) exige hoy el rol `atlas.superadmin`. La HU 1.4 la redefine con el mismo nombre (otra `AddPolicy` reemplaza la anterior) para exigir el permiso del manifiesto, sin tocar los endpoints. La HU 1.4 lo resolvió con el permiso `atlas.identidad.administrar` (ATLAS-ADR-0023).

### 4. Reautenticación

- Filtro reutilizable `.RequiereReautenticacion()` (endpoint o grupo): exige que el `auth_time` de la sesión tenga menos de `OpcionesSesion.VentanaReautenticacion` (10 min, ATLAS-ADR-0004 §6.4). Se aplica a cerrar sesiones de otros dispositivos, a cerrar las de otra persona, a suspender y a reactivar.
- Respuesta para la PWA: **401** `application/problem+json` con `codigo = "reautenticacion_requerida"` y `maxEdadSegundos`, más `WWW-Authenticate: Atlas error="reautenticacion_requerida", max_age="600"`. La sesión sigue viva: la PWA lleva a `GET /api/v1/identidad/entrar/microsoft?reautenticar=true&volver=<ruta>`, que manda `prompt=login` a Entra.
- `auth_time` sale del claim opcional `auth_time` del token de Entra; si no viene, del momento del ingreso **[por verificar: habilitar el claim opcional `auth_time` en el registro de la app de cada ambiente]**. Sin ese claim, un ingreso silencioso por SSO cuenta como reciente; `prompt=login` en la reautenticación sí obliga a escribir las credenciales.

### 5. Bloqueo progresivo

- El bloqueo es el de ASP.NET Core Identity: `MaxFailedAccessAttempts = 10`, `DefaultLockoutTimeSpan = 15 min`, `AllowedForNewUsers = true`.
- `VerificadorContrasena.VerificarAsync` es la puerta que la HU 1.5 debe usar para las contraseñas de los externos: no compara la contraseña si la cuenta está bloqueada, suma el fallo con `AccessFailedAsync` y, al bloquearse, duplica la duración por cada bloqueo de las últimas 24 h (15 min, 30 min, 1 h… hasta 24 h).
- **No se crea un endpoint de ingreso con contraseña** en esta HU: hoy no hay cuentas locales y un endpoint sin MFA ni invitación sería superficie de ataque sin uso. La prueba del CA se hace a nivel de servicio con `UserManager` (10 fallos ⇒ bloqueada). Una cuenta bloqueada tampoco entra por Entra ni por el simulado.

### 6. Limitación de tasa

- `Microsoft.AspNetCore.RateLimiting` (del framework, sin paquetes nuevos), ventana fija configurable en `Identidad:LimiteTasa`: ingresos 20/min por IP, rutas de identidad 120/min por IP y 60/min por usuario. Cubre `/api/v1/identidad/**`, `/api/auth/**` (retorno de Entra) y `/api/v1/plataforma/yo`.
- La IP es la del cliente que deja `UseForwardedHeaders` (solo proxies de `Atlas:Proxy:RedesConfiables`). El middleware va antes de `UseAuthentication` para cubrir el retorno de Entra; para limitar por usuario lee la sesión de esas rutas (el manejador guarda el resultado y no repite la consulta).
- Responde **429** con `Retry-After` y problem+json `codigo = "limite_de_tasa"`. El evento `identidad.limite_tasa` se registra una vez por cliente y ventana, para que un ataque no inunde la auditoría.
- Los contadores viven en memoria de cada réplica (con dos réplicas el límite efectivo es el doble). Un limitador distribuido en Redis (opción 4) queda para cuando haya externos en pdn **[por decidir antes de F4]**. El límite más estricto para externos (§12) llega con la HU 1.5.

### 7. Eventos de seguridad

- Tabla `identidad_evento_seguridad` (§9), de solo inserción como `plataforma_auditoria` (sin `UPDATE`, `DELETE` ni `TRUNCATE` para el dueño y trigger que los rechaza): tipo, usuario, persona, sesión, actor, IP (`@pii`), navegador resumido y detalle en JSON.
- `RegistroSeguridad.RegistrarAsync` escribe el evento y su fila de `plataforma_auditoria` (`identidad.<tipo>`) **en la misma transacción**, por la conexión de Plataforma (no se toca el modelo de Plataforma).
- Tipos (`TiposEventoSeguridad`): `inicio_sesion`, `inicio_rechazado`, `inicio_fallido`, `cuenta_bloqueada`, `cierre_sesion`, `cierre_remoto`, `sesiones_invalidadas`, `persona_suspendida`, `persona_reactivada`, `reautenticacion`, `reautenticacion_requerida`, `limite_tasa`. Los de MFA, cambio de factor, invitación y restablecimiento llegan con la HU 1.5.

## Consecuencias

### Positivas

- Suspender, cambiar roles o cerrar desde la administración corta la sesión en la siguiente petición, sin esperar un intervalo.
- La HU 1.4 y la HU 1.5 reciben puntos de extensión con nombre (`IInvalidadorSesiones`, `PoliticasIdentidad.Administrar`, `VerificadorContrasena`, `.RequiereReautenticacion()`).
- Cada evento de seguridad deja rastro inmutable y consultable por usuario.

### Negativas

- Cada petición autenticada lee la sesión con el usuario (una consulta); con mucho tráfico habrá que medir y, si hace falta, cachear con invalidación por sello.
- Las sesiones abiertas antes de la migración quedan con sello vacío: cada persona entra de nuevo una vez.
- La IP en `identidad_sesion` y en los eventos es un dato personal: requiere la política de retención de la Ley 1581 **[por definir con el líder]**.
- Los límites de tasa son por réplica.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Solo revocar filas | Una sesión creada en medio de la revocación quedaría viva |
| `SecurityStampValidator` de Identity | Revalida cada intervalo (30 min por defecto), no en cada petición, y está atado a la cookie de Identity, que Atlas no usa |
| Limitador distribuido de terceros | Paquete nuevo sin necesidad en el MVP interno; se reevalúa antes de abrir a externos |
| Endpoint de ingreso con contraseña ahora | No hay cuentas locales hasta la HU 1.5; sería superficie sin uso |

## Más información

- ATLAS-ADR-0004 (identidad), ATLAS-ADR-0008 (BFF), ATLAS-ADR-0016 (contrato de módulo), ATLAS-ADR-0021 (contrato OpenAPI).
- Pendientes: claim opcional `auth_time` en Entra; nombre del permiso de administración (HU 1.4); límite distribuido y límites para externos (HU 1.5 / F4); retención de IPs.
