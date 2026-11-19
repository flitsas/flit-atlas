import { PaginaProximamente } from '../../../app/ui/Proximamente';
import { moduloSoporte } from '../modulo';

export default function InicioSoporte() {
  return (
    <PaginaProximamente
      modulo={moduloSoporte}
      fase="F2"
      capacidades={[
        'Ver el calendario de la rotación en lista, semana o mes, también sin conexión.',
        'Solicitar un cambio de turno, proponer reemplazo y seguir su aprobación.',
        'Registrar y consultar compensaciones pendientes.',
        'Recibir recordatorios por push, correo y Teams antes de tu turno.',
        'Ver el turno en tu calendario de Outlook y Teams.',
      ]}
    />
  );
}
