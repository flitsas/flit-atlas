/**
 * Servidores a los que puede apuntar la consola «Probar» del visor OpenAPI (prompt maestro §8.4, HU 3.6): solo sandbox
 * y QA, nunca producción ni desarrollo. Se decide con `x-flit-ambiente` del servidor (lo ponen los specs del legado,
 * ATLAS-ADR-0037) o, si no lo trae, con el host. Las variables se resuelven con su valor por defecto y el servidor queda
 * fijo: en la consola nadie puede cambiar el host por el de producción. Es una función pura que usan la pantalla y el
 * visor aislado (`entrada.ts`), que además rechaza cualquier petición fuera de estos prefijos (y la CSP del visor, en
 * nginx, solo deja conectar a los hosts de QA y sandbox).
 */
export interface ServidorSpec {
  url?: unknown;
  description?: unknown;
  variables?: unknown;
  'x-flit-ambiente'?: unknown;
}

export interface ServidorPermitido {
  url: string;
  description: string;
  ambiente: 'qa' | 'sandbox';
}

const AMBIENTES_PERMITIDOS = new Set(['qa', 'sandbox']);
const AMBIENTES_PROHIBIDOS = new Set(['pdn', 'prod', 'produccion', 'production', 'dev', 'desarrollo', 'local']);

/** `qa` o `sandbox` por el host (`ict.qa.flitsas.online`, `sandbox.atlas.flitsas.com`); `null` si no se puede saber. */
export function ambientePorHost(host: string): 'qa' | 'sandbox' | null {
  const partes = host.toLowerCase().split(/[.-]/);
  if (partes.includes('sandbox')) return 'sandbox';
  if (partes.includes('qa')) return 'qa';
  return null;
}

/** `https://{host}/v1` con `host.default` → `https://ict.qa.flitsas.online/v1`; `null` si una variable no tiene valor. */
export function resolverUrl(url: string, variables: unknown): string | null {
  const definidas = (variables && typeof variables === 'object' ? variables : {}) as Record<string, { default?: unknown } | undefined>;
  let faltante = false;
  const resuelta = url.replace(/\{([^}]+)\}/g, (_, nombre: string) => {
    const valor = definidas[nombre]?.default;
    if (typeof valor !== 'string' && typeof valor !== 'number') {
      faltante = true;
      return '';
    }
    return String(valor);
  });
  return faltante ? null : resuelta;
}

export function servidoresPermitidos(servidores: unknown): ServidorPermitido[] {
  if (!Array.isArray(servidores)) return [];
  const resultado: ServidorPermitido[] = [];
  for (const s of servidores as ServidorSpec[]) {
    if (!s || typeof s.url !== 'string') continue;
    const url = resolverUrl(s.url, s.variables);
    if (!url) continue;
    let destino: URL;
    try {
      destino = new URL(url);
    } catch {
      // Rutas relativas (`/api`): apuntarían a Atlas mismo, no a un ambiente.
      continue;
    }
    if (destino.protocol !== 'https:') continue;
    const declarado = typeof s['x-flit-ambiente'] === 'string' ? s['x-flit-ambiente'].toLowerCase() : null;
    if (declarado && AMBIENTES_PROHIBIDOS.has(declarado)) continue;
    const ambiente = declarado && AMBIENTES_PERMITIDOS.has(declarado) ? (declarado as 'qa' | 'sandbox') : declarado ? null : ambientePorHost(destino.hostname);
    if (!ambiente) continue;
    resultado.push({
      url: url.replace(/\/$/, ''),
      description: typeof s.description === 'string' && s.description ? s.description : ambiente === 'qa' ? 'QA' : 'Sandbox',
      ambiente,
    });
  }
  return resultado;
}

/** Si la URL de una petición de la consola cae dentro de uno de los servidores permitidos. */
export function urlPermitida(url: string, permitidos: readonly string[]): boolean {
  let destino: URL;
  try {
    destino = new URL(url);
  } catch {
    return false;
  }
  return permitidos.some((base) => {
    try {
      const b = new URL(base);
      return destino.origin === b.origin && (destino.pathname === b.pathname || destino.pathname.startsWith(b.pathname.replace(/\/?$/, '/')));
    } catch {
      return false;
    }
  });
}
