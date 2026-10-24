import { useMemo, useState } from 'react';

import { Tarjeta } from '../../../app/ui/Tarjeta';
import type { DocumentoAsync, MensajeAsync } from '../spec/asyncapi';
import { normalizar } from '../spec/referencia';
import { TablaCampos } from './DetalleOperacion';
import { Buscador, Codigo, Etiqueta, Metodo } from './ui';

/**
 * Vista legible de un AsyncAPI (HU 3.6): operaciones con su canal y sus mensajes, canales, y cada mensaje con sus campos
 * y un ejemplo. Propia y simple (ATLAS-ADR-0039): el componente oficial no cabe en la CSP.
 */
export function VistaAsyncApi({ documento: d }: { documento: DocumentoAsync }) {
  const [texto, setTexto] = useState('');
  const buscado = normalizar(texto.trim());
  const coincide = (...partes: Array<string | undefined>) => !buscado || normalizar(partes.filter(Boolean).join(' ')).includes(buscado);
  const porId = useMemo(() => new Map(d.mensajes.map((m) => [m.id, m])), [d.mensajes]);
  const operaciones = d.operaciones.filter((o) => coincide(o.id, o.canal, o.resumen, o.descripcion));
  const mensajes = d.mensajes.filter((m) => coincide(m.id, m.nombre, m.resumen, m.descripcion));

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <p className="text-sm text-atlas-texto-suave">
        AsyncAPI {d.version} · {d.canales.length === 1 ? '1 canal' : `${d.canales.length} canales`} · {d.operaciones.length === 1 ? '1 operación' : `${d.operaciones.length} operaciones`} ·{' '}
        {d.mensajes.length === 1 ? '1 mensaje' : `${d.mensajes.length} mensajes`}
      </p>
      <Buscador etiqueta="Buscar en el AsyncAPI" valor={texto} alCambiar={setTexto} placeholder="Canal, operación o mensaje" />

      <Tarjeta titulo="Operaciones">
        {operaciones.length === 0 ? (
          <p className="text-sm text-atlas-texto-suave">Sin operaciones que coincidan.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border">
            {operaciones.map((o) => (
              <li key={o.id} className="flex min-w-0 flex-col gap-1 py-2 text-sm">
                <span className="flex min-w-0 flex-wrap items-center gap-2">
                  <Metodo metodo={o.accion} />
                  <code className="font-mono text-xs break-all">{o.canal}</code>
                  <span className="text-xs text-atlas-texto-suave">{o.id}</span>
                  {o.etiquetas.map((e) => (
                    <Etiqueta key={e}>{e}</Etiqueta>
                  ))}
                </span>
                {(o.resumen ?? o.descripcion) && <span className="text-atlas-texto-suave">{o.resumen ?? o.descripcion}</span>}
                {o.mensajes.length > 0 && (
                  <span className="text-xs">
                    Mensajes:{' '}
                    {o.mensajes.map((id, i) => (
                      <span key={id}>
                        {i > 0 && ', '}
                        <a href={`#mensaje-${id}`} className="text-atlas-tinta hover:underline">
                          {porId.get(id)?.nombre ?? id}
                        </a>
                      </span>
                    ))}
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>

      <Tarjeta titulo="Canales">
        <ul className="flex flex-col divide-y divide-border">
          {d.canales.map((c) => (
            <li key={c.id} className="flex min-w-0 flex-col gap-0.5 py-2 text-sm">
              <code className="font-mono text-xs break-all">{c.direccion}</code>
              {c.descripcion && <span className="text-atlas-texto-suave">{c.descripcion}</span>}
              <span className="text-xs text-atlas-texto-suave">{c.mensajes.length ? `Mensajes: ${c.mensajes.map((m) => porId.get(m)?.nombre ?? m).join(', ')}` : 'Sin mensajes declarados.'}</span>
            </li>
          ))}
        </ul>
      </Tarjeta>

      <section aria-labelledby="titulo-mensajes" className="flex flex-col gap-3">
        <h3 id="titulo-mensajes" className="text-base font-semibold">
          Mensajes
        </h3>
        {mensajes.map((m) => (
          <TarjetaMensaje key={m.id} mensaje={m} />
        ))}
      </section>

      {d.esquemas.length > 0 && (
        <Tarjeta titulo="Esquemas">
          <ul className="flex flex-col gap-2">
            {d.esquemas.map((e) => (
              <li key={e.nombre}>
                <details className="rounded-xl border border-border">
                  <summary className="flex min-h-toque cursor-pointer items-center px-3 text-sm font-medium">{e.nombre}</summary>
                  <div className="border-t border-border p-3">
                    <TablaCampos titulo={`Campos de ${e.nombre}`} campos={e.campos} vacio="Sin propiedades." />
                  </div>
                </details>
              </li>
            ))}
          </ul>
        </Tarjeta>
      )}
    </div>
  );
}

function TarjetaMensaje({ mensaje: m }: { mensaje: MensajeAsync }) {
  return (
    <div id={`mensaje-${m.id}`} className="flex min-w-0 scroll-mt-4 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <div className="flex flex-wrap items-center gap-2">
        <h4 className="text-sm font-semibold">{m.nombre}</h4>
        {m.tipoContenido && <Etiqueta>{m.tipoContenido}</Etiqueta>}
      </div>
      {(m.resumen ?? m.descripcion) && <p className="text-sm text-atlas-texto-suave">{m.resumen ?? m.descripcion}</p>}
      <TablaCampos titulo={`Campos de ${m.nombre}`} campos={m.campos} vacio="La carga útil no describe campos." />
      {m.ejemplo !== undefined && <Codigo titulo="Ejemplo de carga útil" codigo={JSON.stringify(m.ejemplo, null, 2)} />}
    </div>
  );
}
