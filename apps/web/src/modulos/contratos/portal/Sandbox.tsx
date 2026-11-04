import { useMutation, useQueryClient } from '@tanstack/react-query';
import { FlaskConical, KeyRound, TriangleAlert } from 'lucide-react';
import { useState } from 'react';

import { ErrorApi } from '../../../app/api';
import { BotonCopiar, Codigo, Etiqueta } from '../componentes/ui';
import { Cargando } from '../guias/componentes';
import { conectarAccionPortal } from './acciones';
import { activarSandbox, CLAVE_SANDBOX, type PaqueteCompartido, type SandboxActivado, useSandboxes } from './api';
import { fechaCorta } from './componentes';

/**
 * Sandbox nivel 1 del portal (HU 4.7, plan 00 §8.8, ATLAS-ADR-0045): activar el sandbox de un paquete, la URL base de cada
 * contrato (el proxy de atlas-api hacia Microcks, que responde los ejemplos del spec podado), la llave que se muestra una
 * sola vez para copiarla, las instrucciones y un ejemplo con curl que usa la variable `$LLAVE_SANDBOX`. La llave no se guarda
 * en la PWA (ni en la caché de consultas ni sin conexión): al salir de la pantalla, desaparece.
 */
export const ID_PANEL_SANDBOX = 'panel-sandbox';

/** El botón «Sandbox» de las acciones del paquete lleva al panel (que está en la misma pantalla) y le pasa el foco. */
function irAlPanel() {
  const panel = document.getElementById(ID_PANEL_SANDBOX);
  if (!panel) return;
  panel.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
  panel.focus({ preventScroll: true });
}

conectarAccionPortal('sandbox', irAlPanel);

/** Si el ambiente tiene sandbox (Microcks): mientras no se sabe, se da por disponible; el servidor decide al activar. */
export function useSandboxDisponible(paquete: PaqueteCompartido | undefined): boolean {
  const consulta = useSandboxes(!!paquete && paquete.puedeSandbox && paquete.nivel === 'tecnico');
  return consulta.data?.disponible ?? true;
}

const MOTIVOS_APAGADO: Record<string, string> = {
  revocada: 'la compartición se retiró',
  vencida: 'la compartición venció',
  sin_sandbox: 'la compartición ya no incluye el sandbox',
  paquete_no_disponible: 'el paquete ya no está disponible',
  sin_contratos: 'el paquete ya no comparte operaciones REST',
};

function mensajeDeError(error: unknown): string {
  if (error instanceof ErrorApi) {
    if (error.estado === 503) return 'El sandbox no está disponible en este ambiente.';
    if (error.codigo === 'sin_contratos_mock') return 'Este paquete no comparte operaciones de APIs REST (OpenAPI): el sandbox nivel 1 solo las simula.';
    if (error.estado === 403) return 'La compartición o tu rol no incluyen el sandbox de este paquete.';
    if (error.estado === 404) return 'No tienes este paquete compartido.';
  }
  return 'No pudimos activar el sandbox. Inténtalo de nuevo en unos minutos.';
}

