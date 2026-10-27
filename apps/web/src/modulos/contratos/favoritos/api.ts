import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { pedir } from '../../../app/api';
import { conRespaldoGuardado } from '../../../pwa/sinConexion';
import {
  type FavoritoInterno,
  type FavoritoPortal,
  guardarFavorito,
  mismoObjetivo,
  type ObjetivoFavorito,
  objetivoDePortal,
  olvidarFavorito,
  PERMISO_INTERNO,
  PERMISO_PORTAL,
  recurso,
  RUTA_FAVORITOS_INTERNOS,
  RUTA_FAVORITOS_PORTAL,
  TOPE_RECURSO_BYTES,
} from './sinConexion';

/**
 * Favoritos (HU 4.9): `/api/v1/portal/favoritos` y `/api/v1/contratos/favoritos` (contracts/openapi/atlas.v1.yaml). Las
 * listas se ven sin conexión (lo guardado al sincronizar); marcar y quitar necesitan red y además guardan o borran en el
 * dispositivo lo que muestra el favorito.
 */
export const CLAVE_FAVORITOS = ['contratos', 'favoritos'] as const;

const enc = encodeURIComponent;

export function useFavoritosPortal(habilitado = true) {
  return useQuery({
    queryKey: [...CLAVE_FAVORITOS, 'portal'],
    queryFn: () =>
      conRespaldoGuardado(recurso(RUTA_FAVORITOS_PORTAL, PERMISO_PORTAL), async () => (await (await pedir(RUTA_FAVORITOS_PORTAL)).json()) as FavoritoPortal[]),
    enabled: habilitado,
    staleTime: 60_000,
  });
}

export function useFavoritosInternos(habilitado = true) {
  return useQuery({
    queryKey: [...CLAVE_FAVORITOS, 'internos'],
    queryFn: () =>
      conRespaldoGuardado(recurso(RUTA_FAVORITOS_INTERNOS, PERMISO_INTERNO), async () => (await (await pedir(RUTA_FAVORITOS_INTERNOS)).json()) as FavoritoInterno[]),
    enabled: habilitado,
    staleTime: 60_000,
  });
}

/** La ruta de la API para marcar o quitar el objetivo. */
export function rutaMarca(o: ObjetivoFavorito): string {
  if (o.ambito === 'interno') return `${RUTA_FAVORITOS_INTERNOS}/contratos/${enc(o.contratoId)}`;
  const base = `${RUTA_FAVORITOS_PORTAL}/paquetes/${enc(o.paqueteId)}`;
  if (o.tipo === 'contrato') return `${base}/contratos/${enc(o.contratoId!)}`;
  if (o.tipo === 'guia') return `${base}/guias/${enc(o.guiaId!)}`;
  return base;
}

/**
 * Marcar o quitar un favorito. Al marcarlo, guarda en el dispositivo lo que muestra (solo lo que el servidor le da a la
 * persona); al quitarlo, borra lo suyo que ningún otro favorito usa.
 */
export function useCambiarFavorito() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async ({ objetivo, marcar }: { objetivo: ObjetivoFavorito; marcar: boolean }) => {
      await pedir(rutaMarca(objetivo), { method: marcar ? 'PUT' : 'DELETE' });
      const portal = cliente.getQueryData<FavoritoPortal[]>([...CLAVE_FAVORITOS, 'portal']) ?? [];
      const internos = cliente.getQueryData<FavoritoInterno[]>([...CLAVE_FAVORITOS, 'internos']) ?? [];
      const restantes: ObjetivoFavorito[] = [
        ...portal.map(objetivoDePortal),
        ...internos.map((f): ObjetivoFavorito => ({ ambito: 'interno', contratoId: f.contratoId })),
      ].filter((o) => !mismoObjetivo(o, objetivo));
      if (marcar) await guardarFavorito(objetivo).catch(() => undefined);
      else await olvidarFavorito(objetivo, restantes).catch(() => undefined);
    },
    onSettled: () => cliente.invalidateQueries({ queryKey: CLAVE_FAVORITOS }),
  });
}

export { TOPE_RECURSO_BYTES };
