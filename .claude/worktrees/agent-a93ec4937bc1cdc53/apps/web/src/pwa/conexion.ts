import { useSyncExternalStore } from 'react';

/** `true` mientras el navegador crea tener red; el indicador «Sin conexión» del shell se basa en esto (§11). */
export function useEnLinea(): boolean {
  return useSyncExternalStore(suscribir, () => navigator.onLine, () => true);
}

function suscribir(avisar: () => void) {
  window.addEventListener('online', avisar);
  window.addEventListener('offline', avisar);
  return () => {
    window.removeEventListener('online', avisar);
    window.removeEventListener('offline', avisar);
  };
}
