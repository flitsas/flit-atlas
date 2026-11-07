import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { banderaActiva } from '../../app/modulos/resolver';
import { markdownSeguro } from './Markdown';
import { moduloGia } from './modulo';
import Caso from './paginas/Caso';
import Politica from './paginas/Politica';
import Politicas from './paginas/Politicas';
import Roles from './paginas/Roles';
import Tablero from './paginas/Tablero';

const GESTIONA = ['atlas.gia.ver', 'atlas.gia.gestionar'];

const sesion = vi.hoisted(() => ({ permisos: [] as string[] }));
vi.mock('../../app/sesion', () => ({ useSesion: () => ({ acceso: { permisos: sesion.permisos } }) }));

function conProveedores(ui: React.ReactNode, ruta: string, patron: string, permisos = GESTIONA) {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  sesion.permisos = permisos;
  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[ruta]}>
        <Routes>
          <Route path={patron} element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function api(rutas: Record<string, [number, unknown]>) {
  const llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (entrada: string, opciones?: RequestInit) => {
      llamadas.push({ ruta: entrada, metodo: opciones?.method ?? 'GET', cuerpo: opciones?.body ? JSON.parse(opciones.body as string) : undefined });
      const r = rutas[`${opciones?.method ?? 'GET'} ${entrada}`] ?? rutas[entrada];
      return new Response(JSON.stringify(r?.[1] ?? {}), { status: r?.[0] ?? 404, headers: { 'Content-Type': 'application/json' } });
    }),
  );
  return llamadas;
}

afterEach(() => vi.unstubAllGlobals());

const FICHA = {
  nombre: 'OCR de prueba', aplicacion: 'App', aplicativoId: null, repo: null, duenoPersonaId: null, ambientes: [], proposito: 'Probar', tipo: 'ocr', usuariosAfectados: [], decisionSobrePersonas: null,
  decisionDescripcion: null, humanoEnBucle: null, categoriasDato: ['personal'], tiposPii: [], volumenMensualEstimado: null, baseLegal: null, baseLegalRef: null, minimizacion: null, presidioActivo: null,
  presidioEntidades: [], aliasUsados: ['flit-ocr'], proveedorActual: null, transferenciaInternacional: null, metricasCalidad: null, evalsRuta: null, promptsUbicacion: null, controles: null, avisoUsuario: null, planRollback: null, enlaces: [],
};
const detalleCaso = (estado: string) => ({
  caso: { id: 'c1', codigo: 'ocr-prueba', nombre: 'OCR de prueba', aplicacion: 'App', estado, nivelRiesgo: null, riesgoPreliminar: 'alto', dueno: null, proximaRevision: null, revisionVencida: false },
  ficha: FICHA,
  detallePlan: '**CU-01.** Texto del plan **[por verificar]**.',
  versionActual: 1,
  aprobadoPor: null,
  aprobadoEn: null,
  faltantes: ['dueño del caso'],
  puedeGestionar: true,
});
const EVALUACION = {
  id: 'e1', version: 1, dimensiones: [{ dimension: 'D1', escenario: 'Se expone', p: 3, i: 5, controlesExistentes: null, controlesNuevos: null, p2: 1, i2: 5 }], puntajeInherente: 15, nivelInherente: 'alto', puntajeResidual: 5, nivelResidual: 'medio',
  principios: {}, transferenciaInternacional: null, decision: 'pendiente', condiciones: null, evaluador: { id: 'p1', nombre: 'Persona Demo' }, evaluadaEn: '2026-11-06T12:00:00Z', decididaEn: null,
  rolesRequeridos: ['comite'], requisitos: ['evaluación de impacto en privacidad aprobada'], votos: [],
};

