# src/modulos · Módulos del frontend

Un directorio por módulo (`catalogo/`, `equipo/`, `soporte/`, `contratos/`, `documentacion/`, `asistente/`), cada uno con su `modulo.ts` que exporta un `ModuloAtlas`, y sus páginas y widgets con carga diferida. Agregar un módulo no obliga a tocar los demás: se crea la carpeta y se agrega una línea en `src/app/modulos/registro.ts` (ATLAS-ADR-0016).

Hoy los seis módulos del MVP están en `estado: 'proximamente'` con su página «Próximamente»; cada fase reemplaza sus páginas y widgets por los reales.
