import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MailPlus, Plus, Trash2 } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';

import { useSesion } from '../../../app/sesion';
import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, cn, Insignia } from '../../../app/ui/Tarjeta';
import {
  type Alcance,
  crearInvitacion,
  type EstadoInvitacion,
  finDelDiaBogota,
  type Invitacion,
  type InvitacionEnviada,
  listarInvitaciones,
  opcionesInvitacion,
  reenviarInvitacion,
  renovarInvitacion,
  restablecerCuenta,
  revocarInvitacion,
  suspenderInvitacion,
} from '../api';
import { type Aviso, avisoDeError, claseCampo, Confirmar, MostrarAviso } from '../comunes';

const CLAVE = ['administracion', 'invitaciones'] as const;

const ESTADOS: Record<EstadoInvitacion, { texto: string; tono: 'info' | 'neutral' | 'exito' }> = {
  pendiente: { texto: 'Pendiente', tono: 'info' },
  enlace_vencido: { texto: 'Enlace vencido', tono: 'neutral' },
  aceptada: { texto: 'Aceptada', tono: 'exito' },
  acceso_vencido: { texto: 'Acceso vencido', tono: 'neutral' },
  suspendida: { texto: 'Suspendida', tono: 'neutral' },
  revocada: { texto: 'Revocada', tono: 'neutral' },
};

const hoyMas = (dias: number) => new Date(Date.now() + dias * 86_400_000).toLocaleDateString('en-CA', { timeZone: 'America/Bogota' });

const resultadoEnvio = (r: InvitacionEnviada, exito: string): Aviso =>
  r.correoEnviado
    ? { tono: 'exito', texto: exito }
    : { tono: 'error', texto: 'La invitación quedó guardada, pero el correo no salió. Reenvíala en unos minutos.' };

/**
 * Invitaciones de externos (HU 1.5): crear (organización, rol, alcance, vencimiento y permisos extra), reenviar,
 * suspender, renovar y revocar. Un administrador de organización solo ve y gestiona las de la suya.
 */
