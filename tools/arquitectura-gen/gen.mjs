#!/usr/bin/env node
// Uso (lo invoca .github/workflows/atlas-publicar-arquitectura.yml desde flit-gitops):
//   node gen.mjs --repo <flit-gitops> --config arquitectura-gen.yaml --sha <commit> --salida gen/ [--modelo modelo.json] [--kustomize "kustomize build"]
// Escribe snapshot.<ambiente>.<cluster>.json y, con --modelo, deployment.<ambiente>.c4. La carpeta de salida no se commitea.
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseArgs } from 'node:util';

import { parse } from 'yaml';

import { emitirDespliegue, indiceDeElementos } from './lib/emisor-likec4.mjs';
import { generar, renderizadorKustomize } from './lib/generador.mjs';

const { values } = parseArgs({
  options: {
    repo: { type: 'string', default: '.' },
    config: { type: 'string', default: 'arquitectura-gen.yaml' },
    sha: { type: 'string' },
    salida: { type: 'string', default: 'gen' },
    modelo: { type: 'string' },
    kustomize: { type: 'string', default: 'kustomize build' },
  },
});

const sha = values.sha ?? execFileSync('git', ['-C', values.repo, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const config = parse(readFileSync(join(values.repo, values.config), 'utf8'));
const { snapshots, avisos } = generar({
  repo: values.repo,
  config,
  sha,
  generadoEn: new Date().toISOString(),
  renderizar: renderizadorKustomize(values.kustomize.split(' ').filter(Boolean)),
});

mkdirSync(values.salida, { recursive: true });
for (const s of snapshots) {
  writeFileSync(join(values.salida, `snapshot.${s.ambiente}.${s.cluster.id}.json`), JSON.stringify(s, null, 2));
  console.log(`snapshot.${s.ambiente}.${s.cluster.id}.json: ${s.cluster.namespaces.length} namespaces`);
}
if (values.modelo) {
  const indice = indiceDeElementos(JSON.parse(readFileSync(values.modelo, 'utf8')));
  for (const ambiente of new Set(snapshots.map((s) => s.ambiente))) {
    const { dsl, sinMapear } = emitirDespliegue(ambiente, snapshots.filter((s) => s.ambiente === ambiente), indice);
    writeFileSync(join(values.salida, `deployment.${ambiente}.c4`), dsl);
    if (sinMapear.length > 0) console.log(`deployment.${ambiente}.c4: ${sinMapear.length} workloads sin elemento en el modelo.`);
  }
}
for (const aviso of avisos) console.log(`::warning::${aviso}`);
if (snapshots.length === 0) {
  // Un generador que no encontró nada no debe parecer un éxito: Atlas leería eso como «sin datos» durante semanas.
  console.error('::error::No salió ningún snapshot. Revisa «raices» y «ambientes» en la configuración.');
  process.exit(1);
}
