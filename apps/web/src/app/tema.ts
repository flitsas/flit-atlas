import { useCallback, useSyncExternalStore } from 'react';

/**
 * Tema claro, oscuro o el del sistema. La preferencia es lo único que Atlas guarda en `localStorage` (no es un dato de
 * la sesión). `public/tema.js` la aplica antes de pintar; este módulo la cambia en caliente.
 */
export type Tema = 'claro' | 'oscuro' | 'sistema';

export const CLAVE_TEMA = 'atlas.tema';

const COLOR_BARRA = { claro: '#eef5ff', oscuro: '#020618' } as const;

const oyentes = new Set<() => void>();

export function leerTema(): Tema {
  try {
    const valor = window.localStorage.getItem(CLAVE_TEMA);
    return valor === 'claro' || valor === 'oscuro' ? valor : 'sistema';
  } catch {
    return 'sistema';
  }
}

function sistemaOscuro(): boolean {
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches === true;
}

export function temaEfectivo(tema: Tema = leerTema()): 'claro' | 'oscuro' {
  return tema === 'sistema' ? (sistemaOscuro() ? 'oscuro' : 'claro') : tema;
}

export function aplicarTema(tema: Tema = leerTema()): void {
  const efectivo = temaEfectivo(tema);
  document.documentElement.classList.toggle('dark', efectivo === 'oscuro');
  // La barra del sistema (Android) sigue al tema elegido, no solo al del sistema operativo.
  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    meta.content = COLOR_BARRA[efectivo];
  }
}

export function guardarTema(tema: Tema): void {
  try {
    if (tema === 'sistema') window.localStorage.removeItem(CLAVE_TEMA);
    else window.localStorage.setItem(CLAVE_TEMA, tema);
  } catch {
    // Almacenamiento bloqueado (modo privado estricto): el tema vale solo para esta pestaña.
  }
  aplicarTema(tema);
  for (const avisar of oyentes) avisar();
}

function suscribir(avisar: () => void) {
  oyentes.add(avisar);
  const consulta = window.matchMedia?.('(prefers-color-scheme: dark)');
  const alCambiarSistema = () => {
    if (leerTema() === 'sistema') aplicarTema('sistema');
    avisar();
  };
  consulta?.addEventListener('change', alCambiarSistema);
  return () => {
    oyentes.delete(avisar);
    consulta?.removeEventListener('change', alCambiarSistema);
  };
}

export function useTema(): { tema: Tema; efectivo: 'claro' | 'oscuro'; cambiar: (tema: Tema) => void } {
  const tema = useSyncExternalStore(suscribir, leerTema, () => 'sistema' as Tema);
  const efectivo = useSyncExternalStore(suscribir, () => temaEfectivo(), () => 'claro' as const);
  const cambiar = useCallback((nuevo: Tema) => guardarTema(nuevo), []);
  return { tema, efectivo, cambiar };
}
