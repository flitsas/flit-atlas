import { join } from 'node:path';

import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test, type TestInfo } from '@playwright/test';

import { simularApi } from './api-simulada';
import { vigilarCsp } from './ayudas';

/**
 * Gobierno de IA (ATLAS-ADR-0055) en los tres perfiles, con la API simulada. Contra la imagen nginx revisan además la CSP.
 * Con ATLAS_CAPTURAS=<carpeta> guarda las capturas de la evidencia.
 */
test.use({ serviceWorkers: 'block' });

const FICHA = {
  nombre: 'OCR de prueba', aplicacion: 'App de prueba', aplicativoId: null, repo: null, duenoPersonaId: null, ambientes: [], proposito: 'Prellenar trámites de prueba.', tipo: 'ocr', usuariosAfectados: ['cliente'],
  decisionSobrePersonas: null, decisionDescripcion: null, humanoEnBucle: 'revision_posterior', categoriasDato: ['personal'], tiposPii: [], volumenMensualEstimado: null, baseLegal: null, baseLegalRef: null, minimizacion: null,
  presidioActivo: null, presidioEntidades: [], aliasUsados: ['flit-ocr'], proveedorActual: null, transferenciaInternacional: null, metricasCalidad: null, evalsRuta: null, promptsUbicacion: null, controles: null, avisoUsuario: null, planRollback: null, enlaces: [],
};
const RESUMEN = { id: 'c1', codigo: 'ocr-prueba', nombre: 'OCR de prueba', aplicacion: 'App de prueba', estado: 'borrador', nivelRiesgo: null, riesgoPreliminar: 'alto', dueno: null, proximaRevision: null, revisionVencida: false };

async function simular(page: Page, permisos: string[], bandera = true) {
  await simularApi(page, { permisos: ['atlas.lector', ...permisos], banderas: { 'modulo.gia': bandera } });
  const json = (cuerpo: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(cuerpo) });
  await page.route('**/api/v1/gia/**', (route) => {
    const ruta = new URL(route.request().url()).pathname.replace('/api/v1/gia', '');
    const respuestas: Record<string, unknown> = {
      '/tablero': {
        casosPorEstado: [{ clave: 'borrador', total: 13 }],
        casosPorRiesgo: [{ clave: 'alto', total: 5 }, { clave: 'medio', total: 5 }, { clave: 'medio_alto', total: 2 }, { clave: 'bajo', total: 1 }],
        revisionesVencidas: 0,
        kpis: [
          { codigo: 'costo', nombre: 'Desviación del costo mensual frente al presupuesto', disponible: false, valor: null, total: null, meta: '≤ 10 %', nota: 'Sin datos hasta tener el gateway.' },
          { codigo: 'casos_aprobados', nombre: 'Casos de uso con ficha aprobada', disponible: true, valor: 0, total: 13, meta: '100 %', nota: null },
        ],
      },
      '/casos': { elementos: [RESUMEN] },
      '/casos/c1': { caso: RESUMEN, ficha: FICHA, detallePlan: '**CU-01.** Texto del plan **[por verificar]**.', versionActual: 1, aprobadoPor: null, aprobadoEn: null, faltantes: ['dueño del caso', 'ambientes habilitados'], puedeGestionar: true },
      '/casos/c1/evaluaciones': [],
      '/politicas': [{ id: 'p1', codigo: 'POL-SGSI-IA-01', version: '0.1', titulo: 'Uso aceptable de IA', estado: 'borrador', requiereAceptacion: true, vigenteDesde: null, pendienteDeRedaccion: true, aceptadaPorMi: false }],
      '/politicas/p1': {
        politica: { id: 'p1', codigo: 'POL-SGSI-IA-01', version: '0.1', titulo: 'Uso aceptable de IA', estado: 'borrador', requiereAceptacion: true, vigenteDesde: null, pendienteDeRedaccion: true, aceptadaPorMi: false },
        contenido: '> **[PENDIENTE DE REDACCIÓN]** Esta política no está redactada.\n\n## Insumo del plan 02\n\nPuntos clave.',
        fuenteGit: null,
        aprobadaPor: null,
      },
      '/herramientas': [],
      '/incidentes': [],
      '/incidentes/catalogo': {
        tipos: [{ codigo: 'INC-IA-02', tipo: 'Llave de proveedor o virtual expuesta', ejemplo: 'Llave en un commit', playbook: ['Revocar la llave.'] }],
        severidades: [{ codigo: 'S1', nombre: 'Crítica', criterio: 'PII expuesta', tiempoRespuesta: '1 h', notificaA: 'Comité, OPD' }],
      },
      '/roles-gobierno': [],
    };
    return ruta in respuestas ? route.fulfill(json(respuestas[ruta])) : route.fulfill(json({ codigo: 'no_encontrado', mensaje: 'No existe.' }, 404));
  });
}

