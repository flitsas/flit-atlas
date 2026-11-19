import { Check, Copy, Mail, Phone, TriangleAlert } from 'lucide-react';
import { type ReactNode, useState } from 'react';

import { ErrorApi } from '../../app/api';
import { iniciales } from '../../app/ui/formato';
import { Avatar } from '../../app/ui/Marca';
import { Boton, cn, Insignia } from '../../app/ui/Tarjeta';
import { type DefinicionRol, type Responsable, TEXTO_TIPO, TEXTO_TRASPASO } from './apiResponsables';

/** Piezas de interfaz de los responsables (un módulo no importa a otro: estas son propias, ATLAS-ADR-0026). */
export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

export type Aviso = { tono: 'exito' | 'error'; texto: string } | null;

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    if (error.estado === 403) return { tono: 'error', texto: error.mensajePara('No tienes permiso para esta acción.') };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

export function MostrarAviso({ aviso }: { aviso: Aviso }) {
  if (!aviso) return null;
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={cn(
        'rounded-xl border p-3 text-sm',
        aviso.tono === 'error'
          ? 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]'
          : 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
      )}
    >
      {aviso.texto}
    </div>
  );
}

/** Aviso que no impide seguir (inconsistencias, pendientes, recordatorios). */
export function Advertencia({ children }: { children: ReactNode }) {
  return (
    <div
      role="note"
      className="flex items-start gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm text-[var(--badge-warning-fg)]"
    >
      <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export function InsigniaTraspaso({ responsable }: { responsable: Responsable }) {
  if (responsable.tipo !== 'secundario') return null;
  return (
    <Insignia tono={responsable.estadoTraspaso === 'habilitado' ? 'exito' : 'info'}>{TEXTO_TRASPASO[responsable.estadoTraspaso]}</Insignia>
  );
}

/** Correo y teléfono como enlaces (el teléfono solo llega con `atlas.equipo.ver_contacto`). */
export function Contacto({ correo, telefono }: { correo: string | null; telefono: string | null }) {
  if (!correo && !telefono) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {correo && (
        <a
          href={`mailto:${correo}`}
          className="inline-flex min-h-toque max-w-full items-center gap-2 rounded-xl border border-border px-3 text-sm hover:bg-atlas-activo"
        >
          <Mail className="size-4 shrink-0" aria-hidden="true" />
          <span className="truncate">{correo}</span>
        </a>
      )}
      {telefono && (
        <a
          href={`tel:${telefono.replace(/[^\d+]/g, '')}`}
          className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border px-3 text-sm hover:bg-atlas-activo"
        >
          <Phone className="size-4 shrink-0" aria-hidden="true" />
          {telefono}
        </a>
      )}
    </div>
  );
}

/** Un responsable con su foto, su tipo, su dedicación y su traspaso; con `contacto`, también sus enlaces. */
export function TarjetaResponsable({
  responsable,
  tipo,
  contacto = false,
  acciones,
}: {
  responsable: Responsable | null;
  tipo: 'principal' | 'secundario';
  contacto?: boolean;
  acciones?: ReactNode;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <p className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{TEXTO_TIPO[tipo]}</p>
      {!responsable && <p className="text-sm text-atlas-texto-suave">Sin responsable {tipo}.</p>}
      {responsable && (
        <>
          <div className="flex min-w-0 items-start gap-3">
            <Avatar iniciales={iniciales(responsable.persona.nombre)} foto={responsable.persona.foto} className="size-10" />
            <div className="min-w-0">
              <p className="font-medium break-words">{responsable.persona.nombre}</p>
              {responsable.persona.cargo && <p className="text-xs break-words text-atlas-texto-suave">{responsable.persona.cargo}</p>}
              <div className="mt-1 flex flex-wrap gap-1.5">
                {responsable.dedicacion !== null && <Insignia tono="neutral">~{responsable.dedicacion} %</Insignia>}
                <InsigniaTraspaso responsable={responsable} />
                {!responsable.vigente && <Insignia tono="neutral">Fuera de vigencia</Insignia>}
                {!responsable.persona.activa && <Insignia tono="neutral">Inactiva</Insignia>}
              </div>
            </div>
          </div>
          {contacto && <Contacto correo={responsable.persona.correo} telefono={responsable.persona.telefono} />}
        </>
      )}
      {acciones}
    </div>
  );
}

/** Las definiciones de cada rol tal como están en el §1 del documento de responsables. */
export function Definiciones({ definiciones }: { definiciones: DefinicionRol[] }) {
  if (definiciones.length === 0) return null;
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {definiciones.map((d) => (
        <section key={d.rol} aria-label={d.titulo} className="min-w-0">
          <h3 className="text-sm font-semibold">{d.titulo}</h3>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
            {d.puntos.map((p) => (
              <li key={p} className="break-words">
                {p}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

/** Copia un texto al portapapeles y lo confirma; donde no hay portapapeles, el texto sigue a la vista para copiarlo a mano. */
export function BotonCopiar({ texto, etiqueta }: { texto: string; etiqueta: string }) {
  const [copiado, setCopiado] = useState(false);
  return (
    <Boton
      variante="secundario"
      onClick={() => {
        void navigator.clipboard?.writeText(texto).then(() => {
          setCopiado(true);
          window.setTimeout(() => setCopiado(false), 2000);
        });
      }}
    >
      {copiado ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
      {copiado ? 'Copiado' : etiqueta}
    </Boton>
  );
}

/** Descarga un texto como archivo (funciona en iOS: abre el archivo para compartirlo). */
export function descargar(nombre: string, texto: string, tipo = 'text/markdown') {
  const url = URL.createObjectURL(new Blob([texto], { type: `${tipo};charset=utf-8` }));
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  enlace.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Bloque de texto preformateado que se envuelve (sin scroll horizontal a 360 px). */
export function Bloque({ texto, etiqueta }: { texto: string; etiqueta: string }) {
  return (
    <pre
      aria-label={etiqueta}
      tabIndex={0}
      className="max-h-80 overflow-y-auto rounded-xl border border-border bg-atlas-fondo p-3 text-xs break-words whitespace-pre-wrap"
    >
      {texto}
    </pre>
  );
}
