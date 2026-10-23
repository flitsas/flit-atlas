import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

import { esMovil } from './ayudas';
import { simularApi } from './api-simulada';

/**
 * HU 1.4 (matriz rol × permiso visible en Atlas) y HU 1.9 (módulo de ejemplo «hola» encendido por bandera), en los tres
 * perfiles, con la API simulada (api-simulada.ts): no necesitan el AppHost. Con ATLAS_E2E_URL apuntando a la imagen
 * nginx también revisan la CSP.
 */

// El service worker de producción atiende fetch: se bloquea para que page.route vea todas las peticiones.
test.use({ serviceWorkers: 'block' });

/** Registra las violaciones de la CSP (si el servidor la envía). */
async function vigilarCsp(page: Page): Promise<string[]> {
  const violaciones: string[] = [];
  page.on('console', (mensaje) => {
    if (/Content.Security.Policy/i.test(mensaje.text())) violaciones.push(mensaje.text());
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      console.error(`Content-Security-Policy: ${e.violatedDirective} ${e.blockedURI} ${e.sourceFile}:${e.lineNumber}`);
    });
  });
  return violaciones;
}

async function sinErroresCriticosDeAxe(page: Page, pantalla: string) {
  // A mitad de una animación (la hoja que sube con opacidad) axe mide colores más claros que los reales.
  // Las infinitas (indicadores de carga) no se esperan.
  await page.waitForFunction(() =>
    document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity),
  );
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  test.info().annotations.push({ type: `axe ${pantalla}`, description: resumen.join('\n') || 'sin violaciones' });
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
}

async function sinScrollHorizontal(page: Page, donde: string) {
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, `scroll horizontal en ${donde}`).toBeLessThanOrEqual(vista);
}

/** Abre una entrada de la navegación: la barra lateral en escritorio, la hoja «Más» en el móvil. */
async function abrirDesdeNavegacion(page: Page, esMovilPerfil: boolean, etiqueta: string) {
  if (esMovilPerfil) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: etiqueta, exact: true }).click();
  } else {
    await page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: etiqueta, exact: true }).click();
  }
}

test.describe('matriz rol × permiso', () => {
  test('con atlas.roles.ver: tabla en escritorio, tarjetas en el móvil, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.roles.ver'] });
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Roles y permisos');
    await expect(page).toHaveURL(/\/permisos$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Roles y permisos' })).toBeVisible();
    await expect(page).toHaveTitle('Roles y permisos · Flit Atlas');

    if (esMovil(info)) {
      // La tabla de escritorio está oculta (display: none): en el móvil solo hay tarjetas.
      await expect(page.getByRole('table')).toHaveCount(0);
      const tarjeta = page.getByRole('region', { name: 'Plataforma', exact: true });
      await expect(tarjeta.getByText('atlas.roles.ver')).toBeVisible();
      await expect(page.getByRole('list', { name: 'Roles con atlas.roles.ver' }).getByText('Auditor')).toBeVisible();
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, 'por módulo a 360 px');

      await page.getByRole('radio', { name: 'Rol', exact: true }).check();
      await page.getByRole('group', { name: 'Rol', exact: true }).getByRole('radio', { name: 'Auditor', exact: true }).check();
      await expect(page.getByRole('region', { name: 'Auditor' })).toContainText('lectura de auditoría');
      await sinScrollHorizontal(page, 'por rol a 360 px');
      await page.getByRole('radio', { name: /^Todos \(\d+\)$/ }).check();
      await page.getByRole('radio', { name: 'Administrador de la organización', exact: true }).check();
      await expect(page.getByRole('region', { name: 'Administrador de la organización' })).toBeVisible();
      await sinScrollHorizontal(page, 'por rol (todos) a 360 px');
    } else {
      const tabla = page.getByRole('table', { name: 'Permisos de Plataforma por rol' });
      await expect(tabla).toBeVisible();
      await expect(tabla.getByRole('columnheader')).toHaveCount(9);
      await expect(tabla.getByRole('row', { name: /atlas\.roles\.ver/ }).getByRole('cell')).toHaveText(['Sí', 'Sí', 'No', 'No', 'No', 'No', 'Sí', 'No']);
      // El módulo de ejemplo también está en la matriz (sus permisos existen aunque esté apagado).
      await expect(page.getByRole('table', { name: 'Permisos de Hola (ejemplo) por rol' })).toBeVisible();
      await page.getByRole('radio', { name: /^Todos \(\d+\)$/ }).check();
      await expect(tabla.getByRole('columnheader')).toHaveCount(15);
      await sinScrollHorizontal(page, 'escritorio con todos los roles');
    }

    await sinErroresCriticosDeAxe(page, '/permisos');
    expect(violaciones).toEqual([]);
  });

  test('sin atlas.roles.ver no aparece en la navegación y la ruta explica que no hay acceso', async ({ page }, info) => {
    await simularApi(page, { permisos: ['atlas.lector'] });
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
    if (esMovil(info)) {
      await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
      await expect(page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: 'Notificaciones' })).toBeVisible();
      await expect(page.getByRole('dialog', { name: 'Más' }).getByRole('link', { name: 'Roles y permisos' })).toHaveCount(0);
      await page.keyboard.press('Escape');
    } else {
      await expect(page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: 'Roles y permisos' })).toHaveCount(0);
    }

    await page.goto('/permisos');
    await expect(page.getByRole('heading', { level: 1, name: 'No tienes acceso' })).toBeVisible();
  });
});

