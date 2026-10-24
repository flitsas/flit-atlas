import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import type { Page, Route } from '@playwright/test';

import type { MatrizPermisos, RolMatriz } from '../../src/modulos/permisos/api';

/**
 * atlas-api simulada con `page.route` para las pruebas de los módulos (HU 1.4 y HU 1.9): corren contra cualquier
 * servidor de la PWA (Vite, `vite preview` o la imagen nginx con su CSP) sin levantar la API ni el AppHost. Lo que se
 * prueba del servidor (permisos, banderas, RLS) ya lo cubren las pruebas de .NET.
 */
const RAIZ = resolve(dirname(fileURLToPath(import.meta.url)), '../../../..');

/** La matriz real: se arma desde docs/permisos/matriz-rol-permiso.md, que el código genera (MatrizPermisosTests). */
export function matrizDelRepositorio(): MatrizPermisos {
  const md = readFileSync(resolve(RAIZ, 'docs/permisos/matriz-rol-permiso.md'), 'utf8').replace(/\r\n/g, '\n');
  const celdas = (linea: string) => linea.split('|').slice(1, -1).map((c) => c.trim());
  const codigo = (celda: string) => celda.replace(/`/g, '');
  const secciones = md.split(/\n## /).slice(1);

  const roles: RolMatriz[] = secciones[0]!
    .split('\n')
    .filter((l) => l.startsWith('| `'))
    .map((l) => {
      const [c, nombre, tipo, descripcion] = celdas(l);
      return { codigo: codigo(c!), nombre: nombre!, tipo: tipo as RolMatriz['tipo'], descripcion: descripcion!, asignable: tipo !== 'implicito' };
    });

  const modulos = secciones.slice(1).map((seccion) => {
    const [titulo, ...lineas] = seccion.split('\n');
    const [, nombre, id] = /^(.*) \(`(.*)`\)$/.exec(titulo!)!;
    const columnas = celdas(lineas.find((l) => l.startsWith('| Permiso'))!).slice(2).map(codigo);
    const permisos = lineas
      .filter((l) => l.startsWith('| `'))
      .map((l) => {
        const [c, descripcion, ...marcas] = celdas(l);
        return { codigo: codigo(c!), descripcion: descripcion!, roles: columnas.filter((_, i) => marcas[i] === '✔') };
      });
    return { id: id!, nombre: nombre!, permisos };
  });
  return { roles, modulos };
}

export interface OpcionesApi {
  permisos: string[];
  roles?: string[];
  banderas?: Record<string, boolean>;
  /**
   * HU 2.12: agrega FLITO al catálogo y al reparto, y un turno de la persona en sesión (p1) el primer sábado del rango,
   * con teléfonos (como si tuviera `atlas.equipo.ver_contacto`) para comprobar que no se guardan en el dispositivo.
   */
  sinConexion?: boolean;
}

/** Teléfono ficticio que la prueba sin conexión busca en IndexedDB (no debe estar). */
export const TELEFONO_FICTICIO = '3000000000';

/** El primer sábado desde `dia` (aaaa-mm-dd), en UTC como la PWA. */
function sabadoDesde(dia: string): string {
  const fecha = new Date(`${dia}T12:00:00Z`);
  fecha.setUTCDate(fecha.getUTCDate() + ((6 - fecha.getUTCDay() + 7) % 7));
  return fecha.toISOString().slice(0, 10);
}

const json = (route: Route, cuerpo: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });

