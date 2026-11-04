#!/usr/bin/env node
// Réplica de la marca FLIT en apps/web/src/marca/ (ATLAS-ADR-0015 y ATLAS-ADR-0020): tokens de diseño y logos
// copiados de flitsas/flit en el commit declarado en apps/web/src/marca/origen.json.
//
//   node scripts/marca/sincronizar.mjs --desde <clon de flit> --sha <sha>
//       Copia los archivos desde un clon local de flit que esté en ese commit y anota el sha de blob de cada uno.
//   node scripts/marca/sincronizar.mjs --verificar
//       CI: falla si algún archivo de la réplica se editó a mano (su sha de blob ya no es el anotado). No usa red.
//
// El sha de blob es el mismo que muestra GitHub para ese archivo en ese commit, así que se puede auditar con
//   gh api repos/flitsas/flit/git/trees/<sha>?recursive=1
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const carpeta = join(raiz, 'apps', 'web', 'src', 'marca');
const rutaOrigen = join(carpeta, 'origen.json');

const argumentos = process.argv.slice(2);
const valor = (nombre) => {
  const i = argumentos.indexOf(nombre);
  return i >= 0 ? argumentos[i + 1] : undefined;
};

const origen = JSON.parse(await readFile(rutaOrigen, 'utf8'));
const lf = (texto) => texto.replace(/\r\n/g, '\n');
const shaBlob = (texto) => {
  const bytes = Buffer.from(texto, 'utf8');
  return createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
};

/** CSS y TS llevan un encabezado con la ruta de origen y el sha; los SVG se copian tal cual. */
const conEncabezado = (archivo) => ['.css', '.ts'].includes(extname(archivo.destino));
const encabezado = (archivo) =>
  `/* Réplica de ${origen.repo}@${origen.sha}:${archivo.origen} (ATLAS-ADR-0015).\n` +
  ' * No la edites a mano: node scripts/marca/sincronizar.mjs */\n';

if (argumentos.includes('--verificar')) {
  const diferentes = [];
  for (const archivo of origen.archivos) {
    let contenido = lf(await readFile(join(carpeta, archivo.destino), 'utf8').catch(() => ''));
    if (conEncabezado(archivo)) {
      const esperado = encabezado(archivo);
      contenido = contenido.startsWith(esperado) ? contenido.slice(esperado.length) : '';
    }
    if (shaBlob(contenido) !== archivo.blob) diferentes.push(archivo.destino);
  }
  if (diferentes.length > 0) {
    console.error(
      `La réplica de la marca no coincide con ${origen.repo}@${origen.sha}: ${diferentes.join(', ')}.\n` +
        'No edites esos archivos a mano: sincronízalos con --desde y --sha.',
    );
    process.exit(1);
  }
  console.log(`Marca FLIT al día con ${origen.repo}@${origen.sha}.`);
} else {
  const desde = valor('--desde');
  const sha = valor('--sha');
  if (!desde || !sha || !/^[0-9a-f]{40}$/.test(sha)) {
    console.error('Uso: node scripts/marca/sincronizar.mjs --desde <clon de flit> --sha <sha de 40 caracteres>');
    process.exit(2);
  }
  origen.sha = sha;
  for (const archivo of origen.archivos) {
    const contenido = lf(await readFile(join(desde, archivo.origen), 'utf8'));
    archivo.blob = shaBlob(contenido);
    await writeFile(join(carpeta, archivo.destino), (conEncabezado(archivo) ? encabezado(archivo) : '') + contenido);
    console.log(`${archivo.origen} (${archivo.blob}) → apps/web/src/marca/${archivo.destino}`);
  }
  await writeFile(rutaOrigen, `${JSON.stringify(origen, null, 2)}\n`);
}
