import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, FileSpreadsheet } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';

import { Boton, cn, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  CLAVE,
  confirmarImportacion,
  type FilaImportacion,
  previsualizarImportacion,
  type SolicitudImportacion,
  TEXTO_ESTADO,
  useEquipos,
  type VistaPreviaImportacion,
} from '../api';
import { type Aviso, avisoDeError, AvisoPendiente, MostrarAviso, Opciones } from '../componentes';
import { formatearDia } from '../fechas';

/** Mismo tope que el servidor: la hoja real pesa unos 10 KB. */
const TAMANO_MAXIMO = 1024 * 1024;

/** Contenido del archivo en base64, por partes (un archivo de 1 MB no cabe en un solo `String.fromCharCode`). */
async function aBase64(archivo: File): Promise<string> {
  const bytes = new Uint8Array(await archivo.arrayBuffer());
  let binario = '';
  for (let i = 0; i < bytes.length; i += 0x8000) binario += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(binario);
}

const TEXTO_ACCION: Record<FilaImportacion['accion'], string> = { crear: 'Se importa', ya_importado: 'Ya importada', omitir: 'Se omite' };

/**
 * Importador del Excel de la rotación (HU 2.7): se sube el .xlsx, se elige el equipo destino (Desarrollo TI por defecto)
 * y se revisa la vista previa —cada interpretación y cada caso ambiguo— antes de importar. Lo que cambia la vista previa
 * (crear una persona inactiva, elegir a quién corresponde un nombre ambiguo) la vuelve a pedir: se importa exactamente
 * lo que se ve.
 */
