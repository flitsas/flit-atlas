import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowLeft, ArrowUp, FileSpreadsheet, Trash2 } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';

import { Boton, cn, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  agregarParticipante,
  buscarCandidatos,
  cambiarEquipo,
  cambiarParticipante,
  type CambioEquipo,
  CLAVE,
  type Configuracion,
  confirmarGeneracion,
  crearAusencia,
  crearEquipo,
  crearFestivo,
  eliminarAusencia,
  eliminarFestivo,
  type Equipo,
  guardarConfiguracion,
  listarAusencias,
  listarFestivos,
  obtenerConfiguracion,
  ordenarParticipantes,
  type Participante,
  previsualizarGeneracion,
  type ReglaFestivos,
  TEXTO_AUSENCIA,
  textoCupo,
  type TipoAusencia,
  useEquipos,
  useParticipantes,
  type VistaPrevia,
} from '../api';
import { type Aviso, avisoDeError, AvisoPendiente, claseCampo, FiltroEquipo, MostrarAviso, Opciones } from '../componentes';
import { formatearDia, formatearDiaLargo, hoy } from '../fechas';

/**
 * Gestión del soporte (atlas.soporte.gestionar): configuración global, equipos, y por equipo sus participantes, la
 * generación y las ausencias; además los festivos adicionales. El servidor decide el alcance: quien gestiona un solo
 * equipo recibe 403 en lo global.
 */
export default function Gestion() {
  const [aviso, setAviso] = useState<Aviso>(null);
  const equipos = useEquipos();
  const gestionables = (equipos.data ?? []).filter((e) => e.puedeGestionar);
  const [elegido, setElegido] = useState<string | null>(null);
  const equipo = gestionables.find((e) => e.id === elegido) ?? gestionables[0] ?? null;

  return (
    <div className="mx-auto flex max-w-4xl min-w-0 flex-col gap-5">
      <Link to="/soporte" className="inline-flex min-h-toque items-center gap-1 self-start text-sm font-medium text-atlas-tinta hover:underline">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Volver al calendario
      </Link>
      <h1 className="text-2xl font-semibold">Gestionar el soporte</h1>
      <MostrarAviso aviso={aviso} />
      <Link
        to="/soporte/importar"
        className="inline-flex min-h-toque items-center gap-2 self-start rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo"
      >
        <FileSpreadsheet className="size-4" aria-hidden="true" />
        Importar el Excel de la rotación
      </Link>
      <SeccionConfiguracion alAvisar={setAviso} />
      <SeccionEquipos equipos={equipos.data ?? []} alAvisar={setAviso} />
      {gestionables.length > 0 && equipo && (
        <section aria-label="Equipo que gestionas" className="flex min-w-0 flex-col gap-5">
          <FiltroEquipo equipos={gestionables} valor={equipo.id} alCambiar={(id) => setElegido(id)} conTodos={false} />
          <SeccionParticipantes key={`p-${equipo.id}`} equipo={equipo} alAvisar={setAviso} />
          <SeccionGeneracion key={`g-${equipo.id}`} equipo={equipo} alAvisar={setAviso} />
          <SeccionAusencias key={`a-${equipo.id}`} equipo={equipo} alAvisar={setAviso} />
        </section>
      )}
      <SeccionFestivos alAvisar={setAviso} />
    </div>
  );
}

type PropsSeccion = { alAvisar: (aviso: Aviso) => void };

/** Ejecuta una acción, refresca los datos del módulo y avisa el resultado. */
function useAccion(alAvisar: (aviso: Aviso) => void) {
  const cliente = useQueryClient();
  return async (trabajo: () => Promise<unknown>, exito: string, error: string): Promise<boolean> => {
    alAvisar(null);
    try {
      await trabajo();
      await cliente.invalidateQueries({ queryKey: CLAVE });
      alAvisar({ tono: 'exito', texto: exito });
      return true;
    } catch (e) {
      alAvisar(avisoDeError(e, error));
      return false;
    }
  };
}

// --- Configuración global ---

