import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type Route, test } from '@playwright/test';

import { simularApi } from './api-simulada';
import { esMovil } from './ayudas';

/**
 * HU 4.6 (ATLAS-ADR-0042 «Compartir»): botón Compartir en el contrato, la guía, el paquete y el resultado de la
 * exportación, en los tres perfiles, con la API simulada. La hoja nativa (`navigator.share`) se quita o se simula con un
 * script de inicio: Playwright no abre la del sistema. Se revisa que el botón aparece, que el respaldo copia el enlace
 * interno (nunca la URL firmada), que el archivo se baja de la URL firmada y se entrega a la hoja como `File`, que es
 * accesible y que no hay scroll horizontal a 360 px.
 */
test.use({ serviceWorkers: 'block' });

const PERMISOS = ['atlas.lector', 'atlas.contratos.ver_internos', 'atlas.contratos.guias.editar', 'atlas.exportaciones.solicitar'];
const FIRMADA = 'https://s3.ejemplo.com/atlas/exportaciones/e1/paquete.pdf?X-Amz-Signature=firma-ficticia';

const json = (route: Route, cuerpo: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });

const DETALLE = {
  paquete: {
    id: 'p1', nombre: 'Paquete Integradores Demo', descripcion: null, estado: 'publicado', revisionActual: 2, revisionPublicada: 2,
    cambiosSinPublicar: false, publicadaEn: null, publicadaPor: null, contratos: [], operaciones: 0, actualizadoEn: '2026-10-24T15:00:00Z', puedeGestionar: false,
  },
  revision: {
    numero: 2, nombre: 'Paquete Integradores Demo', descripcion: null, definicion: [], nota: 'Nota', autor: { id: 'p', nombre: 'Persona Demo' },
    guardadaEn: '2026-10-24T15:00:00Z', huella: 'b'.repeat(64), vigente: true,
  },
  revisiones: [],
};

const LISTA = {
  id: 'e1', estado: 'lista', origen: { tipo: 'paquete', paqueteId: 'p1', revision: 2, versionId: null }, nombre: 'Paquete Integradores Demo · revisión 2',
  formatos: ['pdf', 'zip'], solicitadaEn: '2026-10-24T15:00:00Z', iniciadaEn: null, listaEn: '2026-10-24T15:01:00Z', venceEn: '2026-10-31T15:01:00Z',
  hashContenido: 'a'.repeat(64), error: null,
  archivos: [
    { ruta: 'paquete/paquete.zip', nombre: 'paquete.zip', formato: 'zip', bytes: 2_500_000 },
    { ruta: 'paquete/paquete.pdf', nombre: 'paquete.pdf', formato: 'pdf', bytes: 8 },
  ],
};

/** La API simulada de los módulos más el paquete p1, su exportación lista y el S3 con la URL firmada. */
async function simular(page: Page) {
  await simularApi(page, { permisos: PERMISOS });
  // Las rutas que se registran después tienen prioridad.
  await page.route('**/api/v1/contratos/paquetes/p1', (route) => json(route, DETALLE));
  await page.route('**/api/v1/contratos/exportaciones/e1', (route) => json(route, LISTA));
  await page.route('**/api/v1/contratos/exportaciones/e1/descarga**', (route) => json(route, { url: FIRMADA, nombre: 'paquete.pdf', bytes: 8, venceEn: '2026-10-24T15:16:00Z' }));
  await page.route('https://s3.ejemplo.com/**', (route) =>
    route.fulfill({ status: 200, body: '%PDF-1.7', headers: { 'Content-Type': 'application/pdf', 'Access-Control-Allow-Origin': '*' } }),
  );
}

/** Sin hoja nativa (escritorio Linux, Firefox): el respaldo. El portapapeles se simula para leer lo copiado. */
async function sinHojaNativa(page: Page) {
  await page.addInitScript(() => {
    Reflect.deleteProperty(Navigator.prototype, 'share');
    Reflect.deleteProperty(Navigator.prototype, 'canShare');
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: async (texto: string) => void ((window as unknown as { __copiado: string }).__copiado = texto) },
    });
  });
}