test.describe('módulo de ejemplo «hola»', () => {
  test('apagado por defecto: no está en la navegación y su ruta no existe', async ({ page }) => {
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.hola.ver'] });
    await page.goto('/hola');

    await expect(page.getByRole('heading', { level: 1, name: 'Esta página no existe' })).toBeVisible();
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Último saludo' })).toHaveCount(0);
  });

  test('encendido por bandera: widget en Inicio, página en la navegación y publicar un saludo', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, {
      permisos: ['atlas.lector', 'atlas.hola.ver', 'atlas.hola.gestionar'],
      roles: ['atlas.desarrollador'],
      banderas: { 'modulo.hola': true },
    });
    await page.goto('/');

    const widget = page.getByRole('region', { name: 'Último saludo' });
    await expect(widget.getByText('«Hola desde Organismo Demo»')).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Hola');
    await expect(page).toHaveURL(/\/hola$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Hola' })).toBeVisible();
    const formulario = page.getByRole('form', { name: 'Escribir un saludo' });
    await formulario.getByLabel('Tu saludo').fill('Buenos días desde la prueba');
    await formulario.getByRole('button', { name: 'Publicar saludo' }).click();
    await expect(page.getByRole('region', { name: 'Saludos de tu organización' }).getByText('Buenos días desde la prueba')).toBeVisible();
    await expect(formulario.getByLabel('Tu saludo')).toHaveValue('');

    if (esMovil(info)) {
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, '/hola a 360 px');
    }
    await sinErroresCriticosDeAxe(page, '/hola');
    expect(violaciones).toEqual([]);
  });
});

test.describe('catálogo de aplicativos (HU 2.1)', () => {
  test('listado con filtros y ficha con pestañas: accesibles, sin scroll horizontal a 360 px y sin violar la CSP', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.catalogo.ver', 'atlas.catalogo.editar', 'atlas.catalogo.gestionar'] });
    await page.goto('/');
    await expect(page.getByRole('region', { name: 'Aplicativos de FLIT' }).getByText(/4 aplicativos en total/)).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Catálogo');
    await expect(page).toHaveURL(/\/catalogo$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Catálogo de aplicativos' })).toBeVisible();
    await expect(page.getByText('BackApiExternalTransactValiQueryExt')).toBeVisible();
    if (esMovil(info)) {
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, '/catalogo a 360 px');
    }
    await page.getByText(/^Filtros/).click();
    await page.getByRole('group', { name: 'Grupo' }).getByText('Legados', { exact: true }).click();
    await expect(page).toHaveURL(/grupo=legado/);
    await expect(page.getByText('1 aplicativo', { exact: true })).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, '/catalogo con filtros a 360 px');
    await sinErroresCriticosDeAxe(page, '/catalogo');

    await page.goto('/catalogo/flit2');
    await expect(page.getByRole('heading', { level: 1, name: 'FLIT 2.0' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Resumen' })).toHaveAttribute('aria-selected', 'true');
    // «Responsables» ya lo llena el módulo de equipo (HU 2.3): sin atlas.equipo.ver no aparece; los demás espacios siguen.
    await expect(page.getByRole('tab', { name: /Responsables/ })).toHaveCount(0);
    await page.getByRole('tab', { name: /Contratos/ }).click();
    await expect(page.getByText(/Llega con el módulo Contratos de integración/)).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, '/catalogo/flit2 a 360 px');
    await sinErroresCriticosDeAxe(page, '/catalogo/flit2');

    await page.getByRole('button', { name: 'Editar' }).click();
    await expect(page.getByRole('form', { name: 'Datos del aplicativo' })).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, 'formulario a 360 px');
    await sinErroresCriticosDeAxe(page, 'formulario del aplicativo');
    expect(violaciones).toEqual([]);
  });
});

