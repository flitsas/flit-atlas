// Generador de snapshots de despliegue desde flit-gitops (HU 6.4, plan 01 §11, ATLAS-ADR-0056 §5).
//
// Regla: se lee por lista blanca. De un manifiesto solo salen los campos que este archivo nombra: kind, nombre, réplicas, imagen y
// hosts de Ingress. Nunca se abren Secret, SealedSecret, ConfigMap, EndpointSlice ni variables de entorno, así que no hay nada que
// limpiar después. Lo que sí podría colarse (una IP en un host, un registro interno) se quita aquí y, al final, `revisarSalida` falla
// si queda algo con forma sensible: el generador prefiere no publicar antes que publicar de más. atlas-api vuelve a revisar todo.
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, posix } from 'node:path';

import { parseAllDocuments } from 'yaml';

const KINDS_WORKLOAD = new Set(['Deployment', 'StatefulSet', 'DaemonSet', 'CronJob']);
const SUFIJO_INTERNO = /(?:\.svc(?:\.cluster\.local)?|\.cluster\.local|\.local|\.internal|\.intranet|\.lan|\.corp|\.home)$/i;
const FORMA_IPV4 = /(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])/;
const FORMA_SECRETO =
  /eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.|-----BEGIN [A-Z ]*PRIVATE KEY-----|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bAKIA[0-9A-Z]{16}\b|\bxox[baprs]-[A-Za-z0-9-]{10,}|\b(?:password|passwd|contrase(?:ñ|n)a|secret|token|api[_-]?key)\s*[=:]\s*\S/i;
const FORMATO_HOST = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$/;

export function leerYaml(texto) {
  return parseAllDocuments(texto)
    .map((d) => d.toJS())
    .filter((d) => d && typeof d === 'object');
}

function archivosYaml(carpeta) {
  if (!existsSync(carpeta)) return [];
  return readdirSync(carpeta, { withFileTypes: true })
    .filter((e) => e.isFile() && /\.ya?ml$/i.test(e.name) && e.name !== 'kustomization.yaml' && e.name !== 'kustomization.yml')
    .map((e) => join(carpeta, e.name))
    .sort();
}

/** El host puede quedar en el snapshot: público, con dominio y sin forma de IP. */
export function hostPublico(host) {
  if (typeof host !== 'string') return false;
  const sinComodin = host.startsWith('*.') ? host.slice(2) : host;
  return sinComodin.length <= 253 && FORMATO_HOST.test(sinComodin) && !SUFIJO_INTERNO.test(sinComodin) && !FORMA_IPV4.test(sinComodin);
}

/**
 * `registro/ruta:etiqueta@sha256:…` → `{ imagen, digest }`. Un registro con puerto, IP o nombre interno se quita: queda solo la ruta,
 * porque el host del registro es justo lo que no debe salir.
 */
export function leerImagen(referencia) {
  const [conEtiqueta, digest] = String(referencia).split('@');
  const barra = conEtiqueta.indexOf('/');
  let imagen = conEtiqueta;
  if (barra > 0) {
    const registro = conEtiqueta.slice(0, barra);
    const pareceRegistro = registro.includes('.') || registro.includes(':');
    if (pareceRegistro && (registro.includes(':') || !hostPublico(registro))) imagen = conEtiqueta.slice(barra + 1);
  }
  return { imagen, digest: digest && /^sha256:[0-9a-f]{64}$/.test(digest) ? digest : null };
}

function especificacionPod(manifiesto) {
  return manifiesto.kind === 'CronJob' ? manifiesto.spec?.jobTemplate?.spec?.template?.spec : manifiesto.spec?.template?.spec;
}

/** Lo único que sale de un workload. No se miran env, args, volúmenes, anotaciones ni etiquetas. */
export function leerWorkload(manifiesto) {
  const pod = especificacionPod(manifiesto) ?? {};
  const contenedores = [...(pod.containers ?? []), ...(pod.initContainers ?? [])];
  const imagenes = [...new Map(contenedores.filter((c) => c?.image).map((c) => leerImagen(c.image)).map((i) => [`${i.imagen}@${i.digest}`, i])).values()];
  return {
    kind: manifiesto.kind,
    nombre: manifiesto.metadata?.name,
    replicas: typeof manifiesto.spec?.replicas === 'number' ? manifiesto.spec.replicas : null,
    imagenes,
  };
}

/** Hosts de un Ingress; los internos y las IP se cuentan pero no salen. */
export function leerHosts(ingress) {
  const todos = [...(ingress.spec?.rules ?? []).map((r) => r?.host), ...(ingress.spec?.tls ?? []).flatMap((t) => t?.hosts ?? [])].filter(Boolean);
  const publicos = [...new Set(todos.filter(hostPublico))];
  return { publicos, quitados: new Set(todos).size - publicos.length };
}

