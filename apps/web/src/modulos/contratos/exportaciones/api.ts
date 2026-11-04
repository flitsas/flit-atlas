import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../../app/api';

/**
 * Exportaciones de paquetes y versiones (HU 4.5, ATLAS-ADR-0042): la PWA solicita, consulta el estado mientras atlas-docs
 * genera los archivos (cola) y pide una URL firmada de 15 minutos para descargar. La misma forma sirve para los
 * internos (`/api/v1/contratos/exportaciones`) y para el portal de clientes (`/api/v1/portal/exportaciones`).
 */
export type Formato = 'openapi' | 'postman' | 'html' | 'pdf' | 'word' | 'excel' | 'zip';

export type EstadoExportacion = 'pendiente' | 'procesando' | 'lista' | 'fallida' | 'vencida';

/** Base de la API: interna (empleados) o del portal (clientes). */
export type BaseExportaciones = '/api/v1/contratos' | '/api/v1/portal';

export interface ArchivoExportacion {
  ruta: string;
  nombre: string;
  formato: Formato;
  bytes: number;
}

export interface Exportacion {
  id: string;
  estado: EstadoExportacion;
  origen: { tipo: 'paquete' | 'version'; paqueteId: string | null; revision: number | null; versionId: string | null };
  nombre: string;
  formatos: Formato[];
  solicitadaEn: string;
  iniciadaEn: string | null;
  listaEn: string | null;
  venceEn: string | null;
  hashContenido: string | null;
  archivos: ArchivoExportacion[];
  error: { codigo: string; mensaje: string } | null;
}

export interface Descarga {
  url: string;
  nombre: string;
  bytes: number;
  venceEn: string;
}

/** De dónde sale: un paquete (revisión opcional: la vigente o, si nunca se publicó, la última) o una versión interna. */
export type OrigenExportacion = { paqueteId: string; revision?: number } | { versionId: string };

/** Los formatos en el orden en que se muestran; el ZIP lo agrega siempre el servidor. */
export const FORMATOS: Array<{ valor: Formato; etiqueta: string }> = [
  { valor: 'openapi', etiqueta: 'OpenAPI (YAML y JSON)' },
  { valor: 'postman', etiqueta: 'Colección Postman' },
  { valor: 'html', etiqueta: 'HTML' },
  { valor: 'pdf', etiqueta: 'PDF' },
  { valor: 'word', etiqueta: 'Word' },
  { valor: 'excel', etiqueta: 'Excel' },
  { valor: 'zip', etiqueta: 'ZIP con todo' },
];

export const ETIQUETA_FORMATO: Record<Formato, string> = Object.fromEntries(FORMATOS.map((f) => [f.valor, f.etiqueta])) as Record<Formato, string>;

export const TERMINALES: ReadonlySet<EstadoExportacion> = new Set(['lista', 'fallida', 'vencida']);

const ruta = (base: BaseExportaciones, id?: string) => `${base}/exportaciones${id ? `/${encodeURIComponent(id)}` : ''}`;

export const CLAVE_EXPORTACIONES = ['contratos', 'exportaciones'] as const;

export async function solicitarExportacion(base: BaseExportaciones, origen: OrigenExportacion, formatos: Formato[]): Promise<Exportacion> {
  // El portal solo exporta paquetes (la revisión la decide la compartición).
  const cuerpo = base === '/api/v1/portal' && 'paqueteId' in origen ? { paqueteId: origen.paqueteId, formatos } : { ...origen, formatos };
  return (await (await enviarJson(ruta(base), 'POST', cuerpo)).json()) as Exportacion;
}

export async function obtenerExportacion(base: BaseExportaciones, id: string): Promise<Exportacion> {
  return (await (await pedir(ruta(base, id))).json()) as Exportacion;
}

export async function listarExportaciones(filtro: { paquete?: string; version?: string }): Promise<Exportacion[]> {
  const parametros = new URLSearchParams();
  if (filtro.paquete) parametros.set('paquete', filtro.paquete);
  if (filtro.version) parametros.set('version', filtro.version);
  const consulta = parametros.toString();
  return ((await (await pedir(`${ruta('/api/v1/contratos')}${consulta ? `?${consulta}` : ''}`)).json()) as { elementos: Exportacion[] }).elementos;
}

/** URL firmada (15 minutos) de un archivo; sin `archivo`, la del ZIP. */
export async function pedirDescarga(base: BaseExportaciones, id: string, archivo?: string): Promise<Descarga> {
  const consulta = archivo ? `?archivo=${encodeURIComponent(archivo)}` : '';
  return (await (await pedir(`${ruta(base, id)}/descarga${consulta}`)).json()) as Descarga;
}

/** Intervalo del sondeo: cada 2 s y, pasados 30 s desde que empezó, cada 5 s; nada en un estado final. */
export function intervaloSondeo(estado: EstadoExportacion | undefined, transcurridoMs: number): number | false {
  if (estado && TERMINALES.has(estado)) return false;
  return transcurridoMs > 30_000 ? 5_000 : 2_000;
}

/** Estado en vivo de una exportación, con sondeo simple hasta que termine (se detiene al desmontar). */
export function useExportacion(base: BaseExportaciones, id: string | null, desde: number) {
  return useQuery({
    queryKey: [...CLAVE_EXPORTACIONES, base, id],
    queryFn: () => obtenerExportacion(base, id!),
    enabled: id !== null,
    refetchInterval: (consulta) => intervaloSondeo(consulta.state.data?.estado, Date.now() - desde),
    refetchIntervalInBackground: false,
  });
}

export function useSolicitarExportacion(base: BaseExportaciones) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: (p: { origen: OrigenExportacion; formatos: Formato[] }) => solicitarExportacion(base, p.origen, p.formatos),
    onSuccess: (e) => cliente.setQueryData([...CLAVE_EXPORTACIONES, base, e.id], e),
  });
}
