import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';

import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  type EstadoTraspaso,
  fechaCorta,
  importarDocumento,
  previsualizarImportacion,
  TEXTO_TIPO,
  useCambioReparto,
  type VistaPrevia,
} from '../apiResponsables';
import { Advertencia, type Aviso, avisoDeError, claseCampo, Definiciones, MostrarAviso } from '../componentes';

/** Tamaño máximo que acepta el servidor. */
const TAMANO_MAXIMO = 100_000;

/**
 * Importación del documento de responsables (HU 2.3): se sube o pega el Markdown, Atlas lo enlaza con el catálogo y con
 * las personas y muestra la vista previa; lo que no se pudo enlazar queda a la vista y no se inventa. Nada se guarda
 * hasta confirmar, y se confirma exactamente lo que se vio (huella).
 */
export default function Importar() {
  const [markdown, setMarkdown] = useState('');
  const [traspaso, setTraspaso] = useState<EstadoTraspaso>('habilitado');
  const [aviso, setAviso] = useState<Aviso>(null);
  const [vista, setVista] = useState<VistaPrevia | null>(null);
  const previsualizar = useMutation({ mutationFn: previsualizarImportacion });
  const importar = useCambioReparto(importarDocumento);

  const leerArchivo = async (archivo: File | undefined) => {
    if (!archivo) return;
    if (archivo.size > TAMANO_MAXIMO) {
      setAviso({ tono: 'error', texto: 'El documento pesa más de 100 KB.' });
      return;
    }
    setMarkdown(await archivo.text());
    setVista(null);
  };

  return (
    <div className="flex flex-col gap-4">
      <Tarjeta titulo="Importar el documento de responsables">
        <form
          aria-label="Documento de responsables"
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            setAviso(null);
            previsualizar.mutate(markdown, {
              onSuccess: setVista,
              onError: (er) => setAviso(avisoDeError(er, 'No se pudo leer el documento.')),
            });
          }}
        >
          <p className="text-sm text-atlas-texto-suave">
            Sube el Markdown («Responsables por producto y por módulo») o pega su contenido. Las personas se enlazan con el
            directorio por nombre (sin tildes ni mayúsculas) o por correo; los productos y módulos, con el catálogo.
          </p>
          <label className="text-sm font-medium">
            Archivo (.md)
            <input
              type="file"
              accept=".md,text/markdown,text/plain"
              onChange={(e) => void leerArchivo(e.target.files?.[0])}
              className="mt-1 block w-full min-w-0 text-sm file:mr-3 file:min-h-toque file:rounded-xl file:border file:border-border file:bg-card file:px-3"
            />
          </label>
          <label className="text-sm font-medium">
            Contenido
            <textarea
              value={markdown}
              onChange={(e) => {
                setMarkdown(e.target.value);
                setVista(null);
              }}
              rows={8}
              maxLength={TAMANO_MAXIMO}
              spellCheck={false}
              className={`${claseCampo} py-2 font-mono text-sm`}
            />
          </label>
          <Boton type="submit" className="self-start" disabled={!markdown.trim() || previsualizar.isPending}>
            Ver la vista previa
          </Boton>
        </form>
      </Tarjeta>
      <MostrarAviso aviso={aviso} />
      {vista && (
        <Tarjeta titulo="Vista previa">
          <div className="flex flex-col gap-3">
            <p className="text-sm">
              Versión <strong>{vista.version ?? '—'}</strong>
              {vista.fecha && ` del ${fechaCorta(vista.fecha)}`}
              {vista.definidoPor && ` · definió: ${vista.definidoPor}`}
              {vista.versionActual !== null && ` · Atlas va en la ${vista.versionActual}`}
              {vista.proximaRevision && ` · próxima revisión: ${fechaCorta(vista.proximaRevision)}`}
            </p>
            {vista.errores.map((e) => (
              <Advertencia key={e}>{e}</Advertencia>
            ))}
            {vista.pendientes.length > 0 && (
              <section aria-label="Sin enlazar" className="flex flex-col gap-2">
                <h3 className="text-sm font-semibold">Sin enlazar ({vista.pendientes.length}): resuélvelo antes de confirmar</h3>
                <ul className="flex flex-col gap-2 text-sm">
                  {vista.pendientes.map((p) => (
                    <li key={`${p.tipo}-${p.nombre}`} className="rounded-xl border border-border p-3 break-words">
                      <strong>{p.nombre}</strong> ({p.tipo === 'persona' ? 'persona' : 'aplicativo'}, {p.motivo === 'ambiguo' ? 'ambiguo' : 'no encontrado'}):{' '}
                      {p.detalle}
                    </li>
                  ))}
                </ul>
              </section>
            )}
            {vista.inconsistencias.map((i) => (
              <Advertencia key={i}>{i}</Advertencia>
            ))}
            {vista.avisos.map((a) => (
              <Advertencia key={a}>{a}</Advertencia>
            ))}
            <Definiciones definiciones={vista.definiciones} />
            <details>
              <summary className="min-h-toque cursor-pointer text-sm font-medium">{vista.filas.length} responsabilidades enlazadas</summary>
              <ul className="mt-2 flex flex-col gap-1 text-sm">
                {vista.filas.map((f) => (
                  <li key={`${f.aplicativo.id}-${f.tipo}`} className="break-words">
                    <Link to={`/catalogo/${f.aplicativo.slug}`} className="text-atlas-tinta hover:underline">
                      {f.aplicativo.nombre}
                    </Link>{' '}
                    · {TEXTO_TIPO[f.tipo]}: {f.persona} {f.dedicacion !== null && <Insignia tono="neutral">~{f.dedicacion} %</Insignia>}
                  </li>
                ))}
              </ul>
            </details>
            <fieldset className="flex flex-col gap-1">
              <legend className="text-sm font-medium">Secundarios importados</legend>
              {(
                [
                  ['habilitado', 'Habilitados (ya hicieron su traspaso)'],
                  ['en_acompanamiento', 'En acompañamiento (falta el checklist)'],
                ] as const
              ).map(([valor, texto]) => (
                <label key={valor} className="flex min-h-toque items-center gap-2 text-sm">
                  <input type="radio" name="traspaso" checked={traspaso === valor} onChange={() => setTraspaso(valor)} className="size-5" />
                  {texto}
                </label>
              ))}
            </fieldset>
            <Boton
              className="self-start"
              disabled={!vista.puedeConfirmar || importar.isPending}
              onClick={() =>
                importar.mutate(
                  { markdown, huella: vista.huella, traspaso },
                  {
                    onSuccess: (v) => {
                      setVista(null);
                      setAviso({ tono: 'exito', texto: `Importado: el reparto va en la versión ${v.numero}.` });
                    },
                    onError: (er) => setAviso(avisoDeError(er, 'No se pudo importar el documento.')),
                  },
                )
              }
            >
              Confirmar la importación
            </Boton>
          </div>
        </Tarjeta>
      )}
    </div>
  );
}
