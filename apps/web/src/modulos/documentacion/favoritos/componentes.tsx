import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Star } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link } from 'react-router';

import { pedir } from '../../../app/api';
import { formatearFechaHora } from '../../../app/ui/formato';
import { useEnLinea } from '../../../pwa/conexion';
import { conRespaldoGuardado } from '../../../pwa/sinConexion';
import { type FavoritoDocumento, guardadoEn, guardarFavorito, olvidarFavorito, recurso, RUTA_FAVORITOS } from './sinConexion';

const CLAVE = ['documentacion', 'favoritos'] as const;

export function useFavoritos() {
  return useQuery({
    queryKey: CLAVE,
    queryFn: () => conRespaldoGuardado(recurso(RUTA_FAVORITOS), async () => (await (await pedir(RUTA_FAVORITOS)).json()) as FavoritoDocumento[]),
    staleTime: 60_000,
    networkMode: 'always',
  });
}

/** «Guardar sin conexión»: marca o quita el favorito en el servidor y guarda o borra en el dispositivo lo que muestra. */
export function BotonFavorito({ documentoId }: { documentoId: string }) {
  const enLinea = useEnLinea();
  const cliente = useQueryClient();
  const favoritos = useFavoritos();
  const cambiar = useMutation({
    mutationFn: async (marcar: boolean) => {
      await pedir(`${RUTA_FAVORITOS}/${encodeURIComponent(documentoId)}`, { method: marcar ? 'PUT' : 'DELETE' });
      if (marcar) await guardarFavorito(documentoId).catch(() => undefined);
      else await olvidarFavorito(documentoId);
    },
    onSettled: () => cliente.invalidateQueries({ queryKey: CLAVE }),
  });
  const marcado = (Array.isArray(favoritos.data) ? favoritos.data : []).some((f) => f.documentoId === documentoId);
  const [fecha, setFecha] = useState<Date | null>(null);
  useEffect(() => {
    let vigente = true;
    if (!marcado) setFecha(null);
    else void guardadoEn(documentoId).then((f) => vigente && setFecha(f));
    return () => {
      vigente = false;
    };
  }, [documentoId, marcado, favoritos.dataUpdatedAt, cambiar.isPending]);

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <button
        type="button"
        aria-pressed={marcado}
        disabled={!enLinea || cambiar.isPending || favoritos.isPending}
        onClick={() => cambiar.mutate(!marcado)}
        className="inline-flex min-h-toque items-center gap-2 self-start rounded-xl border border-border bg-card px-3 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-70"
      >
        <Star className={`size-4 ${marcado ? 'fill-current' : ''}`} aria-hidden="true" />
        {marcado ? 'Quitar de favoritos' : 'Guardar en favoritos'}
      </button>
      <p className="text-xs text-atlas-texto-suave" aria-live="polite">
        {marcado && fecha
          ? `Disponible sin conexión · sincronizado el ${formatearFechaHora(fecha)}`
          : marcado
            ? 'En favoritos; se guardará en este dispositivo cuando haya conexión.'
            : !enLinea
              ? 'Necesitas conexión para marcar favoritos.'
              : 'Los favoritos se guardan en este dispositivo para leerlos sin conexión.'}
      </p>
      {cambiar.isError && (
        <p role="alert" className="text-xs">
          No pudimos cambiar el favorito. Inténtalo de nuevo.
        </p>
      )}
    </div>
  );
}

/** «Tus favoritos» en la lista de documentos (también sin conexión). */
export function ListaFavoritos() {
  const favoritos = useFavoritos();
  if (!Array.isArray(favoritos.data) || favoritos.data.length === 0) return null;
  return (
    <section aria-labelledby="documentos-favoritos" className="flex flex-col gap-2">
      <h2 id="documentos-favoritos" className="text-lg font-semibold">
        Tus favoritos
      </h2>
      <ul aria-label="Documentos favoritos" className="flex flex-col gap-1 rounded-2xl border border-border bg-card p-2">
        {favoritos.data.map((f) => (
          <li key={f.id}>
            <Link to={f.ruta} className="flex min-h-toque min-w-0 items-center gap-2 rounded-xl px-2 py-1 font-medium break-words hover:bg-atlas-activo">
              <Star className="size-3.5 shrink-0 fill-current" aria-hidden="true" />
              <span className="min-w-0">{f.titulo}</span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
