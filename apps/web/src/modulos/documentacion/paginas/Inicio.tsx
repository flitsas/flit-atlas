import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { cn } from '../../../app/ui/Tarjeta';
import { type DocumentoResumen, TEXTO_TIPO, type TipoDocumento, useAplicativos, useDocumentos } from '../api';
import { Cargando, claseCampo, TarjetaDocumento } from '../componentes';
import { ExportarDocumentos } from '../ExportarDocumentos';
import { ListaFavoritos } from '../favoritos/componentes';

const TIPOS = Object.keys(TEXTO_TIPO) as TipoDocumento[];
const claseFiltro = 'inline-flex min-h-toque cursor-pointer items-center rounded-xl border px-3 text-sm';
/** El tope de una colección en la API. */
const MAXIMO_COLECCION = 50;

/** Lista de documentos por aplicativo con búsqueda full-text (HU 5.4 y 5.5, ATLAS-ADR-0053). */
export default function InicioDocumentacion() {
  const { acceso } = useSesion();
  const [parametros, setParametros] = useSearchParams();
  const [texto, setTexto] = useState(parametros.get('texto') ?? '');
  const tipo = parametros.get('tipo') ?? '';
  const buscado = parametros.get('texto') ?? '';
  const lista = useDocumentos({ texto: buscado, tipo });
  const aplicativos = useAplicativos();

  const nombres = useMemo(() => new Map((aplicativos.data ?? []).map((a) => [a.id, a.nombre])), [aplicativos.data]);
  const grupos = useMemo(() => {
    const porAplicativo = new Map<string, DocumentoResumen[]>();
    for (const d of lista.data?.elementos ?? []) porAplicativo.set(d.aplicativoId, [...(porAplicativo.get(d.aplicativoId) ?? []), d]);
    return [...porAplicativo.entries()]
      .map(([id, documentos]) => ({ id, nombre: nombres.get(id) ?? 'Otros documentos', documentos }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'));
  }, [lista.data, nombres]);

  const exportables = (lista.data?.elementos ?? []).filter((d) => d.versionVigente !== null);
  const puedeExportar = tienePermiso('atlas.docs.exportar', acceso.permisos);
  const exportacion = parametros.get('exportacion');

  const cambiar = (clave: string, valor: string) =>
    setParametros(
      (p) => {
        if (valor) p.set(clave, valor);
        else p.delete(clave);
        return p;
      },
      { replace: true },
    );

  return (
    <div className="mx-auto flex max-w-5xl min-w-0 flex-col gap-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Documentación</h1>
        {tienePermiso('atlas.docs.editar', acceso.permisos) && (
          <Link
            to="/documentacion/nuevo"
            className="inline-flex min-h-toque items-center gap-2 rounded-xl bg-flit-primary px-4 text-sm font-medium text-white hover:bg-[#1f3a6b] dark:bg-flit-brand dark:text-[#0b1220]"
          >
            <Plus className="size-4" aria-hidden="true" /> Nuevo documento
          </Link>
        )}
      </header>

      <form
        role="search"
        aria-label="Buscar documentos"
        onSubmit={(e) => {
          e.preventDefault();
          cambiar('texto', texto.trim());
        }}
      >
        <label className="text-sm font-medium">
          Buscar en los documentos
          <input type="search" maxLength={200} value={texto} onChange={(e) => setTexto(e.target.value)} onBlur={() => cambiar('texto', texto.trim())} className={claseCampo} />
        </label>
      </form>

      <fieldset className="min-w-0">
        <legend className="mb-1 text-sm font-medium">Tipo de documento</legend>
        <div className="flex flex-wrap gap-2">
          {[{ valor: '', texto: 'Todos' }, ...TIPOS.map((t) => ({ valor: t as string, texto: TEXTO_TIPO[t] }))].map((o) => (
            <label key={o.valor} className={cn(claseFiltro, 'focus-within:outline-2', tipo === o.valor ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card')}>
              <input type="radio" name="tipo-documento" className="sr-only" checked={tipo === o.valor} onChange={() => cambiar('tipo', o.valor)} />
              {o.texto}
            </label>
          ))}
        </div>
      </fieldset>

      <ListaFavoritos />

      {lista.isPending && <Cargando etiqueta="Cargando los documentos" alto="h-40" />}
      {lista.isError && <p role="alert">No pudimos cargar los documentos. Revisa tu conexión.</p>}
      {lista.isSuccess && grupos.length === 0 && (
        <p className="text-sm text-atlas-texto-suave">{buscado || tipo ? 'Ningún documento coincide con la búsqueda.' : 'Todavía no hay documentos que puedas ver.'}</p>
      )}
      {puedeExportar && (exportacion || (exportables.length > 0 && exportables.length <= MAXIMO_COLECCION)) && (
        <div className="self-start">
          <ExportarDocumentos
            documentos={exportables.map((d) => ({ documentoId: d.id }))}
            nombre={buscado || tipo ? 'Documentos seleccionados' : 'Documentación'}
            etiqueta={`Exportar lo listado (${exportables.length})`}
            exportacionInicial={exportacion}
          />
        </div>
      )}
      {puedeExportar && exportables.length > MAXIMO_COLECCION && (
        <p role="note" className="text-sm text-atlas-texto-suave">
          Para exportar como colección, acota a {MAXIMO_COLECCION} documentos o menos con la búsqueda o el tipo.
        </p>
      )}
      {grupos.map((g) => (
        <section key={g.id} aria-label={g.nombre} className="flex flex-col gap-2">
          <h2 className="text-lg font-semibold">{g.nombre}</h2>
          <ul className="grid gap-2 sm:grid-cols-2">
            {g.documentos.map((d) => (
              <li key={d.id} className="min-w-0">
                <TarjetaDocumento documento={d} />
              </li>
            ))}
          </ul>
        </section>
      ))}
      {lista.data && lista.data.total > lista.data.elementos.length && (
        <p role="note" className="text-sm text-atlas-texto-suave">
          Se muestran {lista.data.elementos.length} de {lista.data.total}: usa la búsqueda o el tipo para acotar.
        </p>
      )}
    </div>
  );
}
