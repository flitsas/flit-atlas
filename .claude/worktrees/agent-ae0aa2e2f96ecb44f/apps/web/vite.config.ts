/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { type ManifestOptions, VitePWA } from 'vite-plugin-pwa';

import paquete from './package.json' with { type: 'json' };

/**
 * En desarrollo, Vite hace de proxy hacia atlas-api: la PWA y la API comparten origen porque la cookie del BFF
 * es SameSite=Strict (ATLAS-ADR-0008). En dev/qa/pdn lo hace el Ingress (docs/preparacion/03-puertos-y-namespaces.md §3).
 */
const RUTAS_DE_LA_API = ['/api', '/hubs', '/connect', '/.well-known'];

export function urlDeLaApi(entorno: Record<string, string | undefined>): string {
  const url = entorno.ATLAS_API_URL?.trim();
  if (!url) {
    throw new Error(
      'Faltan variables de configuración obligatorias: ATLAS_API_URL. ' +
        'Arranca la web desde el AppHost de Aspire (aspire/) o define la variable.',
    );
  }
  return url;
}

/** Certificado de desarrollo que pasa el AppHost; sin él, Vite sirve http (pruebas y uso sin Aspire). */
export function certificadoHttps(entorno: Record<string, string | undefined>): { cert: Buffer; key: Buffer } | undefined {
  const certificado = entorno.ATLAS_WEB_CERTIFICADO?.trim();
  const llave = entorno.ATLAS_WEB_LLAVE?.trim();
  return certificado && llave ? { cert: readFileSync(certificado), key: readFileSync(llave) } : undefined;
}

