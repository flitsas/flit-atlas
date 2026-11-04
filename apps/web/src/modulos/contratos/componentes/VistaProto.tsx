import { Tarjeta } from '../../../app/ui/Tarjeta';
import type { OperacionIndice } from '../api';
import type { DocumentoProto, MensajeProto } from '../spec/proto';
import { Codigo, Etiqueta, TablaResponsiva } from './ui';

/**
 * Vista de un proto (HU 3.6): servicios con sus rpc (del texto y del índice de operaciones que armó el servidor al
 * publicar), mensajes con sus campos y el archivo completo para copiar.
 */
export function VistaProto({ documento: d, indice, texto }: { documento: DocumentoProto; indice: OperacionIndice[]; texto: string }) {
  const nombres = new Set(d.mensajes.map((m) => m.nombre));
  // rpc que el índice conoce y el lector de la PWA no reconoció (sintaxis que no entiende): igual se listan.
  const conocidos = new Set(d.servicios.flatMap((s) => s.rpcs.map((r) => `${d.paquete ? `${d.paquete}.` : ''}${s.nombre}/${r.nombre}`)));
  const extra = indice.filter((o) => o.clase === 'rpc' && !conocidos.has(o.clave.replace(/^rpc:/, '')));
  const enlace = (tipo: string) =>
    nombres.has(tipo) ? (
      <a href={`#proto-${tipo}`} className="font-mono text-xs text-atlas-tinta hover:underline">
        {tipo}
      </a>
    ) : (
      <code className="font-mono text-xs break-all">{tipo}</code>
    );

  return (
    <div className="flex min-w-0 flex-col gap-4">
      {d.paquete && (
        <p className="text-sm text-atlas-texto-suave">
          Paquete <code className="font-mono">{d.paquete}</code>
        </p>
      )}
      {d.servicios.map((s) => (
        <Tarjeta key={s.nombre} titulo={`Servicio ${s.nombre}`}>
          {s.comentario && <p className="mb-2 text-sm text-atlas-texto-suave">{s.comentario}</p>}
          <ul className="flex flex-col divide-y divide-border">
            {s.rpcs.map((r) => (
              <li key={r.nombre} className="flex min-w-0 flex-col gap-1 py-2 text-sm">
                <span className="flex flex-wrap items-center gap-2">
                  <Etiqueta tono="info" className="font-mono">
                    rpc
                  </Etiqueta>
                  <span className="font-semibold">{r.nombre}</span>
                  {(r.flujoEntrada || r.flujoSalida) && <Etiqueta tono="alerta">stream</Etiqueta>}
                </span>
                <span className="flex flex-wrap items-center gap-1 text-xs">
                  {r.flujoEntrada && 'stream '}
                  {enlace(r.entrada)} <span aria-hidden="true">→</span>
                  <span className="sr-only">devuelve</span> {r.flujoSalida && 'stream '}
                  {enlace(r.salida)}
                </span>
                {r.comentario && <span className="text-atlas-texto-suave">{r.comentario}</span>}
              </li>
            ))}
          </ul>
        </Tarjeta>
      ))}
      {extra.length > 0 && (
        <Tarjeta titulo="Otros rpc del índice">
          <ul className="flex flex-col gap-1 text-sm">
            {extra.map((o) => (
              <li key={o.clave}>
                <code className="font-mono text-xs break-all">{o.clave}</code>
              </li>
            ))}
          </ul>
        </Tarjeta>
      )}
      <section aria-labelledby="titulo-mensajes-proto" className="flex flex-col gap-3">
        <h3 id="titulo-mensajes-proto" className="text-base font-semibold">
          Mensajes
        </h3>
        {d.mensajes.map((m) => (
          <TarjetaMensaje key={m.nombre} mensaje={m} />
        ))}
      </section>
      <details className="rounded-2xl border border-border bg-card">
        <summary className="flex min-h-toque cursor-pointer items-center px-4 text-sm font-medium">Archivo .proto completo</summary>
        <div className="p-3">
          <Codigo titulo="proto" codigo={texto} />
        </div>
      </details>
    </div>
  );
}

function TarjetaMensaje({ mensaje: m }: { mensaje: MensajeProto }) {
  return (
    <div id={`proto-${m.nombre}`} className="flex min-w-0 scroll-mt-4 flex-col gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <h4 className="flex flex-wrap items-center gap-2 text-sm font-semibold">
        <Etiqueta>{m.clase === 'enum' ? 'enum' : 'message'}</Etiqueta>
        <span className="break-all">{m.nombre}</span>
      </h4>
      {m.comentario && <p className="text-sm text-atlas-texto-suave">{m.comentario}</p>}
      <TablaResponsiva
        titulo={`Campos de ${m.nombre}`}
        filas={m.campos}
        clave={(c) => `${c.nombre}-${c.numero}`}
        vacio="Sin campos."
        columnas={
          m.clase === 'enum'
            ? [
                { id: 'nombre', titulo: 'Valor', principal: true, celda: (c) => <code className="font-mono text-xs">{c.nombre}</code> },
                { id: 'numero', titulo: 'Número', className: 'w-24', celda: (c) => c.numero },
                { id: 'comentario', titulo: 'Descripción', celda: (c) => c.comentario ?? '—' },
              ]
            : [
                { id: 'nombre', titulo: 'Campo', principal: true, className: 'w-48', celda: (c) => <code className="font-mono text-xs break-all">{c.nombre}</code> },
                { id: 'tipo', titulo: 'Tipo', className: 'w-56', celda: (c) => <code className="font-mono text-xs break-all">{c.etiqueta && c.etiqueta !== 'map' ? `${c.etiqueta} ${c.tipo}` : c.tipo}</code> },
                { id: 'numero', titulo: 'Número', className: 'w-20', celda: (c) => c.numero },
                { id: 'comentario', titulo: 'Descripción', celda: (c) => c.comentario ?? '—' },
              ]
        }
      />
    </div>
  );
}
