# src/pwa · Capacidades de PWA

- `sw.ts`: service worker (`vite-plugin-pwa` con `injectManifest`): precarga del shell, notificaciones push y activación de la versión nueva cuando la persona acepta.
- `push.ts`: suscripción Web Push por dispositivo con la llave VAPID del servidor, con la guía para iOS (solo con Atlas instalado).
- `almacen.ts`: caché sin conexión en IndexedDB (`idb`) con TTL, que se borra al cerrar sesión.
- `conexion.ts`: estado de la red para el indicador «Sin conexión».

Ver ATLAS-ADR-0020.
