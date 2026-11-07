import { useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { useEnvio, useHerramientas } from '../api';
import { Cargando, claseBoton, claseCampo, ErrorEnvio, Opciones, Pagina, vacioANulo } from '../componentes';

const ESTADOS = [{ valor: 'aprobada', texto: 'Aprobada' }, { valor: 'en_evaluacion', texto: 'En evaluación' }, { valor: 'prohibida', texto: 'Prohibida' }];
const NIVELES = ['publico', 'interno', 'confidencial', 'personal', 'sensible'].map((v) => ({ valor: v, texto: v }));
const TEXTO_ESTADO = Object.fromEntries(ESTADOS.map((e) => [e.valor, e.texto]));

function Nueva() {
  const envio = useEnvio();
  const [codigo, setCodigo] = useState('');
  const [nombre, setNombre] = useState('');
  const [estado, setEstado] = useState<string | null>(null);
  const [nivel, setNivel] = useState<string | null>(null);
  const [plan, setPlan] = useState('');
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        envio.mutate(
          { ruta: '/herramientas', cuerpo: { codigo, nombre, estado, nivelDatoMaximo: nivel, plan: vacioANulo(plan) } },
          { onSuccess: () => { setCodigo(''); setNombre(''); setPlan(''); setEstado(null); setNivel(null); } },
        );
      }}
    >
      <label className="text-sm font-medium">
        Código
        <input required value={codigo} onChange={(e) => setCodigo(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Nombre
        <input required value={nombre} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Plan
        <input value={plan} onChange={(e) => setPlan(e.target.value)} className={claseCampo} />
      </label>
      <Opciones titulo="Estado" valor={estado} opciones={ESTADOS} alCambiar={setEstado} />
      <Opciones titulo="Nivel de dato máximo" valor={nivel} opciones={NIVELES} alCambiar={setNivel} />
      <ErrorEnvio error={envio.error} porDefecto="No pudimos registrar la herramienta." />
      <button type="submit" disabled={envio.isPending || !estado || !nivel} className={`${claseBoton} self-start`}>
        Registrar
      </button>
    </form>
  );
}

/** Herramientas de IA del equipo (HU 8.5). El inventario sale de la encuesta del plan 02 §12.1: no se siembra. */
export default function Herramientas() {
  const { acceso } = useSesion();
  const herramientas = useHerramientas();
  return (
    <Pagina titulo="Herramientas de IA" descripcion="Herramientas del equipo y sus asientos.">
      {herramientas.isPending && <Cargando etiqueta="Cargando las herramientas" />}
      {herramientas.isError && <p role="alert">No pudimos cargar las herramientas. Revisa tu conexión.</p>}
      {herramientas.data?.length === 0 && <p className="text-sm text-atlas-texto-suave">Todavía no hay herramientas: se cargan con el resultado de la encuesta al equipo.</p>}
      {herramientas.data && herramientas.data.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {herramientas.data.map((h) => (
            <li key={h.id} className="min-w-0 rounded-xl border border-border bg-card p-3">
              <p className="font-medium break-words">{h.nombre}</p>
              <p className="text-xs text-atlas-texto-suave">{h.plan ?? 'Sin plan'}</p>
              <p className="mt-1 flex flex-wrap gap-1">
                <Insignia>{TEXTO_ESTADO[h.estado] ?? h.estado}</Insignia>
                <Insignia>{`Hasta ${h.nivelDatoMaximo}`}</Insignia>
                <Insignia>{`${h.asientosActivos} asientos`}</Insignia>
              </p>
            </li>
          ))}
        </ul>
      )}
      {tienePermiso('atlas.gia.gestionar', acceso.permisos) && (
        <Tarjeta titulo="Registrar una herramienta">
          <Nueva />
        </Tarjeta>
      )}
    </Pagina>
  );
}
