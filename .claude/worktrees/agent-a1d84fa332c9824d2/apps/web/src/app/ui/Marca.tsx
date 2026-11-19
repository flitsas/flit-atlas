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

export function Avatar({ iniciales, className }: { iniciales: string; className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'grid size-9 shrink-0 place-items-center rounded-full bg-degradado-flit text-sm font-semibold text-[#0b1220]',
        className,
      )}
    >
      {iniciales}
    </span>
  );
}
