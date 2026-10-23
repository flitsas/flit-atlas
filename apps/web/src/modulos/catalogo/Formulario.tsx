import { type FormEvent, useState } from 'react';

import { Hoja } from '../../app/ui/Hoja';
import { Boton } from '../../app/ui/Tarjeta';
import type { Ambiente, Criticidad, Estado, FichaAplicativo, Grupo, SolicitudAplicativo, Tipo } from './api';
import { type Aviso, avisoDeError, claseCampo, MostrarAviso, Opciones } from './componentes';
import { AMBIENTES, CRITICIDADES, ESTADOS, GRUPOS, opciones, TIPOS } from './textos';

/** Qué se edita: un aplicativo nuevo (con su padre, o ninguno para un producto) o uno existente. */
export type Edicion = { modo: 'crear'; padre: FichaAplicativo | null } | { modo: 'editar'; ficha: FichaAplicativo };

/** «Doctor FLIT» → «doctor-flit»: sin tildes, en minúsculas y con guiones, como exige el servidor. */
export function slugDe(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
}

/** «.NET 10, React» → [".NET 10", "React"]. */
const lista = (texto: string): string[] =>
  texto
    .split(',')
    .map((v) => v.trim())
    .filter(Boolean);

/** Tipos que puede tener un hijo de este padre (la regla de la jerarquía del servidor, ATLAS-ADR-0028). */
export function tiposHijo(padre: FichaAplicativo | null): Tipo[] {
  if (!padre) return ['producto'];
  if (padre.tipo === 'producto') return ['modulo', 'servicio'];
  if (padre.tipo === 'modulo') return ['servicio'];
  return [];
}

/**
 * Formulario de un aplicativo en una hoja inferior (móvil) o diálogo (escritorio). El slug, el tipo y el grupo solo se
 * cambian con `atlas.catalogo.gestionar`: el responsable edita los demás datos. El servidor valida todo otra vez.
 */
export function FormularioAplicativo({
  edicion,
  puedeGestionar,
  alCerrar,
  alGuardar,
}: {
  edicion: Edicion | null;
  puedeGestionar: boolean;
  alCerrar: () => void;
  alGuardar: (solicitud: SolicitudAplicativo) => Promise<Aviso>;
}) {
  const titulo =
    edicion === null
      ? 'Aplicativo'
      : edicion.modo === 'editar'
        ? `Editar ${edicion.ficha.nombre}`
        : edicion.padre
          ? `Nuevo en ${edicion.padre.nombre}`
          : 'Nuevo producto';
  const clave = edicion === null ? 'cerrado' : edicion.modo === 'editar' ? edicion.ficha.id : `nuevo-${edicion.padre?.id ?? 'raiz'}`;
  return (
    <Hoja abierta={edicion !== null} alCerrar={alCerrar} titulo={titulo}>
      {edicion !== null && <Campos key={clave} edicion={edicion} puedeGestionar={puedeGestionar} alGuardar={alGuardar} />}
    </Hoja>
  );
}

