import { useQuery } from '@tanstack/react-query';

import { pedir } from '../../app/api';

/** `GET /api/v1/plataforma/permisos/matriz` (contracts/openapi/atlas.v1.yaml, `MatrizPermisos`). */
export interface RolMatriz {
  codigo: string;
  nombre: string;
  tipo: 'interno' | 'externo' | 'implicito';
  descripcion: string;
  /** Los implícitos (responsable del aplicativo) no se asignan a mano. */
  asignable: boolean;
}

export interface PermisoMatriz {
  codigo: string;
  descripcion: string;
  /** Códigos de los roles que conceden el permiso. */
  roles: string[];
}

export interface ModuloMatriz {
  id: string;
  nombre: string;
  permisos: PermisoMatriz[];
}

export interface MatrizPermisos {
  roles: RolMatriz[];
  modulos: ModuloMatriz[];
}

export async function obtenerMatriz(): Promise<MatrizPermisos> {
  return (await (await pedir('/api/v1/plataforma/permisos/matriz')).json()) as MatrizPermisos;
}

export function useMatriz() {
  // Cambia solo con un despliegue: no hace falta volver a pedirla en la sesión.
  return useQuery({ queryKey: ['plataforma', 'permisos', 'matriz'], queryFn: obtenerMatriz, staleTime: Infinity });
}
