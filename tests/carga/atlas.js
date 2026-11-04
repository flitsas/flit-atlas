// Prueba de carga de Flit Atlas (HU 4.10, ATLAS-ADR-0048) con k6 2.3.0: 20 personas navegando (inicio, catálogo,
// contratos y portal) y, desde el minuto 1, 2 exportaciones simultáneas de un paquete en los siete formatos.
// Umbrales: p95 de las lecturas < 500 ms (antes y durante las exportaciones), lecturas con error < 1 %, exportaciones
// listas. Cómo correrla (local o contra QA) y de dónde salen las sesiones: tests/carga/README.md. Nada de credenciales en
// este archivo: las sesiones llegan por variables de entorno o por un archivo temporal.

import { check, fail, group, sleep } from 'k6';
import exec from 'k6/execution';
import http from 'k6/http';
import { Rate, Trend } from 'k6/metrics';

const BASE = (__ENV.ATLAS_URL || '').replace(/\/$/, '');
if (!BASE) fail('Falta ATLAS_URL (p. ej. https://localhost:4032 o https://qa.atlas.flitsas.com).');

const DATOS = __ENV.ATLAS_CARGA_DATOS ? JSON.parse(open(__ENV.ATLAS_CARGA_DATOS)) : {};
const lista = (valor) => (valor || '').split(',').map((c) => c.trim()).filter(Boolean);
const COOKIES_INTERNOS = DATOS.internos || lista(__ENV.ATLAS_COOKIES_INTERNOS);
const COOKIES_EXTERNOS = DATOS.externos || lista(__ENV.ATLAS_COOKIES_EXTERNOS);
const PAQUETE = DATOS.paqueteId || __ENV.ATLAS_PAQUETE_ID || '';
const SIMULADOS = Number(__ENV.ATLAS_CARGA_SIMULADO || 0);
const VUS = Number(__ENV.ATLAS_CARGA_VUS || 20);
const DURACION = __ENV.ATLAS_CARGA_DURACION || '3m';
const INICIO_EXPORTAR_S = Number(__ENV.ATLAS_CARGA_INICIO_EXPORTAR || 60);
const EXPORTACIONES = Number(__ENV.ATLAS_CARGA_EXPORTACIONES ?? 2);
const IP_POR_VU = __ENV.ATLAS_CARGA_IP_POR_VU === '1';
const FORMATOS = ['openapi', 'postman', 'html', 'pdf', 'word', 'excel'];

const lecturaLenta = new Trend('atlas_lectura_ms', true);
const exportacionDuracion = new Trend('atlas_exportacion_ms', true);
const exportacionesOk = new Rate('atlas_exportaciones_ok');

const escenarios = {
  navegacion: { executor: 'constant-vus', vus: VUS, duration: DURACION, exec: 'navegar', gracefulStop: '10s' },
};
if (EXPORTACIONES > 0) {
  escenarios.exportaciones = {
    executor: 'per-vu-iterations',
    vus: EXPORTACIONES,
    iterations: 1,
    startTime: `${INICIO_EXPORTAR_S}s`,
    maxDuration: '8m',
    exec: 'exportar',
  };
}

