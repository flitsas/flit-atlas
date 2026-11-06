import { join } from 'node:path';

import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test, type TestInfo } from '@playwright/test';

import { simularApi } from './api-simulada';
import { vigilarCsp } from './ayudas';

/**
 * HU 5.3 y 5.4 (ATLAS-ADR-0053): lector, comparación y editor de la documentación en los tres perfiles, con la API simulada.
 * Contra la imagen nginx revisan además la CSP: el Mermaid corre en su iframe aislado y el shell no recibe estilos en línea.
 * Con ATLAS_CAPTURAS=<carpeta> guarda las capturas de la evidencia del PR.
 */
test.use({ serviceWorkers: 'block' });

const CUERPO = `# Trámites en línea

Esta guía explica **cómo radicar** un trámite. Consulta el [portal](https://ejemplo.com/portal).

## Antes de empezar

<Alerta tipo="aviso" titulo="Ten a la mano">Los datos del vehículo de ejemplo.</Alerta>

<Contrato id="external-api" operacion="POST /tramites" />

<Responsables aplicativo="flito" />

<Evil onClick="alert(1)" />

## Flujo

\`\`\`mermaid
graph TD; Radicar-->Revisar; Revisar-->Aprobar;
\`\`\`

## Por canal

<Pestanas>

## Web

Entra por la página.

## Móvil

Entra por la app.

</Pestanas>

| Campo | Largo |
|---|---|
| placa_de_ejemplo_con_un_nombre_bastante_largo_para_el_movil | 6 |
`;

const DOCUMENTO = {
  id: 'd1',
  aplicativoId: 'a-demo',
  origen: 'atlas',
  tipo: 'guia_funcional',
  titulo: 'Trámites en línea',
  visibilidad: 'interno_negocio',
  audiencias: ['negocio'],
  responsable: 'Área Demo',
  estado: 'borrador',
  ruta: null,
  versionVigente: 2,
  revisionActual: 3,
  actualizadoEn: '2026-11-05T12:00:00Z',
};
const version = (numero: number) => ({
  numero,
  titulo: 'Trámites en línea',
  visibilidad: 'interno_negocio',
  esBorrador: false,
  sha256: `h${numero}`,
  repositorio: null,
  rama: null,
  commitSha: null,
  revisionOrigen: numero,
  publicadoEn: '2026-11-04T12:00:00Z',
});
const revision = (numero: number) => ({
  numero,
  origen: 'edicion',
  restauradaDe: null,
  titulo: 'Trámites en línea',
  visibilidad: 'interno_negocio',
  nota: `Cambio ${numero}`,
  autorPersonaId: 'p1',
  guardadaEn: '2026-11-04T12:00:00Z',
  huella: `r${numero}`,
});

async function simular(page: Page, permisos: string[]) {
  await simularApi(page, { permisos: ['atlas.lector', 'atlas.docs.ver', ...permisos], banderas: { 'modulo.documentacion': true } });
  const detalle = () => ({
    documento: DOCUMENTO,
    canales: [{ ambiente: 'pdn', version: 2 }],
    version: { version: version(2), contenido: CUERPO },
    puedeEditar: permisos.includes('atlas.docs.editar'),
    puedeAprobar: false,
  });
  const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  await page.route('**/api/v1/documentacion/**', (route) => {
    const url = new URL(route.request().url());
    const resto = url.pathname.replace('/api/v1/documentacion/documentos', '');
    if (resto === '') return route.fulfill(json({ total: 1, elementos: [DOCUMENTO] }));
    if (resto === '/d1') return route.fulfill(json(detalle()));
    if (resto === '/d1/versiones') return route.fulfill(json([version(2), version(1)]));
    if (resto === '/d1/versiones/1') return route.fulfill(json({ version: version(1), contenido: '# Trámites\n\nTexto anterior.\n' }));
    if (resto === '/d1/versiones/2') return route.fulfill(json({ version: version(2), contenido: CUERPO }));
    if (resto === '/d1/revisiones') return route.fulfill(json([revision(3), revision(2)]));
    if (resto === '/d1/revisiones/3') return route.fulfill(json({ revision: revision(3), tipo: 'guia_funcional', contenido: CUERPO, audiencias: ['negocio'], responsable: 'Área Demo' }));
    return route.fulfill({ status: 404 });
  });
  // Sin permiso sobre estos dos: el documento los muestra como no accesibles.
  await page.route('**/api/v1/contratos?*', (route) => route.fulfill({ status: 403 }));
  await page.route('**/api/v1/equipo/aplicativos/flito/responsables', (route) =>
    route.fulfill(json({ aplicativo: { nombre: 'FLITO' }, principal: { persona: { nombre: 'Persona Demo' } }, secundario: null })),
  );
}

async function revisar(page: Page) {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity));
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, 'scroll horizontal').toBeLessThanOrEqual(vista);
}

