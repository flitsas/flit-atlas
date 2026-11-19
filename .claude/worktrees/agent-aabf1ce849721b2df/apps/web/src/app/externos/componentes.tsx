import { Check, Copy, Download, Fingerprint, KeyRound } from 'lucide-react';
import { type FormEvent, type ReactNode, useId, useState } from 'react';

import { ErrorApi } from '../api';
import { MarcaAtlas } from '../ui/Marca';
import { Hoja } from '../ui/Hoja';
import { Boton, cn } from '../ui/Tarjeta';
import type { ClaveTotp, CuentaLista, DocumentoVigente, Factor, OpcionesPasskey } from './api';
import { crearPasskey, passkeysDisponibles } from './webauthn';

export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

/** Mensaje de un error de la API (el del servidor si lo trae). */
export function mensajeDe(error: unknown, porDefecto: string): string {
  if (error instanceof ErrorApi) {
    if (error.estado === 429) return 'Demasiados intentos seguidos. Espera un minuto y vuelve a intentarlo.';
    return error.mensajePara(porDefecto);
  }
  if (error instanceof DOMException && error.name === 'NotAllowedError') return 'Cancelaste la passkey o venció el tiempo. Vuelve a intentarlo.';
  return porDefecto;
}

export function Alerta({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]">
      {children}
    </p>
  );
}

/** Pantalla pública (sin sesión) con la marca: invitación y recuperación. */
export function PantallaPublica({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <div className="min-h-dvh bg-atlas-fondo">
      <header className="bg-portada px-4 pt-[max(1rem,env(safe-area-inset-top))] pb-4 text-white sm:px-8">
        <MarcaAtlas className="text-white" />
      </header>
      <main className="mx-auto w-full max-w-xl px-4 py-6 pb-[max(2rem,env(safe-area-inset-bottom))] sm:py-10">
        <h1 className="text-2xl font-semibold">{titulo}</h1>
        <div className="mt-5 flex flex-col gap-5">{children}</div>
      </main>
    </div>
  );
}

