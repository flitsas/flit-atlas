import { expect, type Page, type Route, test } from '@playwright/test';

import { esMovil } from './ayudas';
import { matrizDelRepositorio, simularApi, TELEFONO_FICTICIO } from './api-simulada';

/**
 * HU 2.12 (ATLAS-ADR-0034), CA: en modo avión, un desarrollador ve su próximo turno y los responsables de FLITO. Con el
 * service worker de producción (precarga el shell) y la API simulada: se carga en línea, se pasa a sin conexión, se
 * recarga y se ve lo guardado con su aviso. Además, el dispositivo no guarda teléfonos y cerrar sesión lo borra todo.
 * Necesita la PWA construida (`vite preview` o la imagen nginx): en `vite dev` no hay service worker.
 */
test.use({ serviceWorkers: 'allow' });

/** Los permisos del rol atlas.desarrollador según la matriz generada desde el código. */
function permisosDeDesarrollador(): string[] {
  return matrizDelRepositorio()
    .modulos.flatMap((m) => m.permisos)
    .filter((p) => p.roles.includes('atlas.desarrollador'))
    .map((p) => p.codigo);
}

/** Recursos guardados en IndexedDB (`atlas` → `recursos`), como JSON. */
function recursosGuardados(page: Page): Promise<Record<string, string>> {
  return page.evaluate(
    () =>
      new Promise<Record<string, string>>((resolver) => {
        const pedido = indexedDB.open('atlas');
        pedido.onsuccess = () => {
          const base = pedido.result;
          if (!base.objectStoreNames.contains('recursos')) {
            base.close();
            resolver({});
            return;
          }
          const todos = base.transaction('recursos').objectStore('recursos').getAll();
          todos.onsuccess = () => {
            base.close();
            resolver(Object.fromEntries((todos.result as { clave: string; datos: unknown }[]).map((r) => [r.clave, JSON.stringify(r.datos)])));
          };
        };
        pedido.onerror = () => resolver({});
      }),
  );
}

test('en modo avión un desarrollador ve su próximo turno y los responsables de FLITO; cerrar sesión lo borra', async ({ page, context }, info) => {
  await simularApi(page, { permisos: permisosDeDesarrollador(), roles: ['atlas.desarrollador'], sinConexion: true });

  // En línea: el shell instala el service worker y guarda en segundo plano lo de cada módulo según los permisos.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await expect
    .poll(async () => Object.keys(await recursosGuardados(page)).sort())
    .toEqual(expect.arrayContaining(['catalogo.aplicativos', 'equipo.responsabilidades', 'soporte.turnos']));
  const guardados = await recursosGuardados(page);
  expect(Object.values(guardados).join('\n'), 'ningún teléfono en el dispositivo').not.toContain(TELEFONO_FICTICIO);
  expect(guardados['soporte.turnos']).toContain('Empleada Demo');
  expect(guardados['equipo.responsabilidades']).toContain('Principal De Flito Demo');

  // Modo avión: el shell sale de la precarga del service worker y los datos, de IndexedDB. En Chromium `page.route`
  // sigue respondiendo aunque el contexto esté sin red: una ruta más reciente corta la API como lo haría la red.
  const sinRed = (ruta: Route) => ruta.abort('internetdisconnected');
  await context.setOffline(true);
  await page.route('**/api/**', sinRed);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  await expect(page.getByText(/Sin conexión\. Ves lo guardado en este dispositivo/)).toBeVisible();

  await page.goto('/soporte');
  const proximo = page.getByRole('region', { name: 'Tu próximo turno' });
  await expect(proximo.getByRole('article', { name: /^Turno del sábado .*: Empleada Demo$/ })).toBeVisible();
  await expect(proximo).toContainText('Desarrollo TI');
  await expect(proximo).toContainText('08:00 a 12:00');
  await expect(page.getByRole('status').filter({ hasText: /^Sin conexión: datos del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/ })).toBeVisible();

  await page.goto('/equipo/responsables');
  const productos = page.getByRole('list', { name: 'Productos' });
  const flito = productos.getByRole('listitem').filter({ has: page.getByRole('link', { name: 'FLITO', exact: true }) });
  await expect(flito.getByText('Principal De Flito Demo')).toBeVisible();
  await expect(flito.getByText('Secundaria De Flito Demo')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: /^Sin conexión: datos del/ }).first()).toBeVisible();

  // La ficha de FLITO también: del catálogo guardado, con su pestaña de responsables armada del reparto.
  await page.goto('/catalogo/flito?pestana=responsables');
  await expect(page.getByRole('heading', { level: 1, name: 'FLITO' })).toBeVisible();
  await expect(page.getByText('Principal De Flito Demo')).toBeVisible();

  // De vuelta en línea, cerrar sesión borra todo lo guardado en el dispositivo. Sin el service worker, para que la API
  // simulada atienda todas las peticiones también en WebKit (allí `page.route` no ve las que pasan por él).
  await page.evaluate(async () => {
    for (const registro of await navigator.serviceWorker.getRegistrations()) await registro.unregister();
  });
  await page.unroute('**/api/**', sinRed);
  await context.setOffline(false);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  if (esMovil(info)) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByRole('button', { name: 'Cerrar sesión' }).click();
  } else {
    await page.getByRole('button', { name: /^Cuenta de / }).click();
    await page.getByRole('menuitem', { name: 'Cerrar sesión' }).click();
  }
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
  expect(await recursosGuardados(page)).toEqual({});
});

