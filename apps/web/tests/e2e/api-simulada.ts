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
  telefono: null,
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

/** Simula la sesión de una empleada con los permisos dados y los endpoints que usan el shell, la matriz y «hola». */
export async function simularApi(page: Page, opciones: OpcionesApi): Promise<void> {
  const matriz = matrizDelRepositorio();
  const saludos = [{ id: 's1', texto: 'Hola desde Organismo Demo', organizacionId: 'o1', creadoEn: new Date().toISOString() }];

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const metodo = route.request().method();
    switch (url.pathname) {
      case '/api/v1/plataforma/yo':
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
        const elementos = APLICATIVOS.filter((a) => !grupo || a.grupo === grupo);
        return json(route, { total: elementos.length, elementos });
      }
      // Responsables (HU 2.3 a 2.5).
      case '/api/v1/equipo/responsabilidades':
        if (!opciones.permisos.includes('atlas.equipo.ver')) return route.fulfill({ status: 403 });
        return json(route, { ...REPARTO_SIMULADO, puedeGestionar: opciones.permisos.includes('atlas.equipo.gestionar') });
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
          ],
          festivos: [],
        });
      case '/api/v1/soporte/configuracion':
        return json(route, {
          horaInicioPorDefecto: '08:00',
          horaFinPorDefecto: '12:00',
          reglaFestivos: 'sin_turno',
          diasAnticipacionRecordatorio: null,
          canalExterno: null,
          actualizadoEn: null,
        });
      case '/api/v1/soporte/equipos/e1/participantes':
      case '/api/v1/soporte/equipos/e2/participantes':
        return json(route, [
          { id: 'pa1', equipoId: 'e1', personaId: 'p2', nombre: 'Persona Demo', correo: 'persona@ejemplo.com', orden: 1, activo: true, estadoPersona: 'activa' },
        ]);
      case '/api/v1/soporte/ausencias':
        return json(route, []);
      case '/api/v1/soporte/festivos':
        return json(route, [{ fecha: '2026-12-25', nombre: 'Navidad', tipo: 'fijo', trasladado: false, id: null }]);
      default: {
        const ficha = /^\/api\/v1\/catalogo\/aplicativos\/([a-z0-9-]+)$/.exec(url.pathname);
        const aplicativo = ficha && APLICATIVOS.find((a) => a.slug === ficha[1]);
        if (aplicativo) {
          return json(route, {
            ...aplicativo,
            repo: 'flitsas/flit',
            rutaEnRepo: 'services/core-notificaciones',
            ambientes: ['dev', 'qa', 'pdn'],
            hijos: APLICATIVOS.filter((a) => a.padreId === aplicativo.id),
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
