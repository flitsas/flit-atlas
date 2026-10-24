import { expect, type Page, type Route, test } from '@playwright/test';

import { esMovil } from './ayudas';
import { matrizDelRepositorio, simularApi, TELEFONO_FICTICIO } from './api-simulada';

/**
 * HU 2.12 (ATLAS-ADR-0034), CA: en modo avión, un desarrollador ve su próximo turno y los responsables de FLITO. Con el
 * service worker de producción (precarga el shell) y la API simulada: se carga en línea, se pasa a sin conexión, se
 * recarga y se ve lo guardado con su aviso. Además, el dispositivo no guarda teléfonos y cerrar sesión lo borra todo.
 * Necesita la PWA construida (`vite preview` o la imagen nginx): en `vite dev` no hay service worker.
 */
test.use({ serviceWorkers: 'allow' });

/** Los permisos del rol atlas.desarrollador según la matriz generada desde el código. */
function permisosDeDesarrollador(): string[] {
  return matrizDelRepositorio()
    .modulos.flatMap((m) => m.permisos)
    .filter((p) => p.roles.includes('atlas.desarrollador'))
    .map((p) => p.codigo);
}

/** Recursos guardados en IndexedDB (`atlas` → `recursos`), como JSON. */
function recursosGuardados(page: Page): Promise<Record<string, string>> {
  return page.evaluate(
    () =>
      new Promise<Record<string, string>>((resolver) => {
        const pedido = indexedDB.open('atlas');
        pedido.onsuccess = () => {
          const base = pedido.result;
          if (!base.objectStoreNames.contains('recursos')) {
            base.close();
            resolver({});
            return;
          }
          const todos = base.transaction('recursos').objectStore('recursos').getAll();
          todos.onsuccess = () => {
            base.close();
            resolver(Object.fromEntries((todos.result as { clave: string; datos: unknown }[]).map((r) => [r.clave, JSON.stringify(r.datos)])));
          };
        };
        pedido.onerror = () => resolver({});
      }),
  );
}

test('en modo avión un desarrollador ve su próximo turno y los responsables de FLITO; cerrar sesión lo borra', async ({ page, context }, info) => {
  await simularApi(page, { permisos: permisosDeDesarrollador(), roles: ['atlas.desarrollador'], sinConexion: true });

  // En línea: el shell instala el service worker y guarda en segundo plano lo de cada módulo según los permisos.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await expect
    .poll(async () => Object.keys(await recursosGuardados(page)).sort())
    .toEqual(expect.arrayContaining(['catalogo.aplicativos', 'equipo.responsabilidades', 'soporte.turnos']));
  const guardados = await recursosGuardados(page);
  expect(Object.values(guardados).join('\n'), 'ningún teléfono en el dispositivo').not.toContain(TELEFONO_FICTICIO);
  expect(guardados['soporte.turnos']).toContain('Empleada Demo');
  expect(guardados['equipo.responsabilidades']).toContain('Principal De Flito Demo');

  // Modo avión: el shell sale de la precarga del service worker y los datos, de IndexedDB. En Chromium `page.route`
  // sigue respondiendo aunque el contexto esté sin red: una ruta más reciente corta la API como lo haría la red.
  const sinRed = (ruta: Route) => ruta.abort('internetdisconnected');
  await context.setOffline(true);
  await page.route('**/api/**', sinRed);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  await expect(page.getByText(/Sin conexión\. Ves lo guardado en este dispositivo/)).toBeVisible();

  await page.goto('/soporte');
  const proximo = page.getByRole('region', { name: 'Tu próximo turno' });
  await expect(proximo.getByRole('article', { name: /^Turno del sábado .*: Empleada Demo$/ })).toBeVisible();
  await expect(proximo).toContainText('Desarrollo TI');
  await expect(proximo).toContainText('08:00 a 12:00');
  await expect(page.getByRole('status').filter({ hasText: /^Sin conexión: datos del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/ })).toBeVisible();

  await page.goto('/equipo/responsables');
  const productos = page.getByRole('list', { name: 'Productos' });
  const flito = productos.getByRole('listitem').filter({ has: page.getByRole('link', { name: 'FLITO', exact: true }) });
  await expect(flito.getByText('Principal De Flito Demo')).toBeVisible();
  await expect(flito.getByText('Secundaria De Flito Demo')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: /^Sin conexión: datos del/ }).first()).toBeVisible();

  // La ficha de FLITO también: del catálogo guardado, con su pestaña de responsables armada del reparto.
  await page.goto('/catalogo/flito?pestana=responsables');
  await expect(page.getByRole('heading', { level: 1, name: 'FLITO' })).toBeVisible();
  await expect(page.getByText('Principal De Flito Demo')).toBeVisible();

  // De vuelta en línea, cerrar sesión borra todo lo guardado en el dispositivo. Sin el service worker, para que la API
  // simulada atienda todas las peticiones también en WebKit (allí `page.route` no ve las que pasan por él).
  await page.evaluate(async () => {
    for (const registro of await navigator.serviceWorker.getRegistrations()) await registro.unregister();
  });
  await page.unroute('**/api/**', sinRed);
  await context.setOffline(false);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Hola, Empleada' })).toBeVisible();
  if (esMovil(info)) {
    await page.getByRole('navigation', { name: 'Barra inferior' }).getByRole('button', { name: 'Más' }).click();
    await page.getByRole('dialog', { name: 'Más' }).getByRole('button', { name: 'Cerrar sesión' }).click();
  } else {
    await page.getByRole('button', { name: /^Cuenta de / }).click();
    await page.getByRole('menuitem', { name: 'Cerrar sesión' }).click();
  }
  await expect(page.getByRole('form', { name: /simulado/ })).toBeVisible();
  expect(await recursosGuardados(page)).toEqual({});
});