function SeccionConfiguracion({ alAvisar }: PropsSeccion) {
  const configuracion = useQuery({ queryKey: [...CLAVE, 'configuracion'], queryFn: obtenerConfiguracion });
  return (
    <Tarjeta titulo="Configuración general" icono="ajustes">
      {configuracion.isPending && <p role="status">Cargando la configuración…</p>}
      {configuracion.isError && <p role="alert">No se pudo cargar la configuración.</p>}
      {configuracion.data && <FormularioConfiguracion key={configuracion.data.actualizadoEn ?? 'inicial'} actual={configuracion.data} alAvisar={alAvisar} />}
    </Tarjeta>
  );
}

function FormularioConfiguracion({ actual, alAvisar }: { actual: Configuracion } & PropsSeccion) {
  const accion = useAccion(alAvisar);
  const [inicio, setInicio] = useState(actual.horaInicioPorDefecto);
  const [fin, setFin] = useState(actual.horaFinPorDefecto);
  const [regla, setRegla] = useState<ReglaFestivos>(actual.reglaFestivos);
  const [dias, setDias] = useState(actual.diasAnticipacionRecordatorio?.toString() ?? '');
  const [canal, setCanal] = useState(actual.canalExterno ?? '');
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    await accion(
      () =>
        guardarConfiguracion({
          horaInicioPorDefecto: inicio,
          horaFinPorDefecto: fin,
          reglaFestivos: regla,
          diasAnticipacionRecordatorio: dias ? Number(dias) : null,
          canalExterno: canal.trim() || null,
        }),
      'Se guardó la configuración.',
      'No se pudo guardar la configuración.',
    );
    setEnviando(false);
  }

  return (
    <form aria-label="Configuración del soporte" onSubmit={(e) => void enviar(e)} className="flex min-w-0 flex-col gap-4 text-sm">
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="mb-1 font-medium">Horario por defecto (hora de Bogotá)</legend>
        <div className="grid grid-cols-2 gap-3">
          <label className="min-w-0">
            Inicio
            <input type="time" required value={inicio} onChange={(e) => setInicio(e.target.value)} className={claseCampo} />
          </label>
          <label className="min-w-0">
            Fin
            <input type="time" required value={fin} onChange={(e) => setFin(e.target.value)} className={claseCampo} />
          </label>
        </div>
        <p className="text-xs text-atlas-texto-suave">Lo heredan los equipos sin horario propio; cada turno se puede cambiar.</p>
      </fieldset>
      <Opciones
        leyenda="Sábados festivos de Colombia"
        nombre="regla-festivos"
        valor={regla}
        alCambiar={setRegla}
        opciones={[
          { valor: 'sin_turno', texto: 'Se saltan', ayuda: 'La generación no programa soporte en un sábado festivo.' },
          { valor: 'turno_normal', texto: 'Turno normal', ayuda: 'El sábado festivo lleva turno como cualquier otro.' },
        ]}
      />
      <label className="font-medium">
        Recordatorio, días antes del turno
        <input type="number" min={1} max={30} inputMode="numeric" value={dias} onChange={(e) => setDias(e.target.value)} className={claseCampo} />
      </label>
      <label className="font-medium">
        Canal de soporte para externos
        <input type="text" maxLength={200} value={canal} onChange={(e) => setCanal(e.target.value)} className={claseCampo} placeholder="soporte@ejemplo.com" aria-describedby="ayuda-canal" />
      </label>
      <p id="ayuda-canal" className="-mt-2 text-xs text-atlas-texto-suave">
        Los clientes y proveedores solo ven esto (nunca nombres). Vacío: no ven nada del soporte.
      </p>
      <p className="text-xs text-atlas-texto-suave">Aprueba quien tenga el permiso de aprobar el soporte, en general o sobre un equipo.</p>
      <Boton type="submit" className="min-h-12 self-start" disabled={enviando}>
        Guardar configuración
      </Boton>
    </form>
  );
}

// --- Equipos ---

