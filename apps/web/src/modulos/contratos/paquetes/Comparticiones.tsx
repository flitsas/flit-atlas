import { Ban, Pencil, Share2 } from 'lucide-react';
import { type FormEvent, type ReactNode, useState } from 'react';

import { ErrorApi, urlEntrarMicrosoft } from '../../../app/api';
import { useSesionOpcional } from '../../../app/sesion';
import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, Tarjeta } from '../../../app/ui/Tarjeta';
import { Etiqueta } from '../componentes/ui';
import { Cargando, claseCampo } from '../guias/componentes';
import {
  cambiarComparticion,
  type Comparticion,
  compartir,
  finDelDia,
  revocarComparticion,
  type SolicitudComparticion,
  TEXTO_ESTADO_COMPARTICION,
  useCambioComparticion,
  useComparticiones,
  useOrganizacionesCompartibles,
} from './apiComparticiones';
import { Casilla, Opciones } from './componentes';

type Aviso = { tono: 'exito' | 'error'; texto: string; reautenticar?: boolean } | null;

const MENSAJES: Record<string, string> = {
  ya_compartido: 'El paquete ya está compartido con esa organización: cambia la compartición que ya existe.',
  revocada: 'La compartición está revocada: compártelo otra vez para reactivarla.',
  no_publicado: 'Publica el paquete antes de compartirlo.',
  sin_permiso: 'Solo el líder técnico o el responsable de los aplicativos de todos sus contratos comparten este paquete.',
};

function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (!(error instanceof ErrorApi)) return { tono: 'error', texto: porDefecto };
  if (error.codigo === 'reautenticacion_requerida') return { tono: 'error', texto: 'Por seguridad, confirma tu identidad antes de compartir.', reautenticar: true };
  if (error.codigo === 'no_publicable') {
    const detalles = Array.isArray(error.cuerpo?.detalles) ? (error.cuerpo.detalles as string[]) : [];
    return { tono: 'error', texto: detalles.length > 0 ? `Esa revisión ya no se puede compartir: ${detalles.join(' · ')}` : error.mensajePara(porDefecto) };
  }
  const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
  return { tono: 'error', texto: conocido ?? error.mensajePara(porDefecto) };
}

function MostrarAviso({ aviso }: { aviso: Aviso }) {
  const sesion = useSesionOpcional();
  if (!aviso) return null;
  // Las cuentas locales (la de emergencia, ATLAS-ADR-0027) no pasan por Microsoft: se confirma volviendo a entrar.
  const local = sesion?.yo.proveedor === 'emergencia' || sesion?.yo.proveedor === 'local';
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={
        aviso.tono === 'error'
          ? 'rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]'
          : 'rounded-xl border border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] p-3 text-sm text-[var(--badge-success-fg)]'
      }
    >
      {aviso.texto}{' '}
      {aviso.reautenticar && !local && (
        <a className="font-semibold underline" href={`${urlEntrarMicrosoft(window.location.pathname)}&reautenticar=true`}>
          Confirmar con Microsoft
        </a>
      )}
      {aviso.reautenticar && local && (
        <button type="button" className="font-semibold underline" onClick={() => void sesion?.cerrarSesion()}>
          Salir y volver a entrar
        </button>
      )}
    </div>
  );
}

const fecha = (iso: string | null) => (iso ? formatearFecha(new Date(iso)) : null);

/**
 * «Compartido con» del paquete (HU 4.3, plan 00 §8.9 «Paquetes»): cada organización con su vigencia, exportar sí/no,
 * sandbox sí/no y la revisión fijada (o «sigue la vigente»); compartir, cambiar y revocar en hojas inferiores. Las
 * personas de esa organización lo ven en el portal desde su siguiente petición.
 */
