import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';

import { CLAVE_YO } from '../sesion';
import { activarEmergencia, claveTotpEmergencia, consultarEmergencia, type CuentaLista, opcionesPasskeyEmergencia } from './api';
import { Alerta, CodigosRecuperacion, ConfigurarFactor, PantallaPublica } from './componentes';
import { tomarFragmento } from './fragmento';

/**
 * Activación de la cuenta de administrador de emergencia (ATLAS-ADR-0027) con el enlace que imprime el comando de
 * consola (`/emergencia#…`, de un solo uso): se registra una passkey, o una contraseña con TOTP, como en la recuperación
 * de una cuenta externa, y queda la sesión abierta.
 */
export default function ActivarEmergencia() {
  const [enlace] = useState(() => tomarFragmento('/emergencia'));
  const cliente = useQueryClient();
  const navegar = useNavigate();
  const cuenta = useQuery({
    queryKey: ['emergencia', enlace],
    queryFn: () => consultarEmergencia(enlace),
    enabled: enlace !== '',
    retry: false,
    staleTime: Infinity,
  });
  const [lista, setLista] = useState<CuentaLista | null>(null);

  if (!enlace || cuenta.isError) {
    return (
      <PantallaPublica titulo="Este enlace no sirve">
        <Alerta>Este enlace ya se usó, venció o no es válido. Quien administra el ambiente debe generar otro con el comando de emergencia.</Alerta>
      </PantallaPublica>
    );
  }
  if (cuenta.isPending) {
    return (
      <PantallaPublica titulo="Cuenta de emergencia">
        <p role="status">Revisando el enlace…</p>
      </PantallaPublica>
    );
  }
  if (lista) {
    return (
      <PantallaPublica titulo="Cuenta de emergencia activada">
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
    <PantallaPublica titulo="Activa la cuenta de emergencia">
      <p className="flex items-start gap-3 rounded-2xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-4 text-sm text-[var(--badge-warning-fg)]">
        <ShieldAlert className="size-5 shrink-0" aria-hidden="true" />
        <span>
          Cuenta <strong>{cuenta.data.correo}</strong>, con administración total de Atlas. Úsala solo para el arranque del ambiente o si
          Microsoft no responde: cada ingreso queda en la auditoría como evento de alta severidad.
        </span>
      </p>
      <ConfigurarFactor
        textoBoton="Activar la cuenta"
        pedirOpcionesPasskey={() => opcionesPasskeyEmergencia(enlace)}
        pedirClaveTotp={() => claveTotpEmergencia(enlace)}
        completar={async (factor) => {
          const resultado = await activarEmergencia(enlace, factor);
          setLista(resultado);
          return resultado;
        }}
      />
    </PantallaPublica>
  );
}
