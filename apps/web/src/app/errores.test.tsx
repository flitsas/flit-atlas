import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';

import { CABECERA_CSRF } from './api';
import {
  armarReporte,
  debeRecargarPorCarga,
  esErrorDeCarga,
  MARCA_RECARGA,
  marcarSesion,
  pagina,
  referenciaCorta,
  RUTA_ERRORES_CLIENTE,
} from './errores';
import { ErrorDeRuta } from './paginas/Estados';

function pintarConError(error: unknown) {
  function Lanza(): never {
    throw error;
  }
  const enrutador = createMemoryRouter([{ path: '/', element: <Lanza />, errorElement: <ErrorDeRuta /> }]);
  return render(<RouterProvider router={enrutador} />);
}

let recargar: ReturnType<typeof vi.spyOn>;
let fetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  window.sessionStorage.clear();
  recargar = vi.spyOn(pagina, 'recargar').mockImplementation(() => undefined);
  fetch = vi.fn(async () => new Response(null, { status: 202 }));
  vi.stubGlobal('fetch', fetch);
  // React registra en consola el error que atrapa el enrutador: ruido esperado en estas pruebas.
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  marcarSesion(false);
});

describe('errores de carga de chunks', () => {
  it('reconoce los mensajes de Chromium, Safari, Firefox y Vite', () => {
    expect(esErrorDeCarga(new TypeError('Failed to fetch dynamically imported module: https://ejemplo.com/assets/Roles-abc.js'))).toBe(true);
    expect(esErrorDeCarga(new TypeError('Importing a module script failed.'))).toBe(true);
    expect(esErrorDeCarga(new TypeError('error loading dynamically imported module: https://ejemplo.com/a.js'))).toBe(true);
    expect(esErrorDeCarga(Object.assign(new Error('Loading chunk 42 failed.'), { name: 'ChunkLoadError' }))).toBe(true);
    expect(esErrorDeCarga(new Error('Unable to preload CSS for /assets/x.css'))).toBe(true);
    expect(esErrorDeCarga(new TypeError("Cannot read properties of undefined (reading 'x')"))).toBe(false);
    expect(esErrorDeCarga('Failed to fetch dynamically imported module')).toBe(false);
  });

  it('marca la recarga una sola vez por ventana; sin sessionStorage no recarga', () => {
    expect(debeRecargarPorCarga(1_000)).toBe(true);
    expect(debeRecargarPorCarga(2_000)).toBe(false);
    // Pasada la ventana (otro despliegue más tarde), vuelve a intentarlo.
    expect(debeRecargarPorCarga(1_000 + 11 * 60_000)).toBe(true);

    window.sessionStorage.clear();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    expect(debeRecargarPorCarga()).toBe(false);
  });

  it('la pantalla recarga sola la primera vez y no reporta nada', async () => {
    pintarConError(new TypeError('Failed to fetch dynamically imported module: https://ejemplo.com/assets/Roles-abc.js'));

    expect(await screen.findByRole('status')).toHaveTextContent('Recargando');
    expect(recargar).toHaveBeenCalledTimes(1);
    expect(window.sessionStorage.getItem(MARCA_RECARGA)).not.toBeNull();
    expect(fetch).not.toHaveBeenCalled();
  });

  it('si ya recargó, muestra el botón de recargar en vez de entrar en bucle', async () => {
    window.sessionStorage.setItem(MARCA_RECARGA, String(Date.now()));
    pintarConError(new TypeError('Importing a module script failed.'));

    expect(await screen.findByRole('heading', { name: 'Algo salió mal' })).toBeInTheDocument();
    expect(recargar).not.toHaveBeenCalled();
    expect(screen.queryByText(/Referencia/)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Recargar la página' }));
    expect(recargar).toHaveBeenCalledTimes(1);
  });
});

describe('reporte de los demás errores', () => {
  beforeEach(() => marcarSesion(true));

  it('envía el error a atlas-api y muestra la referencia', async () => {
    window.history.pushState({}, '', '/administracion/roles?persona=x#token-secreto');
    pintarConError(new TypeError("Cannot read properties of undefined (reading 'nombre')"));

    const codigo = await screen.findByText(/^[2-9A-Z]{8}$/);
    expect(screen.getByText(/Referencia/)).toBeInTheDocument();
    expect(recargar).not.toHaveBeenCalled();
    expect(fetch).toHaveBeenCalledTimes(1);
    const [ruta, init] = fetch.mock.calls[0] as [string, RequestInit];
    expect(ruta).toBe(RUTA_ERRORES_CLIENTE);
    expect(init.method).toBe('POST');
    expect(new Headers(init.headers).get(CABECERA_CSRF)).toBe('1');
    const cuerpo = JSON.parse(String(init.body)) as Record<string, unknown>;
    expect(cuerpo).toMatchObject({ referencia: codigo.textContent, nombre: 'TypeError', mensaje: "Cannot read properties of undefined (reading 'nombre')" });
    // Solo la ruta: ni la consulta ni el fragmento (puede traer el token de una invitación).
    expect(cuerpo.ruta).toBe('/administracion/roles');
  });

  it('sin sesión (p. ej. en /invitacion) no envía nada, pero muestra la referencia y el botón de recargar', async () => {
    marcarSesion(false);
    window.history.pushState({}, '', '/invitacion');
    pintarConError(new TypeError('Fallo antes de entrar'));

    expect(await screen.findByText(/^[2-9A-Z]{8}$/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Recargar la página' })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });

  it('si la API no responde, la persona igual ve su referencia', async () => {
    fetch.mockRejectedValue(new TypeError('Failed to fetch'));
    pintarConError(new Error('Fallo de prueba'));

    expect(await screen.findByText(/^[2-9A-Z]{8}$/)).toBeInTheDocument();
  });

  it('recorta los campos y arma referencias sin caracteres ambiguos', () => {
    const error = new Error('x'.repeat(2_000));
    error.stack = 'y'.repeat(10_000);
    const reporte = armarReporte(error, 'ABCD2345', `/${'z'.repeat(500)}`);
    expect(reporte.mensaje).toHaveLength(500);
    expect(reporte.pila).toHaveLength(3_000);
    expect(reporte.ruta).toHaveLength(200);
    expect(armarReporte({ status: 404, statusText: 'Not Found' }, 'ABCD2345', '/').mensaje).toBe('404 Not Found');
    for (let i = 0; i < 50; i++) expect(referenciaCorta()).toMatch(/^[2-9A-HJKMNP-Z]{8}$/);
  });
});
