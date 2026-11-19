import { type FormEvent, useId, useState } from 'react';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { formatearRelativo } from '../../../app/ui/formato';
import { Icono } from '../../../app/ui/Icono';
import { Boton } from '../../../app/ui/Tarjeta';
import { LARGO_SALUDO, useCrearSaludo, useSaludos } from '../api';
import { moduloHola } from '../modulo';

/** Página del módulo de ejemplo: los saludos de tu organización y, con `atlas.hola.gestionar`, escribir uno. */
export default function Saludos() {
  const { acceso } = useSesion();
  const saludos = useSaludos();
  const puedeEscribir = tienePermiso('atlas.hola.gestionar', acceso.permisos);

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloHola.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Hola</h1>
          <p className="text-sm text-atlas-texto-suave">{moduloHola.descripcion}</p>
        </div>
      </header>

      {puedeEscribir && <NuevoSaludo />}

      {saludos.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar los saludos. Revisa tu conexión.
        </p>
      )}

      {saludos.data && saludos.data.length === 0 && (
        <p className="rounded-2xl border border-border bg-card px-6 py-10 text-center text-sm text-atlas-texto-suave shadow-tarjeta">
          Todavía no hay saludos en tu organización.
        </p>
      )}

      {saludos.data && saludos.data.length > 0 && (
        <section aria-labelledby="titulo-saludos">
          <h2 id="titulo-saludos" className="mb-2 text-base font-semibold">
            Saludos de tu organización
          </h2>
          <ul className="flex flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-tarjeta">
            {saludos.data.map((s) => (
              <li key={s.id} className="flex items-start justify-between gap-3 border-b border-border px-4 py-3 last:border-b-0">
                <p className="min-w-0 text-sm break-words">{s.texto}</p>
                <time dateTime={s.creadoEn} className="shrink-0 text-xs text-atlas-texto-suave">
                  {formatearRelativo(new Date(s.creadoEn))}
                </time>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function NuevoSaludo() {
  const crear = useCrearSaludo();
  const [texto, setTexto] = useState('');
  const id = useId();
  const recortado = texto.trim();
  const valido = recortado.length > 0 && recortado.length <= LARGO_SALUDO;

  function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!valido) return;
    crear.mutate(recortado, { onSuccess: () => setTexto('') });
  }

  return (
    <form aria-label="Escribir un saludo" onSubmit={enviar} className="flex flex-col gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <label htmlFor={id} className="text-sm font-medium">
        Tu saludo
      </label>
      <textarea
        id={id}
        value={texto}
        onChange={(e) => setTexto(e.target.value)}
        maxLength={LARGO_SALUDO}
        rows={3}
        aria-describedby={`${id}-ayuda`}
        className="w-full rounded-xl border border-border bg-card px-3 py-2 text-base outline-none focus-visible:border-atlas-tinta"
      />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p id={`${id}-ayuda`} className="text-xs text-atlas-texto-suave">
          {recortado.length} de {LARGO_SALUDO} caracteres
        </p>
        <Boton type="submit" disabled={!valido || crear.isPending}>
          Publicar saludo
        </Boton>
      </div>
      {crear.isError && (
        <p role="alert" className="text-sm">
          No se pudo publicar el saludo. Inténtalo de nuevo.
        </p>
      )}
    </form>
  );
}
