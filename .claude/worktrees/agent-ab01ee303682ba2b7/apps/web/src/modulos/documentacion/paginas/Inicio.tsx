import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloDocumentacion } from '../modulo';

export default function InicioDocumentacion() {
  return (
    <PaginaProximamente
      modulo={moduloDocumentacion}
      fase="F5"
      capacidades={[
        'Leer manuales, guías funcionales, runbooks y notas de versión siempre al día.',
        'Ver cualquier versión de un documento y comparar dos.',
        'Escribir documentos funcionales en Atlas con revisión y aprobación del responsable.',
        'Exportar a PDF, Word, HTML o Markdown con la marca FLIT.',
      ]}
    />
  );
}
