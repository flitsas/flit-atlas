import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type Route, test } from '@playwright/test';

import { esMovil, vigilarCsp } from './ayudas';

/**
 * HU 4.4 (ATLAS-ADR-0043): el portal de clientes de una persona externa (`ext.integrador`) en los tres perfiles, con la
 * API simulada con `page.route` (como modulos.spec.ts): sin navegación interna, Inicio del cliente, paquete, contrato
 * compartido con el spec podado y guía; accesible (axe sin errores críticos ni de contraste), sin scroll horizontal a 360
 * px y, en escritorio, Scalar en su iframe aislado. Lo que decide el servidor (organización, alcance, vigencia,
 * aislamiento) lo prueban las pruebas de .NET.
 */
test.use({ serviceWorkers: 'block' });

const json = (route: Route, cuerpo: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });

const CONTRATO = {
  contratoId: 'c1',
  codigo: 'api-tramites-demo',
  nombre: 'API de Trámites Demo para integradores con un nombre largo',
  tipo: 'openapi',
  aplicativo: { id: 'a1', slug: 'tramites-demo', nombre: 'Trámites Demo' },
  versionId: 'v1',
  version: '1.2.0',
  estadoVersion: 'deprecada',
  retiroEn: '2027-06-30',
  nivel: 'tecnico',
  operaciones: 1,
  guias: 1,
};

const PAQUETE = {
  id: 'p1',
  nombre: 'Paquete Integradores Organismo Demo',
  descripcion: 'Contratos de trámites para el equipo integrador.',
  revision: 2,
  nivel: 'tecnico',
  puedeExportar: true,
  puedeSandbox: false,
  vigenteHasta: '2027-01-31T23:59:59-05:00',
  contratos: [CONTRATO],
};

const AVISO = {
  tipo: 'deprecada',
  paqueteId: 'p1',
  paquete: PAQUETE.nombre,
  contratoId: 'c1',
  contrato: CONTRATO.nombre,
  version: '1.2.0',
  retiroEn: '2027-06-30',
  mensaje: `La versión 1.2.0 de ${CONTRATO.nombre} está deprecada y se retira el 30/06/2027.`,
};

// El spec podado: una operación, sin nada interno y con un servidor público de QA (el que «Probar» puede usar).
const SPEC_PODADO = `openapi: 3.1.0
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
      summary: Obtener un trámite
      tags: [tramites]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string }
      responses:
        '200':
          description: El trámite.
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Tramite' }
components:
  schemas:
    Tramite:
      type: object
      properties:
        id: { type: string, description: Id del trámite }
`;

