import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { Insignia } from '../../../app/ui/Tarjeta';
import { formatearRelativo } from '../../../app/ui/formato';
import { CANALES, type Canal, type Comparacion, type EstadoAmbiente, type EstadoCapa, TEXTO_CANAL, textoMotivo, usePanelDrift } from '../api';

const TEXTO_COMPARACION: Record<Comparacion, string> = { con_drift: 'Con drift', sin_drift: 'Sin drift', sin_datos: 'Sin datos' };

const tono = (c: Comparacion) => (c === 'con_drift' ? 'info' : c === 'sin_drift' ? 'exito' : 'neutral');

function Capa({ nombre, capa }: { nombre: string; capa: EstadoCapa }) {
  return (
    <li className="min-w-0">
      <span className="font-medium">{nombre}: </span>
      {capa.estado === 'con_datos' ? 'con datos' : <>sin datos ({textoMotivo(capa.motivo)})</>}
    </li>
  );
}

function TarjetaAmbiente({ estado }: { estado: EstadoAmbiente }) {
  return (
    <li className="min-w-0 rounded-xl border border-border bg-card p-3">
      <h2 className="font-medium">{TEXTO_CANAL[estado.ambiente]}</h2>
      <ul className="mt-1 text-sm">
        <Capa nombre="Modelo" capa={estado.modelo} />
        <Capa nombre="Gitops" capa={estado.gitops} />
        <Capa nombre="Argo CD" capa={estado.argo} />
      </ul>
      <dl className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-sm">
        <div className="flex items-center gap-1">
          <dt>Modelo y gitops:</dt>
          <dd>
            <Insignia tono={tono(estado.modeloGitops)}>{TEXTO_COMPARACION[estado.modeloGitops]}</Insignia>
          </dd>
        </div>
        <div className="flex items-center gap-1">
          <dt>Gitops y Argo CD:</dt>
          <dd>
            <Insignia tono={tono(estado.gitopsArgo)}>{TEXTO_COMPARACION[estado.gitopsArgo]}</Insignia>
          </dd>
        </div>
      </dl>
    </li>
  );
}

/** Panel de drift (HU 6.5). Sin datos no es lo mismo que sin drift, y la pantalla lo dice por capa. Solo con `ver_despliegue`. */
export default function DriftArquitectura() {
  const [parametros, setParametros] = useSearchParams();
  const ambiente = (CANALES as readonly string[]).includes(parametros.get('ambiente') ?? '') ? (parametros.get('ambiente') as Canal) : null;
  const [estado, setEstado] = useState<'abierto' | 'resuelto'>('abierto');
  const panel = usePanelDrift(ambiente, estado);
  const clase = (activa: boolean) => `inline-flex min-h-toque items-center rounded-xl border px-3 text-sm ${activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card'}`;

  return (
    <section aria-labelledby="titulo-drift" className="flex min-w-0 flex-col gap-4">
      <header className="min-w-0">
        <Link to="/arquitectura" className="text-sm underline">
          Arquitectura
        </Link>
        <h1 id="titulo-drift" className="text-2xl font-semibold">
          Panel de drift
        </h1>
        <p className="text-sm text-atlas-texto-suave">Lo que dice el modelo, lo que dice gitops y lo que dice Argo CD, comparados por ambiente.</p>
      </header>
      <div role="group" aria-label="Ambiente" className="flex flex-wrap gap-1">
        <button type="button" aria-pressed={ambiente === null} onClick={() => setParametros({})} className={clase(ambiente === null)}>
          Todos
        </button>
        {CANALES.map((c) => (
          <button key={c} type="button" aria-pressed={c === ambiente} onClick={() => setParametros({ ambiente: c })} className={clase(c === ambiente)}>
            {TEXTO_CANAL[c]}
          </button>
        ))}
      </div>
      {panel.isPending && <div role="status" aria-label="Cargando el panel" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />}
      {panel.isError && <p role="alert">No pudimos cargar el panel. Revisa tu conexión.</p>}
      {panel.data && (
        <>
          <ul className="grid min-w-0 gap-2 lg:grid-cols-3">
            {panel.data.ambientes.map((a) => (
              <TarjetaAmbiente key={a.ambiente} estado={a} />
            ))}
          </ul>
          <div role="group" aria-label="Estado de los hallazgos" className="flex flex-wrap gap-1">
            <button type="button" aria-pressed={estado === 'abierto'} onClick={() => setEstado('abierto')} className={clase(estado === 'abierto')}>
              Abiertos
            </button>
            <button type="button" aria-pressed={estado === 'resuelto'} onClick={() => setEstado('resuelto')} className={clase(estado === 'resuelto')}>
              Resueltos
            </button>
          </div>
          {panel.data.hallazgos.length === 0 && (
            <p className="text-sm text-atlas-texto-suave">
              {estado === 'abierto' ? 'No hay hallazgos abiertos en lo que se pudo comparar. Revisa arriba qué capas no tienen datos.' : 'No hay hallazgos resueltos.'}
            </p>
          )}
          {panel.data.hallazgos.length > 0 && (
            <ul className="flex min-w-0 flex-col gap-2">
              {panel.data.hallazgos.map((h) => (
                <li key={h.id} className="min-w-0 rounded-xl border border-border bg-card p-3 text-sm">
                  <p className="flex flex-wrap items-center gap-1">
                    <Insignia tono={h.severidad === 'alta' ? 'info' : 'neutral'}>{h.severidad === 'alta' ? 'Alta' : 'Media'}</Insignia>
                    <Insignia tono="neutral">{TEXTO_CANAL[h.ambiente]}</Insignia>
                    <Insignia tono="neutral">{h.capa === 'modelo_gitops' ? 'Modelo y gitops' : 'Gitops y Argo CD'}</Insignia>
                    {h.cluster && <Insignia tono="neutral">{h.cluster}</Insignia>}
                  </p>
                  <p className="mt-1 break-words">{h.descripcion}</p>
                  <p className="text-xs text-atlas-texto-suave">Detectado {formatearRelativo(new Date(h.detectadoEn))}</p>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
