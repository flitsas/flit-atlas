import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router';

import { elementosDeVista, textoMotivo } from './api';
import { moduloArquitectura } from './modulo';
import Drift from './paginas/Drift';
import Inicio from './paginas/Inicio';
import { esMensajeModelo, esMensajeVisorLikeC4 } from './visor/mensajes';
import { VisorLikeC4 } from './VisorLikeC4';

vi.mock('../../app/sesion', () => ({ useSesion: () => ({ acceso: { permisos: ['atlas.arquitectura.ver'] } }) }));
vi.mock('../../app/tema', () => ({ useTema: () => ({ efectivo: 'claro' }) }));

const aqui = dirname(fileURLToPath(import.meta.url));
const MODELO = JSON.parse(readFileSync(join(aqui, '../../../../../tools/arquitectura-gen/fixtures/modelo-demo.export.json'), 'utf8'));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const simular = (rutas: Record<string, unknown>) =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      const clave = Object.keys(rutas).find((r) => String(url).includes(r));
      return clave ? new Response(JSON.stringify(rutas[clave]), { status: 200 }) : new Response('{}', { status: 404 });
    }),
  );

const montar = (nodo: React.ReactNode, ruta = '/') =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[ruta]}>
        <Routes>
          <Route path="*" element={nodo} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe('manifiesto', () => {
  it('tiene su bandera, encendida como en el backend, y la entrada es solo para empleados', () => {
    expect(moduloArquitectura.bandera).toBe('modulo.arquitectura');
    expect(moduloArquitectura.banderaPorDefecto).toBe(true);
    expect(moduloArquitectura.navegacion?.[0]?.audiencia).toBe('empleados');
    expect(moduloArquitectura.navegacion?.[0]?.movil).toBe('mas');
  });

  it('despliegue y drift exigen ver_despliegue; lo demás, ver', () => {
    const permiso = (path: string) => moduloArquitectura.rutas?.find((r) => r.path === path)?.permiso;
    expect(permiso('/arquitectura')).toBe('atlas.arquitectura.ver');
    expect(permiso('/arquitectura/proyectos/:proyecto')).toBe('atlas.arquitectura.ver');
    expect(permiso('/arquitectura/despliegue')).toBe('atlas.arquitectura.ver_despliegue');
    expect(permiso('/arquitectura/drift')).toBe('atlas.arquitectura.ver_despliegue');
  });
});

describe('mensajes del visor', () => {
  it('solo acepta ids razonables y el modelo con su forma', () => {
    expect(esMensajeVisorLikeC4({ tipo: 'atlas:likec4-elemento', id: 'demo.web' })).toBe(true);
    expect(esMensajeVisorLikeC4({ tipo: 'atlas:likec4-elemento', id: 'x'.repeat(301) })).toBe(false);
    expect(esMensajeVisorLikeC4({ tipo: 'atlas:likec4-elemento', id: 5 })).toBe(false);
    expect(esMensajeVisorLikeC4({ tipo: 'otro' })).toBe(false);
    expect(esMensajeVisorLikeC4(null)).toBe(false);
    expect(esMensajeModelo({ tipo: 'atlas:likec4-modelo', clave: 'a', modelo: {}, vista: 'v', oscuro: false })).toBe(true);
    expect(esMensajeModelo({ tipo: 'atlas:likec4-modelo', clave: 'a', modelo: null, vista: 'v', oscuro: false })).toBe(false);
  });
});

describe('elementos de una vista', () => {
  it('lista los de la salida real de LikeC4, sin repetir y ordenados', () => {
    const lista = elementosDeVista(MODELO, 'demo_contenedores');
    expect(lista.map((e) => e.id)).toContain('demo.web');
    expect(new Set(lista.map((e) => e.id)).size).toBe(lista.length);
    expect(elementosDeVista(MODELO, 'no-existe')).toEqual([]);
    expect(elementosDeVista(undefined, 'x')).toEqual([]);
  });
});

