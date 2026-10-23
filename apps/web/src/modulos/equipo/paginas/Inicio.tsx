import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloEquipo } from '../modulo';

export default function InicioEquipo() {
  return (
    <PaginaProximamente
      modulo={moduloEquipo}
      fase="F2"
      capacidades={[
        'Ver los responsables principal y secundario de cada producto y módulo.',
        'Consultar el mapa de carga del equipo y resaltar desequilibrios.',
        'Seguir el traspaso acompañado de un secundario nuevo hasta quedar habilitado.',
        'Comparar y restaurar versiones del reparto de responsables.',
        'Preguntar «¿A quién llamo?» desde cualquier aplicativo.',
      ]}
    />
  );
}
