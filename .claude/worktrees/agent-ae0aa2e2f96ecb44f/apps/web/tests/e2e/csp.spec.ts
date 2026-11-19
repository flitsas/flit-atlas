import { expect, test } from '@playwright/test';

import { entrar, esMovil, vigilarCsp } from './ayudas';

/**
 * La CSP de atlas-web (deploy/atlas-seguridad.conf) no permite scripts ni estilos en línea. Esta prueba recorre el shell
 * con un ingreso real (proveedor simulado) y falla con cualquier violación que la política bloquee; las de Trusted Types
 * (solo informe) quedan como anotación. Necesita atlas-api: contra la imagen nginx o `vite preview` con la API del AppHost
 * (sin API se omite; los recorridos con la API simulada de modulos.spec.ts y portal.spec.ts también vigilan la CSP):
 *   ATLAS_E2E_URL=http://localhost:5032 pnpm e2e csp
 */
test('el shell no viola la Content-Security-Policy', async ({ page }, info) => {
  const respuesta = await page.goto('/');
  const csp = respuesta?.headers()['content-security-policy'];
  test.skip(!csp, 'Sin CSP: corre contra la imagen nginx o vite preview (ATLAS_E2E_URL)');
  const api = await page.request.get('/api/v1/identidad/proveedores').catch(() => null);
  test.skip(!api?.ok(), 'Sin atlas-api: el ingreso simulado necesita la API');
  expect(csp).toContain("style-src 'self'");
  expect(csp).toContain("script-src 'self'");

  const violaciones = await vigilarCsp(page);

  await page.reload();
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
  if (info.project.name === 'iphone-14' && page.url().startsWith('http:')) {
    // WebKit no guarda la cookie __Host- (Secure) en http: con la imagen local sin TLS solo se revisa el ingreso.
    test.info().annotations.push({ type: 'csp', description: 'WebKit en http: solo la pantalla de ingreso' });
    expect(violaciones).toEqual([]);
    return;
  }
  await entrar(page, info, 'Persona Csp');
  await expect(page.getByRole('heading', { name: /Soporte de este sábado/ })).toBeVisible();
  if (esMovil(info)) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByText('Oscuro', { exact: true }).click();
    await page.keyboard.press('Escape');
  } else {
    await page.keyboard.press('Control+k');
    await page.getByRole('combobox', { name: 'Buscar en Atlas' }).fill('equipo');
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: /^Cuenta de / }).click();
    await expect(page.getByRole('menu')).toBeVisible();
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Usar el tema oscuro' }).click();
  }
  for (const ruta of ['/soporte', '/buscar', '/notificaciones', '/ajustes', '/asistente']) {
    await page.goto(ruta);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  }
  // El service worker queda registrado (precarga del shell).
  await expect.poll(() => page.evaluate(async () => !!(await navigator.serviceWorker.getRegistration()))).toBe(true);

  expect(violaciones).toEqual([]);
});
