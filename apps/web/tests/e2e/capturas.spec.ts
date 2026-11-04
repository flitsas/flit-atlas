import { join } from 'node:path';

import { expect, test } from '@playwright/test';

import { entrar, esMovil } from './ayudas';

/**
 * Capturas para la evidencia del PR (prompt maestro §1, regla 3). Solo corre si se indica la carpeta, que debe quedar
 * fuera del repo:
 *   ATLAS_CAPTURAS=<carpeta> pnpm e2e capturas
 */
const carpeta = process.env.ATLAS_CAPTURAS;

test('capturas del shell', async ({ page }, info) => {
  test.skip(!carpeta, 'Define ATLAS_CAPTURAS para guardar capturas');
  const foto = (nombre: string) => page.screenshot({ path: join(carpeta!, `${info.project.name}-${nombre}.png`), fullPage: false });

  await page.goto('/');
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
  await foto('01-ingreso');

  await entrar(page, info, 'Ana María Demo');
  await expect(page.getByRole('heading', { name: /Soporte de este sábado/ })).toBeVisible();
  await expect(page.getByText('Fase F2').first()).toBeVisible();
  await foto('02-inicio');

  if (esMovil(info)) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await expect(page.getByRole('dialog', { name: 'Más' })).toBeVisible();
    await page.waitForTimeout(300);
    await foto('03-mas');
    await page.keyboard.press('Escape');
    await page.goto('/buscar');
    await page.getByRole('combobox', { name: 'Buscar en Atlas' }).fill('so');
    await foto('04-buscar');
  } else {
    await page.keyboard.press('Control+k');
    await page.getByRole('combobox', { name: 'Buscar en Atlas' }).fill('so');
    await page.waitForTimeout(200);
    await foto('04-paleta');
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: /^Cuenta de / }).click();
    await page.waitForTimeout(200);
    await foto('03-menu-cuenta');
    await page.keyboard.press('Escape');
  }

  await page.goto('/soporte');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await foto('05-proximamente');

  await page.goto('/ajustes');
  await page.getByRole('main').getByText('Oscuro', { exact: true }).click();
  await page.getByRole('button', { name: 'Enviar una de prueba' }).click();
  await expect(page.getByRole('status').filter({ hasText: /prueba/ })).toBeVisible();
  await foto('06-ajustes-oscuro');

  await page.goto('/');
  await expect(page.getByRole('heading', { name: /Soporte de este sábado/ })).toBeVisible();
  await expect(page.getByText('Fase F2').first()).toBeVisible();
  await foto('07-inicio-oscuro');

  await page.goto('/notificaciones');
  await expect(page.getByText('Notificación de prueba')).toBeVisible();
  await foto('08-notificaciones-oscuro');
});
