import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';

import { ErrorApi, pedir } from '../../../app/api';
import { useEnLinea } from '../../../pwa/conexion';

/**
 * Componentes vivos del MDX (ATLAS-ADR-0053 §5). Cada uno pide sus datos con la sesión de quien lee, por la API: incrustar
 * uno no da permiso para ver lo que muestra. Los atributos los escribe el autor del documento: solo se aceptan códigos en
 * minúsculas, para que no puedan cambiar la ruta que se pide.
 */
const CODIGO = /^[a-z0-9][a-z0-9-]{0,99}$/;

function Caja({ etiqueta, children }: { etiqueta: string; children: React.ReactNode }) {
  return (
    <aside aria-label={etiqueta} className="my-3 rounded-xl border border-border bg-card p-3 text-sm">
      {children}
    </aside>
  );
}

function SinAcceso({ etiqueta }: { etiqueta: string }) {
  const enLinea = useEnLinea();
  return (
    <Caja etiqueta={etiqueta}>
      <p className="text-atlas-texto-suave">{enLinea ? 'No tienes acceso a este contenido.' : 'Este dato se muestra cuando vuelva la conexión.'}</p>
    </Caja>
  );
}

function Invalido({ nombre }: { nombre: string }) {
  return (
    <Caja etiqueta={nombre}>
      <p className="text-atlas-texto-suave">Este componente «{nombre}» está mal escrito en el documento.</p>
    </Caja>
  );
}

/** Sin permiso (403) o sin el recurso (404): lo mismo para quien lee, para no revelar qué existe. */
const reintentar = (veces: number, error: unknown) => !(error instanceof ErrorApi && [401, 403, 404].includes(error.estado)) && veces < 1;

function useVivo<T>(clave: unknown[], ruta: string, activo = true) {
  return useQuery({
    queryKey: ['documentacion', 'vivo', ...clave],
    queryFn: async () => (await (await pedir(ruta)).json()) as T,
    enabled: activo,
    retry: reintentar,
    staleTime: 60_000,
    // Sin red debe fallar y no quedarse «cargando»: el documento guardado sigue siendo legible.
    networkMode: 'always',
  });
}

function Cargando({ etiqueta }: { etiqueta: string }) {
  return <div role="status" aria-label={`Cargando: ${etiqueta}`} className="my-3 h-16 animate-pulse rounded-xl bg-atlas-activo" />;
}

interface ContratoListado {
  id: string;
  codigo: string;
  nombre: string;
  ultimaVersion: { version: string } | null;
}

export function VivoContrato({ id, operacion }: { id?: string; operacion?: string }) {
  const valido = !!id && CODIGO.test(id);
  const consulta = useVivo<{ elementos: ContratoListado[] }>(['contrato', id], `/api/v1/contratos?texto=${encodeURIComponent(id ?? '')}`, valido);
  if (!valido) return <Invalido nombre="Contrato" />;
  const etiqueta = `Contrato ${id}`;
  if (consulta.isPending) return <Cargando etiqueta={etiqueta} />;
  const contrato = consulta.data?.elementos.find((c) => c.codigo === id);
  if (consulta.isError || !contrato) return <SinAcceso etiqueta={etiqueta} />;
  return (
    <Caja etiqueta={etiqueta}>
      <Link to={`/contratos/${encodeURIComponent(contrato.id)}`} className="font-medium text-atlas-tinta underline">
        {contrato.nombre}
      </Link>
      {contrato.ultimaVersion && <span className="ml-2 text-atlas-texto-suave">versión {contrato.ultimaVersion.version}</span>}
      {operacion && <p className="mt-1 break-words font-mono text-xs">{operacion}</p>}
    </Caja>
  );
}

interface ResponsablesAplicativo {
  aplicativo: { nombre: string };
  principal: { persona: { nombre: string } } | null;
  secundario: { persona: { nombre: string } } | null;
}

export function VivoResponsables({ aplicativo }: { aplicativo?: string }) {
  const valido = !!aplicativo && CODIGO.test(aplicativo);
  const consulta = useVivo<ResponsablesAplicativo>(['responsables', aplicativo], `/api/v1/equipo/aplicativos/${encodeURIComponent(aplicativo ?? '')}/responsables`, valido);
  if (!valido) return <Invalido nombre="Responsables" />;
  const etiqueta = `Responsables de ${aplicativo}`;
  if (consulta.isPending) return <Cargando etiqueta={etiqueta} />;
  if (consulta.isError) return <SinAcceso etiqueta={etiqueta} />;
  const d = consulta.data;
  return (
    <Caja etiqueta={etiqueta}>
      <p className="font-medium">Responsables de {d.aplicativo.nombre}</p>
      <p>Principal: {d.principal?.persona.nombre ?? 'sin asignar'}</p>
      <p>Secundario: {d.secundario?.persona.nombre ?? 'sin asignar'}</p>
    </Caja>
  );
}

interface EsteSabado {
  fecha: string;
  equipos: { equipo: { id: string; nombre: string }; turnos: { id: string; persona: { nombre: string } }[] }[];
}

export function VivoSoporteDeTurno() {
  const etiqueta = 'Soporte de turno';
  const consulta = useVivo<EsteSabado>(['soporte'], '/api/v1/soporte/este-sabado');
  if (consulta.isPending) return <Cargando etiqueta={etiqueta} />;
  if (consulta.isError) return <SinAcceso etiqueta={etiqueta} />;
  return (
    <Caja etiqueta={etiqueta}>
      <p className="font-medium">Soporte del sábado {consulta.data.fecha.split('-').reverse().join('/')}</p>
      {consulta.data.equipos.length === 0 && <p className="text-atlas-texto-suave">No hay equipos de soporte activos.</p>}
      {consulta.data.equipos.map((e) => (
        <p key={e.equipo.id}>
          {e.equipo.nombre}: {e.turnos.map((t) => t.persona.nombre).join(', ') || 'sin turno'}
        </p>
      ))}
    </Caja>
  );
}

/** El módulo de diagramas llega en F6; hasta entonces el documento sigue siendo legible. */
export function VivoDiagrama({ id }: { id?: string }) {
  return (
    <Caja etiqueta={`Diagrama ${id ?? ''}`.trim()}>
      <p className="text-atlas-texto-suave">Este diagrama{id ? ` (${id})` : ''} se verá aquí cuando llegue el módulo de diagramas.</p>
    </Caja>
  );
}
