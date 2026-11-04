import { expect, test } from '@playwright/test';

import { cerrarSesion, entrar } from './ayudas';

/**
 * CA de la HU 1.3: no hay tokens en localStorage ni en sessionStorage, y la cookie de sesión no es legible desde
 * JavaScript. Usa el proveedor simulado (solo local y dev). Desde la HU 1.6 el único dato en `localStorage` puede ser la
 * preferencia de tema (`atlas.tema`), que no es un dato de la sesión.
 */
test('el ingreso deja la sesión solo en una cookie HttpOnly y el cierre la borra', async ({ page, context }, info) => {
  const ingreso = page.waitForResponse((r) => r.url().endsWith('/api/v1/identidad/simulado/entrar'));
  await entrar(page, info);
  const setCookie = (await (await ingreso).headerValue('set-cookie')) ?? '';
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Persona' })).toBeVisible();

  const almacenamiento = await page.evaluate(() => ({
    local: Object.keys(window.localStorage).filter((k) => k !== 'atlas.tema'),
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

  await cerrarSesion(page, info);
  expect((await context.cookies()).find((c) => c.name === '__Host-atlas')).toBeUndefined();
});
