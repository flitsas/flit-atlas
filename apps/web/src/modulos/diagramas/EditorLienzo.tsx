import '@xyflow/react/dist/style.css';

import {
  addEdge,
  Background,
  type Connection,
  Controls,
  type Edge,
  type EdgeChange,
  applyEdgeChanges,
  applyNodeChanges,
  type Node,
  type NodeChange,
  ReactFlow,
} from '@xyflow/react';
import { useCallback, useEffect, useState } from 'react';

import { ALTO_NODO, ANCHO_NODO, type Lienzo, leerLienzo, TEXTO_TIPO_NODO, type TipoNodo } from '../../app/diagramas/lienzo';
import { useTema } from '../../app/tema';
import { Boton } from '../../app/ui/Tarjeta';
import type { AplicativoRef } from './api';
import { claseCampo, Opciones } from './componentes';

interface DatosNodo extends Record<string, unknown> {
  label: string;
  tipo: TipoNodo;
  aplicativoId?: string;
  elementoLikeC4?: string;
}

const TIPOS = Object.keys(TEXTO_TIPO_NODO) as TipoNodo[];

function aFlujo(lienzo: Lienzo): { nodos: Node<DatosNodo>[]; relaciones: Edge[] } {
  return {
    nodos: lienzo.nodos.map((n) => ({
      id: n.id,
      position: { x: n.x, y: n.y },
      width: ANCHO_NODO,
      height: ALTO_NODO,
      data: { label: n.etiqueta, tipo: n.tipo, aplicativoId: n.aplicativoId, elementoLikeC4: n.elementoLikeC4 },
    })),
    relaciones: lienzo.relaciones.map((r) => ({ id: r.id, source: r.origen, target: r.destino, label: r.etiqueta })),
  };
}

function aLienzo(nodos: Node<DatosNodo>[], relaciones: Edge[]): Lienzo {
  return {
    nodos: nodos.map((n) => ({
      id: n.id,
      tipo: n.data.tipo,
      etiqueta: n.data.label,
      x: n.position.x,
      y: n.position.y,
      ...(n.data.aplicativoId ? { aplicativoId: n.data.aplicativoId } : {}),
      ...(n.data.elementoLikeC4 ? { elementoLikeC4: n.data.elementoLikeC4 } : {}),
    })),
    relaciones: relaciones.map((r) => ({ id: r.id, origen: r.source, destino: r.target, ...(typeof r.label === 'string' && r.label ? { etiqueta: r.label } : {}) })),
  };
}

function idNuevo(prefijo: string, usados: { id: string }[]): string {
  for (let i = usados.length + 1; ; i += 1) {
    const candidato = `${prefijo}-${i}`;
    if (!usados.some((u) => u.id === candidato)) return candidato;
  }
}

/**
 * Lienzo editable (HU 6.6, ATLAS-ADR-0054 §5) con React Flow en el shell: sus estilos son un `.css` estático y la posición va
 * por CSSOM, así que la CSP estricta no cambia. ELK se descarga solo al pulsar «Acomodar». Todo lo del lienzo también se puede
 * hacer con el panel, sin arrastrar: es lo que se usa en el móvil y con teclado.
 */
