import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { formatearFechaHora } from '../../../app/ui/formato';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  CLAVE_REPARTO,
  compararVersiones,
  fechaCorta,
  obtenerVersiones,
  restaurarVersion,
  TEXTO_TIPO,
  useCambioReparto,
  useReparto,
  type VersionResumen,
} from '../apiResponsables';
import { type Aviso, avisoDeError, claseCampo, MostrarAviso } from '../componentes';

const ORIGEN: Record<VersionResumen['origen'], string> = { importacion: 'Importación', cambio: 'Cambio', restauracion: 'Restauración' };

const CAMBIO = { agregada: 'Nueva', retirada: 'Retirada', persona: 'Cambió de persona', datos: 'Cambió de datos' } as const;

/** Historial versionado del reparto (§8.2): cada versión con autor y nota; comparar dos y restaurar una (crea otra). */
export default function Historial() {
  const { acceso } = useSesion();
  const gestiona = tienePermiso('atlas.equipo.gestionar', acceso.permisos);
  const versiones = useReparto(['versiones'], obtenerVersiones);
  const [par, setPar] = useState<{ desde: number; hasta: number } | null>(null);
  const [restaurando, setRestaurando] = useState<number | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);

  if (versiones.isPending) return <p role="status">Cargando el historial…</p>;
  if (versiones.isError || !versiones.data) return <p role="alert">No pudimos cargar el historial. Revisa tu conexión.</p>;
  const lista = versiones.data;
  const vigente = lista[0]?.numero;

  return (
    <div className="flex flex-col gap-4">
      <MostrarAviso aviso={aviso} />
      {par && <ComparacionVersiones {...par} />}
      <Tarjeta titulo="Versiones del reparto">
        {lista.length === 0 && <p className="text-sm text-atlas-texto-suave">Aún no hay versiones: importa el documento de responsables.</p>}
        <ol aria-label="Versiones del reparto" className="flex flex-col gap-3">
          {lista.map((v, i) => (
            <li key={v.numero} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
              <div className="flex flex-wrap items-center gap-2">
                <p className="font-semibold">Versión {v.numero}</p>
                <Insignia tono="neutral">{ORIGEN[v.origen]}</Insignia>
                {v.numero === vigente && <Insignia tono="exito">Vigente</Insignia>}
                {v.restauradaDe !== null && <Insignia tono="info">Igual a la {v.restauradaDe}</Insignia>}
              </div>
              <p className="text-sm break-words">{v.nota}</p>
              <p className="text-xs text-atlas-texto-suave">
                {v.autor ?? 'Atlas'} · {formatearFechaHora(new Date(v.creadaEn))}
                {v.fechaDocumento && ` · documento del ${fechaCorta(v.fechaDocumento)}`}
                {v.definidoPor && ` · definió: ${v.definidoPor}`} · {v.responsabilidades} responsabilidades
              </p>
              <div className="flex flex-wrap gap-2">
                {lista[i + 1] && (
                  <Boton variante="secundario" onClick={() => setPar({ desde: lista[i + 1]!.numero, hasta: v.numero })}>
                    Comparar con la {lista[i + 1]!.numero}
                  </Boton>
                )}
                {vigente !== undefined && v.numero !== vigente && (
                  <Boton variante="secundario" onClick={() => setPar({ desde: v.numero, hasta: vigente })}>
                    Comparar con la vigente
                  </Boton>
                )}
                {gestiona && v.numero !== vigente && (
                  <Boton variante="fantasma" onClick={() => setRestaurando(v.numero)}>
                    Restaurar
                  </Boton>
                )}
              </div>
              {restaurando === v.numero && (
                <FormularioRestaurar
                  numero={v.numero}
                  alTerminar={(resultado) => {
                    setRestaurando(null);
                    setAviso(resultado);
                  }}
                />
              )}
            </li>
          ))}
        </ol>
      </Tarjeta>
    </div>
  );
}

function FormularioRestaurar({ numero, alTerminar }: { numero: number; alTerminar: (aviso: Aviso) => void }) {
  const [nota, setNota] = useState('');
  const [error, setError] = useState<Aviso>(null);
  const restaurar = useCambioReparto(restaurarVersion);
  return (
    <form
      aria-label={`Restaurar la versión ${numero}`}
      className="flex flex-col gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        restaurar.mutate(
          { numero, nota },
          {
            onSuccess: (v) => alTerminar({ tono: 'exito', texto: `Restaurada: la versión ${v.numero} es igual a la ${numero}.` }),
            onError: (er) => setError(avisoDeError(er, 'No se pudo restaurar la versión.')),
          },
        );
      }}
    >
      <label className="text-sm font-medium">
        Nota de la restauración
        <textarea required maxLength={500} rows={2} value={nota} onChange={(e) => setNota(e.target.value)} className={`${claseCampo} py-2`} />
      </label>
      <MostrarAviso aviso={error} />
      <Boton type="submit" className="self-start" disabled={!nota.trim() || restaurar.isPending}>
        Restaurar la versión {numero}
      </Boton>
    </form>
  );
}

function ComparacionVersiones({ desde, hasta }: { desde: number; hasta: number }) {
  const comparacion = useQuery({
    queryKey: [...CLAVE_REPARTO, 'comparacion', desde, hasta],
    queryFn: () => compararVersiones(desde, hasta),
  });
  return (
    <Tarjeta titulo={`De la versión ${desde} a la ${hasta}`}>
      {comparacion.isPending && <p role="status">Comparando…</p>}
      {comparacion.isError && <p role="alert">No pudimos comparar las versiones.</p>}
      {comparacion.data?.iguales && <p className="text-sm">Reparten igual: mismas personas, dedicaciones, vigencias y orden.</p>}
      {comparacion.data && !comparacion.data.iguales && comparacion.data.cambios.length > 0 && (
        <ul aria-label="Cambios" className="flex flex-col gap-2 text-sm">
          {comparacion.data.cambios.map((c) => (
            <li key={`${c.aplicativo.id}-${c.tipo}`} className="rounded-xl border border-border p-3 break-words">
              <strong>{c.aplicativo.nombre}</strong> · {TEXTO_TIPO[c.tipo]}: {CAMBIO[c.cambio]}
              {c.cambio === 'persona' && ` (${c.antes} → ${c.despues})`}
              {c.cambio === 'agregada' && ` (${c.despues})`}
              {c.cambio === 'retirada' && ` (${c.antes})`}
              {c.campos.length > 0 && ` · ${c.campos.join(', ')}`}
            </li>
          ))}
        </ul>
      )}
    </Tarjeta>
  );
}
