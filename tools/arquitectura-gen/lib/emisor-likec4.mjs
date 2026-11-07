// Emisor del modelo de despliegue en DSL de LikeC4 (plan 01 §11.3). Va aparte para poder cambiar de herramienta sin tocar el
// generador (plan 01 §4.3). No lleva hosts, imágenes ni digests: un .c4 puede terminar en un repo público; lo sensible vive en el
// snapshot. Solo emite los workloads que el modelo lógico conoce: instanceOf exige un elemento que exista.

const id = (texto) => String(texto).replace(/[^A-Za-z0-9_]/g, '_').replace(/^(\d)/, '_$1');
const cadena = (texto) => `'${String(texto).replace(/\\/g, '\\\\').replace(/'/g, "\\'")}'`;

/** De `likec4 export json`: `k8s_workload` → id del elemento. */
export function indiceDeElementos(modelo) {
  const indice = new Map();
  for (const [elemento, datos] of Object.entries(modelo?.elements ?? {})) {
    const carga = datos?.metadata?.k8s_workload;
    if (typeof carga === 'string') indice.set(carga, elemento);
  }
  return indice;
}

/** Un `deployment { environment … }` y su vista, con todos los clústeres del ambiente. */
export function emitirDespliegue(ambiente, snapshots, indice) {
  const sinMapear = [];
  const lineas = ['deployment {', `  environment ${id(ambiente)} {`];
  for (const snapshot of snapshots) {
    lineas.push(`    ${id(snapshot.cluster.id)} = cluster ${cadena(snapshot.cluster.id)} {`);
    for (const espacio of snapshot.cluster.namespaces) {
      const instancias = espacio.workloads.filter((w) => {
        if (indice.has(w.nombre)) return true;
        sinMapear.push(`${espacio.nombre}/${w.nombre}`);
        return false;
      });
      if (instancias.length === 0) continue;
      lineas.push(`      ${id(espacio.nombre)} = namespace ${cadena(espacio.nombre)} {`);
      for (const w of instancias) lineas.push(`        ${id(w.nombre)} = instanceOf ${indice.get(w.nombre)}`);
      lineas.push('      }');
    }
    lineas.push('    }');
  }
  lineas.push('  }', '}', '', 'views {', `  deployment view despliegue_${id(ambiente)} {`, `    title ${cadena(`Despliegue en ${ambiente}`)}`, `    include ${id(ambiente)}.**`, '  }', '}', '');
  return { dsl: lineas.join('\n'), sinMapear };
}
