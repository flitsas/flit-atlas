import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useEnLinea } from '../pwa/conexion';
import { marcarLeida, marcarTodasLeidas, obtenerNotificaciones } from './api';

export const CLAVE_NOTIFICACIONES = ['plataforma', 'notificaciones'] as const;

/** Bandeja del centro de notificaciones; se refresca cada minuto y cuando llega un push (Shell). */
export function useBandeja() {
  const enLinea = useEnLinea();
  return useQuery({
    queryKey: CLAVE_NOTIFICACIONES,
    queryFn: () => obtenerNotificaciones(),
    refetchInterval: enLinea ? 60_000 : false,
    enabled: enLinea,
    staleTime: 30_000,
  });
}

export function useMarcarLeidas() {
  const cliente = useQueryClient();
  const invalidar = () => cliente.invalidateQueries({ queryKey: CLAVE_NOTIFICACIONES });
  return {
    una: useMutation({ mutationFn: marcarLeida, onSettled: invalidar }),
    todas: useMutation({ mutationFn: marcarTodasLeidas, onSettled: invalidar }),
  };
}
