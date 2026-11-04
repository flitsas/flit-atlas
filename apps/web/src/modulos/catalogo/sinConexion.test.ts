import { describe, expect, it } from 'vitest';

import { type AplicativoResumen, filtrarListado } from './api';

/** HU 2.12: sin conexión, los filtros del catálogo se aplican sobre el listado guardado. */
const app = (slug: string, nombre: string, extra: Partial<AplicativoResumen> = {}): AplicativoResumen => ({
  id: slug,
  slug,
  nombre,
  tipo: 'producto',
  grupo: 'flito',
  estado: 'activo',
  criticidad: 'alta',
  manejaDatosPersonales: false,
  padreId: null,
  descripcion: null,
  tecnologias: [],
  etiquetas: [],
  ancestros: [],
  hijos: 0,
  ...extra,
});

describe('catálogo sin conexión', () => {
  it('filtra por grupo, estado, criticidad y tipo, y busca sin tildes ni mayúsculas', () => {
    const listado = {
      total: 3,
      elementos: [
        app('flito', 'FLITO'),
        app('vigia', 'Vigía', { grupo: 'satelite', criticidad: 'media' }),
        app('back-demo', 'Back Demo', { estado: 'en_retiro', grupo: 'legado' }),
      ],
    };
    expect(filtrarListado(listado, { grupo: 'flito' }).elementos.map((a) => a.slug)).toEqual(['flito']);
    expect(filtrarListado(listado, { texto: 'VIGIA' }).elementos.map((a) => a.slug)).toEqual(['vigia']);
    expect(filtrarListado(listado, { estado: 'en_retiro' }).total).toBe(1);
    expect(filtrarListado(listado, { criticidad: 'media', texto: 'flito' }).total).toBe(0);
  });
});
