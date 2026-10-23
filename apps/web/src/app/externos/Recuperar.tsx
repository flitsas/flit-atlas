import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MailCheck } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { CLAVE_YO } from '../sesion';
import { Boton } from '../ui/Tarjeta';
import {
  claveTotpRecuperacion,
  completarRecuperacion,
  consultarRecuperacion,
  type CuentaLista,
  opcionesPasskeyRecuperacion,
  solicitarRecuperacion,
} from './api';
import { Alerta, claseCampo, CodigosRecuperacion, ConfigurarFactor, mensajeDe, PantallaPublica } from './componentes';
import { tomarFragmento } from './fragmento';

/**
 * Recuperación de una cuenta externa (HU 1.5): sin enlace, se pide por correo (la respuesta no dice si la cuenta
 * existe); con el enlace del correo (`/recuperar#…`, de un solo uso y 30 minutos), se registra un factor nuevo, se
 * quitan los anteriores y se cierran todas las sesiones.
 */
export default function Recuperar() {
  const [enlace] = useState(() => tomarFragmento('/recuperar'));
  return enlace ? <RegistrarFactorNuevo enlace={enlace} /> : <PedirEnlace />;
}

function PedirEnlace() {
  const [correo, setCorreo] = useState('');
  const [enviado, setEnviado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setError(null);
    try {
      await solicitarRecuperacion(correo.trim());
      setEnviado(true);
    } catch (e) {
      setError(mensajeDe(e, 'No se pudo enviar la solicitud. Intenta de nuevo.'));
    }
  }

  return (
    <PantallaPublica titulo="Recupera tu cuenta">
      {enviado ? (
        <p role="status" className="flex items-start gap-3 rounded-2xl border border-border bg-card p-4 text-sm">
          <MailCheck className="size-5 shrink-0 text-atlas-tinta" aria-hidden="true" />
          Si el correo tiene una cuenta de Atlas, te enviamos un enlace para registrar un factor nuevo. Vence en 30 minutos y sirve una sola vez.
        </p>
      ) : (
        <form onSubmit={(e) => void enviar(e)} aria-label="Recuperar cuenta" className="flex flex-col gap-4">
          <p className="text-sm text-atlas-texto-suave">
            Para cuentas de clientes, asesores y proveedores. Si eres empleado de FLIT, entra con Microsoft.
          </p>
          <label className="text-sm font-medium">
            Correo de tu cuenta
            <input type="email" required autoComplete="email" value={correo} onChange={(e) => setCorreo(e.target.value)} className={claseCampo} />
          </label>
          <Boton type="submit" className="min-h-12">
            Enviarme el enlace
          </Boton>
          {error && <Alerta>{error}</Alerta>}
        </form>
      )}
      <Link to="/" className="text-sm font-medium text-atlas-tinta underline">
        Volver al ingreso
      </Link>
    </PantallaPublica>
  );
}

function RegistrarFactorNuevo({ enlace }: { enlace: string }) {
  const cliente = useQueryClient();
  const navegar = useNavigate();
  const cuentaARecuperar = useQuery({
    queryKey: ['externos', 'recuperacion', enlace],
    queryFn: () => consultarRecuperacion(enlace),
    retry: false,
    staleTime: Infinity,
  });
  const [lista, setLista] = useState<CuentaLista | null>(null);

  if (cuentaARecuperar.isError) {
    return (
      <PantallaPublica titulo="Este enlace no sirve">
        <Alerta>Este enlace ya se usó o venció. Pide uno nuevo.</Alerta>
        <Link to="/recuperar" className="text-sm font-medium text-atlas-tinta underline">
          Pedir otro enlace
        </Link>
      </PantallaPublica>
    );
  }
  if (cuentaARecuperar.isPending) {
    return (
      <PantallaPublica titulo="Recupera tu cuenta">
        <p role="status">Revisando el enlace…</p>
      </PantallaPublica>
    );
  }
  if (lista) {
    return (
      <PantallaPublica titulo="Recuperaste tu cuenta">
        <CodigosRecuperacion
          codigos={lista.codigosRecuperacion}
          textoBoton={lista.sesionIniciada ? 'Entrar a Atlas' : 'Ir al ingreso'}
          alContinuar={() => {
            void cliente.invalidateQueries({ queryKey: CLAVE_YO });
            void navegar('/', { replace: true });
          }}
        />
      </PantallaPublica>
    );
  }
  return (
    <PantallaPublica titulo="Registra un factor nuevo">
      <p className="text-sm">
        Cuenta <strong>{cuentaARecuperar.data.correo}</strong>. Al terminar, tus passkeys, tu contraseña y tus códigos anteriores dejan de servir y
        se cierran tus sesiones abiertas.
      </p>
      <ConfigurarFactor
        textoBoton="Recuperar mi cuenta"
        pedirOpcionesPasskey={() => opcionesPasskeyRecuperacion(enlace)}
        pedirClaveTotp={() => claveTotpRecuperacion(enlace)}
        completar={async (factor) => {
          const resultado = await completarRecuperacion(enlace, factor);
          setLista(resultado);
          return resultado;
        }}
      />
    </PantallaPublica>
  );
}