export default function Invitaciones() {
  const cliente = useQueryClient();
  const { yo } = useSesion();
  const lista = useQuery({ queryKey: CLAVE, queryFn: listarInvitaciones });
  const [nueva, setNueva] = useState(false);
  const [renovando, setRenovando] = useState<Invitacion | null>(null);
  const [revocando, setRevocando] = useState<Invitacion | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const puedeRestablecer = yo.permisos?.includes('atlas.identidad.administrar') ?? false;
  const puedeVerRoles = yo.permisos?.includes('atlas.roles.ver') ?? false;
  const puedeCrearOrganizaciones = yo.permisos?.includes('atlas.organizaciones.gestionar') ?? false;

  async function accion(trabajo: () => Promise<Aviso>, error: string) {
    setAviso(null);
    try {
      setAviso(await trabajo());
      await cliente.invalidateQueries({ queryKey: CLAVE });
      return true;
    } catch (e) {
      setAviso(avisoDeError(e, error));
      return false;
    }
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Invitaciones</h1>
        <Boton
          onClick={() => {
            setAviso(null);
            setNueva(true);
          }}
        >
          <MailPlus className="size-4" aria-hidden="true" />
          Invitar
        </Boton>
      </div>
      <MostrarAviso aviso={aviso} />
      {lista.isPending && <p role="status">Cargando invitaciones…</p>}
      {lista.isError && <p role="alert">No se pudieron cargar las invitaciones.</p>}
      {lista.data?.length === 0 && <p className="text-atlas-texto-suave">Aún no hay invitaciones.</p>}
      {lista.data && lista.data.length > 0 && (
        <ul aria-label="Invitaciones" className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {lista.data.map((i) => (
            <li key={i.id} className="flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
              <div className="min-w-0">
                <h2 className="truncate font-semibold">{i.nombre ?? i.correo}</h2>
                {i.nombre && <p className="truncate text-sm text-atlas-texto-suave">{i.correo}</p>}
                <p className="truncate text-sm">{i.organizacionNombre}</p>
              </div>
              <div className="flex flex-wrap gap-1.5">
                <Insignia tono={ESTADOS[i.estado].tono}>{ESTADOS[i.estado].texto}</Insignia>
                <Insignia tono="neutral">{i.rolNombre}</Insignia>
                {i.permisosExtra.map((p) => (
                  <Insignia key={p} tono="neutral">
                    {p === 'exportar' ? 'Exportar' : 'Sandbox'}
                  </Insignia>
                ))}
              </div>
              <dl className="grid grid-cols-2 gap-2 text-xs text-atlas-texto-suave">
                <div>
                  <dt>Acceso hasta</dt>
                  <dd className="text-sm text-foreground">{i.accesoVenceEn ? formatearFecha(new Date(i.accesoVenceEn)) : '—'}</dd>
                </div>
                <div>
                  <dt>Alcance</dt>
                  <dd className="text-sm text-foreground">{i.alcance.length === 0 ? 'Lo de su organización' : i.alcance.map((a) => `${a.tipo}: ${a.id}`).join(', ')}</dd>
                </div>
                {i.estado === 'pendiente' && (
                  <div className="col-span-2">
                    <dt>Correo</dt>
                    <dd className="text-sm text-foreground">
                      {i.enviadaEn ? `Enviado; el enlace vence el ${formatearFecha(new Date(i.enlaceVenceEn))}` : 'No salió: reenvíalo'}
                    </dd>
                  </div>
                )}
              </dl>
              <div className="flex flex-wrap gap-2">
                {(i.estado === 'pendiente' || i.estado === 'enlace_vencido') && (
                  <Boton
                    variante="secundario"
                    onClick={() => void accion(async () => resultadoEnvio(await reenviarInvitacion(i.id), `Se reenvió la invitación a ${i.correo}.`), 'No se pudo reenviar.')}
                  >
                    Reenviar
                  </Boton>
                )}
                {(i.estado === 'pendiente' || i.estado === 'enlace_vencido' || i.estado === 'aceptada') && (
                  <Boton
                    variante="secundario"
                    onClick={() =>
                      void accion(async () => {
                        await suspenderInvitacion(i.id);
                        return { tono: 'exito', texto: `Invitación de ${i.correo} suspendida.` };
                      }, 'No se pudo suspender.')
                    }
                  >
                    Suspender
                  </Boton>
                )}
                {i.estado !== 'revocada' && (
                  <Boton variante="secundario" onClick={() => setRenovando(i)}>
                    Renovar
                  </Boton>
                )}
                {i.estado !== 'revocada' && (
                  <Boton variante="fantasma" onClick={() => setRevocando(i)} aria-label={`Revocar la invitación de ${i.correo}`}>
                    Revocar
                  </Boton>
                )}
                {puedeRestablecer && i.personaId && i.estado !== 'revocada' && (
                  <Boton
                    variante="fantasma"
                    onClick={() =>
                      void accion(async () => {
                        const r = await restablecerCuenta(i.personaId!);
                        return r.correoEnviado
                          ? { tono: 'exito', texto: `Se quitaron los factores de ${i.correo} y se le envió un enlace de recuperación.` }
                          : { tono: 'error', texto: 'Se quitaron los factores, pero el correo de recuperación no salió.' };
                      }, 'No se pudo restablecer la cuenta.')
                    }
                  >
                    Forzar restablecimiento
                  </Boton>
                )}
                {puedeVerRoles && i.personaId && (
                  <Link
                    to={`/administracion/roles?persona=${encodeURIComponent(i.personaId)}`}
                    className="inline-flex min-h-toque items-center rounded-xl px-3 text-sm font-medium hover:bg-atlas-activo"
                    aria-label={`Ver los roles de ${i.correo}`}
                  >
                    Ver roles
                  </Link>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <Hoja abierta={nueva} alCerrar={() => setNueva(false)} titulo="Nueva invitación">
        {nueva && aviso?.tono === 'error' && (
          <div className="px-4 pt-2">
            <MostrarAviso aviso={aviso} />
          </div>
        )}
        {nueva && (
          <FormularioInvitacion
            puedeCrearOrganizaciones={puedeCrearOrganizaciones}
            alEnviar={async (solicitud) => {
              const ok = await accion(
                async () => resultadoEnvio(await crearInvitacion(solicitud), `Se envió la invitación a ${solicitud.correo}.`),
                'No se pudo crear la invitación.',
              );
              if (ok) setNueva(false);
            }}
          />
        )}
      </Hoja>

      <Hoja abierta={renovando !== null} alCerrar={() => setRenovando(null)} titulo="Renovar invitación">
        {renovando && (
          <FormularioRenovacion
            invitacion={renovando}
            alEnviar={async (vence) => {
              const i = renovando;
              const ok = await accion(async () => {
                const r = await renovarInvitacion(i.id, vence);
                return i.personaId ? { tono: 'exito', texto: `Acceso de ${i.correo} renovado.` } : resultadoEnvio(r, `Se envió un enlace nuevo a ${i.correo}.`);
              }, 'No se pudo renovar.');
              if (ok) setRenovando(null);
            }}
          />
        )}
      </Hoja>

      <Confirmar
        abierta={revocando !== null}
        titulo="Revocar invitación"
        textoAccion="Revocar"
        alCerrar={() => setRevocando(null)}
        alConfirmar={() => {
          const i = revocando;
          setRevocando(null);
          if (i) {
            void accion(async () => {
              await revocarInvitacion(i.id);
              return { tono: 'exito', texto: `Invitación de ${i.correo} revocada.` };
            }, 'No se pudo revocar.');
          }
        }}
      >
        <p>
          Es definitivo: el enlace deja de servir y, si ya entró, pierde su rol y se cierran sus sesiones. ¿Revocar la invitación de{' '}
          <strong>{revocando?.correo}</strong>?
        </p>
      </Confirmar>
    </div>
  );
}

function FormularioInvitacion({
  puedeCrearOrganizaciones,
  alEnviar,
}: {
  puedeCrearOrganizaciones: boolean;
  alEnviar: (s: Parameters<typeof crearInvitacion>[0]) => Promise<void>;
}) {
  const opciones = useQuery({ queryKey: ['administracion', 'invitaciones', 'opciones'], queryFn: opcionesInvitacion });
  const [correo, setCorreo] = useState('');
  const [nombre, setNombre] = useState('');
  const [organizacion, setOrganizacion] = useState('');
  const [rol, setRol] = useState('');
  const [vence, setVence] = useState('');
  const [alcance, setAlcance] = useState<Alcance[]>([]);
  const [extra, setExtra] = useState<string[]>([]);
  const [enviando, setEnviando] = useState(false);

  if (opciones.isPending) return <p role="status" className="px-4 pb-5">Cargando…</p>;
  if (opciones.isError) return <p role="alert" className="px-4 pb-5">No se pudieron cargar las opciones.</p>;
  const o = opciones.data;
  // Solo se invita a organizaciones cliente o proveedor activas: sin ninguna, el formulario no serviría.
  if (o.organizaciones.length === 0) return <SinOrganizaciones puedeCrear={puedeCrearOrganizaciones} />;
  const organizacionElegida = organizacion || o.organizaciones[0]?.id || '';
  const rolElegido = rol || o.roles[0]?.codigo || '';
  const proveedor = rolElegido === 'ext.proveedor';

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    await alEnviar({
      correo: correo.trim(),
      nombre: nombre.trim() || null,
      organizacionId: organizacionElegida,
      rol: rolElegido,
      alcance: alcance.filter((a) => a.id && a.id.trim()).map((a) => ({ tipo: a.tipo, id: a.id!.trim() })),
      accesoVenceEn: finDelDiaBogota(vence),
      permisosExtra: extra,
    });
    setEnviando(false);
  }

  return (
    <form onSubmit={(e) => void enviar(e)} aria-label="Datos de la invitación" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <label className="text-sm font-medium">
        Correo
        <input type="email" required autoComplete="off" value={correo} onChange={(e) => setCorreo(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Nombre (opcional)
        <input value={nombre} maxLength={200} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Organización
        <select required value={organizacionElegida} onChange={(e) => setOrganizacion(e.target.value)} disabled={o.soloSuOrganizacion} className={claseCampo}>
          {o.organizaciones.map((org) => (
            <option key={org.id} value={org.id}>
              {org.nombre}
            </option>
          ))}
        </select>
      </label>
      <label className="text-sm font-medium">
        Rol
        <select required value={rolElegido} onChange={(e) => setRol(e.target.value)} className={claseCampo}>
          {o.roles.map((r) => (
            <option key={r.codigo} value={r.codigo}>
              {r.nombre}
            </option>
          ))}
        </select>
      </label>
      <label className="text-sm font-medium">
        Vencimiento del acceso {proveedor ? '(obligatorio para proveedores)' : `(si lo dejas vacío, ${o.vigenciaMaximaDias} días)`}
        <input type="date" required={proveedor} min={hoyMas(1)} max={hoyMas(o.vigenciaMaximaDias - 1)} value={vence} onChange={(e) => setVence(e.target.value)} className={claseCampo} />
      </label>
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="text-sm font-medium">
          Alcance {proveedor ? '(obligatorio para proveedores)' : '(vacío: lo que se comparta con su organización)'}
        </legend>
        {alcance.map((a, indice) => (
          <div key={indice} className="flex items-end gap-2">
            <label className="w-36 shrink-0 text-xs font-medium">
              Tipo
              <select
                value={a.tipo}
                onChange={(e) => setAlcance(alcance.map((x, j) => (j === indice ? { ...x, tipo: e.target.value } : x)))}
                className={claseCampo}
              >
                {o.tiposAlcance.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
            </label>
            <label className="min-w-0 flex-1 text-xs font-medium">
              Id del recurso
              <input
                value={a.id ?? ''}
                required
                pattern="\S{1,200}"
                onChange={(e) => setAlcance(alcance.map((x, j) => (j === indice ? { ...x, id: e.target.value } : x)))}
                className={claseCampo}
              />
            </label>
            <button
              type="button"
              aria-label="Quitar este alcance"
              onClick={() => setAlcance(alcance.filter((_, j) => j !== indice))}
              className="grid size-12 shrink-0 place-items-center rounded-xl text-atlas-texto-suave hover:bg-atlas-activo"
            >
              <Trash2 className="size-4" aria-hidden="true" />
            </button>
          </div>
        ))}
        <Boton variante="secundario" onClick={() => setAlcance([...alcance, { tipo: o.tiposAlcance[0] ?? 'aplicativo', id: '' }])}>
          <Plus className="size-4" aria-hidden="true" />
          Agregar aplicativo, paquete o documento
        </Boton>
      </fieldset>
      <fieldset className="flex min-w-0 flex-col gap-1">
        <legend className="text-sm font-medium">Permisos extra</legend>
        {o.permisosExtra.map((p) => (
          <label key={p} className="flex min-h-toque cursor-pointer items-center gap-3 text-sm">
            <input
              type="checkbox"
              checked={extra.includes(p)}
              onChange={(e) => setExtra(e.target.checked ? [...extra, p] : extra.filter((x) => x !== p))}
              className="size-5"
            />
            {p === 'exportar' ? 'Exportar documentos y paquetes' : 'Usar el sandbox de los contratos'}
          </label>
        ))}
      </fieldset>
      <p className={cn('text-xs text-atlas-texto-suave')}>
        Atlas envía un correo con un enlace de un solo uso que vence en 72 horas. Si no llega, puedes reenviarlo.
      </p>
      <Boton type="submit" className="min-h-12" disabled={enviando || !organizacionElegida}>
        Enviar invitación
      </Boton>
    </form>
  );
}

function SinOrganizaciones({ puedeCrear }: { puedeCrear: boolean }) {
  return (
    <div role="status" className="flex flex-col gap-3 px-4 pt-2 pb-5 text-sm">
      <p className="font-semibold">Aún no hay a quién invitar</p>
      <p>
        Cada persona externa entra en una organización cliente o proveedor activa, y todavía no hay ninguna. FLIT es la
        organización interna: sus empleados entran con Microsoft, sin invitación.
      </p>
      {puedeCrear ? (
        <Link
          to="/administracion/organizaciones"
          className="inline-flex min-h-12 items-center justify-center rounded-xl bg-flit-primary px-5 font-medium text-white dark:bg-flit-brand dark:text-[#0b1220]"
        >
          Crear una organización
        </Link>
      ) : (
        <p className="text-atlas-texto-suave">Pídele a quien administra las organizaciones de Atlas que cree la del cliente o el proveedor.</p>
      )}
    </div>
  );
}

function FormularioRenovacion({ invitacion, alEnviar }: { invitacion: Invitacion; alEnviar: (vence: string | null) => Promise<void> }) {
  const [vence, setVence] = useState('');
  const [enviando, setEnviando] = useState(false);
  return (
    <form
      aria-label="Renovar invitación"
      className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0"
      onSubmit={(e) => {
        e.preventDefault();
        setEnviando(true);
        void alEnviar(finDelDiaBogota(vence)).finally(() => setEnviando(false));
      }}
    >
      <p className="text-sm">
        {invitacion.personaId
          ? `Extiende el acceso de ${invitacion.correo} y, si estaba suspendida, la reactiva.`
          : `Envía a ${invitacion.correo} un enlace nuevo de 72 horas.`}
      </p>
      <label className="text-sm font-medium">
        Nuevo vencimiento del acceso (vacío: el máximo)
        <input type="date" min={hoyMas(1)} max={hoyMas(89)} value={vence} onChange={(e) => setVence(e.target.value)} className={claseCampo} />
      </label>
      <Boton type="submit" className="min-h-12" disabled={enviando}>
        Renovar
      </Boton>
    </form>
  );
}
