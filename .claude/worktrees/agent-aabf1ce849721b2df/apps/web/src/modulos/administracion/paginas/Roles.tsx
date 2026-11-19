import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Trash2, Users } from 'lucide-react';
import { type FormEvent, useEffect, useId, useState } from 'react';
import { useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { useSesion } from '../../../app/sesion';
import { formatearFecha, iniciales } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Avatar } from '../../../app/ui/Marca';
import { Boton, cn, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  type Asignacion,
  asignarRol,
  BUSQUEDA_MINIMA,
  buscarPersonas,
  crearMapeo,
  eliminarMapeo,
  esGuid,
  finDelDiaBogota,
  listarAsignaciones,
  listarMapeos,
  listarRoles,
  type MapeoGrupo,
  motivoNoRetirable,
  type OrigenAsignacion,
  type PersonaEncontrada,
  retirarAsignacion,
  type RolAtlas,
  rolesMapeables,
  type SolicitudAsignacion,
  type SolicitudMapeo,
} from '../api';
import { type Aviso, avisoDeError, claseCampo, Confirmar, MostrarAviso } from '../comunes';

const CLAVE_MAPEOS = ['administracion', 'roles', 'mapeos'] as const;
const CLAVE_ROLES = ['administracion', 'roles', 'catalogo'] as const;

/** Espera tras la última tecla antes de buscar, y resultados por búsqueda (el servidor admite hasta 50). */
const RETRASO_BUSQUEDA_MS = 300;
const LIMITE_BUSQUEDA = 20;

const ORIGENES: Record<OrigenAsignacion, string> = {
  manual: 'Manual',
  grupo_entra: 'Grupo de Entra',
  invitacion: 'Invitación',
  emergencia: 'Emergencia',
};

/**
 * Roles (HU 1.4, ATLAS-ADR-0023): los grupos de Entra ID que otorgan roles internos al entrar y las asignaciones de una
 * persona con su origen y vigencia, que se encuentra con el buscador de personas (por nombre o correo). Ver exige
 * `atlas.roles.ver`; crear o eliminar un mapeo y asignar o retirar un rol, `atlas.roles.gestionar` y un ingreso reciente
 * (el servidor responde `reautenticacion_requerida`). Reemplaza el `fetch`
 * desde la consola del arranque de un ambiente con la cuenta de emergencia (ATLAS-ADR-0027).
 */
export default function Roles() {
  const { yo } = useSesion();
  const puedeGestionar = yo.permisos?.includes('atlas.roles.gestionar') ?? false;
  const roles = useQuery({ queryKey: CLAVE_ROLES, queryFn: listarRoles, staleTime: Infinity });
  const [aviso, setAviso] = useState<Aviso>(null);
  const nombreDe = (codigo: string) => roles.data?.find((r) => r.codigo === codigo)?.nombre ?? codigo;

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-5">
      <header>
        <h1 className="text-2xl font-semibold">Asignación de roles</h1>
        <p className="mt-1 max-w-3xl text-sm text-atlas-texto-suave">
          Qué grupos de Entra ID otorgan cada rol interno y qué roles tiene una persona. Todo empleado tiene además el rol
          Lector, sin asignarlo. Lo que cada rol permite está en Roles y permisos.
        </p>
      </header>
      <MostrarAviso aviso={aviso} />
      <SeccionMapeos roles={roles.data} nombreDe={nombreDe} puedeGestionar={puedeGestionar} aviso={aviso} setAviso={setAviso} />
      <SeccionAsignaciones
        personaPropia={yo.personaId}
        roles={roles.data}
        nombreDe={nombreDe}
        puedeGestionar={puedeGestionar}
        setAviso={setAviso}
      />
    </div>
  );
}

/** Ejecuta una acción y deja su resultado en el aviso de la página; devuelve si salió bien. */
async function ejecutar(setAviso: (a: Aviso) => void, trabajo: () => Promise<unknown>, exito: string, error: string): Promise<boolean> {
  setAviso(null);
  try {
    await trabajo();
    setAviso({ tono: 'exito', texto: exito });
    return true;
  } catch (e) {
    setAviso(avisoDeError(e, error));
    return false;
  }
}

