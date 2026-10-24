import { useEffect, useState } from 'react';
import { useLocation } from 'react-router';

import { ExportarPaquete } from '../exportaciones/ExportarPaquete';
import { conectarAccionPortal } from './acciones';

/** `/portal/paquetes/<id>?exportacion=<id>`: el enlace del aviso «Tu exportación está lista». */
function desdeElEnlace(ruta: string, busqueda: string): { paqueteId: string; exportacion: string } | null {
  const paquete = /\/portal\/paquetes\/([0-9a-f-]{36})\/?$/i.exec(ruta)?.[1];
  const exportacion = new URLSearchParams(busqueda).get('exportacion');
  return paquete && exportacion ? { paqueteId: paquete, exportacion } : null;
}

// Se conecta al cargar el módulo (antes de que se pinte el botón): el componente montado recibe los clics.
let abrir: ((paqueteId: string) => void) | null = null;
conectarAccionPortal('exportar', (paqueteId) => abrir?.(paqueteId));

/**
 * Exportar desde el portal (HU 4.5, ATLAS-ADR-0042): conecta la acción `exportar` de `acciones.ts` (el botón del paquete,
 * que el servidor habilita con la compartición y el permiso) y abre la hoja de exportación con la base
 * `/api/v1/portal`. Con el enlace del aviso, abre directamente el estado de esa exportación.
 */
export function ExportacionDelPortal() {
  const { pathname, search } = useLocation();
  const [abierta, setAbierta] = useState<{ paqueteId: string; exportacion: string | null } | null>(() => desdeElEnlace(pathname, search));

  useEffect(() => {
    const propio = (paqueteId: string) => setAbierta({ paqueteId, exportacion: null });
    abrir = propio;
    return () => {
      if (abrir === propio) abrir = null;
    };
  }, []);

  if (!abierta) return null;
  return (
    <ExportarPaquete
      key={`${abierta.paqueteId}:${abierta.exportacion ?? ''}`}
      base="/api/v1/portal"
      origen={{ paqueteId: abierta.paqueteId }}
      exportacionInicial={abierta.exportacion}
      controlada={{ alCerrar: () => setAbierta(null) }}
    />
  );
}
