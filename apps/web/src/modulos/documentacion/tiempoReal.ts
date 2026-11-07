import { useEffect, useRef, useState } from 'react';

import { CABECERA_CSRF } from '../../app/api';
import { useEnLinea } from '../../pwa/conexion';

export const RUTA_HUB = '/api/v1/documentacion/tiempo-real';

export interface PersonaViendo {
  id: string;
  nombre: string;
}

/** Se aleja de a poco y se rinde tras unos minutos: el documento se sigue leyendo sin esto. */
const ESPERAS_MS = [0, 2_000, 5_000, 10_000, 30_000];
const REINTENTOS_MAXIMOS = 12;

export function esperaDeReintento(previos: number): number | null {
  return previos >= REINTENTOS_MAXIMOS ? null : (ESPERAS_MS[previos] ?? ESPERAS_MS[ESPERAS_MS.length - 1]!);
}

/**
 * Tiempo real del lector (HU 5.8, ATLAS-ADR-0053 §8): quién más tiene abierto el documento y el aviso de versión nueva.
 * El cliente de SignalR se carga con `import()` solo aquí. Es un añadido: sin conexión, con el hub apagado o si la API lo
 * rechaza, la página no cambia y simplemente no muestra a nadie. La cabecera antifalsificación va en lo que el navegador
 * deja (negociar y long polling); el WebSocket lo cubren `SameSite=Strict` y la revisión del origen en el hub.
 */
export function useTiempoReal(documentoId: string, alVersionNueva: (numero: number) => void): PersonaViendo[] {
  const enLinea = useEnLinea();
  const [personas, setPersonas] = useState<PersonaViendo[]>([]);
  const aviso = useRef(alVersionNueva);
  useEffect(() => {
    aviso.current = alVersionNueva;
  });

  useEffect(() => {
    if (!enLinea || !documentoId) return;
    let vigente = true;
    let detener: (() => Promise<void>) | undefined;
    const arrancar = async () => {
      const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
      if (!vigente) return;
      const conexion = new HubConnectionBuilder()
        .withUrl(RUTA_HUB, { headers: { [CABECERA_CSRF]: '1' } })
        .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (c) => esperaDeReintento(c.previousRetryCount) })
        .configureLogging(LogLevel.None)
        .build();
      detener = () => conexion.stop();
      const entrar = async () => {
        // false: no existe o ya no lo puede ver; el lector lo sabrá por la API.
        if (!(await conexion.invoke<boolean>('Entrar', documentoId))) setPersonas([]);
      };
      conexion.on('Presencia', (d: { documentoId: string; personas: PersonaViendo[] }) => {
        if (d.documentoId === documentoId) setPersonas(d.personas);
      });
      conexion.on('VersionNueva', (d: { documentoId: string; numero: number }) => {
        if (d.documentoId === documentoId) aviso.current(d.numero);
      });
      conexion.onreconnecting(() => setPersonas([]));
      conexion.onreconnected(() => void entrar().catch(() => undefined));
      conexion.onclose(() => setPersonas([]));
      await conexion.start();
      if (!vigente) {
        await conexion.stop();
        return;
      }
      await entrar();
    };
    void arrancar().catch(() => undefined);
    return () => {
      vigente = false;
      setPersonas([]);
      void detener?.().catch(() => undefined);
    };
  }, [documentoId, enLinea]);

  return personas;
}
