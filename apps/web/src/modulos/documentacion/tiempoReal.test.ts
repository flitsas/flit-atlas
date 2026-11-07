import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { esperaDeReintento, RUTA_HUB, useTiempoReal } from './tiempoReal';

type Oyente = (d: unknown) => void;

const hub = vi.hoisted(() => ({
  oyentes: new Map<string, (d: unknown) => void>(),
  invocar: vi.fn(async () => true),
  arrancar: vi.fn(async () => undefined),
  detener: vi.fn(async () => undefined),
  opciones: null as null | { headers?: Record<string, string> },
  url: '',
}));

vi.mock('@microsoft/signalr', () => {
  class Constructor {
    withUrl(url: string, opciones: { headers?: Record<string, string> }) {
      hub.url = url;
      hub.opciones = opciones;
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: (evento: string, oyente: Oyente) => hub.oyentes.set(evento, oyente),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: hub.arrancar,
        stop: hub.detener,
        invoke: hub.invocar,
      };
    }
  }
  return { HubConnectionBuilder: Constructor, LogLevel: { None: 6 } };
});

beforeEach(() => {
  hub.oyentes.clear();
  hub.invocar.mockClear().mockResolvedValue(true);
  hub.arrancar.mockClear().mockResolvedValue(undefined);
  hub.detener.mockClear();
});

afterEach(() => vi.restoreAllMocks());

describe('useTiempoReal (HU 5.8)', () => {
  it('entra al documento con la cabecera antifalsificación y muestra quién lo ve', async () => {
    const { result, unmount } = renderHook(() => useTiempoReal('d1', () => undefined));

    await waitFor(() => expect(hub.invocar).toHaveBeenCalledWith('Entrar', 'd1'));
    expect(hub.url).toBe(RUTA_HUB);
    expect(hub.opciones?.headers?.['X-Atlas-Csrf']).toBe('1');

    act(() => hub.oyentes.get('Presencia')!({ documentoId: 'd1', personas: [{ id: 'p1', nombre: 'Persona Demo' }] }));
    expect(result.current).toEqual([{ id: 'p1', nombre: 'Persona Demo' }]);

    // Lo de otro documento no se muestra.
    act(() => hub.oyentes.get('Presencia')!({ documentoId: 'd2', personas: [] }));
    expect(result.current).toHaveLength(1);

    unmount();
    await waitFor(() => expect(hub.detener).toHaveBeenCalled());
  });

  it('avisa de la versión nueva solo si es de este documento', async () => {
    const aviso = vi.fn();
    renderHook(() => useTiempoReal('d1', aviso));
    await waitFor(() => expect(hub.invocar).toHaveBeenCalled());

    act(() => hub.oyentes.get('VersionNueva')!({ documentoId: 'd2', numero: 9 }));
    expect(aviso).not.toHaveBeenCalled();
    act(() => hub.oyentes.get('VersionNueva')!({ documentoId: 'd1', numero: 3 }));
    expect(aviso).toHaveBeenCalledWith(3);
  });

  it('si el hub no responde, la página sigue igual y sin nadie', async () => {
    hub.arrancar.mockRejectedValue(new Error('sin hub'));
    const { result } = renderHook(() => useTiempoReal('d1', () => undefined));
    await waitFor(() => expect(hub.arrancar).toHaveBeenCalled());
    expect(result.current).toEqual([]);
    expect(hub.invocar).not.toHaveBeenCalled();
  });

  it('se aleja al reintentar y termina por rendirse', () => {
    expect(esperaDeReintento(0)).toBe(0);
    expect(esperaDeReintento(4)).toBe(30_000);
    expect(esperaDeReintento(11)).toBe(30_000);
    expect(esperaDeReintento(12)).toBeNull();
  });
});
