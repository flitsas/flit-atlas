import { LikeC4Model } from '@likec4/core/model';
import { LikeC4ModelProvider, ReactLikeC4 } from 'likec4/react';
import { Component, type ReactNode, StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';

import { esMensajeModelo, type MensajeVisorLikeC4 } from './mensajes';

/**
 * Visor de LikeC4 (ATLAS-ADR-0056 §7), dentro del iframe aislado: sin cookie ni API. `likec4/react` inyecta estilos en línea en una shadow
 * root, así que no puede correr en el shell. Tocar un elemento solo avisa su id a quien lo enmarca, que abre la ficha con sus propios
 * permisos; los paneles propios de LikeC4 van apagados porque muestran enlaces que aquí no abrirían nada.
 */
const responder = (mensaje: MensajeVisorLikeC4) => window.parent.postMessage(mensaje, '*');

interface Estado {
  clave: string;
  modelo: LikeC4Model<any>; // eslint-disable-line @typescript-eslint/no-explicit-any
  vista: string;
  oscuro: boolean;
}

class Barrera extends Component<{ children: ReactNode }, { fallo: boolean }> {
  override state = { fallo: false };

  static getDerivedStateFromError() {
    return { fallo: true };
  }

  override componentDidCatch() {
    responder({ tipo: 'atlas:likec4-error' });
  }

  override render() {
    return this.state.fallo ? null : this.props.children;
  }
}

function Visor() {
  const [estado, setEstado] = useState<Estado | null>(null);

  useEffect(() => {
    const alRecibir = (evento: MessageEvent) => {
      if (evento.source !== window.parent || !esMensajeModelo(evento.data)) return;
      const { clave, modelo, vista, oscuro } = evento.data;
      setEstado((previo) => {
        try {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const armado = previo?.clave === clave ? previo.modelo : LikeC4Model.fromDump(modelo as any);
          return { clave, modelo: armado, vista, oscuro };
        } catch {
          responder({ tipo: 'atlas:likec4-error' });
          return previo;
        }
      });
    };
    window.addEventListener('message', alRecibir);
    responder({ tipo: 'atlas:likec4-listo' });
    return () => window.removeEventListener('message', alRecibir);
  }, []);

  if (!estado) return null;
  return (
    <LikeC4ModelProvider likec4model={estado.modelo}>
      <ReactLikeC4
        key={`${estado.clave}|${estado.vista}`}
        viewId={estado.vista as never}
        colorScheme={estado.oscuro ? 'dark' : 'light'}
        // La fuente de LikeC4 sale de un CDN externo, que la CSP del visor no permite: usa la del sistema.
        injectFontCss={false}
        pannable
        zoomable
        fitView
        // Un landscape de unos 44 nodos mide miles de px de ancho: se ve completo al abrir y se acerca con gestos o rueda.
        minZoom={0.01}
        maxZoom={2}
        reactFlowProps={{ zoomOnScroll: true, zoomOnPinch: true, panOnScroll: false }}
        controls={['zoom', 'navigation', 'walkthrough'] as never}
        enableElementDetails={false}
        enableRelationshipDetails={false}
        enableRelationshipBrowser={false}
        enableSearch={false}
        enableNotes={false}
        enableFocusMode={false}
        nodesSelectable
        showNavigationButtons
        style={{ width: '100%', height: '100%' }}
        onInitialized={() => responder({ tipo: 'atlas:likec4-pintado' })}
        onNavigateTo={(vista) => responder({ tipo: 'atlas:likec4-navegar', vista: String(vista) })}
        onNodeClick={(nodo) => {
          // En una vista de despliegue el nodo apunta a su instancia; lo que tiene ficha es el elemento del modelo.
          const referencia = (nodo as { modelRef?: unknown }).modelRef;
          if (typeof referencia === 'string') responder({ tipo: 'atlas:likec4-elemento', id: referencia });
        }}
      />
    </LikeC4ModelProvider>
  );
}

createRoot(document.getElementById('diagrama')!).render(
  <StrictMode>
    <Barrera>
      <Visor />
    </Barrera>
  </StrictMode>,
);
