import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';

import { esMovil, vigilarCsp } from './ayudas';
import { simularApi } from './api-simulada';

/**
 * HU 1.4 (matriz rol × permiso visible en Atlas) y HU 1.9 (módulo de ejemplo «hola» encendido por bandera), en los tres
 * perfiles, con la API simulada (api-simulada.ts): no necesitan el AppHost. Con ATLAS_E2E_URL apuntando a la imagen
 * nginx también revisan la CSP.
 */

// El service worker de producción atiende fetch: se bloquea para que page.route vea todas las peticiones.
test.use({ serviceWorkers: 'block' });

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

test.describe('contratos de integración (HU 3.2)', () => {
  test('lista de contratos y versiones: accesible, sin scroll horizontal a 360 px y sin violar la CSP', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    await page.goto('/');
    await expect(page.getByRole('region', { name: 'Contratos publicados' }).getByText('API Externa Demo de Trámites con un nombre largo')).toBeVisible();

    await abrirDesdeNavegacion(page, esMovil(info), 'Contratos');
    await expect(page).toHaveURL(/\/contratos$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Contratos de integración' })).toBeVisible();
    await expect(page.getByText('2 contratos')).toBeVisible();
    await page.getByText('API Externa Demo de Trámites con un nombre largo').click();
    await expect(page.getByRole('list', { name: 'Versiones' }).getByRole('listitem')).toHaveCount(2);
    if (esMovil(info)) {
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, '/contratos a 360 px');
    }
    await sinErroresCriticosDeAxe(page, '/contratos');

    await page.getByRole('group', { name: 'Tipo' }).getByText('AsyncAPI', { exact: true }).click();
    await expect(page).toHaveURL(/tipo=asyncapi/);
    await expect(page.getByText('1 contrato', { exact: true })).toBeVisible();
    expect(violaciones).toEqual([]);
  });

  test('guía (HU 3.7): Markdown sin HTML, editor con vista previa e historial con comparación; accesible, a 360 px y sin violar la CSP', async ({
    page,
  }, info) => {
    const violaciones = await vigilarCsp(page);
    const dialogos: string[] = [];
    page.on('dialog', (d) => {
      dialogos.push(d.message());
      void d.dismiss();
    });
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos', 'atlas.contratos.guias.editar'] });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/contratos/c1/guias/g1');

    const contenido = page.getByRole('article', { name: /^Contenido de Cómo autenticarse/ });
    await expect(contenido.getByRole('heading', { name: 'Autenticación' })).toBeVisible();
    // El HTML del autor se ve como texto y el enlace javascript: queda sin href.
    await expect(contenido.getByText('<script>alert(1)</script>')).toBeVisible();
    await expect(contenido.locator('script, a[href^="javascript"]')).toHaveCount(0);
    await expect(page.getByText(/Pendiente de aprobación/)).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, 'guía a 360 px');
    await sinErroresCriticosDeAxe(page, 'guía');

    await page.getByRole('button', { name: 'Historial' }).click();
    await page.getByRole('button', { name: 'Comparar con la 1' }).click();
    await expect(page.getByRole('list', { name: 'Cambios línea a línea' })).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, 'comparación a 360 px');
    await sinErroresCriticosDeAxe(page, 'historial de la guía');

    await page.getByRole('button', { name: 'Leer' }).click();
    await page.getByRole('button', { name: 'Editar' }).click();
    const editor = page.getByRole('form', { name: 'Editar la guía' });
    await expect(editor.getByLabel(/Cuerpo en Markdown/)).toBeVisible();
    if (esMovil(info)) await editor.getByRole('button', { name: 'Vista previa' }).click();
    await expect(editor.getByRole('article', { name: 'Vista previa de la guía' }).getByRole('heading', { name: 'Autenticación' })).toBeVisible();
    if (esMovil(info)) await sinScrollHorizontal(page, 'editor a 360 px');
    await sinErroresCriticosDeAxe(page, 'editor de la guía');
    expect(dialogos).toEqual([]);
    expect(violaciones).toEqual([]);
  });
});

