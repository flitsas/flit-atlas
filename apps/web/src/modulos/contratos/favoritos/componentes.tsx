import { CloudCheck, Star } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link } from 'react-router';

import { formatearFechaHora } from '../../../app/ui/formato';
import { useEnLinea } from '../../../pwa/conexion';
import { useCambiarFavorito, useFavoritosInternos, useFavoritosPortal } from './api';
import { guardadoEn, mismoObjetivo, type ObjetivoFavorito, objetivoDePortal } from './sinConexion';

/** Cuándo se guardó en el dispositivo lo principal del objetivo (se vuelve a leer cuando cambia `version`). */
function useGuardadoEn(objetivo: ObjetivoFavorito | null, version: unknown): Date | null {
  const [fecha, setFecha] = useState<Date | null>(null);
  const clave = objetivo ? JSON.stringify(objetivo) : '';
  useEffect(() => {
    let vigente = true;
    if (!objetivo) setFecha(null);
    else void guardadoEn(objetivo).then((f) => vigente && setFecha(f));
    return () => {
      vigente = false;
    };
    // `clave` resume el objetivo; `version` cambia al marcar, quitar o resincronizar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave, version]);
  return fecha;
}

/**
 * «Guardar sin conexión» (HU 4.9): marca o quita el favorito en el servidor y guarda o borra en el dispositivo lo que
 * muestra. Dice si ya está disponible sin conexión y desde cuándo. Sin red no se puede cambiar.
 */
export function BotonFavorito({ objetivo, que, enPortada = true }: { objetivo: ObjetivoFavorito; que: string; enPortada?: boolean }) {
  const enLinea = useEnLinea();
  const portal = useFavoritosPortal(objetivo.ambito === 'portal');
  const internos = useFavoritosInternos(objetivo.ambito === 'interno');
  const cambiar = useCambiarFavorito();
  const lista = objetivo.ambito === 'portal' ? portal : internos;
  const marcado =
    objetivo.ambito === 'portal'
      ? (Array.isArray(portal.data) ? portal.data : []).some((f) => mismoObjetivo(objetivoDePortal(f), objetivo))
      : (Array.isArray(internos.data) ? internos.data : []).some((f) => f.contratoId === objetivo.contratoId);
  const fecha = useGuardadoEn(marcado ? objetivo : null, `${lista.dataUpdatedAt}-${cambiar.isPending}`);
  const fondo = enPortada ? 'bg-white/95 text-[#0b1220]' : 'border border-border bg-card';

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <button
        type="button"
        aria-pressed={marcado}
        disabled={!enLinea || cambiar.isPending || lista.isPending}
        onClick={() => cambiar.mutate({ objetivo, marcar: !marcado })}
        className={`inline-flex min-h-toque items-center gap-2 self-start rounded-xl px-3 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-70 ${fondo}`}
      >
        <Star className={`size-4 ${marcado ? 'fill-current' : ''}`} aria-hidden="true" />
        {marcado ? `Quitar ${que} de favoritos` : `Guardar ${que} en favoritos`}
      </button>
      <p className={`text-xs ${enPortada ? 'text-white/85' : 'text-atlas-texto-suave'}`} aria-live="polite">
        {marcado && fecha
          ? `Disponible sin conexión · sincronizado el ${formatearFechaHora(fecha)}`
          : marcado
            ? 'En favoritos; se guardará en este dispositivo cuando haya conexión.'
            : !enLinea
              ? 'Necesitas conexión para marcar favoritos.'
              : 'Los favoritos se guardan en este dispositivo para verlos sin conexión.'}
      </p>
      {cambiar.isError && (
        <p role="alert" className="text-xs">
          No pudimos cambiar el favorito. Inténtalo de nuevo.
        </p>
      )}
    </div>
  );
}

function Disponible({ objetivo, version }: { objetivo: ObjetivoFavorito; version: unknown }) {
  const fecha = useGuardadoEn(objetivo, version);
  if (!fecha) return null;
  return (
    <span className="inline-flex items-center gap-1 text-xs text-atlas-texto-suave">
      <CloudCheck className="size-3.5" aria-hidden="true" /> Sin conexión · {formatearFechaHora(fecha)}
    </span>
  );
}

/** «Tus favoritos» del portal: lo compartido que la persona marcó y todavía ve (también sin conexión). */
export function FavoritosPortal() {
  const favoritos = useFavoritosPortal();
  if (!Array.isArray(favoritos.data) || favoritos.data.length === 0) return null;
  return (
    <section aria-labelledby="tus-favoritos" className="flex flex-col gap-2">
      <h2 id="tus-favoritos" className="text-base font-semibold">
        Tus favoritos
      </h2>
      <ul aria-label="Favoritos" className="flex flex-col gap-1 rounded-2xl border border-border bg-card p-2">
        {favoritos.data.map((f) => (
          <li key={f.id}>
            <Link to={f.ruta} className="flex min-h-toque min-w-0 flex-col justify-center rounded-xl px-2 py-1 hover:bg-atlas-activo">
              <span className="font-medium break-words">
                <Star className="mr-1 inline size-3.5 fill-current align-[-2px]" aria-hidden="true" />
                {f.titulo}
              </span>
              <span className="flex flex-wrap gap-x-2 text-xs text-atlas-texto-suave">
                {f.tipo === 'paquete' ? 'Paquete' : f.tipo === 'contrato' ? `Contrato · ${f.detalle ?? ''}` : `Guía · ${f.detalle ?? ''}`}
                <Disponible objetivo={objetivoDePortal(f)} version={favoritos.dataUpdatedAt} />
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** «Tus favoritos» de los contratos internos. */
export function FavoritosInternos() {
  const favoritos = useFavoritosInternos();
  if (!Array.isArray(favoritos.data) || favoritos.data.length === 0) return null;
  return (
    <section aria-labelledby="tus-contratos-favoritos" className="flex flex-col gap-2">
      <h2 id="tus-contratos-favoritos" className="text-base font-semibold">
        Tus favoritos
      </h2>
      <ul aria-label="Contratos favoritos" className="flex flex-col gap-1 rounded-2xl border border-border bg-card p-2">
        {favoritos.data.map((f) => (
          <li key={f.id}>
            <Link to={f.ruta} className="flex min-h-toque min-w-0 flex-col justify-center rounded-xl px-2 py-1 hover:bg-atlas-activo">
              <span className="font-medium break-words">
                <Star className="mr-1 inline size-3.5 fill-current align-[-2px]" aria-hidden="true" />
                {f.nombre}
              </span>
              <span className="flex flex-wrap gap-x-2 text-xs text-atlas-texto-suave">
                {f.aplicativo.nombre ?? f.aplicativo.slug ?? ''} · {f.codigo}
                {f.version ? ` · ${f.version}` : ''}
                <Disponible objetivo={{ ambito: 'interno', contratoId: f.contratoId }} version={favoritos.dataUpdatedAt} />
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