function Campos({
  edicion,
  puedeGestionar,
  alGuardar,
}: {
  edicion: Edicion;
  puedeGestionar: boolean;
  alGuardar: (solicitud: SolicitudAplicativo) => Promise<Aviso>;
}) {
  const ficha = edicion.modo === 'editar' ? edicion.ficha : null;
  const padre = edicion.modo === 'crear' ? edicion.padre : null;
  const tipos = ficha ? [ficha.tipo] : tiposHijo(padre);
  const [tipo, setTipo] = useState<Tipo>(ficha?.tipo ?? tipos[0] ?? 'producto');
  const [nombre, setNombre] = useState(ficha?.nombre ?? '');
  const [slug, setSlug] = useState(ficha?.slug ?? '');
  const [slugTocado, setSlugTocado] = useState(ficha !== null);
  const [grupo, setGrupo] = useState<Grupo>(ficha?.grupo ?? padre?.grupo ?? 'satelite');
  const [estado, setEstado] = useState<Estado>(ficha?.estado ?? 'activo');
  const [criticidad, setCriticidad] = useState<Criticidad>(ficha?.criticidad ?? 'media');
  const [datosPersonales, setDatosPersonales] = useState(ficha?.manejaDatosPersonales ?? false);
  const [descripcion, setDescripcion] = useState(ficha?.descripcion ?? '');
  const [repo, setRepo] = useState(ficha?.repo ?? padre?.repo ?? '');
  const [ruta, setRuta] = useState(ficha?.rutaEnRepo ?? '');
  const [tecnologias, setTecnologias] = useState((ficha?.tecnologias ?? []).join(', '));
  const [etiquetas, setEtiquetas] = useState((ficha?.etiquetas ?? []).join(', '));
  const [ambientes, setAmbientes] = useState<Ambiente[]>(ficha?.ambientes ?? []);
  const [enviando, setEnviando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(null);
  const estructura = ficha === null || puedeGestionar;
  const slugSugerido = (texto: string) => slugDe(padre ? `${padre.slug}-${texto}` : texto);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    setAviso(null);
    try {
      const resultado = await alGuardar({
        slug: slug.trim(),
        nombre: nombre.trim(),
        tipo,
        padreId: ficha ? ficha.padreId : (padre?.id ?? null),
        grupo: tipo === 'producto' ? grupo : null,
        descripcion: descripcion.trim() || null,
        estado,
        criticidad,
        manejaDatosPersonales: datosPersonales,
        repo: repo.trim() || null,
        rutaEnRepo: ruta.trim() || null,
        tecnologias: lista(tecnologias),
        ambientes,
        etiquetas: lista(etiquetas),
      });
      setAviso(resultado);
    } catch (error) {
      setAviso(avisoDeError(error, 'No se pudo guardar el aplicativo.'));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <form onSubmit={(e) => void enviar(e)} aria-label="Datos del aplicativo" className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 [&>*]:min-w-0">
      <MostrarAviso aviso={aviso} />
      <label className="text-sm font-medium">
        Nombre
        <input
          required
          maxLength={120}
          value={nombre}
          onChange={(e) => {
            setNombre(e.target.value);
            if (!slugTocado) setSlug(slugSugerido(e.target.value));
          }}
          className={claseCampo}
        />
      </label>
      <label className="text-sm font-medium">
        Slug
        <input
          required
          maxLength={80}
          pattern="[a-z0-9]+(-[a-z0-9]+)*"
          value={slug}
          readOnly={!estructura}
          aria-describedby="ayuda-slug"
          onChange={(e) => {
            setSlugTocado(true);
            setSlug(e.target.value);
          }}
          className={claseCampo}
        />
        <span id="ayuda-slug" className="mt-1 block text-xs font-normal text-atlas-texto-suave">
          Minúsculas, números y guiones. Es la dirección de la ficha (/catalogo/{slug || '…'}).
        </span>
      </label>
      {tipos.length > 1 ? (
        <Opciones leyenda="Tipo" valor={tipo} opciones={tipos.map((t) => ({ valor: t, etiqueta: TIPOS[t] }))} alCambiar={(v) => v && setTipo(v)} />
      ) : (
        <p className="text-sm">
          <span className="font-medium">Tipo:</span> {TIPOS[tipo]}
        </p>
      )}
      {tipo === 'producto' && (
        <Opciones
          leyenda="Grupo"
          valor={grupo}
          opciones={opciones(GRUPOS)}
          alCambiar={(v) => v && setGrupo(v)}
          deshabilitado={!estructura}
        />
      )}
      <Opciones leyenda="Estado" valor={estado} opciones={opciones(ESTADOS)} alCambiar={(v) => v && setEstado(v)} />
      <Opciones leyenda="Criticidad" valor={criticidad} opciones={opciones(CRITICIDADES)} alCambiar={(v) => v && setCriticidad(v)} />
      <label className="flex min-h-toque cursor-pointer items-start gap-3 text-sm">
        <input type="checkbox" checked={datosPersonales} onChange={(e) => setDatosPersonales(e.target.checked)} className="mt-1 size-5 shrink-0" />
        <span>
          <span className="block font-medium">Maneja datos personales</span>
          <span className="text-atlas-texto-suave">Trata datos de personas (Ley 1581): lo usan el asistente y el gobierno de IA.</span>
        </span>
      </label>
      <label className="text-sm font-medium">
        Descripción
        <textarea
          maxLength={2000}
          rows={4}
          value={descripcion}
          onChange={(e) => setDescripcion(e.target.value)}
          className={`${claseCampo} py-2`}
        />
      </label>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="min-w-0 text-sm font-medium">
          Repo (dueño/nombre)
          <input maxLength={200} value={repo} onChange={(e) => setRepo(e.target.value)} placeholder="flitsas/flit" className={claseCampo} />
        </label>
        <label className="min-w-0 text-sm font-medium">
          Ruta en el repo
          <input maxLength={300} value={ruta} onChange={(e) => setRuta(e.target.value)} placeholder="services/core-api" className={claseCampo} />
        </label>
      </div>
      <label className="text-sm font-medium">
        Tecnologías (separadas por coma)
        <input value={tecnologias} onChange={(e) => setTecnologias(e.target.value)} placeholder=".NET 10, PostgreSQL" className={claseCampo} />
      </label>
      <fieldset>
        <legend className="mb-1 text-sm font-medium">Ambientes</legend>
        <div className="flex flex-wrap gap-2">
          {opciones(AMBIENTES).map((a) => (
            <label key={a.valor} className="inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border border-border bg-card px-3 text-sm">
              <input
                type="checkbox"
                checked={ambientes.includes(a.valor)}
                onChange={(e) => setAmbientes((actual) => (e.target.checked ? [...actual, a.valor] : actual.filter((v) => v !== a.valor)))}
                className="size-4"
              />
              {a.etiqueta}
            </label>
          ))}
        </div>
      </fieldset>
      <label className="text-sm font-medium">
        Etiquetas (separadas por coma)
        <input value={etiquetas} onChange={(e) => setEtiquetas(e.target.value)} placeholder="ict, api-externa" className={claseCampo} />
      </label>
      <Boton type="submit" className="min-h-12" disabled={enviando}>
        Guardar
      </Boton>
    </form>
  );
}
