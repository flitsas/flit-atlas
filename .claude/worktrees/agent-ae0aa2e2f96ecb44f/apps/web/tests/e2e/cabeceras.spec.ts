import { expect, test } from '@playwright/test';

/**
 * HU 4.10 (ATLAS-ADR-0048): cabeceras de seguridad de atlas-web. Corre contra la imagen nginx (`ATLAS_E2E_URL`) o contra
 * `vite preview`, que sirve las mismas cabeceras leídas de `deploy/`; contra el servidor de desarrollo de Vite no hay
 * cabeceras y se omite. Las de la API las prueba `EndurecimientoTests` (.NET).
 */
test.describe('cabeceras de seguridad de atlas-web', () => {
  test.skip(({ browserName }) => browserName !== 'chromium', 'Las cabeceras no dependen del navegador: basta un perfil');

  const directivas = (csp: string) => new Map(csp.split(';').map((d) => d.trim().split(/\s+/)).map(([n, ...v]) => [n!, v]));

  test('la PWA: CSP estricta con informe, Trusted Types en modo informe y las demás cabeceras', async ({ request }) => {
    const respuesta = await request.get('/');
    const h = respuesta.headers();
    test.skip(!h['content-security-policy'], 'Sin cabeceras: corre contra la imagen nginx o vite preview');

    const csp = directivas(h['content-security-policy']!);
    expect(csp.get('script-src')).toEqual(["'self'"]);
    expect(csp.get('style-src')).toEqual(["'self'"]);
    expect(csp.get('default-src')).toEqual(["'self'"]);
    expect(csp.get('object-src')).toEqual(["'none'"]);
    expect(csp.get('base-uri')).toEqual(["'none'"]);
    expect(csp.get('frame-ancestors')).toEqual(["'none'"]);
    expect(csp.get('form-action')).toEqual(["'self'"]);
    expect(csp.get('report-uri')).toEqual(['/api/v1/plataforma/csp-reportes']);
    expect(csp.get('report-to')).toEqual(['csp']);
    expect(csp.get('connect-src')?.[0]).toBe("'self'");
    expect(h['content-security-policy']).not.toMatch(/unsafe-(inline|eval)/);

    const tt = directivas(h['content-security-policy-report-only'] ?? '');
    expect(tt.get('require-trusted-types-for')).toEqual(["'script'"]);
    expect(h['reporting-endpoints']).toBe('csp="/api/v1/plataforma/csp-reportes"');
    expect(h['x-content-type-options']).toBe('nosniff');
    expect(h['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(h['permissions-policy']).toContain('microphone=(self)');
    expect(h['permissions-policy']).toContain('camera=()');
    expect(h['permissions-policy']).toContain('geolocation=()');
    expect(h['cross-origin-opener-policy']).toBe('same-origin');
    expect(h['cross-origin-resource-policy']).toBe('same-origin');
  });

  test('el manifiesto y los archivos con hash llevan las mismas cabeceras', async ({ request }) => {
    const raiz = await request.get('/');
    test.skip(!raiz.headers()['content-security-policy'], 'Sin cabeceras: corre contra la imagen nginx o vite preview');
    const html = await raiz.text();
    const script = /<script type="module" crossorigin src="([^"]+)"/.exec(html)?.[1];
    expect(script).toBeTruthy();
    for (const ruta of ['/manifest.webmanifest', script!]) {
      const h = (await request.get(ruta)).headers();
      expect(h['content-security-policy'], ruta).toBe(raiz.headers()['content-security-policy']);
      expect(h['x-content-type-options'], ruta).toBe('nosniff');
      expect(h['cross-origin-resource-policy'], ruta).toBe('same-origin');
    }
  });

  test('el visor de contratos: CSP propia con sandbox, solo enmarcable por Atlas, sin micrófono', async ({ request }) => {
    const respuesta = await request.get('/visor/openapi.html');
    const h = respuesta.headers();
    test.skip(!h['content-security-policy'], 'Sin cabeceras: corre contra la imagen nginx o vite preview');

    const csp = directivas(h['content-security-policy']!);
    expect(csp.get('default-src')).toEqual(["'none'"]);
    expect(csp.get('script-src')).toEqual(["'self'"]);
    expect(csp.get('frame-ancestors')).toEqual(["'self'"]);
    expect(csp.get('sandbox')).toEqual(['allow-scripts']);
    expect(csp.get('base-uri')).toEqual(["'none'"]);
    expect(csp.get('object-src')).toEqual(["'none'"]);
    expect(csp.get('report-uri')).toEqual(['/api/v1/plataforma/csp-reportes']);
    expect(h['content-security-policy']).not.toContain('unsafe-eval');
    expect(h['permissions-policy']).toContain('microphone=()');
    expect(h['x-content-type-options']).toBe('nosniff');
    expect(h['cross-origin-resource-policy']).toBe('cross-origin');
  });
});
