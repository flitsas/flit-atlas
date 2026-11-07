import { expect, type Route, test } from '@playwright/test';

import { simularApi } from './api-simulada';

/**
 * HU 5.4 (ATLAS-ADR-0034): un documento marcado como favorito se lee en modo avión, con el service worker de producción y la
 * API simulada; uno que no es favorito, no. Necesita la PWA construida (`vite preview` o la imagen nginx).
 */
test.use({ serviceWorkers: 'allow' });

const DOCUMENTO = {
  id: 'd1',
  aplicativoId: 'a-demo',
  origen: 'atlas',
  tipo: 'guia_funcional',
  titulo: 'Trámites en línea',
  visibilidad: 'interno_negocio',
  audiencias: ['negocio'],
  responsable: 'Área Demo',
  estado: 'publicado',
  ruta: null,
  versionVigente: 1,
  revisionActual: 1,
  actualizadoEn: '2026-11-05T12:00:00Z',
};
const VERSION = {
  numero: 1,
  titulo: 'Trámites en línea',
  visibilidad: 'interno_negocio',
  esBorrador: false,
  sha256: 'h1',
  repositorio: null,
  rama: null,
  commitSha: null,
  revisionOrigen: 1,
  publicadoEn: '2026-11-04T12:00:00Z',
};

test('un favorito se lee en modo avión; otro documento, no', async ({ page, context }) => {
  await simularApi(page, { permisos: ['atlas.lector', 'atlas.docs.ver'], banderas: { 'modulo.documentacion': true } });
  let favorito = false;
  const json = (cuerpo: unknown) => ({ status: 200, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  await page.route('**/api/v1/documentacion/**', (route) => {
    const ruta = new URL(route.request().url()).pathname.replace('/api/v1/documentacion', '');
    if (ruta.startsWith('/tiempo-real')) return route.fulfill({ status: 404 });
    if (ruta === '/favoritos') return route.fulfill(json(favorito ? [{ id: 'f1', documentoId: 'd1', titulo: DOCUMENTO.titulo, tipo: DOCUMENTO.tipo, versionVigente: 1, ruta: '/documentacion/d1', marcadoEn: '2026-11-06T12:00:00Z' }] : []));
    if (ruta === '/favoritos/d1') {
      favorito = route.request().method() === 'PUT';
      return route.fulfill(json({ id: 'f1' }));
    }
    if (ruta === '/documentos/d1' || ruta === '/documentos/d2') {
      return route.fulfill(json({ documento: { ...DOCUMENTO, id: ruta.slice(-2) }, canales: [], version: { version: VERSION, contenido: 'Cuerpo guardado para leer sin red.' }, puedeEditar: false, puedeAprobar: false }));
    }
    if (ruta.endsWith('/versiones')) return route.fulfill(json([VERSION]));
    return route.fulfill({ status: 404 });
  });

  await page.goto('/documentacion/d1');
  await expect(page.getByText('Cuerpo guardado para leer sin red.')).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await page.getByRole('button', { name: 'Guardar en favoritos' }).click();
  await expect(page.getByText(/Disponible sin conexión/)).toBeVisible();

  const sinRed = (ruta: Route) => ruta.abort('internetdisconnected');
  await context.setOffline(true);
  await page.route('**/api/**', sinRed);
  await page.reload();
  await expect(page.getByText('Cuerpo guardado para leer sin red.')).toBeVisible();
  await expect(page.getByText(/Sin conexión: documento guardado el/)).toBeVisible();

  await page.goto('/documentacion/d2').catch(() => undefined);
  await expect(page.getByRole('heading', { name: 'No pudimos cargar el documento' })).toBeVisible();
  await context.setOffline(false);
});
