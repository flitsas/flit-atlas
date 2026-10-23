import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from '../App';
import { TextoLegal } from './componentes';
import { tomarFragmento } from './fragmento';
import { base64UrlABuffer, bufferABase64Url, opcionesCreacion, opcionesSolicitud } from './webauthn';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.pushState({}, '', '/');
});

describe('webauthn', () => {
  it('convierte entre base64url y bytes sin perder nada', () => {
    const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253, 254, 255]);
    const texto = bufferABase64Url(bytes.buffer)!;
    expect(texto).not.toMatch(/[+/=]/);
    expect(new Uint8Array(base64UrlABuffer(texto))).toEqual(bytes);
  });

  it('arma las opciones del navegador desde el JSON del servidor', () => {
    const creacion = opcionesCreacion(
      JSON.stringify({ challenge: 'AQID', rp: { id: 'localhost', name: 'localhost' }, user: { id: 'MTIz', name: 'a@ejemplo.com', displayName: 'A' }, pubKeyCredParams: [], excludeCredentials: [{ type: 'public-key', id: 'BAU' }] }),
    );
    expect(new Uint8Array(creacion.challenge as ArrayBuffer)).toEqual(new Uint8Array([1, 2, 3]));
    expect(new TextDecoder().decode(creacion.user.id as ArrayBuffer)).toBe('123');
    expect(new Uint8Array(creacion.excludeCredentials![0]!.id as ArrayBuffer)).toEqual(new Uint8Array([4, 5]));
    const solicitud = opcionesSolicitud(JSON.stringify({ challenge: 'AQID', rpId: 'localhost', allowCredentials: [] }));
    expect(solicitud.rpId).toBe('localhost');
  });
});

describe('enlaces de un solo uso', () => {
  it('toma el token del fragmento y lo quita de la barra de direcciones', () => {
    const token = 'abcdefghijklmnopqrstuvwxyz0123456789_-AB';
    window.history.pushState({}, '', `/invitacion#${token}`);
    expect(tomarFragmento('/invitacion')).toBe(token);
    expect(window.location.hash).toBe('');
    // El modo estricto de React vuelve a llamar al inicializador: el token sigue en memoria.
    expect(tomarFragmento('/invitacion')).toBe(token);
  });

  it('un enlace usado o vencido muestra que no sirve', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ error: 'enlace_no_valido', mensaje: 'x' }), { status: 410, headers: { 'Content-Type': 'application/json' } })),
    );
    window.history.pushState({}, '', '/recuperar#0123456789abcdef0123456789abcdef.AAAAAAAAAAAAAAAAAAAA');
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Este enlace no sirve' })).toBeInTheDocument();
  });
});

describe('cuenta de emergencia', () => {
  it('un enlace de emergencia usado o vencido muestra que no sirve y quita el token de la barra', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ error: 'enlace_no_valido', mensaje: 'x' }), { status: 410, headers: { 'Content-Type': 'application/json' } })),
    );
    window.history.pushState({}, '', '/emergencia#0123456789abcdef0123456789abcdef.AAAAAAAAAAAAAAAAAAAA');
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Este enlace no sirve' })).toBeInTheDocument();
    expect(window.location.hash).toBe('');
  });

  it('con un enlace válido ofrece activar la cuenta', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ correo: 'admin.emergencia@atlas.local', nombre: 'Administrador de emergencia' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })),
    );
    window.history.pushState({}, '', '/emergencia#0123456789abcdef0123456789abcdef.BBBBBBBBBBBBBBBBBBBB');
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Activa la cuenta de emergencia' })).toBeInTheDocument();
    expect(screen.getByText('admin.emergencia@atlas.local')).toBeInTheDocument();
  });
});

describe('documentos legales', () => {
  it('muestra el aviso de borrador y la lista numerada sin HTML del texto', () => {
    render(<TextoLegal texto={'# Términos\n\n> **BORRADOR [por verificar]**. Ejemplo.\n\n1. **Objeto.** Uno.\n2. Dos.'} />);
    expect(screen.getByRole('heading', { name: 'Términos' })).toBeInTheDocument();
    expect(screen.getByText(/BORRADOR \[por verificar\]/)).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });
});
