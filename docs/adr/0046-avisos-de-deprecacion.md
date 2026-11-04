---
id: ATLAS-ADR-0046
titulo: Avisos de deprecación — deprecar con fecha de retiro, destinatarios calculados con las reglas del portal, marca idempotente por persona y canal, y recordatorios por ventana
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §5.1, §7.3, §8.4, §8.9, §9 y §12, plan 00 §8.2 regla 7, §8.5 y §12, HU 4.8, ATLAS-ADR-0018, ATLAS-ADR-0020, ATLAS-ADR-0023, ATLAS-ADR-0025, ATLAS-ADR-0029, ATLAS-ADR-0031, ATLAS-ADR-0035, ATLAS-ADR-0041, ATLAS-ADR-0043]
---

# ATLAS-ADR-0046 · Avisos de deprecación

## Contexto y problema

La HU 4.8 pide avisar a las organizaciones afectadas cuando se depreca una versión de un contrato (correo, push y en la
app). El plan 00 §8.2 (regla 7) dice que una versión se marca `deprecada` con fecha de retiro, que el portal muestra un
aviso y que Atlas avisa por correo a las organizaciones con paquetes que la usan; §8.5 define
`POST /contratos/{id}/versiones/{v}/deprecar` con `contratos.publicar_manual`, y §12 el evento
`atlas.contratos.version_deprecada`.

Hechos verificados en el código el 24/10/2026:

- `contratos_version` ya tenía `estado` (`vigente | deprecada | retirada`) y `retiro_en`; su trigger de inmutabilidad deja
  cambiar solo esas dos columnas. No había endpoint para deprecar.
- El Inicio del portal ya pintaba un aviso por versión deprecada o retirada de lo que la persona ve (ATLAS-ADR-0043), sin
  decir qué versión la reemplaza.
- El centro de notificaciones (bandeja, push con VAPID y correo con la misma abstracción que las invitaciones,
  `IEnviadorCorreo`) y las preferencias por tipo y canal existen; los recordatorios de soporte (ATLAS-ADR-0029) ya resolvieron
  el envío idempotente por canal con una marca previa y un candado consultivo.
- `AccesoPortal` calculaba la vista solo para la persona de la sesión. Contratos no puede leer el reparto de equipo
  (ATLAS-ADR-0026): solo el rol implícito, por persona.

## Factores de decisión

- A quién le llega un aviso = a quién el portal le muestra la versión: ni una regla más ni una menos (§7.3, §12).
- Nunca datos de otra organización en un mensaje; un correo por persona, sin copias.
- Idempotente frente a reintentos, varias réplicas y deprecar otra vez; auditado.
- Lo que tarda (correos a muchas personas) fuera de la petición (§5.1).
- Recordatorios configurables y probables con un reloj inyectable.

## Opciones consideradas

### Quién recibe el aviso

1. **Por persona, con la vista del portal**: las organizaciones con una compartición vigente cuya revisión (fijada o vigente)
   incluye la versión, y de ellas las personas externas activas a las que `AccesoPortal` (con sus permisos calculados sin
   sesión) les muestra un paquete con la versión.
2. Por organización: todas sus personas externas.

### Cuándo se envía

1. **Un trabajo en segundo plano** (cada hora, más una corrida inmediata que pide el endpoint) que revisa las versiones
   deprecadas con retiro pendiente, con una marca por persona, momento y canal.
2. En la petición de deprecar.
3. Un consumidor del evento `version_deprecada` en la outbox.

### Recordatorios

1. **Ventanas** (30 y 7 días por defecto): toca el recordatorio de la ventana más cercana ya abierta, si se abrió después
   del aviso de la deprecación.
2. Fecha exacta (justo 30 y 7 días antes).

## Decisión

Opción 1 en las tres.

### Deprecar (`POST /api/v1/contratos/{id}/versiones/{version}/deprecar`)

- `atlas.contratos.publicar_manual` (líder técnico; la descripción del permiso ahora lo dice). Cuerpo `{ retiroEn }`: después
  de hoy en Bogotá y a lo sumo 730 días adelante (si no, 400). 404 si la versión no existe; 409 `retirada` si ya se retiró.
- Deprecar otra vez con la misma fecha no cambia nada (`cambio: false`); con otra fecha la cambia y vuelve a avisar.
- En la misma transacción: el estado y la fecha, la auditoría `contratos.version_deprecada` (fecha anterior y reemplazo) y el
  evento `atlas.contratos.version_deprecada` v1 en la outbox (AsyncAPI). Después pide la corrida de avisos de esa versión
  (`DisparadorAvisos`) y responde; los avisos salen fuera de la petición.
- **Reemplazo**: la versión estable vigente más alta del mismo contrato posterior a la deprecada (SemVer), calculada al
  leer; sin columna nueva (el trigger de inmutabilidad no cambia).

### Destinatarios (`AvisosDeprecacion`)

