import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Building2, CalendarClock } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { CLAVE_YO } from '../sesion';
import { formatearFecha, formatearFechaHora } from '../ui/formato';
import { Insignia } from '../ui/Tarjeta';
import {
  aceptaciones,
  aceptarInvitacion,
  claveTotpInvitacion,
  consultarInvitacion,
  type CuentaLista,
  opcionesPasskeyInvitacion,
} from './api';
import { AceptarDocumentos, Alerta, claseCampo, CodigosRecuperacion, ConfigurarFactor, PantallaPublica } from './componentes';
import { tomarFragmento } from './fragmento';

/**
 * Aceptar una invitación (HU 1.5): el token llega en el fragmento del enlace del correo (`/invitacion#<token>`), se
 * saca de la barra de direcciones y solo viaja en el cuerpo de los POST. Pasos: datos y términos, factor de la cuenta
 * (passkey o contraseña con TOTP) y códigos de recuperación. Al terminar, la sesión ya está abierta.
 */
export default function AceptarInvitacion() {
  const [token] = useState(() => tomarFragmento('/invitacion'));
  const cliente = useQueryClient();
  const navegar = useNavigate();
  const invitacion = useQuery({
    queryKey: ['externos', 'invitacion', token],
    queryFn: () => consultarInvitacion(token),
    enabled: token.length > 0,
    retry: false,
    staleTime: Infinity,
  });
  const [nombre, setNombre] = useState<string | null>(null);
  const [aceptados, setAceptados] = useState(false);
  const [cuenta, setCuenta] = useState<CuentaLista | null>(null);

  if (!token || invitacion.isError) {
    return (
      <PantallaPublica titulo="Este enlace no sirve">
        <Alerta>Este enlace ya se usó, venció o no es válido. Pide a quien te invitó que te lo reenvíe.</Alerta>
        <Link to="/" className="text-sm font-medium text-atlas-tinta underline">
          Ir a la pantalla de ingreso
        </Link>
      </PantallaPublica>
    );
  }
  if (invitacion.isPending) {
    return (
      <PantallaPublica titulo="Invitación a Flit Atlas">
        <p role="status">Revisando la invitación…</p>
      </PantallaPublica>
    );
  }

  const datos = invitacion.data;
  const nombreActual = nombre ?? datos.nombre ?? '';

  if (cuenta) {
    return (
      <PantallaPublica titulo="¡Tu cuenta está lista!">
        <CodigosRecuperacion
          codigos={cuenta.codigosRecuperacion}
          textoBoton="Entrar a Atlas"
          alContinuar={() => {
            void cliente.invalidateQueries({ queryKey: CLAVE_YO });
            void navegar('/', { replace: true });
          }}
        />
      </PantallaPublica>
    );
  }

  return (
    <PantallaPublica titulo="Te invitaron a Flit Atlas">
      <section aria-label="Tu invitación" className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
        <p className="flex items-center gap-2 font-semibold">
          <Building2 className="size-5 text-atlas-tinta" aria-hidden="true" />
          {datos.organizacion}
        </p>
        <div className="flex flex-wrap gap-2">
          <Insignia>{datos.rolNombre}</Insignia>
          {datos.accesoVenceEn && <Insignia tono="neutral">Acceso hasta el {formatearFecha(new Date(datos.accesoVenceEn))}</Insignia>}
        </div>
        <p className="text-sm text-atlas-texto-suave">
          Tu cuenta será <strong className="text-foreground">{datos.correo}</strong>.
        </p>
        <p className="flex items-center gap-2 text-xs text-atlas-texto-suave">
          <CalendarClock className="size-4" aria-hidden="true" />
          El enlace vence el {formatearFechaHora(new Date(datos.enlaceVenceEn))}.
        </p>
      </section>

      <label className="text-sm font-medium">
        Tu nombre completo
        <input required autoComplete="name" value={nombreActual} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
      </label>

      <AceptarDocumentos documentos={datos.documentos} aceptados={aceptados} alCambiar={setAceptados} />

      {(!aceptados || nombreActual.trim().length === 0) && (
        <p className="text-sm text-atlas-texto-suave">Escribe tu nombre y acepta los documentos para crear tu cuenta.</p>
      )}

      <ConfigurarFactor
        textoBoton="Crear mi cuenta"
        deshabilitado={!aceptados || nombreActual.trim().length === 0}
        pedirOpcionesPasskey={() => opcionesPasskeyInvitacion(token, nombreActual.trim())}
        pedirClaveTotp={() => claveTotpInvitacion(token)}
        completar={async (factor) => {
          const lista = await aceptarInvitacion(token, nombreActual.trim(), aceptaciones(datos.documentos), factor);
          setCuenta(lista);
          return lista;
        }}
      />
    </PantallaPublica>
  );
}
