import { expect, type Page, type TestInfo } from '@playwright/test';

/** Perfiles móviles de playwright.config.ts (el resto es escritorio). */
export const esMovil = (info: TestInfo): boolean => info.project.name !== 'escritorio';

/** Ingreso con el proveedor simulado (solo local y dev), con un correo distinto por prueba y perfil. */
export async function entrar(page: Page, info: TestInfo, nombre = 'Persona E2E', ruta = '/'): Promise<string> {
  const correo = `e2e.${info.project.name}.${Date.now()}.${Math.random().toString(36).slice(2, 7)}@ejemplo.com`;
  await page.goto(ruta);
  const formulario = page.getByRole('form', { name: /simulado/ });
  await formulario.getByLabel('Correo').fill(correo);
  await formulario.getByLabel('Nombre').fill(nombre);
  await formulario.getByRole('button', { name: 'Entrar (simulado)' }).click();
  // El shell ya está: la barra de navegación que corresponde al ancho.
  await expect(page.getByRole('navigation', { name: esMovil(info) ? 'Barra inferior' : 'Principal' })).toBeVisible();
  return correo;
}

/** Cierre de sesión desde la hoja «Más» (móvil) o el menú de la cuenta (escritorio). */
export async function cerrarSesion(page: Page, info: TestInfo): Promise<void> {
  if (esMovil(info)) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByRole('button', { name: 'Cerrar sesión' }).click();
  } else {
    await page.getByRole('button', { name: /^Cuenta de / }).click();
    await page.getByRole('menuitem', { name: 'Cerrar sesión' }).click();
  }
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
}
