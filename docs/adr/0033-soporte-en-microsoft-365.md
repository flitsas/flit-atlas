---
id: ATLAS-ADR-0033
titulo: Soporte en Microsoft 365 — calendario compartido por Graph con marca de pendiente en la transacción y trabajo con reintentos, aviso en Teams por un flujo de Workflows y feeds .ics con token guardado como hash
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §5.1, §6, §8.3, §8.9, §9, §12 y §15, HU 2.10, ATLAS-ADR-0016, ATLAS-ADR-0018, ATLAS-ADR-0026, ATLAS-ADR-0029, ATLAS-ADR-0030, ATLAS-ADR-0032]
---

# ATLAS-ADR-0033 · Soporte en Microsoft 365

## Contexto y problema

La HU 2.10 pide llevar la rotación del soporte de los sábados (ATLAS-ADR-0029) a Microsoft 365: un evento por turno en un calendario compartido «Soporte TI sábados» con la persona como asistente (para que aparezca en su Outlook y en Teams), que se actualice y se cancele cuando el turno cambia; un aviso en un canal de Teams; y feeds .ics personales y de equipo con token revocable. Criterio de aceptación: aprobar un cambio de turno mueve el evento del Outlook de una persona al de la otra en menos de un minuto. El §8.3 marca el aviso de Teams como **[verificar el mecanismo vigente]**: los conectores de webhook entrante de Office 365 se retiraron.

Hechos verificados antes de decidir (23/10/2026):

- `PerfilGraph`, `DirectorioGraph` y `EnviadorGraph` ya llaman a Graph con `HttpClient` directo y el **token de aplicación** de MSAL (`IAuthorizationHeaderProvider`) que da Microsoft.Identity.Web en la identidad. Un módulo no puede referenciar la identidad (ATLAS-ADR-0016, lo prueba `ArquitecturaModulosTests`).
- La outbox de Plataforma (ATLAS-ADR-0018) publica en RabbitMQ; atlas-api todavía no consume nada de la cola y las pruebas de la API no levantan un broker. Los cambios de turno ocurren en cinco lugares (turnos a mano, estado, reemplazo, generación, importación y aprobación de cambios), y la generación y el importador **borran** turnos generados.
- **Conectores de Office 365 en Teams**: Microsoft los desactivó progresivamente del 18 al 22 de mayo de 2026; «después de esas fechas los conectores dejan de funcionar». El reemplazo indicado son los **Workflows de Teams** (Power Automate) con la plantilla «Publicar en un canal cuando se recibe una solicitud de webhook»; se recomiendan tarjetas adaptables; los mensajes salen con la identidad del bot de Workflows; los canales privados ya se pueden elegir (MC1181996).
- **Graph `chatMessage` en canales**: con permisos de aplicación solo se admite para **migración** (`Teamwork.Migrate.All`, equipos en estado de migración, con posibles cobros); para publicar de verdad hace falta un permiso **delegado** (`ChannelMessage.Send`) con una persona con sesión. No sirve para un trabajo en segundo plano.
- **Calendario**: `POST /users/{buzón}/events` crea el evento en el buzón organizador y Exchange envía la invitación a los asistentes; admite `transactionId` para que un reintento no duplique; `PATCH` actualiza (los asistentes reciben la actualización) y `POST …/cancel` lo cancela (solo el organizador). Un calendario de grupo de Microsoft 365 no admite crear eventos con permisos de aplicación; un **buzón compartido** sí.
- **Permiso acotado**: `Calendars.ReadWrite` de aplicación consentido en Entra vale para **todos** los buzones. Exchange Online ofrece **RBAC para aplicaciones** (rol `Application Calendars.ReadWrite` con un ámbito de administración), que **reemplaza** a las políticas de acceso de aplicaciones; los dos permisos se suman, así que el de Entra no debe consentirse. Los cambios tardan de 30 min a 2 h en aplicarse (caché de Exchange).
- La PWA ya tiene la página de soporte y la cola sin conexión (ATLAS-ADR-0032); otro agente hace en paralelo los recordatorios (HU 2.11) y el offline de F2 (HU 2.12), que reutilizarán el envío a Teams.