function SeccionMapeos({
  roles,
  nombreDe,
  puedeGestionar,
  aviso,
  setAviso,
}: {
  roles: RolAtlas[] | undefined;
  nombreDe: (codigo: string) => string;
  puedeGestionar: boolean;
  aviso: Aviso;
  setAviso: (a: Aviso) => void;
}) {
  const cliente = useQueryClient();
  const mapeos = useQuery({ queryKey: CLAVE_MAPEOS, queryFn: listarMapeos });
  const [nuevo, setNuevo] = useState(false);
  const [eliminando, setEliminando] = useState<MapeoGrupo | null>(null);
  const [errorHoja, setErrorHoja] = useState<Aviso>(null);

  const accion = async (trabajo: () => Promise<unknown>, exito: string, error: string) => {
    const ok = await ejecutar(setAviso, trabajo, exito, error);
    if (ok) await cliente.invalidateQueries({ queryKey: CLAVE_MAPEOS });
    return ok;
  };

  return (
    <Tarjeta
      titulo="Grupos de Entra ID"
      accion={
        puedeGestionar && (
          <Boton
            onClick={() => {
              setErrorHoja(null);
              setAviso(null);
              setNuevo(true);
            }}
          >
            <Plus className="size-4" aria-hidden="true" />
            Nuevo mapeo
          </Boton>
        )
      }
    >
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Los miembros de cada grupo reciben el rol en su siguiente ingreso con Microsoft, sumado a sus asignaciones manuales.
      </p>
      {mapeos.isPending && <p role="status">Cargando mapeos…</p>}
      {mapeos.isError && <p role="alert">No se pudieron cargar los mapeos de grupos.</p>}
      {mapeos.data?.length === 0 && (
        <p className="rounded-xl border border-dashed border-border p-4 text-sm">
          Aún no hay grupos mapeados: solo entran con el rol Lector.{' '}
          {puedeGestionar && 'Empieza por el grupo de administración con Superadministrador.'}
        </p>
      )}
      {mapeos.data && mapeos.data.length > 0 && (
        <ul aria-label="Mapeos de grupos de Entra" className="flex flex-col divide-y divide-border">
          {mapeos.data.map((m) => (
            <li key={m.id} className="flex min-w-0 flex-col gap-2 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <h3 className="font-semibold break-words">{m.grupoNombre}</h3>
                <p className="text-xs text-atlas-texto-suave">
                  Object id <code className="break-all">{m.grupoId}</code>
                </p>
                <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-atlas-texto-suave">
                  <Insignia>{nombreDe(m.rol)}</Insignia>
                  <span>Desde el {formatearFecha(new Date(m.creadoEn))}</span>
                </p>
              </div>
              {puedeGestionar && (
                <Boton variante="fantasma" className="self-start sm:self-center" onClick={() => setEliminando(m)} aria-label={`Eliminar el mapeo de ${m.grupoNombre} a ${nombreDe(m.rol)}`}>
                  <Trash2 className="size-4" aria-hidden="true" />
                  Eliminar
                </Boton>
              )}
            </li>
          ))}
        </ul>
      )}

      <Hoja abierta={nuevo} alCerrar={() => setNuevo(false)} titulo="Nuevo mapeo de grupo">
        {/* El error se repite dentro de la hoja: el diálogo tapa el aviso de la página. */}
        {nuevo && (errorHoja ?? (aviso?.tono === 'error' ? aviso : null)) && (
          <div className="px-4 pt-2">
            <MostrarAviso aviso={errorHoja ?? aviso} />
          </div>
        )}
        {nuevo && (
          <FormularioMapeo
            roles={roles}
            alEnviar={async (solicitud) => {
              setErrorHoja(null);
              const ok = await accion(
                () => crearMapeo(solicitud),
                `${solicitud.grupoNombre} otorga ahora ${nombreDe(solicitud.rol)}.`,
                'No se pudo crear el mapeo.',
              );
              if (ok) setNuevo(false);
            }}
            alError={setErrorHoja}
          />
        )}
      </Hoja>

      <Confirmar
        abierta={eliminando !== null}
        titulo="Eliminar mapeo"
        textoAccion="Eliminar"
        alCerrar={() => setEliminando(null)}
        alConfirmar={() => {
          const m = eliminando;
          setEliminando(null);
          if (m) void accion(() => eliminarMapeo(m.id), `${m.grupoNombre} ya no otorga ${nombreDe(m.rol)}.`, 'No se pudo eliminar el mapeo.');
        }}
      >
        <p>
          ¿Eliminar el mapeo de <strong>{eliminando?.grupoNombre}</strong> a <strong>{eliminando && nombreDe(eliminando.rol)}</strong>? Si ningún
          otro grupo otorga ese rol, quienes lo tenían por el grupo lo pierden ya y se cierran sus sesiones.
        </p>
      </Confirmar>
    </Tarjeta>
  );
}

