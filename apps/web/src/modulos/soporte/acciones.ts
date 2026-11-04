import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef } from 'react';

import { type AccionEncolada, ejecutarOEncolar, nuevaLlave, type ResultadoAccion, useCola } from '../../pwa/cola';
import { CLAVE, RUTA_SOLICITUDES, type SolicitudCambio, type Turno } from './api';
import { formatearDia } from './fechas';

/**
 * Acciones de los cambios de turno (HU 2.9) que funcionan sin conexión (ATLAS-ADR-0032): cada una lleva su llave de
 * idempotencia y la hora en que la persona la hizo; sin red se encolan y se envían al reconectar.
 */
export const MODULO_COLA = 'soporte';

const hechaEn = () => new Date().toISOString();

function accion(descripcion: string, ruta: string, cuerpo: Record<string, unknown>, recurso: string) {
  const llave = nuevaLlave();
  return ejecutarOEncolar<SolicitudCambio>({
    llave,
    modulo: MODULO_COLA,
    descripcion,
    ruta,
    metodo: 'POST',
    cuerpo: { ...cuerpo, llaveIdempotencia: llave, hechaEn: hechaEn() },
    recurso,
  });
}

export const solicitarCambio = (turno: Turno, reemplazoPersonaId: string, motivo: string | null): Promise<ResultadoAccion<SolicitudCambio>> =>
  accion(`Solicitar el cambio del turno del ${formatearDia(turno.fecha)}`, RUTA_SOLICITUDES, { turnoId: turno.id, reemplazoPersonaId, motivo }, turno.id);

const deSolicitud = (s: SolicitudCambio) => `del turno del ${formatearDia(s.turno.fecha)}`;

export const responderSolicitud = (s: SolicitudCambio, acepta: boolean, nota: string | null) =>
  accion(`${acepta ? 'Aceptar' : 'Rechazar'} el reemplazo ${deSolicitud(s)}`, `${RUTA_SOLICITUDES}/${encodeURIComponent(s.id)}/respuesta`, { acepta, nota }, s.id);

export const resolverSolicitud = (s: SolicitudCambio, aprueba: boolean, nota: string | null) =>
  accion(`${aprueba ? 'Aprobar' : 'Rechazar'} el cambio ${deSolicitud(s)}`, `${RUTA_SOLICITUDES}/${encodeURIComponent(s.id)}/resolucion`, { aprueba, nota }, s.id);

export const cancelarSolicitud = (s: SolicitudCambio, nota: string | null) =>
  accion(`Cancelar la solicitud ${deSolicitud(s)}`, `${RUTA_SOLICITUDES}/${encodeURIComponent(s.id)}/cancelacion`, { nota }, s.id);

/**
 * Acciones de soporte pendientes de enviar en este dispositivo. Cuando la cola se vacía (se enviaron al reconectar), se
 * refrescan los datos del módulo.
 */
export function usePendientesSoporte(): AccionEncolada[] {
  const cola = useCola();
  const cliente = useQueryClient();
  const pendientes = useMemo(() => cola.filter((a) => a.modulo === MODULO_COLA), [cola]);
  const antes = useRef(pendientes.length);
  useEffect(() => {
    if (pendientes.length < antes.current) void cliente.invalidateQueries({ queryKey: CLAVE });
    antes.current = pendientes.length;
  }, [pendientes.length, cliente]);
  return pendientes;
}

/** Mensaje para la persona según si la acción salió o quedó en la cola. */
export const textoResultado = (resultado: ResultadoAccion<unknown>, enviada: string): string =>
  resultado.estado === 'enviada' ? enviada : 'Sin conexión: quedó pendiente de enviar y se envía sola al reconectar.';
