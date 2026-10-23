/// <reference lib="webworker" />
/**
 * Service worker de Atlas (vite-plugin-pwa con injectManifest, ATLAS-ADR-0020): precarga el shell para abrir sin
 * conexión, avisa cuando hay una versión nueva (el shell decide cuándo activarla) y muestra las notificaciones push.
 * Nunca guarda respuestas de la API: los datos sin conexión van en IndexedDB y se borran al cerrar sesión.
 */
import { cleanupOutdatedCaches, createHandlerBoundToURL, precacheAndRoute } from 'workbox-precaching';
import { NavigationRoute, registerRoute } from 'workbox-routing';

declare const self: ServiceWorkerGlobalScope & {
  __WB_MANIFEST: Array<string | { url: string; revision: string | null }>;
};

precacheAndRoute(self.__WB_MANIFEST);
cleanupOutdatedCaches();

// Navegaciones de la SPA: index.html precargado. Las rutas del BFF siempre van a la red.
registerRoute(
  new NavigationRoute(createHandlerBoundToURL('/index.html'), {
    denylist: [/^\/api\//, /^\/hubs\//, /^\/connect\//, /^\/\.well-known\//, /^\/health/],
  }),
);

// «Hay una nueva versión»: el shell pide activar la nueva cuando la persona acepta (registerType: prompt).
self.addEventListener('message', (evento) => {
  if ((evento.data as { type?: string } | null)?.type === 'SKIP_WAITING') {
    void self.skipWaiting();
  }
});

interface CargaPush {
  id?: string;
  tipo?: string;
  titulo?: string;
  cuerpo?: string;
  enlace?: string | null;
}

/** Solo rutas internas de la PWA: una notificación no puede llevar a otro sitio. */
function rutaInterna(enlace: string | null | undefined): string {
  return enlace && enlace.startsWith('/') && !enlace.startsWith('//') ? enlace : '/notificaciones';
}

self.addEventListener('push', (evento) => {
  let carga: CargaPush = {};
  try {
    carga = (evento.data?.json() as CargaPush | undefined) ?? {};
  } catch {
    carga = { cuerpo: evento.data?.text() };
  }
  evento.waitUntil(
    (async () => {
      await self.registration.showNotification(carga.titulo ?? 'Flit Atlas', {
        body: carga.cuerpo ?? '',
        icon: '/iconos/icono-192.png',
        badge: '/iconos/insignia-96.png',
        tag: carga.id,
        lang: 'es-CO',
        data: { ruta: rutaInterna(carga.enlace) },
      });
      // Las pestañas abiertas refrescan el centro de notificaciones.
      const clientes = await self.clients.matchAll({ type: 'window' });
      for (const cliente of clientes) cliente.postMessage({ tipo: 'atlas:notificacion' });
    })(),
  );
});

self.addEventListener('notificationclick', (evento) => {
  evento.notification.close();
  const ruta = rutaInterna((evento.notification.data as { ruta?: string } | null)?.ruta);
  evento.waitUntil(
    (async () => {
      const clientes = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
      const abierto = clientes[0];
      if (abierto) {
        await abierto.focus();
        abierto.postMessage({ tipo: 'atlas:navegar', ruta });
        return;
      }
      await self.clients.openWindow(ruta);
    })(),
  );
});
