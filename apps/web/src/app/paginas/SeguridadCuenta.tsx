import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Fingerprint, KeyRound, ListChecks } from 'lucide-react';
import { type FormEvent, type ReactNode, useState } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../api';
import { agregarPasskey, cambiarContrasena, obtenerSeguridadCuenta, opcionesPasskeyCuenta, regenerarCodigos } from '../externos/api';
import { claseCampo, CodigosRecuperacion, mensajeDe } from '../externos/componentes';
import { crearPasskey, passkeysDisponibles } from '../externos/webauthn';
import { useSesion } from '../sesion';
import { formatearFecha } from '../ui/formato';
import { Hoja } from '../ui/Hoja';
import { Boton, cn, Insignia, Tarjeta } from '../ui/Tarjeta';

type Aviso = { tono: 'exito' | 'error'; texto: string; reautenticar?: boolean } | null;

/** Resultado de una acción: la reautenticación se distingue porque se resuelve volviendo a entrar. */
function avisoDe(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi && error.codigo === 'reautenticacion_requerida') {
    return { tono: 'error', texto: 'Por seguridad, vuelve a entrar para confirmar que eres tú antes de este cambio.', reautenticar: true };
  }
  return { tono: 'error', texto: mensajeDe(error, porDefecto) };
}

function MostrarAviso({ aviso }: { aviso: Aviso }) {
  const { cerrarSesion } = useSesion();
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
      {aviso.texto}{' '}
      {aviso.reautenticar && (
        <button type="button" className="min-h-toque font-semibold underline" onClick={() => void cerrarSesion()}>
          Salir y volver a entrar
        </button>
      )}
    </div>
  );
}

/**
 * Seguridad de la cuenta local (F4-SEC-31, ATLAS-ADR-0049 §10): ver los factores, cambiar la contraseña, agregar una
 * passkey y generar códigos de recuperación nuevos. Cada cambio pide un ingreso reciente y le llega un aviso al correo.
 * Móvil primero: tarjetas apiladas, campos de 48 px y la confirmación en una hoja inferior.
 */
export default function SeguridadCuenta() {
  const { yo } = useSesion();
  const local = yo.proveedor === 'local' || yo.proveedor === 'emergencia';
  const seguridad = useQuery({ queryKey: ['identidad', 'cuenta', 'seguridad'], queryFn: obtenerSeguridadCuenta, enabled: local });

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-5">
      <div className="flex items-center gap-2">
        <Link to="/ajustes" aria-label="Volver a ajustes" className="grid size-11 place-items-center rounded-xl hover:bg-atlas-activo">
          <ArrowLeft className="size-5" aria-hidden="true" />
        </Link>
        <h1 className="text-2xl font-semibold">Seguridad de la cuenta</h1>
      </div>

      {!local && (
        <Tarjeta etiqueta="Cuenta de Microsoft">
          <p className="text-sm">Entras con tu cuenta de Microsoft: la contraseña y los factores se administran allí.</p>
        </Tarjeta>
      )}
      {local && seguridad.isPending && <p className="text-sm text-atlas-texto-suave">Cargando…</p>}
      {local && seguridad.isError && <p role="alert" className="text-sm">No pudimos cargar la seguridad de tu cuenta.</p>}
      {local && seguridad.data && (
        <>
          <Tarjeta titulo="Cómo entras" icono="permisos">
            <ul className="flex flex-col divide-y divide-border text-sm">
              <Fila titulo="Contraseña y aplicación de autenticación">
                {seguridad.data.contrasena && seguridad.data.totp ? <Insignia tono="exito">Activa</Insignia> : <Insignia tono="neutral">No</Insignia>}
              </Fila>
              <Fila titulo={`Passkeys (${seguridad.data.passkeys.length})`}>
                {seguridad.data.passkeys.length > 0 ? <Insignia tono="exito">Activas</Insignia> : <Insignia tono="neutral">Ninguna</Insignia>}
              </Fila>
              {seguridad.data.passkeys.map((p, i) => (
                <li key={`${p.creadaEn}-${i}`} className="py-2 pl-4 text-xs text-atlas-texto-suave">
                  {p.nombre ?? `Passkey ${i + 1}`} · creada el {formatearFecha(new Date(p.creadaEn))}
                </li>
              ))}
              <Fila titulo="Códigos de recuperación sin usar">
                <Insignia tono={seguridad.data.codigosRecuperacionRestantes > 2 ? 'info' : 'neutral'}>{seguridad.data.codigosRecuperacionRestantes}</Insignia>
              </Fila>
            </ul>
          </Tarjeta>
          <AgregarPasskey />
          {seguridad.data.contrasena && <CambiarContrasena />}
          <NuevosCodigos />
          <p className="text-center text-xs text-atlas-texto-suave">
            Por seguridad, cada cambio pide que hayas entrado hace menos de 10 minutos y te avisamos por correo.
          </p>
        </>
      )}
    </div>
  );
}

function Fila({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <li className="flex min-h-toque items-center justify-between gap-3 py-2">
      <span>{titulo}</span>
      {children}
    </li>
  );
}

function useRefrescar() {
  const cliente = useQueryClient();
  return () => void cliente.invalidateQueries({ queryKey: ['identidad', 'cuenta', 'seguridad'] });
}

