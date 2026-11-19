import { BuscadorGlobal } from '../busqueda/BuscadorGlobal';

/** Buscador fijo del móvil (§8.9); en escritorio la misma búsqueda abre con ⌘K. */
export default function Buscar() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-4">
      <h1 className="text-2xl font-semibold">Buscar</h1>
      <div className="sticky top-[calc(3.5rem+env(safe-area-inset-top))] z-10 -mx-1 rounded-2xl bg-atlas-fondo px-1 pb-1 md:static">
        <BuscadorGlobal />
      </div>
    </div>
  );
}