test.describe('consolidado de contratos (HU 3.4)', () => {
  test('versión por ambiente, desfases y breaking: tarjetas en el móvil, tabla en escritorio, sin scroll horizontal a 360 px', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    await page.goto('/');

    await abrirDesdeNavegacion(page, esMovil(info), 'Consolidado');
    await expect(page).toHaveURL(/\/contratos\/consolidado$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Consolidado de contratos' })).toBeVisible();
    const vista = esMovil(info)
      ? page.getByRole('list', { name: 'Contratos por ambiente' })
      : page.getByRole('region', { name: 'Tabla del consolidado' });
    await expect(vista).toBeVisible();
    await expect(vista.getByText('qa (1.3.0-rc.a1b2c3d) va adelante de pdn (1.2.0)')).toBeVisible();
    await vista.getByText(/2 cambios rompen compatibilidad/).click();
    await expect(vista.getByText('api-removed-without-deprecation')).toBeVisible();
    if (esMovil(info)) {
      await page.setViewportSize({ width: 360, height: 740 });
      await sinScrollHorizontal(page, '/contratos/consolidado a 360 px');
    }
    await sinErroresCriticosDeAxe(page, '/contratos/consolidado');

    await page.getByText('Solo con desfases').click();
    await expect(page).toHaveURL(/desfases=true/);
    await expect(page.getByRole('article', { name: 'Eventos Demo' })).toHaveCount(0);
    expect(violaciones).toEqual([]);
  });
});

