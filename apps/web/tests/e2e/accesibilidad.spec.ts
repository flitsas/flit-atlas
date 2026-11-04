import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

import { entrar } from './ayudas';

/**
 * CA de la HU 1.6 (prompt maestro §11): axe sin errores críticos, WCAG 2.1 AA, en claro y en oscuro. Las violaciones
 * serias se informan en el reporte para corregirlas, pero no bloquean.
 */
async function revisar(page: Page, pantalla: string) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  test.info().annotations.push({ type: `axe ${pantalla}`, description: resumen.join('\n') || 'sin violaciones' });
  expect(resultado.violations.filter((v) => v.impact === 'critical'), `${pantalla}\n${resumen.join('\n')}`).toEqual([]);
  return resultado.violations;
}

for (const tema of ['light', 'dark'] as const) {
  test(`sin errores críticos de accesibilidad (${tema === 'light' ? 'claro' : 'oscuro'})`, async ({ page }, info) => {
    await page.emulateMedia({ colorScheme: tema });
    await page.goto('/');
    await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
    const violaciones = await revisar(page, 'ingreso');

    await entrar(page, info, 'Persona Accesible');
    await expect(page.getByRole('heading', { name: /Soporte de este sábado/ })).toBeVisible();
    violaciones.push(...(await revisar(page, 'inicio')));

    for (const ruta of ['/soporte', '/buscar', '/notificaciones', '/ajustes']) {
      await page.goto(ruta);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      violaciones.push(...(await revisar(page, ruta)));
    }

    // Tampoco hay violaciones serias de contraste: el tema oscuro y el claro usan los tokens FLIT con AA.
    expect(violaciones.filter((v) => v.id === 'color-contrast').flatMap((v) => v.nodes.map((n) => n.target.join(' ')))).toEqual([]);
  });
}
