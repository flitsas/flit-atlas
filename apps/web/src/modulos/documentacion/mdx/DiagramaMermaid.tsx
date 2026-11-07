import { useEffect, useRef, useState } from 'react';

import { useTema } from '../../../app/tema';
import { esMensajeVisorMermaid, LARGO_MAXIMO_MERMAID, type MensajeFuente, RUTA_VISOR_MERMAID } from '../visor/mensajes';

function Fuente({ fuente, aviso }: { fuente: string; aviso: string }) {
  return (
    <figure className="my-3 min-w-0">
      <figcaption className="mb-1 text-xs text-atlas-texto-suave">{aviso}</figcaption>
      <pre className="overflow-x-auto rounded-xl bg-atlas-activo p-3 text-xs">
        <code>{fuente}</code>
      </pre>
    </figure>
  );
}

/**
 * Diagrama Mermaid (ATLAS-ADR-0053 §5). Mermaid inyecta estilos en línea, que la CSP del shell no permite, así que corre con
 * `securityLevel: 'strict'` en `/visor/mermaid.html`, un iframe con `sandbox="allow-scripts"` (origen opaco, sin cookie ni API).
 * Sin conexión, o si la fuente no es válida, se muestra el texto: el contenido nunca se pierde.
 */
export function DiagramaMermaid({ fuente }: { fuente: string }) {
  const marco = useRef<HTMLIFrameElement>(null);
  const listo = useRef(false);
  const { efectivo } = useTema();
  const [alto, setAlto] = useState(160);
  const [error, setError] = useState(false);
  const [enLinea] = useState(() => typeof navigator === 'undefined' || navigator.onLine);
  const largo = fuente.length > LARGO_MAXIMO_MERMAID;

  useEffect(() => {
    if (!enLinea || largo) return;
    const enviar = () => {
      const mensaje: MensajeFuente = { tipo: 'atlas:mermaid-fuente', fuente, oscuro: efectivo === 'oscuro' };
      // El visor tiene origen opaco: no hay un origen que nombrar. Solo se le escribe a su ventana, la del iframe.
      marco.current?.contentWindow?.postMessage(mensaje, '*');
    };
    const alRecibir = (evento: MessageEvent) => {
      if (evento.source !== marco.current?.contentWindow || !esMensajeVisorMermaid(evento.data)) return;
      if (evento.data.tipo === 'atlas:mermaid-listo') {
        listo.current = true;
        enviar();
      } else if (evento.data.tipo === 'atlas:mermaid-pintado') {
        setError(false);
        setAlto(Math.min(Math.max(evento.data.alto, 80), 2000));
      } else setError(true);
    };
    window.addEventListener('message', alRecibir);
    if (listo.current) enviar();
    return () => window.removeEventListener('message', alRecibir);
  }, [fuente, efectivo, enLinea, largo]);

  if (largo) return <Fuente fuente={fuente} aviso="El diagrama es demasiado grande para dibujarlo: esta es su fuente." />;
  if (!enLinea) return <Fuente fuente={fuente} aviso="Sin conexión no se puede dibujar el diagrama: esta es su fuente." />;
  if (error) return <Fuente fuente={fuente} aviso="No pudimos dibujar este diagrama: esta es su fuente." />;
  return (
    <iframe
      ref={marco}
      src={RUTA_VISOR_MERMAID}
      title="Diagrama del documento"
      sandbox="allow-scripts"
      referrerPolicy="no-referrer"
      height={alto}
      className="my-3 w-full rounded-xl border border-border bg-white"
    />
  );
}
