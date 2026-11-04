import { useQueryClient } from '@tanstack/react-query';
import { Suspense, useEffect, useRef, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router';

import { sincronizarPush } from '../../pwa/push';
import { Paleta } from '../busqueda/Paleta';
import { CLAVE_NOTIFICACIONES } from '../notificaciones';
import { useSesion } from '../sesion';
import { AvisoEmergencia, AvisoNuevaVersion, AvisoSinConexion } from './Avisos';
import { BarraInferior } from './BarraInferior';
import { BarraLateral } from './BarraLateral';
import { Encabezado } from './Encabezado';
import { HojaMas } from './HojaMas';

/**
 * Shell móvil primero (§11): barra inferior y hoja «Más» en el móvil, barra lateral y paleta ⌘K en escritorio
 * (≥ 768 px). Ambas barras existen en el DOM y CSS decide cuál se ve, así no hay saltos al girar el teléfono.
 */
export function Shell() {
  const { navegacion } = useSesion();
  const [paleta, setPaleta] = useState(false);
  const [mas, setMas] = useState(false);
  const ubicacion = useLocation();
  const navegar = useNavigate();
  const cliente = useQueryClient();
  const principal = useRef<HTMLElement>(null);
  const primeraVez = useRef(true);

  // ⌘K / Ctrl+K abre la paleta en cualquier pantalla.
  useEffect(() => {
    const alPresionar = (evento: KeyboardEvent) => {
      if ((evento.metaKey || evento.ctrlKey) && evento.key.toLowerCase() === 'k') {
        evento.preventDefault();
        setPaleta((abierta) => !abierta);
      }
    };
    window.addEventListener('keydown', alPresionar);
    return () => window.removeEventListener('keydown', alPresionar);
  }, []);

  // Mensajes del service worker: llegó un push (refrescar la bandeja) o tocaron una notificación (navegar).
  useEffect(() => {
    if (!('serviceWorker' in navigator)) return;
    const alMensaje = (evento: MessageEvent<{ tipo?: string; ruta?: string }>) => {
      if (evento.data?.tipo === 'atlas:notificacion') void cliente.invalidateQueries({ queryKey: CLAVE_NOTIFICACIONES });
      if (evento.data?.tipo === 'atlas:navegar' && evento.data.ruta?.startsWith('/')) void navegar(evento.data.ruta);
    };
    navigator.serviceWorker.addEventListener('message', alMensaje);
    return () => navigator.serviceWorker.removeEventListener('message', alMensaje);
  }, [cliente, navegar]);

  // Si el dispositivo ya estaba suscrito a push, la suscripción queda a nombre de quien entró.
  useEffect(() => {
    void sincronizarPush().catch(() => undefined);
  }, []);

  // Al cambiar de página: título, scroll arriba y foco en el contenido para los lectores de pantalla.
  useEffect(() => {
    const entrada = [...navegacion.lateral]
      .reverse()
      .find((e) => (e.ruta === '/' ? ubicacion.pathname === '/' : ubicacion.pathname.startsWith(e.ruta)));
    document.title = entrada && entrada.ruta !== '/' ? `${entrada.etiqueta} · Flit Atlas` : 'Flit Atlas';
    if (primeraVez.current) {
      primeraVez.current = false;
      return;
    }
    window.scrollTo({ top: 0 });
    principal.current?.focus({ preventScroll: true });
  }, [ubicacion.pathname, navegacion.lateral]);

  return (
    <div className="min-h-dvh md:flex">
      <a
        href="#contenido"
        className="sr-only z-50 rounded-lg bg-card px-4 py-3 font-medium focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      >
        Saltar al contenido
      </a>
      <BarraLateral className="hidden md:flex" />
      <div className="flex min-w-0 flex-1 flex-col">
        <Encabezado alBuscar={() => setPaleta(true)} alAbrirMas={() => setMas(true)} />
        <AvisoEmergencia />
        <AvisoSinConexion />
        <main
          id="contenido"
          ref={principal}
          tabIndex={-1}
          className="w-full flex-1 pt-5 pr-[max(1rem,env(safe-area-inset-right))] pb-barra-inferior pl-[max(1rem,env(safe-area-inset-left))] outline-none md:px-8 md:pt-8 md:pb-12"
        >
          <Suspense fallback={<CargandoPagina />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
      <BarraInferior className="md:hidden" alAbrirMas={() => setMas(true)} />
      <HojaMas abierta={mas} alCerrar={() => setMas(false)} />
      <Paleta abierta={paleta} alCerrar={() => setPaleta(false)} />
      <AvisoNuevaVersion />
    </div>
  );
}

export function CargandoPagina() {
  return (
    <div role="status" aria-label="Cargando" className="mx-auto flex max-w-5xl flex-col gap-4">
      <div className="h-36 animate-pulse rounded-3xl bg-card" />
      <div className="grid gap-4 md:grid-cols-2">
        <div className="h-40 animate-pulse rounded-2xl bg-card" />
        <div className="h-40 animate-pulse rounded-2xl bg-card" />
      </div>
    </div>
  );
}
