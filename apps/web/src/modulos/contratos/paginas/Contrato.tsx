import { ArrowLeft, Download, History, Hourglass, Package, Radio } from 'lucide-react';
import { lazy, type ReactNode, Suspense, useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { NoEncontrada } from '../../../app/paginas/Estados';
import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, Tarjeta } from '../../../app/ui/Tarjeta';
import { type ContratoResumen, TIPOS, useContrato, useOperaciones, useSpec, useVersiones, type VersionResumen } from '../api';
import { MatrizObligatoriedad } from '../componentes/MatrizObligatoriedad';
import { Operaciones } from '../componentes/Operaciones';
import { PuntoGuias } from '../componentes/PuntoGuias';
import { TablasReferencia } from '../componentes/TablasReferencia';
import { Etiqueta, type Pestana, Pestanas, useEscritorio } from '../componentes/ui';
import { Versiones } from '../componentes/Versiones';
import { VistaAsyncApi } from '../componentes/VistaAsyncApi';
import { VistaProto } from '../componentes/VistaProto';
import { documentoAsync } from '../spec/asyncapi';
import { descargar } from '../spec/csv';
import { leerSpec, type Objeto } from '../spec/documento';
import { infoDe, operacionesOpenApi } from '../spec/openapi';
import { leerProto } from '../spec/proto';
import { matricesDeObligatoriedad, tablasDeReferencia } from '../spec/referencia';

// El visor con Scalar solo se pinta en escritorio; su componente (el iframe) va en su propio chunk.
const VisorScalar = lazy(() => import('../componentes/VisorScalar').then((m) => ({ default: m.VisorScalar })));

/**
 * Pantalla del contrato (HU 3.6 y 3.9, plan 00 §8.9): `/contratos/<id>` (última versión) y `/contratos/<id>/v/<versión>`.
 * Pestañas según el tipo: operaciones (vista propia con ejemplos), referencia interactiva con Scalar (OpenAPI, solo en
 * escritorio), matriz de obligatoriedad y tablas de referencia (si el spec las trae), versiones con diff, guías (HU 3.7),
 * canales (HU 3.4) y paquetes (F4). La pestaña y la operación van en la URL (`?pestana=`, `?op=`) para compartirlas.
 */
export default function Contrato() {
  const { id = '', version: versionRuta } = useParams();
  const contrato = useContrato(id);
  const versiones = useVersiones(id, true);

  if (contrato.isPending) return <div role="status" aria-label="Cargando el contrato" className="mx-auto h-60 max-w-6xl animate-pulse rounded-3xl bg-card" />;
  if (contrato.isError) {
    if (contrato.error instanceof ErrorApi && contrato.error.estado === 404) return <NoEncontrada />;
    return <Aviso>No pudimos cargar el contrato. Revisa tu conexión e inténtalo de nuevo.</Aviso>;
  }
  const version = versionRuta ?? contrato.data.ultimaVersion?.version;
  if (!version) return <Aviso>Este contrato todavía no tiene versiones publicadas.</Aviso>;
  if (versiones.data && !versiones.data.some((v) => v.version === version)) return <NoEncontrada />;
  return <Contenido contrato={contrato.data} version={version} versiones={versiones.data ?? []} />;
}

function Aviso({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="mx-auto max-w-6xl rounded-xl border border-border bg-card p-4 text-sm">
      {children}
    </p>
  );
}

type Documento =
  | { tipo: 'openapi'; raiz: Objeto }
  | { tipo: 'asyncapi'; raiz: Objeto }
  | { tipo: 'proto'; texto: string }
  | { tipo: 'ilegible' };