// --- HU 4.9 (ATLAS-ADR-0034): contratos y guías favoritos del portal sin conexión ---

const SPEC_FAVORITO = `openapi: 3.1.0
info:
  title: API de Trámites Demo
  version: 1.2.0
servers:
  - url: https://qa.api.ejemplo.com/tramites
    description: QA
    x-flit-ambiente: qa
paths:
  /tramites/{id}:
    get:
      operationId: obtenerTramite
      summary: Obtener un trámite guardado
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string }
      responses:
        '200':
          description: El trámite.
`;

const CONTRATO_FAVORITO = {
  contratoId: 'c1', codigo: 'api-tramites-demo', nombre: 'API de Trámites Demo', tipo: 'openapi',
  aplicativo: { id: 'a1', slug: 'tramites-demo', nombre: 'Trámites Demo' }, versionId: 'v1', version: '1.2.0', estadoVersion: 'vigente',
  retiroEn: null, nivel: 'tecnico', operaciones: 1, guias: 1,
};
const PAQUETE_FAVORITO = {
  id: 'p1', nombre: 'Paquete Integradores Demo', descripcion: 'Para el equipo integrador.', revision: 1, nivel: 'tecnico',
  puedeExportar: false, puedeSandbox: false, vigenteHasta: null, contratos: [CONTRATO_FAVORITO],
};

