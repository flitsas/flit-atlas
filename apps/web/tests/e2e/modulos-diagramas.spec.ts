import { join } from 'node:path';

import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test, type TestInfo } from '@playwright/test';

import { simularApi } from './api-simulada';
import { vigilarCsp } from './ayudas';

/**
 * HU 6.6 y 6.7 (ATLAS-ADR-0054) en los tres perfiles, con la API simulada. Contra la imagen nginx revisan además la CSP: React
 * Flow y ELK corren en el shell sin relajarla, y `bpmn-js` y Mermaid en sus iframes aislados.
 * Con ATLAS_CAPTURAS=<carpeta> guarda las capturas de la evidencia.
 */
test.use({ serviceWorkers: 'block' });

const BPMN = `<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="Def" targetNamespace="https://ejemplo.com/b">
  <bpmn:process id="Proceso"><bpmn:startEvent id="Inicio" name="Radicar" /></bpmn:process>
  <bpmndi:BPMNDiagram id="D"><bpmndi:BPMNPlane id="P" bpmnElement="Proceso">
    <bpmndi:BPMNShape id="Inicio_di" bpmnElement="Inicio"><dc:Bounds x="160" y="80" width="36" height="36" /></bpmndi:BPMNShape>
  </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
</bpmn:definitions>`;
const LIENZO = JSON.stringify({
  nodos: [
    { id: 'a', tipo: 'sistema', etiqueta: 'Radicación', x: 0, y: 0, aplicativoId: 'a-demo' },
    { id: 'b', tipo: 'contenedor', etiqueta: 'Revisión', x: 280, y: 0 },
  ],
  relaciones: [{ id: 'r', origen: 'a', destino: 'b', etiqueta: 'envía' }],
});

const FUENTES: Record<string, { tipo: string; fuente: string; titulo: string }> = {
  lienzo: { tipo: 'lienzo', fuente: LIENZO, titulo: 'Mapa de radicación' },
  mermaid: { tipo: 'mermaid', fuente: 'graph TD; Radicar-->Revisar;', titulo: 'Flujo de radicación' },
  bpmn: { tipo: 'bpmn', fuente: BPMN, titulo: 'Proceso de radicación' },
};

const resumen = (id: string) => ({
  id,
  codigo: `dgm-${id}`,
  aplicativoId: 'a-demo',
  tipo: FUENTES[id]!.tipo,
  titulo: FUENTES[id]!.titulo,
  visibilidad: 'interno_negocio',
  versionActual: 1,
  actualizadoEn: '2026-11-06T12:00:00Z',
});
const version = (id: string) => ({
  numero: 1,
  origen: 'creacion',
  restauradaDe: null,
  titulo: FUENTES[id]!.titulo,
  visibilidad: 'interno_negocio',
  nota: 'Primera versión',
  autor: { id: 'p1', nombre: 'Persona Demo' },
  guardadaEn: '2026-11-06T12:00:00Z',
  huella: `h-${id}`,
});

async function simular(page: Page, permisos: string[]) {
  await simularApi(page, { permisos: ['atlas.lector', 'atlas.docs.ver', ...permisos], banderas: { 'modulo.diagramas': true, 'modulo.documentacion': true } });
  const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  const guardados: unknown[] = [];
  await page.route('**/api/v1/catalogo/aplicativos**', (route) => route.fulfill(json({ elementos: [{ id: 'a-demo', slug: 'demo', nombre: 'Aplicativo Demo' }] })));
  await page.route('**/api/v1/diagramas**', async (route) => {
    const url = new URL(route.request().url());
    const metodo = route.request().method();
    const [, , , , id, recurso] = url.pathname.split('/');
    if (!id) return route.fulfill(json({ elementos: Object.keys(FUENTES).map(resumen), total: 3 }));
    if (!FUENTES[id]) return route.fulfill(json({ codigo: 'no_encontrado', mensaje: 'No existe.' }, 404));
    const detalle = {
      diagrama: resumen(id),
      version: { version: version(id), fuente: FUENTES[id]!.fuente },
      referencias: id === 'lienzo' ? [{ aplicativoId: 'a-demo', slug: 'demo', nombre: 'Aplicativo Demo' }] : [],
      puedeEditar: permisos.includes('atlas.diagramas.editar'),
    };
    if (metodo === 'PUT') {
      guardados.push(route.request().postDataJSON());
      return route.fulfill(json(detalle));
    }
    if (recurso === 'versiones') return route.fulfill(json([version(id)]));
    if (recurso === 'comentarios') return route.fulfill(json([]));
    return route.fulfill(json(detalle));
  });
  return guardados;
}

