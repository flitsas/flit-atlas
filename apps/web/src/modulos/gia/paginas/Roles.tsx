import { useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Tarjeta } from '../../../app/ui/Tarjeta';
import { TEXTO_ROL, useEnvio, useRoles, type PersonaGia } from '../api';
import { Aviso, Cargando, claseBoton, claseBotonSuave, ElegirPersona, ErrorEnvio, Opciones, Pagina } from '../componentes';

const OPCIONES = Object.entries(TEXTO_ROL).map(([valor, texto]) => ({ valor, texto }));

/** Roles de gobierno (ATLAS-ADR-0055 §2): la calidad en la que alguien vota. Sin asignaciones, nadie puede aprobar. */
export default function Roles() {
  const { acceso } = useSesion();
  const roles = useRoles();
  const envio = useEnvio();
  const [persona, setPersona] = useState<PersonaGia | null>(null);
  const [rol, setRol] = useState<string | null>(null);
  const gestiona = tienePermiso('atlas.gia.gestionar', acceso.permisos);
  return (
    <Pagina titulo="Roles de gobierno" descripcion="Quién aprueba según el nivel de riesgo.">
      {roles.isPending && <Cargando etiqueta="Cargando los roles" />}
      {roles.isError && <p role="alert">No pudimos cargar los roles. Revisa tu conexión.</p>}
      {roles.data?.length === 0 && <Aviso tono="info">Nadie tiene un rol de gobierno todavía: hasta asignarlos, ningún caso se puede aprobar.</Aviso>}
      {roles.data && roles.data.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2">
          {roles.data.map((r) => (
            <li key={r.id} className="flex min-w-0 flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-card p-3">
              <span className="min-w-0 break-words">
                <span className="block font-medium">{r.persona.nombre}</span>
                <span className="text-sm text-atlas-texto-suave">{TEXTO_ROL[r.rol] ?? r.rol}</span>
              </span>
              {gestiona && (
                <button type="button" className={claseBotonSuave} onClick={() => envio.mutate({ ruta: `/roles-gobierno/${r.id}`, metodo: 'DELETE' })}>
                  Quitar
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {gestiona && (
        <Tarjeta titulo="Asignar un rol">
          <form
            className="flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              envio.mutate({ ruta: '/roles-gobierno', cuerpo: { personaId: persona?.id, rol } }, { onSuccess: () => { setPersona(null); setRol(null); } });
            }}
          >
            <ElegirPersona titulo="Persona" elegida={persona} alElegir={setPersona} />
            <Opciones titulo="Rol" valor={rol} opciones={OPCIONES} alCambiar={setRol} />
            <button type="submit" disabled={!persona || !rol || envio.isPending} className={`${claseBoton} self-start`}>
              Asignar
            </button>
          </form>
        </Tarjeta>
      )}
      <ErrorEnvio error={envio.error} porDefecto="No pudimos cambiar el rol." />
    </Pagina>
  );
}
