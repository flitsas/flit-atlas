import { useState } from 'react';

/** Reemplazo de `virtual:pwa-register/react` en Vitest (jsdom no tiene service worker). */
export function useRegisterSW() {
  return {
    needRefresh: useState(false),
    offlineReady: useState(false),
    updateServiceWorker: async () => undefined,
  };
}