/** El portal de una integradora con favoritos en memoria; `revocar()` simula que FLIT retiró la compartición. */
async function simularPortalConFavoritos(page: Page) {
  const estado = { revocado: false, favoritos: [] as Array<Record<string, unknown>> };
  const json = (ruta: Route, cuerpo: unknown, status = 200) => ruta.fulfill({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  const noEsta = (ruta: Route) => json(ruta, { codigo: 'no_encontrado', mensaje: 'No tienes este paquete compartido.' }, 404);
  await page.route('**/api/**', async (ruta) => {
    const url = new URL(ruta.request().url());
    const metodo = ruta.request().method();
    const p = url.pathname;
    if (p === '/api/v1/identidad/proveedores') return json(ruta, { microsoft: false, simulado: true });
    if (p === '/api/v1/plataforma/yo')
      return json(ruta, {
        sub: 'u1', personaId: 'p1', nombre: 'Integradora Demo', tipoPrincipal: 'externo', organizacionId: 'o1', proveedor: 'local',
        roles: ['ext.integrador'], permisos: ['atlas.portal.ver'],
      });
    if (p === '/api/v1/plataforma/banderas') return json(ruta, {});
    if (p === '/api/v1/plataforma/notificaciones') return json(ruta, { noLeidas: 0, elementos: [] });
    if (p === '/api/v1/plataforma/push/clave') return json(ruta, { habilitado: false, clavePublica: null });
    if (p === '/api/v1/portal/favoritos') return json(ruta, estado.revocado ? [] : estado.favoritos);
    const marca = /^\/api\/v1\/portal\/favoritos\/paquetes\/p1(?:\/(contratos|guias)\/(\w+))?$/.exec(p);
    if (marca) {
      const tipo = marca[1] === 'contratos' ? 'contrato' : marca[1] === 'guias' ? 'guia' : 'paquete';
      const id = `${tipo}-${marca[2] ?? 'p1'}`;
      estado.favoritos = estado.favoritos.filter((f) => f.id !== id);
      if (metodo === 'DELETE') return ruta.fulfill({ status: 204 });
      const favorito = {
        id, tipo, paqueteId: 'p1', contratoId: tipo === 'contrato' ? marca[2] : null, guiaId: tipo === 'guia' ? marca[2] : null,
        titulo: tipo === 'guia' ? 'Cómo autenticarte' : 'API de Trámites Demo 1.2.0', detalle: PAQUETE_FAVORITO.nombre,
        ruta: tipo === 'guia' ? '/portal/paquetes/p1/guias/g1' : '/portal/paquetes/p1/contratos/c1', marcadoEn: new Date().toISOString(),
      };
      estado.favoritos.unshift(favorito);
      return json(ruta, favorito);
    }
    if (estado.revocado && p.startsWith('/api/v1/portal/paquetes/')) return noEsta(ruta);
    if (p === '/api/v1/portal/inicio')
      return json(ruta, {
        organizacion: { id: 'o1', nombre: 'Organismo Demo' },
        aplicativos: [{ aplicativo: CONTRATO_FAVORITO.aplicativo, paquetes: ['p1'], contratos: 1, documentos: [] }],
        paquetes: [PAQUETE_FAVORITO],
        avisos: [],
      });
    if (p === '/api/v1/portal/paquetes/p1')
      return json(ruta, {
        paquete: PAQUETE_FAVORITO,
        contratos: [
          {
            contrato: CONTRATO_FAVORITO,
            operaciones: [{ clave: 'GET /tramites/{id}', metodo: 'GET', ruta: '/tramites/{id}', operationId: 'obtenerTramite', resumen: 'Obtener un trámite guardado', etiquetas: [] }],
            guias: [{ id: 'g1', titulo: 'Cómo autenticarte', operacion: null, revision: 1 }],
          },
        ],
        avisos: [],
      });
    if (p === '/api/v1/portal/paquetes/p1/contratos/c1/spec') return ruta.fulfill({ status: 200, contentType: 'application/yaml', body: SPEC_FAVORITO });
    if (p === '/api/v1/portal/paquetes/p1/contratos/c1/cambios') return json(ruta, { contratoId: 'c1', version: '1.2.0', base: null, elementos: [] });
    if (p === '/api/v1/portal/paquetes/p1/guias/g1')
      return json(ruta, {
        id: 'g1', paqueteId: 'p1', contratoId: 'c1', titulo: 'Cómo autenticarte', operacion: null, revision: 1,
        contenido: '## Token guardado\n\nEnvía `Authorization: Bearer $TOKEN`.',
      });
    return json(ruta, { codigo: 'no_encontrado' }, 404);
  });
  return { revocar: () => (estado.revocado = true) };
}

test('una integradora guarda un contrato y una guía en favoritos, los ve sin conexión y se borran al perder el acceso', async ({ page, context }, info) => {
  const portal = await simularPortalConFavoritos(page);

  await page.goto('/portal/paquetes/p1/contratos/c1');
  await expect(page.getByRole('heading', { level: 1, name: 'API de Trámites Demo' })).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await page.getByRole('button', { name: 'Guardar el contrato en favoritos' }).click();
  await expect(page.getByRole('button', { name: 'Quitar el contrato de favoritos' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText(/^Disponible sin conexión · sincronizado el \d{2}\/\d{2}\/\d{4}/)).toBeVisible();
  if (!esMovil(info)) {
    // En línea, el visor (Scalar en su iframe aislado, ATLAS-ADR-0039) muestra el spec podado sin pedir nada a otros orígenes.
    const origen = new URL(page.url()).origin;
    const externas: string[] = [];
    const vigilar = (r: { url: () => string }) => {
      if (!r.url().startsWith(origen) && !/^(data|blob|about):/.test(r.url())) externas.push(r.url());
    };
    page.on('request', vigilar);
    await page.getByRole('tab', { name: 'Referencia interactiva' }).click();
    await expect(page.frameLocator('iframe[title^="Referencia interactiva"]').getByRole('heading', { name: /API de Trámites Demo/ }).first()).toBeVisible({ timeout: 20_000 });
    page.off('request', vigilar);
    expect(externas).toEqual([]);
    await page.getByRole('tab', { name: 'Operaciones' }).click();
  }

  // A la guía por los enlaces (sin recargar: la API simulada atiende todo antes de que el service worker controle la página).
  await page.getByRole('link', { name: PAQUETE_FAVORITO.nombre }).click();
  await page.getByRole('link', { name: 'Cómo autenticarte' }).first().click();
  await page.getByRole('button', { name: 'Guardar la guía en favoritos' }).click();
  await expect(page.getByText(/^Disponible sin conexión · sincronizado el/)).toBeVisible();

  expect(Object.keys(await recursosGuardados(page))).toEqual(
    expect.arrayContaining([
      'contratos.fav:/api/v1/portal/paquetes/p1',
      'contratos.fav:/api/v1/portal/paquetes/p1/contratos/c1/spec',
      'contratos.fav:/api/v1/portal/paquetes/p1/guias/g1',
    ]),
  );

  // Modo avión: el Inicio no está guardado, pero «Tus favoritos» sí; y el contrato y la guía, con su aviso.
  const sinRed = (ruta: Route) => ruta.abort('internetdisconnected');
  await context.setOffline(true);
  await page.route('**/api/**', sinRed);
  await page.goto('/portal');
  const favoritos = page.getByRole('list', { name: 'Favoritos' });
  await expect(favoritos.getByRole('listitem')).toHaveCount(2);
  await favoritos.getByRole('link', { name: /API de Trámites Demo/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'API de Trámites Demo' })).toBeVisible();
  await expect(page.getByText('Obtener un trámite guardado').first()).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: /^Sin conexión: datos del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}\.$/ }).first()).toBeVisible();
  if (!esMovil(info)) {
    // Scalar corre en un iframe de origen opaco que el service worker no controla: sin red lo dice y remite a «Operaciones».
    await page.getByRole('tab', { name: 'Referencia interactiva' }).click();
    await expect(page.getByRole('note').filter({ hasText: /Sin conexión, la referencia interactiva no se puede abrir/ })).toBeVisible();
  }
  await page.goto('/portal/paquetes/p1/guias/g1');
  await expect(page.getByRole('heading', { name: 'Token guardado' })).toBeVisible();

  // De vuelta en línea, FLIT retiró la compartición: al sincronizar, lo guardado de esos favoritos se borra.
  await page.evaluate(async () => {
    for (const registro of await navigator.serviceWorker.getRegistrations()) await registro.unregister();
  });
  portal.revocar();
  await page.unroute('**/api/**', sinRed);
  await context.setOffline(false);
  await page.goto('/portal');
  await expect
    .poll(async () => Object.keys(await recursosGuardados(page)).filter((c) => c.startsWith('contratos.fav:/api/v1/portal/paquetes/')))
    .toEqual([]);
});