test.describe('pantalla del contrato (HU 3.6 y 3.9)', () => {
  test('operaciones, obligatoriedad, tablas, diff y canales del contrato de ICT: hoja inferior en el móvil, accesible y sin scroll a 360 px', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    const movil = esMovil(info);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/contratos/c3');

    await expect(page.getByRole('heading', { level: 1, name: /API de ICT/ })).toBeVisible();
    const operaciones = page.getByRole('navigation', { name: 'Operaciones' });
    await expect(operaciones.getByRole('button').first()).toBeVisible();
    await page.getByRole('searchbox', { name: 'Buscar operaciones' }).fill('external-transaction/register');
    await operaciones.getByRole('button', { name: /\/api\/v1\/external-transaction\/register/ }).first().click();
    // En el móvil, la ficha va en una hoja inferior; en escritorio, al lado de la lista.
    const ficha = movil ? page.getByRole('dialog', { name: /POST \/api\/v1\/external-transaction\/register/ }) : page.locator('article').first();
    await expect(ficha.getByRole('heading', { name: 'Errores' })).toBeVisible();
    await expect(ficha.getByText(/curl --request POST/)).toContainText('{{baseUrl}}');
    await ficha.getByRole('tab', { name: 'Go' }).click();
    await expect(ficha.getByText(/package main/)).toBeVisible();
    if (movil) await sinScrollHorizontal(page, 'ficha de la operación a 360 px');
    await sinErroresCriticosDeAxe(page, 'operación del contrato');
    if (movil) await page.keyboard.press('Escape');

    await page.getByRole('tab', { name: 'Obligatoriedad' }).click();
    const matriz = page.getByRole('region', { name: 'Matriz de obligatoriedad de SolicitudRegistroTramite' });
    await matriz.getByRole('searchbox', { name: 'Buscar campo' }).fill('vin');
    if (movil) {
      // En el móvil, por tipo de trámite como lista.
      await matriz.getByText('Matricula inicial (1)', { exact: true }).click();
      await expect(matriz.getByRole('region', { name: 'Obligatorio en Matricula inicial' }).getByText('vin', { exact: true })).toBeVisible();
      await sinScrollHorizontal(page, 'matriz de obligatoriedad a 360 px');
    } else {
      await expect(matriz.getByRole('table').getByRole('columnheader', { name: 'Matricula tipo leasing' })).toBeVisible();
    }
    await sinErroresCriticosDeAxe(page, 'matriz de obligatoriedad');

    await page.getByRole('tab', { name: /Tablas de referencia/ }).click();
    await page.getByRole('searchbox', { name: 'Buscar en las tablas de referencia' }).fill('pasaporte');
    await expect(page.locator('summary').getByText('Tabla Tipo Documento', { exact: true })).toBeVisible();
    if (movil) await sinScrollHorizontal(page, 'tablas de referencia a 360 px');
    await sinErroresCriticosDeAxe(page, 'tablas de referencia');

    await page.getByRole('tab', { name: /Versiones/ }).click();
    await expect(page.getByRole('region', { name: 'Rompen compatibilidad' }).getByText(/Se quitó la ruta/)).toBeVisible();
    await expect(page.getByText('Breaking: sí')).toBeVisible();
    if (movil) await sinScrollHorizontal(page, 'versiones y diff a 360 px');
    await sinErroresCriticosDeAxe(page, 'versiones y diff');

    // Canales (HU 3.4): versión por ambiente e historial, apilados en el móvil.
    await page.getByRole('tab', { name: 'Canales' }).click();
    await expect(page.getByRole('list', { name: 'Versión por ambiente' }).getByRole('listitem', { name: 'dev' })).toContainText('Sin versión desplegada');
    await expect(page.getByRole('list', { name: 'Historial de canales' }).getByRole('listitem')).toHaveCount(2);
    if (movil) await sinScrollHorizontal(page, 'canales a 360 px');
    await sinErroresCriticosDeAxe(page, 'canales');
    expect(violaciones).toEqual([]);
  });

  test('referencia interactiva con Scalar en un iframe aislado, solo en escritorio y solo hacia QA', async ({ page }, info) => {
    test.skip(esMovil(info), 'En el móvil se usa la vista propia de operaciones; Scalar es solo de escritorio.');
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    await page.goto('/contratos/c3?pestana=referencia');

    const marco = page.locator('iframe[title^="Referencia interactiva"]');
    await expect(marco).toHaveAttribute('sandbox', 'allow-scripts');
    const visor = page.frameLocator('iframe[title^="Referencia interactiva"]');
    await expect(visor.getByRole('heading', { name: /ICT/ }).first()).toBeVisible({ timeout: 20_000 });
    await expect(visor.getByText('https://ict.qa.flitsas.com').first()).toBeVisible();
    await expect(page.getByText(/«Probar» solo llama a https:\/\/ict\.qa\.flitsas\.com/)).toBeVisible();
    // El iframe no tiene el origen de Atlas: no puede leer su cookie ni su DOM.
    const origen = await page.frames().find((f) => f.url().includes('/visor/'))!.evaluate(() => window.origin);
    expect(origen).toBe('null');
    await sinErroresCriticosDeAxe(page, 'referencia interactiva');
    expect(violaciones).toEqual([]);
  });

  test('en el tema oscuro, la matriz, las tablas y el diff pasan axe (contraste incluido)', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    for (const pestana of ['obligatoriedad', 'tablas', 'versiones', 'canales']) {
      await page.goto(`/contratos/c3?pestana=${pestana}`);
      await expect(page.getByRole('tab', { selected: true })).toBeVisible();
      await expect(page.getByRole('status', { name: /Cargando/ })).toHaveCount(0);
      await sinErroresCriticosDeAxe(page, `contrato en oscuro (${pestana})`);
    }
    await page.goto('/contratos/c3?op=POST%20%2Fapi%2Fv1%2Fict%2Freprocess%2F%7Bid%7D');
    await expect(page.getByRole('heading', { name: 'Errores' })).toBeVisible();
    await sinErroresCriticosDeAxe(page, 'operación en oscuro');
  });

  test('AsyncAPI: canales, operaciones y mensajes legibles, sin scroll a 360 px', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos'] });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/contratos/c2');

    await expect(page.getByRole('heading', { level: 1, name: 'Eventos Demo' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'TramiteRadicado' })).toBeVisible();
    await expect(page.getByText('tramites.radicado.con.una.direccion.de.canal.larga').first()).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Referencia interactiva' })).toHaveCount(0);
    if (esMovil(info)) await sinScrollHorizontal(page, 'AsyncAPI a 360 px');
    await sinErroresCriticosDeAxe(page, 'AsyncAPI');
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
    await expect(page.getByLabel('Hora de envío')).toHaveValue('07:00');
    // HU 2.11: el estado de los recordatorios con sus canales y la última corrida.
    await expect(page.getByRole('list', { name: 'Canales de los recordatorios' })).toContainText('Teams: sin configurar');
    await expect(page.getByRole('list', { name: 'Últimas corridas' })).toContainText('6 enviados, 1 fallidos, 2 omitidos');
    await expect(page.getByRole('button', { name: 'Enviar ahora' })).toBeEnabled();
    await sinScrollHorizontal(page, '/soporte/gestion');
    await sinErroresCriticosDeAxe(page, '/soporte/gestion');
    expect(violaciones).toEqual([]);
  });

  test('importador del Excel: vista previa con el informe, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, {
      permisos: ['atlas.lector', 'atlas.soporte.ver', 'atlas.soporte.gestionar', 'atlas.soporte.aprobar'],
      roles: ['atlas.lider_tecnico'],
    });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/soporte/importar');

    await expect(page.getByRole('heading', { level: 1, name: 'Importar el Excel de la rotación' })).toBeVisible();
    await page.getByLabel(/Archivo .xlsx/).setInputFiles({
      name: 'rotacion-ficticia.xlsx',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      buffer: Buffer.from('PK ficticio'),
    });
    await page.getByRole('button', { name: 'Ver vista previa' }).click();
    const previa = page.getByRole('region', { name: 'Vista previa de la importación' });
    await expect(previa.getByText('Fecha serial de Excel 46200 → 27/06/2026.')).toBeVisible();
    await expect(previa.getByRole('checkbox', { name: /Crear a Persona Ficticia como persona inactiva/ })).toBeVisible();
    await expect(previa.getByRole('button', { name: 'Importar 1 fila' })).toBeVisible();
    await sinScrollHorizontal(page, '/soporte/importar');
    await sinErroresCriticosDeAxe(page, '/soporte/importar');
    expect(violaciones).toEqual([]);
  });

  test('cambios de turno y compensaciones: por aprobar, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, {
      permisos: ['atlas.lector', 'atlas.soporte.ver', 'atlas.soporte.solicitar_cambio', 'atlas.soporte.aprobar'],
      roles: ['atlas.lider_tecnico'],
    });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/soporte/cambios');

    await expect(page.getByRole('heading', { level: 1, name: 'Cambios de turno y compensaciones' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Aprobar el cambio' })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Historial' })).toContainText('Aceptó el reemplazo');
    await expect(page.getByRole('button', { name: 'Marcar tomada' })).toBeVisible();
    await sinScrollHorizontal(page, '/soporte/cambios');
    await sinErroresCriticosDeAxe(page, '/soporte/cambios');
    expect(violaciones).toEqual([]);
  });

  test('Outlook, Teams y feeds .ics: apagado con su motivo, accesible y sin scroll horizontal', async ({ page }, info) => {
    const violaciones = await vigilarCsp(page);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.soporte.ver'], roles: ['atlas.desarrollador'] });
    if (esMovil(info)) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/soporte/microsoft-365');

    await expect(page.getByRole('heading', { level: 1, name: 'Outlook, Teams y calendarios' })).toBeVisible();
    await expect(page.getByRole('region', { name: /Calendario compartido de Outlook/ })).toContainText('Apagado en este ambiente');
    await expect(page.getByRole('list', { name: 'Mis feeds' })).toContainText('Equipo Operaciones');
    await page.getByRole('button', { name: 'Feed de mis turnos' }).click();
    await expect(page.getByLabel('Dirección del feed')).toHaveValue(/\/api\/v1\/soporte\/ics\/.+\.ics$/);
    await sinScrollHorizontal(page, '/soporte/microsoft-365');
    await sinErroresCriticosDeAxe(page, '/soporte/microsoft-365');
    // WebKit de Playwright informa un estilo en línea al crear el <input readOnly> del feed (la pila es el createElement
    // de React; la PWA no pone estilos en línea): se anota y queda [por verificar] en Safari real (ATLAS-ADR-0048).
    const propias = info.project.name === 'iphone-14'
      ? violaciones.filter((v) => !/style-src-attr inline|Refused to apply a stylesheet/.test(v))
      : violaciones;
    if (propias.length !== violaciones.length) test.info().annotations.push({ type: 'csp-webkit', description: violaciones.join(' | ') });
    expect(propias).toEqual([]);
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

test.describe('paquetes de contratos (HU 4.1)', () => {
  test('asistente de 3 pasos, guardar, publicar y fijar una anterior: hoja inferior en el móvil, accesible, sin scroll a 360 px y sin violar la CSP', async ({
    page,
  }, info) => {
    const violaciones = await vigilarCsp(page);
    const movil = esMovil(info);
    await simularApi(page, { permisos: ['atlas.lector', 'atlas.contratos.ver_internos', 'atlas.paquetes.gestionar'] });
    if (movil) await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/');

    await abrirDesdeNavegacion(page, movil, 'Paquetes');
    await expect(page).toHaveURL(/\/contratos\/paquetes$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Paquetes de contratos' })).toBeVisible();
    await page.getByRole('link', { name: 'Nuevo paquete' }).click();

    // Paso 1: contrato y versión en la hoja inferior.
    await page.getByRole('checkbox', { name: /API Externa Demo/ }).check();
    await page.getByRole('button', { name: /Cambiar la versión de/ }).click();
    const hoja = page.getByRole('dialog', { name: /Versión de API Externa Demo/ });
    await expect(hoja).toBeVisible();
    if (movil) await sinScrollHorizontal(page, 'hoja de versiones a 360 px');
    await sinErroresCriticosDeAxe(page, 'hoja de versiones del paquete');
    // Ya estaba elegida: el clic igual cierra la hoja.
    await hoja.getByRole('radio', { name: /1\.3\.0/ }).click();
    await expect(hoja).toBeHidden();
    if (movil) await sinScrollHorizontal(page, 'paso 1 a 360 px');
    await sinErroresCriticosDeAxe(page, 'paso 1 del asistente');

    // Paso 2: solo x-public y seleccionar todo.
    await page.getByRole('button', { name: /Siguiente: Operaciones/ }).click();
    const operaciones = page.getByRole('list', { name: /Operaciones de API Externa Demo/ });
    await expect(operaciones.getByRole('checkbox')).toHaveCount(3);
    await page.getByRole('checkbox', { name: /Solo las públicas/ }).check();
    await expect(operaciones.getByRole('checkbox')).toHaveCount(2);
    await page.getByRole('button', { name: 'Seleccionar todo (2)' }).click();
    if (movil) await sinScrollHorizontal(page, 'paso 2 a 360 px');
    await sinErroresCriticosDeAxe(page, 'paso 2 del asistente');

    // Paso 3 y resumen.
    await page.getByRole('button', { name: /Siguiente: Guías/ }).click();
    await page.getByRole('checkbox', { name: /Cómo autenticarse/ }).check();
    await page.getByRole('button', { name: /Siguiente: Resumen/ }).click();
    await page.getByRole('textbox', { name: 'Nombre del paquete' }).fill('Paquete Integradores Demo');
    await page.getByRole('textbox', { name: 'Nota del cambio' }).fill('Primera versión.');
    if (movil) await sinScrollHorizontal(page, 'resumen a 360 px');
    await sinErroresCriticosDeAxe(page, 'resumen del asistente');
    await page.getByRole('button', { name: /Guardar revisión/ }).click();

    // Detalle: publicar la 1, guardar la 2 sin la guía, publicarla y fijar la 1.
    await expect(page).toHaveURL(/\/contratos\/paquetes\/p1$/);
    await expect(page.getByText('Paquete creado en borrador (revisión 1).')).toBeVisible();
    await page.getByRole('button', { name: 'Publicar la revisión 1' }).click();
    await expect(page.getByText('Publicada: la revisión 1 es la vigente.')).toBeVisible();
    await page.getByRole('button', { name: /Editar \(nueva revisión\)/ }).click();
    await page.getByRole('list', { name: 'Pasos del asistente' }).getByRole('button', { name: /Guías/ }).click();
    await page.getByRole('checkbox', { name: /Cómo autenticarse/ }).uncheck();
    await page.getByRole('button', { name: /Siguiente: Resumen/ }).click();
    await page.getByRole('textbox', { name: 'Nota del cambio' }).fill('Sin la guía.');
    await page.getByRole('button', { name: /Guardar revisión/ }).click();
    await expect(page.getByText(/Guardada la revisión 2/)).toBeVisible();
    await page.getByRole('button', { name: 'Publicar la revisión 2' }).click();
    await expect(page.getByText('Publicada: la revisión 2 es la vigente.')).toBeVisible();

    const historial = page.getByRole('list', { name: 'Revisiones del paquete' });
    await historial.getByRole('listitem', { name: 'Revisión 1' }).getByRole('button', { name: /Fijar esta/ }).click();
    const confirmar = page.getByRole('dialog', { name: 'Fijar la revisión 1' });
    await expect(confirmar).toBeVisible();
    await sinErroresCriticosDeAxe(page, 'confirmar la fijación');
    await confirmar.getByRole('button', { name: /Fijar la revisión 1/ }).click();
    await expect(page.getByText('Fijada: la revisión 1 es la vigente.')).toBeVisible();
    await expect(historial.getByRole('listitem', { name: 'Revisión 1' }).getByText('Vigente')).toBeVisible();
    // HU 4.3: «Compartido con» y la hoja para compartir con otra organización.
    await expect(page.getByRole('list', { name: 'Organizaciones con las que se comparte' })).toContainText('Organismo Demo de Tránsito');
    if (movil) await sinScrollHorizontal(page, 'detalle del paquete a 360 px');
    await sinErroresCriticosDeAxe(page, 'detalle del paquete');
    await page.getByRole('region', { name: 'Compartido con' }).getByRole('button', { name: /Compartir/ }).click();
    const compartir = page.getByRole('dialog', { name: 'Compartir con una organización' });
    await expect(compartir.getByRole('radio', { name: /Proveedor Demo/ })).toBeAttached();
    await sinErroresCriticosDeAxe(page, 'compartir el paquete');
    expect(violaciones).toEqual([]);
  });
});