function FormularioMapeo({
  roles,
  alEnviar,
  alError,
}: {
  roles: RolAtlas[] | undefined;
  alEnviar: (s: SolicitudMapeo) => Promise<void>;
  alError: (a: Aviso) => void;
}) {
  const nombreRadio = useId();
  const [grupoId, setGrupoId] = useState('');
  const [grupoNombre, setGrupoNombre] = useState('');
  const [rol, setRol] = useState('');
  const [enviando, setEnviando] = useState(false);
  if (!roles) return <p role="status" className="px-4 pb-5">Cargando roles…</p>;
  const opciones = rolesMapeables(roles);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!esGuid(grupoId)) {
      alError({ tono: 'error', texto: 'El object id del grupo es un GUID, como 00000000-0000-0000-0000-000000000000. Cópialo de Entra ID.' });
      return;
    }
    if (!rol) {
      alError({ tono: 'error', texto: 'Elige el rol que otorga el grupo.' });
      return;
    }
    setEnviando(true);
    await alEnviar({ grupoId: grupoId.trim().toLowerCase(), grupoNombre: grupoNombre.trim(), rol });
    setEnviando(false);
  }

  return (
    <form onSubmit={(e) => void enviar(e)} aria-label="Datos del mapeo" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <label className="text-sm font-medium">
        Object id del grupo
        <input
          required
          autoComplete="off"
          spellCheck={false}
          inputMode="text"
          value={grupoId}
          onChange={(e) => setGrupoId(e.target.value)}
          aria-describedby={`${nombreRadio}-ayuda`}
          className={cn(claseCampo, 'font-mono text-sm')}
        />
        <span id={`${nombreRadio}-ayuda`} className="mt-1 block text-xs font-normal text-atlas-texto-suave">
          En Entra ID: Grupos → el grupo → Información general → Id. de objeto.
        </span>
      </label>
      <label className="text-sm font-medium">
        Nombre del grupo
        <input required maxLength={200} value={grupoNombre} onChange={(e) => setGrupoNombre(e.target.value)} className={claseCampo} />
      </label>
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="text-sm font-medium">Rol que otorga</legend>
        {opciones.map((r) => (
          <label
            key={r.codigo}
            className={cn(
              'flex min-h-toque cursor-pointer items-start gap-3 rounded-xl border p-3 text-sm',
              r.codigo === rol ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card',
            )}
          >
            <input
              type="radio"
              name={nombreRadio}
              value={r.codigo}
              checked={r.codigo === rol}
              onChange={() => setRol(r.codigo)}
              className="mt-0.5 size-4 shrink-0 accent-[var(--flit-brand-ink)]"
            />
            <span className="min-w-0">
              <span className="block font-medium">{r.nombre}</span>
              <span className="block text-atlas-texto-suave">{r.descripcion}</span>
            </span>
          </label>
        ))}
      </fieldset>
      {rol === 'atlas.superadmin' && (
        <p className="rounded-xl border border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] p-3 text-sm text-[var(--badge-info-fg)]">
          Superadministrador recibe todos los permisos de Atlas. Úsalo solo con el grupo de administración.
        </p>
      )}
      <Boton type="submit" className="min-h-12" disabled={enviando}>
        Crear mapeo
      </Boton>
    </form>
  );
}