async function revisar(page: Page) {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity));
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumenAxe = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumenAxe.join('\n')).toEqual([]);
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, 'scroll horizontal').toBeLessThanOrEqual(vista);
}

const foto = async (page: Page, info: TestInfo, nombre: string) => {
  if (process.env.ATLAS_CAPTURAS) await page.screenshot({ path: join(process.env.ATLAS_CAPTURAS, `${info.project.name}-${nombre}.png`), fullPage: true });
};

const sinViolaciones = (violaciones: string[]) => {
  // La captura de Playwright inyecta un <style> que la CSP bloquea en WebKit (ATLAS-ADR-0053).
  if (!process.env.ATLAS_CAPTURAS) expect(violaciones).toEqual([]);
};

test('lista y lectura de un lienzo con su SVG propio, sin violar la CSP', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, []);
  await page.goto('/diagramas');
  await expect(page.getByRole('heading', { name: 'Diagramas', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: /Mapa de radicación/ })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Nuevo diagrama' })).toHaveCount(0);
  await revisar(page);
  await foto(page, info, 'diagramas-lista');

  await page.getByRole('link', { name: /Mapa de radicación/ }).click();
  await expect(page.getByRole('img', { name: 'Mapa de radicación' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Aplicativo Demo' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Editar' })).toHaveCount(0);
  await page.setViewportSize({ width: 360, height: 740 });
  await revisar(page);
  await foto(page, info, 'diagramas-lienzo-360');
  sinViolaciones(violaciones);
});

test('el lienzo se descarga como SVG y, sin atlas-render, se explica sin dañar la lectura', async ({ page }) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, []);
  let disponible = true;
  await page.route('**/api/v1/diagramas/*/versiones/*/svg', (route) =>
    route.fulfill(
      disponible
        ? { status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg"></svg>' }
        : { status: 503, contentType: 'application/json', body: JSON.stringify({ codigo: 'render_no_disponible', mensaje: 'La exportación a SVG no está disponible en este momento.' }) },
    ),
  );
  await page.goto('/diagramas/lienzo');
  const boton = page.getByRole('button', { name: 'Descargar SVG' });
  await expect(boton).toBeVisible();

  const [descarga] = await Promise.all([page.waitForEvent('download'), boton.click()]);
  expect(descarga.suggestedFilename()).toMatch(/-v\d+\.svg$/);

  disponible = false;
  await boton.click();
  await expect(page.getByText('La exportación a SVG no está disponible en este momento.')).toBeVisible();
  await expect(page.getByRole('img', { name: 'Mapa de radicación' })).toBeVisible();
  await revisar(page);
  sinViolaciones(violaciones);
});

test('el lienzo se edita con React Flow y ELK sin relajar la CSP, y guarda un JSON válido', async ({ page }, info) => {
  test.setTimeout(90_000);
  const violaciones = await vigilarCsp(page);
  const guardados = await simular(page, ['atlas.diagramas.editar']);
  await page.goto('/diagramas/lienzo/editar');
  await expect(page.locator('.react-flow__node').first()).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('.react-flow__node')).toHaveCount(2);

  // React Flow se pintó con sus estilos, que vienen de un .css estático.
  const posicion = await page.locator('.react-flow__renderer').evaluate((e) => getComputedStyle(e).position);
  expect(posicion).toBe('absolute');

  await page.getByRole('button', { name: 'Agregar nodo' }).click();
  await expect(page.locator('.react-flow__node')).toHaveCount(3);
  await page.getByLabel('Etiqueta del nodo').fill('Aprobación');
  await page.getByRole('button', { name: 'Revisión' }).last().click();
  await expect(page.locator('.react-flow__edge')).toHaveCount(2);

  await page.getByRole('button', { name: 'Acomodar' }).click();
  await expect(page.getByRole('button', { name: 'Acomodar' })).toBeEnabled({ timeout: 30_000 });
  await expect(page.getByRole('alert')).toHaveCount(0);

  await revisar(page);
  await foto(page, info, 'diagramas-editor-lienzo');

  await page.getByLabel('Nota del cambio').fill('Agrega la aprobación');
  await page.getByRole('button', { name: 'Guardar la versión' }).click();
  await expect.poll(() => guardados.length).toBe(1);
  const cuerpo = guardados[0] as { fuente: string; versionBase: number };
  expect(cuerpo.versionBase).toBe(1);
  const lienzo = JSON.parse(cuerpo.fuente) as { nodos: { etiqueta: string; aplicativoId?: string }[]; relaciones: unknown[] };
  expect(lienzo.nodos.map((n) => n.etiqueta)).toContain('Aprobación');
  expect(lienzo.nodos.find((n) => n.etiqueta === 'Radicación')?.aplicativoId).toBe('a-demo');
  expect(lienzo.relaciones).toHaveLength(2);
  sinViolaciones(violaciones);
});