- **Externos**: primero, barato, las organizaciones cuya compartición vigente y no revocada de un paquete publicado incluye
  la versión en su revisión; después, por cada persona externa activa de ellas, `CalculadoraPermisos` + `AccesoPortal.VistaDeAsync`
  (las mismas reglas del portal: alcance de la invitación, nivel del rol, vencimientos, contrato aún compartible). Nivel
  resumen también recibe el aviso (el resumen incluye los avisos). El texto nombra solo sus paquetes.
- **Internos**: los responsables vigentes (principal o secundario) del aplicativo del contrato o, si no tiene, del ancestro
  más cercano con responsables, por la lectura nueva `ILectorResponsables` de Plataforma que implementa Equipo. Su texto
  dice a cuántas organizaciones se avisó, sin nombrarlas, y enlaza a `/contratos/<id>`.

### Envío idempotente

- Tabla `contratos_aviso_deprecacion` con `organizacion_id` de la persona (predicado y RLS forzada) y clave única
  `version:retiro:momento[-días]:persona:canal`. Cada canal se marca `en_curso` antes de salir; `enviado`, o `fallido` con
  solo el tipo de la falla y hasta 3 intentos en corridas siguientes. Uno `en_curso` (corrida cortada) no se repite.
- Tipo de notificación `contratos.aviso_deprecacion` (app, push y correo, encendidos por defecto; la persona apaga push o
  correo en Ajustes). La bandeja siempre; push solo si hay llaves VAPID y dispositivos; un correo por persona con la
  plantilla de la marca.
- Candado consultivo de PostgreSQL: una réplica a la vez. La corrida programada cubre lo que una réplica caída no envió.
- Cada aviso por persona queda en `plataforma_auditoria` (`contratos.aviso_deprecacion_enviado`, actor sistema) con la
  organización de la persona, la versión, el momento, los canales y los paquetes; nunca el correo.

### Recordatorios (`Contratos:AvisosDeprecacion`)

- `DiasRecordatorio` (por defecto 30 y 7), `Intervalo` (1 h) y `Habilitados`. Toca el recordatorio de `d` días si faltan `d`
  o menos y el aviso de la deprecación salió antes de abrirse esa ventana; de varios, el de menos días. Así, deprecar a 5
  días del retiro manda un solo aviso; una corrida atrasada no manda los recordatorios vencidos; una persona que llega
  después (nueva compartición o invitación) recibe el aviso de la deprecación y desde ahí los recordatorios que falten.
- Pasada la fecha de retiro ya no se avisa. Marcar la versión como `retirada` sigue siendo manual **[por verificar con el
  líder si se retira sola al llegar la fecha]**.

### Portal

`AvisoDelPortal` suma `reemplazo` y su `mensaje` es el mismo texto que el correo («La versión 1.2.0 de API Demo está
deprecada y se retira el 31/12/2026. La reemplaza la versión 2.0.0.»).

## Consecuencias

### Positivas

- Recibe el aviso exactamente quien ve la versión en el portal; revocar, vencer o quitar el alcance lo corta igual que en
  las pantallas. Lo prueban `AvisosDeprecacionTests` (otra organización, compartición revocada y vencida, alcance a otro
  paquete, idempotencia y recordatorios con `FakeTimeProvider`) y `ReglasDeprecacionTests`.
- Reintentos y réplicas no duplican avisos; la auditoría muestra a quién y por dónde.

### Negativas

- Cada corrida calcula la vista de cada persona de las organizaciones afectadas (unas seis consultas por persona); con
  muchas personas habrá que agrupar por alcance.
- Un aviso `en_curso` de una corrida cortada no se reenvía (mejor uno de menos que uno repetido).
- El trabajo programado está apagado en las pruebas de la API (la base es compartida); se corre por versión.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Todas las personas de la organización | Llegaría a quien no ve el paquete (alcance de su invitación) y le revelaría que existe |
| Enviar en la petición | Un correo por persona puede tardar segundos cada uno (§5.1) |
| Consumidor de la outbox | No hay consumidores en atlas-api todavía y los recordatorios necesitan igual un trabajo con reloj |
| Fecha exacta | Un día sin corrida (caída, despliegue) perdería el recordatorio |
| Guardar el reemplazo en la versión | Tocaría el trigger de inmutabilidad; calculado al leer siempre está al día |

## Más información

- `services/api/src/Flit.Atlas.Modulos.Contratos/Deprecacion/`, `…/Portal/EndpointsPortal.cs`,
  `services/api/src/Flit.Atlas.Modulos.Equipo/LectorResponsablesEquipo.cs`,
  `services/api/tests/Flit.Atlas.Api.Tests/AvisosDeprecacionTests.cs`.
- Pendiente **[por verificar]**: la UI interna para deprecar (hoy por la API); el texto de los correos con el líder; push
  real en un dispositivo.