export default function Importar() {
  const cliente = useQueryClient();
  const equipos = useEquipos();
  const gestionables = (equipos.data ?? []).filter((e) => e.puedeGestionar);
  const porDefecto = gestionables.find((e) => e.nombre === 'Desarrollo TI') ?? gestionables[0];
  const [equipo, setEquipo] = useState<string | null>(null);
  const destino = equipo ?? porDefecto?.id ?? null;
  const [archivo, setArchivo] = useState<{ nombre: string; contenido: string } | null>(null);
  const [crear, setCrear] = useState<string[]>([]);
  const [elegidas, setElegidas] = useState<Record<string, string>>({});
  const [participantes, setParticipantes] = useState(true);
  const [previa, setPrevia] = useState<VistaPreviaImportacion | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const [trabajando, setTrabajando] = useState(false);
  /** Ya se importó esta vista previa: se muestra como informe, sin el botón. */
  const [importada, setImportada] = useState(false);

  const solicitud = (cambios: Partial<SolicitudImportacion> = {}): SolicitudImportacion => ({
    equipoId: destino,
    nombreArchivo: archivo?.nombre ?? null,
    contenido: archivo?.contenido ?? '',
    crearInactivas: crear,
    elegidas: Object.entries(elegidas).map(([clave, personaId]) => ({ clave, personaId })),
    agregarParticipantes: participantes,
    ...cambios,
  });

  async function verPrevia(cambios: Partial<SolicitudImportacion> = {}) {
    setTrabajando(true);
    setAviso(null);
    setImportada(false);
    try {
      setPrevia(await previsualizarImportacion(solicitud(cambios)));
    } catch (e) {
      setPrevia(null);
      setAviso(avisoDeError(e, 'No se pudo leer el archivo.'));
    } finally {
      setTrabajando(false);
    }
  }

  async function elegirArchivo(lista: FileList | null) {
    const elegido = lista?.[0];
    setPrevia(null);
    setArchivo(null);
    if (!elegido) return;
    if (elegido.size > TAMANO_MAXIMO) {
      setAviso({ tono: 'error', texto: 'El archivo pesa más de 1 MB.' });
      return;
    }
    setAviso(null);
    setArchivo({ nombre: elegido.name, contenido: await aBase64(elegido) });
  }

  async function importar() {
    if (!previa) return;
    setTrabajando(true);
    setAviso(null);
    try {
      const { informe } = await confirmarImportacion({ ...solicitud(), huella: previa.huella });
      await cliente.invalidateQueries({ queryKey: CLAVE });
      setPrevia(informe);
      setImportada(true);
      const r = informe.resumen;
      setAviso({
        tono: 'exito',
        texto: `Se importaron ${r.importadas} filas en ${informe.equipo.nombre}: ${r.turnos} turnos, ${r.compensaciones} compensaciones y ${r.personasInactivas} personas inactivas nuevas.`,
      });
    } catch (e) {
      setAviso(avisoDeError(e, 'No se pudo importar.'));
    } finally {
      setTrabajando(false);
    }
  }

  function alternarPersona(clave: string, marcada: boolean) {
    const nuevas = marcada ? [...crear, clave] : crear.filter((c) => c !== clave);
    setCrear(nuevas);
    void verPrevia({ crearInactivas: nuevas });
  }

  function elegirPersona(clave: string, persona: string) {
    const nuevas = { ...elegidas, [clave]: persona };
    setElegidas(nuevas);
    void verPrevia({ elegidas: Object.entries(nuevas).map(([c, personaId]) => ({ clave: c, personaId })) });
  }

  return (
    <div className="mx-auto flex max-w-4xl min-w-0 flex-col gap-5">
      <Link to="/soporte/gestion" className="inline-flex min-h-toque items-center gap-1 self-start text-sm font-medium text-atlas-tinta hover:underline">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Volver a gestionar
      </Link>
      <h1 className="text-2xl font-semibold">Importar el Excel de la rotación</h1>
      <MostrarAviso aviso={aviso} />

      <Tarjeta titulo="Archivo y equipo" icono="calendario">
        <form
          aria-label="Archivo a importar"
          className="flex min-w-0 flex-col gap-4 text-sm"
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            void verPrevia();
          }}
        >
          <label className="font-medium">
            Archivo .xlsx (hoja «Rotacion Soporte»: Fecha, Responsable, Estado, Compensado, Observación)
            <input
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              onChange={(e) => void elegirArchivo(e.target.files)}
              className="mt-1 block w-full min-w-0 max-w-full text-sm file:mr-3 file:min-h-toque file:rounded-xl file:border file:border-border file:bg-card file:px-4"
            />
          </label>
          {gestionables.length > 1 && (
            <Opciones
              leyenda="Equipo destino"
              nombre="equipo-importacion"
              valor={destino}
              opciones={gestionables.map((e) => ({ valor: e.id, texto: e.nombre }))}
              alCambiar={(id) => {
                setEquipo(id);
                setPrevia(null);
              }}
            />
          )}
          <label className="flex min-h-toque cursor-pointer items-center gap-3">
            <input type="checkbox" checked={participantes} onChange={(e) => setParticipantes(e.target.checked)} className="size-5 shrink-0" />
            Agregar a la rotación del equipo a los responsables que aún no están
          </label>
          <Boton type="submit" className="self-start" disabled={!archivo || !destino || trabajando}>
            <FileSpreadsheet className="size-4" aria-hidden="true" />
            Ver vista previa
          </Boton>
        </form>
      </Tarjeta>

      {previa && (
        <section aria-label="Vista previa de la importación" className="flex min-w-0 flex-col gap-4">
          <Resumen previa={previa} />
          {previa.avisos.map((a) => (
            <AvisoPendiente key={a}>{a}</AvisoPendiente>
          ))}

          {previa.personasNuevas.length > 0 && (
            <Tarjeta titulo="Personas que no existen en Atlas" icono="equipo" className={cn(importada && 'hidden')}>
              <p className="mb-2 text-sm text-atlas-texto-suave">
                Se crean como personas inactivas: quedan en el historial y no entran en la generación de turnos.
              </p>
              <ul className="flex flex-col gap-1">
                {previa.personasNuevas.map((p) => (
                  <li key={p.clave}>
                    <label className="flex min-h-toque cursor-pointer items-start gap-3 text-sm">
                      <input
                        type="checkbox"
                        className="mt-0.5 size-5 shrink-0"
                        checked={crear.includes(p.clave)}
                        disabled={trabajando}
                        onChange={(e) => alternarPersona(p.clave, e.target.checked)}
                      />
                      <span className="min-w-0 break-words">
                        Crear a <strong>{p.nombre}</strong> como persona inactiva ({p.filas} {p.filas === 1 ? 'fila' : 'filas'}
                        {p.ultimaFecha && `, último turno el ${formatearDia(p.ultimaFecha)}`})
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </Tarjeta>
          )}

          {previa.nombresAmbiguos.map((a) => (
            <Tarjeta key={a.clave} titulo={`¿Quién es «${a.texto}»?`} icono="equipo">
              <Opciones
                leyenda="Elige la persona"
                nombre={`ambiguo-${a.clave}`}
                valor={a.elegida}
                opciones={a.candidatas.map((c) => ({ valor: c.id, texto: c.nombre, ayuda: c.activa ? undefined : 'Inactiva' }))}
                alCambiar={(id) => elegirPersona(a.clave, id)}
              />
            </Tarjeta>
          ))}

          {previa.participantesNuevos.length > 0 && (
            <p className="text-sm">
              Entran a la rotación de {previa.equipo.nombre}: {previa.participantesNuevos.map((p) => `${p.nombre}${p.activo ? '' : ' (inactiva)'}`).join(', ')}.
            </p>
          )}

          <ul aria-label="Informe por fila" className="flex min-w-0 flex-col gap-3">
            {previa.filas.map((f) => (
              <li key={f.fila} className="min-w-0">
                <TarjetaFila fila={f} />
              </li>
            ))}
          </ul>

          {!importada && (
            <Boton className="min-h-12 self-start" disabled={trabajando || previa.resumen.importadas === 0} onClick={() => void importar()}>
              {previa.resumen.importadas === 0 ? 'Nada nuevo que importar' : `Importar ${previa.resumen.importadas} ${previa.resumen.importadas === 1 ? 'fila' : 'filas'}`}
            </Boton>
          )}
        </section>
      )}
    </div>
  );
}

function Resumen({ previa }: { previa: VistaPreviaImportacion }) {
  const r = previa.resumen;
  const datos: [string, number][] = [
    ['Filas leídas', r.filas],
    ['Se importan', r.importadas],
    ['Ya importadas', r.yaImportadas],
    ['Omitidas', r.omitidas],
    ['Turnos nuevos', r.turnos],
    ['Compensaciones', r.compensaciones],
    ['Personas inactivas nuevas', r.personasInactivas],
    ['Interpretaciones', r.interpretaciones],
    ['Para revisión', r.ambiguos],
  ];
  return (
    <Tarjeta titulo={`Vista previa: ${previa.equipo.nombre}`} icono="calendario">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        {datos.map(([texto, valor]) => (
          <div key={texto} className="min-w-0 rounded-xl border border-border p-3">
            <dt className="text-xs text-atlas-texto-suave">{texto}</dt>
            <dd className="text-lg font-semibold">{valor}</dd>
          </div>
        ))}
      </dl>
    </Tarjeta>
  );
}

function TarjetaFila({ fila: f }: { fila: FilaImportacion }) {
  const nombre = f.responsable ? (f.responsable.nombre ?? f.responsable.texto) : 'Sin responsable';
  return (
    <article
      aria-label={`Fila ${f.fila}`}
      className={cn('flex min-w-0 flex-col gap-2 rounded-xl border border-border bg-card p-3 text-sm', f.accion === 'omitir' && 'border-dashed')}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">
          Fila {f.fila} · {f.fecha ? formatearDia(f.fecha) : (f.textoFecha ?? 'sin fecha')}
        </h3>
        <div className="flex flex-wrap gap-1.5">
          {f.requiereRevision && <Insignia tono="neutral">Para revisión</Insignia>}
          <Insignia tono={f.accion === 'crear' ? 'exito' : f.accion === 'omitir' ? 'neutral' : 'info'}>{TEXTO_ACCION[f.accion]}</Insignia>
        </div>
      </header>
      <p className="break-words">
        {nombre}
        {f.reemplazo && ` → lo tomó ${f.reemplazo.nombre ?? f.reemplazo.texto}`} · {TEXTO_ESTADO[f.estado]}
      </p>
      {f.motivo && <p className="break-words text-atlas-texto-suave">{f.motivo}</p>}
      {f.interpretaciones.length > 0 && (
        <ul aria-label="Interpretaciones" className="list-disc pl-5 text-xs">
          {f.interpretaciones.map((i) => (
            <li key={i} className="break-words">
              {i}
            </li>
          ))}
        </ul>
      )}
      {f.ambiguedades.length > 0 && (
        <ul aria-label="Casos ambiguos" className="list-disc pl-5 text-xs font-medium marker:text-[var(--badge-warning-fg)]">
          {f.ambiguedades.map((a) => (
            <li key={a} className="break-words">
              {a}
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}