## Factores de decisión

- Confiable e idempotente: un cambio de turno nunca se pierde aunque Graph o la red fallen, y repetir no duplica eventos.
- Menos de un minuto entre la aprobación y el evento movido, con varias réplicas de atlas-api.
- Permisos mínimos (§15): ningún acceso de Atlas a buzones distintos del calendario del soporte.
- Sin SDK nuevo ni dependencias nuevas en producción; nada se rompe sin Microsoft 365 (local, dev).
- Ningún secreto en el repo: la URL del flujo de Teams es una credencial.
- Feeds sin datos de contacto (§7.3 y §12) y con un token que se pueda cortar.

## Opciones consideradas

### Sincronización con el calendario

1. **Marca de pendiente por turno en la transacción del cambio** (tabla `soporte_evento_calendario`, que también guarda el id del evento) y un **trabajo en segundo plano con reintentos** que calcula el evento desde el turno actual; se despierta con cada cambio y revisa cada 15 s.
2. Evento por la outbox de Plataforma y un consumidor en atlas-api con su inbox.
3. Llamar a Graph dentro de la petición que cambia el turno.

### Aviso en el canal de Teams

1. **Webhook de un flujo de Workflows de Teams** (plantilla «Publicar en un canal cuando se recibe una solicitud de webhook»), con una tarjeta adaptable; la URL en un SealedSecret.
2. Graph `POST /teams/{id}/channels/{id}/messages`.
3. Un bot de Teams (Azure Bot Service) con mensajes proactivos.

### Calendario compartido

1. **Buzón compartido** organizador de los eventos.
2. Calendario de un grupo de Microsoft 365.
3. Un evento directamente en el calendario de cada persona.

### Feeds .ics

1. **Token aleatorio de 256 bits en la URL, guardado como SHA-256**, uno vigente por persona y tipo, regenerable y revocable; el feed se genera al vuelo.
2. Un token firmado (sin tabla) con vencimiento.
3. Exigir la cookie de sesión.

## Decisión

Opción 1 en los cuatro.

### Token de Graph para los módulos

- Plataforma define `IAccesoGraph` (`Disponible` y `CabeceraAsync`) y el cliente HTTP con nombre `atlas-graph` (base `https://graph.microsoft.com/`). Por defecto, `SinAccesoGraph` (no disponible). Con Entra configurado, la identidad lo reemplaza por `AccesoGraphEntra`, que pide el token de aplicación con la misma credencial y caché de MSAL que `PerfilGraph`. Ningún token se guarda ni va a los logs.

### Calendario compartido (`soporte_evento_calendario`)

