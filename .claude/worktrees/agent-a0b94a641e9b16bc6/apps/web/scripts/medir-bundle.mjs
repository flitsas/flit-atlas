#!/usr/bin/env node
// Tamaño del bundle inicial (prompt maestro §11: < 200 KB gzip): lo que index.html carga o precarga (JS, CSS y
// tema.js). Los chunks diferidos de páginas y widgets no cuentan. Falla si se pasa del tope. Correr tras `pnpm build`:
//   pnpm bundle
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const TOPE_KB = 200;
const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const html = readFileSync(join(dist, 'index.html'), 'utf8');
const archivos = [...html.matchAll(/(?:src|href)="\/([^"]+\.(?:js|css))"/g)].map((m) => m[1]);

let total = 0;
for (const archivo of archivos) {
  const kb = gzipSync(readFileSync(join(dist, archivo)), { level: 9 }).length / 1024;
  total += kb;
  console.log(`${kb.toFixed(1).padStart(7)} KB  ${archivo}`);
}
console.log(`${total.toFixed(1).padStart(7)} KB  total inicial (gzip), tope ${TOPE_KB} KB`);
if (total > TOPE_KB) process.exit(1);
