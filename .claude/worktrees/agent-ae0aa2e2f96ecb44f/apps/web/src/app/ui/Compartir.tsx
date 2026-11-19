import { Check, Copy, Mail, Share2, Users } from 'lucide-react';
import { useEffect, useId, useState } from 'react';

import { compartir, copiarEnlace, enlaceInterno, puedeCompartir, urlCorreo, urlTeams } from '../../pwa/compartir';
import { useSesionOpcional } from '../sesion';
import { Hoja } from './Hoja';
import { Boton, cn } from './Tarjeta';

/** Un enlace con aspecto de botón secundario y objetivo táctil de 44 px (§11). */
export const CLASE_ENLACE_BOTON =
  'inline-flex min-h-toque items-center justify-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium text-foreground hover:bg-atlas-activo';

/**
 * Botón «Compartir» (HU 4.6, ATLAS-ADR-0042 «Compartir»): comparte el **enlace interno** de Atlas a lo que se está viendo
 * (contrato, paquete, guía, turno…). Con la hoja nativa (`navigator.share`: Android, iOS, Windows, macOS) la abre en el
 * mismo toque; sin ella, o si falla, abre una hoja inferior con «Copiar enlace», «Abrir en correo» y, para empleados,
 * «Abrir en Teams». El enlace exige sesión: quien lo reciba sin acceso no ve nada. Un externo solo recibe rutas del
 * portal (`/portal/…`), que sí puede abrir.
 */
export function BotonCompartir({
  ruta,
  titulo,
  que,
  etiqueta = 'Compartir',
  className,
}: {
  /** Ruta interna (`/contratos/…`, `/portal/…`); nunca una URL firmada ni externa. */
  ruta: string;
  /** Título de lo compartido: va como título de la hoja nativa y como asunto del correo. */
  titulo: string;
  /** «este contrato», «este paquete»…: para el texto de la hoja. */
  que: string;
  etiqueta?: string;
  className?: string;
}) {
  const [abierta, setAbierta] = useState(false);
  const [fallo, setFallo] = useState(false);
  const enlace = enlaceInterno(ruta);

  async function alTocar() {
    const datos: ShareData = { title: titulo, text: `${titulo} · Flit Atlas`, url: enlace };
    if (puedeCompartir(datos)) {
      const resultado = await compartir(datos);
      // Cancelar la hoja nativa no es un error: no se muestra nada.
      if (resultado === 'compartido' || resultado === 'cancelado') return;
      setFallo(true);
    }
    setAbierta(true);
  }

  return (
    <>
      <Boton variante="secundario" className={className} onClick={() => void alTocar()}>
        <Share2 className="size-4" aria-hidden="true" /> {etiqueta}
      </Boton>
      <Hoja
        abierta={abierta}
        alCerrar={() => {
          setAbierta(false);
          setFallo(false);
        }}
        titulo="Compartir"
      >
        {abierta && <OpcionesEnlace enlace={enlace} titulo={titulo} que={que} fallo={fallo} />}
      </Hoja>
    </>
  );
}

/** Respaldo sin hoja nativa: copiar, correo y Teams (solo empleados: Teams es el de FLIT). */
export function OpcionesEnlace({ enlace, titulo, que, fallo = false }: { enlace: string; titulo: string; que: string; fallo?: boolean }) {
  const externo = useSesionOpcional()?.yo.tipoPrincipal === 'externo';
  const [copia, setCopia] = useState<'copiado' | 'fallo' | null>(null);
  const idEnlace = useId();

  useEffect(() => {
    if (copia !== 'copiado') return;
    const t = setTimeout(() => setCopia(null), 3000);
    return () => clearTimeout(t);
  }, [copia]);

  return (
    <div className="flex min-w-0 flex-col gap-3 px-4 pt-2 pb-5 text-sm">
      {fallo && <p className="text-atlas-texto-suave">No se pudo abrir la hoja de compartir de este dispositivo; usa una de estas opciones.</p>}
      <p className="text-atlas-texto-suave">
        Es el enlace de {que} en Flit Atlas: quien lo abra necesita iniciar sesión y tener acceso. No es un enlace público.
      </p>
      <label htmlFor={idEnlace} className="font-medium">
        Enlace
      </label>
      <input
        id={idEnlace}
        readOnly
        value={enlace}
        onFocus={(e) => e.currentTarget.select()}
        className="min-h-toque w-full min-w-0 rounded-xl border border-border bg-atlas-fondo px-3 text-sm"
      />
      <Boton
        onClick={() => {
          void copiarEnlace(enlace).then((ok) => setCopia(ok ? 'copiado' : 'fallo'));
        }}
      >
        {copia === 'copiado' ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
        {copia === 'copiado' ? 'Enlace copiado' : 'Copiar enlace'}
      </Boton>
      <p role="status" aria-live="polite" className={cn('text-xs', copia === null && 'sr-only')}>
        {copia === 'copiado' && 'Copiamos el enlace al portapapeles.'}
        {copia === 'fallo' && 'No pudimos copiar el enlace: selecciónalo en el campo y cópialo.'}
      </p>
      <a href={urlCorreo(`${titulo} · Flit Atlas`, enlace)} className={CLASE_ENLACE_BOTON}>
        <Mail className="size-4" aria-hidden="true" /> Abrir en correo
      </a>
      {!externo && (
        <a href={urlTeams(enlace, `${titulo} · Flit Atlas`)} target="_blank" rel="noopener noreferrer" className={CLASE_ENLACE_BOTON}>
          <Users className="size-4" aria-hidden="true" /> Abrir en Teams
        </a>
      )}
    </div>
  );
}
