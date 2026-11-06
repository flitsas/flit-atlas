import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css';

import { esMensajeXmlBpmn, type MensajeVisorBpmn } from './mensajesBpmn';

/**
 * Visor y editor BPMN (ATLAS-ADR-0054 §5), dentro del iframe aislado: sin cookie ni API, solo intercambia XML con quien lo
 * enmarca. El editor carga aparte: leer un proceso no lo necesita.
 */
interface Motor {
  importXML(xml: string): Promise<unknown>;
  saveXML(opciones: { format: boolean }): Promise<{ xml?: string }>;
  get(servicio: string): { zoom?: (nivel: string) => void };
  on(evento: string, manejador: () => void): void;
  destroy(): void;
}

const contenedor = document.getElementById('diagrama')!;
const responder = (mensaje: MensajeVisorBpmn) => window.parent.postMessage(mensaje, '*');
let motor: Motor | undefined;
let modoActual: boolean | undefined;
let turno = 0;
let pendiente: ReturnType<typeof setTimeout> | undefined;

async function preparar(editable: boolean): Promise<Motor> {
  if (motor && modoActual === editable) return motor;
  motor?.destroy();
  const Clase = editable ? (await import('bpmn-js/lib/Modeler')).default : (await import('bpmn-js/lib/NavigatedViewer')).default;
  const nuevo = new Clase({ container: contenedor }) as unknown as Motor;
  if (editable) {
    nuevo.on('commandStack.changed', () => {
      clearTimeout(pendiente);
      pendiente = setTimeout(() => {
        void nuevo.saveXML({ format: false }).then(({ xml }) => {
          if (xml) responder({ tipo: 'atlas:bpmn-cambio', xml });
        });
      }, 300);
    });
  }
  motor = nuevo;
  modoActual = editable;
  return nuevo;
}

window.addEventListener('message', (evento) => {
  if (evento.source !== window.parent || !esMensajeXmlBpmn(evento.data)) return;
  const { xml, editable } = evento.data;
  const este = ++turno;
  void preparar(editable)
    .then(async (m) => {
      await m.importXML(xml);
      if (este !== turno) return;
      m.get('canvas').zoom?.('fit-viewport');
      responder({ tipo: 'atlas:bpmn-pintado' });
    })
    .catch(() => {
      if (este === turno) responder({ tipo: 'atlas:bpmn-error' });
    });
});

responder({ tipo: 'atlas:bpmn-listo' });
