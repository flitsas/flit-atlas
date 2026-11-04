import { useEffect, useRef, useState } from 'react';

import { useTema } from '../../../app/tema';
import { esMensajeVisor, type MensajeSpec, RUTA_VISOR } from '../visor/mensajes';
import { servidoresPermitidos } from '../visor/servidores';

/**
 * Referencia interactiva con Scalar (HU 3.6, ATLAS-ADR-0039), solo en escritorio. Scalar corre en `/visor/openapi.html`
 * dentro de un iframe con `sandbox="allow-scripts"` (origen opaco): no ve la cookie del BFF ni la API, y su CSP propia es
 * la única que permite estilos en línea. La pantalla le pasa el spec por `postMessage` con los servidores ya filtrados
 * (solo QA y sandbox, nunca pdn), así que «Probar» no puede apuntar a producción.
 */
export function VisorScalar(props: { contenido: string; servidores: unknown; titulo: string }) {
  // HU 4.9: el iframe del visor tiene origen opaco, así que el service worker no lo controla ni lo precarga: sin red no se
  // puede abrir (si ya estaba abierto, sigue funcionando). Lo guardado de un favorito se ve en «Operaciones».
  const [disponible] = useState(() => typeof navigator === 'undefined' || navigator.onLine);
  if (!disponible) {
    return (
      <p role="note" className="rounded-xl border border-border bg-card p-4 text-sm">
        Sin conexión, la referencia interactiva no se puede abrir: corre aislada y necesita descargar el visor. El spec guardado
        se ve completo en la pestaña «Operaciones».
      </p>
    );
  }
  return <VisorEnLinea {...props} />;
}

function VisorEnLinea({ contenido, servidores, titulo }: { contenido: string; servidores: unknown; titulo: string }) {
  const marco = useRef<HTMLIFrameElement>(null);
  const [estado, setEstado] = useState<'cargando' | 'listo' | 'error'>('cargando');
  const { efectivo } = useTema();
  const listo = useRef(false);
  const permitidos = servidoresPermitidos(servidores);

  useEffect(() => {
    const enviar = () => {
      const mensaje: MensajeSpec = {
        tipo: 'atlas:visor-spec',
        contenido,
        servidores: servidoresPermitidos(servidores).map((s) => ({ url: s.url, description: `${s.description} (${s.ambiente === 'qa' ? 'QA' : 'sandbox'})` })),
        oscuro: efectivo === 'oscuro',
      };
      // El visor tiene origen opaco: no hay un origen que nombrar. Solo se le escribe a su ventana, la del iframe.
      marco.current?.contentWindow?.postMessage(mensaje, '*');
    };
    function alRecibir(evento: MessageEvent) {
      if (evento.source !== marco.current?.contentWindow || !esMensajeVisor(evento.data)) return;
      if (evento.data.tipo === 'atlas:visor-listo') {
        listo.current = true;
        enviar();
        setEstado('listo');
      } else setEstado('error');
    }
    window.addEventListener('message', alRecibir);
    // Si el visor ya estaba listo (cambió la versión o el tema), se le manda de nuevo.
    if (listo.current) enviar();
    return () => window.removeEventListener('message', alRecibir);
  }, [contenido, servidores, efectivo]);

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <p className="text-xs text-atlas-texto-suave">
        {permitidos.length > 0
          ? `«Probar» solo llama a ${permitidos.map((s) => s.url).join(', ')} (QA o sandbox). Nunca a producción.`
          : 'Este contrato no declara servidores de QA ni de sandbox: la consola «Probar» está desactivada.'}
      </p>
      {estado === 'error' && (
        <p role="alert" className="rounded-xl border border-border bg-card p-3 text-sm">
          El visor no pudo mostrar este spec. Usa la pestaña «Operaciones».
        </p>
      )}
      <iframe
        ref={marco}
        src={RUTA_VISOR}
        title={`Referencia interactiva de ${titulo}`}
        sandbox="allow-scripts"
        referrerPolicy="no-referrer"
        className="h-[80vh] w-full rounded-2xl border border-border bg-white"
      />
    </div>
  );
}
