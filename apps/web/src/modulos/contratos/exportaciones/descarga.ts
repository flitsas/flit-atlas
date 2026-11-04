/**
 * Abre la URL firmada de una exportación en la misma pestaña (HU 4.5, HU 4.6 y §11). La URL responde con
 * `Content-Disposition: attachment`, así que el navegador descarga y la PWA no se pierde.
 *
 * Por qué así y no de otra forma (HU 4.6, ATLAS-ADR-0042 «Compartir»):
 * - `window.open(blob:…)` o `window.open(url)`: Safari en iOS bloquea la ventana nueva que no sale directo del gesto
 *   (aquí hay un `await` antes: pedir la URL firmada) y con `blob:` abre una pestaña en blanco. Descartado.
 * - `<a download href="https://s3…">`: el atributo `download` solo aplica al mismo origen (o `blob:`/`data:`); con la
 *   URL firmada del S3 (otro origen) el navegador lo ignora y navega igual que `location.assign`. No aporta nada.
 * - `<a download href="blob:…">` (bajar a memoria y luego guardar): funciona en Safari 13+ en el navegador, pero en la
 *   PWA instalada (standalone) de iOS hubo versiones que no muestran nada o abren una vista previa sin salida, y además
 *   duplica la memoria y exige que el S3 permita CORS y la CSP `connect-src`. Descartado para descargar; se usa solo para
 *   compartir el archivo con la hoja nativa (`CompartirExportacion`), donde iOS ofrece «Guardar en Archivos».
 * - `location.assign(url)`: en Safari (navegador) muestra «¿Descargar…?» y la página sigue ahí; en la PWA instalada,
 *   la URL fuera del alcance (`scope`) del manifiesto se abre en la vista de Safari dentro de la app, que descarga y se
 *   cierra con «OK» volviendo a Atlas; en Android (Chrome y la PWA) descarga sin salir. Es lo que se usa.
 *
 * Probado: Vitest (que se llama con la URL firmada) y Playwright con los perfiles de escritorio, Pixel 7 e iPhone 14
 * (WebKit emulado). [por verificar] En un iPhone real con la PWA instalada (iOS 17 y 18) y en un Android de gama media.
 */
export function abrirEnLaMismaPestana(url: string): void {
  window.location.assign(url);
}
