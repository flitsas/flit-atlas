import { useEffect, useRef, useState } from 'react';

import { useTema } from '../../app/tema';
import { esMensajeVisorLikeC4, type MensajeModelo, RUTA_VISOR_LIKEC4 } from './visor/mensajes';

/**
 * Una vista de LikeC4 (ATLAS-ADR-0056 §7). Va en un iframe con `sandbox="allow-scripts"` (origen opaco, sin cookie ni API) y su propia CSP,
 * así que el visor y sus 3 MB no están en el bundle inicial ni en la precarga del service worker: se bajan al abrir la primera vista.
 */
export function VisorLikeC4({
  clave,
  modelo,
  vista,
  titulo,
  alTocarElemento,
  alNavegar,
}: {
  clave: string;
  modelo: unknown;
  vista: string;
  titulo: string;
  alTocarElemento: (id: string) => void;
  alNavegar: (vista: string) => void;
}) {
  const marco = useRef<HTMLIFrameElement>(null);
  const contenedor = useRef<HTMLDivElement>(null);
  const listo = useRef(false);
  const { efectivo } = useTema();
  const [pintado, setPintado] = useState(false);
  const [error, setError] = useState(false);
  const [completa, setCompleta] = useState(false);

  useEffect(() => {
    const enviar = () => {
      const mensaje: MensajeModelo = { tipo: 'atlas:likec4-modelo', clave, modelo, vista, oscuro: efectivo === 'oscuro' };
      // El visor tiene origen opaco: no hay un origen que nombrar. Solo se le escribe a su ventana, la del iframe.
      marco.current?.contentWindow?.postMessage(mensaje, '*');
    };
    const alRecibir = (evento: MessageEvent) => {
      if (evento.source !== marco.current?.contentWindow || !esMensajeVisorLikeC4(evento.data)) return;
      const dato = evento.data;
      if (dato.tipo === 'atlas:likec4-listo') {
        listo.current = true;
        enviar();
      } else if (dato.tipo === 'atlas:likec4-pintado') {
        setError(false);
        setPintado(true);
      } else if (dato.tipo === 'atlas:likec4-error') {
        setError(true);
      } else if (dato.tipo === 'atlas:likec4-elemento') {
        alTocarElemento(dato.id);
      } else {
        alNavegar(dato.vista);
      }
    };
    window.addEventListener('message', alRecibir);
    if (listo.current) enviar();
    return () => window.removeEventListener('message', alRecibir);
  }, [clave, modelo, vista, efectivo, alTocarElemento, alNavegar]);

  useEffect(() => {
    const alCambiar = () => setCompleta(document.fullscreenElement === contenedor.current);
    document.addEventListener('fullscreenchange', alCambiar);
    return () => document.removeEventListener('fullscreenchange', alCambiar);
  }, []);

  const alternar = () => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void contenedor.current?.requestFullscreen?.();
  };

  if (error) {
    return <p role="alert">No pudimos dibujar esta vista. Abajo puedes consultar sus elementos.</p>;
  }
  return (
    <div ref={contenedor} className="flex h-[70dvh] min-h-96 w-full min-w-0 flex-col gap-2 bg-card">
      {typeof document.documentElement.requestFullscreen === 'function' && (
        <button type="button" onClick={alternar} className="min-h-toque self-end rounded-xl border border-border bg-card px-3 text-sm font-medium">
          {completa ? 'Salir de pantalla completa' : 'Pantalla completa'}
        </button>
      )}
      <iframe
        ref={marco}
        src={RUTA_VISOR_LIKEC4}
        title={`Vista de arquitectura: ${titulo}`}
        sandbox="allow-scripts"
        referrerPolicy="no-referrer"
        data-pintado={pintado}
        className="min-h-0 w-full flex-1 rounded-xl border border-border bg-card"
      />
    </div>
  );
}
