import { useState } from 'react';
import { Link } from 'react-router';

import { Boton, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  type AplicativoRef,
  cambiarResponsable,
  type FilaReparto,
  obtenerReparto,
  type Responsable,
  retirarResponsable,
  TEXTO_TIPO,
  type TipoResponsabilidad,
  useCambioReparto,
  useReparto,
} from '../apiResponsables';
import { useDirectorio } from '../api';
import { type Aviso, avisoDeError, claseCampo, Definiciones, MostrarAviso, TarjetaResponsable } from '../componentes';
import { Hoja } from '../../../app/ui/Hoja';

/** Reparto por producto y por módulo (tablas 3.1 y 3.2 del documento), con las definiciones de cada rol tal cual. */
export default function PorAplicativo() {
  const reparto = useReparto(['vigente'], obtenerReparto);
  const [cambio, setCambio] = useState<{ aplicativo: AplicativoRef; tipo: TipoResponsabilidad; actual: Responsable | null } | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const datos = reparto.data;

  if (reparto.isPending) return <p role="status">Cargando el reparto…</p>;
  if (reparto.isError || !datos) return <p role="alert">No pudimos cargar el reparto. Revisa tu conexión.</p>;

  const grupos: { titulo: string; filas: FilaReparto[] }[] = [
    { titulo: 'Productos', filas: datos.productos },
    { titulo: 'Módulos de FLIT 2', filas: datos.modulos },
    { titulo: 'Servicios', filas: datos.servicios },
  ];

  return (
    <div className="flex flex-col gap-4">
      {datos.definiciones.length > 0 && (
        <Tarjeta titulo="Qué significa cada rol">
          <Definiciones definiciones={datos.definiciones} />
        </Tarjeta>
      )}
      <MostrarAviso aviso={aviso} />
      {grupos
        .filter((g) => g.filas.length > 0)
        .map((g) => (
          <Tarjeta key={g.titulo} titulo={g.titulo}>
            <ul aria-label={g.titulo} className="grid gap-3 lg:grid-cols-2">
              {g.filas.map((fila) => (
                <li key={fila.aplicativo.id} className="flex min-w-0 flex-col gap-3 rounded-xl border border-border p-3">
                  <Link to={`/catalogo/${fila.aplicativo.slug}`} className="font-semibold break-words text-atlas-tinta hover:underline">
                    {fila.aplicativo.nombre}
                  </Link>
                  <div className="grid gap-3 sm:grid-cols-2">
                    {(['principal', 'secundario'] as const).map((tipo) => (
                      <TarjetaResponsable
                        key={tipo}
                        tipo={tipo}
                        responsable={fila[tipo]}
                        acciones={
                          datos.puedeGestionar && (
                            <Boton
                              variante="secundario"
                              className="self-start"
                              aria-label={`Cambiar ${TEXTO_TIPO[tipo].toLowerCase()} de ${fila.aplicativo.nombre}`}
                              onClick={() => setCambio({ aplicativo: fila.aplicativo, tipo, actual: fila[tipo] })}
                            >
                              Cambiar
                            </Boton>
                          )
                        }
                      />
                    ))}
                  </div>
                </li>
              ))}
            </ul>
          </Tarjeta>
        ))}
      {cambio && (
        <HojaCambio
          {...cambio}
          alCerrar={(resultado) => {
            setCambio(null);
            if (resultado) setAviso(resultado);
          }}
        />
      )}
    </div>
  );
}

/** Cambiar (o retirar) un responsable: persona del directorio, dedicación y nota obligatoria; crea una versión. */
function HojaCambio({
  aplicativo,
  tipo,
  actual,
  alCerrar,
}: {
  aplicativo: AplicativoRef;
  tipo: TipoResponsabilidad;
  actual: Responsable | null;
  alCerrar: (aviso: Aviso) => void;
}) {
  const [texto, setTexto] = useState('');
  const [personaId, setPersonaId] = useState<string | null>(null);
  const [dedicacion, setDedicacion] = useState(actual?.dedicacion?.toString() ?? (tipo === 'secundario' ? '20' : ''));
  const [nota, setNota] = useState('');
  const [error, setError] = useState<Aviso>(null);
  const directorio = useDirectorio(texto.trim().length >= 2 ? texto : '', false);
  const cambiar = useCambioReparto(cambiarResponsable);
  const retirar = useCambioReparto(retirarResponsable);
  const candidatas = (directorio.data?.personas ?? []).filter((p) => p.estado === 'activa').slice(0, 8);

  return (
    <Hoja abierta alCerrar={() => alCerrar(null)} titulo={`${TEXTO_TIPO[tipo]} de ${aplicativo.nombre}`}>
      <form
        aria-label="Cambio de responsable"
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          if (!personaId) {
            setError({ tono: 'error', texto: 'Elige a la persona.' });
            return;
          }
          cambiar.mutate(
            { aplicativoId: aplicativo.id, tipo, personaId, dedicacion: dedicacion ? Number(dedicacion) : null, nota },
            {
              onSuccess: (v) => alCerrar({ tono: 'exito', texto: `Listo: el reparto va en la versión ${v.numero}.` }),
              onError: (er) => setError(avisoDeError(er, 'No se pudo cambiar el responsable.')),
            },
          );
        }}
      >
        {actual && <p className="text-sm">Hoy: {actual.persona.nombre}.</p>}
        <label className="text-sm font-medium">
          Busca a la persona
          <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} autoComplete="off" className={claseCampo} />
        </label>
        {candidatas.length > 0 && (
          <fieldset className="flex flex-col gap-1">
            <legend className="text-sm font-medium">Persona</legend>
            {candidatas.map((p) => (
              <label key={p.id} className="flex min-h-toque items-center gap-2 text-sm">
                <input type="radio" name="persona" value={p.id} checked={personaId === p.id} onChange={() => setPersonaId(p.id)} className="size-5" />
                <span className="min-w-0 break-words">
                  {p.nombre}
                  {p.cargo ? ` · ${p.cargo}` : ''}
                </span>
              </label>
            ))}
          </fieldset>
        )}
        <label className="text-sm font-medium">
          Dedicación orientativa (%)
          <input
            type="number"
            min={1}
            max={100}
            inputMode="numeric"
            value={dedicacion}
            onChange={(e) => setDedicacion(e.target.value)}
            className={claseCampo}
          />
        </label>
        <label className="text-sm font-medium">
          Nota del cambio
          <textarea required maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} rows={3} className={`${claseCampo} py-2`} />
        </label>
        <MostrarAviso aviso={error} />
        <div className="flex flex-wrap gap-2">
          <Boton type="submit" disabled={cambiar.isPending || !nota.trim()}>
            Guardar cambio
          </Boton>
          {actual && (
            <Boton
              variante="secundario"
              disabled={retirar.isPending || !nota.trim()}
              onClick={() =>
                retirar.mutate(
                  { id: actual.id, nota },
                  {
                    onSuccess: (v) => alCerrar({ tono: 'exito', texto: `Responsable retirado: el reparto va en la versión ${v.numero}.` }),
                    onError: (er) => setError(avisoDeError(er, 'No se pudo retirar el responsable.')),
                  },
                )
              }
            >
              Retirar
            </Boton>
          )}
        </div>
        <p className="text-xs text-atlas-texto-suave">Cada cambio crea una versión nueva del reparto, con tu nombre y la nota.</p>
      </form>
    </Hoja>
  );
}