function Contenido({ contrato: c, version, versiones }: { contrato: ContratoResumen; version: string; versiones: VersionResumen[] }) {
  const spec = useSpec(c.id, version);
  const indice = useOperaciones(c.id, version);
  const escritorio = useEscritorio();
  const [parametros, setParametros] = useSearchParams();
  const [eligiendoVersion, setEligiendoVersion] = useState(false);
  const datos = versiones.find((v) => v.version === version) ?? null;

  const documento = useMemo<Documento | null>(() => {
    if (spec.data === undefined) return null;
    if (c.tipo === 'proto') return { tipo: 'proto', texto: spec.data };
    const raiz = leerSpec(spec.data);
    return raiz ? { tipo: c.tipo, raiz } : { tipo: 'ilegible' };
  }, [spec.data, c.tipo]);

  const derivados = useMemo(() => {
    if (documento?.tipo !== 'openapi') return null;
    return {
      operaciones: operacionesOpenApi(documento.raiz),
      matrices: matricesDeObligatoriedad(documento.raiz),
      tablas: tablasDeReferencia(documento.raiz),
      info: infoDe(documento.raiz),
    };
  }, [documento]);
  const asincrono = useMemo(() => (documento?.tipo === 'asyncapi' ? documentoAsync(documento.raiz) : null), [documento]);
  const proto = useMemo(() => (documento?.tipo === 'proto' ? leerProto(documento.texto) : null), [documento]);

  const pestanas: Pestana[] = [
    { id: 'documentacion', titulo: c.tipo === 'openapi' ? 'Operaciones' : 'Documentación' },
    ...(c.tipo === 'openapi' && escritorio ? [{ id: 'referencia', titulo: 'Referencia interactiva' }] : []),
    ...(derivados && derivados.matrices.length > 0 ? [{ id: 'obligatoriedad', titulo: 'Obligatoriedad' }] : []),
    ...(derivados && derivados.tablas.length > 0 ? [{ id: 'tablas', titulo: 'Tablas de referencia', marca: <Contador n={derivados.tablas.length} /> }] : []),
    { id: 'versiones', titulo: 'Versiones', marca: <Contador n={versiones.length} /> },
    { id: 'guias', titulo: 'Guías' },
    { id: 'canales', titulo: 'Canales' },
    { id: 'paquetes', titulo: 'Paquetes' },
  ];
  const activa = pestanas.find((p) => p.id === parametros.get('pestana'))?.id ?? 'documentacion';
  const cambiar = (cambios: Record<string, string | null>) =>
    setParametros(
      (actual) => {
        const siguiente = new URLSearchParams(actual);
        for (const [clave, valor] of Object.entries(cambios)) {
          if (valor) siguiente.set(clave, valor);
          else siguiente.delete(clave);
        }
        return siguiente;
      },
      { replace: true },
    );
  const nombreArchivo = `${c.codigo}-${version}`;
  const extension = datos?.formato === 'json' ? 'json' : datos?.formato === 'proto' ? 'proto' : 'yaml';

  return (
    <div className="mx-auto flex max-w-6xl min-w-0 flex-col gap-4">
      <nav aria-label="Ruta" className="flex min-w-0 flex-wrap items-center gap-1 text-sm text-atlas-texto-suave">
        <Link to="/contratos" className="inline-flex min-h-toque items-center gap-1 rounded-lg pr-2 font-medium text-atlas-tinta hover:underline">
          <ArrowLeft className="size-4" aria-hidden="true" />
          Contratos
        </Link>
        <span aria-hidden="true">›</span>
        <span className="min-w-0 truncate">{c.aplicativo.nombre ?? c.aplicativo.slug ?? 'Aplicativo fuera del catálogo'}</span>
      </nav>

      <header className="flex min-w-0 flex-col gap-3 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm text-white/85">
          {TIPOS[c.tipo]} · <span className="break-all">{c.codigo}</span> · {c.visibilidad === 'compartible' ? 'Compartible con clientes' : 'Interno'}
        </p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">{c.nombre}</h1>
        {(derivados?.info.descripcion ?? c.descripcion) && <p className="line-clamp-3 max-w-3xl text-sm text-white/85">{derivados?.info.descripcion ?? c.descripcion}</p>}
        <div className="flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center gap-2 rounded-xl bg-white/95 px-3 py-1.5 text-sm text-[#0b1220]">
            Versión <strong className="break-all">{version}</strong>
            {datos && <span className="text-xs">· {datos.esEstable ? 'estable' : 'prerelease'} · {formatearFecha(new Date(datos.publicadoEn))}</span>}
          </span>
          <Boton variante="secundario" onClick={() => setEligiendoVersion(true)}>
            <History className="size-4" aria-hidden="true" />
            Otra versión
          </Boton>
          {spec.data !== undefined && (
            <Boton variante="secundario" onClick={() => descargar(`${nombreArchivo}.${extension}`, spec.data!, 'text/plain;charset=utf-8')}>
              <Download className="size-4" aria-hidden="true" />
              Descargar spec
            </Boton>
          )}
        </div>
      </header>

      <Pestanas etiqueta="Secciones del contrato" pestanas={pestanas} activa={activa} alElegir={(p) => cambiar({ pestana: p === 'documentacion' ? null : p, op: null })}>
        {spec.isPending && <div role="status" aria-label="Cargando el spec" className="h-60 animate-pulse rounded-2xl bg-card" />}
        {spec.isError && <Aviso>No pudimos cargar el spec de esta versión.</Aviso>}
        {documento?.tipo === 'ilegible' && <Aviso>El spec de esta versión no se pudo leer en el navegador. Descárgalo para revisarlo.</Aviso>}

        {activa === 'documentacion' && derivados && (
          <Operaciones operaciones={derivados.operaciones} elegida={parametros.get('op')} alElegir={(op) => cambiar({ op })} />
        )}
        {activa === 'documentacion' && asincrono && <VistaAsyncApi documento={asincrono} />}
        {activa === 'documentacion' && proto && documento?.tipo === 'proto' && <VistaProto documento={proto} indice={indice.data ?? []} texto={documento.texto} />}

        {activa === 'referencia' && documento?.tipo === 'openapi' && spec.data !== undefined && (
          <Suspense fallback={<div role="status" aria-label="Cargando el visor" className="h-60 animate-pulse rounded-2xl bg-card" />}>
            <VisorScalar contenido={spec.data} servidores={documento.raiz.servers} titulo={c.nombre} />
          </Suspense>
        )}
        {activa === 'obligatoriedad' && derivados && (
          <div className="flex flex-col gap-6">
            {derivados.matrices.map((m) => (
              <MatrizObligatoriedad key={m.esquema} matriz={m} nombreArchivo={nombreArchivo} />
            ))}
          </div>
        )}
        {activa === 'tablas' && derivados && <TablasReferencia tablas={derivados.tablas} nombreArchivo={nombreArchivo} />}
        {activa === 'versiones' && <Versiones contratoId={c.id} tipo={c.tipo} versiones={versiones} actual={version} />}
        {activa === 'guias' && <PuntoGuias contratoId={c.id} version={version} tipo={c.tipo} />}
        {activa === 'canales' && (
          <Marcador titulo="Canales" icono={<Radio className="size-4 shrink-0" aria-hidden="true" />}>
            Qué versión corre en dev, qa y pdn, con sus desfases. Llega con la publicación de canales desde el CD (HU 3.4); mientras tanto, la última
            publicada es {c.ultimaVersion?.version ?? '—'} y la última estable {c.ultimaEstable?.version ?? '—'}.
          </Marcador>
        )}
        {activa === 'paquetes' && (
          <Marcador titulo="Paquetes que lo usan" icono={<Package className="size-4 shrink-0" aria-hidden="true" />}>
            Los paquetes para clientes que incluyen este contrato, con su versión fijada. Llegan con el portal de clientes (fase F4).
          </Marcador>
        )}
      </Pestanas>

      <Hoja abierta={eligiendoVersion} alCerrar={() => setEligiendoVersion(false)} titulo="Elegir versión">
        <ul className="flex flex-col gap-1 px-4 pt-2 pb-5">
          {versiones.map((v) => (
            <li key={v.id}>
              <Link
                to={`/contratos/${c.id}/v/${encodeURIComponent(v.version)}`}
                onClick={() => setEligiendoVersion(false)}
                aria-current={v.version === version ? 'page' : undefined}
                className="flex min-h-toque flex-wrap items-center gap-2 rounded-xl px-3 text-sm hover:bg-atlas-activo aria-[current=page]:bg-atlas-activo aria-[current=page]:font-medium"
              >
                <span className="break-all">{v.version}</span>
                <Etiqueta tono={v.esEstable ? 'exito' : 'info'}>{v.esEstable ? 'Estable' : 'Prerelease'}</Etiqueta>
                {v.tieneBreaking && <Etiqueta tono="peligro">Breaking</Etiqueta>}
                <span className="text-xs text-atlas-texto-suave">{formatearFecha(new Date(v.publicadoEn))}</span>
              </Link>
            </li>
          ))}
        </ul>
      </Hoja>
    </div>
  );
}

function Contador({ n }: { n: number }) {
  return <span className="rounded-full bg-atlas-activo px-1.5 text-xs">{n}</span>;
}

function Marcador({ titulo, icono, children }: { titulo: string; icono: ReactNode; children: ReactNode }) {
  return (
    <Tarjeta titulo={titulo}>
      <p className="flex items-start gap-2 text-sm text-atlas-texto-suave">
        {icono}
        <span>{children}</span>
      </p>
      <p className="mt-3 inline-flex items-center gap-2 text-sm">
        <Hourglass className="size-4 text-atlas-tinta" aria-hidden="true" />
        Próximamente
      </p>
    </Tarjeta>
  );
}
