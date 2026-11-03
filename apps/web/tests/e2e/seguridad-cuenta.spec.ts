import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

/**
 * F4-SEC-31 (ATLAS-ADR-0049 §10): la seguridad de la cuenta local en escritorio, Pixel 7 e iPhone 14, con la API
 * simulada por `page.route` (no necesita el AppHost; lo del servidor lo prueban `CuentasExternasTests`). Comprueba que se
 * llega desde Ajustes, que cabe en 360 px sin scroll horizontal, que los botones y campos tienen al menos 44 px, que axe
 * no encuentra errores críticos y que la reautenticación ofrece volver a entrar.
 */
test.use({ serviceWorkers: 'block' });

const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });

async function simular(page: Page, { reautenticar = false } = {}) {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const metodo = route.request().method();
    switch (url.pathname) {
      case '/api/v1/identidad/proveedores':
        return route.fulfill(json({ microsoft: false, simulado: false }));
      case '/api/v1/plataforma/yo':
        return route.fulfill(json({
          sub: 'u1', personaId: 'p1', nombre: 'Externa Demo', tipoPrincipal: 'externo', organizacionId: 'o1',
          proveedor: 'local', roles: ['ext.integrador'], permisos: [],
        }));
      case '/api/v1/plataforma/banderas':
        return route.fulfill(json({}));
      case '/api/v1/plataforma/notificaciones':
        return route.fulfill(json({ noLeidas: 0, elementos: [] }));
      case '/api/v1/plataforma/notificaciones/preferencias':
        return route.fulfill(json([]));
      case '/api/v1/plataforma/push/clave':
        return route.fulfill(json({ habilitado: false, clavePublica: null }));
      case '/api/v1/identidad/cuenta/seguridad':
        return route.fulfill(json({ contrasena: true, totp: true, passkeys: [], codigosRecuperacionRestantes: 10 }));
      case '/api/v1/identidad/cuenta/contrasena':
        if (metodo !== 'POST' || route.request().headers()['x-atlas-csrf'] !== '1') return route.fulfill({ status: 403 });
        return reautenticar
          ? route.fulfill(json({ title: 'Vuelve a ingresar.', codigo: 'reautenticacion_requerida' }, 401))
          : route.fulfill({ status: 204 });
      default:
        return route.fulfill({ status: 404 });
    }
  });
}

async function sinErroresCriticosDeAxe(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
}

test('desde Ajustes: cambiar la contraseña, accesible y sin scroll horizontal', async ({ page }) => {
  await simular(page);
  await page.goto('/ajustes');
  await page.getByRole('link', { name: /Seguridad de la cuenta/ }).click();
  await expect(page).toHaveURL(/\/ajustes\/seguridad$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Seguridad de la cuenta' })).toBeVisible();

  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho).toBeLessThanOrEqual(vista);
  const formulario = page.getByRole('form', { name: 'Cambiar la contraseña' });
  for (const objetivo of [formulario.getByLabel('Contraseña actual'), formulario.getByRole('button', { name: 'Cambiar contraseña' }),
    page.getByRole('button', { name: 'Generar códigos nuevos' })]) {
    expect((await objetivo.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  }
  await sinErroresCriticosDeAxe(page);

  await formulario.getByLabel('Contraseña actual').fill('Rio Magdalena al amanecer 2026');
  await formulario.getByLabel(/Contraseña nueva/).fill('Nevado del Ruiz en la tarde 2026');
  await formulario.getByLabel('Repite la contraseña nueva').fill('Nevado del Ruiz en la tarde 2026');
  await formulario.getByRole('button', { name: 'Cambiar contraseña' }).click();
  await expect(formulario.getByRole('status')).toContainText('Cambiamos tu contraseña');
});

test('sin un ingreso reciente ofrece salir y volver a entrar', async ({ page }) => {
  await simular(page, { reautenticar: true });
  await page.goto('/ajustes/seguridad');
  const formulario = page.getByRole('form', { name: 'Cambiar la contraseña' });
  await formulario.getByLabel('Contraseña actual').fill('Rio Magdalena al amanecer 2026');
  await formulario.getByLabel(/Contraseña nueva/).fill('Nevado del Ruiz en la tarde 2026');
  await formulario.getByLabel('Repite la contraseña nueva').fill('Nevado del Ruiz en la tarde 2026');
  await formulario.getByRole('button', { name: 'Cambiar contraseña' }).click();
  await expect(formulario.getByRole('button', { name: 'Salir y volver a entrar' })).toBeVisible();
});
