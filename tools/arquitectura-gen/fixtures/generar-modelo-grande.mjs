#!/usr/bin/env node
// Escribe fixtures/modelo-grande/: un landscape ficticio de 44 nodos (el tamaño del real) para comprobar que se lee bien (HU 6.3).
// Después: likec4 export json --project grande -o modelo-grande.export.json modelo-grande
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const carpeta = join(dirname(fileURLToPath(import.meta.url)), 'modelo-grande');
mkdirSync(carpeta, { recursive: true });
const grupos = ['Radicación', 'Consultas', 'Pagos', 'Reportes', 'Identidad', 'Mensajería'];
const sistemas = Array.from({ length: 40 }, (_, i) => ({ id: `sistema_${String(i + 1).padStart(2, '0')}`, titulo: `${grupos[i % grupos.length]} ficticio ${i + 1}` }));
const actores = Array.from({ length: 4 }, (_, i) => ({ id: `actor_${i + 1}`, titulo: `Perfil demo ${i + 1}` }));
const lineas = ['specification {', '  element actor', '  element system', '}', '', 'model {'];
for (const a of actores) lineas.push(`  ${a.id} = actor '${a.titulo}'`);
for (const s of sistemas) lineas.push(`  ${s.id} = system '${s.titulo}' { description 'Sistema ficticio para probar el tamaño del landscape' }`);
sistemas.forEach((s, i) => {
  if (i > 0) lineas.push(`  ${sistemas[Math.floor((i - 1) / 3)].id} -> ${s.id} 'usa'`);
});
actores.forEach((a, i) => lineas.push(`  ${a.id} -> ${sistemas[i].id} 'entra'`));
lineas.push('}', '', 'views {', '  view landscape {', "    title 'Landscape ficticio de 44 nodos'", '    include *', '  }', '}', '');
writeFileSync(join(carpeta, 'modelo.c4'), lineas.join('\n'));
writeFileSync(join(carpeta, 'likec4.config.json'), `${JSON.stringify({ name: 'grande', title: 'Landscape ficticio' })}\n`);
