/**
 * Visor OpenAPI con Scalar (HU 3.6, ATLAS-ADR-0039). Es una página aparte (`/visor/openapi.html`, build propio en
 * `vite.visor.config.ts`) que la pantalla del contrato carga en un iframe con `sandbox="allow-scripts"`: origen opaco,
 * sin la cookie del BFF, sin acceso a la API ni a la página que lo contiene. Su CSP (nginx, `location /visor/`) es la del
 * shell más estilos en línea, que Scalar inyecta, y solo deja conectar a los hosts de QA y sandbox. Scalar no entra al
 * bundle de la PWA.
 */
// Primero el almacén en memoria; Scalar se importa después (dinámico) para que lo encuentre ya instalado.
import './almacen';
import './visor.css';

import { config as configurarZod } from 'zod';

import { esMensajeSpec, type MensajeVisor } from './mensajes';
import { urlPermitida } from './servidores';

// Zod (que usa Scalar) prueba `new Function('')` para compilar validadores; sin `unsafe-eval` la CSP lo bloquea (y lo
// reporta). En modo `jitless` no lo intenta: valida igual, sin generar código.
configurarZod({ jitless: true });

type Scalar = typeof import('@scalar/api-reference');

// Solo la página que lo contiene, del mismo origen de la URL del visor (el del documento es opaco).
const ORIGEN = new URL(location.href).origin;

function avisar(mensaje: MensajeVisor) {
  // El origen opaco no puede nombrar el de la página: el mensaje no lleva datos.
  window.parent.postMessage(mensaje, '*');
}

let scalar: Promise<Scalar> | null = null;
let montado: ReturnType<Scalar['createApiReference']> | null = null;

function cargarScalar(): Promise<Scalar> {
  scalar ??= Promise.all([import('@scalar/api-reference'), import('@scalar/api-reference/style.css')]).then(([modulo]) => modulo);
  return scalar;
}

window.addEventListener('message', (evento) => {
  if (evento.source !== window.parent || evento.origin !== ORIGEN || !esMensajeSpec(evento.data)) return;
  const { contenido, servidores, oscuro } = evento.data;
  const permitidos = servidores.map((s) => s.url);
  document.documentElement.classList.toggle('dark-mode', oscuro);
  document.documentElement.classList.toggle('light-mode', !oscuro);
  cargarScalar()
    .then(({ createApiReference }) => {
      montado?.destroy();
      montado = createApiReference('#visor', {
        content: contenido,
        // Solo QA y sandbox, ya resueltos y sin variables (la pantalla los filtra con `servidoresPermitidos`).
        servers: servidores,
        hideTestRequestButton: servidores.length === 0,
        // Nada sale a scalar.com: ni proxy, ni fuentes, ni telemetría, ni el agente, ni el registro.
        proxyUrl: '',
        withDefaultFonts: false,
        telemetry: false,
        agent: { disabled: true },
        mcp: { disabled: true },
        showDeveloperTools: 'never',
        hideClientButton: true,
        // La descarga y el tema los da la pantalla de Atlas.
        documentDownloadType: 'none',
        hideDarkModeToggle: true,
        forceDarkModeState: oscuro ? 'dark' : 'light',
        darkMode: oscuro,
        defaultOpenAllTags: false,
        persistAuth: false,
        // Segunda barrera tras la CSP: la consola solo llama a los servidores permitidos y nunca manda cookies.
        customFetch: (entrada: RequestInfo | URL, init?: RequestInit) => {
          const url = entrada instanceof Request ? entrada.url : String(entrada);
          if (!urlPermitida(url, permitidos)) return Promise.reject(new TypeError('Atlas solo permite probar contra QA o sandbox.'));
          return fetch(entrada, { ...init, credentials: 'omit' });
        },
      } as Parameters<Scalar['createApiReference']>[1]);
    })
    .catch(() => avisar({ tipo: 'atlas:visor-error' }));
});

avisar({ tipo: 'atlas:visor-listo' });
