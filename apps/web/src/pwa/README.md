# src/pwa · Capacidades de PWA

- `sw.ts`: service worker (`vite-plugin-pwa` con `injectManifest`): precarga del shell, notificaciones push y activación de la versión nueva cuando la persona acepta.
- `push.ts`: suscripción Web Push por dispositivo con la llave VAPID del servidor, con la guía para iOS (solo con Atlas instalado).
- `almacen.ts`: caché sin conexión en IndexedDB (`idb`) con TTL, que se borra al cerrar sesión.
- `conexion.ts`: estado de la red para el indicador «Sin conexión».
- `cola.ts`: acciones hechas sin conexión, con su llave de idempotencia, en IndexedDB; se envían al reconectar o con «Reintentar ahora» (ATLAS-ADR-0032).
- `sinConexion.ts`: datos de los módulos sin conexión (HU 2.12): `conRespaldo` y `conRespaldoDerivado` guardan lo que la API devolvió (sin teléfonos) y, sin red, lo muestran con el aviso de `app/ui/DatosGuardados.tsx`. Solo lo cargan las páginas de los módulos.
- `sincronizacion.ts`: con sesión y red, el shell (con `import()`) guarda los `offline.recursos` de cada módulo activo según los permisos y borra lo que perdió el permiso. Lo guardado es de una sola persona: sin sesión o con otra, `sesion.tsx` lo borra (ATLAS-ADR-0034).

Ver ATLAS-ADR-0020, ATLAS-ADR-0032 y ATLAS-ADR-0034.
