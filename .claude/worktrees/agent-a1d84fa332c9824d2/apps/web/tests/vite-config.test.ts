import { describe, expect, it } from 'vitest';

import { urlDeLaApi } from '../vite.config';

describe('configuración de Vite', () => {
  it('falla nombrando ATLAS_API_URL si no está definida', () => {
    expect(() => urlDeLaApi({})).toThrow(/ATLAS_API_URL/);
    expect(() => urlDeLaApi({ ATLAS_API_URL: '  ' })).toThrow(/ATLAS_API_URL/);
  });

  it('devuelve la URL de la API cuando existe', () => {
    expect(urlDeLaApi({ ATLAS_API_URL: 'http://localhost:4031' })).toBe('http://localhost:4031');
  });
});
