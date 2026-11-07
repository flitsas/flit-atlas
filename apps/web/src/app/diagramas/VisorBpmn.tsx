import { useEffect, useRef, useState } from 'react';

import { esMensajeVisorBpmn, LARGO_MAXIMO_BPMN, type MensajeXmlBpmn, RUTA_VISOR_BPMN } from './mensajesBpmn';

/**
 * Proceso BPMN (ATLAS-ADR-0054 §5). `bpmn-js` inyecta estilos en línea, que la CSP del shell no permite, así que corre en un
 * iframe con `sandbox="allow-scripts"`. El XML que devuelve el editor no se da por bueno: lo valida y lo limpia el servidor.
 * Sin conexión, o si no se puede dibujar, se avisa: leer no pierde el contenido, que sigue en la versión.
 */
export function VisorBpmn({ xml, titulo, editable = false, alCambiar }: { xml: string; titulo: string; editable?: boolean; alCambiar?: (xml: string) => void }) {
  const marco = useRef<HTMLIFrameElement>(null);
  const listo = useRef(false);
  const [error, setError] = useState(false);
  const inicial = useRef(xml);
  const largo = xml.length > LARGO_MAXIMO_BPMN;
  // En el editor, el visor es la fuente de los cambios: volver a mandarle lo que acaba de escribir reiniciaría la edición.
  const contenido = editable ? inicial.current : xml;

  useEffect(() => {
    if (largo) return;
    const enviar = () => {
      const mensaje: MensajeXmlBpmn = { tipo: 'atlas:bpmn-xml', xml: contenido, editable };
      // El visor tiene origen opaco: no hay un origen que nombrar. Solo se le escribe a su ventana, la del iframe.
      marco.current?.contentWindow?.postMessage(mensaje, '*');
    };
    const alRecibir = (evento: MessageEvent) => {
      if (evento.source !== marco.current?.contentWindow || !esMensajeVisorBpmn(evento.data)) return;
      if (evento.data.tipo === 'atlas:bpmn-listo') {
        listo.current = true;
        enviar();
      } else if (evento.data.tipo === 'atlas:bpmn-pintado') setError(false);
      else if (evento.data.tipo === 'atlas:bpmn-error') setError(true);
      else alCambiar?.(evento.data.xml);
    };
    window.addEventListener('message', alRecibir);
    if (listo.current) enviar();
    return () => window.removeEventListener('message', alRecibir);
  }, [contenido, editable, largo, alCambiar]);

  if (largo || error) return <p className="my-3 rounded-xl border border-border bg-card p-3 text-sm text-atlas-texto-suave">No pudimos dibujar este proceso. Revisa tu conexión.</p>;
  return (
    <iframe
      ref={marco}
      src={RUTA_VISOR_BPMN}
      title={titulo}
      sandbox="allow-scripts"
      referrerPolicy="no-referrer"
      height={editable ? 520 : 420}
      className="my-3 w-full rounded-xl border border-border bg-white"
    />
  );
}
