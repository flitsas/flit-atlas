import { formatearFecha, proximoSabado } from '../../../app/ui/formato';
import { WidgetProximamente } from '../../../app/ui/Proximamente';

export default function EsteSabado() {
  const sabado = proximoSabado();
  return (
    <WidgetProximamente
      ruta="/soporte"
      sobre="el soporte de los sábados"
      fase="F2"
      destacado={
        <p className="flex items-baseline gap-2">
          <span className="text-2xl font-semibold">Sábado</span>
          <span className="text-lg text-atlas-texto-suave">{formatearFecha(sabado)}</span>
        </p>
      }
    >
      La persona de turno, su horario y su contacto aparecen aquí cuando llegue el módulo de Soporte.
    </WidgetProximamente>
  );
}
