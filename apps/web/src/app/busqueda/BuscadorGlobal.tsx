import { CornerDownLeft, Search } from 'lucide-react';
import { type KeyboardEvent, useId, useMemo, useState } from 'react';
import { useNavigate } from 'react-router';

import { useSesion } from '../sesion';
import { useTema } from '../tema';
import { Icono } from '../ui/Icono';
import { cn } from '../ui/Tarjeta';
import { buscar, construirIndice, type GrupoResultado, proveedoresDeModulos, type Resultado } from './indice';

/**
 * Buscador global con el patrón combobox de ARIA 1.2: el foco se queda en el campo y las flechas recorren los
 * resultados. Lo usan la paleta ⌘K de escritorio y la página Buscar del móvil.
 */
export function BuscadorGlobal({ autoFoco = false, alElegir }: { autoFoco?: boolean; alElegir?: () => void }) {
  const { navegacion, modulos, cerrarSesion } = useSesion();
  const { cambiar } = useTema();
  const navegar = useNavigate();
  const id = useId();
  const [consulta, setConsulta] = useState('');
  const [activo, setActivo] = useState(0);

  const indice = useMemo(() => construirIndice(navegacion), [navegacion]);
  const resultados = useMemo(() => buscar(indice, consulta), [indice, consulta]);
  const grupos = useMemo(() => {
    const mapa = new Map<GrupoResultado, Resultado[]>();
    for (const r of resultados) mapa.set(r.grupo, [...(mapa.get(r.grupo) ?? []), r]);
    return [...mapa.entries()];
  }, [resultados]);
  const proveedores = useMemo(() => proveedoresDeModulos(modulos), [modulos]);
  const seleccionado = Math.min(activo, Math.max(resultados.length - 1, 0));

  function ejecutar(resultado: Resultado) {
    alElegir?.();
    if ('ruta' in resultado.destino) {
      void navegar(resultado.destino.ruta);
      return;
    }
    if (resultado.destino.accion === 'tema_claro') cambiar('claro');
    if (resultado.destino.accion === 'tema_oscuro') cambiar('oscuro');
    if (resultado.destino.accion === 'cerrar_sesion') void cerrarSesion();
  }

  function alPresionar(evento: KeyboardEvent<HTMLInputElement>) {
    // En un campo de búsqueda, Chromium usa Escape para vaciarlo; en la paleta, Escape la cierra de una vez.
    if (evento.key === 'Escape' && alElegir) {
      evento.preventDefault();
      alElegir();
      return;
    }
    const total = resultados.length;
    if (total === 0) return;
    const mover: Record<string, number> = { ArrowDown: seleccionado + 1, ArrowUp: seleccionado - 1, Home: 0, End: total - 1 };
    if (evento.key in mover) {
      evento.preventDefault();
      setActivo(((mover[evento.key]! % total) + total) % total);
    } else if (evento.key === 'Enter') {
      evento.preventDefault();
      const elegido = resultados[seleccionado];
      if (elegido) ejecutar(elegido);
    }
  }

  const idOpcion = (r: Resultado) => `${id}-${r.id.replace(/[^a-z0-9-]/gi, '-')}`;

  return (
    <div className="flex flex-col">
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2 text-atlas-texto-suave" aria-hidden="true" />
        <input
          type="search"
          role="combobox"
          aria-label="Buscar en Atlas"
          aria-expanded={resultados.length > 0}
          aria-controls={`${id}-lista`}
          aria-autocomplete="list"
          aria-activedescendant={resultados[seleccionado] ? idOpcion(resultados[seleccionado]) : undefined}
          // La paleta abre para escribir.
          autoFocus={autoFoco}
          autoComplete="off"
          enterKeyHint="search"
          placeholder="Busca módulos, páginas o acciones"
          value={consulta}
          onChange={(e) => {
            setConsulta(e.target.value);
            setActivo(0);
          }}
          onKeyDown={alPresionar}
          className="min-h-12 w-full rounded-xl border border-border bg-card pr-4 pl-12 text-base outline-none placeholder:text-atlas-texto-suave focus-visible:border-atlas-tinta"
        />
      </div>

      <div id={`${id}-lista`} role="listbox" aria-label="Resultados" className="mt-3 flex flex-col gap-3">
        {grupos.map(([grupo, lista]) => (
          <div key={grupo} role="group" aria-labelledby={`${id}-${grupo}`}>
            <p id={`${id}-${grupo}`} className="px-2 pb-1 text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">
              {grupo}
            </p>
            {lista.map((r) => {
              const esActivo = resultados[seleccionado] === r;
              return (
                <div
                  key={r.id}
                  id={idOpcion(r)}
                  role="option"
                  aria-selected={esActivo}
                  onClick={() => ejecutar(r)}
                  onMouseMove={() => setActivo(resultados.indexOf(r))}
                  className={cn(
                    'flex min-h-toque cursor-pointer items-center gap-3 rounded-xl px-2 py-1.5',
                    esActivo && 'bg-atlas-activo',
                  )}
                >
                  <span className="grid size-9 shrink-0 place-items-center rounded-lg border border-border bg-card text-atlas-tinta">
                    <Icono nombre={r.icono} className="size-4.5" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium">{r.titulo}</span>
                    {r.descripcion && <span className="block truncate text-xs text-atlas-texto-suave">{r.descripcion}</span>}
                  </span>
                  {esActivo && <CornerDownLeft className="hidden size-4 text-atlas-texto-suave md:block" aria-hidden="true" />}
                </div>
              );
            })}
          </div>
        ))}
        {resultados.length === 0 && (
          <p role="status" className="px-2 py-6 text-center text-sm text-atlas-texto-suave">
            Sin resultados para «{consulta}».
          </p>
        )}
      </div>

      {proveedores.length > 0 && (
        <p className="mt-4 border-t border-border px-2 pt-3 text-xs text-atlas-texto-suave">
          Muy pronto también vas a encontrar aquí{' '}
          {proveedores
            .flatMap((p) => p.tipos)
            .filter((t, i, todos) => todos.indexOf(t) === i)
            .map((t) => TIPOS[t] ?? t)
            .join(', ')}
          , según tus permisos.
        </p>
      )}
    </div>
  );
}

const TIPOS: Record<string, string> = {
  aplicativo: 'aplicativos',
  modulo: 'módulos',
  servicio: 'servicios',
  persona: 'personas',
  responsabilidad: 'responsabilidades',
  turno: 'turnos',
  contrato: 'contratos',
  operacion: 'operaciones',
  documento: 'documentos',
};