/** Una hoja nativa que acepta enlaces y PDF (como Android o iOS) y anota lo que recibe. */
async function conHojaNativa(page: Page) {
  await page.addInitScript(() => {
    const registro: Array<{ url?: string; archivos: string[] }> = [];
    (window as unknown as { __compartido: typeof registro }).__compartido = registro;
    Object.defineProperty(Navigator.prototype, 'canShare', {
      configurable: true,
      value: (d: ShareData) => !d.files || d.files.every((f) => f.type === 'application/pdf'),
    });
    Object.defineProperty(Navigator.prototype, 'share', {
      configurable: true,
      value: async (d: ShareData) => void registro.push({ url: d.url, archivos: (d.files ?? []).map((f) => `${f.name}|${f.type}|${f.size}`) }),
    });
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

async function altoMinimo(page: Page, nombre: string | RegExp) {
  const caja = await page.getByRole('button', { name: nombre }).first().boundingBox();
  expect(caja?.height ?? 0, `alto de «${String(nombre)}»`).toBeGreaterThanOrEqual(44);
}

test.describe('compartir desde el móvil (HU 4.6)', () => {
  test('sin hoja nativa: Compartir en contrato, guía y paquete abre el respaldo y copia el enlace interno', async ({ page, baseURL }, info) => {
    const movil = esMovil(info);
    await sinHojaNativa(page);
    await simular(page);
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    const origen = new URL(baseURL!).origin;

    // Contrato: el enlace lleva la pestaña.
    await page.goto('/contratos/c3?pestana=tablas');
    await expect(page.getByRole('heading', { level: 1, name: /API de ICT/ })).toBeVisible();
    await altoMinimo(page, 'Compartir');
    await page.getByRole('button', { name: 'Compartir', exact: true }).click();
    const hoja = page.getByRole('dialog', { name: 'Compartir' });
    await expect(hoja.getByRole('textbox', { name: 'Enlace' })).toHaveValue(`${origen}/contratos/c3?pestana=tablas`);
    await hoja.getByRole('button', { name: 'Copiar enlace' }).click();
    await expect(hoja.getByRole('status')).toHaveText('Copiamos el enlace al portapapeles.');
    expect(await page.evaluate(() => (window as unknown as { __copiado: string }).__copiado)).toBe(`${origen}/contratos/c3?pestana=tablas`);
    await expect(hoja.getByRole('link', { name: 'Abrir en correo' })).toHaveAttribute('href', /^mailto:\?subject=/);
    await expect(hoja.getByRole('link', { name: 'Abrir en Teams' })).toHaveAttribute('href', /^https:\/\/teams\.microsoft\.com\/share\?href=/);
    if (movil) await sinScrollHorizontal(page, 'hoja Compartir a 360 px');
    await sinErroresCriticosDeAxe(page, 'hoja Compartir');
    await page.keyboard.press('Escape');
    await expect(hoja).toBeHidden();
    if (movil) await sinScrollHorizontal(page, 'contrato a 360 px');

    // Guía.
    await page.goto('/contratos/c1/guias/g1');
    await page.getByRole('group', { name: 'Acciones de la guía' }).getByRole('button', { name: 'Compartir' }).click();
    await expect(page.getByRole('dialog', { name: 'Compartir' }).getByRole('textbox', { name: 'Enlace' })).toHaveValue(`${origen}/contratos/c1/guias/g1`);
    await page.keyboard.press('Escape');
    if (movil) await sinScrollHorizontal(page, 'guía a 360 px');

    // Paquete: sin `?exportacion=` (es de cada persona).
    await page.goto('/contratos/paquetes/p1?exportacion=e1');
    await expect(page.getByText('Tu exportación está lista')).toBeVisible();
    const exportar = page.getByRole('dialog', { name: 'Exportar' });
    // Sin hoja nativa no se ofrece compartir el archivo, solo el enlace.
    await expect(exportar.getByText(/no se comparten archivos/)).toBeVisible();
    await exportar.getByRole('button', { name: 'Compartir enlace al paquete' }).click();
    const enlacePaquete = page.getByRole('dialog', { name: 'Compartir' }).getByRole('textbox', { name: 'Enlace' });
    await expect(enlacePaquete).toHaveValue(`${origen}/contratos/paquetes/p1`);
    if (movil) await sinScrollHorizontal(page, 'resultado de la exportación a 360 px');
    await sinErroresCriticosDeAxe(page, 'compartir el resultado de la exportación');
  });

  test('con hoja nativa: comparte el enlace en un toque y el PDF bajado de la URL firmada como archivo', async ({ page, baseURL }, info) => {
    const movil = esMovil(info);
    await conHojaNativa(page);
    await simular(page);
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    const origen = new URL(baseURL!).origin;
    const compartido = () => page.evaluate(() => (window as unknown as { __compartido: Array<{ url?: string; archivos: string[] }> }).__compartido);

    await page.goto('/contratos/paquetes/p1');
    await expect(page.getByRole('heading', { level: 1, name: 'Paquete Integradores Demo' })).toBeVisible();
    await page.getByRole('button', { name: 'Compartir', exact: true }).click();
    await expect.poll(compartido).toEqual([{ url: `${origen}/contratos/paquetes/p1`, archivos: [] }]);
    await expect(page.getByRole('dialog', { name: 'Compartir' })).toHaveCount(0);

    await page.goto('/contratos/paquetes/p1?exportacion=e1');
    const exportar = page.getByRole('dialog', { name: 'Exportar' });
    await expect(exportar.getByText('Tu exportación está lista')).toBeVisible();
    await exportar.getByRole('button', { name: 'Preparar paquete.pdf para compartir' }).click();
    const boton = exportar.getByRole('button', { name: 'Compartir paquete.pdf' });
    await expect(boton).toBeVisible();
    await altoMinimo(page, 'Compartir paquete.pdf');
    await boton.click();
    // El archivo llega completo; la URL firmada no se comparte.
    await expect.poll(compartido).toEqual([{ url: undefined, archivos: ['paquete.pdf|application/pdf|8'] }]);
    if (movil) await sinScrollHorizontal(page, 'archivo listo para compartir a 360 px');
    await sinErroresCriticosDeAxe(page, 'archivo listo para compartir');
  });
});
