import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import AxeBuilder from '@axe-core/playwright';
import { expect, type Frame, type Page, test, type TestInfo } from '@playwright/test';

import { simularApi } from './api-simulada';
import { esMovil, vigilarCsp } from './ayudas';

/**
 * HU 6.3 a 6.5 (ATLAS-ADR-0056) en los tres perfiles, con la API SIMULADA: aquí se prueba la PWA y el visor real de `likec4/react` con la
 * salida real de `likec4 export json` de modelos ficticios. Contra la imagen nginx revisan además la CSP del shell y la del visor.
 * Con ATLAS_CAPTURAS=<carpeta> guarda las capturas; con ATLAS_MODELO_GRANDE=<json> dibuja otro landscape (solo para revisar a mano).
 */
test.use({ serviceWorkers: 'block' });

const FIXTURES = join(process.cwd(), '..', '..', 'tools', 'arquitectura-gen', 'fixtures');
const DEMO = readFileSync(join(FIXTURES, 'modelo-demo.export.json'), 'utf8');
const GRANDE = readFileSync(process.env.ATLAS_MODELO_GRANDE ?? join(FIXTURES, 'modelo-grande.export.json'), 'utf8');

const publicacion = (id: string, canal = 'pdn') => ({
  id, tipo: 'modelo', canal, cluster: '', rama: 'release', commitSha: 'abc1234def', version: null, estado: 'publicado', visibilidad: 'interno_ti',
  herramientaVersion: '1.59.4', publicadoEn: '2026-11-06T12:00:00Z',
});

const VISTAS_DEMO = [
  { id: 'landscape', tipo: 'estatica', categoria: 'landscape', titulo: 'Landscape demo' },
  { id: 'demo_contexto', tipo: 'estatica', categoria: 'contexto', titulo: 'Contexto del sistema demo' },
  { id: 'demo_contenedores', tipo: 'estatica', categoria: 'contenedores', titulo: 'Contenedores del sistema demo' },
  { id: 'demo_web_componentes', tipo: 'estatica', categoria: 'componentes', titulo: 'Componentes del portal web' },
  { id: 'flujo_radicacion', tipo: 'dinamica', categoria: 'dinamica', titulo: 'Radicación de un trámite' },
  { id: 'despliegue_qa', tipo: 'despliegue', categoria: 'despliegue', titulo: 'Despliegue en QA' },
];

const FICHA = {
  id: 'demo.web', kind: 'container', titulo: 'Portal web', descripcion: 'Portal ficticio de radicación', tecnologia: 'Next.js', etiquetas: ['critico'],
  metadatos: { k8s_workload: ['demo-web'], adr: ['ADR-0001'] },
  aplicativo: { slug: 'demo-portal', nombre: 'Portal Demo', ruta: '/catalogo/demo-portal' },
  contratos: [{ ruta: 'contracts/openapi/demo-api.v1.yaml', tipo: 'openapi', nombre: 'API Demo', ultimaVersion: '1.2.0', rutaAtlas: '/contratos/c-demo' }],
  adr: [{ codigo: 'ADR-0001', rutaAtlas: '/documentacion?texto=ADR-0001' }],
  enlaces: [{ url: 'https://ejemplo.com/demo', titulo: 'Sitio' }],
  driftAbierto: 2,
};

const PERMISOS = ['atlas.lector', 'atlas.arquitectura.ver', 'atlas.arquitectura.ver_despliegue', 'atlas.catalogo.ver'];

