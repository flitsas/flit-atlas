import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { parse } from 'yaml';

import { emitirDespliegue, indiceDeElementos } from './lib/emisor-likec4.mjs';
import { generar, hostPublico, leerHosts, leerImagen, leerWorkload, renderizadorKustomize, revisarSalida } from './lib/generador.mjs';

const aqui = dirname(fileURLToPath(import.meta.url));
const repo = join(aqui, 'fixtures', 'gitops-demo');
const config = parse(readFileSync(join(repo, 'arquitectura-gen.yaml'), 'utf8'));
const SHA = '0123456789abcdef0123456789abcdef01234567';

function generarDemo(renderizar = renderizadorKustomize(['no-hay-kustomize'])) {
  return generar({ repo, config, sha: SHA, generadoEn: '2026-11-06T15:00:00.000Z', renderizar });
}

const kustomizeDisponible = () => spawnSync('kubectl', ['kustomize', '--help'], { stdio: 'ignore' }).status === 0;

test('saca un snapshot por ambiente y clúster, versionado por sha', { skip: !kustomizeDisponible() }, () => {
  const { snapshots } = generarDemo(renderizadorKustomize(['kubectl', 'kustomize']));
  assert.deepEqual(snapshots.map((s) => `${s.ambiente}/${s.cluster.id}`), ['pdn/cluster-a', 'pdn/cluster-b', 'qa/cluster-a']);
  const qa = snapshots.find((s) => s.ambiente === 'qa');
  assert.equal(qa.gitopsSha, SHA);
  assert.equal(qa.cluster.raiz, 'root-demo');
  const espacio = qa.cluster.namespaces[0];
  assert.equal(espacio.nombre, 'demo-qa');
  assert.equal(espacio.application, 'demo-qa');
  assert.deepEqual(espacio.workloads.map((w) => [w.kind, w.nombre]), [['Deployment', 'demo-api'], ['CronJob', 'demo-limpieza'], ['Deployment', 'demo-web']]);
  assert.deepEqual(espacio.ingressHosts, ['app.ejemplo.com']);
  assert.equal(espacio.workloads[2].replicas, 2);
});

test('sin secretos, IP, hosts internos ni registros internos en la salida, aunque el fixture los traiga', { skip: !kustomizeDisponible() }, () => {
  const { snapshots, avisos } = generarDemo(renderizadorKustomize(['kubectl', 'kustomize']));
  const texto = JSON.stringify(snapshots);
  for (const prohibido of [
    'VALOR-SECRETO-FICTICIO',
    'CLAVE_FICTICIA',
    'SELLADO-FICTICIO',
    'configmap-ficticio',
    '192.0.2.10',
    '198.51.100.7',
    'interno.svc.cluster.local',
    'nodo.internal',
    'registro.interno.local',
    'ConfigMap',
    'SealedSecret',
    'EndpointSlice',
    'demo-secreto',
    'anotacion-ficticia',
  ]) {
    assert.ok(!texto.includes(prohibido), `La salida no debe traer «${prohibido}»`);
  }
  assert.ok(avisos.some((a) => a.includes('hosts internos')));
});

test('la salida coincide con el fixture que valida atlas-api (ReglasTests)', { skip: !kustomizeDisponible() }, () => {
  const { snapshots } = generarDemo(renderizadorKustomize(['kubectl', 'kustomize']));
  const qa = snapshots.find((s) => s.ambiente === 'qa');
  const guardado = JSON.parse(readFileSync(join(aqui, 'fixtures', 'snapshot-demo.json'), 'utf8'));
  assert.deepEqual(qa, guardado);
});

test('la salida pasa la revisión final y lo que se cuele la detiene sin citar el valor', () => {
  assert.doesNotThrow(() => revisarSalida({ imagen: 'ghcr.io/x/app:1.59.4', digest: `sha256:${'a'.repeat(64)}` }));
  assert.throws(() => revisarSalida({ a: { b: ['192.0.2.10'] } }), (e) => e.message.includes('$.a.b[0]') && !e.message.includes('192.0.2.10'));
  assert.throws(() => revisarSalida({ a: 'password=x' }), /forma de IP o de secreto/);
});