- Una fila por turno: `turno_id` (único, **sin llave foránea**: un turno borrado por la generación o el importador debe cancelar su evento), `evento_graph_id`, `persona_sincronizada_id`, `pendiente`, `version`, `intentos`, `proximo_intento_en`, `ultimo_error` (solo el código HTTP o el tipo de excepción) y `sincronizado_en`.
- `SoporteDbContext.SaveChanges` marca la fila **pendiente** (y sube `version`) de cada turno nuevo, borrado o con otra persona, otro horario u otro estado, en la misma transacción que el cambio (el patrón de la outbox, sin broker). La observación y la marca de revisión no tocan el evento. Así lo cubren todas las rutas que cambian turnos, también las futuras.
- `SincronizadorM365` calcula lo que debe haber para cada fila pendiente y vencida: un turno que ya pasó no se toca (su evento queda como historia aunque después quede `aplicado`, y el historial importado no llena calendarios); si el turno no existe, está reemplazado o cancelado, o su persona no tiene buzón (inactiva o creada por el importador con `@personas.invalid`), **cancela** el evento si lo hay; si no, lo **actualiza** (misma persona) o **cancela el anterior y crea uno nuevo** (otra persona: la anterior recibe la cancelación y la nueva la invitación). La creación lleva `transactionId = atlas-soporte-<turno>-<versión>`. Un reemplazo aprobado (HU 2.9) deja el turno original `reemplazado` y crea el del reemplazo: el evento sale del Outlook de una persona y entra al de la otra.
- Evento: asunto «Soporte de los sábados: <equipo>», horario del turno (o del equipo, o el global 08:00–12:00) en la zona `SA Pacific Standard Time` (Bogotá), la persona como asistente obligatoria, recordatorio 60 min, sin pedir respuesta ni proponer otra hora y un texto sin datos de contacto.
- Cierre con control de concurrencia: la fila solo se cierra si `version` no cambió mientras se llamaba a Graph; si cambió, se guarda el id del evento y queda pendiente para el ciclo siguiente.
- Fallo (Graph, red o MSAL): `intentos + 1` y espera de 30 s que se duplica hasta 30 min, o la del `Retry-After`. El manejador de resiliencia de ServiceDefaults ya reintenta 429 y 5xx dentro de cada llamada.
- **Trabajo** `TrabajoM365Soporte`: corre en todas las réplicas, un candado consultivo de PostgreSQL deja trabajar a una; cada `Soporte:M365:Intervalo` (15 s) con el `TimeProvider` de la aplicación o en cuanto esta réplica confirma una transacción con algo pendiente (`DespertadorM365`). Con la señal, un cambio aprobado llega a Graph en segundos; sin ella (otra réplica), en a lo sumo 15 s más la latencia de Graph: el CA de menos de un minuto se cumple con margen.
- Métricas `atlas.soporte.m365.operaciones` (medidor `Flit.Atlas.Soporte`, por tipo y resultado) y advertencias en el log con el id del turno, sin nombres ni correos.

### Aviso en Teams (`INotificadorTeams`)

- **Mecanismo elegido: webhook de un flujo de Workflows de Teams.** Es el reemplazo que Microsoft indica para los conectores retirados, no requiere permisos de Graph nuevos ni un bot registrado, y funciona desde un trabajo en segundo plano. Graph `chatMessage` queda descartado (solo migración con permisos de aplicación).
- Plataforma define `INotificadorTeams` (`Configurado(canal)` y `EnviarAsync(canal, MensajeTeams)`) con `NotificadorTeamsWorkflows`: canales con nombre en `Plataforma:Teams:Canales:<nombre>` (solo URL absolutas HTTPS; si no, el canal queda apagado), tarjeta adaptable 1.4 (`message` con un adjunto `application/vnd.microsoft.card.adaptive`: título, texto, datos y un botón «Abrir en Atlas» con `Atlas:UrlPublica`), tiempo máximo de 15 s y errores que nunca incluyen la URL. Su cliente HTTP no registra peticiones (`RemoveAllLoggers`): la URL lleva la firma del flujo.
- El soporte encola los avisos en `soporte_aviso_teams` (contenido `@pii`: lleva nombres de empleados; el canal es interno) en la transacción de la acción, con `ColaAvisosTeams` (pública para la HU 2.11, con el tipo `recordatorio`); el mismo trabajo los publica con reintentos y descarta los que no salieron en 24 h o cuyo canal ya no está configurado. Sin canal configurado no se encola nada.
- Avisos de la HU 2.10: al **confirmar una generación** («Rotación publicada: <equipo>», con los sábados y quién atiende, hasta 15 datos) y al **aprobar un cambio de turno** («Cambio de turno aprobado: <equipo>», de quién a quién). El recordatorio del sábado lo envía la HU 2.11 con este mismo servicio: lo encola con `ColaAvisosTeams` (tipo `recordatorio`) y sale por el canal del soporte.
- **Sin aviso personal por Teams** (conciliación con la HU 2.11, 23/10/2026): Workflows publica en un canal, no a una persona, así que el centro de notificaciones muestra el canal `teams` como no disponible en las preferencias y nadie recibe un aviso de Teams a su nombre. `INotificadorTeams` es la única interfaz de Teams de Atlas (la provisional de la HU 2.11 se eliminó).

