---
id: ATLAS-ADR-0034
titulo: Datos sin conexión — respuestas de la API guardadas en IndexedDB por recurso, con permiso y TTL, sin teléfonos y de una sola persona por dispositivo
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §6.3, §7.3, §8.2, §10, §11 y §12, HU 2.12, HU 4.9, ATLAS-ADR-0039, ATLAS-ADR-0043, ATLAS-ADR-0020, ATLAS-ADR-0029, ATLAS-ADR-0031, ATLAS-ADR-0032]
---

# ATLAS-ADR-0034 · Datos sin conexión

## Contexto y problema

El §11 pide que el calendario de soporte (próximos 3 meses), los responsables y el catálogo se vean sin conexión «solo si sus permisos lo permiten, con TTL, y se borran al cerrar sesión o al perder el permiso», con un indicador de la fecha de la última sincronización. La HU 2.12 lo concreta con un CA: en modo avión, un desarrollador ve su próximo turno y los responsables de FLITO.

ATLAS-ADR-0032 resolvió lo contrario (las **acciones** hechas sin conexión, en una cola). Este ADR resuelve las **lecturas**: qué se guarda, dónde, cuándo se borra y qué no se guarda nunca.

Hechos verificados:

- El service worker solo precarga el shell y nunca guarda respuestas de la API (ATLAS-ADR-0020). La PWA ya tiene `apps/web/src/pwa/almacen.ts` (IndexedDB con `idb`, base `atlas`, almacén `recursos` con TTL) y lo usa para `/yo` y las banderas; la cola de ATLAS-ADR-0032 vive en la misma base. Cerrar sesión borra la base entera y las cachés, y la API envía `Clear-Site-Data`.
- El contrato de módulo del frontend ya tenía `offline: { recursos, ttlHoras }` sin uso.
- Las respuestas de equipo y soporte traen `telefono` solo con `atlas.equipo.ver_contacto` (ATLAS-ADR-0030 y ATLAS-ADR-0031).
- El bundle inicial está en 173,6 KB gzip de un tope de 200 KB.

## Factores de decisión

- Solo lo que la persona puede ver: el filtro sigue en el servidor; el dispositivo no decide permisos.
- Mínimo de datos personales en un dispositivo que se puede perder (Ley 1581, §12).
- Nada de una persona queda para la siguiente que use el mismo dispositivo.
- No inflar el bundle inicial; un módulo no conoce a los demás (ATLAS-ADR-0026).

## Opciones consideradas

### Dónde se guarda

1. **IndexedDB, almacén `recursos` de la base `atlas` que ya existe**, desde la página (el cliente de cada módulo).
2. Caché HTTP del service worker (Workbox `NetworkFirst` sobre `/api`).

### Qué se guarda

1. **La respuesta tal como la API se la dio a la persona**, por recurso (calendario de tres meses de todos los equipos, este sábado, equipos, reparto vigente, listado completo del catálogo), y lo demás (otros meses, un equipo, la ficha de un aplicativo, sus responsables) se **deriva** de eso sin red.
2. Cada URL consultada, tal cual.

### Teléfonos

1. **Nunca se guardan**, ni con `atlas.equipo.ver_contacto`.
2. Guardarlos si la persona tiene el permiso.

## Decisión

Opción 1 en las tres.

- **Recursos**: cada módulo declara en su manifiesto `offline.recursos` (clave, ruta y permiso) y un TTL de 72 horas. Soporte: `soporte.turnos` (hoy a tres meses, todos los equipos), `soporte.este-sabado`, `soporte.equipos`. Equipo: `equipo.responsabilidades` (reparto vigente). Catálogo: `catalogo.aplicativos` (listado sin filtros).
- **Cuándo se guarda**: con sesión y red, el shell carga con `import()` `pwa/sincronizacion.ts`, que pide los recursos de los módulos activos para los que la persona tiene el permiso (salvo los guardados hace menos de 15 minutos). Además, cada consulta de esos recursos en las páginas refresca lo guardado (`pwa/sinConexion.ts`). Lo que se guarda es lo que la API ya le devolvió a la persona, con su filtro de permisos y organización.
- **Sin red**: el cliente del módulo responde con lo guardado (o lo deriva: el mes del calendario sale de los tres meses; la ficha y los responsables de un aplicativo, del listado y del reparto; «¿A quién llamo?», del reparto, sin el soporte de turno) y la página muestra «Sin conexión: datos del dd/mm/aaaa hh:mm (sin teléfonos)». La tarjeta «Tu próximo turno» del calendario sale de los mismos tres meses. Un error del servidor (4xx/5xx) nunca se tapa con lo guardado.
- **Teléfonos**: todo campo `telefono` se deja vacío antes de guardar. Es lo más seguro: un teléfono de una persona del equipo en un dispositivo perdido es el dato más sensible de estas pantallas, y sin red tampoco se puede llamar por Teams; el correo y el nombre sí quedan (son los de la vista interna).
- **Cuándo se borra**: al cerrar sesión (la base entera, como ya hacía ATLAS-ADR-0020); cuando `/yo` responde 401 (sesión vencida o cerrada en otro lado) o trae a **otra persona** (`plataforma.dueno` guarda de quién es lo guardado; si cambia, se borra la base, cola incluida, antes de guardar nada de la nueva); y, por recurso, cuando la persona **pierde el permiso** que lo exige (cada registro guarda su permiso y la sincronización borra los que ya no están en `/yo`). Lo vencido (TTL) se borra al leerlo.
- **Bundle**: el inicial solo suma los manifiestos (clave, ruta y permiso) y el control del dueño; `sinConexion.ts`, `sincronizacion.ts` y el aviso van en chunks diferidos.