export function PanelSandbox({ paquete, contratoId }: { paquete: PaqueteCompartido; contratoId?: string }) {
  const consulta = useSandboxes(paquete.puedeSandbox && paquete.nivel === 'tecnico');
  const cliente = useQueryClient();
  const [activado, setActivado] = useState<SandboxActivado | null>(null);
  const activar = useMutation({
    mutationFn: () => activarSandbox(paquete.id),
    onSuccess: (resultado) => {
      setActivado(resultado);
      void cliente.invalidateQueries({ queryKey: CLAVE_SANDBOX });
    },
  });

  if (!paquete.puedeSandbox || paquete.nivel !== 'tecnico') return null;

  const sandbox = activado?.sandbox ?? consulta.data?.sandboxes.find((s) => s.paqueteId === paquete.id);
  const activo = sandbox?.estado === 'activo';
  const contratos = (sandbox?.contratos ?? []).filter((c) => !contratoId || c.contratoId === contratoId);

  return (
    <section
      id={ID_PANEL_SANDBOX}
      tabIndex={-1}
      aria-labelledby="titulo-sandbox"
      className="flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta outline-none focus-visible:ring-2 focus-visible:ring-atlas-tinta"
    >
      <header className="flex flex-wrap items-center gap-2">
        <FlaskConical className="size-5 shrink-0" aria-hidden="true" />
        <h2 id="titulo-sandbox" className="text-lg font-semibold">
          Sandbox
        </h2>
        <Etiqueta tono="info">Nivel 1 · respuestas de ejemplo</Etiqueta>
        {sandbox && <Etiqueta tono={activo ? 'exito' : 'neutral'}>{activo ? 'Activo' : 'Apagado'}</Etiqueta>}
      </header>

      {consulta.isPending && <Cargando etiqueta="Cargando el sandbox" />}
      {consulta.data && !consulta.data.disponible && (
        <p role="note" className="rounded-xl border border-border bg-atlas-fondo p-3 text-sm text-atlas-texto-suave">
          El sandbox no está disponible en este ambiente.
        </p>
      )}

      {consulta.data?.disponible && (
        <>
          <p className="text-sm text-atlas-texto-suave">
            Prueba tu integración contra un simulador de las operaciones que FLIT te comparte: responde los ejemplos del contrato. Las operaciones que no se te
            comparten responden 404. Nunca uses datos reales.
          </p>

          {activado && (
            <div role="alert" className="flex min-w-0 flex-col gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm text-[var(--badge-warning-fg)]">
              <p className="flex items-start gap-2 font-semibold">
                <KeyRound className="mt-0.5 size-4 shrink-0" aria-hidden="true" /> Tu llave del sandbox
              </p>
              <p>Cópiala y guárdala ahora en un lugar seguro: no la volverás a ver. Si la pierdes, genera una nueva (la anterior deja de servir).</p>
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <code aria-label="Llave del sandbox" className="min-w-0 rounded-lg bg-card px-2 py-1.5 font-mono text-xs break-all select-all">
                  {activado.llave}
                </code>
                <BotonCopiar texto={activado.llave} etiqueta="Copiar la llave" />
              </div>
            </div>
          )}

          {activo && sandbox && (
            <>
              <p className="text-xs text-atlas-texto-suave">
                Revisión {sandbox.revision} del paquete · llave del {fechaCorta(sandbox.llaveCreadaEn)}
                {sandbox.expiraEn ? ` · vence el ${fechaCorta(sandbox.expiraEn)}` : ''}
              </p>
              {contratos.length === 0 ? (
                <p className="text-sm text-atlas-texto-suave">Este contrato no está en el sandbox (solo se simulan las APIs REST con operaciones compartidas).</p>
              ) : (
                <ul aria-label="URL base del sandbox" className="flex flex-col gap-2">
                  {contratos.map((c) => (
                    <li key={c.contratoId} className="min-w-0">
                      <Codigo titulo={`${c.nombre} · ${c.version} · ${c.operaciones} ${c.operaciones === 1 ? 'operación' : 'operaciones'}`} codigo={c.urlBase} />
                    </li>
                  ))}
                </ul>
              )}
              <details className="min-w-0 text-sm">
                <summary className="flex min-h-toque cursor-pointer items-center font-medium">Cómo usarlo</summary>
                <div className="flex min-w-0 flex-col gap-2 pt-1">
                  <p className="break-words text-atlas-texto-suave">{sandbox.instrucciones}</p>
                  <Codigo titulo="Guarda la llave en una variable" codigo={"export LLAVE_SANDBOX='<pega aquí tu llave>'"} />
                  <Codigo titulo="Ejemplo con curl" codigo={sandbox.ejemploCurl} />
                </div>
              </details>
            </>
          )}

          {sandbox && !activo && (
            <p role="note" className="flex items-start gap-2 text-sm text-atlas-texto-suave">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              El sandbox se apagó porque {MOTIVOS_APAGADO[sandbox.motivoApagado ?? ''] ?? 'ya no se puede usar'}. Si vuelves a activarlo, recibirás una URL y una llave
              nuevas.
            </p>
          )}

          {activar.isError && (
            <p role="alert" className="rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]">
              {mensajeDeError(activar.error)}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={activar.isPending}
              onClick={() => activar.mutate()}
              aria-describedby={activo ? 'aviso-llave-nueva' : undefined}
              className="inline-flex min-h-toque items-center justify-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo disabled:cursor-not-allowed disabled:opacity-60"
            >
              <KeyRound className="size-4" aria-hidden="true" />
              {activar.isPending ? 'Preparando el sandbox…' : activo ? 'Generar una llave nueva' : 'Activar el sandbox'}
            </button>
          </div>
          {activo && (
            <p id="aviso-llave-nueva" className="text-xs text-atlas-texto-suave">
              Generar una llave nueva invalida la anterior en ese instante.
            </p>
          )}
        </>
      )}
    </section>
  );
}
