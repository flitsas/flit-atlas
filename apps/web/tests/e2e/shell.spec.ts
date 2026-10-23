import { expect, type Page, test } from '@playwright/test';

import { cerrarSesion, entrar, esMovil } from './ayudas';

/** CA de la HU 1.6 en los tres perfiles: barra correcta según el ancho, navegación, tema, búsqueda y cierre. */

const barraInferior = (page: Page) => page.getByRole('navigation', { name: 'Barra inferior' });
const lateral = (page: Page) => page.getByRole('navigation', { name: 'Principal' });

test('entra y ve la barra que corresponde al ancho, con la navegación completa', async ({ page }, info) => {
  await entrar(page, info, 'Ana Demo');
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Ana' })).toBeVisible();

  if (esMovil(info)) {
    await expect(barraInferior(page)).toBeVisible();
    await expect(lateral(page)).toBeHidden();
    await expect(barraInferior(page).getByRole('link')).toHaveText(['Inicio', 'Buscar', 'Soporte', 'Asistente']);
    await barraInferior(page).getByRole('button', { name: 'Más' }).click();
    const mas = page.getByRole('dialog', { name: 'Más' });
    for (const nombre of ['Catálogo', 'Equipo', 'Contratos', 'Documentación', 'Notificaciones', 'Ajustes']) {
      await expect(mas.getByRole('link', { name: new RegExp(nombre) })).toBeVisible();
    }
    await expect(mas.getByText('Ana Demo')).toBeVisible();
  } else {
    await expect(lateral(page)).toBeVisible();
    await expect(barraInferior(page)).toBeHidden();
    for (const nombre of ['Inicio', 'Catálogo', 'Equipo', 'Soporte', 'Contratos', 'Documentación', 'Asistente']) {
      await expect(lateral(page).getByRole('link', { name: new RegExp(nombre) })).toBeVisible();
    }
    await page.getByRole('button', { name: 'Cuenta de Ana Demo' }).click();
    await expect(page.getByRole('menu')).toContainText('Ana Demo');
    await page.keyboard.press('Escape');
  }
});

test('navega a un módulo y ve su página «Próximamente»', async ({ page }, info) => {
  await entrar(page, info);
  const navegacion = esMovil(info) ? barraInferior(page) : lateral(page);
  await navegacion.getByRole('link', { name: /Soporte/ }).click();

  await expect(page).toHaveURL(/\/soporte$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Soporte de los sábados' })).toBeVisible();
  await expect(page.getByText('Próximamente · fase F2')).toBeVisible();
  await expect(navegacion.getByRole('link', { name: /Soporte/ })).toHaveAttribute('aria-current', 'page');
  await expect(page).toHaveTitle('Soporte · Flit Atlas');
});

test('cambia al tema oscuro y lo conserva al recargar', async ({ page }, info) => {
  await entrar(page, info);
  await expect(page.locator('html')).not.toHaveClass(/dark/);

  if (esMovil(info)) {
    await barraInferior(page).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByText('Oscuro', { exact: true }).click();
    await page.keyboard.press('Escape');
  } else {
    await page.getByRole('button', { name: 'Usar el tema oscuro' }).click();
  }

  await expect(page.locator('html')).toHaveClass(/dark/);
  await page.reload();
  await expect(page.locator('html')).toHaveClass(/dark/);
  // Fondo del tema oscuro de FLIT (--background, oklch 0.129); WebKit redondea distinto los decimales.
  await expect(page.locator('body')).toHaveCSS('background-color', /^oklch\(0\.129 0\.042 264\.69/);
});

test('la búsqueda global lleva a un módulo (⌘K en escritorio, Buscar en el móvil)', async ({ page }, info) => {
  await entrar(page, info);
  if (esMovil(info)) {
    await barraInferior(page).getByRole('link', { name: 'Buscar' }).click();
  } else {
    await page.keyboard.press('Control+k');
    await expect(page.getByRole('dialog', { name: 'Buscar en Atlas' })).toBeVisible();
  }
  const campo = page.getByRole('combobox', { name: 'Buscar en Atlas' });
  await campo.fill('contratos');
  await expect(page.getByRole('option', { name: /Contratos de integración/ })).toBeVisible();
  await campo.press('Enter');

  await expect(page).toHaveURL(/\/contratos$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Contratos de integración' })).toBeVisible();
});

test('la notificación de prueba llega al centro de notificaciones', async ({ page }, info) => {
  await entrar(page, info);
  await page.goto('/ajustes');
  await page.getByRole('button', { name: 'Enviar una de prueba' }).click();
  await expect(page.getByRole('status').filter({ hasText: /notificación de prueba/i })).toBeVisible();

  await page.getByRole('link', { name: /^Notificaciones, 1 sin leer$/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Notificaciones' })).toBeVisible();
  await expect(page.getByText('1 sin leer')).toBeVisible();
  await page.getByRole('button', { name: 'Marcar todas como leídas' }).click();
  await expect(page.getByText('0 sin leer')).toBeVisible();
});

test('sin scroll horizontal a 360 px y objetivos táctiles de al menos 44 px', async ({ page }, info) => {
  test.skip(!esMovil(info), 'Criterio del móvil');
  await page.setViewportSize({ width: 360, height: 740 });
  await entrar(page, info, 'Persona Con Un Nombre Bastante Largo Para Probar');

  for (const ruta of ['/', '/buscar', '/soporte', '/catalogo', '/notificaciones', '/ajustes', '/no-existe']) {
    await page.goto(ruta);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    const ancho = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(ancho, `scroll horizontal en ${ruta}`).toBeLessThanOrEqual(360);
  }

  await page.goto('/');
  const objetivos = barraInferior(page).locator('a, button');
  for (let i = 0; i < (await objetivos.count()); i++) {
    const caja = await objetivos.nth(i).boundingBox();
    expect(caja!.height).toBeGreaterThanOrEqual(44);
    expect(caja!.width).toBeGreaterThanOrEqual(44);
  }
});

test('cierra sesión y vuelve al ingreso', async ({ page }, info) => {
  await entrar(page, info);
  await cerrarSesion(page, info);
  await page.goto('/soporte');
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
});