async function simular(page: Page, permisos = PERMISOS, proyectos?: unknown[]) {
  await simularApi(page, { permisos, banderas: { 'modulo.arquitectura': true } });
  const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  await page.route('**/api/v1/arquitectura/**', (route) => {
    const url = new URL(route.request().url());
    const ruta = url.pathname.replace('/api/v1/arquitectura', '');
    if (ruta === '/proyectos') {
      return route.fulfill(json(proyectos ?? [{ codigo: 'demo', aplicativo: FICHA.aplicativo, canales: [{ canal: 'pdn', publicacion: publicacion('p-demo') }] }]));
    }
    if (ruta === '/proyectos/demo/vigente') return route.fulfill(json({ proyecto: 'demo', publicacion: publicacion('p-demo', url.searchParams.get('canal') ?? 'pdn'), vistas: VISTAS_DEMO }));
    if (ruta === '/proyectos/grande/vigente') {
      return route.fulfill(json({ proyecto: 'grande', publicacion: publicacion('p-grande'), vistas: [{ id: 'landscape', tipo: 'estatica', categoria: 'landscape', titulo: 'Landscape de 44 nodos' }] }));
    }
    if (ruta === '/publicaciones/p-demo/modelo') return route.fulfill({ status: 200, contentType: 'application/json', body: DEMO });
    if (ruta === '/publicaciones/p-grande/modelo') return route.fulfill({ status: 200, contentType: 'application/json', body: GRANDE });
    if (ruta.includes('/elementos/')) return route.fulfill(json(FICHA));
    if (ruta === '/drift') {
      return route.fulfill(json({
        ambientes: [{
          ambiente: 'qa', modelo: { estado: 'con_datos', motivo: null }, gitops: { estado: 'con_datos', motivo: null }, argo: { estado: 'sin_datos', motivo: 'sin_configurar' },
          clusteres: [{ cluster: 'cluster-demo', estado: 'sin_datos', motivo: 'sin_configurar', leidaEn: null }], modeloGitops: 'con_drift', gitopsArgo: 'sin_datos',
        }],
        hallazgos: [{
          id: 'h1', capa: 'modelo_gitops', ambiente: 'qa', cluster: null, codigo: 'ausente_en_gitops', elementoId: 'demo.api', sujeto: 'demo-api',
          descripcion: 'El modelo declara «demo-api» en qa y gitops no lo tiene.', severidad: 'media', estado: 'abierto', detectadoEn: '2026-11-06T12:00:00Z', resueltoEn: null,
        }],
      }));
    }
    if (ruta === '/despliegue/vigentes' || ruta === '/despliegue/publicaciones') {
      return route.fulfill(json({ elementos: [{ ...publicacion('s2', 'qa'), tipo: 'despliegue', cluster: 'cluster-demo' }, { ...publicacion('s1', 'qa'), tipo: 'despliegue', cluster: 'cluster-demo', publicadoEn: '2026-11-05T12:00:00Z' }].slice(0, ruta.endsWith('vigentes') ? 1 : 2) }));
    }
    if (ruta === '/despliegue/publicaciones/s2') {
      return route.fulfill(json({
        publicacion: { ...publicacion('s2', 'qa'), tipo: 'despliegue', cluster: 'cluster-demo' },
        snapshot: {
          ambiente: 'qa', gitopsSha: 'abc1234def', generadoEn: '2026-11-06T12:00:00Z', fueraDeK3s: [],
          cluster: { id: 'cluster-demo', raiz: 'root-demo', namespaces: [{ nombre: 'demo-qa', application: 'demo-qa', ingressHosts: ['app.ejemplo.com'], workloads: [{ kind: 'Deployment', nombre: 'demo-web', replicas: 2, imagenes: [{ imagen: 'ghcr.io/flitsas-demo/web', digest: `sha256:${'a'.repeat(64)}` }] }] }] },
        },
      }));
    }
    if (ruta === '/despliegue/diff/s1/s2') return route.fulfill(json({ altas: [{ tipo: 'workload', id: 'demo-qa/Deployment/demo-web', detalle: null }], bajas: [], cambios: [] }));
    return route.fulfill(json({}, 404));
  });
}

const visor = async (page: Page): Promise<Frame> => {
  const marco = page.locator('iframe[src="/visor/likec4.html"]');
  await expect(marco).toHaveAttribute('data-pintado', 'true', { timeout: 30_000 });
  const frame = await (await marco.elementHandle())!.contentFrame();
  return frame!;
};

async function revisar(page: Page) {
  // El lienzo es de LikeC4 y corre aislado: su alternativa accesible es la lista «Elementos de esta vista» (ATLAS-ADR-0056, «Por verificar»).
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity));
  const resultado = await new AxeBuilder({ page }).exclude('iframe').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
  const { ancho, ventana } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, ventana: window.innerWidth }));
  expect(ancho, 'scroll horizontal').toBeLessThanOrEqual(ventana);
}

const foto = async (page: Page, info: TestInfo, nombre: string) => {
  if (process.env.ATLAS_CAPTURAS) await page.screenshot({ path: join(process.env.ATLAS_CAPTURAS, `${info.project.name}-${nombre}.png`) });
};

const sinViolaciones = (violaciones: string[]) => {
  // La captura de Playwright inyecta un <style> que la CSP bloquea en WebKit (ATLAS-ADR-0053).
  if (!process.env.ATLAS_CAPTURAS) expect(violaciones).toEqual([]);
};

