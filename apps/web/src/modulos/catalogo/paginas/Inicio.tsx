import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloCatalogo } from '../modulo';

export default function InicioCatalogo() {
  return (
    <PaginaProximamente
      modulo={moduloCatalogo}
      fase="F2"
      capacidades={[
        'Ver la jerarquía producto → módulo → servicio de FLIT 2.0, FLITO, satélites y legados.',
        'Consultar repo, tecnología, ambientes, criticidad y manejo de datos personales.',
        'Abrir la ficha de cada aplicativo con responsables, contratos, documentos y diagramas.',
        'Filtrar por estado: planeado, activo, en retiro o retirado.',
      ]}
    />
  );
}
