---
id: ATLAS-ADR-0032
titulo: Acciones sin conexión — cola en IndexedDB del dispositivo y llave de idempotencia por acción que el servidor guarda con la acción
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §11 (sin conexión), §8.3, HU 2.9, ATLAS-ADR-0020, ATLAS-ADR-0029]
---

# ATLAS-ADR-0032 · Acciones sin conexión con llave de idempotencia

## Contexto y problema

El §11 pide que las acciones hechas sin conexión (el ejemplo es solicitar un cambio de turno) «se encolan y se envían al reconectar (Background Sync donde exista, reintento manual donde no)». La HU 2.9 es la primera que lo necesita: solicitar, aceptar, aprobar y cancelar un cambio de turno. Una acción que se reenvía (porque la respuesta se perdió, porque la persona pulsó dos veces o porque la cola la manda al reconectar) no debe repetirse en el servidor.

Hechos verificados:

- La PWA ya guarda caché sin conexión en IndexedDB (`apps/web/src/pwa/almacen.ts`, base `atlas`, con TTL) y la borra entera al cerrar sesión (`borrarDatosLocales`).
- La sesión va en la cookie `HttpOnly` del BFF y toda petición que cambia estado lleva la cabecera antifalsificación (ATLAS-ADR-0008 y ATLAS-ADR-0022). El service worker no tiene acceso a esa cabecera desde el código de la página sin duplicar la lógica de `pedir`.
- Background Sync solo existe en navegadores Chromium; Safari (iOS) no lo tiene.

## Factores de decisión

- Reenviar nunca duplica, aunque la respuesta se pierda a mitad de camino.
- Nada sensible queda en el dispositivo sin sesión.
- Funciona igual en Android, iOS y escritorio; sin depender de Background Sync.
- Reutilizable por otros módulos sin tocar el núcleo de la API.

## Opciones consideradas

### Dónde vive la cola

1. **IndexedDB del dispositivo, en la misma base `atlas` de la caché** (un almacén `cola` más), enviada desde la página.
2. Background Sync del service worker.
3. Solo en memoria (se pierde al cerrar la pestaña).

### Cómo se evita repetir

1. **Llave de idempotencia por acción (uuid que genera el cliente) guardada con la acción en el servidor, con índice único**; la repetición devuelve el estado actual.
2. Una tabla genérica de idempotencia en Plataforma con la respuesta guardada.
3. Cabecera `Idempotency-Key` con caché en Redis.

## Decisión

Opción 1 en las dos.

- **Cola** (`apps/web/src/pwa/cola.ts`): `ejecutarOEncolar` envía si hay red; si no hay (o la red falla en el camino) guarda la acción —llave, módulo, descripción, ruta, método, cuerpo, recurso al que se refiere y hora— en el almacén `cola` (la base `atlas` pasa a la versión 2). Se envía en orden al volver la red (evento `online`) o con «Reintentar ahora»; se detiene en la primera falla de red. Un rechazo del servidor (4xx salvo 408 y 429) deja la acción con su motivo para que la persona la descarte; no se reintenta. Sin IndexedDB, la cola vive en memoria mientras la pestaña esté abierta. Cerrar sesión borra la base entera, cola incluida: una acción sin enviar no sobrevive a la sesión (se le dice a la persona en la pantalla de pendientes).
- **Llave**: cada acción lleva `llaveIdempotencia` (uuid) y `hechaEn` (la hora del dispositivo, que el servidor guarda solo si es creíble: no futura ni de más de 30 días). En soporte (ATLAS-ADR-0029) la llave queda en `soporte_solicitud_evento` (el historial de la solicitud) con índice único: si llega otra vez de la misma persona, para la misma acción y la misma solicitud, la respuesta es la solicitud tal como quedó (200) sin repetir nada; si la llave es de otra acción, 409 `llave_usada`. Dos peticiones simultáneas con la misma llave las resuelve el índice único (la segunda responde como repetición).
- **Interfaz**: cada pantalla muestra «Pendiente de enviar» en el recurso afectado y una lista de pendientes con su estado, como pide el §11.
- **Background Sync** queda para después: el reintento al reconectar con la página abierta cubre Android, iOS y escritorio por igual; un sync del service worker tendría que repetir la sesión y la cabecera antifalsificación fuera de `pedir`.

## Consecuencias

### Positivas

- El historial y la idempotencia son la misma fila: no hay una tabla aparte que limpiar ni una respuesta guardada que pueda quedar desactualizada.
- Cualquier módulo puede usar la cola; solo debe guardar la llave con su acción y responder la repetición.
- Lo que la persona hizo sin conexión queda con su hora real en el historial.

### Negativas

- Las acciones pendientes solo se envían con Atlas abierto (sin Background Sync).
- Cerrar sesión con acciones sin enviar las pierde (por seguridad).
- Cada módulo implementa la búsqueda de la llave en su tabla.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Background Sync como único mecanismo | No existe en iOS; además el service worker tendría que manejar la sesión del BFF y la cabecera antifalsificación |
| Cola solo en memoria | Se pierde al cerrar la pestaña o reiniciar el teléfono, justo el caso sin conexión |
| Tabla genérica de idempotencia en Plataforma | Toca el núcleo y guarda respuestas que se desactualizan; el historial de cada módulo ya es el lugar natural |
| `Idempotency-Key` en Redis | Redis es caché (ATLAS-ADR-0007): la llave debe durar lo que dura la acción |

## Más información

- `apps/web/src/pwa/cola.ts`, `apps/web/src/modulos/soporte/acciones.ts`, `services/api/src/Flit.Atlas.Modulos.Soporte/Endpoints/EndpointsCambios.cs`.
- Pendiente **[por verificar]**: si otro módulo con acciones sin conexión (p. ej. documentos en F5) justifica Background Sync en Android.
