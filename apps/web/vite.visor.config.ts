import { defineConfig } from 'vite';

/**
 * Build aparte del visor OpenAPI con Scalar (HU 3.6, ATLAS-ADR-0039): `dist/visor/openapi.html` y sus archivos en
 * `dist/visor/assets/`. Va después del build de la PWA (`pnpm build`) y en su propia carpeta para que:
 * - no comparta chunks con la PWA (nada de Scalar ni de Vue en el bundle inicial ni en la precarga del service worker);
 * - nginx le ponga su CSP propia a `/visor/` sin tocar la del shell.
 */
export default defineConfig({
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: false,
    assetsDir: 'visor/assets',
    sourcemap: false,
    // Scalar pesa más de lo que Vite avisa por defecto; va en un iframe que solo se carga en escritorio.
    chunkSizeWarningLimit: 6000,
    // Mermaid (ATLAS-ADR-0053 §5) y bpmn-js (ATLAS-ADR-0054 §5) van igual porque inyectan estilos en línea, que la CSP del shell no permite.
    // likec4/react también: lo aísla ATLAS-ADR-0056 §7.
    rollupOptions: { input: ['visor/openapi.html', 'visor/mermaid.html', 'visor/bpmn.html', 'visor/likec4.html'] },
  },
});