test('un registro interno se quita de la imagen y el digest se separa', () => {
  const digest = `sha256:${'a'.repeat(64)}`;
  assert.deepEqual(leerImagen(`ghcr.io/org/app:1@${digest}`), { imagen: 'ghcr.io/org/app:1', digest });
  assert.deepEqual(leerImagen('192.0.2.10:5000/org/app:1'), { imagen: 'org/app:1', digest: null });
  assert.deepEqual(leerImagen('registro.interno.local/app'), { imagen: 'app', digest: null });
  assert.deepEqual(leerImagen('nginx:1.30'), { imagen: 'nginx:1.30', digest: null });
  assert.deepEqual(leerImagen('org/app'), { imagen: 'org/app', digest: null });
  assert.equal(leerImagen('ghcr.io/org/app@sha256:corto').digest, null);
});

test('de un workload solo salen los campos de la lista blanca', () => {
  const leido = leerWorkload({
    kind: 'Deployment',
    metadata: { name: 'a', annotations: { x: 'y' } },
    spec: { replicas: 3, template: { spec: { containers: [{ image: 'ghcr.io/o/a', env: [{ name: 'K', value: 'V' }], args: ['--clave=V'] }] } } },
  });
  assert.deepEqual(Object.keys(leido).sort(), ['imagenes', 'kind', 'nombre', 'replicas']);
  assert.deepEqual(Object.keys(leido.imagenes[0]).sort(), ['digest', 'imagen']);
});

test('los hosts públicos pasan; las IP y los internos no', () => {
  assert.ok(hostPublico('app.ejemplo.com'));
  assert.ok(hostPublico('*.ejemplo.com'));
  for (const host of ['192.0.2.10', 'a.svc.cluster.local', 'nodo.internal', 'sinpunto', 'a.lan', '']) assert.ok(!hostPublico(host), host);
  const { publicos, quitados } = leerHosts({ spec: { rules: [{ host: 'app.ejemplo.com' }, { host: '192.0.2.10' }], tls: [{ hosts: ['app.ejemplo.com', 'nodo.internal'] }] } });
  assert.deepEqual(publicos, ['app.ejemplo.com']);
  assert.equal(quitados, 2);
});

test('el modelo de despliegue no lleva hosts ni imágenes y avisa lo que no conoce', { skip: !kustomizeDisponible() }, () => {
  const { snapshots } = generarDemo(renderizadorKustomize(['kubectl', 'kustomize']));
  const modelo = JSON.parse(readFileSync(join(aqui, 'fixtures', 'modelo-demo.export.json'), 'utf8'));
  const indice = indiceDeElementos(modelo);
  assert.equal(indice.get('demo-web'), 'demo.web');
  const { dsl, sinMapear } = emitirDespliegue('qa', snapshots.filter((s) => s.ambiente === 'qa'), indice);
  assert.ok(dsl.includes('demo_web = instanceOf demo.web'));
  assert.ok(dsl.includes('deployment view despliegue_qa'));
  assert.ok(!dsl.includes('ejemplo.com') && !dsl.includes('sha256') && !dsl.includes('ghcr.io'));
  assert.deepEqual(sinMapear, ['demo-qa/demo-limpieza']);
});

test('un namespace sin ambiente o una raíz sin clústeres se avisan, no se inventan', { skip: !kustomizeDisponible() }, () => {
  const { avisos } = generarDemo(renderizadorKustomize(['kubectl', 'kustomize']));
  assert.ok(avisos.some((a) => a.includes('«sin-ambiente»')));
  assert.ok(avisos.some((a) => a.includes('«root-sin-clusteres»')));
});

test('la CLI escribe los archivos y falla si no salió ningún snapshot', { skip: !kustomizeDisponible() }, () => {
  const salida = mkdtempSync(join(tmpdir(), 'arq-gen-'));
  execFileSync(process.execPath, [join(aqui, 'gen.mjs'), '--repo', repo, '--sha', SHA, '--salida', salida, '--kustomize', 'kubectl kustomize',
    '--modelo', join(aqui, 'fixtures', 'modelo-demo.export.json')], { stdio: 'pipe' });
  assert.deepEqual(readdirSync(salida).sort(), ['deployment.pdn.c4', 'deployment.qa.c4', 'snapshot.pdn.cluster-a.json', 'snapshot.pdn.cluster-b.json', 'snapshot.qa.cluster-a.json']);

  const vacio = generar({ repo, config: { ...config, raices: {}, fueraDeK3s: [] }, sha: SHA, generadoEn: 'x', renderizar: () => '' });
  assert.equal(vacio.snapshots.length, 0);
});
