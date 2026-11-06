import mermaid from 'mermaid';

import { esMensajeFuente, type MensajeVisorMermaid } from './mensajes';

/** Visor Mermaid (ATLAS-ADR-0053 §5): `securityLevel: 'strict'` dentro del iframe aislado; solo le escribe a quien lo enmarca. */
const contenedor = document.getElementById('diagrama')!;
const responder = (mensaje: MensajeVisorMermaid) => window.parent.postMessage(mensaje, '*');
let turno = 0;

window.addEventListener('message', (evento) => {
  if (evento.source !== window.parent || !esMensajeFuente(evento.data)) return;
  const { fuente, oscuro } = evento.data;
  const este = ++turno;
  mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: oscuro ? 'dark' : 'default', suppressErrorRendering: true, maxTextSize: 20_000 });
  void mermaid
    .render(`diagrama-${este}`, fuente)
    .then(({ svg }) => {
      if (este !== turno) return;
      contenedor.innerHTML = svg;
      responder({ tipo: 'atlas:mermaid-pintado', alto: Math.ceil(document.documentElement.scrollHeight) });
    })
    .catch(() => {
      if (este === turno) responder({ tipo: 'atlas:mermaid-error' });
    });
});

responder({ tipo: 'atlas:mermaid-listo' });
