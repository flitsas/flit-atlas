import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

import { esMovil } from './ayudas';
import { simularApi } from './api-simulada';

/**
 * HU 1.4 (matriz rol × permiso visible en Atlas) y HU 1.9 (módulo de ejemplo «hola» encendido por bandera), en los tres
 * perfiles, con la API simulada (api-simulada.ts): no necesitan el AppHost. Con ATLAS_E2E_URL apuntando a la imagen
 * nginx también revisan la CSP.
 */

// El service worker de producción atiende fetch: se bloquea para que page.route vea todas las peticiones.
test.use({ serviceWorkers: 'block' });

/** Registra las violaciones de la CSP (si el servidor la envía). */
async function vigilarCsp(page: Page): Promise<string[]> {
  const violaciones: string[] = [];
  page.on('console', (mensaje) => {
    if (/Content.Security.Policy/i.test(mensaje.text())) violaciones.push(mensaje.text());
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      console.error(`Content-Security-Policy: ${e.violatedDirective} ${e.blockedURI} ${e.sourceFile}:${e.lineNumber}`);
    });
  });
  return violaciones;
}

async function sinErroresCriticosDeAxe(page: Page, pantalla: string) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  test.info().annotations.push({ type: `axe ${pantalla}`, description: resumen.join('\n') || 'sin violaciones' });
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
}

async function sinScrollHorizontal(page: Page, donde: string) {
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, `scroll horizontal en ${donde}`).toBeLessThanOrEqual(vista);
}

/** Abre una entrada de la navegación: la barra lateral en escritorio, la hoja «Más» en el móvil. */
async function abrirDesdeNavegacion(page: Page, esMovilPerfil: boolean, etiqueta: string) {
  if (esMovilPerfil) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: etiqueta, exact: true }).click();
  } else {
    await page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: etiqueta, exact: true }).click();
  }
}

test.describe('matriz rol × permiso', () => {
  test('con atlas.roles.ver: tabla en escritorio, tarjetas en el móvil, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.roles.ver'] });
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Roles y permisos');
    await expect(page).toHaveURL(/\/permisos$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Roles y permisos' })).toBeVisible();
    await expect(page).toHaveTitle('Roles y permisos · Flit Atlas');

    if (esMovil(info)) {
      // La tabla de escritorio está oculta (display: none): en el móvil solo hay tarjetas.
      await expect(page.getByRole('table')).toHaveCount(0);
      const tarjeta = page.getByRole('region', { name: 'Plataforma', exact: true });
      await expect(tarjeta.getByText('atlas.roles.ver')).toBeVisible();
      await expect(page.getByRole('list', { name: 'Roles con atlas.roles.ver' }).getByText('Auditor')).toBeVisible();
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, 'por módulo a 360 px');

      await page.getByRole('radio', { name: 'Rol', exact: true }).check();
      await page.getByRole('group', { name: 'Rol', exact: true }).getByRole('radio', { name: 'Auditor', exact: true }).check();
      await expect(page.getByRole('region', { name: 'Auditor' })).toContainText('lectura de auditoría');
      await sinScrollHorizontal(page, 'por rol a 360 px');
      await page.getByRole('radio', { name: /^Todos \(\d+\)$/ }).check();
      await page.getByRole('radio', { name: 'Administrador de la organización', exact: true }).check();
      await expect(page.getByRole('region', { name: 'Administrador de la organización' })).toBeVisible();
      await sinScrollHorizontal(page, 'por rol (todos) a 360 px');
    } else {
      const tabla = page.getByRole('table', { name: 'Permisos de Plataforma por rol' });
      await expect(tabla).toBeVisible();
      await expect(tabla.getByRole('columnheader')).toHaveCount(9);
      await expect(tabla.getByRole('row', { name: /atlas\.roles\.ver/ }).getByRole('cell')).toHaveText(['Sí', 'Sí', 'No', 'No', 'No', 'No', 'Sí', 'No']);
      // El módulo de ejemplo también está en la matriz (sus permisos existen aunque esté apagado).
      await expect(page.getByRole('table', { name: 'Permisos de Hola (ejemplo) por rol' })).toBeVisible();
      await page.getByRole('radio', { name: /^Todos \(\d+\)$/ }).check();
      await expect(tabla.getByRole('columnheader')).toHaveCount(15);
      await sinScrollHorizontal(page, 'escritorio con todos los roles');
    }

    await sinErroresCriticosDeAxe(page, '/permisos');
    expect(violaciones).toEqual([]);
  });

  test('sin atlas.roles.ver no aparece en la navegación y la ruta explica que no hay acceso', async ({ page }, info) => {
    await simularApi(page, { permisos: ['atlas.lector'] });
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
    if (esMovil(info)) {
      await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
      await expect(page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: 'Notificaciones' })).toBeVisible();
      await expect(page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: 'Roles y permisos' })).toHaveCount(0);
      await page.keyboard.press('Escape');
    } else {
      await expect(page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: 'Roles y permisos' })).toHaveCount(0);
    }

    await page.goto('/permisos');
    await expect(page.getByRole('heading', { level: 1, name: 'No tienes acceso' })).toBeVisible();
  });
});

test.describe('módulo de ejemplo «hola»', () => {
  test('apagado por defecto: no está en la navegación y su ruta no existe', async ({ page }) => {
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.hola.ver'] });
    await page.goto('/hola');

    await expect(page.getByRole('heading', { level: 1, name: 'Esta página no existe' })).toBeVisible();
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Último saludo' })).toHaveCount(0);
  });

  test('encendido por bandera: widget en Inicio, página en la navegación y publicar un saludo', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, {
      permisos: ['atlas.lector', 'atlas.hola.ver', 'atlas.hola.gestionar'],
      roles: ['atlas.desarrollador'],
      banderas: { 'modulo.hola': true },
    });
    await page.goto('/');

    const widget = page.getByRole('region', { name: 'Último saludo' });
    await expect(widget.getByText('«Hola desde Organismo Demo»')).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Hola');
    await expect(page).toHaveURL(/\/hola$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Hola' })).toBeVisible();
    const formulario = page.getByRole('form', { name: 'Escribir un saludo' });
    await formulario.getByLabel('Tu saludo').fill('Buenos días desde la prueba');
    await formulario.getByRole('button', { name: 'Publicar saludo' }).click();
    await expect(page.getByRole('region', { name: 'Saludos de tu organización' }).getByText('Buenos días desde la prueba')).toBeVisible();
    await expect(formulario.getByLabel('Tu saludo')).toHaveValue('');

    if (esMovil(info)) {
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, '/hola a 360 px');
    }
    await sinErroresCriticosDeAxe(page, '/hola');
    expect(violaciones).toEqual([]);
  });
});
