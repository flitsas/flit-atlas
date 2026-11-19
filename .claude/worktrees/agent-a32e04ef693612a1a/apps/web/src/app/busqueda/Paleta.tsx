import { Hoja } from '../ui/Hoja';
import { BuscadorGlobal } from './BuscadorGlobal';

/** Paleta ⌘K / Ctrl+K de escritorio (§8.9). */
export function Paleta({ abierta, alCerrar }: { abierta: boolean; alCerrar: () => void }) {
  return (
    <Hoja abierta={abierta} alCerrar={alCerrar} titulo="Buscar en Atlas" ocultarTitulo posicion="arriba">
      <div className="p-3 sm:p-4">{abierta && <BuscadorGlobal autoFoco alElegir={alCerrar} />}</div>
    </Hoja>
  );
}
