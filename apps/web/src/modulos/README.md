# src/modulos · Módulos del frontend

Un directorio por módulo (`catalogo/`, `equipo/`, `soporte/`, `contratos/`, `documentacion/`, `diagramas/`, `gia/`, `asistente/`, `permisos/` y el ejemplo `hola/`), cada uno con su `modulo.ts` que exporta un `ModuloAtlas`, y sus páginas y widgets con carga diferida. Agregar un módulo no obliga a tocar los demás: se crea la carpeta y se agrega una línea en `src/app/modulos/registro.ts` (ATLAS-ADR-0016).

`documentacion/` ya está disponible (HU 5.3, 5.4, 5.6 y 5.8, ATLAS-ADR-0053). `diagramas/` también (HU 6.6 y 6.7, ATLAS-ADR-0054): sus visores aislados viven en `src/app/diagramas/` porque los usa también la documentación. `catalogo/` ya está disponible (HU 2.1, ATLAS-ADR-0028): listado, ficha con pestañas (los demás módulos aportan las suyas con `pestanasFicha`) y formularios. Los otros módulos del MVP siguen en `estado: 'proximamente'` con su página «Próximamente»; cada fase reemplaza sus páginas y widgets por los reales.

`gia/` es el gobierno de IA (HU 8.1, 8.2, 8.3, 8.5 y 8.6, ATLAS-ADR-0055): viene apagado por bandera hasta que el líder revise lo sembrado.

`permisos/` es la pantalla «Roles y permisos» (matriz rol × permiso de la HU 1.4, solo con `atlas.roles.ver`) y `hola/` es el módulo de ejemplo de la HU 1.9, apagado por bandera. Paso a paso para crear uno: [`docs/como-crear-un-modulo.md`](../../../../docs/como-crear-un-modulo.md). Ningún módulo importa a otro (`tests/arquitectura-modulos.test.ts`).
