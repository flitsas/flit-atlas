import { join } from 'node:path';

import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test, type TestInfo } from '@playwright/test';

import { cerrarSesion, esMovil } from './ayudas';
import { autenticadorVirtual, codigoTotp, conPasskey, correoNuevo, enlaceDelCorreo, entrarComoAdministrador } from './externos';

/**
 * CA de la HU 1.5 en escritorio, Pixel 7 e iPhone 14: invitar → aceptar → registrar el factor → entrar → vencer. En
 * Chromium la passkey usa el autenticador virtual de WebAuthn (CDP); en WebKit no existe, y el flujo va con contraseña y
 * TOTP (la passkey en iOS queda como prueba manual del líder [por verificar]). El correo se lee de Mailpit.
 */
const CONTRASENA = 'Rio Magdalena al amanecer 2026';
const carpeta = process.env.ATLAS_CAPTURAS;

async function foto(page: Page, info: TestInfo, nombre: string) {
  if (carpeta) await page.screenshot({ path: join(carpeta, `${info.project.name}-hu15-${nombre}.png`) });
}

async function axe(page: Page, pantalla: string) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.length} nodo(s) — ${v.help}`);
  test.info().annotations.push({ type: `axe ${pantalla}`, description: resumen.join('\n') || 'sin violaciones' });
  expect(resultado.violations.filter((v) => v.impact === 'critical'), `${pantalla}\n${resumen.join('\n')}`).toEqual([]);
}

async function crearOrganizacionEnPantalla(page: Page, info: TestInfo): Promise<string> {
  const nombre = `Organismo Demo ${info.project.name} ${Date.now()}`;
  await page.goto('/administracion/organizaciones');
  await expect(page.getByRole('heading', { level: 1, name: 'Organizaciones' })).toBeVisible();
  await page.getByRole('button', { name: 'Nueva organización' }).click();
  const hoja = page.getByRole('dialog', { name: 'Nueva organización' });
  await hoja.getByLabel('Nombre').fill(nombre);
  await foto(page, info, '01-nueva-organizacion');
  await hoja.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByRole('status').filter({ hasText: `Se creó ${nombre}` })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Organizaciones' }).getByText(nombre)).toBeVisible();
  await axe(page, 'organizaciones');
  return nombre;
}

test('invitar → aceptar → registrar el factor → entrar → vencer', async ({ browser, page }, info) => {
  test.setTimeout(240_000);
  await entrarComoAdministrador(page, info);
  const organizacion = await crearOrganizacionEnPantalla(page, info);
  const organizacionId = await page.evaluate(async (nombre) => {
    const lista = (await (await fetch('/api/v1/plataforma/organizaciones')).json()) as { id: string; nombre: string }[];
    return lista.find((o) => o.nombre === nombre)!.id;
  }, organizacion);

  // La invitación de prueba vence en ~2 minutos (mínimo del servidor: 1); por la pantalla, el vencimiento es por día.
  const correo = correoNuevo(info);
  const vence = new Date(Date.now() + 120_000).toISOString();
  const creada = await page.request.post('/api/v1/plataforma/invitaciones', {
    headers: { 'X-Atlas-Csrf': '1' },
    data: { correo, nombre: 'Externa E2E', organizacionId, rol: 'ext.integrador', accesoVenceEn: vence, alcance: [], permisosExtra: [] },
  });
  expect(creada.status()).toBe(201);
  expect(((await creada.json()) as { correoEnviado: boolean }).correoEnviado).toBe(true);
  await page.goto('/administracion/invitaciones');
  await expect(page.getByRole('list', { name: 'Invitaciones' }).getByText(correo)).toBeVisible();
  // Sin scroll horizontal aunque el correo sea largo (§11).
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await foto(page, info, '02-invitaciones');
  await axe(page, 'invitaciones');

  // El invitado, en un navegador sin sesión, abre el enlace del correo.
  const token = await enlaceDelCorreo(page.request, correo, '/invitacion');
  // Mismo dispositivo del perfil, sin la sesión del administrador.
  const { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, baseURL, ignoreHTTPSErrors } = info.project.use;
  const invitado = await browser.newContext({ viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, baseURL, ignoreHTTPSErrors });
  const externa = await invitado.newPage();
  if (conPasskey(info)) await autenticadorVirtual(invitado, externa);
  await externa.goto(`/invitacion#${token}`);
  await expect(externa.getByRole('heading', { level: 1, name: 'Te invitaron a Flit Atlas' })).toBeVisible();
  // El token sale de la barra de direcciones apenas se lee.
  expect(new URL(externa.url()).hash).toBe('');
  await expect(externa.getByText(organizacion)).toBeVisible();
  await externa.getByLabel('Tu nombre completo').fill('Externa E2E');
  await externa.getByLabel(/Leí y acepto/).check();
  await foto(externa, info, '03-aceptar-invitacion');
  await axe(externa, 'aceptar invitación');

  let claveTotp = '';
  if (conPasskey(info)) {
    await externa.getByRole('button', { name: 'Crear mi cuenta con una passkey' }).click();
  } else {
    await externa.getByRole('radio', { name: /Contraseña y aplicación/ }).click();
    const formulario = externa.getByRole('form', { name: 'Contraseña y aplicación de autenticación' });
    await formulario.getByLabel('Contraseña (mínimo 12 caracteres)').fill(CONTRASENA);
    await formulario.getByLabel('Repite la contraseña').fill(CONTRASENA);
    await expect(externa.getByRole('img', { name: /Código QR/ })).toBeVisible();
    claveTotp = (await externa.locator('[data-clave-totp]').textContent())!.trim();
    await formulario.getByLabel(/Código de 6 dígitos/).fill(codigoTotp(claveTotp));
    await foto(externa, info, '04-contrasena-totp');
    await formulario.getByRole('button', { name: 'Crear mi cuenta' }).click();
  }

  // Códigos de recuperación: diez, una sola vez.
  await expect(externa.getByRole('heading', { name: 'Guarda tus códigos de recuperación' })).toBeVisible();
  await expect(externa.getByRole('list', { name: 'Códigos de recuperación' }).getByRole('listitem')).toHaveCount(10);
  await foto(externa, info, '05-codigos');
  await axe(externa, 'códigos de recuperación');
  await externa.getByLabel('Ya guardé mis códigos').check();
  await externa.getByRole('button', { name: 'Entrar a Atlas' }).click();
  await expect(externa.getByRole('heading', { level: 1, name: 'Hola, Externa' })).toBeVisible();
  await foto(externa, info, '06-inicio-externo');

  // Un enlace usado no sirve.
  const segunda = await invitado.newPage();
  await segunda.goto(`/invitacion#${token}`);
  await expect(segunda.getByRole('heading', { name: 'Este enlace no sirve' })).toBeVisible();
  await segunda.close();

  // Sale y vuelve a entrar con su factor.
  await cerrarSesion(externa, info);
  const ingreso = externa.getByRole('region', { name: 'Ingreso de clientes y proveedores' });
  await foto(externa, info, '07-ingreso-externo');
  if (conPasskey(info)) {
    await ingreso.getByRole('button', { name: 'Entrar con passkey' }).click();
  } else {
    const formulario = ingreso.getByRole('form', { name: 'Entrar con correo y contraseña' });
    await formulario.getByLabel('Correo').fill(correo);
    await formulario.getByLabel('Contraseña').fill(CONTRASENA);
    await formulario.getByRole('button', { name: 'Continuar' }).click();
    // Un código distinto al del registro (un código no sirve dos veces).
    const codigo = ingreso.getByRole('form', { name: 'Código de verificación' });
    await codigo.getByLabel(/Código de 6 dígitos/).fill(codigoTotp(claveTotp, Date.now() + 30_000));
    await codigo.getByRole('button', { name: 'Entrar' }).click();
  }
  await expect(externa.getByRole('heading', { level: 1, name: 'Hola, Externa' })).toBeVisible();
  // Un externo no ve la administración de organizaciones.
  await externa.goto('/administracion/organizaciones');
  await expect(externa.getByRole('heading', { name: 'No tienes acceso' })).toBeVisible();

  // Al vencer, el acceso se corta sin intervención: la sesión deja de valer y ya no puede entrar.
  await expect
    .poll(
      async () => {
        await externa.goto('/');
        const ingreso = externa.getByRole('form', { name: /simulado/ });
        // Espera a que la PWA decida (ingreso o shell) antes de mirar.
        await ingreso.or(externa.getByRole('heading', { level: 1, name: 'Hola, Externa' })).first().waitFor({ timeout: 15_000 });
        return ingreso.isVisible();
      },
      { timeout: 150_000, intervals: [5_000] },
    )
    .toBe(true);
  if (conPasskey(info)) {
    await externa.getByRole('region', { name: 'Ingreso de clientes y proveedores' }).getByRole('button', { name: 'Entrar con passkey' }).click();
  } else {
    const formulario = externa.getByRole('form', { name: 'Entrar con correo y contraseña' });
    await formulario.getByLabel('Correo').fill(correo);
    await formulario.getByLabel('Contraseña').fill(CONTRASENA);
    await formulario.getByRole('button', { name: 'Continuar' }).click();
  }
  await expect(externa.getByRole('alert')).toContainText('no está vigente');
  await foto(externa, info, '08-acceso-vencido');
  await invitado.close();
});

