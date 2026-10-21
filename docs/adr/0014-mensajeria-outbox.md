---
id: ATLAS-ADR-0014
titulo: Mensajería — outbox e inbox del SDK de plataforma de FLIT sobre RabbitMQ
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §5.2 y A-06, flit ADR-0064, flit services/core-api/src/Flit.Platform.Sdk/README.md]
---

# ATLAS-ADR-0014 · Mensajería — outbox e inbox del SDK de plataforma de FLIT sobre RabbitMQ

## Contexto y problema

Atlas publica eventos (`atlas.contrato.version_publicada`, cambios de turno, exportaciones solicitadas…) y despacha trabajos de más de ~1 s a `atlas-docs`, `atlas-ia` y `atlas-render` por RabbitMQ (exchange `flit.atlas`). Necesita **outbox** (el evento sale si y solo si se confirma el cambio) e **inbox** (un evento repetido no repite el efecto). El prompt maestro deja dos caminos: el SDK de plataforma de FLIT, si está disponible como paquete, o MassTransit v8.

Hechos verificados el 21/10/2026:

- `Flit.Platform.Sdk` (en `flit/services/core-api/src/Flit.Platform.Sdk/`) ya tiene `AddFlitOutbox<TContext>`, `IPlatformOutbox`, `AddFlitConsumer` / `AddFlitInbox`, con reintentos a 10 s, 1 min y 10 min, cola `.dlq` y métrica de dead-letter. Está construido sobre `RabbitMQ.Client` 7.x, **sin MassTransit** (ADR-0064 de `flit`), y usa el sobre de eventos del contrato de plataforma §7.
- El SDK **no está publicado** como paquete: no hay `dotnet nuget push` en el repo (ver ATLAS-ADR-0015).
- **MassTransit v9 es comercial** (Massient, Inc.; última 9.2.3 del 02/10/2026). La **v8 sigue siendo Apache-2.0** (última 8.5.11 del 30/09/2026), pero el contrato comercial no la soporta y su soporte se describe solo «en el corto plazo», sin fecha de fin publicada.

## Factores de decisión

- El mismo sobre de eventos, las mismas convenciones de exchange y DLQ y la misma operación que el resto de FLIT.
- Licencia abierta y sin riesgo de quedar en una versión sin soporte.
- Python (`atlas-ia`, `atlas-docs`) y Go (`atlas-render`) consumen los mismos mensajes: el formato debe ser JSON simple, no el sobre de una librería .NET.

## Opciones consideradas

1. **Outbox e inbox de `Flit.Platform.Sdk`**, consumido como paquete NuGet publicado desde `flit`.
2. MassTransit v8 (Apache-2.0) con su outbox de EF Core.
3. MassTransit v9 con licencia comercial.
4. Implementación propia en Atlas sobre `RabbitMQ.Client`.
5. Wolverine u otra librería de mensajería.

## Decisión

Opción elegida: **1, `Flit.Platform.Sdk` (mensajería y telemetría)**, condicionada a que se publique como paquete (ATLAS-ADR-0015).

- Atlas usa solo `Messaging` (outbox, inbox y consumidores) y `Telemetry`. **No** usa `AddFlitPlatformAuthentication`, que valida tokens de core-identity (ATLAS-ADR-0004).
- Tablas de outbox e inbox con el esquema `atlas` y el prefijo `plataforma_` **[por verificar: si el SDK permite fijar el nombre de tabla o si impone el suyo]**.
- Exchange `flit.atlas`, con productor `atlas`. Los trabajos para otros servicios de FLIT (p. ej. `flit.notificaciones`) siguen el patrón `EnqueueJob` del SDK.
- Los consumidores Go y Python leen el sobre JSON del contrato de plataforma §7, documentado en `contracts/asyncapi/atlas-events.v1.yaml`.
- **Plan B**, si el SDK no puede publicarse antes de F1 o impone dependencias incompatibles: implementar un outbox mínimo propio sobre `RabbitMQ.Client` que respete el mismo sobre y las mismas convenciones de DLQ, con la idea de migrar al SDK cuando esté publicado. MassTransit no se adopta.

## Consecuencias

### Positivas

- Una sola forma de mensajería en FLIT, sin licencias comerciales ni una v8 a punto de quedar sin soporte.
- El sobre JSON es neutral para Go y Python.

### Negativas

- Depende de un cambio en el repo `flit` (publicar el paquete), que hay que coordinar con el líder.
- Atlas queda atado al ritmo de versiones del SDK. Mitigación: versión fijada y actualización por PR.
- El SDK no tiene sagas ni planificador de mensajes: si Atlas los necesita, va a cron (`flit-scheduler`) o a lógica propia.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| MassTransit v8 | Apache-2.0, pero sin soporte comercial y sin fecha de fin publicada; FLIT ya decidió no usar MassTransit (ADR-0064 de `flit`) |
| MassTransit v9 | Licencia comercial por línea de producto u organización; costo sin necesidad, porque el SDK ya resuelve el caso |
| Implementación propia como camino principal | Duplica el SDK; queda solo como plan B |
| Wolverine u otras | Otra dependencia y otro sobre de mensajes distinto del de la plataforma |

## Más información

- <https://masstransit.massient.com/support>, <https://massient.com/license> (licencia de v9 y estado de v8).
- ADR-0064 de `flit` (datos separados, eventos y reportes).