export function Comparticiones({ paqueteId, revisiones, vigente }: { paqueteId: string; revisiones: number[]; vigente: number | null }) {
  const lista = useComparticiones(paqueteId);
  const [editando, setEditando] = useState<Comparticion | 'nueva' | null>(null);
  const [revocando, setRevocando] = useState<Comparticion | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const revocar = useCambioComparticion(paqueteId, (c: Comparticion) => revocarComparticion(paqueteId, c.id));

  return (
    <Tarjeta
      titulo="Compartido con"
      accion={
        lista.data?.puedeCompartir ? (
          <Boton variante="secundario" onClick={() => setEditando('nueva')}>
            <Share2 className="size-4" aria-hidden="true" /> Compartir
          </Boton>
        ) : undefined
      }
    >
      <div className="flex flex-col gap-3">
        <MostrarAviso aviso={aviso} />
        {lista.isPending && <Cargando etiqueta="Cargando con quién se comparte" />}
        {lista.isError && <p role="alert">No pudimos cargar con quién se comparte el paquete.</p>}
        {lista.data?.elementos.length === 0 && (
          <p className="text-sm text-atlas-texto-suave">
            Todavía no se comparte con ninguna organización.
            {!lista.data.puedeCompartir && vigente === null && ' Publícalo para poder compartirlo.'}
          </p>
        )}
        {lista.data && lista.data.elementos.length > 0 && (
          <ul aria-label="Organizaciones con las que se comparte" className="flex flex-col gap-2">
            {lista.data.elementos.map((c) => (
              <li key={c.id} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
                <div className="flex min-w-0 flex-wrap items-center gap-2">
                  <span className="font-semibold break-words">{c.organizacion.nombre}</span>
                  {c.organizacion.tipo && <Etiqueta>{c.organizacion.tipo === 'cliente' ? 'Cliente' : 'Proveedor'}</Etiqueta>}
                  <Etiqueta tono={c.estado === 'vigente' ? 'exito' : c.estado === 'programada' ? 'info' : 'neutral'}>{TEXTO_ESTADO_COMPARTICION[c.estado]}</Etiqueta>
                  {c.organizacion.estado === 'suspendida' && <Etiqueta tono="alerta">Organización suspendida</Etiqueta>}
                </div>
                <dl className="grid grid-cols-1 gap-1 text-sm sm:grid-cols-2">
                  <Dato titulo="Revisión">{c.revisionFijada ? `Fijada: la ${c.revisionFijada}` : `Sigue la vigente${c.revisionQueVe ? ` (la ${c.revisionQueVe})` : ''}`}</Dato>
                  <Dato titulo="Vigencia">
                    Desde el {fecha(c.vigenteDesde)}
                    {c.vigenteHasta ? ` hasta el ${fecha(c.vigenteHasta)}` : ', sin vencimiento'}
                  </Dato>
                  <Dato titulo="Exportar">{c.puedeExportar ? 'Sí' : 'No'}</Dato>
                  <Dato titulo="Sandbox">{c.puedeSandbox ? 'Sí' : 'No'}</Dato>
                </dl>
                <p className="text-xs text-atlas-texto-suave">
                  Compartió {c.otorgadoPor.nombre}
                  {c.revocadaEn ? ` · revocada el ${fecha(c.revocadaEn)}` : ''}
                </p>
                {c.puedeGestionar && (
                  <div className="flex flex-wrap gap-2">
                    <Boton variante="secundario" onClick={() => setEditando(c)} aria-label={`Cambiar la compartición con ${c.organizacion.nombre}`}>
                      <Pencil className="size-4" aria-hidden="true" /> Cambiar
                    </Boton>
                    <Boton variante="fantasma" onClick={() => setRevocando(c)} aria-label={`Revocar la compartición con ${c.organizacion.nombre}`}>
                      <Ban className="size-4" aria-hidden="true" /> Revocar
                    </Boton>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <Hoja
        abierta={editando !== null}
        alCerrar={() => setEditando(null)}
        titulo={editando === 'nueva' ? 'Compartir con una organización' : `Cambiar la compartición${editando ? ` con ${editando.organizacion.nombre}` : ''}`}
      >
        {editando !== null && (
          <Formulario
            key={editando === 'nueva' ? 'nueva' : editando.id}
            paqueteId={paqueteId}
            existente={editando === 'nueva' ? null : editando}
            revisiones={revisiones}
            alTerminar={(texto) => {
              setEditando(null);
              setAviso({ tono: 'exito', texto });
            }}
          />
        )}
      </Hoja>

      <Hoja abierta={revocando !== null} alCerrar={() => setRevocando(null)} titulo="Revocar la compartición">
        <div className="flex flex-col gap-3 px-4 pt-2 pb-5 text-sm">
          <p>
            {revocando?.organizacion.nombre} deja de ver el paquete desde ya: sus personas lo pierden en el portal sin hacer nada más. La compartición queda
            en la historia y puedes volver a compartirlo.
          </p>
          <Boton
            disabled={revocar.isPending}
            onClick={() =>
              revocando &&
              revocar.mutate(revocando, {
                onSuccess: (c) => {
                  setRevocando(null);
                  setAviso({ tono: 'exito', texto: `Revocada: ${c.organizacion.nombre} ya no ve el paquete.` });
                },
                onError: (e) => {
                  setRevocando(null);
                  setAviso(avisoDeError(e, 'No se pudo revocar la compartición.'));
                },
              })
            }
          >
            <Ban className="size-4" aria-hidden="true" /> Revocar
          </Boton>
          <Boton variante="secundario" onClick={() => setRevocando(null)}>
            Cancelar
          </Boton>
        </div>
      </Hoja>
    </Tarjeta>
  );
}

function Dato({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-wrap gap-x-2">
      <dt className="text-xs text-atlas-texto-suave">{titulo}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </div>
  );
}

function Formulario({
  paqueteId,
  existente,
  revisiones,
  alTerminar,
}: {
  paqueteId: string;
  existente: Comparticion | null;
  revisiones: number[];
  alTerminar: (texto: string) => void;
}) {
  const organizaciones = useOrganizacionesCompartibles(existente === null);
  const [organizacion, setOrganizacion] = useState('');
  const [revision, setRevision] = useState(existente?.revisionFijada ? String(existente.revisionFijada) : 'vigente');
  const [exportar, setExportar] = useState(existente?.puedeExportar ?? true);
  const [sandbox, setSandbox] = useState(existente?.puedeSandbox ?? false);
  const [hasta, setHasta] = useState(existente?.vigenteHasta ? existente.vigenteHasta.slice(0, 10) : '');
  const [aviso, setAviso] = useState<Aviso>(null);
  const guardar = useCambioComparticion(paqueteId, (s: SolicitudComparticion) =>
    existente ? cambiarComparticion(paqueteId, existente.id, s) : compartir(paqueteId, s),
  );
  const elegida = organizaciones.data?.find((o) => o.id === organizacion);
  const requiereVencimiento = elegida?.tipo === 'proveedor' || existente?.organizacion.tipo === 'proveedor';

  function enviar(evento: FormEvent) {
    evento.preventDefault();
    setAviso(null);
    if (!existente && !organizacion) {
      setAviso({ tono: 'error', texto: 'Elige la organización.' });
      return;
    }
    guardar.mutate(
      {
        ...(existente ? {} : { organizacionId: organizacion }),
        revisionFijada: revision === 'vigente' ? null : Number(revision),
        puedeExportar: exportar,
        puedeSandbox: sandbox,
        vigenteHasta: hasta ? finDelDia(hasta) : null,
      },
      {
        onSuccess: (c) => alTerminar(existente ? `Guardado: ${c.organizacion.nombre} ve el paquete con estos cambios.` : `Compartido con ${c.organizacion.nombre}.`),
        onError: (e) => setAviso(avisoDeError(e, 'No se pudo guardar la compartición.')),
      },
    );
  }

  return (
    <form onSubmit={enviar} aria-label="Datos de la compartición" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <MostrarAviso aviso={aviso} />
      {!existente && (
        <>
          {organizaciones.isPending && <Cargando etiqueta="Cargando las organizaciones" />}
          {organizaciones.isError && <p role="alert">No pudimos cargar las organizaciones.</p>}
          {organizaciones.data && organizaciones.data.length === 0 && (
            <p className="text-sm text-atlas-texto-suave">No hay clientes ni proveedores activos. Créalos en Administración → Organizaciones.</p>
          )}
          {organizaciones.data && organizaciones.data.length > 0 && (
            <Opciones
              leyenda="Organización"
              nombre="organizacion-comparticion"
              valor={organizacion}
              alCambiar={setOrganizacion}
              opciones={organizaciones.data.map((o) => ({ valor: o.id, etiqueta: `${o.nombre} · ${o.tipo === 'cliente' ? 'cliente' : 'proveedor'}` }))}
            />
          )}
        </>
      )}
      <Opciones
        leyenda="Revisión que verá"
        nombre="revision-comparticion"
        valor={revision}
        alCambiar={setRevision}
        opciones={[{ valor: 'vigente', etiqueta: 'Seguir la vigente' }, ...revisiones.map((n) => ({ valor: String(n), etiqueta: `Fijar la ${n}` }))]}
      />
      <Casilla marcada={exportar} alCambiar={() => setExportar(!exportar)}>
        <span className="block font-medium">Puede exportar</span>
        <span className="text-sm text-atlas-texto-suave">Las personas con el permiso de exportar podrán descargar el paquete.</span>
      </Casilla>
      <Casilla marcada={sandbox} alCambiar={() => setSandbox(!sandbox)}>
        <span className="block font-medium">Puede usar el sandbox</span>
        <span className="text-sm text-atlas-texto-suave">Las personas con el permiso del sandbox podrán probar las operaciones compartidas.</span>
      </Casilla>
      <label className="text-sm font-medium">
        Compartido hasta {requiereVencimiento ? '(obligatorio con un proveedor)' : '(opcional)'}
        <input type="date" required={requiereVencimiento} value={hasta} onChange={(e) => setHasta(e.target.value)} className={claseCampo} />
      </label>
      <Boton type="submit" className="min-h-12" disabled={guardar.isPending}>
        {existente ? 'Guardar cambios' : 'Compartir'}
      </Boton>
    </form>
  );
}
