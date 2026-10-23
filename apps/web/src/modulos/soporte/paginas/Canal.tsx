import { Tarjeta } from '../../../app/ui/Tarjeta';
import CanalSabado from '../widgets/CanalSabado';

/** Página para externos (`atlas.soporte.ver_canal`): el canal de soporte del sábado, sin nombres de personas. */
export default function Canal() {
  return (
    <div className="mx-auto flex max-w-3xl min-w-0 flex-col gap-4">
      <h1 className="text-2xl font-semibold">Soporte del sábado</h1>
      <Tarjeta etiqueta="Canal de soporte">
        <CanalSabado />
      </Tarjeta>
    </div>
  );
}