### Feeds .ics (`soporte_feed_ics`)

- `POST /api/v1/soporte/feeds` (`atlas.soporte.ver`) crea el feed **personal** (mis turnos) o el de un **equipo** y devuelve la URL con el token una sola vez (también como `webcal://`); si ya había uno igual, lo revoca (regenerar). Se guarda solo el SHA-256 del token (único), la persona, el tipo, el equipo, `revocado_en` y `ultimo_uso_en`. Un vigente por persona, tipo y equipo (índice único parcial con `NULLS NOT DISTINCT`) y hasta 20 por persona. `GET /feeds` lista los propios sin URL y `DELETE /feeds/{id}` revoca. Todo queda en la auditoría (`soporte.feed_ics_creado`, `_regenerado`, `_revocado`).
- `GET /api/v1/soporte/ics/{token}.ics` es **público** (lo leen Outlook, Google Calendar y el calendario del teléfono, que no tienen la cookie): está en la lista de públicos de `AutorizacionTests` y en las excepciones de vacuum (`owasp-no-credentials-in-url`, `contracts/vacuum/openapi-excepciones.yaml`). Responde 404 a un token desconocido, revocado o de una persona inactiva o sin `atlas.soporte.ver` (se recalcula con `CalculadoraPermisos` en cada lectura). `text/calendar; charset=utf-8`, `Cache-Control: private, max-age=300`, `X-Robots-Tag: noindex` y `Referrer-Policy: no-referrer`; `ultimo_uso_en` se escribe a lo sumo una vez por hora.
- Contenido (`CalendarioIcs`, RFC 5545): `METHOD:PUBLISH`, `VTIMEZONE` `America/Bogota` (UTC−5 fijo), un `VEVENT` por turno vigente de 90 días atrás a 400 adelante con UID estable, `DTSTART`/`DTEND` con `TZID`, `STATUS` `TENTATIVE` (programado) o `CONFIRMED`, texto escapado y líneas plegadas a 75 octetos con CRLF. El personal dice «Soporte <equipo>»; el de equipo agrega el nombre de quien atiende. **Nunca** correos ni teléfonos.

### Estado y pantalla

- `GET /api/v1/soporte/microsoft-365` (`atlas.soporte.ver`): si el calendario y Teams están activos y, si no, el motivo (sin Entra, sin buzón, apagado, sin URL del canal), los pendientes, los que tienen reintentos y la última sincronización.
- PWA: `/soporte/microsoft-365` («Outlook y calendarios» desde el calendario): el estado de las dos integraciones con su motivo cuando están apagadas, y los feeds (crear el personal o el de un equipo, copiar la dirección, abrir en la app de calendario, regenerar y revocar). Móvil primero, sin scroll horizontal a 360 px.

### Configuración (sin secretos en el repo)

| Variable | Por defecto | Uso |
|---|---|---|
| `Soporte__M365__Buzon` | — | UPN del buzón compartido del calendario. Sin él, el calendario queda apagado |
| `Soporte__M365__CalendarioHabilitado` | `true` | `false` apaga el calendario aunque haya buzón |
| `Soporte__M365__NombreCalendario` | `Soporte TI sábados` | Nombre en la pantalla y los feeds |
| `Soporte__M365__CanalTeams` | `soporte` | Canal de `Plataforma__Teams__Canales__*` |
| `Soporte__M365__Intervalo` | `00:00:15` | Revisión periódica |
| `Soporte__M365__EsperaReintento`, `Soporte__M365__EsperaMaxima` | `00:00:30`, `00:30:00` | Reintentos |
| `Plataforma__Teams__Canales__soporte` | — | URL del disparador del flujo de Workflows. **Secreto: SealedSecret** |

Sin Entra (local, dev con el proveedor simulado) o sin esa configuración, cada parte se apaga limpio: los turnos y los cambios funcionan igual, las filas quedan pendientes (si se configura después, los turnos futuros se sincronizan) y la pantalla dice por qué está apagado.

