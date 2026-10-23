import { moduloAsistente } from '../../modulos/asistente/modulo';
import { moduloCatalogo } from '../../modulos/catalogo/modulo';
import { moduloContratos } from '../../modulos/contratos/modulo';
import { moduloDocumentacion } from '../../modulos/documentacion/modulo';
import { moduloEquipo } from '../../modulos/equipo/modulo';
import { moduloSoporte } from '../../modulos/soporte/modulo';
import type { ModuloAtlas } from './contrato';
import { validarRegistro } from './resolver';

/**
 * Lista explícita de módulos del frontend (ATLAS-ADR-0016): agregar uno es una línea aquí y su carpeta en
 * `src/modulos/`. Los del MVP están «próximamente» hasta su fase; cada uno se apaga con su bandera `modulo.<id>`.
 */
export const MODULOS_REGISTRADOS: readonly ModuloAtlas[] = [
  moduloCatalogo,
  moduloEquipo,
  moduloSoporte,
  moduloContratos,
  moduloDocumentacion,
  moduloAsistente,
];

validarRegistro(MODULOS_REGISTRADOS);