/** El valor después de `ms` sin cambios: la búsqueda no consulta al servidor con cada tecla. */
function useRetrasado<T>(valor: T, ms: number): T {
  const [retrasado, setRetrasado] = useState(valor);
  useEffect(() => {
    const espera = window.setTimeout(() => setRetrasado(valor), ms);
    return () => window.clearTimeout(espera);
  }, [valor, ms]);
  return retrasado;
}

const ESTADOS_PERSONA: Record<PersonaEncontrada['estado'], string | null> = { activa: null, inactiva: 'Inactiva', suspendida: 'Suspendida' };

/** Tipo, organización, estado y si es la cuenta de emergencia, como insignias. */
function InsigniasPersona({ persona }: { persona: PersonaEncontrada }) {
  const estado = ESTADOS_PERSONA[persona.estado];
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      <Insignia tono="neutral">{persona.tipo === 'externo' ? `Externo · ${persona.organizacion.nombre}` : 'Empleado'}</Insignia>
      {estado && <Insignia tono="neutral">{estado}</Insignia>}
      {persona.emergencia && <Insignia>Emergencia</Insignia>}
    </span>
  );
}

function SeccionAsignaciones({
  personaPropia,
  roles,
  nombreDe,
  puedeGestionar,
  setAviso,
}: {
  personaPropia: string;
  roles: RolAtlas[] | undefined;
  nombreDe: (codigo: string) => string;
  puedeGestionar: boolean;
  setAviso: (a: Aviso) => void;
}) {
  const cliente = useQueryClient();
  const idAyuda = useId();
  const [parametros, setParametros] = useSearchParams();
  const persona = parametros.get('persona') ?? '';
  const [texto, setTexto] = useState('');
  const consulta = useRetrasado(texto.trim(), RETRASO_BUSQUEDA_MS);
  const [todas, setTodas] = useState(false);
  const [retirando, setRetirando] = useState<Asignacion | null>(null);
  const [asignando, setAsignando] = useState(false);
  const [errorHoja, setErrorHoja] = useState<Aviso>(null);
  const clave = ['administracion', 'roles', 'asignaciones', persona, todas] as const;
  const asignaciones = useQuery({ queryKey: clave, queryFn: () => listarAsignaciones(persona, todas), enabled: esGuid(persona) });
  // Un GUID completo no se busca: se abre directo con Enter. Al vaciar el campo, los resultados se van sin esperar.
  const buscando = consulta.length >= BUSQUEDA_MINIMA && texto.trim().length >= BUSQUEDA_MINIMA && !esGuid(consulta);
  const resultados = useQuery({
    queryKey: ['administracion', 'roles', 'personas', consulta],
    queryFn: ({ signal }) => buscarPersonas(consulta, LIMITE_BUSQUEDA, signal),
    enabled: buscando,
    staleTime: 30_000,
    placeholderData: keepPreviousData,
  });
  // Nombre, correo y tipo de la persona abierta (también si llegó con ?persona=<id>, p. ej. desde una invitación).
  const seleccionada = useQuery({
    queryKey: ['administracion', 'roles', 'persona', persona],
    queryFn: async ({ signal }) => (await buscarPersonas(persona, 1, signal)).personas[0] ?? null,
    enabled: esGuid(persona),
    retry: false,
  });

  const ver = (id: string) => {
    setTexto('');
    setParametros(id ? { persona: id } : {}, { replace: true });
  };

  const recargarPersona = () => cliente.invalidateQueries({ predicate: (c) => c.queryKey[1] === 'roles' && c.queryKey[3] === persona });

  return (
    <Tarjeta titulo="Asignaciones de una persona">
      <p className="mb-3 text-sm text-atlas-texto-suave">
        Busca a la persona por nombre o correo para ver sus roles con su origen y vigencia. También se abre desde una invitación
        aceptada o pegando su id de persona de Atlas.
      </p>
      <form
        role="search"
        aria-label="Buscar personas"
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const valor = texto.trim();
          if (esGuid(valor)) ver(valor.toLowerCase());
          else if (buscando && resultados.data?.personas.length === 1) ver(resultados.data.personas[0]!.id);
        }}
      >
        <label className="min-w-0 flex-1 basis-64 text-sm font-medium">
          Nombre, correo o id
          <input
            type="search"
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            autoComplete="off"
            spellCheck={false}
            enterKeyHint="search"
            maxLength={100}
            aria-describedby={idAyuda}
            className={claseCampo}
          />
          <span id={idAyuda} className="mt-1 block text-xs font-normal text-atlas-texto-suave">
            Al menos {BUSQUEDA_MINIMA} letras; no importan las mayúsculas ni las tildes.
          </span>
        </label>
        <Boton type="button" variante="secundario" className="min-h-12" onClick={() => ver(personaPropia)}>
          <Users className="size-4" aria-hidden="true" />
          Las mías
        </Boton>
      </form>

      {buscando && (
        <div className="mt-3" aria-live="polite">
          {resultados.isPending && <p className="text-sm text-atlas-texto-suave">Buscando…</p>}
          {resultados.isError && <p role="alert">{avisoDeError(resultados.error, 'No se pudo buscar personas.')?.texto}</p>}
          {resultados.data?.personas.length === 0 && <p className="text-sm text-atlas-texto-suave">Nadie coincide con «{consulta}».</p>}
          {resultados.data && resultados.data.personas.length > 0 && (
            <ul aria-label="Personas encontradas" className="flex flex-col divide-y divide-border rounded-2xl border border-border">
              {resultados.data.personas.map((p) => (
                <li key={p.id}>
                  <button
                    type="button"
                    onClick={() => ver(p.id)}
                    className="flex min-h-toque w-full min-w-0 items-start gap-3 px-4 py-3 text-left hover:bg-atlas-activo focus-visible:bg-atlas-activo focus-visible:outline-none"
                  >
                    <Avatar iniciales={iniciales(p.nombre)} foto={p.foto} />
                    <span className="flex min-w-0 flex-col items-start gap-1">
                      <span className="font-semibold break-words">{p.nombre}</span>
                      <span className="text-xs break-all text-atlas-texto-suave">{p.correo}</span>
                      <InsigniasPersona persona={p} />
                      {p.roles.length > 0 && (
                        <span className="text-xs text-atlas-texto-suave">{p.roles.map(nombreDe).join(' · ')}</span>
                      )}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {resultados.data?.hayMas && (
            <p className="mt-2 text-xs text-atlas-texto-suave">Hay más resultados: escribe más del nombre o el correo para afinar.</p>
          )}
        </div>
      )}

      {persona && !esGuid(persona) && (
        <p role="alert" className="mt-3 text-sm">
          El id de la persona es un GUID.
        </p>
      )}
      {esGuid(persona) && (
        <div className="mt-4 flex flex-col gap-3">
          {seleccionada.data && (
            <div className="flex min-w-0 flex-col gap-2 rounded-2xl bg-atlas-activo p-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex min-w-0 items-start gap-3">
                <Avatar iniciales={iniciales(seleccionada.data.nombre)} foto={seleccionada.data.foto} className="size-11" />
                <div className="flex min-w-0 flex-col gap-1">
                  <h3 className="font-semibold break-words">{seleccionada.data.nombre}</h3>
                  <p className="text-xs break-all text-atlas-texto-suave">{seleccionada.data.correo}</p>
                  <InsigniasPersona persona={seleccionada.data} />
                </div>
              </div>
              {puedeGestionar && seleccionada.data.tipo === 'empleado' && (
                <Boton
                  className="self-start sm:self-center"
                  onClick={() => {
                    setErrorHoja(null);
                    setAviso(null);
                    setAsignando(true);
                  }}
                >
                  <Plus className="size-4" aria-hidden="true" />
                  Asignar rol
                </Boton>
              )}
            </div>
          )}
          <label className="flex min-h-toque cursor-pointer items-center gap-3 text-sm">
            <input type="checkbox" checked={todas} onChange={(e) => setTodas(e.target.checked)} className="size-5" />
            Incluir las vencidas y las que aún no empiezan
          </label>
          {asignaciones.isPending && <p role="status">Cargando asignaciones…</p>}
          {asignaciones.isError && (
            <p role="alert">
              {asignaciones.error instanceof ErrorApi && asignaciones.error.estado === 404
                ? 'No hay ninguna persona con ese id.'
                : avisoDeError(asignaciones.error, 'No se pudieron cargar las asignaciones.')?.texto}
            </p>
          )}
          {asignaciones.data?.length === 0 && (
            <p className="text-sm text-atlas-texto-suave">Sin asignaciones{todas ? '' : ' vigentes'}: si es empleado, solo tiene el rol Lector.</p>
          )}
          {asignaciones.data && asignaciones.data.length > 0 && (
            <ul aria-label="Asignaciones" className="grid grid-cols-1 gap-3 md:grid-cols-2">
              {asignaciones.data.map((a) => {
                const motivo = motivoNoRetirable(a);
                return (
                  <li key={a.id} className="flex min-w-0 flex-col gap-2 rounded-2xl border border-border p-4">
                    <h3 className="font-semibold">{a.rolNombre}</h3>
                    <div className="flex flex-wrap gap-1.5">
                      <Insignia tono="neutral">{ORIGENES[a.origen]}</Insignia>
                      <Insignia tono={a.vigente ? 'exito' : 'neutral'}>{a.vigente ? 'Vigente' : 'No vigente'}</Insignia>
                      {a.tipoRol === 'externo' && <Insignia tono="neutral">Externo</Insignia>}
                    </div>
                    <dl className="grid grid-cols-2 gap-2 text-xs text-atlas-texto-suave">
                      <div>
                        <dt>Desde</dt>
                        <dd className="text-sm text-foreground">{formatearFecha(new Date(a.vigenteDesde))}</dd>
                      </div>
                      <div>
                        <dt>Vence</dt>
                        <dd className="text-sm text-foreground">{a.vigenteHasta ? formatearFecha(new Date(a.vigenteHasta)) : 'Sin vencimiento'}</dd>
                      </div>
                      <div className="col-span-2">
                        <dt>Alcance</dt>
                        <dd className="text-sm break-all text-foreground">{a.alcance.id ? `${a.alcance.tipo}: ${a.alcance.id}` : 'Global'}</dd>
                      </div>
                    </dl>
                    {puedeGestionar && !motivo && (
                      <Boton variante="fantasma" className="self-start" onClick={() => setRetirando(a)} aria-label={`Retirar ${a.rolNombre}`}>
                        <Trash2 className="size-4" aria-hidden="true" />
                        Retirar
                      </Boton>
                    )}
                    {motivo && <p className="text-xs text-atlas-texto-suave">{motivo}</p>}
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}

      <Hoja abierta={asignando} alCerrar={() => setAsignando(false)} titulo="Asignar rol">
        {asignando && errorHoja && (
          <div className="px-4 pt-2">
            <MostrarAviso aviso={errorHoja} />
          </div>
        )}
        {asignando && seleccionada.data && (
          <FormularioAsignacion
            roles={roles}
            nombrePersona={seleccionada.data.nombre}
            alEnviar={async (solicitud) => {
              setErrorHoja(null);
              setAviso(null);
              try {
                await asignarRol(persona, solicitud);
              } catch (e) {
                setErrorHoja(avisoDeError(e, 'No se pudo asignar el rol.'));
                return;
              }
              setAsignando(false);
              setAviso({ tono: 'exito', texto: `${seleccionada.data!.nombre} tiene ahora ${nombreDe(solicitud.rol)}.` });
              await recargarPersona();
            }}
            alError={setErrorHoja}
          />
        )}
      </Hoja>

      <Confirmar
        abierta={retirando !== null}
        titulo="Retirar asignación"
        textoAccion="Retirar"
        alCerrar={() => setRetirando(null)}
        alConfirmar={() => {
          const a = retirando;
          setRetirando(null);
          if (a) {
            void ejecutar(setAviso, () => retirarAsignacion(a.id), `Se retiró ${a.rolNombre}.`, 'No se pudo retirar la asignación.').then(
              async (ok) => {
                if (ok) await recargarPersona();
              },
            );
          }
        }}
      >
        <p>
          ¿Retirar <strong>{retirando?.rolNombre}</strong>? La persona pierde sus permisos desde su siguiente petición y se cierran sus sesiones.
        </p>
      </Confirmar>
    </Tarjeta>
  );
}

/** Rol interno asignable (los mismos que otorga un grupo de Entra) con vencimiento opcional, para un empleado. */
function FormularioAsignacion({
  roles,
  nombrePersona,
  alEnviar,
  alError,
}: {
  roles: RolAtlas[] | undefined;
  nombrePersona: string;
  alEnviar: (s: SolicitudAsignacion) => Promise<void>;
  alError: (a: Aviso) => void;
}) {
  const nombreRadio = useId();
  const [rol, setRol] = useState('');
  const [vence, setVence] = useState('');
  const [enviando, setEnviando] = useState(false);
  if (!roles) return <p role="status" className="px-4 pb-5">Cargando roles…</p>;
  const opciones = rolesMapeables(roles);
  const hoy = new Date().toLocaleDateString('en-CA', { timeZone: 'America/Bogota' });

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!rol) {
      alError({ tono: 'error', texto: 'Elige el rol que vas a asignar.' });
      return;
    }
    setEnviando(true);
    await alEnviar({ rol, vigenteHasta: finDelDiaBogota(vence) });
    setEnviando(false);
  }

  return (
    <form onSubmit={(e) => void enviar(e)} aria-label="Datos de la asignación" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <p className="text-sm text-atlas-texto-suave">
        Se suma a los roles de {nombrePersona} desde su siguiente petición, sin cambiar los que recibe por grupos de Entra. Queda en la
        auditoría.
      </p>
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="text-sm font-medium">Rol</legend>
        {opciones.map((r) => (
          <label
            key={r.codigo}
            className={cn(
              'flex min-h-toque cursor-pointer items-start gap-3 rounded-xl border p-3 text-sm',
              r.codigo === rol ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card',
            )}
          >
            <input
              type="radio"
              name={nombreRadio}
              value={r.codigo}
              checked={r.codigo === rol}
              onChange={() => setRol(r.codigo)}
              className="mt-0.5 size-4 shrink-0 accent-[var(--flit-brand-ink)]"
            />
            <span className="min-w-0">
              <span className="block font-medium">{r.nombre}</span>
              <span className="block text-atlas-texto-suave">{r.descripcion}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <label className="text-sm font-medium">
        Vence (opcional)
        <input type="date" min={hoy} value={vence} onChange={(e) => setVence(e.target.value)} className={claseCampo} />
        <span className="mt-1 block text-xs font-normal text-atlas-texto-suave">Sin fecha, no vence. Vence al final de ese día, hora de Bogotá.</span>
      </label>
      {rol === 'atlas.superadmin' && (
        <p className="rounded-xl border border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] p-3 text-sm text-[var(--badge-info-fg)]">
          Superadministrador recibe todos los permisos de Atlas.
        </p>
      )}
      <Boton type="submit" className="min-h-12" disabled={enviando}>
        Asignar rol
      </Boton>
    </form>
  );
}
