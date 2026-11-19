import { createHmac } from 'node:crypto';

import { type APIRequestContext, type BrowserContext, expect, type Page, type TestInfo } from '@playwright/test';

import { esMovil } from './ayudas';

/** API de Mailpit, el servidor SMTP de pruebas del AppHost (solo local). */
export const MAILPIT = process.env.ATLAS_E2E_MAILPIT ?? 'http://localhost:4038';

export const correoNuevo = (info: TestInfo, prefijo = 'externo') =>
  `${prefijo}.${info.project.name}.${Date.now()}.${Math.random().toString(36).slice(2, 7)}@ejemplo.com`;

/** Ingreso simulado con el grupo de administración (superadmin) que el AppHost habilita en local. */
export async function entrarComoAdministrador(page: Page, info: TestInfo): Promise<void> {
  await page.goto('/');
  const formulario = page.getByRole('form', { name: /simulado/ });
  await formulario.getByLabel('Correo').fill(correoNuevo(info, 'admin'));
  await formulario.getByLabel('Nombre').fill('Admin E2E');
  await formulario.getByLabel(/rol de administración/).check();
  await formulario.getByRole('button', { name: 'Entrar (simulado)' }).click();
  await expect(page.getByRole('navigation', { name: esMovil(info) ? 'Barra inferior' : 'Principal' })).toBeVisible();
}

/** Fragmento (token) del último enlace `ruta#…` enviado a `correo`, leído de Mailpit. */
export async function enlaceDelCorreo(request: APIRequestContext, correo: string, ruta: '/invitacion' | '/recuperar'): Promise<string> {
  let token: string | undefined;
  await expect
    .poll(
      async () => {
        const busqueda = await request.get(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:"${correo}"`)}`);
        const mensajes = ((await busqueda.json()) as { messages: { ID: string }[] }).messages;
        if (!mensajes.length) return undefined;
        const mensaje = (await (await request.get(`${MAILPIT}/api/v1/message/${mensajes[0]!.ID}`)).json()) as { Text: string };
        token = new RegExp(`${ruta}#([A-Za-z0-9_.-]+)`).exec(mensaje.Text)?.[1];
        return token;
      },
      { timeout: 20_000, message: `correo para ${correo}` },
    )
    .toBeTruthy();
  return token!;
}

/** Autenticador WebAuthn virtual de Chromium (CDP), con verificación de usuario: solo existe en Chromium. */
export async function autenticadorVirtual(context: BrowserContext, page: Page): Promise<void> {
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
}

/** Código TOTP (RFC 6238: HMAC-SHA1, 30 s, 6 dígitos) para la clave base32 que muestra Atlas. */
export function codigoTotp(claveBase32: string, momento = Date.now()): string {
  const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const limpia = claveBase32.replace(/\s/g, '').toUpperCase();
  let bits = '';
  for (const c of limpia) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0');
  const clave = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const contador = Buffer.alloc(8);
  contador.writeBigInt64BE(BigInt(Math.floor(momento / 1000 / 30)));
  const hash = createHmac('sha1', clave).update(contador).digest();
  const desplazamiento = hash[hash.length - 1]! & 0x0f;
  return ((hash.readUInt32BE(desplazamiento) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

/** WebKit (iPhone 14) no tiene autenticador virtual: ahí el flujo va con contraseña y TOTP. */
export const conPasskey = (info: TestInfo) => info.project.name !== 'iphone-14';