async function revisar(page: Page) {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity));
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const resumen = resultado.violations.map((v) => `${v.impact} ${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(resultado.violations.filter((v) => v.impact === 'critical' || v.id === 'color-contrast'), resumen.join('\n')).toEqual([]);
  const { ancho, vista } = await page.evaluate(() => ({ ancho: document.documentElement.scrollWidth, vista: window.innerWidth }));
  expect(ancho, 'scroll horizontal').toBeLessThanOrEqual(vista);
}

const foto = async (page: Page, info: TestInfo, nombre: string) => {
  if (process.env.ATLAS_CAPTURAS) await page.screenshot({ path: join(process.env.ATLAS_CAPTURAS, `${info.project.name}-${nombre}.png`), fullPage: true });
};

const sinViolaciones = (violaciones: string[]) => {
  // La captura de Playwright inyecta un <style> que la CSP bloquea en WebKit (ATLAS-ADR-0053).
  if (!process.env.ATLAS_CAPTURAS) expect(violaciones).toEqual([]);
};

test('apagado por bandera, el módulo no está aunque se tenga el permiso', async ({ page }) => {
  await simular(page, ['atlas.gia.ver'], false);
  await page.goto('/gobierno-ia');
  await expect(page.getByRole('heading', { name: 'Gobierno de IA', level: 1 })).toHaveCount(0);
});

test('tablero, casos y ficha a 360 px, sin violar la CSP', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, ['atlas.gia.ver', 'atlas.gia.gestionar']);
  await page.goto('/gobierno-ia');
  await expect(page.getByRole('heading', { name: 'Gobierno de IA', level: 1 })).toBeVisible();
  await expect(page.getByText('Sin datos', { exact: true })).toBeVisible();
  await expect(page.getByText('Sin datos hasta tener el gateway.')).toBeVisible();
  await page.setViewportSize({ width: 360, height: 740 });
  await revisar(page);
  await foto(page, info, 'gia-tablero-360');

  await page.getByRole('link', { name: 'Casos', exact: true }).click();
  await page.getByRole('link', { name: /OCR de prueba/ }).click();
  await expect(page.getByRole('heading', { name: 'OCR de prueba', level: 1 })).toBeVisible();
  await expect(page.getByText('dueño del caso')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar a revisión' })).toBeVisible();
  await expect(page.getByLabel('Ficha del plan')).toContainText('[por verificar]');
  await revisar(page);
  await foto(page, info, 'gia-caso-360');
  sinViolaciones(violaciones);
});

test('una política pendiente avisa que no tiene vigencia y no se publica', async ({ page }, info) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, ['atlas.gia.ver', 'atlas.gia.gestionar']);
  await page.goto('/gobierno-ia/politicas');
  await expect(page.getByText('Pendiente de redacción', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: /Uso aceptable de IA/ }).click();
  await expect(page.getByText(/no está redactada ni aprobada/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Publicar como Comité' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Leí y acepto' })).toHaveCount(0);
  await revisar(page);
  await foto(page, info, 'gia-politica');
  sinViolaciones(violaciones);
});

test('herramientas, incidentes y roles se ven vacíos sin inventar datos', async ({ page }) => {
  const violaciones = await vigilarCsp(page);
  await simular(page, ['atlas.gia.ver', 'atlas.gia.gestionar']);
  await page.goto('/gobierno-ia/herramientas');
  await expect(page.getByText(/se cargan con el resultado de la encuesta/)).toBeVisible();
  await revisar(page);
  await page.getByRole('link', { name: 'Incidentes', exact: true }).click();
  await expect(page.getByText('No hay incidentes registrados.')).toBeVisible();
  await page.getByRole('radio', { name: /Llave de proveedor/ }).check();
  await expect(page.getByText('Revocar la llave.')).toBeVisible();
  await revisar(page);
  await page.getByRole('link', { name: 'Roles', exact: true }).click();
  await expect(page.getByText(/ningún caso se puede aprobar/)).toBeVisible();
  await revisar(page);
  sinViolaciones(violaciones);
});

test('quien no tiene permiso no ve la entrada ni la ruta', async ({ page }) => {
  await simular(page, []);
  await page.goto('/gobierno-ia');
  await expect(page.getByRole('heading', { name: 'Gobierno de IA', level: 1 })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Gobierno de IA' })).toHaveCount(0);
});