const foto = async (page: Page, info: TestInfo, nombre: string) => {
  if (process.env.ATLAS_CAPTURAS) await page.screenshot({ path: join(process.env.ATLAS_CAPTURAS, `${info.project.name}-${nombre}.png`), fullPage: true });
};

test('lector: MDX por árbol, Mermaid aislado, componentes vivos con permisos, sin violar la CSP', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, []);
  await page.goto('/documentacion');
  await expect(page.getByRole('heading', { level: 1, name: 'Documentación' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Nuevo documento' })).toHaveCount(0);
  await revisar(page);
  await foto(page, info, 'documentacion-lista');

  await page.getByRole('link', { name: /Trámites en línea/ }).click();
  const articulo = page.getByRole('article', { name: 'Trámites en línea' });
  await expect(articulo.getByRole('heading', { level: 1, name: 'Trámites en línea' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Índice del documento' }).getByRole('link', { name: 'Flujo' })).toHaveAttribute('href', '#doc-flujo');
  await expect(page.getByRole('link', { name: 'Versión 2 · Producción' })).toHaveAttribute('aria-current', 'page');
  await expect(articulo.getByRole('link', { name: 'portal' })).toHaveAttribute('rel', 'noopener noreferrer nofollow');

  // Componentes vivos: sin permiso, no se revela; con permiso, el dato. Lo que no está en la lista blanca no se ejecuta.
  await expect(articulo.getByText('No tienes acceso a este contenido.')).toBeVisible();
  await expect(articulo.getByText('Principal: Persona Demo')).toBeVisible();
  await expect(articulo.getByText('Componente no permitido: Evil.')).toBeVisible();
  await expect(articulo.locator('[onclick]')).toHaveCount(0);

  // Pestañas.
  await expect(articulo.getByText('Entra por la página.')).toBeVisible();
  await articulo.getByRole('button', { name: 'Móvil' }).click();
  await expect(articulo.getByText('Entra por la app.')).toBeVisible();

  // Mermaid dibuja dentro de su iframe, con origen opaco.
  const marco = page.frameLocator('iframe[title="Diagrama del documento"]');
  await expect(marco.locator('svg').first()).toBeVisible({ timeout: 20_000 });
  await expect(marco.getByText('Radicar').first()).toBeVisible();
  const origen = await page.frames().find((f) => f.url().includes('/visor/mermaid'))!.evaluate(() => window.origin);
  expect(origen).toBe('null');
  expect(await page.locator('iframe[title="Diagrama del documento"]').getAttribute('sandbox')).toBe('allow-scripts');

  await page.setViewportSize({ width: 360, height: 740 });
  await revisar(page);
  await foto(page, info, 'documentacion-lector-360');
  await page.setViewportSize({ width: 1280, height: 800 });
  await foto(page, info, 'documentacion-lector-ancho');

  // Comparación.
  await page.setViewportSize({ width: 360, height: 740 });
  await page.getByRole('link', { name: 'Comparar versiones' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Comparar versiones' })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Cambios línea a línea' })).toContainText('Texto anterior.');
  await revisar(page);
  await foto(page, info, 'documentacion-comparar-360');

  // En WebKit, la captura de Playwright inyecta un <style> en línea que la CSP bloquea: con capturas no se mide.
  if (!process.env.ATLAS_CAPTURAS) expect(violaciones).toEqual([]);
});

test('editor: vista previa con el mismo renderizador, y restaurar pide nota', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, ['atlas.docs.editar']);
  await page.goto('/documentacion/d1');
  await expect(page.getByRole('button', { name: 'Enviar a revisión' })).toBeVisible();
  await page.getByRole('button', { name: 'Editar' }).click();

  const formulario = page.getByRole('form', { name: 'Editar el documento' });
  const cuerpo = formulario.getByLabel('Cuerpo en MDX');
  await expect(cuerpo).toHaveValue(/Trámites en línea/);
  await cuerpo.fill('# Vista previa\n\n<Alerta titulo="Nota de prueba">Hola</Alerta>\n\n<Evil />');

  if (info.project.name !== 'escritorio') await formulario.getByRole('button', { name: 'Vista previa' }).click();
  const previa = page.getByRole('article', { name: 'Vista previa del documento' });
  await expect(previa.getByRole('heading', { name: 'Vista previa' })).toBeVisible();
  await expect(previa.getByRole('complementary', { name: 'Nota de prueba' })).toBeVisible();
  await expect(previa.getByText('Componente no permitido: Evil.')).toBeVisible();
  await revisar(page);
  await foto(page, info, 'documentacion-editor');

  await expect(formulario.getByRole('button', { name: 'Guardar revisión' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Restaurar la revisión 2' })).toBeDisabled();
  await page.getByLabel('Nota para restaurar').fill('Volver a la anterior');
  await expect(page.getByRole('button', { name: 'Restaurar la revisión 2' })).toBeEnabled();

  // En WebKit, la captura de Playwright inyecta un <style> en línea que la CSP bloquea: con capturas no se mide.
  if (!process.env.ATLAS_CAPTURAS) expect(violaciones).toEqual([]);
});
