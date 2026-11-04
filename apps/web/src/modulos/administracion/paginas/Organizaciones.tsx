import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Building2, Pencil, Plus, Trash2 } from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Link } from 'react-router';

import { useSesionOpcional } from '../../../app/sesion';
import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, Insignia } from '../../../app/ui/Tarjeta';
import {
  actualizarOrganizacion,
  cambiarEstadoOrganizacion,
  crearOrganizacion,
  eliminarOrganizacion,
  listarOrganizaciones,
  listarPaquetesCompartidos,
  type Organizacion,
  type SolicitudOrganizacion,
} from '../api';
import { type Aviso, avisoDeError, claseCampo, Confirmar, MostrarAviso } from '../comunes';

const CLAVE = ['administracion', 'organizaciones'] as const;
const TIPOS: Record<Organizacion['tipo'], string> = { interna: 'Interna (FLIT)', cliente: 'Cliente', proveedor: 'Proveedor' };

/**
 * Organizaciones: FLIT (interna), clientes y proveedores (HU 1.5). Lista en tarjetas; formularios en hojas inferiores. Con
 * `atlas.contratos.ver_internos`, cada cliente o proveedor muestra los paquetes compartidos con él (HU 4.3).
 */
export default function Organizaciones() {
  const cliente = useQueryClient();
  const verPaquetes = useSesionOpcional()?.yo.permisos?.includes('atlas.contratos.ver_internos') ?? false;
  const lista = useQuery({ queryKey: CLAVE, queryFn: listarOrganizaciones });
  const [editando, setEditando] = useState<Organizacion | 'nueva' | null>(null);
  const [eliminando, setEliminando] = useState<Organizacion | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);

  async function accion(trabajo: () => Promise<unknown>, exito: string, error: string) {
    setAviso(null);
    try {
      await trabajo();
      setAviso({ tono: 'exito', texto: exito });
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
        <h1 className="text-2xl font-semibold">Organizaciones</h1>
        <Boton onClick={() => setEditando('nueva')}>
          <Plus className="size-4" aria-hidden="true" />
          Nueva organización
        </Boton>
      </div>
      <MostrarAviso aviso={aviso} />
      {lista.isPending && <p role="status">Cargando organizaciones…</p>}
      {lista.isError && <p role="alert">No se pudieron cargar las organizaciones.</p>}
      {lista.data && (
        <ul aria-label="Organizaciones" className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {lista.data.map((o) => (
            <li key={o.id} className="flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
              <div className="flex items-start gap-3">
                <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-atlas-activo text-atlas-tinta">
                  <Building2 className="size-5" aria-hidden="true" />
                </span>
                <div className="min-w-0 flex-1">
                  <h2 className="truncate font-semibold">{o.nombre}</h2>
                  <div className="mt-1 flex flex-wrap gap-1.5">
                    <Insignia tono="neutral">{TIPOS[o.tipo]}</Insignia>
                    <Insignia tono={o.estado === 'activa' ? 'exito' : 'neutral'}>{o.estado === 'activa' ? 'Activa' : 'Suspendida'}</Insignia>
                    {o.adminOrganizacionHabilitado && <Insignia>Admin. de la organización</Insignia>}
                  </div>
                </div>
              </div>
              <p className="text-sm text-atlas-texto-suave">
                {o.personas} {o.personas === 1 ? 'persona' : 'personas'} · {o.invitacionesPendientes} {o.invitacionesPendientes === 1 ? 'invitación pendiente' : 'invitaciones pendientes'}
              </p>
              {verPaquetes && o.tipo !== 'interna' && <PaquetesCompartidos organizacion={o} />}
              <div className="flex flex-wrap gap-2">
                <Boton variante="secundario" onClick={() => setEditando(o)} aria-label={`Editar ${o.nombre}`}>
                  <Pencil className="size-4" aria-hidden="true" />
                  Editar
                </Boton>
                {o.tipo !== 'interna' && (
                  <Boton
                    variante="secundario"
                    onClick={() =>
                      void accion(
                        () => cambiarEstadoOrganizacion(o.id, o.estado === 'activa' ? 'suspender' : 'reactivar'),
                        o.estado === 'activa' ? `${o.nombre} quedó suspendida: nadie de ella entra.` : `${o.nombre} está activa de nuevo.`,
                        'No se pudo cambiar el estado.',
                      )
                    }
                  >
                    {o.estado === 'activa' ? 'Suspender' : 'Reactivar'}
                  </Boton>
                )}
                {o.tipo !== 'interna' && o.personas === 0 && (
                  <Boton variante="fantasma" onClick={() => setEliminando(o)} aria-label={`Eliminar ${o.nombre}`}>
                    <Trash2 className="size-4" aria-hidden="true" />
                    Eliminar
                  </Boton>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <FormularioOrganizacion
        organizacion={editando}
        alCerrar={() => setEditando(null)}
        alGuardar={async (solicitud) => {
          const actual = editando;
          const ok = await accion(
            () => (actual === 'nueva' || actual === null ? crearOrganizacion(solicitud) : actualizarOrganizacion(actual.id, solicitud)),
            actual === 'nueva' ? `Se creó ${solicitud.nombre}.` : 'Cambios guardados.',
            'No se pudo guardar la organización.',
          );
          if (ok) setEditando(null);
        }}
      />
      <Confirmar
        abierta={eliminando !== null}
        titulo="Eliminar organización"
        textoAccion="Eliminar"
        alCerrar={() => setEliminando(null)}
        alConfirmar={() => {
          const o = eliminando;
          setEliminando(null);
          if (o) void accion(() => eliminarOrganizacion(o.id), `Se eliminó ${o.nombre}.`, 'No se pudo eliminar.');
        }}
      >
        <p>
          ¿Eliminar <strong>{eliminando?.nombre}</strong>? Solo se puede si no tiene personas ni invitaciones; si las tiene, suspéndela.
        </p>
      </Confirmar>
    </div>
  );
}

function FormularioOrganizacion({
  organizacion,
  alCerrar,
  alGuardar,
}: {
  organizacion: Organizacion | 'nueva' | null;
  alCerrar: () => void;
  alGuardar: (solicitud: SolicitudOrganizacion) => Promise<void>;
}) {
  const existente = organizacion !== null && organizacion !== 'nueva' ? organizacion : null;
  return (
    <Hoja abierta={organizacion !== null} alCerrar={alCerrar} titulo={existente ? 'Editar organización' : 'Nueva organización'}>
      {organizacion !== null && <Campos key={existente?.id ?? 'nueva'} existente={existente} alGuardar={alGuardar} />}
    </Hoja>
  );
}

function Campos({ existente, alGuardar }: { existente: Organizacion | null; alGuardar: (s: SolicitudOrganizacion) => Promise<void> }) {
  const [nombre, setNombre] = useState(existente?.nombre ?? '');
  const [tipo, setTipo] = useState<SolicitudOrganizacion['tipo']>(existente?.tipo ?? 'cliente');
  const [admin, setAdmin] = useState(existente?.adminOrganizacionHabilitado ?? false);
  const [enviando, setEnviando] = useState(false);
  const interna = tipo === 'interna';

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    await alGuardar({ nombre: nombre.trim(), tipo, adminOrganizacionHabilitado: !interna && admin });
    setEnviando(false);
  }

  return (
    <form onSubmit={(e) => void enviar(e)} aria-label="Datos de la organización" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <label className="text-sm font-medium">
        Nombre
        <input required maxLength={200} value={nombre} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
      </label>
      {!interna && (
        <label className="text-sm font-medium">
          Tipo
          <select value={tipo} onChange={(e) => setTipo(e.target.value as SolicitudOrganizacion['tipo'])} className={claseCampo}>
            <option value="cliente">Cliente</option>
            <option value="proveedor">Proveedor</option>
          </select>
        </label>
      )}
      {!interna && (
        <label className="flex min-h-toque cursor-pointer items-start gap-3 text-sm">
          <input type="checkbox" checked={admin} onChange={(e) => setAdmin(e.target.checked)} className="mt-1 size-5 shrink-0" />
          <span>
            <span className="block font-medium">Habilitar el administrador de la organización</span>
            <span className="text-atlas-texto-suave">Quien tenga el rol ext.admin_organizacion podrá invitar a sus colegas, dentro de su propio alcance.</span>
          </span>
        </label>
      )}
      <Boton type="submit" className="min-h-12" disabled={enviando}>
        Guardar
      </Boton>
    </form>
  );
}

const ESTADOS_COMPARTICION = { vigente: 'Vigente', programada: 'Programada', vencida: 'Vencida', revocada: 'Revocada' } as const;

/** Los paquetes compartidos con la organización; se piden al abrir la sección. */
function PaquetesCompartidos({ organizacion }: { organizacion: Organizacion }) {
  const [abierta, setAbierta] = useState(false);
  const paquetes = useQuery({
    queryKey: ['administracion', 'organizaciones', organizacion.id, 'paquetes'],
    queryFn: () => listarPaquetesCompartidos(organizacion.id),
    enabled: abierta,
  });
  return (
    <details className="min-w-0" onToggle={(e) => setAbierta((e.target as HTMLDetailsElement).open)}>
      <summary className="flex min-h-toque cursor-pointer items-center text-sm font-medium">Paquetes compartidos</summary>
      {abierta && paquetes.isPending && <p role="status" className="text-sm">Cargando…</p>}
      {paquetes.isError && <p role="alert" className="text-sm">No se pudieron cargar los paquetes.</p>}
      {paquetes.data?.length === 0 && <p className="text-sm text-atlas-texto-suave">No se le ha compartido ningún paquete.</p>}
      {paquetes.data && paquetes.data.length > 0 && (
        <ul aria-label={`Paquetes compartidos con ${organizacion.nombre}`} className="flex flex-col gap-1">
          {paquetes.data.map((p) => (
            <li key={p.id} className="flex min-w-0 flex-wrap items-center gap-2 text-sm">
              <Link to={`/contratos/paquetes/${p.paqueteId}`} className="inline-flex min-h-toque items-center font-medium break-words text-atlas-tinta hover:underline">
                {p.paqueteNombre}
              </Link>
              <Insignia tono={p.estado === 'vigente' ? 'exito' : 'neutral'}>{ESTADOS_COMPARTICION[p.estado]}</Insignia>
              <span className="text-xs text-atlas-texto-suave">
                {p.revisionQueVe ? `Revisión ${p.revisionQueVe}` : ''}
                {p.vigenteHasta ? ` · hasta el ${formatearFecha(new Date(p.vigenteHasta))}` : ''}
                {` · exportar ${p.puedeExportar ? 'sí' : 'no'} · sandbox ${p.puedeSandbox ? 'sí' : 'no'}`}
              </span>
            </li>
          ))}
        </ul>
      )}
    </details>
  );
}
