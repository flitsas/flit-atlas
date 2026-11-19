# src/modulos · Módulos del frontend

Un directorio por módulo (`catalogo/`, `equipo/`, `soporte/`, `contratos/`, `documentacion/`, `asistente/`, `permisos/` y el ejemplo `hola/`), cada uno con su `modulo.ts` que exporta un `ModuloAtlas`, y sus páginas y widgets con carga diferida. Agregar un módulo no obliga a tocar los demás: se crea la carpeta y se agrega una línea en `src/app/modulos/registro.ts` (ATLAS-ADR-0016).

Hoy los seis módulos del MVP están en `estado: 'proximamente'` con su página «Próximamente»; cada fase reemplaza sus páginas y widgets por los reales.

`permisos/` es la pantalla «Roles y permisos» (matriz rol × permiso de la HU 1.4, solo con `atlas.roles.ver`) y `hola/` es el módulo de ejemplo de la HU 1.9, apagado por bandera. Paso a paso para crear uno: [`docs/como-crear-un-modulo.md`](../../../../docs/como-crear-un-modulo.md). Ningún módulo importa a otro (`tests/arquitectura-modulos.test.ts`).