test('dibuja cada tipo de vista con likec4/react en el iframe aislado, sin violar la CSP', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page);
  await page.goto('/arquitectura');
  await expect(page.getByRole('heading', { name: 'Arquitectura', level: 1 })).toBeVisible();
  await page.getByRole('link', { name: /Portal Demo/ }).click();

  let frame = await visor(page);
  await expect(frame.getByText('Sistema Demo').first()).toBeVisible();
  await foto(page, info, 'landscape');
  await revisar(page);

  for (const [nombre, texto] of [
    ['Contexto del sistema demo', 'Sistema Demo'],
    ['Contenedores del sistema demo', 'Portal web'],
    ['Componentes del portal web', 'Inicio de sesión'],
    ['Radicación de un trámite', 'Portal web'],
    ['Despliegue en QA', 'Portal web'],
  ] as const) {
    await page.getByRole('navigation', { name: 'Vistas' }).getByRole('button', { name: nombre }).click();
    await expect(page).toHaveURL(/\/vistas\//);
    frame = await visor(page);
    await expect(frame.getByText(texto).first()).toBeVisible({ timeout: 15_000 });
    await foto(page, info, nombre.split(' ')[0]!.toLowerCase());
  }
  sinViolaciones(violaciones);
});

test('tocar un elemento abre su ficha con los enlaces al catálogo, a contratos y a ADR', async ({ page }, info) => {
  await simular(page);
  await page.goto('/arquitectura/proyectos/demo/vistas/demo_contenedores');
  const frame = await visor(page);
  // Como una persona: donde se ve el nodo. El nodo de LikeC4 se anima al pasar por encima y Playwright nunca lo da por estable.
  const nodo = frame.locator('.react-flow__node', { hasText: 'Portal web' }).first();
  await expect(nodo).toBeVisible();
  await page.locator('iframe[src="/visor/likec4.html"]').scrollIntoViewIfNeeded();
  const caja = (await nodo.boundingBox())!;
  if (info.project.name === 'escritorio') await page.mouse.click(caja.x + caja.width / 2, caja.y + caja.height / 2);
  else await page.touchscreen.tap(caja.x + caja.width / 2, caja.y + caja.height / 2);

  const ficha = page.getByRole('dialog', { name: 'Portal web' });
  await expect(ficha).toBeVisible();
  await expect(ficha.getByRole('link', { name: 'Portal Demo' })).toHaveAttribute('href', '/catalogo/demo-portal');
  await expect(ficha.getByRole('link', { name: /API Demo/ })).toHaveAttribute('href', '/contratos/c-demo');
  await expect(ficha.getByRole('link', { name: 'ADR-0001' })).toHaveAttribute('href', '/documentacion?texto=ADR-0001');
  await expect(ficha.getByRole('link', { name: '2 hallazgos de drift abiertos' })).toBeVisible();
  await foto(page, info, 'ficha');
});

test('un landscape de 44 nodos se dibuja completo, entra en escritorio y se puede consultar en lista', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, PERMISOS, [{ codigo: 'grande', aplicativo: null, canales: [{ canal: 'pdn', publicacion: publicacion('p-grande') }] }]);
  await page.goto('/arquitectura/proyectos/grande');
  const frame = await visor(page);
  const nodos = frame.locator('.react-flow__node');
  await expect(nodos.first()).toBeVisible({ timeout: 30_000 });
  const total = JSON.parse(GRANDE).views.landscape.nodes.length;
  await expect(nodos).toHaveCount(total);
  expect(total).toBeGreaterThanOrEqual(40);
  await foto(page, info, 'landscape-44');

  // En escritorio entra todo al abrir. En móvil no: el ajuste de LikeC4 tiene un zoom mínimo fijo de 5 % y, a ese ancho, los bordes quedan
  // cortados; se llega con arrastre o pellizco, o por la lista (ATLAS-ADR-0056, «Por verificar»).
  const marco = (await page.locator('iframe[src="/visor/likec4.html"]').boundingBox())!;
  const fuera = () => nodos.evaluateAll((lista, { ancho, alto }) => lista.filter((n) => {
    const c = n.getBoundingClientRect();
    return c.left < -1 || c.top < -1 || c.right > ancho + 1 || c.bottom > alto + 1;
  }).length, { ancho: marco.width, alto: marco.height });
  if (!esMovil(info)) await expect.poll(fuera, { message: 'nodos fuera del marco al abrir', timeout: 10_000 }).toBe(0);
  const anchoNodo = await nodos.first().evaluate((n) => n.getBoundingClientRect().width);
  test.info().annotations.push({ type: 'ancho-nodo-px', description: `${Math.round(anchoNodo)} de marco ${Math.round(marco.width)}x${Math.round(marco.height)}` });
  console.log(`[${info.project.name}] ${total} nodos; ancho de un nodo ${Math.round(anchoNodo)} px; marco ${Math.round(marco.width)}x${Math.round(marco.height)}`);

  // La alternativa legible en cualquier pantalla.
  await page.getByText(/Elementos de esta vista/).click();
  await expect(page.getByRole('button', { name: 'Perfil demo 1' })).toBeVisible();
  sinViolaciones(violaciones);
});