### Datos y migración

Migración `Microsoft365` del módulo: `soporte_evento_calendario`, `soporte_aviso_teams` y `soporte_feed_ics` (llave foránea a `soporte_equipo` y, a mano, a `plataforma_persona` con borrado en cascada). Sin `organizacion_id` ni RLS, como el resto del módulo (solo empleados). No se publican eventos nuevos en la outbox: el AsyncAPI no cambia.

## Pasos del líder (no requieren código)

1. **Buzón compartido**: crea en Exchange Online un buzón compartido para el calendario (p. ej. `soporte-sabados@<dominio>`), con el nombre para mostrar «Soporte TI sábados». No necesita licencia. Si quieres que el equipo vea el calendario completo, compártelo en lectura con el grupo de TI.
2. **Permiso acotado al buzón** (RBAC para aplicaciones de Exchange Online, desde `Connect-ExchangeOnline` con el rol Organization Management):

   ```powershell
   New-ServicePrincipal -AppId <client id de «Flit Atlas»> -ObjectId <object id de la aplicación empresarial> -DisplayName "Flit Atlas <ambiente>"
   New-ManagementScope -Name "Atlas soporte sábados" -RecipientRestrictionFilter "PrimarySmtpAddress -eq '<buzón compartido>'"
   New-ManagementRoleAssignment -App <object id> -Role "Application Calendars.ReadWrite" -CustomResourceScope "Atlas soporte sábados"
   Test-ServicePrincipalAuthorization -Identity <object id> -Resource <buzón compartido>
   ```

   **No** consientas `Calendars.ReadWrite` en Entra: se sumaría al de Exchange y daría acceso a todos los buzones. Los cambios tardan de 30 min a 2 h. (Las políticas de acceso de aplicaciones que mencionaba el plan están reemplazadas por este RBAC.)
3. **Canal de Teams**: en el canal del soporte, Workflows → plantilla «Publicar en un canal cuando se recibe una solicitud de webhook», con una cuenta de servicio o del equipo (el flujo es suyo); copia la URL del disparador y guárdala **solo** en un SealedSecret de atlas-api en `flit-gitops` como `Plataforma__Teams__Canales__soporte`. Si se filtra, borra el flujo y crea otro.
4. **ConfigMap** de atlas-api en `flit-gitops` (no son secretos): `Soporte__M365__Buzon: "<buzón compartido>"` y, si no está, `Atlas__UrlPublica` (los botones de Teams y las URL de los feeds).
5. Después del despliegue, entra a **Soporte → Outlook y calendarios**: las dos tarjetas deben decir «Activo». Crea un turno de prueba para ti y verifica el evento en tu Outlook y en Teams; luego cancélalo.

## Consecuencias

### Positivas

- Ningún cambio de turno se pierde: la marca viaja en la misma transacción y el trabajo reintenta; repetir no duplica (cálculo desde el estado actual y `transactionId`).
- El calendario de cada persona se mantiene con un permiso limitado a un solo buzón; Atlas no lee ni escribe ningún otro.
- `INotificadorTeams` y `ColaAvisosTeams` quedan listos para los recordatorios (HU 2.11) y otros módulos; cambiar de mecanismo de Teams solo cambia la implementación.
- Los feeds funcionan en cualquier app de calendario sin Microsoft 365 y se cortan al instante.

### Negativas

