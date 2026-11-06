import { useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { formatearFechaHora } from '../../../app/ui/formato';
import { useCatalogoIncidentes, useEnvio, useIncidentes, type CatalogoIncidentes, type PersonaGia } from '../api';
import { Aviso, Cargando, claseBoton, claseCampo, ElegirPersona, ErrorEnvio, Opciones, Pagina } from '../componentes';

function Nuevo({ catalogo }: { catalogo: CatalogoIncidentes }) {
  const envio = useEnvio();
  const [tipo, setTipo] = useState<string | null>(null);
  const [severidad, setSeveridad] = useState<string | null>(null);
  const [descripcion, setDescripcion] = useState('');
  const [detectado, setDetectado] = useState('');
  const [responsable, setResponsable] = useState<PersonaGia | null>(null);
  const elegido = catalogo.tipos.find((t) => t.codigo === tipo);
  const severo = catalogo.severidades.find((s) => s.codigo === severidad);
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        envio.mutate(
          { ruta: '/incidentes', cuerpo: { tipo, severidad, descripcion, detectadoEn: new Date(detectado).toISOString(), reportadoSic: false, responsablePersonaId: responsable?.id, acciones: [] } },
          { onSuccess: () => { setDescripcion(''); setDetectado(''); setTipo(null); setSeveridad(null); setResponsable(null); } },
        );
      }}
    >
      <Opciones titulo="Tipo" valor={tipo} opciones={catalogo.tipos.map((t) => ({ valor: t.codigo, texto: t.tipo }))} alCambiar={setTipo} />
      {elegido && elegido.playbook.length > 0 && (
        <Aviso tono="info">
          <p className="font-medium">Playbook</p>
          <ol className="mt-1 list-decimal pl-5">
            {elegido.playbook.map((paso) => (
              <li key={paso}>{paso}</li>
            ))}
          </ol>
        </Aviso>
      )}
      <Opciones titulo="Severidad" valor={severidad} opciones={catalogo.severidades.map((s) => ({ valor: s.codigo, texto: `${s.codigo} ${s.nombre}` }))} alCambiar={setSeveridad} />
      {severo && (
        <Aviso tono="info">
          {severo.criterio}. Responder en {severo.tiempoRespuesta}. Notificar a: {severo.notificaA}. Atlas no envía este aviso: hazlo tú.
        </Aviso>
      )}
      <label className="text-sm font-medium">
        Qué pasó
        <textarea required rows={3} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} className={`${claseCampo} py-2`} />
      </label>
      <label className="text-sm font-medium">
        Detectado el
        <input required type="datetime-local" value={detectado} onChange={(e) => setDetectado(e.target.value)} className={claseCampo} />
      </label>
      <ElegirPersona titulo="Responsable" elegida={responsable} alElegir={setResponsable} />
      <ErrorEnvio error={envio.error} porDefecto="No pudimos registrar el incidente." />
      <button type="submit" disabled={envio.isPending || !tipo || !severidad || !responsable} className={`${claseBoton} self-start`}>
        Registrar
      </button>
    </form>
  );
}

/** Incidentes de IA (HU 8.5, plan 02 §6.6). Atlas registra; no notifica ni reporta a la SIC. */
export default function Incidentes() {
  const { acceso } = useSesion();
  const incidentes = useIncidentes();
  const catalogo = useCatalogoIncidentes();
  return (
    <Pagina titulo="Incidentes de IA" descripcion="Registro y seguimiento. Atlas no envía avisos ni reporta a la SIC.">
      {incidentes.isPending && <Cargando etiqueta="Cargando los incidentes" />}
      {incidentes.isError && <p role="alert">No pudimos cargar los incidentes. Revisa tu conexión.</p>}
      {incidentes.data?.length === 0 && <p className="text-sm text-atlas-texto-suave">No hay incidentes registrados.</p>}
      {incidentes.data && incidentes.data.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2">
          {incidentes.data.map((i) => (
            <li key={i.id} className="min-w-0 rounded-xl border border-border bg-card p-3">
              <p className="font-medium">{i.codigo}</p>
              <p className="mt-1 text-sm break-words">{i.descripcion}</p>
              <p className="mt-1 flex flex-wrap gap-1">
                <Insignia>{i.severidad}</Insignia>
                <Insignia>{i.tipo}</Insignia>
                <Insignia>{i.cerradoEn ? 'Cerrado' : 'Abierto'}</Insignia>
              </p>
              <p className="mt-1 text-xs text-atlas-texto-suave">
                {i.responsable.nombre} · {formatearFechaHora(new Date(i.detectadoEn))}
              </p>
            </li>
          ))}
        </ul>
      )}
      {catalogo.data && tienePermiso('atlas.gia.gestionar', acceso.permisos) && (
        <Tarjeta titulo="Registrar un incidente">
          <Nuevo catalogo={catalogo.data} />
        </Tarjeta>
      )}
    </Pagina>
  );
}
