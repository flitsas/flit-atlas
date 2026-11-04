import { HTTPSnippet } from '@readme/httpsnippet';

import type { OperacionOpenApi } from './openapi';

/**
 * Ejemplos de código de una operación (HU 3.6, prompt maestro §8.4) con httpsnippet (`@readme/httpsnippet`, el sucesor
 * mantenido de `httpsnippet`) en curl, C#, Python, Go y JS. Nunca llevan valores reales: la URL base es `{{baseUrl}}`
 * (en un webhook, `{{urlWebhook}}`, la del gestor), el token `$TOKEN`, las llaves `$API_KEY` y los parámetros
 * `{{nombre}}`; el cuerpo es el ejemplo saneado del spec.
 */
export const LENGUAJES = [
  { id: 'curl', etiqueta: 'curl', objetivo: 'shell', cliente: 'curl' },
  { id: 'csharp', etiqueta: 'C#', objetivo: 'csharp', cliente: 'httpclient' },
  { id: 'python', etiqueta: 'Python', objetivo: 'python', cliente: 'requests' },
  { id: 'go', etiqueta: 'Go', objetivo: 'go', cliente: 'native' },
  { id: 'js', etiqueta: 'JavaScript', objetivo: 'javascript', cliente: 'fetch' },
] as const;

export type IdLenguaje = (typeof LENGUAJES)[number]['id'];

// Un origen que no existe (`.invalid`, RFC 2606) y que se cambia por `{{baseUrl}}` en el texto generado.
const BASE = 'https://base-url.invalid';

const variable = (nombre: string) => `{{${nombre}}}`;

function restaurar(codigo: string, webhook: boolean): string {
  return codigo
    .split(webhook ? `${BASE}/` : BASE)
    .join(webhook ? '{{urlWebhook}}' : '{{baseUrl}}')
    .replace(/%7B/gi, '{')
    .replace(/%7D/gi, '}')
    .replace(/%24/g, '$');
}

/** Ejemplo por lenguaje; si httpsnippet no puede con uno, queda un aviso en lugar del código. */
export function ejemplosDe(operacion: OperacionOpenApi): Record<IdLenguaje, string> {
  const ruta = operacion.esWebhook ? '/' : operacion.ruta.replace(/\{([^}]+)\}/g, (_, nombre: string) => variable(nombre));
  const consulta = operacion.parametros
    .filter((p) => p.ubicacion === 'query' && p.obligatorio)
    .map((p) => ({ name: p.nombre, value: variable(p.nombre) }));
  const cabeceras: Array<{ name: string; value: string }> = [];
  if (operacion.requiereToken) cabeceras.push({ name: 'Authorization', value: 'Bearer $TOKEN' });
  for (const llave of operacion.llaves) cabeceras.push({ name: llave, value: '$API_KEY' });
  for (const c of operacion.cabeceras.filter((c) => c.obligatorio && c.nombre.toLowerCase() !== 'authorization')) {
    cabeceras.push({ name: c.nombre, value: variable(c.nombre) });
  }

  let postData: { mimeType: string; text?: string; params?: Array<{ name: string; value?: string; fileName?: string }> } | undefined;
  const cuerpo = operacion.cuerpo;
  if (cuerpo) {
    if (/multipart|form-urlencoded/i.test(cuerpo.tipoContenido)) {
      postData = {
        mimeType: cuerpo.tipoContenido,
        params: cuerpo.campos
          .filter((c) => c.profundidad === 0)
          .map((c) => (/binary|base64/.test(c.tipo) ? { name: c.nombre, fileName: 'archivo.pdf' } : { name: c.nombre, value: variable(c.nombre) })),
      };
    } else {
      const textoCuerpo = cuerpo.ejemplo === undefined ? '' : typeof cuerpo.ejemplo === 'string' ? cuerpo.ejemplo : JSON.stringify(cuerpo.ejemplo, null, 2);
      postData = { mimeType: cuerpo.tipoContenido, text: textoCuerpo };
      cabeceras.push({ name: 'Content-Type', value: cuerpo.tipoContenido });
    }
  }

  const salida = {} as Record<IdLenguaje, string>;
  for (const l of LENGUAJES) {
    try {
      const snippet = new HTTPSnippet({
        method: operacion.metodo,
        url: `${BASE}${ruta}`,
        httpVersion: 'HTTP/1.1',
        headers: cabeceras,
        queryString: consulta,
        cookies: [],
        headersSize: -1,
        bodySize: -1,
        ...(postData ? { postData } : {}),
      } as ConstructorParameters<typeof HTTPSnippet>[0]);
      const [codigo] = snippet.convert(l.objetivo as never, l.cliente as never, { indent: '  ' } as never);
      salida[l.id] = typeof codigo === 'string' ? restaurar(codigo, operacion.esWebhook) : 'No hay ejemplo para este lenguaje.';
    } catch {
      salida[l.id] = 'No se pudo generar el ejemplo para este lenguaje.';
    }
  }
  return salida;
}