test('el administrador invita desde la pantalla y el correo llega con el enlace', async ({ page, request }, info) => {
  test.setTimeout(90_000);
  await entrarComoAdministrador(page, info);
  const organizacion = await crearOrganizacionEnPantalla(page, info);
  const correo = correoNuevo(info, 'pantalla');
  await page.goto('/administracion/invitaciones');
  await page.getByRole('button', { name: 'Invitar' }).click();
  const hoja = page.getByRole('dialog', { name: 'Nueva invitación' });
  await hoja.getByLabel('Correo').fill(correo);
  await hoja.getByLabel('Nombre (opcional)').fill('Persona Pantalla');
  await hoja.locator('select').nth(0).selectOption({ label: organizacion });
  await hoja.locator('select').nth(1).selectOption({ label: 'Cliente funcional' });
  await hoja.getByLabel('Exportar documentos y paquetes').check();
  await foto(page, info, '09-nueva-invitacion');
  await axe(page, 'nueva invitación');
  await hoja.getByRole('button', { name: 'Enviar invitación' }).click();
  await expect(page.getByRole('status').filter({ hasText: `Se envió la invitación a ${correo}` })).toBeVisible();
  const tarjeta = page.getByRole('listitem').filter({ hasText: correo });
  await expect(tarjeta.getByText('Pendiente')).toBeVisible();
  expect(await enlaceDelCorreo(request, correo, '/invitacion')).toMatch(/^[A-Za-z0-9_-]{43}$/);

  // Revocar desde la pantalla deja el enlace sin efecto.
  await tarjeta.getByRole('button', { name: `Revocar la invitación de ${correo}` }).click();
  await page.getByRole('dialog', { name: 'Revocar invitación' }).getByRole('button', { name: 'Revocar' }).click();
  await expect(tarjeta.getByText('Revocada')).toBeVisible();
  if (esMovil(info)) {
    // Objetivos táctiles de al menos 44 px en la tarjeta.
    for (const boton of await tarjeta.getByRole('button').all()) {
      expect((await boton.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    }
  }
});

test('recuperar la cuenta y las pantallas públicas no tienen errores críticos de accesibilidad', async ({ page }, info) => {
  test.setTimeout(60_000);
  await page.goto('/recuperar');
  await expect(page.getByRole('heading', { level: 1, name: 'Recupera tu cuenta' })).toBeVisible();
  await axe(page, 'recuperar');
  await page.getByLabel('Correo de tu cuenta').fill(correoNuevo(info, 'nadie'));
  await page.getByRole('button', { name: 'Enviarme el enlace' }).click();
  // Misma respuesta exista o no la cuenta.
  await expect(page.getByRole('status')).toContainText('Si el correo tiene una cuenta de Atlas');
  await foto(page, info, '10-recuperar');

  await page.goto('/invitacion#token-inventado-que-no-existe-0000000000');
  await expect(page.getByRole('heading', { name: 'Este enlace no sirve' })).toBeVisible();
  await axe(page, 'enlace inválido');
});
