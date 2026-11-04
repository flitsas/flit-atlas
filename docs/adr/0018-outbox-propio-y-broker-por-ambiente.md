---
id: ATLAS-ADR-0018
titulo: Outbox e inbox propios de Atlas y RabbitMQ propio por ambiente
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: [ATLAS-ADR-0014, "ATLAS-ADR-0015 (solo la parte NuGet de Flit.Platform.Sdk)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-06 y §5.1, F0.5 N-04, N-05 y N-08, flit deploy/rabbitmq (HU 13349), flit Flit.Platform.Sdk/Messaging]
---

# ATLAS-ADR-0018 · Outbox e inbox propios de Atlas y RabbitMQ propio por ambiente

## Contexto y problema

ATLAS-ADR-0014 eligió el outbox y el inbox de `Flit.Platform.Sdk`, y ATLAS-ADR-0015 propuso publicar ese SDK como paquete NuGet desde `flit` para consumirlo desde Atlas. Al revisar el costo en F0.5 (N-04):

- Publicar el SDK exige un workflow nuevo en `flit`, un token de lectura de paquetes en el CI de Atlas y un ciclo de publicar → actualizar → probar cada vez que el SDK cambie.
- Atlas no comparte mensajes con FLIT 2.0 en el MVP. El correo sale por Microsoft Graph (N-05), no por `core-notificaciones`. Los consumidores de la cola de Atlas son sus propios servicios (`atlas-docs`, `atlas-ia`, `atlas-render`), y dos de ellos no son .NET, así que tampoco podrían usar el SDK.
- En `flit-gitops`, solo `flit2-dev` tiene RabbitMQ; `flit2-qa` y `flit2-pdn` no lo declaran (F0.4, ítem 4.3). Atlas necesita un broker en los tres ambientes de todas formas.

El líder técnico decidió el 21/10/2026 no usar el SDK por ahora.

## Factores de decisión

- No depender de cambios ni de versiones de `flit` para la mensajería de Atlas.
- No perder mensajes ni procesarlos dos veces.
- Que Go y Python lean los mensajes sin depender de una librería .NET.
- Poder migrar al SDK o al broker compartido de FLIT más adelante sin romper nada.

## Opciones consideradas

1. **Outbox e inbox propios** sobre `RabbitMQ.Client`, con el mismo sobre y las mismas convenciones del SDK de FLIT, y **RabbitMQ propio** en cada ambiente.
2. Publicar `Flit.Platform.Sdk` como NuGet y usarlo (ATLAS-ADR-0014).
3. MassTransit v8 con su outbox de EF Core.
4. Outbox propio sobre el broker de FLIT 2.0, con un vhost `atlas`.

## Decisión

Opción elegida: **1**.

### Formato del mensaje

El sobre es el mismo del contrato de plataforma de FLIT §7 (`EventEnvelope` del SDK), en JSON camelCase:

| Campo | Valor en Atlas |
|---|---|
| `eventId` | uuid v7; es la llave de idempotencia del inbox |
| `type` | `atlas.<modulo>.<evento>`, p. ej. `atlas.contratos.version_publicada`. Es también la llave de ruteo |
| `version` | Versión del esquema de `data` |
| `occurredAt` | `timestamptz` en UTC |
| `tenantId` | `organizacion_id` cuando el evento pertenece a una organización externa; `00000000-0000-0000-0000-000000000000` si es interno |
| `producer` | `atlas` |
| `correlationId` | Id de la traza (W3C `traceparent`) de la petición que lo originó |
| `data` | El contenido del evento |

Se documenta en `contracts/asyncapi/atlas-events.v1.yaml` (HU 1.7).

### Outbox (publicar)

1. El módulo agrega el evento en la **misma transacción** de EF Core que el cambio que lo origina. Queda una fila en `atlas.plataforma_outbox`.
2. Un `BackgroundService` de `atlas-api` reclama los pendientes en orden con `FOR NO KEY UPDATE SKIP LOCKED`, seguro con varias réplicas. Los publica en el exchange `flit.atlas` (tipo topic, durable) con confirmación del broker, y sella `publicado_en` solo cuando el broker confirma.
3. Si el broker no responde, el evento queda pendiente, sube `intentos` y el lote se corta, para conservar el orden. La espera entre ciclos se duplica en cada fallo seguido, con un tope de 1 minuto. No hay tope de intentos: un broker caído es transitorio y el evento no se pierde.
4. Los eventos publicados se borran a los 7 días.

### Inbox (consumir)

- Tabla `atlas.plataforma_inbox` con llave `(evento_id, consumidor)`. El consumidor registra el evento en la misma transacción que su efecto. Si el evento vuelve a llegar, lo confirma sin repetir el efecto.
- Colas `atlas.<consumidor>.<proposito>` (p. ej. `atlas.docs.exportar`), con exchange de reintentos `<cola>.reintentos` y cola de errores (DLQ), como en `flit/deploy/rabbitmq`.
- Go y Python aplican la misma regla en sus servicios, con su propia tabla de eventos procesados.

### Broker

- **Un RabbitMQ propio de Atlas en cada ambiente** (`atlas-dev`, `atlas-qa`, `atlas-pdn`), declarado en `flit-gitops` en la HU 1.8. En local lo levanta el AppHost de Aspire.
- Se siguen las convenciones de `flit/deploy/rabbitmq`: vhost propio, un usuario por servicio de Atlas, y exchange `flit.atlas` del que solo `atlas-api` publica. Así, si un día Atlas pasa al broker de FLIT 2.0, el cambio es de configuración y no de código.

## Consecuencias

### Positivas

- Atlas no depende de `flit` para su mensajería, ni del ciclo de versiones del SDK.
- Cualquier lenguaje lee el sobre, y el formato es compatible con el resto de FLIT.
- Los exportes y las indexaciones de Atlas no compiten por recursos con el broker de FLIT 2.0, y las credenciales quedan aisladas por namespace.

### Negativas

- Atlas mantiene unas pocas clases de mensajería que el SDK ya resuelve, con sus pruebas de integración (Postgres y RabbitMQ reales con Testcontainers).
- Si el SDK mejora (p. ej. métricas o reintentos), Atlas no lo recibe automáticamente.
- Un RabbitMQ más que operar por ambiente, con su respaldo y su monitoreo.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. SDK como NuGet | Trabajo en otro repo y un ciclo de versiones permanente para una sola función que Atlas usa |
| 3. MassTransit v8 | La v9 es comercial y la v8 tiene fecha de fin de soporte; además su sobre no es el de FLIT |
| 4. Broker de FLIT 2.0 con vhost `atlas` | qa y pdn no tienen broker hoy, y compartirlo acopla la capacidad de los dos productos. Queda como camino si Atlas llega a intercambiar mensajes con FLIT 2.0 |

## Más información

- ATLAS-ADR-0014 queda **reemplazado** por este ADR.
- De ATLAS-ADR-0015 se reemplaza solo la publicación de `Flit.Platform.Sdk`. La réplica de los tokens de marca sigue vigente.
- Cierra las preguntas N-04 y N-08 (en lo que toca a RabbitMQ) de `docs/preparacion/05-preguntas-abiertas.md`.