export const options = {
  scenarios: escenarios,
  insecureSkipTLSVerify: __ENV.ATLAS_CARGA_TLS_INSEGURO === '1',
  thresholds: {
    'http_req_duration{tipo:lectura}': ['p(95)<500'],
    'http_req_duration{tipo:lectura,fase:base}': ['p(95)<500'],
    'http_req_duration{tipo:lectura,fase:con_exportaciones}': ['p(95)<500'],
    'http_req_failed{tipo:lectura}': ['rate<0.01'],
    ...(EXPORTACIONES > 0 ? { atlas_exportaciones_ok: ['rate==1'] } : {}),
    checks: ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

/** Proveedor simulado (solo local y dev): N empleados con el rol de administración, cada uno con su sesión. */
function entrarSimulados(n) {
  const cookies = [];
  for (let i = 0; i < n; i++) {
    const r = http.post(
      `${BASE}/api/v1/identidad/simulado/entrar`,
      JSON.stringify({ correo: `carga.${i}.${Date.now()}@ejemplo.com`, nombre: `Persona Carga ${i}`, administrador: true }),
      { headers: { 'Content-Type': 'application/json', 'X-Atlas-Csrf': '1', ...ip(i + 1) }, tags: { tipo: 'preparacion' } },
    );
    const cookie = r.cookies['__Host-atlas']?.[0]?.value;
    if (r.status !== 204 || !cookie) fail(`El ingreso simulado respondió ${r.status}`);
    cookies.push(`__Host-atlas=${cookie}`);
  }
  return cookies;
}

function ip(vu) {
  // Una IP de documentación por persona simulada: solo vale contra la API de pruebas, que confía en el proxy de loopback.
  return IP_POR_VU ? { 'X-Forwarded-For': `198.51.100.${(vu % 250) + 1}` } : {};
}

export function setup() {
  const internos = COOKIES_INTERNOS.length > 0 ? COOKIES_INTERNOS : entrarSimulados(SIMULADOS);
  if (internos.length + COOKIES_EXTERNOS.length === 0) {
    fail('Sin sesiones: usa ATLAS_CARGA_DATOS, ATLAS_COOKIES_INTERNOS/ATLAS_COOKIES_EXTERNOS o ATLAS_CARGA_SIMULADO (README).');
  }
  return { internos, externos: COOKIES_EXTERNOS, inicio: Date.now() };
}

function cabeceras(cookie) {
  return { Cookie: cookie, Accept: 'application/json', ...ip(exec.vu.idInTest) };
}

function fase(datos) {
  return EXPORTACIONES > 0 && Date.now() - datos.inicio >= INICIO_EXPORTAR_S * 1000 ? 'con_exportaciones' : 'base';
}

/** GET de lectura: etiquetado para los umbrales y revisado (200). */
function leer(datos, cookie, ruta, nombre) {
  const r = http.get(`${BASE}${ruta}`, { headers: cabeceras(cookie), tags: { tipo: 'lectura', fase: fase(datos), name: nombre } });
  check(r, { [`${nombre}: 200`]: (x) => x.status === 200 });
  lecturaLenta.add(r.timings.duration, { name: nombre });
  return r;
}

function json(r) {
  try {
    return r.json();
  } catch {
    return null;
  }
}

const elegir = (arreglo) => (arreglo && arreglo.length > 0 ? arreglo[Math.floor(Math.random() * arreglo.length)] : null);
const pausa = () => sleep(1 + Math.random() * 2);

function inicio(datos, cookie) {
  group('inicio', () => {
    leer(datos, cookie, '/api/v1/plataforma/yo', 'yo');
    leer(datos, cookie, '/api/v1/plataforma/banderas', 'banderas');
    leer(datos, cookie, '/api/v1/plataforma/notificaciones?limite=20', 'notificaciones');
  });
}

function navegarInterno(datos, cookie) {
  inicio(datos, cookie);
  leer(datos, cookie, '/api/v1/soporte/este-sabado', 'soporte este sábado');
  pausa();
  group('catálogo', () => {
    const aplicativos = json(leer(datos, cookie, '/api/v1/catalogo/aplicativos', 'catálogo'));
    const aplicativo = elegir(aplicativos?.elementos);
    if (aplicativo) leer(datos, cookie, `/api/v1/catalogo/aplicativos/${encodeURIComponent(aplicativo.slug)}`, 'ficha del aplicativo');
  });
  pausa();
  group('contratos', () => {
    const contratos = json(leer(datos, cookie, '/api/v1/contratos', 'contratos'));
    const contrato = elegir((contratos?.elementos || []).filter((c) => c.ultimaVersion));
    if (contrato) {
      const v = encodeURIComponent(contrato.ultimaVersion.version);
      leer(datos, cookie, `/api/v1/contratos/${contrato.id}`, 'contrato');
      leer(datos, cookie, `/api/v1/contratos/${contrato.id}/versiones`, 'versiones');
      leer(datos, cookie, `/api/v1/contratos/${contrato.id}/versiones/${v}/operaciones`, 'operaciones');
      leer(datos, cookie, `/api/v1/contratos/${contrato.id}/versiones/${v}/spec`, 'spec');
    }
    leer(datos, cookie, '/api/v1/contratos/paquetes', 'paquetes');
  });
  pausa();
}

function navegarExterno(datos, cookie) {
  inicio(datos, cookie);
  pausa();
  group('portal', () => {
    const portal = json(leer(datos, cookie, '/api/v1/portal/inicio', 'portal: inicio'));
    const paquete = elegir(portal?.paquetes);
    if (paquete) {
      const detalle = json(leer(datos, cookie, `/api/v1/portal/paquetes/${paquete.id}`, 'portal: paquete'));
      const contrato = elegir((detalle?.contratos || []).filter((c) => c.estadoVersion !== 'retirada'));
      if (contrato && detalle?.nivel === 'tecnico') {
        leer(datos, cookie, `/api/v1/portal/paquetes/${paquete.id}/contratos/${contrato.contratoId}/spec`, 'portal: spec podado');
      }
    }
    leer(datos, cookie, '/api/v1/portal/buscar?q=tramite', 'portal: buscar');
  });
  pausa();
}

/** Cada VU es una persona: primero los empleados, después los externos (si hay), en rueda. */
export function navegar(datos) {
  const personas = [...datos.internos.map((c) => ['interno', c]), ...datos.externos.map((c) => ['externo', c])];
  const [tipo, cookie] = personas[(exec.vu.idInTest - 1) % personas.length];
  if (tipo === 'interno') navegarInterno(datos, cookie);
  else navegarExterno(datos, cookie);
}

/** Una exportación del paquete en los siete formatos por la cola, hasta que queda lista y se pide su URL firmada. */
export function exportar(datos) {
  if (!PAQUETE) fail('Sin paquete para exportar: ATLAS_CARGA_DATOS (paqueteId) o ATLAS_PAQUETE_ID.');
  const cookie = datos.internos[(exec.vu.idInTest - 1) % datos.internos.length];
  if (!cookie) fail('Sin sesión de empleado para exportar.');
  const comienzo = Date.now();
  const solicitud = http.post(`${BASE}/api/v1/contratos/exportaciones`, JSON.stringify({ paqueteId: PAQUETE, formatos: FORMATOS }), {
    headers: { ...cabeceras(cookie), 'Content-Type': 'application/json', 'X-Atlas-Csrf': '1' },
    tags: { tipo: 'exportacion', name: 'solicitar exportación' },
  });
  if (!check(solicitud, { 'exportación: 202': (r) => r.status === 202 })) {
    console.error(`La solicitud de exportación respondió ${solicitud.status} (${solicitud.headers['Www-Authenticate'] || ''}): ${solicitud.body}`);
    exportacionesOk.add(false);
    return;
  }
  const id = solicitud.json('id');
  let estado = 'pendiente';
  while (['pendiente', 'procesando'].includes(estado) && Date.now() - comienzo < 7 * 60 * 1000) {
    sleep(2);
    const r = http.get(`${BASE}/api/v1/contratos/exportaciones/${id}`, { headers: cabeceras(cookie), tags: { tipo: 'exportacion', name: 'estado de la exportación' } });
    estado = r.status === 200 ? r.json('estado') : estado;
  }
  exportacionDuracion.add(Date.now() - comienzo);
  const lista = estado === 'lista';
  if (lista) {
    const descarga = http.get(`${BASE}/api/v1/contratos/exportaciones/${id}/descarga`, {
      headers: cabeceras(cookie),
      tags: { tipo: 'exportacion', name: 'URL de descarga' },
    });
    check(descarga, { 'descarga: URL firmada': (r) => r.status === 200 && !!r.json('url') });
  } else {
    console.error(`La exportación ${id} terminó en «${estado}».`);
  }
  exportacionesOk.add(lista);
}