export function EditorLienzo({ fuente, aplicativos, alCambiar }: { fuente: string; aplicativos: AplicativoRef[]; alCambiar: (fuente: string) => void }) {
  const [inicial] = useState(() => aFlujo(leerLienzo(fuente) ?? { nodos: [], relaciones: [] }));
  const [nodos, setNodos] = useState(inicial.nodos);
  const [relaciones, setRelaciones] = useState(inicial.relaciones);
  const [seleccion, setSeleccion] = useState<string | null>(null);
  const [acomodando, setAcomodando] = useState(false);
  const [errorAcomodo, setErrorAcomodo] = useState(false);
  const [buscar, setBuscar] = useState('');
  const { efectivo } = useTema();

  useEffect(() => {
    alCambiar(JSON.stringify(aLienzo(nodos, relaciones)));
  }, [nodos, relaciones, alCambiar]);

  const alCambiarNodos = useCallback((c: NodeChange<Node<DatosNodo>>[]) => setNodos((n) => applyNodeChanges(c, n)), []);
  const alCambiarRelaciones = useCallback((c: EdgeChange[]) => setRelaciones((r) => applyEdgeChanges(c, r)), []);
  const conectar = useCallback(
    (c: Connection) => setRelaciones((r) => addEdge({ ...c, id: idNuevo('relacion', r) }, r)),
    [],
  );

  const elegido = nodos.find((n) => n.id === seleccion);
  const editar = (cambios: Partial<DatosNodo>) => setNodos((lista) => lista.map((n) => (n.id === seleccion ? { ...n, data: { ...n.data, ...cambios } } : n)));

  const agregar = () => {
    const id = idNuevo('nodo', nodos);
    const siguiente = nodos.length;
    setNodos((lista) => [
      ...lista,
      { id, position: { x: (siguiente % 4) * (ANCHO_NODO + 40), y: Math.floor(siguiente / 4) * (ALTO_NODO + 60) }, width: ANCHO_NODO, height: ALTO_NODO, data: { label: 'Nuevo nodo', tipo: 'sistema' } },
    ]);
    setSeleccion(id);
  };

  const quitar = () => {
    if (!seleccion) return;
    setNodos((lista) => lista.filter((n) => n.id !== seleccion));
    setRelaciones((lista) => lista.filter((r) => r.source !== seleccion && r.target !== seleccion));
    setSeleccion(null);
  };

  const relacionar = (destino: string) => {
    if (seleccion) setRelaciones((r) => addEdge({ source: seleccion, target: destino, id: idNuevo('relacion', r) }, r));
  };

  const acomodar = async () => {
    setAcomodando(true);
    setErrorAcomodo(false);
    try {
      const { default: ELK } = await import('elkjs/lib/elk.bundled.js');
      const resultado = await new ELK().layout({
        id: 'raiz',
        layoutOptions: { 'elk.algorithm': 'layered', 'elk.direction': 'RIGHT', 'elk.spacing.nodeNode': '40', 'elk.layered.spacing.nodeNodeBetweenLayers': '80' },
        children: nodos.map((n) => ({ id: n.id, width: ANCHO_NODO, height: ALTO_NODO })),
        edges: relaciones.map((r) => ({ id: r.id, sources: [r.source], targets: [r.target] })),
      });
      const posiciones = new Map((resultado.children ?? []).map((h) => [h.id, { x: h.x ?? 0, y: h.y ?? 0 }]));
      setNodos((lista) => lista.map((n) => ({ ...n, position: posiciones.get(n.id) ?? n.position })));
    } catch {
      setErrorAcomodo(true);
    } finally {
      setAcomodando(false);
    }
  };

  const texto = buscar.trim().toLowerCase();
  const encontrados = texto ? aplicativos.filter((a) => a.nombre.toLowerCase().includes(texto) || a.slug.includes(texto)).slice(0, 8) : [];
  const aplicativoElegido = aplicativos.find((a) => a.id === elegido?.data.aplicativoId);
  const otros = nodos.filter((n) => n.id !== seleccion && !relaciones.some((r) => r.source === seleccion && r.target === n.id));

  return (
    <div className="grid min-w-0 gap-3 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <div role="group" aria-label="Lienzo" className="h-[420px] min-w-0 overflow-hidden rounded-xl border border-border bg-card lg:h-[560px]">
        <ReactFlow
          nodes={nodos}
          edges={relaciones}
          onNodesChange={alCambiarNodos}
          onEdgesChange={alCambiarRelaciones}
          onConnect={conectar}
          onNodeClick={(_, n) => setSeleccion(n.id)}
          onPaneClick={() => setSeleccion(null)}
          fitView
          colorMode={efectivo === 'oscuro' ? 'dark' : 'light'}
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>
      <aside aria-label="Panel del lienzo" className="flex min-w-0 flex-col gap-3">
        <div className="flex flex-wrap gap-2">
          <Boton variante="secundario" onClick={agregar}>
            Agregar nodo
          </Boton>
          <Boton variante="secundario" disabled={acomodando || nodos.length === 0} onClick={() => void acomodar()}>
            {acomodando ? 'Acomodando…' : 'Acomodar'}
          </Boton>
        </div>
        {errorAcomodo && <p role="alert" className="text-sm">No pudimos acomodar el lienzo. Revisa tu conexión.</p>}

        <fieldset className="min-w-0">
          <legend className="mb-1 text-sm font-medium">Nodos ({nodos.length})</legend>
          <ul className="flex max-h-40 flex-col gap-1 overflow-y-auto">
            {nodos.map((n) => (
              <li key={n.id}>
                <Boton variante={n.id === seleccion ? 'primario' : 'fantasma'} aria-pressed={n.id === seleccion} className="w-full justify-start break-words text-left" onClick={() => setSeleccion(n.id)}>
                  {n.data.label || n.id}
                </Boton>
              </li>
            ))}
          </ul>
        </fieldset>

        {elegido ? (
          <div className="flex min-w-0 flex-col gap-3 rounded-xl border border-border p-3">
            <label className="text-sm font-medium">
              Etiqueta del nodo
              <input required maxLength={200} value={elegido.data.label} onChange={(e) => editar({ label: e.target.value })} className={claseCampo} />
            </label>
            <Opciones titulo="Tipo del nodo" valor={elegido.data.tipo} opciones={TIPOS} textos={TEXTO_TIPO_NODO} alCambiar={(tipo) => editar({ tipo })} />
            <div className="min-w-0">
              <label className="text-sm font-medium">
                Aplicativo del catálogo
                <input type="search" value={buscar} onChange={(e) => setBuscar(e.target.value)} placeholder={aplicativoElegido?.nombre ?? 'Buscar…'} className={claseCampo} />
              </label>
              {aplicativoElegido && (
                <p className="mt-1 flex flex-wrap items-center gap-2 text-sm">
                  Enlazado a {aplicativoElegido.nombre}
                  <Boton variante="fantasma" onClick={() => editar({ aplicativoId: undefined })}>
                    Quitar el enlace
                  </Boton>
                </p>
              )}
              <ul className="mt-1 flex flex-col gap-1">
                {encontrados.map((a) => (
                  <li key={a.id}>
                    <Boton
                      variante="fantasma"
                      className="w-full justify-start text-left"
                      onClick={() => {
                        editar({ aplicativoId: a.id });
                        setBuscar('');
                      }}
                    >
                      {a.nombre}
                    </Boton>
                  </li>
                ))}
              </ul>
            </div>
            {otros.length > 0 && (
              <div className="min-w-0">
                <p className="mb-1 text-sm font-medium">Relacionar con</p>
                <ul className="flex max-h-32 flex-col gap-1 overflow-y-auto">
                  {otros.map((n) => (
                    <li key={n.id}>
                      <Boton variante="fantasma" className="w-full justify-start break-words text-left" onClick={() => relacionar(n.id)}>
                        {n.data.label || n.id}
                      </Boton>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <div>
              <Boton variante="secundario" onClick={quitar}>
                Quitar este nodo
              </Boton>
            </div>
          </div>
        ) : (
          <p className="text-sm text-atlas-texto-suave">Elige un nodo para editarlo o relacionarlo.</p>
        )}
      </aside>
    </div>
  );
}
