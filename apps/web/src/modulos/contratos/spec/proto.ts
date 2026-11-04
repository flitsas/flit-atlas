/**
 * Vista de un proto (HU 3.6): paquete, servicios con sus rpc y mensajes con sus campos, leídos del texto del archivo.
 * El servidor ya valida el proto al publicarlo (protobuf-net, ATLAS-ADR-0035) y su índice trae los rpc; aquí solo se
 * reconoce lo necesario para mostrarlo (sin opciones ni extensiones) y se tolera lo que no se entienda.
 */
export interface RpcProto {
  nombre: string;
  entrada: string;
  salida: string;
  flujoEntrada: boolean;
  flujoSalida: boolean;
  comentario?: string;
}

export interface ServicioProto {
  nombre: string;
  rpcs: RpcProto[];
  comentario?: string;
}

export interface CampoProto {
  nombre: string;
  tipo: string;
  numero: string;
  etiqueta?: 'repeated' | 'optional' | 'map';
  comentario?: string;
}

export interface MensajeProto {
  nombre: string;
  campos: CampoProto[];
  /** `enum` o `message`. */
  clase: 'message' | 'enum';
  comentario?: string;
}

export interface DocumentoProto {
  paquete?: string;
  servicios: ServicioProto[];
  mensajes: MensajeProto[];
}

/**
 * Una sentencia por línea (`message A { string id = 1; }` → tres líneas), sin comentarios de bloque; el comentario de
 * línea queda pegado a la primera sentencia de su línea.
 */
function lineas(textoProto: string): string[] {
  return textoProto
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split(/\r?\n/)
    .flatMap((cruda) => {
      const [codigo = '', ...resto] = cruda.split('//');
      const nota = resto.join('//').trim();
      const partes = codigo
        .replace(/\{/g, '{\n')
        .replace(/;/g, ';\n')
        .replace(/\}/g, '\n}\n')
        .split('\n')
        .map((p) => p.trim())
        .filter(Boolean);
      if (partes.length === 0) return [nota ? `//${nota}` : ''];
      return partes.map((p, i) => (i === 0 && nota ? `${p} //${nota}` : p));
    });
}

export function leerProto(textoProto: string): DocumentoProto {
  const paquete = /^\s*package\s+([\w.]+)\s*;/m.exec(textoProto)?.[1];
  const servicios: ServicioProto[] = [];
  const mensajes: MensajeProto[] = [];
  const pila: Array<{ clase: 'service' | 'message' | 'enum' | 'otro'; nombre: string; destino?: ServicioProto | MensajeProto }> = [];
  let comentario: string | undefined;

  for (const cruda of lineas(textoProto)) {
    const [codigo = '', ...resto] = cruda.split('//');
    const nota = resto.join('//').trim() || undefined;
    const linea = codigo.trim();
    if (!linea) {
      comentario = nota ?? (cruda.trim() ? comentario : undefined);
      continue;
    }
    const apertura = /^(service|message|enum|oneof|extend)\s+([\w.]+)\s*\{/.exec(linea);
    if (apertura) {
      const [, clase, nombre] = apertura as unknown as [string, string, string];
      const padre = pila.filter((p) => p.clase === 'message').map((p) => p.nombre);
      if (clase === 'service') {
        const s: ServicioProto = { nombre, rpcs: [], comentario };
        servicios.push(s);
        pila.push({ clase: 'service', nombre, destino: s });
      } else if (clase === 'message' || clase === 'enum') {
        const m: MensajeProto = { nombre: [...padre, nombre].join('.'), campos: [], clase, comentario };
        mensajes.push(m);
        pila.push({ clase, nombre, destino: m });
      } else {
        // oneof: sus campos son del mensaje que lo contiene.
        const contenedor = [...pila].reverse().find((p) => p.clase === 'message');
        pila.push({ clase: 'otro', nombre, destino: contenedor?.destino });
      }
      comentario = undefined;
      continue;
    }
    const actual = pila.at(-1);
    const rpc = /^rpc\s+(\w+)\s*\(\s*(stream\s+)?([\w.]+)\s*\)\s*returns\s*\(\s*(stream\s+)?([\w.]+)\s*\)/.exec(linea);
    if (rpc && actual?.clase === 'service') {
      (actual.destino as ServicioProto).rpcs.push({
        nombre: rpc[1]!,
        entrada: rpc[3]!,
        salida: rpc[5]!,
        flujoEntrada: !!rpc[2],
        flujoSalida: !!rpc[4],
        comentario: nota ?? comentario,
      });
    } else if (actual && actual.destino && 'campos' in actual.destino) {
      const destino = actual.destino;
      const valorEnum = /^(\w+)\s*=\s*(-?\d+)/.exec(linea);
      const campoMapa = /^map\s*<\s*([\w.]+)\s*,\s*([\w.]+)\s*>\s+(\w+)\s*=\s*(\d+)/.exec(linea);
      const campo = /^(repeated\s+|optional\s+|required\s+)?([\w.]+)\s+(\w+)\s*=\s*(\d+)/.exec(linea);
      if (destino.clase === 'enum' && valorEnum) destino.campos.push({ nombre: valorEnum[1]!, tipo: '', numero: valorEnum[2]!, comentario: nota ?? comentario });
      else if (campoMapa) destino.campos.push({ nombre: campoMapa[3]!, tipo: `map<${campoMapa[1]}, ${campoMapa[2]}>`, numero: campoMapa[4]!, etiqueta: 'map', comentario: nota ?? comentario });
      else if (campo && campo[2] !== 'option' && campo[2] !== 'reserved') {
        const etiqueta = campo[1]?.trim();
        destino.campos.push({
          nombre: campo[3]!,
          tipo: campo[2]!,
          numero: campo[4]!,
          etiqueta: etiqueta === 'repeated' || etiqueta === 'optional' ? etiqueta : undefined,
          comentario: nota ?? comentario,
        });
      }
    }
    comentario = undefined;
    for (const _ of linea.match(/\}/g) ?? []) pila.pop();
  }
  return { paquete, servicios, mensajes };
}