function SeccionEquipos({ equipos, alAvisar }: { equipos: Equipo[] } & PropsSeccion) {
  const accion = useAccion(alAvisar);
  const [editando, setEditando] = useState<Equipo | 'nuevo' | null>(null);
  return (
    <Tarjeta titulo="Equipos de soporte" icono="equipo">
      <div className="flex min-w-0 flex-col gap-3 text-sm">
        <ul aria-label="Equipos" className="flex flex-col gap-2">
          {equipos.map((e) => (
            <li key={e.id} className="flex min-w-0 flex-wrap items-center justify-between gap-2 rounded-xl border border-border p-3">
              <span className="min-w-0">
                <span className="block font-medium break-words">{e.nombre}</span>
                <span className="text-atlas-texto-suave">
                  {e.area ? `${e.area} · ` : ''}
                  {textoCupo(e)} · {e.horarioEfectivo}
                </span>
                {!e.activo && (
                  <span className="mt-1 block">
                    <Insignia tono="neutral">Inactivo</Insignia>
                  </span>
                )}
              </span>
              {e.puedeGestionar && (
                <Boton variante="secundario" onClick={() => setEditando(e)} aria-label={`Editar ${e.nombre}`}>
                  Editar
                </Boton>
              )}
            </li>
          ))}
        </ul>
        {editando === null ? (
          <Boton variante="secundario" className="self-start" onClick={() => setEditando('nuevo')}>
            Nuevo equipo
          </Boton>
        ) : (
          <FormularioEquipo
            key={editando === 'nuevo' ? 'nuevo' : editando.id}
            equipo={editando === 'nuevo' ? null : editando}
            alCancelar={() => setEditando(null)}
            alGuardar={async (cambio) => {
              const actual = editando;
              const ok = await accion(
                () => (actual === 'nuevo' ? crearEquipo(cambio) : cambiarEquipo(actual.id, cambio)),
                actual === 'nuevo' ? `Se creó el equipo ${cambio.nombre}.` : 'Se guardó el equipo.',
                'No se pudo guardar el equipo.',
              );
              if (ok) setEditando(null);
            }}
          />
        )}
      </div>
    </Tarjeta>
  );
}

function FormularioEquipo({
  equipo,
  alGuardar,
  alCancelar,
}: {
  equipo: Equipo | null;
  alGuardar: (cambio: CambioEquipo) => Promise<void>;
  alCancelar: () => void;
}) {
  const [nombre, setNombre] = useState(equipo?.nombre ?? '');
  const [area, setArea] = useState(equipo?.area ?? '');
  const [minimo, setMinimo] = useState(String(equipo?.personasMinimo ?? 1));
  const [maximo, setMaximo] = useState(String(equipo?.personasMaximo ?? 1));
  const [inicio, setInicio] = useState(equipo?.horaInicio ?? '');
  const [fin, setFin] = useState(equipo?.horaFin ?? '');
  const [activo, setActivo] = useState(equipo?.activo ?? true);

  return (
    <form
      aria-label={equipo ? `Editar ${equipo.nombre}` : 'Nuevo equipo'}
      onSubmit={(e) => {
        e.preventDefault();
        void alGuardar({
          nombre: nombre.trim(),
          area: area.trim() || null,
          personasMinimo: Number(minimo),
          personasMaximo: Number(maximo),
          horaInicio: inicio || null,
          horaFin: fin || null,
          activo,
        });
      }}
      className="flex min-w-0 flex-col gap-3 rounded-xl border border-border p-3"
    >
      <label className="font-medium">
        Nombre
        <input required maxLength={100} value={nombre} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
      </label>
      <label className="font-medium">
        Área
        <input maxLength={100} value={area} onChange={(e) => setArea(e.target.value)} className={claseCampo} />
      </label>
      <div className="grid grid-cols-2 gap-3">
        <label className="min-w-0 font-medium">
          Mínimo por sábado
          <input type="number" min={1} max={10} required inputMode="numeric" value={minimo} onChange={(e) => setMinimo(e.target.value)} className={claseCampo} />
        </label>
        <label className="min-w-0 font-medium">
          Máximo por sábado
          <input type="number" min={1} max={10} required inputMode="numeric" value={maximo} onChange={(e) => setMaximo(e.target.value)} className={claseCampo} />
        </label>
      </div>
      <fieldset className="flex min-w-0 flex-col gap-1">
        <legend className="font-medium">Horario propio (vacío: el general)</legend>
        <div className="grid grid-cols-2 gap-3">
          <label className="min-w-0">
            Inicio
            <input type="time" value={inicio} onChange={(e) => setInicio(e.target.value)} className={claseCampo} />
          </label>
          <label className="min-w-0">
            Fin
            <input type="time" value={fin} onChange={(e) => setFin(e.target.value)} className={claseCampo} />
          </label>
        </div>
      </fieldset>
      <label className="flex min-h-toque cursor-pointer items-center gap-3">
        <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} className="size-5 shrink-0" />
        Activo
      </label>
      <div className="flex flex-wrap gap-2">
        <Boton type="submit">Guardar equipo</Boton>
        <Boton variante="secundario" onClick={alCancelar}>
          Cancelar
        </Boton>
      </div>
    </form>
  );
}

