import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router';

import { Tarjeta } from '../../../app/ui/Tarjeta';
import { DIMENSIONES, TEXTO_RIESGO, useCaso, useEnvio } from '../api';
import { claseBoton, claseCampo, ErrorEnvio, Pagina, vacioANulo } from '../componentes';

interface Fila {
  escenario: string;
  p: string;
  i: string;
  p2: string;
  i2: string;
  controles: string;
}

const FILA: Fila = { escenario: '', p: '', i: '', p2: '', i2: '', controles: '' };

/** Igual que el servidor (plan 02 §8.2); es solo una ayuda: el nivel que vale lo calcula atlas-api. */
const nivel = (puntaje: number) => (puntaje <= 4 ? 'bajo' : puntaje <= 9 ? 'medio' : puntaje <= 15 ? 'alto' : 'critico');

const claseEscala = `${claseCampo} max-w-24`;

function Escala({ titulo, valor, alCambiar }: { titulo: string; valor: string; alCambiar: (v: string) => void }) {
  return (
    <label className="text-sm font-medium">
      {titulo}
      <input type="number" min={1} max={5} inputMode="numeric" value={valor} onChange={(e) => alCambiar(e.target.value)} className={claseEscala} />
    </label>
  );
}

/** Evaluación de riesgo paso a paso (HU 8.2, plan 02 §8): seis dimensiones y nivel por la peor. */
export default function Evaluar() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const caso = useCaso(id);
  const envio = useEnvio();
  const [filas, setFilas] = useState<Record<string, Fila>>(Object.fromEntries(DIMENSIONES.map((d) => [d.id, FILA])));
  const [transferencia, setTransferencia] = useState('');
  const [principios, setPrincipios] = useState({ idoneidad: '', necesidad: '', razonabilidad: '', proporcionalidad: '' });

  const poner = (dimension: string, campo: keyof Fila, valor: string) => setFilas((f) => ({ ...f, [dimension]: { ...f[dimension]!, [campo]: valor } }));

  const peor = useMemo(() => {
    const puntajes = Object.values(filas).map((f) => Number(f.p) * Number(f.i)).filter((n) => n > 0);
    return puntajes.length > 0 ? Math.max(...puntajes) : null;
  }, [filas]);

  const enviar = (e: React.FormEvent) => {
    e.preventDefault();
    envio.mutate(
      {
        ruta: `/casos/${id}/evaluaciones`,
        cuerpo: {
          dimensiones: DIMENSIONES.map((d) => {
            const f = filas[d.id]!;
            return {
              dimension: d.id,
              escenario: f.escenario,
              p: Number(f.p),
              i: Number(f.i),
              controlesExistentes: null,
              controlesNuevos: vacioANulo(f.controles),
              p2: f.p2 === '' ? null : Number(f.p2),
              i2: f.i2 === '' ? null : Number(f.i2),
            };
          }),
          principios,
          transferenciaInternacional: vacioANulo(transferencia),
        },
      },
      { onSuccess: () => void navegar(`/gobierno-ia/casos/${id}`) },
    );
  };

  return (
    <Pagina titulo="Evaluar el riesgo" descripcion={caso.data?.caso.nombre}>
      <p className="text-sm text-atlas-texto-suave">Probabilidad e impacto de 1 a 5. El nivel del caso es el de la peor dimensión, no el promedio.</p>
      <form onSubmit={enviar} className="flex flex-col gap-4">
        {DIMENSIONES.map((d) => {
          const f = filas[d.id]!;
          return (
            <Tarjeta key={d.id} titulo={`${d.id} · ${d.nombre}`}>
              <p className="mb-2 text-sm text-atlas-texto-suave">{d.pregunta}</p>
              <label className="text-sm font-medium">
                Escenario de riesgo
                <textarea required rows={2} value={f.escenario} onChange={(e) => poner(d.id, 'escenario', e.target.value)} className={`${claseCampo} py-2`} />
              </label>
              <div className="mt-2 flex flex-wrap gap-3">
                <Escala titulo="Probabilidad" valor={f.p} alCambiar={(v) => poner(d.id, 'p', v)} />
                <Escala titulo="Impacto" valor={f.i} alCambiar={(v) => poner(d.id, 'i', v)} />
                <Escala titulo="Probabilidad residual" valor={f.p2} alCambiar={(v) => poner(d.id, 'p2', v)} />
                <Escala titulo="Impacto residual" valor={f.i2} alCambiar={(v) => poner(d.id, 'i2', v)} />
              </div>
              <label className="mt-2 block text-sm font-medium">
                Controles nuevos
                <input value={f.controles} onChange={(e) => poner(d.id, 'controles', e.target.value)} className={claseCampo} />
              </label>
            </Tarjeta>
          );
        })}
        <Tarjeta titulo="Principios de la Circular 002">
          {(['idoneidad', 'necesidad', 'razonabilidad', 'proporcionalidad'] as const).map((p) => (
            <label key={p} className="mt-2 block text-sm font-medium capitalize">
              {p}
              <input value={principios[p]} onChange={(e) => setPrincipios({ ...principios, [p]: e.target.value })} className={claseCampo} />
            </label>
          ))}
          <label className="mt-2 block text-sm font-medium">
            Transferencia internacional
            <input value={transferencia} onChange={(e) => setTransferencia(e.target.value)} className={claseCampo} />
          </label>
        </Tarjeta>
        {peor !== null && (
          <p role="status" className="font-medium">
            Nivel inherente estimado: {TEXTO_RIESGO[nivel(peor)]} ({peor}). Lo confirma el servidor al guardar.
          </p>
        )}
        <ErrorEnvio error={envio.error} porDefecto="No pudimos guardar la evaluación." />
        <button type="submit" disabled={envio.isPending} className={`${claseBoton} self-start`}>
          Guardar la evaluación
        </button>
      </form>
    </Pagina>
  );
}