test('BPMN corre aislado, con origen opaco y la marca de bpmn.io visible', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, []);
  await page.goto('/diagramas/bpmn');
  const selector = 'iframe[title="Proceso de radicación"]';
  const marco = page.frameLocator(selector);
  await expect(marco.locator('.djs-element[data-element-id="Inicio"]')).toBeVisible({ timeout: 30_000 });
  await expect(marco.getByText('Radicar')).toBeVisible();
  await expect(marco.locator('a.bjs-powered-by')).toBeVisible();
  expect(await page.locator(selector).getAttribute('sandbox')).toBe('allow-scripts');
  const origen = await page.frames().find((f) => f.url().includes('/visor/bpmn'))!.evaluate(() => window.origin);
  expect(origen).toBe('null');
  await revisar(page);
  await foto(page, info, 'diagramas-bpmn');
  sinViolaciones(violaciones);
});

test('el editor BPMN devuelve el XML al shell', async ({ page }) => {
  test.setTimeout(90_000);
  const guardados = await simular(page, ['atlas.diagramas.editar']);
  await page.goto('/diagramas/bpmn/editar');
  const marco = page.frameLocator('iframe[title="Editor del proceso BPMN"]');
  await expect(marco.locator('.djs-element[data-element-id="Inicio"]')).toBeVisible({ timeout: 30_000 });
  await expect(marco.locator('.djs-palette')).toBeVisible();
  // Mover el evento con el editor es un cambio real del modelo.
  const forma = marco.locator('.djs-element[data-element-id="Inicio"]');
  const caja = (await forma.boundingBox())!;
  await page.mouse.move(caja.x + caja.width / 2, caja.y + caja.height / 2);
  await page.mouse.down();
  await page.mouse.move(caja.x + caja.width / 2 + 60, caja.y + caja.height / 2 + 30, { steps: 8 });
  await page.mouse.up();
  await page.getByLabel('Nota del cambio').fill('Mueve el inicio');
  await page.waitForTimeout(500);
  await page.getByRole('button', { name: 'Guardar la versión' }).click();
  await expect.poll(() => guardados.length).toBe(1);
  expect((guardados[0] as { fuente: string }).fuente).toContain('bpmn:definitions');
});

test('<Diagrama/> de un documento pide el diagrama con la sesión de quien lee; sin acceso no revela nada', async ({ page }) => {
  const violaciones = await vigilarCsp(page);
  await simularApi(page, { permisos: ['atlas.lector', 'atlas.docs.ver'], banderas: { 'modulo.documentacion': true } });
  const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  await page.route('**/api/v1/diagramas/por-codigo/**', (route) =>
    route.request().url().endsWith('/visible')
      ? route.fulfill(json({ diagrama: { tipo: 'mermaid', titulo: 'Flujo' }, version: { fuente: 'graph TD; Radicar-->Revisar;', version: { titulo: 'Flujo' } } }))
      : route.fulfill(json({ codigo: 'no_encontrado' }, 404)),
  );
  const DOCUMENTO = { id: 'd1', aplicativoId: 'a-demo', origen: 'atlas', tipo: 'guia_funcional', titulo: 'Guía', visibilidad: 'interno_negocio', audiencias: [], responsable: 'Área Demo', estado: 'publicado', ruta: null, versionVigente: 1, revisionActual: 1, actualizadoEn: '2026-11-05T12:00:00Z' };
  const VERSION = { numero: 1, titulo: 'Guía', visibilidad: 'interno_negocio', esBorrador: false, sha256: 'h', repositorio: null, rama: null, commitSha: null, revisionOrigen: 1, publicadoEn: '2026-11-04T12:00:00Z' };
  await page.route('**/api/v1/documentacion/**', (route) => {
    const ruta = new URL(route.request().url()).pathname;
    if (ruta.includes('/tiempo-real')) return route.fulfill({ status: 404 });
    if (ruta.endsWith('/favoritos')) return route.fulfill(json([]));
    if (ruta.endsWith('/versiones')) return route.fulfill(json([VERSION]));
    if (ruta.endsWith('/documentos/d1'))
      return route.fulfill(json({ documento: DOCUMENTO, canales: [], version: { version: VERSION, contenido: '# Guía\n\n<Diagrama id="visible" />\n\n<Diagrama id="ajeno" />\n' }, puedeEditar: false, puedeAprobar: false }));
    return route.fulfill(json({}, 404));
  });
  await page.goto('/documentacion/d1');
  await expect(page.frameLocator('iframe[title="Diagrama del documento"]').locator('svg').first()).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('No tienes acceso a este contenido.')).toBeVisible();
  expect(violaciones).toEqual([]);
});
