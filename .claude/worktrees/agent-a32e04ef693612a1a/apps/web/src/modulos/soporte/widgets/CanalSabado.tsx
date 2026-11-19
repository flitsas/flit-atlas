import { useQuery } from '@tanstack/react-query';

import { CLAVE, horario, obtenerCanal } from '../api';
import { formatearDia } from '../fechas';

/**
 * Widget de Inicio para externos: el canal de soporte del sábado que se configuró, sin nombres de personas
 * (prompt maestro §7.3 y §8.3). Sin canal configurado no muestra nada del soporte interno.
 */
export default function CanalSabado() {
  const canal = useQuery({ queryKey: [...CLAVE, 'canal'], queryFn: obtenerCanal, staleTime: 5 * 60_000 });
  const datos = canal.data;

  return (
    <div className="flex flex-col gap-2 text-sm">
      {canal.isPending && <p role="status">Cargando…</p>}
      {canal.isError && <p role="alert">No pudimos cargar el canal de soporte.</p>}
      {datos && !datos.configurado && <p className="text-atlas-texto-suave">Tu contacto de FLIT te indicará el canal de soporte.</p>}
      {datos?.configurado && (
        <>
          <p className="flex flex-wrap items-baseline gap-2">
            <span className="text-lg font-semibold">Sábado {formatearDia(datos.fecha)}</span>
            {datos.haySoporte && horario(datos) && <span className="text-atlas-texto-suave">{horario(datos)}</span>}
          </p>
          {datos.haySoporte ? (
            <p className="break-words">
              Escríbenos a: <strong>{datos.canal}</strong>
            </p>
          ) : (
            <p className="text-atlas-texto-suave">Este sábado no hay soporte programado. Canal habitual: {datos.canal}</p>
          )}
        </>
      )}
    </div>
  );
}