describe('VisorLikeC4', () => {
  it('va en un iframe aislado, sin allow-same-origin', () => {
    montar(<VisorLikeC4 clave="c" modelo={MODELO} vista="landscape" titulo="Landscape" alTocarElemento={() => {}} alNavegar={() => {}} />);
    const marco = screen.getByTitle('Vista de arquitectura: Landscape');
    expect(marco.getAttribute('sandbox')).toBe('allow-scripts');
    expect(marco.getAttribute('src')).toBe('/visor/likec4.html');
  });

  it('ignora los mensajes que no vienen de su iframe', () => {
    const tocar = vi.fn();
    montar(<VisorLikeC4 clave="c" modelo={MODELO} vista="landscape" titulo="L" alTocarElemento={tocar} alNavegar={() => {}} />);
    window.dispatchEvent(new MessageEvent('message', { data: { tipo: 'atlas:likec4-elemento', id: 'demo.web' }, source: window }));
    expect(tocar).not.toHaveBeenCalled();
  });
});

describe('Inicio', () => {
  it('dice que no hay nada cuando el servidor responde vacío, como a un externo', async () => {
    simular({ '/api/v1/arquitectura/proyectos': [] });
    montar(<Inicio />);
    expect(await screen.findByText(/no hay modelos publicados/)).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Panel de drift' })).toBeNull();
  });
});

describe('Drift', () => {
  const ambiente = {
    ambiente: 'qa',
    modelo: { estado: 'con_datos', motivo: null },
    gitops: { estado: 'con_datos', motivo: null },
    argo: { estado: 'sin_datos', motivo: 'sin_configurar' },
    clusteres: [],
    modeloGitops: 'sin_drift',
    gitopsArgo: 'sin_datos',
  };

  it('sin Argo configurado dice «Sin datos» y no «Sin drift» en esa comparación', async () => {
    simular({ '/api/v1/arquitectura/drift': { ambientes: [ambiente], hallazgos: [] } });
    montar(<Drift />, '/?ambiente=qa');
    expect(await screen.findByText(/la lectura de Argo CD no está configurada/)).toBeTruthy();
    const gitopsArgo = screen.getByText('Gitops y Argo CD:').closest('div')!;
    expect(gitopsArgo.textContent).toContain('Sin datos');
    expect(gitopsArgo.textContent).not.toContain('Sin drift');
    expect(screen.getByText('Modelo y gitops:').closest('div')!.textContent).toContain('Sin drift');
    expect(screen.getByText(/en lo que se pudo comparar/)).toBeTruthy();
  });

  it('muestra los hallazgos con su severidad y deja ver los resueltos', async () => {
    const hallazgo = {
      id: 'h1', capa: 'gitops_argo', ambiente: 'qa', cluster: 'cluster-demo', codigo: 'sin_sincronizar', elementoId: null, sujeto: 'demo-qa',
      descripcion: 'La Application «demo-qa» no está sincronizada (OutOfSync).', severidad: 'alta', estado: 'abierto', detectadoEn: '2026-11-06T12:00:00Z', resueltoEn: null,
    };
    simular({ 'estado=abierto': { ambientes: [{ ...ambiente, gitopsArgo: 'con_drift' }], hallazgos: [hallazgo] }, 'estado=resuelto': { ambientes: [ambiente], hallazgos: [] } });
    montar(<Drift />);
    expect(await screen.findByText(/no está sincronizada/)).toBeTruthy();
    expect(screen.getByText('Alta')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Resueltos' }));
    await waitFor(() => expect(screen.getByText('No hay hallazgos resueltos.')).toBeTruthy());
  });
});

describe('textoMotivo', () => {
  it('traduce los códigos y un estado http', () => {
    expect(textoMotivo('http_401')).toBe('Argo CD respondió 401');
    expect(textoMotivo('sin_respuesta')).toBe('Argo CD no respondió');
    expect(textoMotivo(null)).toBe('no hay datos');
  });
});