test.describe('soporte de los sábados', () => {
  test('calendario y gestión: lista en el móvil, filtro por equipo, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, {
      permisos: ['atlas.lector', 'atlas.soporte.ver', 'atlas.soporte.gestionar', 'atlas.soporte.aprobar'],
      roles: ['atlas.lider_tecnico'],
    });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/soporte');

    await expect(page.getByRole('heading', { level: 1, name: 'Soporte de los sábados' })).toBeVisible();
    await expect(page.getByRole('radio', { name: esMovil(info) ? 'Lista' : 'Mes' })).toBeChecked();
    await expect(page.getByRole('radio', { name: 'Todos' })).toBeChecked();
    // En la lista del móvil, la tarjeta con nombre y correo largos no debe desbordar.
    if (esMovil(info)) await expect(page.getByText('Persona Demo Con Un Nombre Bastante Largo').first()).toBeVisible();
    await sinScrollHorizontal(page, '/soporte');
    await sinErroresCriticosDeAxe(page, '/soporte');

    await page.getByText('Mes', { exact: true }).click();
    await expect(page.getByRole('grid')).toBeVisible();
    await sinScrollHorizontal(page, '/soporte (mes)');

    await page.goto('/soporte/gestion');
    await expect(page.getByRole('heading', { level: 1, name: 'Gestionar el soporte' })).toBeVisible();
    await expect(page.getByRole('form', { name: 'Configuración del soporte' })).toBeVisible();
    await sinScrollHorizontal(page, '/soporte/gestion');
    await sinErroresCriticosDeAxe(page, '/soporte/gestion');
    expect(violaciones).toEqual([]);
  });
});

test.describe('responsables (HU 2.3 a 2.5)', () => {
  test('reparto, mapa de carga y «¿A quién llamo?» en la ficha: accesibles, sin scroll horizontal a 360 px y sin violar la CSP', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.catalogo.ver', 'atlas.equipo.ver'] });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/');
    await abrirDesdeNavegacion(page, esMovil(info), 'Responsables');
    await expect(page).toHaveURL(/\/equipo\/responsables$/);

    await expect(page.getByRole('heading', { level: 1, name: 'Responsables' })).toBeVisible();
    await expect(page.getByText('Revisa (o delega la revisión de) todo PR que toque su módulo.')).toBeVisible();
    await expect(page.getByRole('list', { name: 'Productos' }).getByText('Persona Demo Con Un Nombre Bastante Largo Para El Móvil')).toBeVisible();
    await expect(page.getByText('20/01/2027')).toBeVisible();
    await sinScrollHorizontal(page, '/equipo/responsables');
    await sinErroresCriticosDeAxe(page, '/equipo/responsables');

    await page.getByRole('link', { name: 'Mapa de carga' }).click();
    await expect(page.getByRole('list', { name: 'Carga por persona' })).toBeVisible();
    await sinScrollHorizontal(page, '/equipo/responsables?vista=carga');
    await sinErroresCriticosDeAxe(page, 'mapa de carga');

    await page.goto('/catalogo/flit2?pestana=responsables');
    await expect(page.getByRole('tab', { name: 'Responsables' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('button', { name: /¿A quién llamo\?/ }).click();
    await expect(page.getByRole('list', { name: 'Soporte de turno por equipo' }).getByText(/Desarrollo TI/)).toBeVisible();
    await sinScrollHorizontal(page, '¿A quién llamo? a 360 px');
    await sinErroresCriticosDeAxe(page, '¿A quién llamo?');
    expect(violaciones).toEqual([]);
  });
});
