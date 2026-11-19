import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../app/api';

/** Contrato de `/api/v1/hola` (contracts/openapi/atlas.v1.yaml, etiqueta «Hola»). */
export interface Saludo {
  id: string;
  texto: string;
  organizacionId: string;
  creadoEn: string;
}

export const LARGO_SALUDO = 280;

const CLAVE_SALUDOS = ['hola', 'saludos'] as const;

export async function listarSaludos(): Promise<Saludo[]> {
  return (await (await pedir('/api/v1/hola/saludos')).json()) as Saludo[];
}

export async function crearSaludo(texto: string): Promise<Saludo> {
  return (await (await enviarJson('/api/v1/hola/saludos', 'POST', { texto })).json()) as Saludo;
}

export function useSaludos() {
  return useQuery({ queryKey: CLAVE_SALUDOS, queryFn: listarSaludos, staleTime: 30_000 });
}

export function useCrearSaludo() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: crearSaludo,
    onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE_SALUDOS }),
  });
}