async function simularPortal(page: Page) {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    switch (url.pathname) {
      case '/api/v1/identidad/proveedores':
        return json(route, { microsoft: false, simulado: true });
      case '/api/v1/plataforma/yo':
        return json(route, {
          sub: 'u1',
          personaId: 'p1',
          nombre: 'Integradora Demo',
          tipoPrincipal: 'externo',
          organizacionId: 'o1',
          proveedor: 'local',
          roles: ['ext.integrador'],
          permisos: ['atlas.asistente.usar', 'atlas.docs.ver', 'atlas.exportaciones.solicitar', 'atlas.portal.ver', 'atlas.sandbox.usar'],
        });
      case '/api/v1/plataforma/banderas':
        return json(route, {});
      case '/api/v1/plataforma/notificaciones':
        return json(route, { noLeidas: 0, elementos: [] });
      case '/api/v1/plataforma/push/clave':
        return json(route, { habilitado: false, clavePublica: null });
      case '/api/v1/portal/inicio':
        return json(route, {
          organizacion: { id: 'o1', nombre: 'Organismo Demo' },
          aplicativos: [{ aplicativo: CONTRATO.aplicativo, paquetes: ['p1'], contratos: 1, documentos: [] }],
          paquetes: [PAQUETE],
          avisos: [AVISO],
        });
      case '/api/v1/portal/paquetes/p1':
        return json(route, {
          paquete: PAQUETE,
          contratos: [
            {
              contrato: CONTRATO,
              operaciones: [{ clave: 'GET /tramites/{id}', metodo: 'GET', ruta: '/tramites/{id}', operationId: 'obtenerTramite', resumen: 'Obtener un trámite', etiquetas: ['tramites'] }],
              guias: [{ id: 'g1', titulo: 'Cómo autenticarte en la API', operacion: null, revision: 1 }],
            },
          ],
          avisos: [AVISO],
        });
      case '/api/v1/portal/paquetes/p1/contratos/c1/spec':
        return route.fulfill({ status: 200, contentType: 'application/yaml', body: SPEC_PODADO });
      case '/api/v1/portal/paquetes/p1/contratos/c1/cambios':
        return json(route, { contratoId: 'c1', version: '1.2.0', base: '1.1.0', elementos: [{ id: 'response-property-added', texto: 'Se agregó la propiedad estado.', nivel: 1, metodo: 'GET', ruta: '/tramites/{id}' }] });
      case '/api/v1/portal/paquetes/p1/guias/g1':
        return json(route, {
          id: 'g1', paqueteId: 'p1', contratoId: 'c1', titulo: 'Cómo autenticarte en la API', operacion: null, revision: 1,
          contenido: '## Token\n\nEnvía `Authorization: Bearer $TOKEN` en cada petición.\n\n<script>alert(1)</script>',
        });
      case '/api/v1/portal/buscar':
        return json(route, [{ tipo: 'operacion', titulo: 'GET /tramites/{id}', detalle: 'Obtener un trámite', paqueteId: 'p1', contratoId: 'c1', ruta: '/portal/paquetes/p1/contratos/c1' }]);
      default:
        return json(route, { codigo: 'no_encontrado', mensaje: 'No tienes este paquete compartido.' }, 404);
    }
  });
}

async function sinErroresCriticosDeAxe(page: Page, pantalla: string) {
  await page.waitForFunction(() =>
    document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity),
  );
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  test.info().annotations.push({ type: `axe ${pantalla}`, description: resumen.join('\n') || 'sin violaciones' });
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
}

async function sinScrollHorizontal(page: Page, donde: string) {
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, `scroll horizontal en ${donde}`).toBeLessThanOrEqual(vista);
}

