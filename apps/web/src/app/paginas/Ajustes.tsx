import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BellRing, LogOut, Send } from 'lucide-react';
import { useEffect, useState } from 'react';

import { activarPush, desactivarPush, estadoPush, type EstadoPush, instalada } from '../../pwa/push';
import { type Canal, enviarPushDePrueba, guardarPreferencia, obtenerPreferencias } from '../api';
import { useSesion } from '../sesion';
import { type Tema, useTema } from '../tema';
import { nombreRol } from '../ui/formato';
import { Boton, cn, Insignia, Tarjeta } from '../ui/Tarjeta';

const TEMAS: { valor: Tema; etiqueta: string; detalle: string }[] = [
  { valor: 'claro', etiqueta: 'Claro', detalle: 'Fondo claro' },
  { valor: 'oscuro', etiqueta: 'Oscuro', detalle: 'Fondo oscuro' },
  { valor: 'sistema', etiqueta: 'Sistema', detalle: 'Igual que tu dispositivo' },
];

const NOMBRES_CANAL: Record<Canal, string> = { app: 'En Atlas', push: 'Push', correo: 'Correo', teams: 'Teams' };

/** Ajustes de la persona: tema, notificaciones push del dispositivo, preferencias por tipo y cuenta. */
export default function Ajustes() {
  const { yo, cerrarSesion } = useSesion();
  const { tema, cambiar } = useTema();

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-5">
      <h1 className="text-2xl font-semibold">Ajustes</h1>

      <Tarjeta titulo="Apariencia" icono="ajustes">
        <fieldset>
          <legend className="sr-only">Tema</legend>
          <div className="grid gap-2 sm:grid-cols-3">
            {TEMAS.map((opcion) => (
              <label
                key={opcion.valor}
                className={cn(
                  'flex min-h-toque cursor-pointer flex-col justify-center rounded-xl border px-4 py-3 has-focus-visible:outline-2 has-focus-visible:outline-atlas-tinta',
                  tema === opcion.valor ? 'border-atlas-tinta bg-atlas-activo' : 'border-border',
                )}
              >
                <input
                  type="radio"
                  name="tema"
                  value={opcion.valor}
                  checked={tema === opcion.valor}
                  onChange={() => cambiar(opcion.valor)}
                  className="sr-only"
                />
                <span className="text-sm font-semibold">{opcion.etiqueta}</span>
                <span className="text-xs text-atlas-texto-suave">{opcion.detalle}</span>
              </label>
            ))}
          </div>
        </fieldset>
      </Tarjeta>

      <NotificacionesPush />
      <Preferencias />

      <Tarjeta titulo="Cuenta" icono="equipo">
        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-atlas-texto-suave">Nombre</dt>
            <dd className="font-medium">{yo.nombre}</dd>
          </div>
          <div>
            <dt className="text-atlas-texto-suave">Tipo de cuenta</dt>
            <dd className="font-medium">{yo.tipoPrincipal === 'empleado' ? 'Empleado de FLIT' : 'Externo'}</dd>
          </div>
          <div className="sm:col-span-2">
            <dt className="text-atlas-texto-suave">Roles</dt>
            <dd className="mt-1 flex flex-wrap gap-1.5">
              {yo.roles.length > 0 ? yo.roles.map((r) => <Insignia key={r}>{nombreRol(r)}</Insignia>) : <Insignia tono="neutral">Sin roles todavía</Insignia>}
            </dd>
          </div>
        </dl>
        <Boton variante="secundario" className="mt-4" onClick={() => void cerrarSesion()}>
          <LogOut className="size-4" aria-hidden="true" />
          Cerrar sesión
        </Boton>
        <p className="mt-2 text-xs text-atlas-texto-suave">
          Al cerrar sesión se borran de este dispositivo los datos guardados sin conexión y su suscripción a notificaciones.
        </p>
      </Tarjeta>

      <p className="text-center text-xs text-atlas-texto-suave">
        Flit Atlas · versión {__ATLAS_VERSION__} {instalada() ? '· instalada' : ''}
      </p>
    </div>
  );
}

const TEXTOS_PUSH: Record<EstadoPush, string> = {
  activo: 'Este dispositivo recibe notificaciones de Atlas.',
  inactivo: 'Activa las notificaciones para recibir en este dispositivo los recordatorios y avisos de Atlas.',
  bloqueado: 'Bloqueaste las notificaciones de Atlas en este navegador. Habilítalas en la configuración del sitio y vuelve aquí.',
  ios_sin_instalar:
    'En iPhone y iPad las notificaciones solo funcionan con Atlas instalado (iOS 16.4 o posterior): toca Compartir y luego «Agregar a inicio», abre Atlas desde ese ícono y vuelve aquí.',
  no_soportado: 'Este navegador no admite notificaciones push. Seguirás viéndolas en el centro de notificaciones.',
  deshabilitado_en_servidor: 'Las notificaciones push aún no están habilitadas en este ambiente.',
};

