import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode, Suspense, useState } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router';
import { RouterProvider } from 'react-router/dom';

import { CargandoPagina, Shell } from './layout/Shell';
import type { ModuloAtlas } from './modulos/contrato';
import { diferido } from './modulos/diferidos';
import { MODULOS_REGISTRADOS } from './modulos/registro';
import { RutaDeModulo } from './modulos/RutaDeModulo';
import { ErrorDeRuta, NoEncontrada } from './paginas/Estados';
import Inicio from './paginas/Inicio';
import { Ingreso } from './paginas/Ingreso';
import { ProveedorSesion, useEstadoSesion } from './sesion';
import { Boton } from './ui/Tarjeta';

const Buscar = diferido(() => import('./paginas/Buscar'));
const Notificaciones = diferido(() => import('./paginas/Notificaciones'));
const Ajustes = diferido(() => import('./paginas/Ajustes'));
// Páginas públicas de las cuentas externas (HU 1.5): sin sesión y con el token en el fragmento de la URL.
const AceptarInvitacion = diferido(() => import('./externos/AceptarInvitacion'));
const Recuperar = diferido(() => import('./externos/Recuperar'));

/**
 * Raíz: sin sesión, el ingreso (en cualquier ruta, para volver a ella al entrar); con sesión, el shell. Mientras se
 * consulta `/yo` se mantiene la pantalla de arranque.
 */
function Raiz({ modulos }: { modulos: readonly ModuloAtlas[] }) {
  const sesion = useEstadoSesion();
  if (sesion.isPending) return <Arranque />;
  if (sesion.isError) return <SinServicio alReintentar={() => void sesion.refetch()} />;
  if (!sesion.data.yo) return <Ingreso />;
  return (
    <ProveedorSesion estado={{ ...sesion.data, yo: sesion.data.yo }} modulos={modulos} cargando={<Arranque />}>
      <Shell />
    </ProveedorSesion>
  );
}

export function rutasAtlas(modulos: readonly ModuloAtlas[] = MODULOS_REGISTRADOS): RouteObject[] {
  return [
    { path: 'invitacion', element: <Publica><AceptarInvitacion /></Publica>, errorElement: <ErrorDeRuta /> },
    { path: 'recuperar', element: <Publica><Recuperar /></Publica>, errorElement: <ErrorDeRuta /> },
    {
      element: <Raiz modulos={modulos} />,
      errorElement: <ErrorDeRuta />,
      children: [
        { index: true, element: <Inicio /> },
        { path: 'buscar', element: <Buscar /> },
        { path: 'notificaciones', element: <Notificaciones /> },
        { path: 'ajustes', element: <Ajustes /> },
        ...modulos.flatMap((modulo) =>
          modulo.rutas.map((ruta) => ({
            path: `${ruta.path.replace(/^\//, '')}/*`,
            element: <RutaDeModulo modulo={modulo} ruta={ruta} />,
          })),
        ),
        { path: '*', element: <NoEncontrada /> },
      ],
    },
  ];
}

export function crearClienteConsultas(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: true } },
  });
}

export function App({ modulos = MODULOS_REGISTRADOS }: { modulos?: readonly ModuloAtlas[] }) {
  const [cliente] = useState(crearClienteConsultas);
  const [enrutador] = useState(() => createBrowserRouter(rutasAtlas(modulos)));
  return (
    <QueryClientProvider client={cliente}>
      <RouterProvider router={enrutador} />
    </QueryClientProvider>
  );
}

/** Página pública: no depende de la sesión (si hay una, la aceptación o la recuperación la reemplazan). */
function Publica({ children }: { children: ReactNode }) {
  return <Suspense fallback={<Arranque />}>{children}</Suspense>;
}

/** Misma pantalla que el splash de index.html, para que no haya salto entre ambos. */
function Arranque() {
  return (
    <div className="splash" role="status" aria-label="Cargando Flit Atlas">
      <img src="/favicon.svg" alt="" />
      <span>Flit Atlas</span>
    </div>
  );
}

function SinServicio({ alReintentar }: { alReintentar: () => void }) {
  return (
    <main className="grid min-h-dvh place-items-center px-6">
      <div className="max-w-sm text-center">
        <h1 className="text-xl font-semibold">No se pudo conectar con Atlas</h1>
        <p role="alert" className="mt-2 text-sm text-atlas-texto-suave">
          Revisa tu conexión. Si el problema sigue, el servicio puede estar en mantenimiento.
        </p>
        <Boton className="mt-5" onClick={alReintentar}>
          Reintentar
        </Boton>
      </div>
    </main>
  );
}

export { CargandoPagina };
