import { useQueryClient } from '@tanstack/react-query';
import { RefreshCw, ShieldAlert, WifiOff } from 'lucide-react';
import { useEffect } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';

import { useEnLinea } from '../../pwa/conexion';
import { CLAVE_YO, useSesion } from '../sesion';
import { formatearFechaHora } from '../ui/formato';
import { Boton } from '../ui/Tarjeta';

/** Indicador «Sin conexión» con la fecha de lo último guardado (§11). Al volver la red, recarga la sesión. */
export function AvisoSinConexion() {
  const enLinea = useEnLinea();
  const { sinConexion, guardadoEn } = useSesion();
  const cliente = useQueryClient();

  useEffect(() => {
    if (enLinea && sinConexion) void cliente.invalidateQueries({ queryKey: CLAVE_YO });
  }, [enLinea, sinConexion, cliente]);

  if (enLinea && !sinConexion) return null;
  return (
    <div role="status" className="flex items-center gap-2 bg-[#3b4256] px-4 py-2 text-sm text-white dark:bg-[#26314a]">
      <WifiOff className="size-4 shrink-0" aria-hidden="true" />
      <span>
        Sin conexión.
        {guardadoEn
          ? ` Ves lo guardado en este dispositivo el ${formatearFechaHora(guardadoEn)}.`
          : ' Algunas secciones no estarán disponibles hasta que vuelva la red.'}
      </span>
    </div>
  );
}

/**
 * Aviso fijo de la cuenta de administrador de emergencia (ATLAS-ADR-0027): que nadie la use sin darse cuenta ni la deje
 * abierta. Cada ingreso queda en la auditoría como evento de alta severidad.
 */
export function AvisoEmergencia() {
  const { yo } = useSesion();
  if (!yo.emergencia) return null;
  return (
    <div
      role="status"
      className="flex items-center gap-2 border-b border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] px-4 py-2 text-sm text-[var(--badge-warning-fg)]"
    >
      <ShieldAlert className="size-4 shrink-0" aria-hidden="true" />
      <span>
        <strong className="font-semibold">Cuenta de emergencia.</strong> Úsala solo para recuperar el acceso a Atlas y cierra la sesión al
        terminar: todo queda en la auditoría.
      </span>
    </div>
  );
}

/** Revisa si hay un service worker nuevo cada hora y, si lo hay, ofrece activarlo (§11: recarga controlada). */
const INTERVALO_REVISION = 60 * 60_000;

export function AvisoNuevaVersion() {
  const {
    needRefresh: [hayNueva, setHayNueva],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registro) {
      if (registro) setInterval(() => void registro.update(), INTERVALO_REVISION);
    },
  });

  if (!hayNueva) return null;
  return (
    <div
      role="status"
      className="fixed inset-x-3 bottom-[calc(5.5rem+env(safe-area-inset-bottom))] z-40 mx-auto flex max-w-md animate-entrar flex-wrap items-center gap-3 rounded-2xl border border-border bg-card p-4 shadow-2xl md:inset-x-auto md:right-6 md:bottom-6"
    >
      <RefreshCw className="size-5 shrink-0 text-atlas-tinta" aria-hidden="true" />
      <p className="min-w-0 flex-1 text-sm">
        <strong className="block font-semibold">Hay una nueva versión de Atlas.</strong>
        Actualiza para usar lo último; tarda un segundo.
      </p>
      <div className="flex gap-2">
        <Boton variante="fantasma" onClick={() => setHayNueva(false)}>
          Más tarde
        </Boton>
        <Boton onClick={() => void updateServiceWorker(true)}>Actualizar</Boton>
      </div>
    </div>
  );
}
