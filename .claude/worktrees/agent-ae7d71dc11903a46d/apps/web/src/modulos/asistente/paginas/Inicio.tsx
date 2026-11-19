import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloAsistente } from '../modulo';

export default function InicioAsistente() {
  return (
    <PaginaProximamente
      modulo={moduloAsistente}
      fase="F7"
      capacidades={[
        'Preguntar cómo funciona un aplicativo y recibir respuestas con citas verificables.',
        'Ver diagramas generados a partir de la documentación y los contratos.',
        'Dictar preguntas por voz en el móvil y compartir las respuestas.',
        'Consultar Atlas desde Claude Code o Cursor por MCP, con tus mismos permisos.',
      ]}
    />
  );
}
