import { parse } from 'yaml';

/**
 * Lectura de specs OpenAPI y AsyncAPI en la PWA (HU 3.6 y 3.9): el texto tal como se publicó (JSON o YAML) a un objeto, y
 * la resolución de `$ref` locales. Va en el chunk de la pantalla del contrato (el de `yaml` incluido), nunca en el inicial.
 * Las referencias externas no se siguen: el visor no hace peticiones por el contenido del spec.
 */
export type Objeto = Record<string, unknown>;

export const esObjeto = (valor: unknown): valor is Objeto => typeof valor === 'object' && valor !== null && !Array.isArray(valor);

/** El spec como objeto; `null` si no se puede leer. Los alias de YAML tienen tope (una bomba de alias no cuelga la pestaña). */
export function leerSpec(texto: string): Objeto | null {
  try {
    const recortado = texto.trimStart();
    const valor: unknown = recortado.startsWith('{') ? JSON.parse(recortado) : parse(texto, { maxAliasCount: 200, prettyErrors: false });
    return esObjeto(valor) ? valor : null;
  } catch {
    return null;
  }
}

/** `#/components/schemas/Tramite` → el nodo; `undefined` si no es local o no existe. */
export function seguirPuntero(raiz: Objeto, ref: string): unknown {
  if (!ref.startsWith('#/')) return undefined;
  let nodo: unknown = raiz;
  for (const crudo of ref.slice(2).split('/')) {
    const parte = decodeURIComponent(crudo).replace(/~1/g, '/').replace(/~0/g, '~');
    if (!esObjeto(nodo) && !Array.isArray(nodo)) return undefined;
    nodo = (nodo as Record<string, unknown>)[parte];
  }
  return nodo;
}

/**
 * Resuelve un `$ref` local (en cadena, con tope para los ciclos). Las propiedades hermanas del `$ref` (OpenAPI 3.1 deja
 * `description` junto al `$ref`) se conservan encima del nodo resuelto.
 */
export function resolver(raiz: Objeto, nodo: unknown, saltos = 0): unknown {
  if (!esObjeto(nodo) || typeof nodo.$ref !== 'string' || saltos > 20) return nodo;
  const { $ref, ...hermanas } = nodo;
  const destino = resolver(raiz, seguirPuntero(raiz, $ref as string), saltos + 1);
  if (!esObjeto(destino)) return Object.keys(hermanas).length > 0 ? hermanas : destino;
  return Object.keys(hermanas).length > 0 ? { ...destino, ...hermanas } : destino;
}

/** El último segmento de un `$ref` local (`#/components/schemas/Tramite` → `Tramite`). */
export const nombreDeRef = (ref: string): string => ref.split('/').at(-1) ?? ref;

export const texto = (valor: unknown): string | undefined => (typeof valor === 'string' && valor.trim() ? valor : undefined);