### Ampliación de la HU 4.9 (24/10/2026): contratos y guías favoritos

- **Favoritos en el servidor** (`contratos_favorito`, por persona, con `organizacion_id` y RLS): contratos internos
  (`/api/v1/contratos/favoritos`, con `ver_internos`) y paquetes, contratos y guías compartidos
  (`/api/v1/portal/favoritos/...`, con `portal.ver`; cada endpoint con su caso en `AislamientoPortalTests`). Marcar algo
  fuera de la vista de la persona responde 404 (o 403 `solo_resumen`); el listado solo trae lo que hoy ve.
- **Qué se guarda**: la respuesta de la API tal cual se la dio a la persona, por ruta (`contratos.fav:<ruta>`), con el
  permiso que la exige y el mismo TTL de 72 horas. Contrato interno: el contrato, sus versiones, el spec y el índice de la
  última versión. Paquete: su detalle y el spec **podado** (y los cambios) de cada contrato con detalle técnico. Contrato
  compartido: el detalle, el spec podado y los cambios. Guía: el detalle del paquete y la guía. Las listas de favoritos
  también, para verlas sin red.
- **Topes**: 5 MiB por respuesta y 30 MiB en total (del favorito más reciente al más antiguo); lo que no cabe se ve solo en
  línea y el botón no dice «Disponible sin conexión».
- **Cuándo se guarda**: al marcar (en el momento) y al sincronizar (el shell carga el sincronizador del módulo con
  `offline.cargarSincronizador`, en su chunk); al consultar una ruta ya guardada se refresca (`conRespaldoGuardado`). Una
  consulta que no es de un favorito nunca se guarda.
- **Cuándo se borra**: al cerrar sesión (la base entera); al quitar el favorito (lo suyo que ningún otro favorito usa); al
  perder el permiso (`depurarSinPermiso`); al resincronizar, lo que el servidor ya no devuelve en la lista o lo que responde
  403/404 (compartición revocada o vencida, fuera del alcance); y lo vencido por TTL al leerlo.
- **Indicadores**: el banner del shell «Sin conexión», el aviso de la página «Sin conexión: datos del <fecha>» y, en el
  botón y en «Tus favoritos», «Disponible sin conexión · sincronizado el <fecha>».
- **Visor**: sin red, la pantalla del contrato (interna y del portal) funciona con el spec guardado: operaciones con sus
  ejemplos, esquemas y matriz (OpenAPI), AsyncAPI y proto. Scalar (ATLAS-ADR-0039) no pide nada fuera del origen (lo
  comprueba la prueba E2E), pero corre en un iframe con `sandbox="allow-scripts"` y origen opaco, que el service worker no
  controla (se comprobó: ninguna respuesta del visor sale del service worker, ni con un manejador para `/visor/`) y
  `openapi.html` va con `Cache-Control: no-cache`. Sin red, la pestaña «Referencia interactiva» lo dice y remite a
  «Operaciones»; si el visor ya estaba abierto, sigue funcionando. Se descartó darle `allow-same-origin` al iframe (le daría
  la cookie y la API) y servirlo como `blob:` o `srcdoc` (heredarían la CSP del shell, que bloquea los estilos de Scalar).

## Consecuencias

### Positivas

- En modo avión se ve el próximo turno, el soporte de este sábado, los responsables de cada producto y el catálogo, con la fecha de lo guardado. Lo prueba `tests/e2e/sin-conexion.spec.ts` con el service worker real en los tres perfiles.
- Sin decisiones de permisos en el dispositivo ni datos de otra persona: lo guardado es la respuesta del servidor para quien está.
- Un módulo nuevo solo declara sus recursos y envuelve su cliente; el shell no lo conoce.

### Negativas

- Sin red no hay teléfonos ni el soporte de turno dentro de «¿A quién llamo?» (se ve en el calendario).
- La ficha sin red no trae repo, ambientes ni permisos de edición; los responsables heredados del padre no se derivan.
- Con permisos que no llegan en `/yo` (anterior a la HU 1.4) no se puede depurar por permiso; hoy siempre llegan.
- El TTL de 72 horas es un supuesto **[por verificar con el líder]**: cubre un fin de semana sin red.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Caché HTTP del service worker | Guardaría cualquier respuesta de `/api` sin permiso asociado ni forma de borrarla por permiso; ATLAS-ADR-0020 decidió que el service worker no guarda la API |
| Cada URL tal cual | Un mes o un filtro que la persona no abrió en línea no se vería; con la derivación basta un recurso por pantalla |
| Guardar teléfonos con `ver_contacto` | Más datos personales en el dispositivo por poco beneficio sin red |

## Más información

- `apps/web/src/pwa/sinConexion.ts`, `apps/web/src/pwa/sincronizacion.ts`, `apps/web/src/app/sesion.tsx`, `apps/web/src/modulos/*/recursos.ts`, `apps/web/tests/e2e/sin-conexion.spec.ts`.
- Pendiente **[por verificar]**: prueba a mano en un Android de gama media y en un iPhone instalado (iOS puede borrar IndexedDB de una PWA sin uso en semanas).
