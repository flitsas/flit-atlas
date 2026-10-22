import { expect, test } from '@playwright/test';

/**
 * CA de la HU 1.3: no hay tokens en localStorage ni en sessionStorage, y la cookie de sesión no es legible desde
 * JavaScript. Usa el proveedor simulado (solo local y dev).
 */
test('el ingreso deja la sesión solo en una cookie HttpOnly y el cierre la borra', async ({ page, context }, info) => {
  const correo = `e2e.${info.project.name}.${Date.now()}@ejemplo.com`;
  await page.goto('/');

  const formulario = page.getByRole('form', { name: /simulado/ });
  await formulario.getByLabel('Correo').fill(correo);
  await formulario.getByLabel('Nombre').fill('Persona E2E');
  const ingreso = page.waitForResponse((r) => r.url().endsWith('/api/v1/identidad/simulado/entrar'));
  await formulario.getByRole('button', { name: 'Entrar (simulado)' }).click();
  const setCookie = (await (await ingreso).headerValue('set-cookie')) ?? '';
  await expect(page.getByText('Hola, Persona E2E.')).toBeVisible();

  const almacenamiento = await page.evaluate(() => ({
    local: Object.keys(window.localStorage),
    sesion: Object.keys(window.sessionStorage),
    cookiesVisibles: document.cookie,
  }));
  expect(almacenamiento.local).toEqual([]);
  expect(almacenamiento.sesion).toEqual([]);
  expect(almacenamiento.cookiesVisibles).not.toContain('__Host-atlas');

  const cookie = (await context.cookies()).find((c) => c.name === '__Host-atlas');
  expect(cookie, 'cookie de sesión').toBeDefined();
  expect(cookie?.httpOnly).toBe(true);
  expect(cookie?.secure).toBe(true);
  expect(cookie?.path).toBe('/');
  // SameSite se verifica en la cabecera del servidor: el WebKit de Playwright en Windows informa «None» para toda
  // cookie, aunque la reciba con Strict.
  expect(setCookie.toLowerCase()).toContain('samesite=strict');

  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
  expect((await context.cookies()).find((c) => c.name === '__Host-atlas')).toBeUndefined();
});
