/** Tokens ya tomados en esta carga de la página (el modo estricto de React llama dos veces a los inicializadores). */
const tomados = new Map<string, string>();

/**
 * Lee el token de un enlace de un solo uso desde el fragmento de la URL (`/invitacion#<token>`) y lo quita de la barra
 * de direcciones y del historial. El fragmento nunca llega a un servidor (ni a los logs de nginx ni al `Referer`); el
 * token solo queda en la memoria de la página.
 */
export function tomarFragmento(ruta: string): string {
  const previo = tomados.get(ruta);
  if (previo !== undefined && !window.location.hash) return previo;
  const valor = decodeURIComponent(window.location.hash.replace(/^#/, '')).trim();
  if (valor && window.location.pathname === ruta) {
    window.history.replaceState(window.history.state, '', ruta);
  }
  const token = /^[A-Za-z0-9_.-]{20,2048}$/.test(valor) ? valor : '';
  tomados.set(ruta, token);
  return token;
}