/** Renderiza una carpeta: `kustomize build` si es una base de kustomize; si no, sus YAML tal cual. */
export function renderizadorKustomize(comando = ['kustomize', 'build']) {
  return (carpeta) => {
    const tieneKustomization = ['kustomization.yaml', 'kustomization.yml', 'Kustomization'].some((a) => existsSync(join(carpeta, a)));
    if (!tieneKustomization) return archivosYaml(carpeta).map((a) => readFileSync(a, 'utf8')).join('\n---\n');
    const [programa, ...argumentos] = comando;
    // Sin shell: la ruta sale del repo de gitops y no debe interpretarse.
    return execFileSync(programa, [...argumentos, carpeta], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
  };
}

/** Las Application de Argo de una raíz: las de la carpeta a la que apunta su `spec.source.path`. */
function aplicacionesDe(raizRepo, raiz) {
  const fuente = raiz.spec?.source ?? (raiz.spec?.sources ?? []).find((s) => s?.path);
  if (!fuente?.path) return [];
  return archivosYaml(join(raizRepo, fuente.path))
    .flatMap((a) => leerYaml(readFileSync(a, 'utf8')))
    .filter((d) => d.kind === 'Application' && d.metadata?.name);
}

/**
 * @param {object} opciones
 * @param {string} opciones.repo carpeta de flit-gitops
 * @param {object} opciones.config arquitectura-gen.yaml ya leído
 * @param {string} opciones.sha commit de gitops
 * @param {string} opciones.generadoEn fecha ISO
 * @param {(carpeta: string) => string} opciones.renderizar
 * @returns {{ snapshots: object[], avisos: string[] }}
 */
export function generar({ repo, config, sha, generadoEn, renderizar }) {
  const avisos = [];
  const raices = archivosYaml(join(repo, 'bootstrap'))
    .flatMap((a) => leerYaml(readFileSync(a, 'utf8')))
    .filter((d) => d.kind === 'Application' && d.metadata?.name);
  const clusteresDe = config.raices ?? {};
  const ambienteDe = config.ambientes ?? {};
  const resultado = new Map();
  const obtener = (ambiente, cluster, raiz) => {
    const clave = `${ambiente}|${cluster}`;
    if (!resultado.has(clave)) {
      resultado.set(clave, { ambiente, gitopsSha: sha, generadoEn, cluster: { id: cluster, raiz, namespaces: [] }, fueraDeK3s: [], ignorar: config.ignorar ?? [] });
    }
    return resultado.get(clave);
  };

  for (const raiz of raices) {
    const clusteres = clusteresDe[raiz.metadata.name];
    if (!clusteres) {
      avisos.push(`La raíz «${raiz.metadata.name}» no está en «raices» de la configuración: se salta.`);
      continue;
    }
    for (const aplicacion of aplicacionesDe(repo, raiz)) {
      const espacio = aplicacion.spec?.destination?.namespace;
      const ambiente = ambienteDe[espacio];
      if (!ambiente) {
        avisos.push(`El namespace «${espacio}» de «${aplicacion.metadata.name}» no tiene ambiente: se salta.`);
        continue;
      }
      const fuente = aplicacion.spec?.source ?? (aplicacion.spec?.sources ?? []).find((s) => s?.path);
      if (!fuente?.path) {
        avisos.push(`«${aplicacion.metadata.name}» no apunta a una carpeta del repo: se salta.`);
        continue;
      }
      const manifiestos = leerYaml(renderizar(join(repo, ...fuente.path.split(posix.sep))));
      const workloads = manifiestos.filter((m) => KINDS_WORKLOAD.has(m.kind) && m.metadata?.name).map(leerWorkload).sort((a, b) => a.nombre.localeCompare(b.nombre));
      let quitados = 0;
      const hosts = new Set();
      for (const ingress of manifiestos.filter((m) => m.kind === 'Ingress')) {
        const leidos = leerHosts(ingress);
        quitados += leidos.quitados;
        leidos.publicos.forEach((h) => hosts.add(h));
      }
      if (quitados > 0) avisos.push(`«${aplicacion.metadata.name}»: se quitaron ${quitados} hosts internos o IP.`);
      for (const cluster of clusteres) {
        obtener(ambiente, cluster, raiz.metadata.name).cluster.namespaces.push({
          nombre: espacio,
          application: aplicacion.metadata.name,
          workloads,
          ingressHosts: [...hosts].sort(),
        });
      }
    }
  }
  for (const servicio of config.fueraDeK3s ?? []) {
    if (!servicio.ambiente || !servicio.cluster) {
      avisos.push(`«${servicio.nombre}» en fueraDeK3s no dice ambiente y clúster: se salta.`);
      continue;
    }
    obtener(servicio.ambiente, servicio.cluster, null).fueraDeK3s.push({
      nombre: servicio.nombre,
      metodo: servicio.metodo ?? null,
      elementoModelo: servicio.elementoModelo ?? null,
      verificadoEn: servicio.verificadoEn ?? null,
    });
  }
  const snapshots = [...resultado.values()].sort((a, b) => `${a.ambiente}|${a.cluster.id}`.localeCompare(`${b.ambiente}|${b.cluster.id}`));
  for (const s of snapshots) {
    s.cluster.namespaces.sort((a, b) => a.nombre.localeCompare(b.nombre));
    revisarSalida(s);
  }
  return { snapshots, avisos };
}

/** Falla si algo con forma de IP o de secreto llegó a la salida; nombra el campo, nunca el valor (queda en los logs de la CI). */
export function revisarSalida(valor, ruta = '$') {
  if (typeof valor === 'string') {
    if (FORMA_IPV4.test(valor) || FORMA_SECRETO.test(valor)) {
      throw new Error(`Se detuvo la generación: «${ruta}» tiene forma de IP o de secreto. Revisa de dónde sale ese campo.`);
    }
    return;
  }
  if (Array.isArray(valor)) {
    valor.forEach((v, i) => revisarSalida(v, `${ruta}[${i}]`));
    return;
  }
  if (valor && typeof valor === 'object') {
    for (const [k, v] of Object.entries(valor)) revisarSalida(v, `${ruta}.${k}`);
  }
}
