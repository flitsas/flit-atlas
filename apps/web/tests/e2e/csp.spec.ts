import { expect, test } from '@playwright/test';

import { entrar, esMovil } from './ayudas';

/**
 * La CSP de atlas-web (deploy/atlas-seguridad.conf) no permite scripts ni estilos en línea. Esta prueba recorre el shell
 * con la imagen nginx (o el build de producción detrás de ella) y falla con cualquier violación. Contra el servidor de
 * Vite no hay CSP y se omite:
 *   ATLAS_E2E_URL=http://localhost:5032 pnpm e2e csp
 */
test('el shell no viola la Content-Security-Policy', async ({ page }, info) => {
  const respuesta = await page.goto('/');
  const csp = respuesta?.headers()['content-security-policy'];
  test.skip(!csp, 'Sin CSP: corre contra la imagen nginx (ATLAS_E2E_URL)');
  expect(csp).toContain("style-src 'self'");
  expect(csp).toContain("script-src 'self'");

  const violaciones: string[] = [];
  page.on('console', (mensaje) => {
    if (/Content.Security.Policy/i.test(mensaje.text())) violaciones.push(mensaje.text());
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      console.error(`Content-Security-Policy: ${e.violatedDirective} ${e.blockedURI} ${e.sourceFile}:${e.lineNumber}`);
    });
  });

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
