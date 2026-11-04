import { useQueryClient } from '@tanstack/react-query';
import { Fingerprint } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../api';
import { CLAVE_YO } from '../sesion';
import { Boton } from '../ui/Tarjeta';
import {
  aceptaciones,
  aceptarTerminosAlEntrar,
  entrarConContrasena,
  entrarConPasskey,
  entrarConSegundoFactor,
  opcionesPasskeyIngreso,
  type TerminosPendientes,
} from './api';
import { AceptarDocumentos, Alerta, claseCampo, mensajeDe } from './componentes';
import { passkeysDisponibles, usarPasskey } from './webauthn';

/**
 * Ingreso de clientes, asesores y proveedores con su cuenta de Atlas (HU 1.5): passkey, o contraseña y el código de la
 * aplicación de autenticación (nunca la contraseña sola). Si hay versiones nuevas de los términos, se aceptan aquí.
 */
export default function IngresoExterno() {
  const cliente = useQueryClient();
  const [paso, setPaso] = useState<{ tipo: 'inicio' } | { tipo: 'totp'; estado: string } | { tipo: 'terminos'; pendientes: TerminosPendientes }>({
    tipo: 'inicio',
  });
  const [correo, setCorreo] = useState('');
  const [contrasena, setContrasena] = useState('');
  const [codigo, setCodigo] = useState('');
  const [conRecuperacion, setConRecuperacion] = useState(false);
  const [aceptados, setAceptados] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  /** 204 abre la sesión; 409 pide aceptar documentos nuevos. */
  async function terminar(peticion: Promise<Response>) {
    try {
      await peticion;
      await cliente.invalidateQueries({ queryKey: CLAVE_YO });
    } catch (e) {
      if (e instanceof ErrorApi && e.codigo === 'terminos_pendientes') {
        setPaso({ tipo: 'terminos', pendientes: e.cuerpo as unknown as TerminosPendientes });
        return;
      }
      throw e;
    }
  }

  async function ejecutar(accion: () => Promise<void>, porDefecto: string) {
    setError(null);
    setEnviando(true);
    try {
      await accion();
    } catch (e) {
      setError(mensajeDe(e, porDefecto));
      if (e instanceof ErrorApi && (e.codigo === 'paso_vencido' || e.codigo === 'cuenta_bloqueada')) setPaso({ tipo: 'inicio' });
    } finally {
      setEnviando(false);
    }
  }

  const conPasskey = () =>
    ejecutar(async () => {
      const opciones = await opcionesPasskeyIngreso();
      const credencial = await usarPasskey(opciones.opciones);
      await terminar(entrarConPasskey(opciones.estado, credencial));
    }, 'No se pudo entrar con la passkey.');

  const conContrasena = (evento: FormEvent) => {
    evento.preventDefault();
    return ejecutar(async () => {
      const resultado = await entrarConContrasena(correo.trim(), contrasena);
      setContrasena('');
      setPaso({ tipo: 'totp', estado: resultado.estado });
    }, 'Correo o contraseña incorrectos.');
  };

  const conCodigo = (evento: FormEvent, estado: string) => {
    evento.preventDefault();
    const valor = codigo.replace(/\s/g, '');
    return ejecutar(
      () => terminar(conRecuperacion ? entrarConSegundoFactor(estado, undefined, valor) : entrarConSegundoFactor(estado, valor)),
      'El código no es válido.',
    );
  };

  return (
    <section aria-label="Ingreso de clientes y proveedores" className="flex flex-col gap-4 rounded-2xl border border-border p-4">
      <p className="text-sm font-semibold">¿Eres cliente, asesor o proveedor?</p>

      {paso.tipo === 'inicio' && (
        <>
          {passkeysDisponibles() && (
            <Boton className="min-h-12" disabled={enviando} onClick={() => void conPasskey()}>
              <Fingerprint className="size-5" aria-hidden="true" />
              Entrar con passkey
            </Boton>
          )}
          <form onSubmit={(e) => void conContrasena(e)} aria-label="Entrar con correo y contraseña" className="flex flex-col gap-3">
            <label className="text-sm font-medium">
              Correo
              <input type="email" required autoComplete="username" value={correo} onChange={(e) => setCorreo(e.target.value)} className={claseCampo} />
            </label>
            <label className="text-sm font-medium">
              Contraseña
              <input type="password" required autoComplete="current-password" value={contrasena} onChange={(e) => setContrasena(e.target.value)} className={claseCampo} />
            </label>
            <Boton type="submit" variante="secundario" className="min-h-12" disabled={enviando}>
              Continuar
            </Boton>
          </form>
          <Link to="/recuperar" className="min-h-toque text-sm font-medium text-atlas-tinta underline">
            ¿Perdiste el acceso? Recupera tu cuenta
          </Link>
        </>
      )}

      {paso.tipo === 'totp' && (
        <form onSubmit={(e) => void conCodigo(e, paso.estado)} aria-label="Código de verificación" className="flex flex-col gap-3">
          <label className="text-sm font-medium">
            {conRecuperacion ? 'Código de recuperación' : 'Código de 6 dígitos de tu aplicación de autenticación'}
            <input
              required
              autoComplete="one-time-code"
              inputMode={conRecuperacion ? 'text' : 'numeric'}
              value={codigo}
              onChange={(e) => setCodigo(e.target.value)}
              className={claseCampo}
            />
          </label>
          <Boton type="submit" className="min-h-12" disabled={enviando}>
            Entrar
          </Boton>
          <button type="button" className="min-h-toque text-left text-sm font-medium text-atlas-tinta underline" onClick={() => setConRecuperacion(!conRecuperacion)}>
            {conRecuperacion ? 'Usar la aplicación de autenticación' : 'Usar un código de recuperación'}
          </button>
        </form>
      )}

      {paso.tipo === 'terminos' && (
        <div className="flex flex-col gap-3">
          <p className="text-sm">Actualizamos los documentos legales. Acéptalos para entrar.</p>
          <AceptarDocumentos documentos={paso.pendientes.documentos} aceptados={aceptados} alCambiar={setAceptados} />
          <Boton
            className="min-h-12"
            disabled={!aceptados || enviando}
            onClick={() =>
              void ejecutar(
                () => terminar(aceptarTerminosAlEntrar(paso.pendientes.estado, aceptaciones(paso.pendientes.documentos))),
                'No se pudo terminar el ingreso.',
              )
            }
          >
            Aceptar y entrar
          </Boton>
        </div>
      )}

      {error && <Alerta>{error}</Alerta>}
    </section>
  );
}
