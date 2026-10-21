/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * En desarrollo, Vite hace de proxy hacia atlas-api: la PWA y la API comparten origen porque la cookie del BFF
 * es SameSite=Strict (ATLAS-ADR-0008). En dev/qa/pdn lo hace el Ingress (docs/preparacion/03-puertos-y-namespaces.md §3).
 */
const RUTAS_DE_LA_API = ['/api', '/hubs', '/connect', '/.well-known', '/signin-oidc', '/signout-callback-oidc'];

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

export default defineConfig(({ command, mode }) => {
  const servidor = command === 'serve' && mode !== 'test';
  const api = servidor ? urlDeLaApi(process.env) : '';

  return {
    plugins: [react()],
    server: {
      port: Number(process.env.PORT ?? 4032),
      strictPort: true,
      proxy: servidor
        ? Object.fromEntries(RUTAS_DE_LA_API.map((ruta) => [ruta, { target: api, changeOrigin: false, ws: true }]))
        : undefined,
    },
    build: {
      target: 'es2022',
      sourcemap: true,
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./tests/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}', 'tests/**/*.test.{ts,tsx}'],
    },
  };
});
