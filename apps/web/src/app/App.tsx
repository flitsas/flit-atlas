import { useEffect, useState } from 'react';

type EstadoApi = { tipo: 'cargando' } | { tipo: 'ok'; version: string } | { tipo: 'error' };

/**
 * Pantalla de la HU 1.1: comprueba que la PWA llega a atlas-api por el mismo origen.
 * El shell móvil primero (barra inferior, módulos, tema) llega en la HU 1.6.
 */
export function App() {
  const [estado, setEstado] = useState<EstadoApi>({ tipo: 'cargando' });

  useEffect(() => {
    const control = new AbortController();
    fetch('/api/v1/version', { signal: control.signal, credentials: 'same-origin' })
      .then(async (respuesta) => {
        if (!respuesta.ok) throw new Error(String(respuesta.status));
        const cuerpo = (await respuesta.json()) as { version?: string };
        setEstado({ tipo: 'ok', version: cuerpo.version ?? 'sin versión' });
      })
      .catch(() => {
        if (!control.signal.aborted) setEstado({ tipo: 'error' });
      });
    return () => control.abort();
  }, []);

  return (
    <main>
      <h1>Flit Atlas</h1>
      <p role="status">
        {estado.tipo === 'cargando' && 'Conectando con la API…'}
        {estado.tipo === 'ok' && `API conectada (versión ${estado.version}).`}
        {estado.tipo === 'error' && 'No se pudo conectar con la API.'}
      </p>
    </main>
  );
}