/** Unos pocos aplicativos del catálogo (HU 2.1), con nombres largos para probar el móvil a 360 px. */
const APLICATIVOS = [
  {
    id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0', tipo: 'producto', grupo: 'flit2', estado: 'activo', criticidad: 'alta',
    manejaDatosPersonales: true, padreId: null, descripcion: 'Plataforma FLIT 2.0 en el monorepo flitsas/flit.', tecnologias: ['.NET 10', 'Next 16'],
    etiquetas: ['monorepo'], ancestros: [], hijos: 1,
  },
  {
    id: 'a2', slug: 'flit2-notificaciones', nombre: 'Notificaciones', tipo: 'modulo', grupo: 'flit2', estado: 'activo', criticidad: 'media',
    manejaDatosPersonales: false, padreId: 'a1', descripcion: null, tecnologias: [], etiquetas: [],
    ancestros: [{ id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0' }], hijos: 0,
  },
  {
    id: 'a3', slug: 'legado-back-api-external-transact-vali-query-ext', nombre: 'BackApiExternalTransactValiQueryExt', tipo: 'servicio',
    grupo: 'legado', estado: 'en_retiro', criticidad: 'media', manejaDatosPersonales: false, padreId: 'a4',
    descripcion: 'API externa de validaciones y consultas (ICT).', tecnologias: ['Node', 'Express', 'tsoa'], etiquetas: ['ict'],
    ancestros: [{ id: 'a4', slug: 'legados-v1', nombre: 'FLIT V1 (legados)' }], hijos: 0,
  },
  {
    id: 'a5', slug: 'sentinel', nombre: 'Sentinel', tipo: 'producto', grupo: 'satelite', estado: 'activo', criticidad: 'media',
    manejaDatosPersonales: false, padreId: null, descripcion: '[por verificar] Confirmar con el líder técnico qué es.', tecnologias: [],
    etiquetas: [], ancestros: [], hijos: 0,
  },
];

/** Responsables (HU 2.3): un producto y un módulo, con nombres largos para probar el móvil a 360 px. */
const PERSONA_LARGA = {
  id: 'p7',
  nombre: 'Persona Demo Con Un Nombre Bastante Largo Para El Móvil',
  correo: 'persona.demo.con.un.correo.bastante.largo@ejemplo.com',
  cargo: 'Desarrolladora',
  foto: null,
  telefono: null as string | null,
  usuarioGitHub: null,
  activa: true,
};
const PERSONA_CORTA = { ...PERSONA_LARGA, id: 'p8', nombre: 'Persona Demo Ocho', correo: 'persona.ocho@ejemplo.com' };
const responsableSimulado = (id: string, tipo: 'principal' | 'secundario', persona: typeof PERSONA_LARGA) => ({
  id,
  tipo,
  persona,
  dedicacion: tipo === 'secundario' ? 20 : null,
  vigenteDesde: '2026-10-20',
  vigenteHasta: null,
  vigente: true,
  estadoTraspaso: tipo === 'secundario' ? 'en_acompanamiento' : 'habilitado',
});
const REF_FLIT2 = { id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0', tipo: 'producto' };
const REF_NOTIFICACIONES = { id: 'a2', slug: 'flit2-notificaciones', nombre: 'Notificaciones', tipo: 'modulo' };
const DEFINICIONES_SIMULADAS = [
  { rol: 'principal', titulo: 'Responsable principal', puntos: ['Revisa (o delega la revisión de) todo PR que toque su módulo.'] },
  { rol: 'secundario', titulo: 'Responsable secundario', puntos: ['Dedicación orientativa: ~20 % de su tiempo en ese módulo.'] },
];
const PRINCIPAL_FLIT2 = responsableSimulado('r1', 'principal', PERSONA_LARGA);
const SECUNDARIO_FLIT2 = responsableSimulado('r2', 'secundario', PERSONA_CORTA);
const REPARTO_SIMULADO = {
  version: {
    numero: 5,
    origen: 'importacion',
    restauradaDe: null,
    fechaDocumento: '2026-10-20',
    definidoPor: 'Persona Demo (Líder técnico)',
    autor: 'Líder Demo',
    nota: 'Importación del documento de responsables.',
    creadaEn: '2026-10-23T15:00:00Z',
    huella: 'a'.repeat(64),
    responsabilidades: 4,
  },
  definiciones: DEFINICIONES_SIMULADAS,
  porPersona: [
    { persona: PERSONA_LARGA, principalDe: [REF_FLIT2], secundarioDe: [REF_NOTIFICACIONES] },
    { persona: PERSONA_CORTA, principalDe: [REF_NOTIFICACIONES], secundarioDe: [REF_FLIT2] },
  ],
  productos: [{ aplicativo: REF_FLIT2, principal: PRINCIPAL_FLIT2, secundario: SECUNDARIO_FLIT2 }],
  modulos: [
    {
      aplicativo: REF_NOTIFICACIONES,
      principal: responsableSimulado('r3', 'principal', PERSONA_CORTA),
      secundario: responsableSimulado('r4', 'secundario', PERSONA_LARGA),
    },
  ],
  servicios: [],
  huerfanas: 0,
  veContacto: false,
  puedeGestionar: false,
};

/** FLITO en el catálogo y su reparto (HU 2.12), con un teléfono ficticio en el principal. */
const FLITO = {
  id: 'a6', slug: 'flito', nombre: 'FLITO', tipo: 'producto', grupo: 'flito', estado: 'activo', criticidad: 'alta',
  manejaDatosPersonales: true, padreId: null, descripcion: 'Producto FLITO (datos ficticios de prueba).', tecnologias: ['.NET 10'],
  etiquetas: [], ancestros: [], hijos: 0,
};
const REF_FLITO = { id: 'a6', slug: 'flito', nombre: 'FLITO', tipo: 'producto' };
const PRINCIPAL_FLITO = responsableSimulado('r5', 'principal', { ...PERSONA_CORTA, id: 'p9', nombre: 'Principal De Flito Demo', telefono: TELEFONO_FICTICIO });
const SECUNDARIO_FLITO = responsableSimulado('r6', 'secundario', { ...PERSONA_CORTA, id: 'p10', nombre: 'Secundaria De Flito Demo' });

/** Una versión de contrato ficticia (HU 3.2): repos y workflows `flitsas-demo`. */
function versionSimulada(version: string, estable: boolean, publicadoEn: string) {
  const rama = estable ? 'release' : 'develop';
  return {
    id: `v-${version}`, version, esEstable: estable, sha256: 'a'.repeat(64), formato: 'yaml', tamano: 1200,
    repositorio: 'flitsas-demo/api-demo-con-un-nombre-de-repositorio-largo', rama,
    commit: '0123456789abcdef0123456789abcdef01234567', workflow: '.github/workflows/ci.yml',
    workflowDelJob: 'flitsas-demo/plantillas/.github/workflows/atlas-publicar.yml@refs/tags/v1', ejecucion: '1234567890',
    publicadoPor: `github:flitsas-demo/api-demo@${rama}`, publicadoEn, tieneBreaking: !estable, estado: 'vigente', retiroEn: null,
  };
}

const VERSIONES_CONTRATO = [versionSimulada('1.3.0-dev.a1b2c3d', false, '2026-10-23T16:00:00Z'), versionSimulada('1.2.0', true, '2026-10-23T15:00:00Z')];

const CONTRATOS = [
  {
    id: 'c1', codigo: 'api-externa', nombre: 'API Externa Demo de Trámites con un nombre largo', descripcion: 'Contrato ficticio de prueba.',
    tipo: 'openapi', visibilidad: 'interna', aplicativo: { id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0' },
    repositorio: 'flitsas-demo/api-demo', versiones: 2, ultimaVersion: VERSIONES_CONTRATO[0], ultimaEstable: VERSIONES_CONTRATO[1],
  },
  {
    id: 'c2', codigo: 'eventos', nombre: 'Eventos Demo', descripcion: null, tipo: 'asyncapi', visibilidad: 'interna',
    aplicativo: { id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0' }, repositorio: 'flitsas-demo/api-demo', versiones: 1,
    ultimaVersion: VERSIONES_CONTRATO[1], ultimaEstable: VERSIONES_CONTRATO[1],
  },
];

// Guías del contrato c1 (HU 3.7): el cuerpo trae HTML y un enlace javascript: que la PWA no debe interpretar.
const AUTORA_GUIA = { id: 'p1', nombre: 'Persona Demo con un nombre bastante largo' };
const revisionGuia = (numero: number, contenido: string) => ({
  numero, origen: numero === 1 ? 'creacion' : 'edicion', restauradaDe: null, titulo: 'Cómo autenticarse en la API Externa Demo de Trámites',
  contenido, operacion: 'GET /tramites/{id}', aplicaDesde: '1.0.0', aplicaHasta: null, visibilidad: 'compartible',
  nota: numero === 1 ? 'Primera versión.' : 'Renovación del token.', autor: AUTORA_GUIA, guardadaEn: '2026-10-23T15:00:00Z',
  huella: String(numero).repeat(64), publicada: false,
});
const CUERPO_GUIA =
  '# Autenticación\n\nPide un token y envíalo como `Authorization: Bearer $TOKEN`.\n\n<script>alert(1)</script>\n\n[enlace malo](javascript:alert(2))\n\n' +
  '| Campo | Descripción |\n|---|---|\n| token | Vence en una hora |\n\n```bash\ncurl -H "Authorization: Bearer $TOKEN" https://qa.ejemplo.com/api/v1/tramites/123\n```\n';
const REVISIONES_GUIA = [revisionGuia(2, CUERPO_GUIA), revisionGuia(1, '# Autenticación\n\nPide un token.\n')];
const GUIA = {
  id: 'g1', contratoId: 'c1', titulo: REVISIONES_GUIA[0]!.titulo, operacion: 'GET /tramites/{id}', aplicaDesde: '1.0.0', aplicaHasta: null,
  visibilidad: 'compartible', estado: 'borrador', revisionActual: 2, revisionPublicada: null, publicadaEn: null, publicadaPor: null,
  aprobadaPor: null, actualizadaEn: '2026-10-23T15:00:00Z',
};

// Paquetes (HU 4.1): un contrato compartible con dos versiones estables, su índice y una guía aprobada.
const OPERACIONES_PAQUETE = [
  { clave: 'GET /tramites/{id}', clase: 'operacion', metodo: 'GET', ruta: '/tramites/{id}', operationId: 'obtenerTramite', resumen: 'Obtener un trámite por su identificador', etiquetas: ['tramites', 'public'], publica: true },
  { clave: 'POST /tramites', clase: 'operacion', metodo: 'POST', ruta: '/tramites', operationId: 'crearTramite', resumen: 'Radicar un trámite nuevo con sus adjuntos', etiquetas: ['tramites'], publica: false },
  { clave: 'POST webhook:tramiteRadicado', clase: 'webhook', metodo: 'POST', ruta: 'tramiteRadicado', operationId: 'avisarRadicado', resumen: 'Aviso al cliente cuando se radica', etiquetas: [], publica: true },
];
const ELEGIBLE_PAQUETE = {
  id: 'c1', codigo: 'api-externa', nombre: CONTRATOS[0]!.nombre, tipo: 'openapi', aplicativo: CONTRATOS[0]!.aplicativo,
  versiones: [
    { id: 'v13', version: '1.3.0', estado: 'vigente', retiroEn: null, publicadoEn: '2026-10-23T15:00:00Z', operaciones: 3, publicas: 2 },
    { id: 'v12', version: '1.2.0', estado: 'deprecada', retiroEn: '2027-01-31', publicadoEn: '2026-09-23T15:00:00Z', operaciones: 3, publicas: 2 },
  ],
  guias: [{ id: 'g1', titulo: GUIA.titulo, operacion: 'GET /tramites/{id}', revisionPublicada: 2, aplicaDesde: '1.0.0', aplicaHasta: null }],
};

interface SolicitudPaqueteSimulada {
  nombre: string;
  descripcion: string | null;
  definicion: Array<{ contratoId: string; versionId: string; operaciones: string[]; guias: Array<{ guiaId: string; revision: number | null }> }>;
  nota: string;
}

/** Detalle de un paquete simulado a partir de sus revisiones guardadas (la última manda) y la vigente. */
function detallePaquete(revisiones: SolicitudPaqueteSimulada[], vigente: number | null, gestiona: boolean) {
  const actual = revisiones.length;
  const definicion = (r: SolicitudPaqueteSimulada) =>
    r.definicion.map((e) => {
      const version = ELEGIBLE_PAQUETE.versiones.find((v) => v.id === e.versionId)!;
      return {
        contratoId: e.contratoId, codigo: ELEGIBLE_PAQUETE.codigo, nombre: ELEGIBLE_PAQUETE.nombre, tipo: 'openapi', visibilidad: 'compartible',
        aplicativo: ELEGIBLE_PAQUETE.aplicativo, versionId: e.versionId, version: version.version, estadoVersion: version.estado,
        operaciones: e.operaciones, guias: e.guias.map((g) => ({ guiaId: g.guiaId, titulo: GUIA.titulo, revision: g.revision ?? 2 })),
      };
    });
  const detalleRevision = (n: number) => ({
    numero: n, nombre: revisiones[n - 1]!.nombre, descripcion: revisiones[n - 1]!.descripcion, definicion: definicion(revisiones[n - 1]!),
    nota: revisiones[n - 1]!.nota, autor: AUTORA_GUIA, guardadaEn: '2026-10-24T15:00:00Z', huella: String(n).repeat(64), vigente: n === vigente,
  });
  const ultima = revisiones[actual - 1]!;
  return {
    paquete: {
      id: 'p1', nombre: ultima.nombre, descripcion: ultima.descripcion, estado: vigente === null ? 'borrador' : 'publicado', revisionActual: actual,
      revisionPublicada: vigente, cambiosSinPublicar: vigente !== actual, publicadaEn: vigente === null ? null : '2026-10-24T16:00:00Z',
      publicadaPor: vigente === null ? null : AUTORA_GUIA, contratos: ultima.definicion.map((e) => e.contratoId),
      operaciones: ultima.definicion.reduce((t, e) => t + e.operaciones.length, 0), actualizadoEn: '2026-10-24T15:00:00Z', puedeGestionar: gestiona,
    },
    revision: detalleRevision(actual),
    revisiones: revisiones
      .map((r, i) => ({
        numero: i + 1, nombre: r.nombre, nota: r.nota, autor: AUTORA_GUIA, guardadaEn: '2026-10-24T15:00:00Z', huella: String(i + 1).repeat(64),
        contratos: r.definicion.length, operaciones: r.definicion.reduce((t, e) => t + e.operaciones.length, 0),
        guias: r.definicion.reduce((t, e) => t + e.guias.length, 0), vigente: i + 1 === vigente,
      }))
      .reverse(),
    detalleRevision,
  };
}

/** Consolidado ficticio por ambiente (HU 3.4): un contrato con desfase y breaking pendientes, otro al día. */
const CONSOLIDADO_CONTRATOS = [
  {
    id: 'c1', codigo: 'api-externa', nombre: CONTRATOS[0]!.nombre, tipo: 'openapi', aplicativo: CONTRATOS[0]!.aplicativo,
    canales: [
      { ambiente: 'dev', version: '1.3.0-dev.a1b2c3d', esEstable: false, desplegadoEn: '2026-10-23T16:00:00Z' },
      { ambiente: 'qa', version: '1.3.0-rc.a1b2c3d', esEstable: false, desplegadoEn: '2026-10-23T17:00:00Z' },
      { ambiente: 'pdn', version: '1.2.0', esEstable: true, desplegadoEn: '2026-10-01T15:00:00Z' },
    ],
    desfases: [{ ambiente: 'qa', respectoDe: 'pdn', tipo: 'adelante', mensaje: 'qa (1.3.0-rc.a1b2c3d) va adelante de pdn (1.2.0)' }],
    breakingPendientes: {
      desde: '1.2.0', hasta: '1.3.0-rc.a1b2c3d', estado: 'con_breaking', fuente: 'changelog',
      cambios: [
        { regla: 'api-removed-without-deprecation', texto: 'Se quitó la operación sin deprecarla', metodo: 'GET', ruta: '/api/v1/tramites-demo/{id}/adjuntos-con-una-ruta-larga' },
        { regla: 'request-property-became-required', texto: 'La propiedad «placa» del cuerpo pasó a ser obligatoria', metodo: 'POST', ruta: '/api/v1/tramites-demo' },
      ],
    },
  },
  {
    id: 'c2', codigo: 'eventos', nombre: 'Eventos Demo', tipo: 'asyncapi', aplicativo: CONTRATOS[1]!.aplicativo,
    canales: [
      { ambiente: 'dev', version: null, esEstable: null, desplegadoEn: null },
      { ambiente: 'qa', version: '1.2.0', esEstable: true, desplegadoEn: '2026-10-20T15:00:00Z' },
      { ambiente: 'pdn', version: '1.2.0', esEstable: true, desplegadoEn: '2026-10-20T16:00:00Z' },
    ],
    desfases: [],
    breakingPendientes: null,
  },
];

/**
 * HU 3.6 y 3.9: el contrato real de ICT de FLIT 2 enriquecido con el Excel (`contracts/ict/core-ict.enriquecido.v1.yaml`,
 * ATLAS-ADR-0037), con dos versiones, su diff y sus canales (HU 3.4).
 */
const VERSIONES_ICT = [
  { ...versionSimulada('3.0.0', true, '2026-10-20T15:00:00Z'), tieneBreaking: true },
  versionSimulada('2.9.0', true, '2026-09-01T15:00:00Z'),
];
const CONTRATO_ICT = {
  id: 'c3', codigo: 'core-ict', nombre: 'API de ICT · Integración con Terceros (core-ict)', descripcion: null, tipo: 'openapi',
  visibilidad: 'compartible', aplicativo: { id: 'a1', slug: 'flit2', nombre: 'FLIT 2.0' }, repositorio: 'flitsas-demo/flit',
  versiones: 2, ultimaVersion: VERSIONES_ICT[0], ultimaEstable: VERSIONES_ICT[0],
};
const DIFF_ICT = {
  contratoId: 'c3', desde: '2.9.0', hasta: '3.0.0', motor: 'oasdiff v1.33.0', sinDiferencias: false, changelog: '', desdeCache: true,
  resumen: { total: 2, errores: 1, advertencias: 0, informativos: 1, breaking: 1, hayBreaking: true },
  cambios: [
    { regla: 'api-path-removed-without-deprecation', nivel: 'error', breaking: true, texto: 'Se quitó la ruta /api/v1/ict/antigua sin deprecarla', comentario: null, metodo: 'GET', ruta: '/api/v1/ict/antigua', operationId: null, seccion: 'paths' },
    { regla: 'endpoint-added', nivel: 'info', breaking: false, texto: 'Se agregó la ruta de reproceso', comentario: null, metodo: 'POST', ruta: '/api/v1/ict/reprocess/{id}', operationId: null, seccion: 'paths' },
  ],
};
/** Canales ficticios de c3 (HU 3.4): la 3.0.0 en pdn y en qa, nada en dev. */
const CANALES_ICT = {
  ambientes: ['dev', 'qa', 'pdn'],
  canales: [
    { ambiente: 'qa', versionId: 'v-3.0.0', version: '3.0.0', esEstable: true, desplegadoEn: '2026-10-20T16:00:00Z', desplegadoPor: 'github:flitsas-demo/flit@staging', commit: '0123456789abcdef0123456789abcdef01234567', ejecucion: '11' },
    { ambiente: 'pdn', versionId: 'v-3.0.0', version: '3.0.0', esEstable: true, desplegadoEn: '2026-10-21T16:00:00Z', desplegadoPor: 'github:flitsas-demo/flit@release', commit: '0123456789abcdef0123456789abcdef01234567', ejecucion: '12' },
  ],
  historial: [
    { ambiente: 'pdn', version: '3.0.0', versionAnterior: '2.9.0', desplegadoEn: '2026-10-21T16:00:00Z', desplegadoPor: 'github:flitsas-demo/flit@release', repositorio: 'flitsas-demo/flit', rama: 'release', commit: '0123456789abcdef0123456789abcdef01234567', ejecucion: '12' },
    { ambiente: 'qa', version: '3.0.0', versionAnterior: '2.9.0', desplegadoEn: '2026-10-20T16:00:00Z', desplegadoPor: 'github:flitsas-demo/flit@staging', repositorio: 'flitsas-demo/flit', rama: 'staging', commit: '0123456789abcdef0123456789abcdef01234567', ejecucion: '11' },
  ],
};
const OPENAPI_SIMULADO = `openapi: 3.1.0
info: { title: API Demo, version: 1.3.0-dev.a1b2c3d }
paths:
  /tramites/{id}:
    get:
      summary: Obtener un trámite
      parameters: [{ name: id, in: path, required: true, schema: { type: string } }]
      responses: { '200': { description: El trámite. }, '404': { description: No existe. } }
`;
const ASYNCAPI_SIMULADO = `asyncapi: 3.0.0
info: { title: Eventos Demo, version: 1.2.0 }
channels:
  radicados:
    address: tramites.radicado.con.una.direccion.de.canal.larga
    messages:
      radicado: { $ref: '#/components/messages/Radicado' }
operations:
  publicarRadicado:
    action: send
    summary: Se radicó un trámite.
    channel: { $ref: '#/channels/radicados' }
components:
  messages:
    Radicado:
      name: TramiteRadicado
      payload:
        type: object
        required: [id]
        properties:
          id: { type: string, description: Id del trámite }
          organismo: { type: string, description: Organismo Demo que lo radicó }
`;

/** Simula la sesión de una empleada con los permisos dados y los endpoints que usan el shell, la matriz y «hola». */
export async function simularApi(page: Page, opciones: OpcionesApi): Promise<void> {
  const matriz = matrizDelRepositorio();
  const saludos = [{ id: 's1', texto: 'Hola desde Organismo Demo', organizacionId: 'o1', creadoEn: new Date().toISOString() }];
  const aplicativos = opciones.sinConexion ? [...APLICATIVOS, FLITO] : APLICATIVOS;
  const reparto = opciones.sinConexion
    ? { ...REPARTO_SIMULADO, productos: [...REPARTO_SIMULADO.productos, { aplicativo: REF_FLITO, principal: PRINCIPAL_FLITO, secundario: SECUNDARIO_FLITO }] }
    : REPARTO_SIMULADO;
  let sesionCerrada = false;
  // Paquetes (HU 4.1): las revisiones guardadas en esta página y la vigente.
  const revisionesPaquete: SolicitudPaqueteSimulada[] = [];
  let vigentePaquete: number | null = null;

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const metodo = route.request().method();
    switch (url.pathname) {
      case '/api/v1/identidad/salir':
        sesionCerrada = true;
        return route.fulfill({ status: 204 });
      case '/api/v1/identidad/proveedores':
        return json(route, { microsoft: false, simulado: true });
      case '/api/v1/plataforma/yo':
        if (sesionCerrada) return route.fulfill({ status: 401 });
        return json(route, {
          sub: 'u1',
          personaId: 'p1',
          nombre: 'Empleada Demo',
          tipoPrincipal: 'empleado',
          organizacionId: 'o1',
          proveedor: 'simulado',
          roles: opciones.roles ?? ['atlas.auditor'],
          permisos: opciones.permisos,
        });
      case '/api/v1/plataforma/banderas':
        return json(route, opciones.banderas ?? {});
      case '/api/v1/plataforma/notificaciones':
        return json(route, { noLeidas: 0, elementos: [] });
      case '/api/v1/plataforma/push/clave':
        return json(route, { habilitado: false, clavePublica: null });
      case '/api/v1/plataforma/permisos/matriz':
        return opciones.permisos.includes('atlas.roles.ver') ? json(route, matriz) : route.fulfill({ status: 403 });
      case '/api/v1/hola/saludos': {
        if (!opciones.banderas?.['modulo.hola']) return route.fulfill({ status: 404 });
        if (metodo === 'POST') {
          if (route.request().headers()['x-atlas-csrf'] !== '1') return route.fulfill({ status: 403 });
          const { texto } = route.request().postDataJSON() as { texto: string };
          const nuevo = { id: `s${saludos.length + 1}`, texto, organizacionId: 'o1', creadoEn: new Date().toISOString() };
          saludos.unshift(nuevo);
          return json(route, nuevo, 201);
        }
        return json(route, saludos);
      }
      case '/api/v1/catalogo/aplicativos': {
        if (!opciones.permisos.includes('atlas.catalogo.ver')) return route.fulfill({ status: 403 });
        const grupo = url.searchParams.get('grupo');
        const elementos = aplicativos.filter((a) => !grupo || a.grupo === grupo);
        return json(route, { total: elementos.length, elementos });
      }
      // Contratos (HU 3.2).
      case '/api/v1/contratos': {
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        const tipo = url.searchParams.get('tipo');
        const elementos = CONTRATOS.filter((c) => !tipo || c.tipo === tipo);
        return json(route, { total: elementos.length, elementos });
      }
      case '/api/v1/contratos/consolidado': {
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        const tipo = url.searchParams.get('tipo');
        const soloDesfases = url.searchParams.get('desfases') === 'true';
        const elementos = CONSOLIDADO_CONTRATOS.filter((c) => (!tipo || c.tipo === tipo) && (!soloDesfases || c.desfases.length > 0));
        return json(route, {
          ambientes: ['dev', 'qa', 'pdn'], total: elementos.length,
          conDesfases: elementos.filter((c) => c.desfases.length > 0).length,
          conBreaking: elementos.filter((c) => c.breakingPendientes?.estado === 'con_breaking').length,
          elementos,
        });
      }
      case '/api/v1/contratos/c1/versiones':
      case '/api/v1/contratos/c2/versiones':
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        return json(route, url.pathname.includes('/c1/') ? VERSIONES_CONTRATO : [VERSIONES_CONTRATO[1]]);
      // Guías (HU 3.7): quien edita, sin ser responsable (no aprueba lo compartible).
      case '/api/v1/contratos/c1/guias':
      case '/api/v1/contratos/guias/g1':
      case '/api/v1/contratos/guias/g1/revisiones':
      case '/api/v1/contratos/guias/g1/revisiones/1':
      case '/api/v1/contratos/guias/g1/revisiones/2': {
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        const edita = opciones.permisos.includes('atlas.contratos.guias.editar');
        if (url.pathname.endsWith('/c1/guias')) return json(route, { contratoId: 'c1', puedeEditar: edita, puedeAprobar: false, elementos: [GUIA] });
        if (url.pathname.endsWith('/revisiones')) return json(route, REVISIONES_GUIA);
        if (url.pathname.endsWith('/revisiones/1')) return json(route, REVISIONES_GUIA[1]);
        if (url.pathname.endsWith('/revisiones/2')) return json(route, REVISIONES_GUIA[0]);
        return json(route, { guia: GUIA, revision: REVISIONES_GUIA[0], puedeEditar: edita, puedeAprobar: false });
      }
      // Paquetes (HU 4.1): leer con ver_internos; armar con atlas.paquetes.gestionar.
      case '/api/v1/contratos/paquetes': {
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        const gestiona = opciones.permisos.includes('atlas.paquetes.gestionar');
        if (metodo === 'POST') {
          if (!gestiona) return route.fulfill({ status: 403 });
          revisionesPaquete.push(route.request().postDataJSON() as SolicitudPaqueteSimulada);
          const { detalleRevision: _, ...creado } = detallePaquete(revisionesPaquete, vigentePaquete, gestiona);
          return json(route, creado, 201);
        }
        const archivados = url.searchParams.get('estado') === 'archivado';
        const elementos = revisionesPaquete.length === 0 || archivados ? [] : [detallePaquete(revisionesPaquete, vigentePaquete, gestiona).paquete];
        return json(route, { puedeCrear: gestiona, elementos });
      }
      case '/api/v1/contratos/paquetes/elegibles':
        if (!opciones.permisos.includes('atlas.paquetes.gestionar')) return route.fulfill({ status: 403 });
        return json(route, { elementos: [ELEGIBLE_PAQUETE] });
      case '/api/v1/contratos/paquetes/versiones/v13/operaciones':
      case '/api/v1/contratos/paquetes/versiones/v12/operaciones': {
        if (!opciones.permisos.includes('atlas.paquetes.gestionar')) return route.fulfill({ status: 403 });
        const etiqueta = url.searchParams.get('etiqueta');
        const soloPublicas = url.searchParams.get('soloPublicas') === 'true';
        const versionId = url.pathname.includes('/v13/') ? 'v13' : 'v12';
        return json(route, {
          contratoId: 'c1', versionId, version: versionId === 'v13' ? '1.3.0' : '1.2.0', total: OPERACIONES_PAQUETE.length, etiquetas: ['public', 'tramites'],
          elementos: OPERACIONES_PAQUETE.filter((o) => (!soloPublicas || o.publica) && (!etiqueta || o.etiquetas.includes(etiqueta))),
        });
      }
      case '/api/v1/contratos/paquetes/p1':
      case '/api/v1/contratos/paquetes/p1/revisiones':
      case '/api/v1/contratos/paquetes/p1/publicacion':
      case '/api/v1/contratos/paquetes/p1/revisiones/1':
      case '/api/v1/contratos/paquetes/p1/revisiones/1/fijacion':
      case '/api/v1/contratos/paquetes/p1/revisiones/2': {
        if (!opciones.permisos.includes('atlas.contratos.ver_internos') || revisionesPaquete.length === 0) return route.fulfill({ status: 404 });
        const gestiona = opciones.permisos.includes('atlas.paquetes.gestionar');
        if (metodo === 'POST' && !gestiona) return route.fulfill({ status: 403 });
        if (metodo === 'POST' && url.pathname.endsWith('/revisiones')) revisionesPaquete.push(route.request().postDataJSON() as SolicitudPaqueteSimulada);
        if (metodo === 'POST' && url.pathname.endsWith('/publicacion')) vigentePaquete = revisionesPaquete.length;
        if (metodo === 'POST' && url.pathname.endsWith('/fijacion')) vigentePaquete = 1;
        const { detalleRevision, ...detalle } = detallePaquete(revisionesPaquete, vigentePaquete, gestiona);
        const numero = /\/revisiones\/(\d+)$/.exec(url.pathname)?.[1];
        if (metodo === 'GET' && numero) return json(route, detalleRevision(Number(numero)));
        return json(route, detalle, metodo === 'POST' && url.pathname.endsWith('/revisiones') ? 201 : 200);
      }
      // «Compartido con» (HU 4.3): una organización con la que ya se comparte y las que se pueden elegir.
      case '/api/v1/contratos/paquetes/p1/comparticiones':
        if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
        return json(route, {
          puedeCompartir: opciones.permisos.includes('atlas.paquetes.gestionar') && vigentePaquete !== null,
          elementos: [
            {
              id: 'k1', paqueteId: 'p1', paqueteNombre: 'Paquete Integradores Demo', paqueteEstado: 'publicado',
              organizacion: { id: 'o2', nombre: 'Organismo Demo de Tránsito con un nombre largo', tipo: 'cliente', estado: 'activa' },
              revisionFijada: null, revisionQueVe: vigentePaquete, puedeExportar: true, puedeSandbox: false,
              vigenteDesde: '2026-10-24T15:00:00Z', vigenteHasta: '2027-01-31T23:59:59-05:00', estado: 'vigente',
              otorgadoPor: { id: 'p1', nombre: 'Empleada Demo' }, creadaEn: '2026-10-24T15:00:00Z', actualizadaEn: '2026-10-24T15:00:00Z',
              revocadaEn: null, puedeGestionar: opciones.permisos.includes('atlas.paquetes.gestionar'),
            },
          ],
        });
      case '/api/v1/contratos/comparticiones/organizaciones':
        if (!opciones.permisos.includes('atlas.paquetes.gestionar')) return route.fulfill({ status: 403 });
        return json(route, [
          { id: 'o2', nombre: 'Organismo Demo de Tránsito con un nombre largo', tipo: 'cliente' },
          { id: 'o3', nombre: 'Proveedor Demo', tipo: 'proveedor' },
        ]);
      // Responsables (HU 2.3 a 2.5).
      case '/api/v1/equipo/responsabilidades':
        if (!opciones.permisos.includes('atlas.equipo.ver')) return route.fulfill({ status: 403 });
        return json(route, { ...reparto, puedeGestionar: opciones.permisos.includes('atlas.equipo.gestionar') });
      case '/api/v1/equipo/revisiones':
        return json(route, {
          proxima: { id: 'v1', fecha: '2027-01-20', estado: 'pendiente', recordatorioEnviadoEn: null, hechaEn: null, hechaPor: null, nota: null },
          historial: [],
          diasAnticipacion: 7,
        });
      case '/api/v1/equipo/carga':
        return json(route, {
          media: 1.2,
          umbralAlto: 1.56,
          umbralBajo: 0.84,
          personas: [
            { persona: PERSONA_LARGA, principales: 1, secundarias: 1, puntaje: 1.2, nivel: 'normal' },
            { persona: PERSONA_CORTA, principales: 1, secundarias: 1, puntaje: 1.2, nivel: 'normal' },
          ],
        });
      case '/api/v1/equipo/yo/responsabilidades':
        return json(route, { persona: PERSONA_LARGA, principalDe: [], secundarioDe: [] });
      case '/api/v1/equipo/aplicativos/flit2/responsables':
        return json(route, {
          aplicativo: REF_FLIT2,
          principal: PRINCIPAL_FLIT2,
          secundario: SECUNDARIO_FLIT2,
          heredadoDe: null,
          definiciones: DEFINICIONES_SIMULADAS,
          traspaso: null,
          version: 5,
          puedeGestionar: false,
          veContacto: false,
        });
      case '/api/v1/equipo/aplicativos/flit2/a-quien-llamo':
        return json(route, {
          aplicativo: REF_FLIT2,
          hoy: '2026-10-24',
          esSabado: true,
          soporte: {
            fecha: '2026-10-24',
            festivo: null,
            equipos: [{ id: 'e1', nombre: 'Desarrollo TI', area: 'TI', horaInicio: '08:00', horaFin: '12:00', personas: [PERSONA_LARGA] }],
          },
          principal: PRINCIPAL_FLIT2,
          secundario: SECUNDARIO_FLIT2,
          heredadoDe: null,
          veContacto: false,
        });
      // Soporte de los sábados (HU 2.6): los dos equipos de la migración y un turno con nombre y correo largos.
      case '/api/v1/soporte/equipos':
        return json(
          route,
          [
            { id: 'e1', nombre: 'Desarrollo TI', personasMinimo: 1, personasMaximo: 1 },
            { id: 'e2', nombre: 'Operaciones', personasMinimo: 1, personasMaximo: 2 },
          ].map((e) => ({
            ...e,
            area: null,
            horaInicio: null,
            horaFin: null,
            horarioEfectivo: '08:00 a 12:00',
            activo: true,
            puedeGestionar: opciones.permisos.includes('atlas.soporte.gestionar'),
            puedeAprobar: opciones.permisos.includes('atlas.soporte.aprobar'),
          })),
        );
      case '/api/v1/soporte/este-sabado':
        return json(route, {
          fecha: '2026-10-24',
          festivo: null,
          equipos: [
            { equipo: { id: 'e1', nombre: 'Desarrollo TI' }, turnos: [], faltan: 1 },
            { equipo: { id: 'e2', nombre: 'Operaciones' }, turnos: [], faltan: 1 },
          ],
        });
      case '/api/v1/soporte/turnos':
        return json(route, {
          desde: url.searchParams.get('desde') ?? '2026-10-24',
          hasta: url.searchParams.get('hasta') ?? '2027-01-23',
          equipoId: url.searchParams.get('equipo'),
          turnos: [
            {
              id: 't1',
              equipo: { id: 'e2', nombre: 'Operaciones' },
              fecha: url.searchParams.get('desde') ?? '2026-10-24',
              persona: { id: 'p2', nombre: 'Persona Demo Con Un Nombre Bastante Largo', correo: 'persona.demo.con.correo.largo@ejemplo.com', cargo: 'Desarrollo', telefono: null },
              horaInicio: null,
              horaFin: null,
              estado: 'programado',
              origen: 'manual',
              turnoReemplazadoId: null,
              festivo: null,
              observacion: null,
              requiereRevision: false,
            },
            ...(opciones.sinConexion
              ? [
                  {
                    id: 't-p1',
                    equipo: { id: 'e1', nombre: 'Desarrollo TI' },
                    fecha: sabadoDesde(url.searchParams.get('desde') ?? '2026-10-24'),
                    persona: { id: 'p1', nombre: 'Empleada Demo', correo: 'empleada.demo@ejemplo.com', cargo: 'Desarrolladora', telefono: TELEFONO_FICTICIO },
                    horaInicio: '08:00',
                    horaFin: '12:00',
                    estado: 'programado',
                    origen: 'generado',
                    turnoReemplazadoId: null,
                    festivo: null,
                    observacion: null,
                    requiereRevision: false,
                  },
                ]
              : []),
          ],
          festivos: [],
        });
      case '/api/v1/soporte/configuracion':
        return json(route, {
          horaInicioPorDefecto: '08:00',
          horaFinPorDefecto: '12:00',
          reglaFestivos: 'sin_turno',
          diasAnticipacionRecordatorio: 2,
          canalExterno: null,
          actualizadoEn: null,
          horaRecordatorios: '07:00',
        });
      // Recordatorios de turno (HU 2.11): una corrida con un fallo que se reintenta.
      case '/api/v1/soporte/recordatorios':
        if (!opciones.permisos.includes('atlas.soporte.gestionar')) return route.fulfill({ status: 403 });
        return json(route, {
          programados: true,
          hora: '07:00',
          diasAnticipacion: 2,
          canales: { push: true, correo: true, teams: false },
          enCurso: false,
          corridas: [
            { fecha: '2026-10-22', disparo: 'programada', iniciadaEn: '2026-10-22T12:00:00Z', terminadaEn: '2026-10-22T12:00:05Z', enviados: 6, fallidos: 1, omitidos: 2 },
          ],
        });
      case '/api/v1/soporte/equipos/e1/participantes':
      case '/api/v1/soporte/equipos/e2/participantes':
        return json(route, [
          { id: 'pa1', equipoId: 'e1', personaId: 'p2', nombre: 'Persona Demo', correo: 'persona@ejemplo.com', orden: 1, activo: true, estadoPersona: 'activa' },
        ]);
      case '/api/v1/soporte/ausencias':
        return json(route, []);
      // Cambios de turno y compensaciones (HU 2.9): una solicitud por aprobar con nombres largos y una compensación.
      case '/api/v1/soporte/solicitudes-cambio': {
        const persona = (id: string, nombre: string) => ({ id, nombre, correo: null, cargo: null, telefono: null });
        return json(route, [
          {
            id: 'sc1',
            turno: {
              id: 't1', equipo: { id: 'e2', nombre: 'Operaciones' }, fecha: '2026-10-31', persona: persona('p2', 'Persona Demo Con Un Nombre Bastante Largo'),
              horaInicio: '08:00', horaFin: '12:00', estado: 'programado', origen: 'manual', turnoReemplazadoId: null, festivo: null, observacion: null, requiereRevision: false,
            },
            solicitante: persona('p2', 'Persona Demo Con Un Nombre Bastante Largo'),
            reemplazo: persona('p3', 'Otra Persona Demo Con Apellidos Largos'),
            estado: 'pendiente_aprobacion', motivo: 'Cita médica programada desde hace semanas', turnoNuevoId: null, resueltaPor: null, resueltaEn: null,
            creadaEn: '2026-10-23T15:00:00Z',
            historial: [
              { accion: 'solicitada', persona: { id: 'p2', nombre: 'Persona Demo Con Un Nombre Bastante Largo' }, nota: 'Cita médica', ocurridoEn: '2026-10-23T15:00:00Z', hechaEn: null },
              { accion: 'aceptada', persona: { id: 'p3', nombre: 'Otra Persona Demo Con Apellidos Largos' }, nota: null, ocurridoEn: '2026-10-23T16:00:00Z', hechaEn: '2026-10-23T15:30:00Z' },
            ],
            puedeResponder: false, puedeResolver: opciones.permisos.includes('atlas.soporte.aprobar'), puedeCancelar: false,
          },
        ]);
      }
      case '/api/v1/soporte/compensaciones/pendientes':
        return json(route, {
          equipoId: null,
          total: 1,
          personas: [
            {
              persona: { id: 'p3', nombre: 'Otra Persona Demo Con Apellidos Largos', correo: null, cargo: null, telefono: null },
              pendientes: 1, sinFecha: 1, proxima: null,
              compensaciones: [
                {
                  id: 'c1', turno: { id: 't0', fecha: '2026-10-17', equipo: { id: 'e1', nombre: 'Desarrollo TI' } },
                  persona: { id: 'p3', nombre: 'Otra Persona Demo Con Apellidos Largos', correo: null, cargo: null, telefono: null },
                  fechaCompensada: null, estado: 'pendiente', aprobadaPor: null, nota: null, puedeCambiar: opciones.permisos.includes('atlas.soporte.aprobar'),
                  creadaEn: '2026-10-18T12:00:00Z',
                },
              ],
            },
          ],
        });
      // Importador del Excel (HU 2.7): una vista previa ficticia con interpretaciones, un caso ambiguo y una persona nueva.
      case '/api/v1/soporte/importacion/vista-previa':
        return json(route, {
          equipo: { id: 'e1', nombre: 'Desarrollo TI' },
          hoja: 'Rotacion Soporte',
          huella: 'h1',
          resumen: { filas: 2, importadas: 1, yaImportadas: 0, omitidas: 1, turnos: 2, compensaciones: 1, personasInactivas: 0, participantes: 1, generadosQuitados: 0, interpretaciones: 3, ambiguos: 1 },
          filas: [
            {
              fila: 2, fecha: '2026-06-27', textoFecha: '46200', estado: 'aplicado', observacion: 'Compensado: lo cubrió alguien del equipo de soporte del sábado',
              responsable: { texto: 'persona demo con un nombre bastante largo', clave: 'persona demo con un nombre bastante largo', coincidencia: 'exacta', personaId: 'p2', nombre: 'Persona Demo Con Un Nombre Bastante Largo', nueva: false },
              reemplazo: null, compensacion: null, requiereRevision: true, accion: 'crear', motivo: null,
              interpretaciones: ['Fecha serial de Excel 46200 → 27/06/2026.', '«persona demo con un nombre bastante largo» es Persona Demo Con Un Nombre Bastante Largo (sin tildes ni mayúsculas).'],
              ambiguedades: ['Compensado «lo cubrió alguien del equipo de soporte del sábado» no es una fecha ni dice «reemplazo»: queda en la observación para revisión.'],
            },
            {
              fila: 3, fecha: '2026-07-04', textoFecha: '2026-07-04', estado: 'aplicado', observacion: null,
              responsable: { texto: 'Persona Ficticia', clave: 'persona ficticia', coincidencia: 'ninguna', personaId: null, nombre: null, nueva: false },
              reemplazo: null, compensacion: null, requiereRevision: false, accion: 'omitir',
              motivo: '«Persona Ficticia» no existe en Atlas: confirma crearla como persona inactiva para importar la fila.', interpretaciones: [], ambiguedades: [],
            },
          ],
          personasNuevas: [{ clave: 'persona ficticia', nombre: 'Persona Ficticia', filas: 1, ultimaFecha: '2026-07-04', confirmada: false }],
          nombresAmbiguos: [],
          participantesNuevos: [{ personaId: 'p2', nombre: 'Persona Demo Con Un Nombre Bastante Largo', activo: true }],
          avisos: ['Hay personas del Excel que no existen en Atlas: confirma crearlas inactivas o sus filas no se importan.'],
        });
      case '/api/v1/soporte/festivos':
        return json(route, [{ fecha: '2026-12-25', nombre: 'Navidad', tipo: 'fijo', trasladado: false, id: null }]);
      // HU 2.10: Microsoft 365 apagado (como en local y dev) y un feed de equipo.
      case '/api/v1/soporte/microsoft-365':
        return json(route, {
          calendario: {
            activo: false,
            motivo: 'Falta el buzón compartido del calendario (Soporte__M365__Buzon) y su permiso en Graph.',
            nombreCalendario: 'Soporte TI sábados',
            pendientes: 0,
            conErrores: 0,
            ultimaSincronizacion: null,
          },
          teams: { activo: false, motivo: 'Falta la URL del flujo de Workflows del canal de Teams (Plataforma__Teams__Canales__soporte).', pendientes: 0 },
        });
      case '/api/v1/soporte/feeds':
        if (route.request().method() === 'POST')
          return json(route, {
            id: 'f1', tipo: 'personal', equipo: null, creadoEn: '2026-10-23T15:00:00Z',
            url: 'https://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio-de-una-direccion-larga-para-el-movil.ics',
            urlWebcal: 'webcal://atlas.ejemplo.com/api/v1/soporte/ics/token-ficticio-de-una-direccion-larga-para-el-movil.ics',
          }, 201);
        return json(route, [{ id: 'f0', tipo: 'equipo', equipo: { id: 'e2', nombre: 'Operaciones' }, creadoEn: '2026-10-22T15:00:00Z', ultimoUsoEn: '2026-10-23T10:00:00Z' }]);
      default: {
        // Pantalla del contrato (HU 3.6 y 3.9): c3 es el contrato real de ICT (saneado) y c2 un AsyncAPI ficticio.
        const deContrato = /^\/api\/v1\/contratos\/(c[123])(\/.*)?$/.exec(url.pathname);
        if (deContrato) {
          if (!opciones.permisos.includes('atlas.contratos.ver_internos')) return route.fulfill({ status: 403 });
          const [, id, resto = ''] = deContrato;
          const contrato = id === 'c3' ? CONTRATO_ICT : CONTRATOS.find((c) => c.id === id)!;
          if (resto === '') return json(route, contrato);
          if (resto === '/versiones') return json(route, id === 'c3' ? VERSIONES_ICT : id === 'c1' ? VERSIONES_CONTRATO : [VERSIONES_CONTRATO[1]]);
          if (resto.endsWith('/operaciones')) return json(route, []);
          if (resto.endsWith('/spec')) {
            const texto = id === 'c3' ? readFileSync(resolve(RAIZ, 'contracts/ict/core-ict.enriquecido.v1.yaml'), 'utf8') : id === 'c2' ? ASYNCAPI_SIMULADO : OPENAPI_SIMULADO;
            return route.fulfill({ status: 200, contentType: 'application/yaml', body: texto });
          }
          if (resto === '/diff') return json(route, DIFF_ICT);
          if (resto === '/canales') return json(route, CANALES_ICT);
        }
        const ficha = /^\/api\/v1\/catalogo\/aplicativos\/([a-z0-9-]+)$/.exec(url.pathname);
        const aplicativo = ficha && aplicativos.find((a) => a.slug === ficha[1]);
        if (aplicativo) {
          return json(route, {
            ...aplicativo,
            repo: 'flitsas/flit',
            rutaEnRepo: 'services/core-notificaciones',
            ambientes: ['dev', 'qa', 'pdn'],
            hijos: aplicativos.filter((a) => a.padreId === aplicativo.id),
            permisos: { editar: opciones.permisos.includes('atlas.catalogo.editar'), gestionar: opciones.permisos.includes('atlas.catalogo.gestionar') },
            creadoEn: '2026-10-23T12:00:00Z',
            actualizadoEn: '2026-10-23T12:00:00Z',
          });
        }
        return route.fulfill({ status: 404 });
      }
    }
  });
}