// --- Participantes ---

function SeccionParticipantes({ equipo, alAvisar }: { equipo: Equipo } & PropsSeccion) {
  const participantes = useParticipantes(equipo.id);
  const accion = useAccion(alAvisar);
  const lista = participantes.data ?? [];

  function mover(indice: number, paso: number) {
    const nuevo = [...lista];
    const [p] = nuevo.splice(indice, 1);
    nuevo.splice(indice + paso, 0, p!);
    void accion(() => ordenarParticipantes(equipo.id, nuevo.map((x) => x.id)), 'Se cambió el orden de la rotación.', 'No se pudo cambiar el orden.');
  }

  return (
    <Tarjeta titulo={`Participantes de ${equipo.nombre}`} icono="equipo">
      <div className="flex min-w-0 flex-col gap-4 text-sm">
        <p className="text-atlas-texto-suave">{textoCupo(equipo)}. La generación reparte los sábados en este orden.</p>
        {participantes.isPending && <p role="status">Cargando…</p>}
        {participantes.data?.length === 0 && <p className="text-atlas-texto-suave">Todavía no hay nadie en la rotación de este equipo.</p>}
        {lista.length > 0 && (
          <ol aria-label="Orden de la rotación" className="flex flex-col gap-2">
            {lista.map((p, i) => (
              <FilaParticipante
                key={p.id}
                participante={p}
                primero={i === 0}
                ultimo={i === lista.length - 1}
                alMover={(paso) => mover(i, paso)}
                alCambiar={() =>
                  void accion(
                    () => cambiarParticipante(p.id, !p.activo),
                    p.activo ? `${p.nombre} salió de la generación.` : `${p.nombre} volvió a la generación.`,
                    'No se pudo cambiar.',
                  )
                }
              />
            ))}
          </ol>
        )}
        <AgregarParticipante
          equipo={equipo.id}
          alAgregar={(id, nombre) => void accion(() => agregarParticipante(equipo.id, id), `Se agregó a ${nombre}.`, 'No se pudo agregar.')}
        />
      </div>
    </Tarjeta>
  );
}

function FilaParticipante({
  participante: p,
  primero,
  ultimo,
  alMover,
  alCambiar,
}: {
  participante: Participante;
  primero: boolean;
  ultimo: boolean;
  alMover: (paso: number) => void;
  alCambiar: () => void;
}) {
  return (
    <li className="flex min-w-0 flex-wrap items-center gap-2 rounded-xl border border-border p-2">
      <span className="grid size-8 shrink-0 place-items-center rounded-full bg-atlas-activo text-xs font-semibold">{p.orden}</span>
      <span className="min-w-0 flex-1">
        <span className={cn('block font-medium break-words', !p.activo && 'text-atlas-texto-suave')}>{p.nombre}</span>
        <span className="flex flex-wrap gap-1">
          {!p.activo && <Insignia tono="neutral">Fuera de la generación</Insignia>}
          {p.estadoPersona !== 'activa' && <Insignia tono="neutral">Persona {p.estadoPersona}</Insignia>}
        </span>
      </span>
      <span className="flex gap-1">
        <Boton variante="fantasma" aria-label={`Subir a ${p.nombre}`} disabled={primero} onClick={() => alMover(-1)}>
          <ArrowUp className="size-4" aria-hidden="true" />
        </Boton>
        <Boton variante="fantasma" aria-label={`Bajar a ${p.nombre}`} disabled={ultimo} onClick={() => alMover(1)}>
          <ArrowDown className="size-4" aria-hidden="true" />
        </Boton>
        <Boton variante="secundario" onClick={alCambiar}>
          {p.activo ? 'Desactivar' : 'Activar'}
        </Boton>
      </span>
    </li>
  );
}

