# contracts/asyncapi

`atlas-events.v1.yaml` (AsyncAPI 3.0) describe el exchange `flit.atlas`, el sobre de los mensajes de la outbox (ATLAS-ADR-0018) y cada evento que publica Atlas. Se escribe a mano y lo atan al código las pruebas de `ContratoAsyncApiTests` (ATLAS-ADR-0021).

## Agregar un evento

1. Declara el tipo como campo `public static readonly TipoEvento` en el módulo que lo publica (p. ej. `EventosSoporte.TurnoCambiado = new("atlas.soporte.turno_cambiado", 1)`).
2. Encólalo con `contexto.EncolarEvento(EventosSoporte.TurnoCambiado, datos, organizacionId)`. Una prueba rechaza los tipos escritos como texto suelto.
3. Agrega el mensaje en `components.messages` con `x-atlas-tipo` y `x-atlas-version` (la plantilla está al final del archivo) y referéncialo desde `channels.flitAtlas.messages` y `operations.publicarEvento.messages`.
4. Un cambio incompatible en `data` es una versión nueva: otro `TipoEvento` con `version` + 1 y otro mensaje; el anterior se mantiene mientras haya consumidores.

Hoy el catálogo está vacío: Atlas aún no publica eventos desde código de producción.
