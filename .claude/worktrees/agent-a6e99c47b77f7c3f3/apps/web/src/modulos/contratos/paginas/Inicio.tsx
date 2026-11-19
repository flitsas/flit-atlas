import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloContratos } from '../modulo';

export default function InicioContratos() {
  return (
    <PaginaProximamente
      modulo={moduloContratos}
      fase="F3"
      capacidades={[
        'Consultar los contratos OpenAPI, AsyncAPI y proto publicados desde cada repo.',
        'Comparar versiones y ver los cambios que rompen compatibilidad.',
        'Leer las guías de integración y los avisos de deprecación.',
        'Armar paquetes para clientes y exportarlos a PDF, Word, Postman o Excel.',
        'Probar las operaciones contra el sandbox, nunca contra producción.',
      ]}
    />
  );
}
