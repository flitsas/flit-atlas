/**
 * Passkeys (WebAuthn) en el navegador, sin librerías: las opciones llegan del servidor (PasskeyHandler de ASP.NET Core
 * Identity) como JSON de WebAuthn nivel 3 y la respuesta del autenticador vuelve igual. Se convierte a mano entre
 * base64url y ArrayBuffer porque `PublicKeyCredential.parseCreationOptionsFromJSON` y `toJSON()` aún no están en
 * todos los navegadores que soporta Atlas.
 */

type Json = Record<string, unknown>;

export function passkeysDisponibles(): boolean {
  return typeof window !== 'undefined' && typeof window.PublicKeyCredential === 'function' && !!navigator.credentials;
}

export function base64UrlABuffer(valor: string): ArrayBuffer {
  const base64 = valor.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(valor.length / 4) * 4, '=');
  const binario = atob(base64);
  const bytes = new Uint8Array(binario.length);
  for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i);
  return bytes.buffer;
}

export function bufferABase64Url(buffer: ArrayBuffer | ArrayBufferView | null | undefined): string | undefined {
  if (!buffer) return undefined;
  const bytes = buffer instanceof ArrayBuffer ? new Uint8Array(buffer) : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binario = '';
  for (const b of bytes) binario += String.fromCharCode(b);
  return btoa(binario).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

const descriptores = (lista: unknown) =>
  Array.isArray(lista) ? lista.map((d: Json) => ({ ...d, id: base64UrlABuffer(d.id as string) })) : undefined;

/** Opciones de creación (JSON) → `PublicKeyCredentialCreationOptions`. */
export function opcionesCreacion(json: string): PublicKeyCredentialCreationOptions {
  const o = JSON.parse(json) as Json;
  const usuario = o.user as Json;
  return {
    ...(o as unknown as PublicKeyCredentialCreationOptions),
    challenge: base64UrlABuffer(o.challenge as string),
    user: { ...(usuario as unknown as PublicKeyCredentialUserEntity), id: base64UrlABuffer(usuario.id as string) },
    excludeCredentials: descriptores(o.excludeCredentials) as PublicKeyCredentialDescriptor[] | undefined,
  };
}

/** Opciones de ingreso (JSON) → `PublicKeyCredentialRequestOptions`. */
export function opcionesSolicitud(json: string): PublicKeyCredentialRequestOptions {
  const o = JSON.parse(json) as Json;
  return {
    ...(o as unknown as PublicKeyCredentialRequestOptions),
    challenge: base64UrlABuffer(o.challenge as string),
    allowCredentials: descriptores(o.allowCredentials) as PublicKeyCredentialDescriptor[] | undefined,
  };
}

/** `PublicKeyCredential` → JSON que entiende el servidor (el mismo formato de `toJSON()`). */
export function credencialAJson(credencial: PublicKeyCredential): string {
  const r = credencial.response as AuthenticatorAttestationResponse & AuthenticatorAssertionResponse;
  const respuesta: Json = { clientDataJSON: bufferABase64Url(r.clientDataJSON) };
  if ('attestationObject' in r && r.attestationObject) {
    respuesta.attestationObject = bufferABase64Url(r.attestationObject);
    respuesta.transports = typeof r.getTransports === 'function' ? r.getTransports() : [];
  } else {
    respuesta.authenticatorData = bufferABase64Url(r.authenticatorData);
    respuesta.signature = bufferABase64Url(r.signature);
    respuesta.userHandle = bufferABase64Url(r.userHandle);
  }
  return JSON.stringify({
    id: credencial.id,
    rawId: bufferABase64Url(credencial.rawId),
    type: credencial.type,
    authenticatorAttachment: credencial.authenticatorAttachment ?? undefined,
    clientExtensionResults: credencial.getClientExtensionResults(),
    response: respuesta,
  });
}

export async function crearPasskey(opcionesJson: string): Promise<string> {
  const credencial = (await navigator.credentials.create({ publicKey: opcionesCreacion(opcionesJson) })) as PublicKeyCredential | null;
  if (!credencial) throw new Error('El navegador no creó la passkey.');
  return credencialAJson(credencial);
}

export async function usarPasskey(opcionesJson: string): Promise<string> {
  const credencial = (await navigator.credentials.get({ publicKey: opcionesSolicitud(opcionesJson) })) as PublicKeyCredential | null;
  if (!credencial) throw new Error('El navegador no devolvió la passkey.');
  return credencialAJson(credencial);
}