function AgregarPasskey() {
  const [aviso, setAviso] = useState<Aviso>(null);
  const refrescar = useRefrescar();
  const disponible = passkeysDisponibles();
  const agregar = useMutation({
    mutationFn: async () => {
      const opciones = await opcionesPasskeyCuenta();
      await agregarPasskey(opciones.estado, await crearPasskey(opciones.opciones));
    },
    onSuccess: () => {
      setAviso({ tono: 'exito', texto: 'Agregamos la passkey. Ya puedes entrar con ella.' });
      refrescar();
    },
    onError: (e) => setAviso(avisoDe(e, 'No se pudo agregar la passkey. Vuelve a intentarlo.')),
  });
  return (
    <section aria-labelledby="titulo-passkey" className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta sm:p-5">
      <h2 id="titulo-passkey" className="flex items-center gap-2 text-base font-semibold">
        <Fingerprint className="size-5 text-atlas-tinta" aria-hidden="true" />
        Agregar una passkey
      </h2>
      <p className="text-sm text-atlas-texto-suave">
        {disponible
          ? 'Usa la huella, el rostro o el PIN de este dispositivo para entrar sin contraseña.'
          : 'Este navegador no admite passkeys. Ábrelo desde otro dispositivo o navegador para agregar una.'}
      </p>
      <Boton className="min-h-12 self-start" disabled={!disponible || agregar.isPending} onClick={() => agregar.mutate()}>
        <Fingerprint className="size-4" aria-hidden="true" />
        {agregar.isPending ? 'Esperando la passkey…' : 'Agregar passkey'}
      </Boton>
      <MostrarAviso aviso={aviso} />
    </section>
  );
}

function CambiarContrasena() {
  const [actual, setActual] = useState('');
  const [nueva, setNueva] = useState('');
  const [repetida, setRepetida] = useState('');
  const [aviso, setAviso] = useState<Aviso>(null);
  const cambiar = useMutation({
    mutationFn: () => cambiarContrasena(actual, nueva),
    onSuccess: () => {
      setActual('');
      setNueva('');
      setRepetida('');
      setAviso({ tono: 'exito', texto: 'Cambiamos tu contraseña y cerramos tus demás sesiones.' });
    },
    onError: (e) => setAviso(avisoDe(e, 'No se pudo cambiar la contraseña.')),
  });

  function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (nueva.length < 12) return setAviso({ tono: 'error', texto: 'La contraseña nueva debe tener al menos 12 caracteres.' });
    if (nueva !== repetida) return setAviso({ tono: 'error', texto: 'Las contraseñas nuevas no coinciden.' });
    setAviso(null);
    cambiar.mutate();
  }

  return (
    <section aria-labelledby="titulo-contrasena" className="rounded-2xl border border-border bg-card p-4 shadow-tarjeta sm:p-5">
      <h2 id="titulo-contrasena" className="mb-3 flex items-center gap-2 text-base font-semibold">
        <KeyRound className="size-5 text-atlas-tinta" aria-hidden="true" />
        Cambiar la contraseña
      </h2>
      <form onSubmit={enviar} aria-label="Cambiar la contraseña" className="flex flex-col gap-4">
        <label className="text-sm font-medium">
          Contraseña actual
          <input type="password" autoComplete="current-password" required value={actual} onChange={(e) => setActual(e.target.value)} className={claseCampo} />
        </label>
        <label className="text-sm font-medium">
          Contraseña nueva (mínimo 12 caracteres)
          <input type="password" autoComplete="new-password" required minLength={12} value={nueva} onChange={(e) => setNueva(e.target.value)} className={claseCampo} />
        </label>
        <label className="text-sm font-medium">
          Repite la contraseña nueva
          <input type="password" autoComplete="new-password" required value={repetida} onChange={(e) => setRepetida(e.target.value)} className={claseCampo} />
        </label>
        <p className="text-xs text-atlas-texto-suave">Usa una frase larga y propia. Revisamos que no aparezca en filtraciones públicas.</p>
        <Boton type="submit" className="min-h-12" disabled={cambiar.isPending}>
          Cambiar contraseña
        </Boton>
        <MostrarAviso aviso={aviso} />
      </form>
    </section>
  );
}

function NuevosCodigos() {
  const [confirmando, setConfirmando] = useState(false);
  const [codigos, setCodigos] = useState<string[] | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const refrescar = useRefrescar();
  const generar = useMutation({
    mutationFn: regenerarCodigos,
    onSuccess: (r) => {
      setConfirmando(false);
      setAviso(null);
      setCodigos(r.codigosRecuperacion);
      refrescar();
    },
    onError: (e) => {
      setConfirmando(false);
      setAviso(avisoDe(e, 'No se pudieron generar los códigos.'));
    },
  });

  return (
    <section aria-labelledby="titulo-codigos-nuevos" className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta sm:p-5">
      <h2 id="titulo-codigos-nuevos" className="flex items-center gap-2 text-base font-semibold">
        <ListChecks className="size-5 text-atlas-tinta" aria-hidden="true" />
        Códigos de recuperación
      </h2>
      {codigos ? (
        <CodigosRecuperacion codigos={codigos} textoBoton="Listo" alContinuar={() => setCodigos(null)} />
      ) : (
        <>
          <p className="text-sm text-atlas-texto-suave">
            Si usaste varios o crees que alguien los vio, genera diez nuevos. Los anteriores dejan de servir.
          </p>
          <Boton variante="secundario" className="min-h-12 self-start" onClick={() => setConfirmando(true)}>
            Generar códigos nuevos
          </Boton>
        </>
      )}
      <MostrarAviso aviso={aviso} />
      <Hoja abierta={confirmando} alCerrar={() => setConfirmando(false)} titulo="¿Generar códigos nuevos?">
        <div className="flex flex-col gap-4 px-4 pt-2 pb-6">
          <p className="text-sm">Los códigos que tienes guardados dejarán de servir. Los nuevos se muestran una sola vez.</p>
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Boton variante="secundario" className="min-h-12" onClick={() => setConfirmando(false)}>
              Cancelar
            </Boton>
            <Boton className="min-h-12" disabled={generar.isPending} onClick={() => generar.mutate()}>
              Generar
            </Boton>
          </div>
        </div>
      </Hoja>
    </section>
  );
}
