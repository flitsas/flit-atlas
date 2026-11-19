import { LoaderCircle, Share2 } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';

import { BotonCompartir } from '../../../app/ui/Compartir';
import { Boton } from '../../../app/ui/Tarjeta';
import { compartir, descargarComoArchivo, ErrorArchivo, puedeCompartirArchivo, TOPE_ARCHIVO_BYTES } from '../../../pwa/compartir';
import { type ArchivoExportacion, type BaseExportaciones, type Exportacion, pedirDescarga } from './api';

/** Lo que se comparte como enlace: la ruta interna del paquete (nunca la de la exportación, que es de cada persona). */
export interface EnlaceCompartido {
  ruta: string;
  /** Sin título, el nombre de la exportación. */
  titulo?: string;
  que: string;
  etiqueta?: string;
}

type Paso =
  | { tipo: 'inicial' }
  | { tipo: 'preparando' }
  | { tipo: 'listo'; archivo: File }
  | { tipo: 'aviso'; texto: string };

/** El PDF primero (es lo que más se comparte), luego el resto en el orden del servidor. */
function ordenar(archivos: ArchivoExportacion[]): ArchivoExportacion[] {
  return [...archivos].sort((a, b) => Number(b.formato === 'pdf') - Number(a.formato === 'pdf'));
}

/**
 * «Compartir» en el resultado de una exportación lista (HU 4.6). Dos caminos:
 *
 * 1. **El archivo** (si el dispositivo comparte ese tipo, `canShare({ files })`): «Preparar» pide la URL firmada y lo
 *    descarga a memoria con `fetch`; «Compartir» abre la hoja nativa con el `File`. Son dos toques a propósito: Safari
 *    exige que `navigator.share` se llame en el gesto y la descarga lo consumiría. El archivo no vuelve al servidor.
 * 2. **El enlace interno** del paquete (`BotonCompartir`): exige sesión; nunca la URL firmada.
 *
 * Sin hoja nativa para archivos (escritorio, Firefox, un ZIP en Android) solo se ofrece el enlace y descargar.
 */
export function CompartirExportacion({ base, exportacion: e, enlace }: { base: BaseExportaciones; exportacion: Exportacion; enlace: EnlaceCompartido }) {
  const candidatos = ordenar(e.archivos.filter((a) => a.bytes <= TOPE_ARCHIVO_BYTES && puedeCompartirArchivo(a.nombre)));
  const [ruta, setRuta] = useState<string | null>(candidatos[0]?.ruta ?? null);
  const [paso, setPaso] = useState<Paso>({ tipo: 'inicial' });
  const [avisoToque, setAvisoToque] = useState(false);
  const cancelar = useRef<AbortController | null>(null);
  const idSelector = useId();
  const elegido = candidatos.find((a) => a.ruta === ruta) ?? candidatos[0];

  useEffect(() => () => cancelar.current?.abort(), []);

  async function preparar(archivo: ArchivoExportacion) {
    cancelar.current?.abort();
    const control = new AbortController();
    cancelar.current = control;
    setPaso({ tipo: 'preparando' });
    try {
      // El ZIP se pide sin `archivo`; los sueltos, con su ruta.
      const d = await pedirDescarga(base, e.id, archivo.formato === 'zip' ? undefined : archivo.ruta);
      const listo = await descargarComoArchivo(d.url, archivo.nombre, control.signal);
      if (!control.signal.aborted) setPaso({ tipo: 'listo', archivo: listo });
    } catch (error) {
      if (control.signal.aborted) return;
      const grande = error instanceof ErrorArchivo && error.motivo === 'grande';
      setPaso({
        tipo: 'aviso',
        texto: grande
          ? 'El archivo es muy grande para compartirlo desde aquí. Descárgalo y compártelo desde tus archivos.'
          : 'No pudimos preparar el archivo en este dispositivo. Descárgalo y compártelo desde tus archivos, o comparte el enlace.',
      });
    }
  }

  async function compartirArchivo(archivo: File) {
    const resultado = await compartir({ files: [archivo], title: archivo.name });
    if (resultado === 'compartido' || resultado === 'cancelado') return;
    setPaso(
      resultado === 'no_permitido'
        ? { tipo: 'listo', archivo }
        : { tipo: 'aviso', texto: 'Este dispositivo no pudo compartir el archivo. Descárgalo y compártelo desde tus archivos.' },
    );
    if (resultado === 'no_permitido') setAvisoToque(true);
  }

  return (
    <section aria-labelledby={`${idSelector}-titulo`} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
      <h3 id={`${idSelector}-titulo`} className="font-medium">
        Compartir
      </h3>
      {elegido ? (
        <>
          {candidatos.length > 1 && (
            <>
              <label htmlFor={idSelector} className="text-xs text-atlas-texto-suave">
                Archivo para compartir
              </label>
              <select
                id={idSelector}
                value={elegido.ruta}
                onChange={(evento) => {
                  setRuta(evento.target.value);
                  setPaso({ tipo: 'inicial' });
                  setAvisoToque(false);
                }}
                className="min-h-toque w-full min-w-0 rounded-xl border border-border bg-card px-3 text-sm"
              >
                {candidatos.map((a) => (
                  <option key={a.ruta} value={a.ruta}>
                    {a.nombre}
                  </option>
                ))}
              </select>
            </>
          )}
          {paso.tipo === 'listo' ? (
            <Boton
              onClick={() => {
                setAvisoToque(false);
                void compartirArchivo(paso.archivo);
              }}
            >
              <Share2 className="size-4" aria-hidden="true" /> <span className="min-w-0 break-all">Compartir {paso.archivo.name}</span>
            </Boton>
          ) : (
            <Boton variante="secundario" disabled={paso.tipo === 'preparando'} onClick={() => void preparar(elegido)}>
              {paso.tipo === 'preparando' ? <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> : <Share2 className="size-4" aria-hidden="true" />}
              <span className="min-w-0 break-all">{paso.tipo === 'preparando' ? 'Preparando el archivo…' : `Preparar ${elegido.nombre} para compartir`}</span>
            </Boton>
          )}
          <p role="status" aria-live="polite" className="text-xs text-atlas-texto-suave">
            {paso.tipo === 'listo' && !avisoToque && 'Listo: toca «Compartir» para elegir la app.'}
            {paso.tipo === 'listo' && avisoToque && 'Vuelve a tocar «Compartir»: el dispositivo pidió confirmar el gesto.'}
            {paso.tipo === 'aviso' && paso.texto}
          </p>
        </>
      ) : (
        e.archivos.length > 0 && (
          <p className="text-xs text-atlas-texto-suave">Desde este navegador no se comparten archivos: descárgalo y adjúntalo, o comparte el enlace.</p>
        )
      )}
      <BotonCompartir ruta={enlace.ruta} titulo={enlace.titulo ?? e.nombre} que={enlace.que} etiqueta={enlace.etiqueta ?? 'Compartir enlace'} />
    </section>
  );
}
