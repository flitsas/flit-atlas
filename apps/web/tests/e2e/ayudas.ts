import { expect, type Page, test, type TestInfo } from '@playwright/test';

/** Perfiles móviles de playwright.config.ts (el resto es escritorio). */
export const esMovil = (info: TestInfo): boolean => info.project.name !== 'escritorio';

/** Ingreso con el proveedor simulado (solo local y dev), con un correo distinto por prueba y perfil. */
/** Con `administrador`, el ingreso simulado pide el grupo de administración (superadmin) que el AppHost habilita en local. */
export async function entrar(page: Page, info: TestInfo, nombre = 'Persona E2E', ruta = '/', administrador = false): Promise<string> {
  const correo = `e2e.${info.project.name}.${Date.now()}.${Math.random().toString(36).slice(2, 7)}@ejemplo.com`;
  await page.goto(ruta);
  const formulario = page.getByRole('form', { name: /simulado/ });
  await formulario.getByLabel('Correo').fill(correo);
  await formulario.getByLabel('Nombre').fill(nombre);
  if (administrador) await formulario.getByLabel(/rol de administración/).check();
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

/**
 * Registra las violaciones de la CSP que la política bloquea (si el servidor la envía: la imagen nginx o `vite preview`,
 * que sirve las mismas cabeceras). Las de `Content-Security-Policy-Report-Only` (Trusted Types, ATLAS-ADR-0048) no fallan
 * la prueba: quedan como anotación para medir qué rompería exigirlas.
 */
export async function vigilarCsp(page: Page): Promise<string[]> {
  const violaciones: string[] = [];
  page.on('console', (mensaje) => {
    const texto = mensaje.text();
    if (texto.startsWith('Content-Security-Policy (report):') || texto.includes('[Report Only]')) {
      test.info().annotations.push({ type: 'csp-informe', description: texto });
    } else if (/Content.Security.Policy/i.test(texto)) {
      violaciones.push(texto);
    }
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      const tipo = e.disposition === 'report' ? ' (report)' : '';
      console.error(`Content-Security-Policy${tipo}: ${e.violatedDirective} ${e.blockedURI} ${e.sourceFile}:${e.lineNumber}`);
    });
  });
  return violaciones;
}