function AgregarParticipante({ equipo, alAgregar }: { equipo: string; alAgregar: (personaId: string, nombre: string) => void }) {
  const [texto, setTexto] = useState('');
  const consulta = texto.trim();
  const candidatos = useQuery({
    queryKey: [...CLAVE, 'candidatos', equipo, consulta],
    queryFn: ({ signal }) => buscarCandidatos(consulta, equipo, signal),
    enabled: consulta.length >= 2,
    staleTime: 30_000,
  });
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <label className="font-medium">
        Agregar persona interna
        <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Nombre o correo" className={claseCampo} />
      </label>
      {candidatos.data && candidatos.data.length === 0 && <p className="text-atlas-texto-suave">Nadie más coincide.</p>}
      {candidatos.data && candidatos.data.length > 0 && (
        <ul aria-label="Personas encontradas" className="flex flex-col gap-1">
          {candidatos.data.map((c) => (
            <li key={c.id} className="flex min-w-0 items-center justify-between gap-2 rounded-xl px-1">
              <span className="min-w-0">
                <span className="block break-words">{c.nombre}</span>
                <span className="block truncate text-xs text-atlas-texto-suave">{c.correo}</span>
              </span>
              <Boton
                variante="secundario"
                onClick={() => {
                  alAgregar(c.id, c.nombre);
                  setTexto('');
                }}
              >
                Agregar
              </Boton>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// --- Ausencias ---

function SeccionAusencias({ equipo, alAvisar }: { equipo: Equipo } & PropsSeccion) {
  const ausencias = useQuery({ queryKey: [...CLAVE, 'ausencias'], queryFn: listarAusencias });
  const participantes = useParticipantes(equipo.id);
  const accion = useAccion(alAvisar);
  const [persona, setPersona] = useState<string | null>(null);
  const [desde, setDesde] = useState(hoy());
  const [hasta, setHasta] = useState(hoy());
  const [tipo, setTipo] = useState<TipoAusencia>('vacaciones');

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!persona) return;
    if (await accion(() => crearAusencia({ personaId: persona, desde, hasta, tipo }), 'Se registró la ausencia.', 'No se pudo registrar la ausencia.')) {
      setPersona(null);
    }
  }

  return (
    <Tarjeta titulo="Ausencias" icono="calendario">
      <div className="flex min-w-0 flex-col gap-4 text-sm">
        <p className="text-atlas-texto-suave">
          Vacaciones, incapacidades u otras: la generación no asigna turnos esos días, en ningún equipo. El motivo solo lo ven quienes gestionan el soporte.
        </p>
        {ausencias.data && ausencias.data.length > 0 && (
          <ul aria-label="Ausencias vigentes" className="flex flex-col gap-2">
            {ausencias.data.map((a) => (
              <li key={a.id} className="flex min-w-0 items-center justify-between gap-2 rounded-xl border border-border p-2">
                <span className="min-w-0">
                  <span className="block font-medium break-words">{a.nombre}</span>
                  <span className="text-atlas-texto-suave">
                    {formatearDia(a.desde)} a {formatearDia(a.hasta)} · {TEXTO_AUSENCIA[a.tipo]}
                  </span>
                </span>
                <Boton
                  variante="fantasma"
                  aria-label={`Quitar la ausencia de ${a.nombre}`}
                  onClick={() => void accion(() => eliminarAusencia(a.id), 'Se quitó la ausencia.', 'No se pudo quitar.')}
                >
                  <Trash2 className="size-4" aria-hidden="true" />
                </Boton>
              </li>
            ))}
          </ul>
        )}
        <form aria-label="Nueva ausencia" onSubmit={(e) => void enviar(e)} className="flex min-w-0 flex-col gap-3">
          <Opciones
            leyenda={`Persona de ${equipo.nombre}`}
            nombre="persona-ausencia"
            valor={persona}
            alCambiar={setPersona}
            opciones={(participantes.data ?? []).map((p) => ({ valor: p.personaId, texto: p.nombre }))}
          />
          <div className="grid grid-cols-2 gap-3">
            <label className="min-w-0 font-medium">
              Desde
              <input type="date" required value={desde} onChange={(e) => setDesde(e.target.value)} className={claseCampo} />
            </label>
            <label className="min-w-0 font-medium">
              Hasta
              <input type="date" required value={hasta} min={desde} onChange={(e) => setHasta(e.target.value)} className={claseCampo} />
            </label>
          </div>
          <Opciones
            leyenda="Motivo"
            nombre="tipo-ausencia"
            valor={tipo}
            alCambiar={setTipo}
            opciones={(Object.keys(TEXTO_AUSENCIA) as TipoAusencia[]).map((t) => ({ valor: t, texto: TEXTO_AUSENCIA[t] }))}
          />
          <Boton type="submit" variante="secundario" disabled={!persona} className="self-start">
            Registrar ausencia
          </Boton>
        </form>
      </div>
    </Tarjeta>
  );
}

// --- Generación ---

const TEXTO_DECISION: Record<VistaPrevia['sabados'][number]['decision'], string> = {
  asignado: 'Con turnos nuevos',
  fijo: 'Ya publicado',
  festivo: 'Festivo: se salta',
  sin_disponibles: 'Faltan personas',
};

function SeccionGeneracion({ equipo, alAvisar }: { equipo: Equipo } & PropsSeccion) {
  const cliente = useQueryClient();
  const [meses, setMeses] = useState('3');
  const [previa, setPrevia] = useState<VistaPrevia | null>(null);
  const [cargando, setCargando] = useState(false);

  async function generar(evento: FormEvent) {
    evento.preventDefault();
    setCargando(true);
    alAvisar(null);
    try {
      setPrevia(await previsualizarGeneracion(equipo.id, Number(meses)));
    } catch (e) {
      setPrevia(null);
      alAvisar(avisoDeError(e, 'No se pudo generar la vista previa.'));
    } finally {
      setCargando(false);
    }
  }

  async function publicar() {
    if (!previa) return;
    setCargando(true);
    alAvisar(null);
    try {
      const r = await confirmarGeneracion(equipo.id, Number(meses), previa.desde, previa.huella);
      await cliente.invalidateQueries({ queryKey: CLAVE });
      setPrevia(null);
      alAvisar({ tono: 'exito', texto: `Se publicó la rotación de ${equipo.nombre}: ${r.creados} turnos nuevos y ${r.eliminados} quitados.` });
    } catch (e) {
      alAvisar(avisoDeError(e, 'No se pudo publicar la rotación.'));
    } finally {
      setCargando(false);
    }
  }

  return (
    <Tarjeta titulo={`Generación automática de ${equipo.nombre}`} icono="calendario">
      <div className="flex min-w-0 flex-col gap-4 text-sm">
        <p className="text-atlas-texto-suave">
          Reparte los sábados en orden entre los participantes activos ({textoCupo(equipo)}), con el mismo número de turnos por persona, saltando ausencias y turnos en otros equipos y aplicando la
          regla de festivos de Colombia. Los turnos ya confirmados o aplicados no cambian. Nada se publica sin confirmación.
        </p>
        <form aria-label="Generar rotación" onSubmit={(e) => void generar(e)} className="flex flex-wrap items-end gap-3">
          <label className="min-w-0 font-medium">
            Meses hacia adelante
            <input type="number" min={1} max={12} required inputMode="numeric" value={meses} onChange={(e) => setMeses(e.target.value)} className={cn(claseCampo, 'w-28')} />
          </label>
          <Boton type="submit" disabled={cargando}>
            Ver vista previa
          </Boton>
        </form>
        {previa && (
          <section aria-label="Vista previa" className="flex min-w-0 flex-col gap-3">
            <p>
              Del {formatearDia(previa.desde)} al {formatearDia(previa.hasta)}: {previa.nuevos} turnos nuevos y {previa.eliminados} quitados. Diferencia máxima entre personas:{' '}
              {previa.desbalance} {previa.desbalance === 1 ? 'turno' : 'turnos'}.
            </p>
            {previa.avisos.map((a) => (
              <AvisoPendiente key={a}>{a}</AvisoPendiente>
            ))}
            <ul aria-label="Turnos por persona" className="flex flex-wrap gap-2">
              {previa.turnosPorPersona.map((c) => (
                <li key={c.personaId}>
                  <Insignia tono="neutral">
                    {c.nombre}: {c.turnos}
                  </Insignia>
                </li>
              ))}
            </ul>
            <ol aria-label="Sábados propuestos" className="flex flex-col gap-1">
              {previa.sabados.map((s) => (
                <li key={s.fecha} className={cn('flex min-w-0 flex-wrap items-center justify-between gap-2 rounded-xl border border-border px-3 py-2', s.cambia && 'border-atlas-tinta')}>
                  <span className="min-w-0">
                    <span className="block font-medium first-letter:uppercase">{formatearDiaLargo(s.fecha)}</span>
                    <span className="block break-words">
                      {s.personas.length > 0 ? s.personas.map((p) => p.nombre).join(', ') : (s.festivo ?? '—')}
                    </span>
                  </span>
                  <span className="flex flex-wrap gap-1">
                    {s.festivo && s.decision !== 'festivo' && <Insignia tono="neutral">Festivo</Insignia>}
                    <Insignia tono={s.decision === 'asignado' ? 'info' : 'neutral'}>{TEXTO_DECISION[s.decision]}</Insignia>
                    {s.cambia && <Insignia tono="exito">Cambia</Insignia>}
                  </span>
                </li>
              ))}
            </ol>
            {equipo.puedeAprobar ? (
              <Boton onClick={() => void publicar()} disabled={cargando} className="min-h-12 self-start">
                Publicar esta rotación
              </Boton>
            ) : (
              <p className="text-atlas-texto-suave">La publica quien tenga el permiso de aprobar el soporte de este equipo.</p>
            )}
          </section>
        )}
      </div>
    </Tarjeta>
  );
}

// --- Festivos ---

function SeccionFestivos({ alAvisar }: PropsSeccion) {
  const [anio, setAnio] = useState(() => Number(hoy().slice(0, 4)));
  const festivos = useQuery({ queryKey: [...CLAVE, 'festivos', anio], queryFn: () => listarFestivos(anio) });
  const accion = useAccion(alAvisar);
  const [fecha, setFecha] = useState('');
  const [nombre, setNombre] = useState('');

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (await accion(() => crearFestivo(fecha, nombre.trim()), 'Se agregó el día no laborable.', 'No se pudo agregar el día.')) {
      setFecha('');
      setNombre('');
    }
  }

  return (
    <Tarjeta titulo={`Festivos de ${anio}`} icono="calendario">
      <div className="flex min-w-0 flex-col gap-4 text-sm">
        <p className="text-atlas-texto-suave">Calculados en Atlas con la Ley Emiliani (sin servicios externos). Agrega aquí los días cívicos que decrete el Gobierno.</p>
        <div className="flex gap-2">
          <Boton variante="secundario" onClick={() => setAnio(anio - 1)}>
            {anio - 1}
          </Boton>
          <Boton variante="secundario" onClick={() => setAnio(anio + 1)}>
            {anio + 1}
          </Boton>
        </div>
        {festivos.data && (
          <ul aria-label={`Festivos de ${anio}`} className="grid grid-cols-1 gap-1 sm:grid-cols-2">
            {festivos.data.map((f) => (
              <li key={`${f.fecha}-${f.nombre}`} className="flex min-w-0 items-center justify-between gap-2 rounded-lg px-2 py-1">
                <span className="min-w-0">
                  <span className="font-medium">{formatearDia(f.fecha)}</span> <span className="break-words">{f.nombre}</span>
                </span>
                {f.id && (
                  <Boton variante="fantasma" aria-label={`Quitar ${f.nombre}`} onClick={() => void accion(() => eliminarFestivo(f.id!), 'Se quitó el día.', 'No se pudo quitar.')}>
                    <Trash2 className="size-4" aria-hidden="true" />
                  </Boton>
                )}
              </li>
            ))}
          </ul>
        )}
        <form aria-label="Nuevo día no laborable" onSubmit={(e) => void enviar(e)} className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-end">
          <label className="min-w-0 font-medium">
            Fecha
            <input type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} className={claseCampo} />
          </label>
          <label className="min-w-0 flex-1 font-medium">
            Nombre
            <input type="text" required maxLength={100} value={nombre} onChange={(e) => setNombre(e.target.value)} className={claseCampo} />
          </label>
          <Boton type="submit" variante="secundario">
            Agregar
          </Boton>
        </form>
      </div>
    </Tarjeta>
  );
}