- Un flujo de Workflows depende de la cuenta que lo creó (si se desactiva, el flujo se detiene) y publica con la identidad del bot de Workflows; Power Automate tiene sus propios límites de ejecución.
- El token del feed va en la URL: puede quedar en el historial del cliente de calendario o en trazas de proxies; se mitiga con 256 bits, solo hash en la base, revocación, sin datos de contacto y `no-referrer`. Las trazas de OpenTelemetry de atlas-api registran la ruta (`url.path`) del feed **[por verificar si se redacta en el colector]**.
- El evento se recrea (cancelar y crear) cuando cambia la persona del mismo turno, en lugar de cambiar el asistente: la persona nueva recibe una invitación nueva.
- Un turno que cambia de equipo o un equipo que cambia de nombre no actualiza el asunto de los eventos ya creados hasta el siguiente cambio del turno.
- Las filas de calendario quedan pendientes en ambientes sin Microsoft 365 (crecen con los turnos; son pocas).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Outbox de Plataforma con consumidor en atlas-api | Exige consumir de RabbitMQ en atlas-api (hoy no lo hace) y un broker en las pruebas; la marca en la tabla del módulo da la misma garantía transaccional y además guarda el id del evento |
| Llamar a Graph dentro de la petición | Viola §5.1 (más de ~1 s sale de la petición) y pierde el cambio si Graph falla |
| Graph `chatMessage` | Con permisos de aplicación solo se admite para migración; publicar exige un permiso delegado con una persona con sesión |
| Bot de Teams | Registro en Azure Bot Service, manifiesto de app de Teams y conversación de referencia por canal: demasiado para un aviso de una vía |
| Calendario de grupo de Microsoft 365 | Graph no permite crear eventos de grupo con permisos de aplicación |
| Evento en el calendario de cada persona | Exige `Calendars.ReadWrite` sobre todos los buzones del equipo; con el buzón compartido basta uno |
| Token firmado sin tabla | No se puede revocar uno solo sin una lista de revocados, que es la misma tabla |
| Feed con cookie | Ningún cliente de calendario la envía |

## Más información

- Código: `services/api/src/Flit.Atlas.Plataforma/Microsoft365/`, `services/api/src/Flit.Atlas.Identidad/Entra/AccesoGraphEntra.cs`, `services/api/src/Flit.Atlas.Modulos.Soporte/Dominio/Microsoft365/`, `services/api/src/Flit.Atlas.Modulos.Soporte/Endpoints/EndpointsM365.cs`, `apps/web/src/modulos/soporte/paginas/Microsoft365.tsx`. Configuración: `services/api/README.md` y `deploy/README.md`.
- Pruebas: `M365Tests` (módulo: marca en la transacción, crear, actualizar, mover y cancelar sin duplicar, reintentos con `FakeTimeProvider`, turnos pasados y personas sin buzón, cola de Teams, RFC 5545), `NotificadorTeamsTests` (tarjeta, canales y errores) y `SoporteM365Tests` (API con Graph y Workflows simulados: el CA de menos de un minuto con y sin la señal, apagado limpio y feeds).
- Fuentes:
  - Microsoft 365 Developer Blog, «Retirement of Office 365 connectors within Microsoft Teams» (actualizado en 2026): https://devblogs.microsoft.com/microsoft365dev/retirement-of-office-365-connectors-within-microsoft-teams/
  - Centro de mensajes MC1181996, «Migration update for Office 365 connectors retirement in Teams – webhook URL support»: https://mc.merill.net/message/MC1181996
  - Microsoft Graph, «Send chatMessage in a channel» (permisos de aplicación solo para migración): https://learn.microsoft.com/graph/api/channel-post-messages
  - Microsoft Graph, «Create event» (`transactionId`) y «event: cancel»: https://learn.microsoft.com/graph/api/user-post-events y https://learn.microsoft.com/graph/api/event-cancel
  - Exchange Online, «Role Based Access Control for Applications in Exchange Online»: https://learn.microsoft.com/exchange/permissions-exo/application-rbac
  - RFC 5545 (iCalendar): https://www.rfc-editor.org/rfc/rfc5545
- Pendientes **[por verificar]**: la cuenta dueña del flujo de Workflows (de servicio o del equipo) y si el canal es privado; si el equipo debe ver el calendario compartido completo; si los avisos de Teams deben esperar a una hora del día; redacción de la ruta del feed en las trazas; si cancelar un turno ya pasado debe cancelar también su evento (hoy queda como historia).
- Relacionados: ATLAS-ADR-0018, ATLAS-ADR-0026, ATLAS-ADR-0029, ATLAS-ADR-0030, ATLAS-ADR-0032.