/** Manifiesto de la PWA (prompt maestro §11): instalable, standalone, íconos maskable y atajos. */
export const MANIFIESTO: Partial<ManifestOptions> = {
  id: '/',
  name: 'Flit Atlas',
  short_name: 'Atlas',
  description: 'El conocimiento de FLIT en un solo lugar: aplicativos, responsables, soporte, contratos y documentación.',
  lang: 'es-CO',
  dir: 'ltr',
  start_url: '/',
  scope: '/',
  display: 'standalone',
  display_override: ['standalone', 'minimal-ui'],
  orientation: 'any',
  // Splash de Android: fondo azul FLIT con el ícono; la barra del sistema la ajusta tema.ts según el tema.
  background_color: '#162744',
  theme_color: '#162744',
  categories: ['business', 'productivity'],
  icons: [
    { src: '/iconos/icono-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
    { src: '/iconos/icono-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
    { src: '/iconos/icono-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
    { src: '/iconos/icono-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    { src: '/favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
  ],
  shortcuts: [
    {
      name: 'Soporte de los sábados',
      short_name: 'Soporte',
      url: '/soporte',
      icons: [{ src: '/iconos/atajo-soporte-96.png', sizes: '96x96', type: 'image/png' }],
    },
    { name: 'Buscar', url: '/buscar', icons: [{ src: '/iconos/atajo-buscar-96.png', sizes: '96x96', type: 'image/png' }] },
    {
      name: 'Asistente Atlas',
      short_name: 'Asistente',
      url: '/asistente',
      icons: [{ src: '/iconos/atajo-asistente-96.png', sizes: '96x96', type: 'image/png' }],
    },
    {
      name: 'Notificaciones',
      url: '/notificaciones',
      icons: [{ src: '/iconos/atajo-notificaciones-96.png', sizes: '96x96', type: 'image/png' }],
    },
  ],
};

/**
 * Cabeceras de seguridad de la imagen nginx (`deploy/`), leídas de los mismos archivos, para que `vite preview` (E2E)
 * sirva la PWA y el visor con su CSP y las pruebas vean las mismas violaciones que en qa (HU 4.10, ATLAS-ADR-0048). Las
 * variables de nginx toman su valor de local: sin HSTS; el S3 de las pruebas; el visor, con los hosts por defecto de la imagen.
 */
function cabecerasDeNginx(): (ruta: string) => [string, string][] {
  const leer = (archivo: string) => readFileSync(fileURLToPath(new URL(`./deploy/${archivo}`, import.meta.url)), 'utf8');
  const variables: Record<string, string> = {
    $atlas_hsts: '',
    // El S3 ficticio de las pruebas E2E (compartir el archivo exportado, HU 4.6); en la imagen sale de ATLAS_S3_PUBLICO.
    $atlas_s3_publico: process.env.ATLAS_S3_PUBLICO ?? 'https://s3.ejemplo.com',
    '${ATLAS_VISOR_CONEXIONES}': 'https://*.qa.flitsas.online https://*.qa.flitsas.com',
  };
  const cabeceras = (texto: string): [string, string][] =>
    [...texto.matchAll(/^\s*add_header\s+(\S+)\s+(?:"([^"]*)"|'([^']*)'|(\S+))\s+always;/gm)]
      .map((m): [string, string] => [m[1]!, Object.entries(variables).reduce((v, [k, r]) => v.split(k).join(r), m[2] ?? m[3] ?? m[4] ?? '').trim()])
      .filter(([nombre, valor]) => valor !== '' && nombre !== 'Cache-Control');
  const plantilla = leer('nginx.conf.template');
  // El bloque termina en la llave de cierre sola en su línea (la CSP del visor lleva ${…}).
  const bloque = (location: string) => cabeceras(plantilla.split(`location ${location} {`)[1]!.split(/\r?\n\s*\}/)[0]!);
  const pwa = cabeceras(leer('atlas-seguridad.conf'));
  const visor = cabeceras(leer('atlas-seguridad-visor.conf'));
  const documentoVisor = [...visor, ...bloque('^~ /visor/')];
  const archivosVisor = [...visor, ...bloque('^~ /visor/assets/')];
  return (ruta) => (ruta.startsWith('/visor/assets/') ? archivosVisor : ruta.startsWith('/visor/') ? documentoVisor : pwa);
}

export default defineConfig(({ command, mode }) => {
  const servidor = command === 'serve' && mode !== 'test';
  const api = servidor ? urlDeLaApi(process.env) : '';

  return {
    define: {
      __ATLAS_VERSION__: JSON.stringify(`${paquete.version}${process.env.ATLAS_COMMIT ? `+${process.env.ATLAS_COMMIT.slice(0, 7)}` : ''}`),
    },
    plugins: [
      react(),
      tailwindcss(),
      VitePWA({
        // Service worker propio (src/pwa/sw.ts) para manejar push; Workbox solo precarga el shell (ATLAS-ADR-0020).
        strategies: 'injectManifest',
        srcDir: 'src/pwa',
        filename: 'sw.ts',
        // «Hay una nueva versión»: la persona decide cuándo recargar (§11).
        registerType: 'prompt',
        // El registro va en el bundle (virtual:pwa-register/react): nada en línea en index.html (CSP).
        injectRegister: false,
        manifest: MANIFIESTO,
        injectManifest: {
          globPatterns: ['**/*.{js,css,html,svg,png,woff2,webmanifest}'],
          // Las pantallas de arranque de iOS solo las pide iOS al instalar; no se precargan.
          globIgnores: ['iconos/pantalla-*.png'],
        },
        devOptions: { enabled: false },
      }),
      {
        name: 'atlas-cabeceras-preview',
        configurePreviewServer(previa) {
          const cabeceras = cabecerasDeNginx();
          previa.middlewares.use((peticion, respuesta, siguiente) => {
            const ruta = (peticion.url ?? '/').split('?')[0]!;
            if (!RUTAS_DE_LA_API.some((r) => ruta.startsWith(`${r}/`))) {
              for (const [nombre, valor] of cabeceras(ruta)) respuesta.setHeader(nombre, valor);
            }
            siguiente();
          });
        },
      },
    ],
    server: {
      port: Number(process.env.PORT ?? 4032),
      strictPort: true,
      https: servidor ? certificadoHttps(process.env) : undefined,
      // xfwd: la API recibe X-Forwarded-Proto/For y arma la redirect_uri de Entra con https, como detrás del Ingress.
      proxy: servidor
        ? Object.fromEntries(RUTAS_DE_LA_API.map((ruta) => [ruta, { target: api, changeOrigin: false, ws: true, xfwd: true }]))
        : undefined,
    },
    // `vite preview` (E2E): el visor de contratos corre con origen opaco (iframe con sandbox, ATLAS-ADR-0039) y sus módulos
    // se piden con CORS y `Origin: null`; nginx hace lo mismo solo en /visor/ (deploy/nginx.conf.template).
    preview: {
      cors: { origin: [/^https?:\/\/(?:localhost|127\.0\.0\.1|\[::1\])(?::\d+)?$/, 'null'] },
    },
    build: {
      target: 'es2022',
      sourcemap: true,
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./tests/setup.ts'],
      // jsdom no tiene service worker: el registro de vite-plugin-pwa se reemplaza por uno vacío.
      alias: { 'virtual:pwa-register/react': fileURLToPath(new URL('./tests/registro-sw-falso.ts', import.meta.url)) },
      include: ['src/**/*.test.{ts,tsx}', 'tests/**/*.test.{ts,tsx}'],
    },
  };
});
