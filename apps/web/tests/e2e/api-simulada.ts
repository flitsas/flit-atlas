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