/** Markdown mínimo de los documentos legales: títulos, citas, listas numeradas y párrafos (sin HTML). */
export function TextoLegal({ texto }: { texto: string }) {
  const bloques = texto.split(/\n{2,}/).map((b) => b.trim()).filter(Boolean);
  const limpio = (s: string) => s.replace(/\*\*(.+?)\*\*/g, '$1').replace(/`([^`]+)`/g, '$1').replace(/\n\s*/g, ' ');
  return (
    <div className="flex flex-col gap-3 text-sm leading-relaxed">
      {bloques.map((bloque, i) => {
        if (bloque.startsWith('# ')) return <h3 key={i} className="text-base font-semibold">{limpio(bloque.slice(2))}</h3>;
        if (bloque.startsWith('>')) {
          return (
            <p key={i} className="rounded-xl border-l-4 border-[var(--badge-pending-fg)] bg-atlas-activo p-3 font-medium">
              {limpio(bloque.replace(/^>\s?/gm, ''))}
            </p>
          );
        }
        if (/^\d+\.\s/.test(bloque)) {
          return (
            <ol key={i} className="flex list-decimal flex-col gap-2 pl-5">
              {bloque.split(/\n(?=\d+\.\s)/).map((item, j) => <li key={j}>{limpio(item.replace(/^\d+\.\s/, ''))}</li>)}
            </ol>
          );
        }
        return <p key={i}>{limpio(bloque)}</p>;
      })}
    </div>
  );
}

/** Casillas de aceptación de los documentos legales, cada uno con su texto completo en una hoja. */
export function AceptarDocumentos({
  documentos,
  aceptados,
  alCambiar,
}: {
  documentos: DocumentoVigente[];
  aceptados: boolean;
  alCambiar: (valor: boolean) => void;
}) {
  const [leyendo, setLeyendo] = useState<DocumentoVigente | null>(null);
  const id = useId();
  return (
    <fieldset className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4">
      <legend className="px-1 text-sm font-semibold">Términos y privacidad (Ley 1581 de 2012)</legend>
      <ul className="flex flex-col gap-1">
        {documentos.map((d) => (
          <li key={d.documento}>
            <button type="button" className="min-h-toque text-left text-sm font-medium text-atlas-tinta underline" onClick={() => setLeyendo(d)}>
              Leer {d.titulo.toLowerCase()} (versión {d.version})
            </button>
          </li>
        ))}
      </ul>
      <label htmlFor={id} className="flex min-h-toque cursor-pointer items-start gap-3 text-sm">
        <input id={id} type="checkbox" checked={aceptados} onChange={(e) => alCambiar(e.target.checked)} className="mt-1 size-5 shrink-0 accent-[var(--atlas-tinta)]" />
        <span>Leí y acepto los términos de uso y el aviso de privacidad, y autorizo a FLIT S.A.S. a tratar mis datos para estas finalidades.</span>
      </label>
      <Hoja abierta={leyendo !== null} alCerrar={() => setLeyendo(null)} titulo={leyendo?.titulo ?? 'Documento'}>
        <div className="px-4 pt-2 pb-6">{leyendo && <TextoLegal texto={leyendo.texto} />}</div>
      </Hoja>
    </fieldset>
  );
}

/**
 * Registro del factor de la cuenta: passkey (preferida, si el navegador la soporta) o contraseña de 12+ caracteres con
 * una aplicación de autenticación (QR o clave). Lo usan la invitación y la recuperación.
 */
export function ConfigurarFactor({
  pedirOpcionesPasskey,
  pedirClaveTotp,
  completar,
  textoBoton,
  deshabilitado,
}: {
  pedirOpcionesPasskey: () => Promise<OpcionesPasskey>;
  pedirClaveTotp: () => Promise<ClaveTotp>;
  completar: (factor: Factor) => Promise<CuentaLista>;
  textoBoton: string;
  deshabilitado?: boolean;
}) {
  const conPasskey = passkeysDisponibles();
  const [metodo, setMetodo] = useState<'passkey' | 'contrasena'>(conPasskey ? 'passkey' : 'contrasena');
  const [clave, setClave] = useState<ClaveTotp | null>(null);
  const [contrasena, setContrasena] = useState('');
  const [confirmacion, setConfirmacion] = useState('');
  const [codigo, setCodigo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function elegirContrasena() {
    setMetodo('contrasena');
    setError(null);
    if (!clave) {
      try {
        setClave(await pedirClaveTotp());
      } catch (e) {
        setError(mensajeDe(e, 'No se pudo preparar la aplicación de autenticación.'));
      }
    }
  }

  async function registrarPasskey() {
    setError(null);
    setEnviando(true);
    try {
      const opciones = await pedirOpcionesPasskey();
      const credencial = await crearPasskey(opciones.opciones);
      await completar({ metodo: 'passkey', estado: opciones.estado, credencial });
    } catch (e) {
      setError(mensajeDe(e, 'No se pudo registrar la passkey. Vuelve a intentarlo.'));
    } finally {
      setEnviando(false);
    }
  }

  async function registrarContrasena(evento: FormEvent) {
    evento.preventDefault();
    setError(null);
    if (contrasena.length < 12) return setError('La contraseña debe tener al menos 12 caracteres.');
    if (contrasena !== confirmacion) return setError('Las contraseñas no coinciden.');
    if (!clave) return setError('Espera a que aparezca el código QR.');
    setEnviando(true);
    try {
      await completar({ metodo: 'contrasena', estado: clave.estado, contrasena, codigo: codigo.replace(/\s/g, '') });
    } catch (e) {
      setError(mensajeDe(e, 'No se pudo crear la cuenta. Revisa los datos.'));
      if (e instanceof ErrorApi && e.codigo === 'paso_vencido') setClave(await pedirClaveTotp().catch(() => null));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <section aria-labelledby="titulo-factor" className="flex flex-col gap-4">
      <h2 id="titulo-factor" className="text-lg font-semibold">
        ¿Cómo vas a entrar?
      </h2>
      <div role="radiogroup" aria-label="Método de ingreso" className="grid gap-2 sm:grid-cols-2">
        <OpcionMetodo
          activa={metodo === 'passkey'}
          deshabilitada={!conPasskey}
          icono={<Fingerprint className="size-5" aria-hidden="true" />}
          titulo="Passkey (recomendada)"
          detalle={conPasskey ? 'Huella, rostro o PIN de tu dispositivo. Sin contraseña.' : 'Este navegador no soporta passkeys.'}
          alElegir={() => setMetodo('passkey')}
        />
        <OpcionMetodo
          activa={metodo === 'contrasena'}
          icono={<KeyRound className="size-5" aria-hidden="true" />}
          titulo="Contraseña y aplicación"
          detalle="Contraseña de 12+ caracteres y un código de tu aplicación de autenticación."
          alElegir={() => void elegirContrasena()}
        />
      </div>

      {metodo === 'passkey' && (
        <Boton className="min-h-12" disabled={enviando || deshabilitado} onClick={() => void registrarPasskey()}>
          <Fingerprint className="size-5" aria-hidden="true" />
          {textoBoton} con una passkey
        </Boton>
      )}

      {metodo === 'contrasena' && (
        <form onSubmit={(e) => void registrarContrasena(e)} aria-label="Contraseña y aplicación de autenticación" className="flex flex-col gap-4">
          <label className="text-sm font-medium">
            Contraseña (mínimo 12 caracteres)
            <input type="password" autoComplete="new-password" required minLength={12} value={contrasena} onChange={(e) => setContrasena(e.target.value)} className={claseCampo} />
          </label>
          <label className="text-sm font-medium">
            Repite la contraseña
            <input type="password" autoComplete="new-password" required value={confirmacion} onChange={(e) => setConfirmacion(e.target.value)} className={claseCampo} />
          </label>
          <div className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4">
            <p className="text-sm">
              Escanea el código con tu aplicación de autenticación (Microsoft Authenticator, Google Authenticator u otra) o escribe la clave.
            </p>
            {clave ? (
              <div className="flex flex-col items-center gap-3 sm:flex-row sm:items-start">
                <img src={clave.qr} alt="Código QR para la aplicación de autenticación" className="size-44 shrink-0 rounded-xl bg-white p-2" />
                <div className="min-w-0 text-sm">
                  <p className="text-atlas-texto-suave">Clave para escribir a mano:</p>
                  <p data-clave-totp className="mt-1 font-mono text-base break-all">
                    {clave.clave}
                  </p>
                </div>
              </div>
            ) : (
              <p role="status">Preparando el código…</p>
            )}
            <label className="text-sm font-medium">
              Código de 6 dígitos de la aplicación
              <input inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}" required value={codigo} onChange={(e) => setCodigo(e.target.value)} className={claseCampo} />
            </label>
          </div>
          <Boton type="submit" className="min-h-12" disabled={enviando || deshabilitado || !clave}>
            {textoBoton}
          </Boton>
        </form>
      )}
      {error && <Alerta>{error}</Alerta>}
    </section>
  );
}

function OpcionMetodo({
  activa,
  deshabilitada,
  icono,
  titulo,
  detalle,
  alElegir,
}: {
  activa: boolean;
  deshabilitada?: boolean;
  icono: ReactNode;
  titulo: string;
  detalle: string;
  alElegir: () => void;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={activa}
      disabled={deshabilitada}
      onClick={alElegir}
      className={cn(
        'flex min-h-toque items-start gap-3 rounded-xl border p-3 text-left disabled:cursor-not-allowed disabled:opacity-60',
        activa ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card',
      )}
    >
      <span className="mt-0.5 text-atlas-tinta">{icono}</span>
      <span>
        <span className="block text-sm font-semibold">{titulo}</span>
        <span className="block text-xs text-atlas-texto-suave">{detalle}</span>
      </span>
    </button>
  );
}

/** Los códigos de recuperación se muestran una sola vez: copiar, descargar y confirmar que se guardaron. */
export function CodigosRecuperacion({ codigos, alContinuar, textoBoton }: { codigos: string[]; alContinuar: () => void; textoBoton: string }) {
  const [guardados, setGuardados] = useState(false);
  const [copiados, setCopiados] = useState(false);
  const texto = `Códigos de recuperación de Flit Atlas (cada uno sirve una vez):\n${codigos.join('\n')}\n`;
  const descarga = `data:text/plain;charset=utf-8,${encodeURIComponent(texto)}`;
  return (
    <section aria-labelledby="titulo-codigos" className="flex flex-col gap-4">
      <h2 id="titulo-codigos" className="text-lg font-semibold">
        Guarda tus códigos de recuperación
      </h2>
      <p className="text-sm text-atlas-texto-suave">
        Si pierdes tu passkey o tu teléfono, cada código te deja entrar una vez. No los volverás a ver: guárdalos en un lugar seguro.
      </p>
      <ol aria-label="Códigos de recuperación" className="grid grid-cols-2 gap-2 rounded-2xl border border-border bg-card p-4 font-mono text-sm">
        {codigos.map((c) => (
          <li key={c}>{c}</li>
        ))}
      </ol>
      <div className="flex flex-wrap gap-2">
        <Boton
          variante="secundario"
          onClick={() => void navigator.clipboard?.writeText(texto).then(() => setCopiados(true), () => undefined)}
        >
          {copiados ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
          {copiados ? 'Copiados' : 'Copiar'}
        </Boton>
        <a
          href={descarga}
          download="flit-atlas-codigos-de-recuperacion.txt"
          className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo"
        >
          <Download className="size-4" aria-hidden="true" />
          Descargar
        </a>
      </div>
      <label className="flex min-h-toque cursor-pointer items-center gap-3 text-sm">
        <input type="checkbox" checked={guardados} onChange={(e) => setGuardados(e.target.checked)} className="size-5 accent-[var(--atlas-tinta)]" />
        Ya guardé mis códigos
      </label>
      <Boton className="min-h-12" disabled={!guardados} onClick={alContinuar}>
        {textoBoton}
      </Boton>
    </section>
  );
}
