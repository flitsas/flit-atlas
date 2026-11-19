import { useQuery, useQueryClient } from '@tanstack/react-query';
import { FlaskConical, ShieldCheck } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { useLocation } from 'react-router';

import { entrarSimulado, obtenerProveedores, type Proveedores, urlEntrarMicrosoft } from '../api';
import { CLAVE_YO } from '../sesion';
import { MarcaAtlas } from '../ui/Marca';
import { Boton } from '../ui/Tarjeta';

const MENSAJES_ERROR: Record<string, string> = {
  cuenta_no_permitida: 'Esa cuenta no puede entrar a Atlas. Usa tu cuenta de FLIT.',
  inicio_fallido: 'No se pudo completar el inicio de sesión. Intenta de nuevo.',
};

/**
 * Ingreso de la HU 1.3: «Iniciar sesión con Microsoft» y, solo en local y dev, el proveedor simulado. Al entrar,
 * la persona vuelve a la ruta que pidió.
 */
export function Ingreso() {
  const ubicacion = useLocation();
  const error = new URLSearchParams(ubicacion.search).get('error');
  const proveedores = useQuery({ queryKey: ['identidad', 'proveedores'], queryFn: obtenerProveedores, retry: 1 });
  const volver = ubicacion.pathname === '/' ? '/' : `${ubicacion.pathname}${ubicacion.search}`;

  return (
    <div className="grid min-h-dvh lg:grid-cols-[1.1fr_1fr]">
      <section className="relative flex flex-col justify-between gap-10 overflow-hidden bg-portada px-6 pt-[max(2rem,env(safe-area-inset-top))] pb-10 text-white sm:px-10 lg:px-14 lg:py-14">
        <MarcaAtlas className="text-white" />
        <div className="max-w-lg">
          <h1 className="text-3xl leading-tight font-semibold sm:text-4xl">El conocimiento de FLIT, en un solo lugar.</h1>
          <p className="mt-4 text-white/85">
            Aplicativos, responsables, soporte de los sábados, contratos de integración y documentación, con el acceso que
            te corresponde.
          </p>
        </div>
        <p className="hidden items-center gap-2 text-sm text-white/75 lg:flex">
          <ShieldCheck className="size-4" aria-hidden="true" />
          Tu sesión vive solo en una cookie segura; Atlas no guarda tokens en el navegador.
        </p>
      </section>

      <main className="flex items-center justify-center px-6 py-10 pb-[max(2.5rem,env(safe-area-inset-bottom))] sm:px-10">
        <div className="w-full max-w-sm">
          <h2 className="text-2xl font-semibold">Ingresa a Atlas</h2>
          <p className="mt-1 text-sm text-atlas-texto-suave">Usa tu cuenta corporativa de FLIT.</p>

          {error && MENSAJES_ERROR[error] && (
            <p role="alert" className="mt-5 rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]">
              {MENSAJES_ERROR[error]}
            </p>
          )}

          <div className="mt-6 flex flex-col gap-4">
            {proveedores.isPending && <p role="status">Cargando…</p>}
            {proveedores.isError && <p role="alert">No se pudo conectar con Atlas.</p>}
            {proveedores.data && <Metodos proveedores={proveedores.data} volver={volver} />}
          </div>
        </div>
      </main>
    </div>
  );
}

function Metodos({ proveedores, volver }: { proveedores: Proveedores; volver: string }) {
  return (
    <section aria-label="Iniciar sesión" className="flex flex-col gap-5">
      {proveedores.microsoft && (
        <a
          href={urlEntrarMicrosoft(volver)}
          className="flex min-h-12 items-center justify-center gap-3 rounded-xl border border-border bg-card px-4 text-sm font-semibold shadow-tarjeta hover:border-atlas-tinta"
        >
          <LogoMicrosoft />
          Iniciar sesión con Microsoft
        </a>
      )}
      {proveedores.simulado && <IngresoSimulado />}
      {!proveedores.microsoft && !proveedores.simulado && <p>No hay métodos de ingreso configurados.</p>}
    </section>
  );
}

function IngresoSimulado() {
  const cliente = useQueryClient();
  const [correo, setCorreo] = useState('');
  const [nombre, setNombre] = useState('');
  const [fallo, setFallo] = useState(false);
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setFallo(false);
    setEnviando(true);
    try {
      await entrarSimulado(correo, nombre);
      await cliente.invalidateQueries({ queryKey: CLAVE_YO });
    } catch {
      setFallo(true);
    } finally {
      setEnviando(false);
    }
  }

  const campo =
    'mt-1 min-h-12 w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

  return (
    <form
      onSubmit={enviar}
      aria-label="Ingreso simulado (solo desarrollo)"
      className="flex flex-col gap-3 rounded-2xl border border-dashed border-border p-4"
    >
      <p className="flex items-center gap-2 text-xs font-medium text-atlas-texto-suave">
        <FlaskConical className="size-4" aria-hidden="true" />
        Ingreso simulado: solo existe en local y dev.
      </p>
      <label className="text-sm font-medium">
        Correo
        <input type="email" required autoComplete="email" value={correo} onChange={(e) => setCorreo(e.target.value)} className={campo} />
      </label>
      <label className="text-sm font-medium">
        Nombre
        <input required autoComplete="name" value={nombre} onChange={(e) => setNombre(e.target.value)} className={campo} />
      </label>
      <Boton type="submit" disabled={enviando} className="min-h-12">
        Entrar (simulado)
      </Boton>
      {fallo && <p role="alert" className="text-sm text-[var(--badge-danger-fg)]">No se pudo entrar.</p>}
    </form>
  );
}

function LogoMicrosoft() {
  return (
    <svg viewBox="0 0 21 21" className="size-5" aria-hidden="true" focusable="false">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}
