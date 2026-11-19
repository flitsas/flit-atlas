import { useQuery } from '@tanstack/react-query';
import { Download, PhoneCall } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import type { AplicativoDeFicha } from '../../../app/modulos/contrato';
import { DatosGuardados } from '../../../app/ui/DatosGuardados';
import { Boton, Insignia } from '../../../app/ui/Tarjeta';
import { iniciales } from '../../../app/ui/formato';
import { Avatar } from '../../../app/ui/Marca';
import { CLAVE_REPARTO, fechaCorta, obtenerAQuienLlamo, obtenerFicha, obtenerResponsablesDe, useReparto } from '../apiResponsables';
import { Bloque, BotonCopiar, Contacto, Definiciones, descargar, TarjetaResponsable } from '../componentes';
import { RECURSO_REPARTO } from '../recursos';
import { TarjetaTraspaso } from '../vistas/Traspasos';

/**
 * Pestaña «Responsables» de la ficha del aplicativo (`pestanasFicha` del catálogo, HU 2.3 a 2.5): principal y secundario
 * (o los del ancestro), las definiciones de cada rol, el traspaso del secundario, el botón «¿A quién llamo?» y la ficha
 * de responsables en Markdown para el README.
 */
export default function PestanaResponsables({ aplicativo }: { aplicativo: AplicativoDeFicha }) {
  const responsables = useReparto(['aplicativo', aplicativo.slug], () => obtenerResponsablesDe(aplicativo.slug));
  const [llamar, setLlamar] = useState(false);
  const [ficha, setFicha] = useState(false);
  const datos = responsables.data;

  if (responsables.isPending) return <p role="status">Cargando los responsables…</p>;
  if (responsables.isError || !datos) return <p role="alert">No pudimos cargar los responsables. Revisa tu conexión.</p>;

  return (
    <div className="flex flex-col gap-4">
      <DatosGuardados recurso={RECURSO_REPARTO.clave} />
      <div className="flex flex-wrap items-center gap-2">
        <Boton onClick={() => setLlamar((v) => !v)} aria-expanded={llamar}>
          <PhoneCall className="size-4" aria-hidden="true" />
          ¿A quién llamo?
        </Boton>
        <Boton variante="secundario" onClick={() => setFicha((v) => !v)} aria-expanded={ficha}>
          Ficha para el README
        </Boton>
      </div>
      {llamar && <AQuienLlamo slug={aplicativo.slug} />}
      {ficha && <FichaMarkdown slug={aplicativo.slug} />}

      {datos.heredadoDe && (
        <p className="text-sm">
          {aplicativo.nombre} no tiene responsables propios: son los de{' '}
          <Link to={`/catalogo/${datos.heredadoDe.slug}`} className="font-medium text-atlas-tinta hover:underline">
            {datos.heredadoDe.nombre}
          </Link>
          .
        </p>
      )}
      {!datos.principal && !datos.secundario ? (
        <p className="text-sm text-atlas-texto-suave">
          Aún no tiene responsables en el reparto
          {datos.puedeGestionar && (
            <>
              {' '}
              (
              <Link to="/equipo/responsables" className="font-medium text-atlas-tinta hover:underline">
                asígnalos
              </Link>
              )
            </>
          )}
          .
        </p>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2">
          <TarjetaResponsable tipo="principal" responsable={datos.principal} contacto />
          <TarjetaResponsable tipo="secundario" responsable={datos.secundario} contacto />
        </div>
      )}
      {datos.traspaso && <TarjetaTraspaso traspaso={datos.traspaso} comoItem={false} />}
      {datos.definiciones.length > 0 && (
        <details>
          <summary className="min-h-toque cursor-pointer text-sm font-medium">Qué significa cada rol</summary>
          <div className="mt-2">
            <Definiciones definiciones={datos.definiciones} />
          </div>
        </details>
      )}
      {datos.version !== null && <p className="text-xs text-atlas-texto-suave">Reparto versión {datos.version}.</p>}
    </div>
  );
}

/** «¿A quién llamo?» (§8.2, HU 2.5): el soporte de turno si hoy es sábado (por equipo) y el principal y el secundario. */
function AQuienLlamo({ slug }: { slug: string }) {
  const llamada = useQuery({ queryKey: [...CLAVE_REPARTO, 'a-quien-llamo', slug], queryFn: () => obtenerAQuienLlamo(slug), staleTime: 60_000 });
  const datos = llamada.data;
  return (
    <section aria-label="¿A quién llamo?" className="flex flex-col gap-3 rounded-2xl border border-atlas-tinta/40 bg-atlas-activo p-4">
      {llamada.isPending && <p role="status">Buscando a quién llamar…</p>}
      {llamada.isError && <p role="alert">No pudimos saber a quién llamar. Revisa tu conexión.</p>}
      {datos && (
        <>
          {datos.esSabado && datos.soporte && (
            <div className="flex flex-col gap-2">
              <p className="flex flex-wrap items-center gap-2 font-semibold">
                Hoy sábado {fechaCorta(datos.soporte.fecha)}: soporte de turno
                {datos.soporte.festivo && <Insignia tono="neutral">Festivo: {datos.soporte.festivo}</Insignia>}
              </p>
              <ul aria-label="Soporte de turno por equipo" className="grid gap-3 sm:grid-cols-2">
                {datos.soporte.equipos.map((equipo) => (
                  <li key={equipo.id} className="min-w-0">
                    <p className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">
                      {equipo.nombre}
                      {equipo.horaInicio && equipo.horaFin && ` · ${equipo.horaInicio} a ${equipo.horaFin}`}
                    </p>
                    {equipo.personas.length === 0 && <p className="text-sm text-atlas-texto-suave">Sin turno hoy.</p>}
                    {equipo.personas.map((p) => (
                      <div key={p.id} className="mt-1 flex min-w-0 flex-col gap-1">
                        <p className="flex items-center gap-2 font-medium break-words">
                          <Avatar iniciales={iniciales(p.nombre)} foto={p.foto} className="size-8" />
                          {p.nombre}
                        </p>
                        <Contacto correo={p.correo} telefono={p.telefono} />
                      </div>
                    ))}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {!datos.esSabado && <p className="text-sm">Hoy no es sábado: llama primero al principal y, si no responde, al secundario.</p>}
          <div className="grid gap-4 sm:grid-cols-2">
            <TarjetaResponsable tipo="principal" responsable={datos.principal} contacto />
            <TarjetaResponsable tipo="secundario" responsable={datos.secundario} contacto />
          </div>
          {!datos.veContacto && <p className="text-xs text-atlas-texto-suave">Los teléfonos solo se muestran a quien tiene permiso de ver el contacto.</p>}
        </>
      )}
    </section>
  );
}

function FichaMarkdown({ slug }: { slug: string }) {
  const ficha = useQuery({ queryKey: [...CLAVE_REPARTO, 'ficha', slug], queryFn: () => obtenerFicha(slug) });
  if (ficha.isPending) return <p role="status">Generando la ficha…</p>;
  if (ficha.isError || !ficha.data) return <p role="alert">No pudimos generar la ficha.</p>;
  return (
    <div className="flex flex-col gap-2">
      <Bloque texto={ficha.data.markdown} etiqueta="Ficha de responsables en Markdown" />
      <div className="flex flex-wrap gap-2">
        <BotonCopiar texto={ficha.data.markdown} etiqueta="Copiar ficha" />
        <Boton variante="secundario" onClick={() => descargar(`responsables-${slug}.md`, ficha.data.markdown)}>
          <Download className="size-4" aria-hidden="true" />
          Descargar
        </Boton>
      </div>
    </div>
  );
}
