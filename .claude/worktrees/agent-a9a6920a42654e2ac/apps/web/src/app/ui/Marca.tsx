import { useState } from 'react';

import isotipo from '../../marca/isotipo.svg';
import { cn } from './Tarjeta';

/** Isotipo FLIT con el nombre «Flit Atlas» (ATLAS-ADR-0020: el logotipo de FLIT 2.0 no se usa en Atlas). */
export function MarcaAtlas({ className, tamano = 'normal' }: { className?: string; tamano?: 'normal' | 'grande' }) {
  return (
    <span className={cn('inline-flex items-center gap-2.5 font-semibold tracking-tight', className)}>
      <img src={isotipo} alt="" className={tamano === 'grande' ? 'size-12' : 'size-8'} />
      <span className={tamano === 'grande' ? 'text-2xl' : 'text-lg'}>
        Flit <span className="font-medium opacity-90">Atlas</span>
      </span>
    </span>
  );
}

/**
 * Avatar de una persona: su foto de Microsoft 365 (sincronizada a diario, ATLAS-ADR-0030) o, si no tiene o no carga, sus
 * iniciales. Es decorativo: el nombre siempre va al lado o en un texto para lectores de pantalla.
 */
export function Avatar({ iniciales, foto, className }: { iniciales: string; foto?: string | null; className?: string }) {
  // La URL de la foto cambia con su hash: si falla una, la siguiente se vuelve a intentar.
  const [fallida, setFallida] = useState<string | null>(null);
  const clase = cn(
    'grid size-9 shrink-0 place-items-center overflow-hidden rounded-full bg-degradado-flit text-sm font-semibold text-[#0b1220]',
    className,
  );
  if (foto && fallida !== foto) {
    return (
      <span aria-hidden="true" className={clase}>
        <img
          src={foto}
          alt=""
          loading="lazy"
          decoding="async"
          className="size-full object-cover"
          onError={() => setFallida(foto)}
        />
      </span>
    );
  }
  return (
    <span aria-hidden="true" className={clase}>
      {iniciales}
    </span>
  );
}
