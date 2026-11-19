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
      default:
        return route.fulfill({ status: 404 });
    }
  });
}