describe('gobierno de IA (ATLAS-ADR-0055)', () => {
  it('viene apagado: sin la bandera no aparece', () => {
    expect(moduloGia.banderaPorDefecto).toBe(false);
    expect(banderaActiva(moduloGia, {})).toBe(false);
    expect(banderaActiva(moduloGia, { 'modulo.gia': true })).toBe(true);
    expect(moduloGia.navegacion?.[0]?.audiencia).toBe('empleados');
  });

  it('todas las rutas exigen un permiso de atlas.gia y escribir exige gestionar', () => {
    expect(moduloGia.rutas.every((r) => r.permiso?.startsWith('atlas.gia.'))).toBe(true);
    for (const ruta of ['/gobierno-ia/casos/nuevo', '/gobierno-ia/casos/:id/editar', '/gobierno-ia/casos/:id/evaluar']) {
      expect(moduloGia.rutas.find((r) => r.path === ruta)?.permiso).toBe('atlas.gia.gestionar');
    }
  });

  it('quien solo lee políticas no ve las demás secciones', async () => {
    api({ '/api/v1/gia/politicas': [200, []] });
    conProveedores(<Politicas />, '/gobierno-ia/politicas', '/gobierno-ia/politicas', ['atlas.gia.politicas.ver']);

    const secciones = await screen.findByRole('navigation', { name: 'Secciones de gobierno de IA' });
    expect(within(secciones).getAllByRole('link').map((e) => e.textContent)).toEqual(['Políticas']);
    expect(moduloGia.rutas.find((r) => r.path === '/gobierno-ia')?.permiso).toBe('atlas.gia.ver');
    expect(moduloGia.rutas.find((r) => r.path === '/gobierno-ia/politicas/:id')?.permiso).toBe('atlas.gia.politicas.ver');
  });

  it('el tablero dice «Sin datos» en lo que no se puede medir, no cero', async () => {
    api({
      '/api/v1/gia/tablero': [200, {
        casosPorEstado: [{ clave: 'borrador', total: 13 }],
        casosPorRiesgo: [{ clave: 'alto', total: 5 }],
        revisionesVencidas: 0,
        kpis: [
          { codigo: 'costo', nombre: 'Desviación del costo', disponible: false, valor: null, total: null, meta: '≤ 10 %', nota: 'Sin datos hasta tener el gateway.' },
          { codigo: 'casos_aprobados', nombre: 'Casos con ficha aprobada', disponible: true, valor: 0, total: 13, meta: '100 %', nota: null },
        ],
      }],
    });
    conProveedores(<Tablero />, '/gobierno-ia', '/gobierno-ia');

    const costo = (await screen.findByText('Desviación del costo')).closest('li')!;
    expect(within(costo).getByText('Sin datos')).toBeInTheDocument();
    expect(within(costo).getByText('Sin datos hasta tener el gateway.')).toBeInTheDocument();
    expect(screen.getByText('0 de 13')).toBeInTheDocument();
    expect(screen.getByText('Borrador: 13')).toBeInTheDocument();
  });

  it('el error de enviar a revisión muestra los campos que faltan', async () => {
    const llamadas = api({
      '/api/v1/gia/casos/c1': [200, detalleCaso('borrador')],
      '/api/v1/gia/casos/c1/evaluaciones': [200, []],
      'POST /api/v1/gia/casos/c1/envio-revision': [422, { codigo: 'ficha_incompleta', mensaje: 'Faltan campos obligatorios de la ficha.', detalle: ['tipo', 'ambientes habilitados'] }],
    });
    conProveedores(<Caso />, '/gobierno-ia/casos/c1', '/gobierno-ia/casos/:id');

    fireEvent.click(await screen.findByRole('button', { name: 'Enviar a revisión' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Faltan campos obligatorios de la ficha.');
    expect(screen.getByText('ambientes habilitados')).toBeInTheDocument();
    expect(llamadas.some((l) => l.metodo === 'POST' && l.ruta.endsWith('/envio-revision'))).toBe(true);
  });

  it('el caso en revisión deja votar con el rol que pide el nivel y muestra lo que falta', async () => {
    const llamadas = api({
      '/api/v1/gia/casos/c1': [200, detalleCaso('en_revision')],
      '/api/v1/gia/casos/c1/evaluaciones': [200, [EVALUACION]],
      'POST /api/v1/gia/casos/c1/evaluaciones/1/votos': [422, { codigo: 'requisitos_pendientes', mensaje: 'Faltan requisitos del nivel.', detalle: ['plan de rollback'] }],
    });
    conProveedores(<Caso />, '/gobierno-ia/casos/c1', '/gobierno-ia/casos/:id');

    expect(await screen.findByText(/Deciden: Comité de IA/)).toBeInTheDocument();
    expect(screen.getByText('evaluación de impacto en privacidad aprobada')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Aprobar' }));

    await waitFor(() => expect(llamadas.find((l) => l.ruta.endsWith('/votos'))?.cuerpo).toMatchObject({ rol: 'comite', voto: 'aprobar' }));
    expect(await screen.findByText('plan de rollback')).toBeInTheDocument();
  });

  it('quien solo lee no ve las acciones', async () => {
    api({
      '/api/v1/gia/casos/c1': [200, detalleCaso('en_revision')],
      '/api/v1/gia/casos/c1/evaluaciones': [200, [EVALUACION]],
    });
    conProveedores(<Caso />, '/gobierno-ia/casos/c1', '/gobierno-ia/casos/:id', ['atlas.gia.ver']);

    expect(await screen.findByText(/Deciden: Comité de IA/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar la EIPD' })).not.toBeInTheDocument();
  });

  it('una política pendiente avisa que no tiene vigencia y no deja publicar ni aceptar', async () => {
    api({
      '/api/v1/gia/politicas/p1': [200, {
        politica: { id: 'p1', codigo: 'POL-SGSI-IA-01', version: '0.1', titulo: 'Uso aceptable', estado: 'borrador', requiereAceptacion: true, vigenteDesde: null, pendienteDeRedaccion: true, aceptadaPorMi: false },
        contenido: '> [PENDIENTE DE REDACCIÓN]',
        fuenteGit: null,
        aprobadaPor: null,
      }],
    });
    conProveedores(<Politica />, '/gobierno-ia/politicas/p1', '/gobierno-ia/politicas/:id');

    expect(await screen.findByText(/no está redactada ni aprobada/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publicar como Comité' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Leí y acepto' })).not.toBeInTheDocument();
  });

  it('sin roles de gobierno avisa que nadie puede aprobar', async () => {
    api({ '/api/v1/gia/roles-gobierno': [200, []] });
    conProveedores(<Roles />, '/gobierno-ia/roles', '/gobierno-ia/roles');

    expect(await screen.findByText(/ningún caso se puede aprobar/)).toBeInTheDocument();
  });

  it('el Markdown de las fichas no interpreta HTML ni deja enlaces inseguros', () => {
    const html = markdownSeguro('<script>alert(1)</script>\n\n[x](javascript:alert(1))\n\n[y](https://ejemplo.com)');

    expect(html).not.toContain('<script');
    expect(html).not.toMatch(/href="javascript/i);
    expect(html).toContain('href="https://ejemplo.com"');
  });
});
