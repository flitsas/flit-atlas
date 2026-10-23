import { useQuery } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  type BloqueCodeowners,
  CLAVE_REPARTO,
  type DiferenciaCodeowners,
  guardarUsuarioGitHub,
  obtenerCodeowners,
  obtenerDiferencias,
  type PersonaEquipo,
  useCambioReparto,
  useReparto,
} from '../apiResponsables';
import { Advertencia, type Aviso, avisoDeError, Bloque, BotonCopiar, claseCampo, descargar, MostrarAviso } from '../componentes';

const ESTADO: Record<DiferenciaCodeowners['estado'], string> = {
  igual: 'Igual',
  distinto: 'Distinto',
  falta_en_github: 'Falta en GitHub',
  solo_en_github: 'Solo en GitHub',
};

/**
 * CODEOWNERS sugerido por repo y carpeta (HU 2.4): sale del repo y la ruta del catálogo y del reparto vigente. Quien
 * gestiona el equipo lo compara con el CODEOWNERS real (lectura de la API de GitHub) y edita los usuarios de GitHub.
 */
export default function Codeowners() {
  const { acceso } = useSesion();
  const gestiona = tienePermiso('atlas.equipo.gestionar', acceso.permisos);
  const codeowners = useReparto(['codeowners'], obtenerCodeowners);
  if (codeowners.isPending) return <p role="status">Cargando el CODEOWNERS sugerido…</p>;
  if (codeowners.isError || !codeowners.data) return <p role="alert">No pudimos cargar el CODEOWNERS sugerido. Revisa tu conexión.</p>;
  const datos = codeowners.data;

  return (
    <div className="flex flex-col gap-4">
      {!datos.gitHubConfigurado && (
        <Advertencia>
          Este ambiente no tiene GitHub configurado (Plataforma:GitHub): ves el bloque sugerido, pero no las diferencias con el
          CODEOWNERS real.
        </Advertencia>
      )}
      {datos.personasSinUsuario.length > 0 && (
        <Tarjeta titulo="Responsables sin usuario de GitHub">
          <p className="mb-3 text-sm text-atlas-texto-suave">Sin su usuario, la línea de CODEOWNERS no los puede nombrar.</p>
          <ul aria-label="Responsables sin usuario de GitHub" className="flex flex-col gap-2">
            {datos.personasSinUsuario.map((p) => (
              <UsuarioGitHub key={p.id} persona={p} editable={gestiona} />
            ))}
          </ul>
        </Tarjeta>
      )}
      {datos.repos.length === 0 && <p className="text-sm">Aún no hay repos con responsables: importa el reparto o agrega el repo en el catálogo.</p>}
      {datos.repos.map((bloque) => (
        <BloqueRepo key={bloque.repo} bloque={bloque} comparar={gestiona && datos.gitHubConfigurado} />
      ))}
    </div>
  );
}

function UsuarioGitHub({ persona, editable }: { persona: PersonaEquipo; editable: boolean }) {
  const [usuario, setUsuario] = useState('');
  const [aviso, setAviso] = useState<Aviso>(null);
  const guardar = useCambioReparto(guardarUsuarioGitHub);
  return (
    <li className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
      <p className="font-medium break-words">{persona.nombre}</p>
      {editable && (
        <form
          aria-label={`Usuario de GitHub de ${persona.nombre}`}
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            guardar.mutate(
              { personaId: persona.id, usuario: usuario.trim() || null },
              { onError: (er) => setAviso(avisoDeError(er, 'No se pudo guardar el usuario.')) },
            );
          }}
        >
          <label className="min-w-0 flex-1 basis-48 text-sm font-medium">
            Usuario de GitHub
            <input value={usuario} onChange={(e) => setUsuario(e.target.value)} maxLength={40} autoComplete="off" spellCheck={false} className={claseCampo} />
          </label>
          <Boton type="submit" disabled={!usuario.trim() || guardar.isPending}>
            Guardar
          </Boton>
        </form>
      )}
      <MostrarAviso aviso={aviso} />
    </li>
  );
}

function BloqueRepo({ bloque, comparar }: { bloque: BloqueCodeowners; comparar: boolean }) {
  const [ver, setVer] = useState(false);
  const diferencias = useQuery({
    queryKey: [...CLAVE_REPARTO, 'diferencias', bloque.repo],
    queryFn: () => obtenerDiferencias(bloque.repo),
    enabled: ver,
  });
  const nombre = bloque.repo.replace('/', '-');
  return (
    <Tarjeta titulo={bloque.repo}>
      <div className="flex flex-col gap-3">
        <Bloque texto={bloque.texto} etiqueta={`CODEOWNERS sugerido para ${bloque.repo}`} />
        <div className="flex flex-wrap gap-2">
          <BotonCopiar texto={bloque.texto} etiqueta="Copiar bloque" />
          <Boton variante="secundario" onClick={() => descargar(`CODEOWNERS-${nombre}`, bloque.texto, 'text/plain')}>
            <Download className="size-4" aria-hidden="true" />
            Descargar
          </Boton>
          {comparar && (
            <Boton variante="secundario" onClick={() => setVer(true)} disabled={diferencias.isFetching}>
              Comparar con GitHub
            </Boton>
          )}
        </div>
        {diferencias.isError && <p role="alert">No pudimos comparar con GitHub.</p>}
        {diferencias.data && (
          <div className="flex flex-col gap-2" aria-live="polite">
            {diferencias.data.mensaje && <Advertencia>{diferencias.data.mensaje}</Advertencia>}
            {diferencias.data.archivo && <p className="text-sm">Archivo en GitHub: {diferencias.data.archivo}</p>}
            <ul aria-label={`Diferencias con GitHub en ${bloque.repo}`} className="flex flex-col gap-2 text-sm">
              {diferencias.data.diferencias.map((d) => (
                <li key={d.patron} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border p-3">
                  <p className="flex flex-wrap items-center gap-2">
                    <code className="break-all">{d.patron}</code>
                    <Insignia tono={d.estado === 'igual' ? 'exito' : 'neutral'}>{ESTADO[d.estado]}</Insignia>
                  </p>
                  {d.estado !== 'igual' && (
                    <p className="break-words text-atlas-texto-suave">
                      Atlas: {d.enAtlas.join(' ') || '—'} · GitHub: {d.enGitHub.join(' ') || '—'}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </Tarjeta>
  );
}
