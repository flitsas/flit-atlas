/**
 * Abre la URL firmada de una exportación en la misma pestaña (HU 4.5 y §11). En iOS es lo único que descarga de forma
 * confiable: un `blob:` o una ventana nueva (`window.open`) se bloquean o abren una pestaña vacía. La URL responde con
 * `Content-Disposition: attachment`, así que la PWA no se pierde.
 */
export function abrirEnLaMismaPestana(url: string): void {
  window.location.assign(url);
}
