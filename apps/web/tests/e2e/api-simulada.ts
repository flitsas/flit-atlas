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

/** Simula la sesión de una empleada con los permisos dados y los endpoints que usan el shell, la matriz y «hola». */
export async function simularApi(page: Page, opciones: OpcionesApi): Promise<void> {
  const matriz = matrizDelRepositorio();
  const saludos = [{ id: 's1', texto: 'Hola desde Organismo Demo', organizacionId: 'o1', creadoEn: new Date().toISOString() }];
  const aplicativos = opciones.sinConexion ? [...APLICATIVOS, FLITO] : APLICATIVOS;
  const reparto = opciones.sinConexion
    ? { ...REPARTO_SIMULADO, productos: [...REPARTO_SIMULADO.productos, { aplicativo: REF_FLITO, principal: PRINCIPAL_FLITO, secundario: SECUNDARIO_FLITO }] }
    : REPARTO_SIMULADO;
  let sesionCerrada = false;

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