test.describe('portal de clientes (HU 4.4)', () => {
  test('un externo: sin navegación interna, Inicio del cliente, paquete, contrato podado y guía; accesible y sin scroll a 360 px', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularPortal(page);
    const movil = esMovil(info);
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/');

    // Inicio: el widget del cliente; la navegación solo trae lo suyo.
    await expect(page.getByRole('heading', { name: 'Lo que FLIT comparte contigo' })).toBeVisible();
    const navegacion = page.getByRole('navigation', { name: movil ? 'Barra inferior' : 'Principal' });
    await expect(navegacion.getByRole('link', { name: 'Portal', exact: true })).toBeVisible();
    for (const interna of ['Catálogo', 'Equipo', 'Soporte', 'Contratos', 'Paquetes', 'Consolidado']) {
      await expect(page.getByRole('link', { name: interna, exact: true })).toHaveCount(0);
    }
    await sinErroresCriticosDeAxe(page, 'inicio del externo');

    // Portal: avisos, aplicativos con sus paquetes y la búsqueda en lo compartido.
    await navegacion.getByRole('link', { name: 'Portal', exact: true }).click();
    await expect(page).toHaveURL(/\/portal$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Lo que FLIT comparte contigo' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Avisos de deprecación' })).toContainText('30/06/2027');
    await page.getByRole('searchbox', { name: 'Buscar en lo compartido' }).fill('tramite');
    await expect(page.getByRole('list', { name: 'Resultados' }).getByRole('link', { name: /GET \/tramites/ })).toBeVisible();
    if (movil) await sinScrollHorizontal(page, '/portal a 360 px');
    await sinErroresCriticosDeAxe(page, '/portal');

    // Paquete: contratos, operaciones y guías; Exportar y Sandbox según la compartición.
    await page.getByRole('list', { name: /Paquetes de Trámites Demo/ }).getByRole('link', { name: /Paquete Integradores/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: PAQUETE.nombre })).toBeVisible();
    const acciones = page.getByRole('group', { name: 'Acciones del paquete' });
    // HU 4.5 conectó «Exportar» (la compartición lo permite: `puedeExportar`); el sandbox llega con la HU 4.7.
    await expect(acciones.getByRole('button', { name: /Exportar/ })).toBeEnabled();
    await expect(acciones.getByRole('button', { name: /Sandbox/ })).toBeDisabled();
    if (movil) await sinScrollHorizontal(page, 'paquete a 360 px');
    await sinErroresCriticosDeAxe(page, 'paquete del portal');

    // Contrato compartido: la vista de operaciones sale del spec podado.
    await page.getByRole('link', { name: CONTRATO.nombre }).click();
    await expect(page.getByRole('heading', { level: 1, name: CONTRATO.nombre })).toBeVisible();
    await expect(page.getByText('/tramites/{id}').first()).toBeVisible();
    await page.getByRole('tab', { name: 'Cambios' }).click();
    await expect(page.getByText('Se agregó la propiedad estado.')).toBeVisible();
    if (movil) await sinScrollHorizontal(page, 'contrato del portal a 360 px');
    await sinErroresCriticosDeAxe(page, 'contrato del portal');

    // Guía: Markdown sanitizado (el <script> sale como texto, no se ejecuta).
    await page.getByRole('tab', { name: /Guías/ }).click();
    await page.getByRole('link', { name: /Cómo autenticarte/ }).click();
    const guia = page.getByRole('article', { name: /Contenido de Cómo autenticarte/ });
    await expect(guia).toContainText('Bearer $TOKEN');
    await expect(guia.locator('script')).toHaveCount(0);
    if (movil) await sinScrollHorizontal(page, 'guía del portal a 360 px');
    await sinErroresCriticosDeAxe(page, 'guía del portal');

    // Un paquete que no está compartido no revela nada.
    await page.goto('/portal/paquetes/otro');
    await expect(page.getByText('No tienes este paquete compartido.')).toBeVisible();
    expect(violaciones).toEqual([]);
  });

  test('Compartir (HU 4.6): el contrato compartido solo ofrece el enlace del portal, sin Teams; a 360 px y accesible', async ({ page, baseURL }, info) => {
    await page.addInitScript(() => {
      Reflect.deleteProperty(Navigator.prototype, 'share');
      Reflect.deleteProperty(Navigator.prototype, 'canShare');
    });
    await simularPortal(page);
    const movil = esMovil(info);
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/portal/paquetes/p1/contratos/c1');
    await expect(page.getByRole('heading', { level: 1, name: CONTRATO.nombre })).toBeVisible();
    await page.getByRole('button', { name: 'Compartir', exact: true }).click();
    const hoja = page.getByRole('dialog', { name: 'Compartir' });
    await expect(hoja.getByRole('textbox', { name: 'Enlace' })).toHaveValue(`${new URL(baseURL!).origin}/portal/paquetes/p1/contratos/c1`);
    await expect(hoja.getByRole('link', { name: 'Abrir en correo' })).toBeVisible();
    await expect(hoja.getByRole('link', { name: 'Abrir en Teams' })).toHaveCount(0);
    if (movil) await sinScrollHorizontal(page, 'Compartir del portal a 360 px');
    await sinErroresCriticosDeAxe(page, 'Compartir del portal');
  });

  test('en escritorio, la referencia interactiva usa Scalar en su iframe aislado con el spec podado', async ({ page }, info) => {
    test.skip(esMovil(info), 'Scalar solo se monta en escritorio (ATLAS-ADR-0039).');
    await simularPortal(page);
    await page.goto('/portal/paquetes/p1/contratos/c1');
    await page.getByRole('tab', { name: 'Referencia interactiva' }).click();
    const marco = page.locator('iframe[title^="Referencia interactiva de"]');
    await expect(marco).toHaveAttribute('sandbox', 'allow-scripts');
    await expect(page.getByText(/«Probar» solo llama a https:\/\/qa\.api\.ejemplo\.com\/tramites/)).toBeVisible();
  });
});