function NotificacionesPush() {
  const [estado, setEstado] = useState<EstadoPush | null>(null);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const cliente = useQueryClient();

  useEffect(() => {
    void estadoPush().then(setEstado, () => setEstado('no_soportado'));
  }, []);

  const cambiar = useMutation({
    mutationFn: async (activar: boolean) => {
      if (activar) return activarPush();
      await desactivarPush();
      return 'inactivo' as const;
    },
    onSuccess: (nuevo) => {
      setEstado(nuevo);
      setMensaje(null);
    },
    onError: () => setMensaje('No se pudo cambiar la suscripción. Intenta de nuevo.'),
  });

  const prueba = useMutation({
    mutationFn: enviarPushDePrueba,
    onSuccess: ({ dispositivos }) => {
      setMensaje(
        dispositivos > 0
          ? `Enviamos una notificación de prueba a ${dispositivos === 1 ? 'tu dispositivo' : `tus ${dispositivos} dispositivos`}.`
          : 'Creamos la notificación de prueba en el centro de notificaciones.',
      );
      void cliente.invalidateQueries({ queryKey: ['plataforma', 'notificaciones'] });
    },
    onError: () => setMensaje('No se pudo enviar la notificación de prueba.'),
  });

  return (
    <section id="notificaciones-push" aria-labelledby="titulo-push" className="scroll-mt-24 rounded-2xl border border-border bg-card p-4 shadow-tarjeta sm:p-5">
      <h2 id="titulo-push" className="mb-3 flex items-center gap-2 text-base font-semibold">
        <span className="grid size-8 place-items-center rounded-lg bg-atlas-activo text-atlas-tinta">
          <BellRing className="size-4.5" aria-hidden="true" />
        </span>
        Notificaciones en este dispositivo
      </h2>
      <p className="text-sm text-atlas-texto-suave">{estado ? TEXTOS_PUSH[estado] : 'Revisando…'}</p>
      <div className="mt-4 flex flex-wrap gap-2">
        {(estado === 'inactivo' || estado === 'deshabilitado_en_servidor') && (
          <Boton onClick={() => cambiar.mutate(true)} disabled={cambiar.isPending}>
            <BellRing className="size-4" aria-hidden="true" />
            Activar notificaciones
          </Boton>
        )}
        {estado === 'activo' && (
          <Boton variante="secundario" onClick={() => cambiar.mutate(false)} disabled={cambiar.isPending}>
            Desactivar en este dispositivo
          </Boton>
        )}
        <Boton variante="secundario" onClick={() => prueba.mutate()} disabled={prueba.isPending}>
          <Send className="size-4" aria-hidden="true" />
          Enviar una de prueba
        </Boton>
      </div>
      {mensaje && (
        <p role="status" className="mt-3 text-sm">
          {mensaje}
        </p>
      )}
    </section>
  );
}

function Preferencias() {
  const cliente = useQueryClient();
  const preferencias = useQuery({ queryKey: ['plataforma', 'preferencias'], queryFn: obtenerPreferencias });
  const guardar = useMutation({
    mutationFn: ({ tipo, canal, activa }: { tipo: string; canal: Canal; activa: boolean }) => guardarPreferencia(tipo, canal, activa),
    onSettled: () => cliente.invalidateQueries({ queryKey: ['plataforma', 'preferencias'] }),
  });

  return (
    <Tarjeta titulo="Qué te notificamos" icono="notificaciones">
      {preferencias.isError && <p role="alert" className="text-sm">No pudimos cargar tus preferencias.</p>}
      {preferencias.isPending && <p className="text-sm text-atlas-texto-suave">Cargando…</p>}
      <div className="flex flex-col divide-y divide-border">
        {preferencias.data?.map((tipo) => (
          <fieldset key={tipo.tipo} className="py-3 first:pt-0 last:pb-0">
            <legend className="text-sm font-semibold">{tipo.nombre}</legend>
            <p className="text-xs text-atlas-texto-suave">{tipo.descripcion}</p>
            <div className="mt-2 flex flex-wrap gap-2">
              {tipo.canales.map((canal) => (
                <label
                  key={canal.canal}
                  className={cn(
                    'inline-flex min-h-toque items-center gap-2 rounded-xl border border-border px-3 text-sm',
                    (!canal.disponible || !canal.editable) && 'opacity-70',
                  )}
                >
                  <input
                    type="checkbox"
                    className="size-4 accent-[var(--flit-brand-ink)]"
                    checked={canal.activa}
                    disabled={!canal.disponible || !canal.editable || guardar.isPending}
                    onChange={(e) => guardar.mutate({ tipo: tipo.tipo, canal: canal.canal, activa: e.target.checked })}
                  />
                  {NOMBRES_CANAL[canal.canal]}
                  {!canal.disponible && <span className="text-xs text-atlas-texto-suave">(no disponible)</span>}
                </label>
              ))}
            </div>
          </fieldset>
        ))}
      </div>
    </Tarjeta>
  );
}
