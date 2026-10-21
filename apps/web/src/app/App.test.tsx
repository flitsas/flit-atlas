import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App', () => {
  it('muestra la versión cuando la API responde por el mismo origen', async () => {
    const fetchSimulado = vi.fn().mockResolvedValue(new Response(JSON.stringify({ version: '1.0.0.0' })));
    vi.stubGlobal('fetch', fetchSimulado);

    render(<App />);

    expect(await screen.findByText('API conectada (versión 1.0.0.0).')).toBeInTheDocument();
    expect(fetchSimulado).toHaveBeenCalledWith('/api/v1/version', expect.objectContaining({ credentials: 'same-origin' }));
  });

  it('avisa cuando la API no responde', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })));

    render(<App />);

    expect(await screen.findByText('No se pudo conectar con la API.')).toBeInTheDocument();
  });
});