test('el lienzo hace zoom con la rueda en escritorio y con gestos en móvil', async ({ page, browserName }, info) => {
  await simular(page);
  await page.goto('/arquitectura/proyectos/demo/vistas/demo_contenedores');
  const frame = await visor(page);
  const transformacion = () => frame.locator('.react-flow__viewport').evaluate((v) => getComputedStyle(v).transform);
  await expect(frame.locator('.react-flow__viewport')).toBeVisible();
  await page.locator('iframe[src="/visor/likec4.html"]').scrollIntoViewIfNeeded();
  const antes = await transformacion();
  const caja = (await page.locator('iframe[src="/visor/likec4.html"]').boundingBox())!;
  const centro = { x: Math.round(caja.x + caja.width / 2), y: Math.round(caja.y + caja.height / 2) };

  if (info.project.name === 'pixel-7' && browserName === 'chromium') {
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Input.synthesizePinchGesture', { x: centro.x, y: centro.y, scaleFactor: 2, relativeSpeed: 400, gestureSourceType: 'touch' });
  } else if (info.project.name === 'iphone-14') {
    test.skip(true, 'WebKit en Playwright no sintetiza gestos de pellizco: queda para la prueba a mano en un iPhone.');
  } else {
    await page.mouse.move(centro.x, centro.y);
    await page.mouse.wheel(0, -400);
  }
  await expect.poll(transformacion, { timeout: 5000 }).not.toBe(antes);
});

test('el panel de drift dice «sin datos» en Argo CD y no «sin drift»', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page);
  await page.goto('/arquitectura/drift?ambiente=qa');
  await expect(page.getByRole('heading', { name: 'Panel de drift' })).toBeVisible();
  await expect(page.getByText(/la lectura de Argo CD no está configurada/)).toBeVisible();
  const gitopsArgo = page.locator('dl div', { hasText: 'Gitops y Argo CD:' });
  await expect(gitopsArgo).toContainText('Sin datos');
  await expect(gitopsArgo).not.toContainText('Sin drift');
  await expect(page.getByText(/El modelo declara «demo-api»/)).toBeVisible();
  await foto(page, info, 'drift');
  await revisar(page);
  sinViolaciones(violaciones);
});

test('el despliegue muestra el snapshot y lo compara con el anterior', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page);
  await page.goto('/arquitectura/despliegue');
  await page.getByRole('button', { name: /QA/ }).click();
  await expect(page.getByText('app.ejemplo.com', { exact: false })).toBeVisible();
  await expect(page.getByText('demo-web')).toBeVisible();
  await page.getByRole('button', { name: 'Comparar con el snapshot anterior' }).click();
  await expect(page.getByRole('region', { name: /Cambios frente/ })).toContainText('demo-qa/Deployment/demo-web');
  await foto(page, info, 'despliegue');
  await revisar(page);
  sinViolaciones(violaciones);
});

test('sin ver_despliegue no se ofrecen despliegue ni drift, y la ruta dice que no hay acceso', async ({ page }) => {
  await simular(page, ['atlas.lector', 'atlas.arquitectura.ver']);
  await page.goto('/arquitectura');
  await expect(page.getByRole('link', { name: /Portal Demo/ })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Panel de drift' })).toHaveCount(0);
  await page.goto('/arquitectura/drift');
  await expect(page.getByText(/No tienes acceso/)).toBeVisible();
});

test('sin el permiso, el módulo no aparece y la ruta dice que no hay acceso', async ({ page }) => {
  await simular(page, ['atlas.lector']);
  await page.goto('/arquitectura');
  await expect(page.getByText(/No tienes acceso/)).toBeVisible();
});
