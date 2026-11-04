import { useEffect, useId, useState } from 'react';

import type { IdLenguaje } from '../spec/ejemplos';
import type { Campo, OperacionOpenApi, Respuesta } from '../spec/openapi';
import { Codigo, type Columna, Etiqueta, Metodo, Pestanas, TablaResponsiva } from './ui';

/**
 * Ficha de una operación OpenAPI (HU 3.6): cabeceras, parámetros, cuerpo, ejemplos por lenguaje con «Copiar»,
 * respuesta de ejemplo y tabla de errores. En el móvil va dentro de una hoja inferior; en escritorio, al lado de la lista.
 */
export function DetalleOperacion({ operacion: op }: { operacion: OperacionOpenApi }) {
  const idTitulo = useId();
  const cabeceras: Campo[] = [
    ...(op.requiereToken
      ? [{ nombre: 'Authorization', ubicacion: 'header', tipo: 'string', obligatorio: true, descripcion: 'Bearer $TOKEN', restricciones: [], profundidad: 0 } satisfies Campo]
      : []),
    ...op.llaves.map((llave) => ({ nombre: llave, ubicacion: 'header', tipo: 'string', obligatorio: true, descripcion: 'Llave de la API ($API_KEY)', restricciones: [], profundidad: 0 }) satisfies Campo),
    ...(op.cuerpo && !/multipart/i.test(op.cuerpo.tipoContenido)
      ? [{ nombre: 'Content-Type', ubicacion: 'header', tipo: 'string', obligatorio: true, descripcion: op.cuerpo.tipoContenido, restricciones: [], profundidad: 0 } satisfies Campo]
      : []),
    ...op.cabeceras,
  ];

  return (
    <article aria-labelledby={idTitulo} className="flex min-w-0 flex-col gap-5">
      <header className="flex flex-col gap-2">
        <h3 id={idTitulo} className="flex min-w-0 flex-wrap items-center gap-2 text-base font-semibold">
          <Metodo metodo={op.metodo} />
          <code className="min-w-0 font-mono text-sm break-all">{op.esWebhook ? `webhook ${op.ruta}` : op.ruta}</code>
        </h3>
        {op.resumen && <p className="text-sm font-medium">{op.resumen}</p>}
        <div className="flex flex-wrap gap-1.5">
          {op.esWebhook && <Etiqueta tono="info">Webhook (FLIT llama a tu URL)</Etiqueta>}
          {op.publica && <Etiqueta tono="exito">Compartible</Etiqueta>}
          {op.interna && <Etiqueta>Interna</Etiqueta>}
          {op.deprecada && <Etiqueta tono="alerta">Deprecada</Etiqueta>}
          {op.operationId && <Etiqueta>operationId: {op.operationId}</Etiqueta>}
        </div>
        {op.descripcion && <p className="text-sm whitespace-pre-line text-atlas-texto-suave">{op.descripcion}</p>}
      </header>

      <Seccion titulo="Cabeceras">
        <TablaCampos titulo="Cabeceras" campos={cabeceras} vacio="No pide cabeceras." />
      </Seccion>
      <Seccion titulo="Parámetros">
        <TablaCampos titulo="Parámetros" campos={op.parametros} vacio="No tiene parámetros de ruta ni de consulta." conUbicacion />
      </Seccion>
      {op.cuerpo && (
        <Seccion titulo={`Cuerpo · ${op.cuerpo.tipoContenido}`}>
          <TablaCampos titulo="Campos del cuerpo" campos={op.cuerpo.campos} vacio="El esquema del cuerpo no describe campos." />
        </Seccion>
      )}
      <Seccion titulo="Ejemplos">
        <Ejemplos operacion={op} />
      </Seccion>
      {op.respuestas.length > 0 && (
        <Seccion titulo="Respuestas">
          <div className="flex flex-col gap-3">
            {op.respuestas.map((r) => (
              <RespuestaEjemplo key={r.codigo} respuesta={r} />
            ))}
          </div>
        </Seccion>
      )}
      <Seccion titulo="Errores">
        <TablaResponsiva<Respuesta>
          titulo="Errores"
          filas={op.errores}
          clave={(r) => r.codigo}
          vacio="El contrato no documenta errores para esta operación."
          columnas={[
            { id: 'codigo', titulo: 'Código', principal: true, className: 'w-24', celda: (r) => <code className="font-mono">{r.codigo}</code> },
            { id: 'nombre', titulo: 'Nombre', className: 'w-48', celda: (r) => r.nombre },
            { id: 'descripcion', titulo: 'Descripción', celda: (r) => r.descripcion ?? '—' },
          ]}
        />
      </Seccion>
    </article>
  );
}

function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="flex min-w-0 flex-col gap-2">
      <h4 className="text-sm font-semibold">{titulo}</h4>
      {children}
    </section>
  );
}

const UBICACIONES: Record<Campo['ubicacion'], string> = { path: 'ruta', query: 'consulta', header: 'cabecera', cookie: 'cookie', cuerpo: 'cuerpo' };

export function TablaCampos({ titulo, campos, vacio, conUbicacion = false }: { titulo: string; campos: Campo[]; vacio: string; conUbicacion?: boolean }) {
  const columnas: Columna<Campo>[] = [
    {
      id: 'nombre',
      titulo: 'Nombre',
      principal: true,
      className: 'min-w-32',
      celda: (c) => (
        <code className="font-mono text-xs break-all">
          {c.nombre}
          {c.obligatorio && (
            <span className="text-[var(--badge-danger-fg)]" aria-hidden="true">
              {' '}
              *
            </span>
          )}
        </code>
      ),
    },
    ...(conUbicacion ? [{ id: 'en', titulo: 'En', className: 'w-24', celda: (c: Campo) => UBICACIONES[c.ubicacion] }] : []),
    { id: 'tipo', titulo: 'Tipo', className: 'min-w-20', celda: (c) => <span className="font-mono text-xs">{c.tipo}</span> },
    { id: 'obligatorio', titulo: 'Obligatorio', celda: (c) => (c.obligatorio ? 'Sí' : 'No') },
    {
      id: 'descripcion',
      titulo: 'Descripción',
      celda: (c) => (
        <span className="flex flex-col gap-1">
          <span>{c.descripcion ?? '—'}</span>
          {c.restricciones.length > 0 && <span className="text-xs text-atlas-texto-suave">{c.restricciones.join(' · ')}</span>}
          {c.valores && c.valores.length > 0 && (
            <span className="text-xs text-atlas-texto-suave">
              Valores:{' '}
              {c.valores
                .slice(0, 12)
                .map((v) => (v.descripcion && v.descripcion !== v.codigo ? `${v.codigo} (${v.descripcion})` : v.codigo))
                .join(', ')}
              {c.valores.length > 12 && ` y ${c.valores.length - 12} más`}
            </span>
          )}
        </span>
      ),
    },
  ];
  return <TablaResponsiva titulo={titulo} columnas={columnas} filas={campos} clave={(c) => `${c.ubicacion}:${c.nombre}`} vacio={vacio} />;
}

function RespuestaEjemplo({ respuesta: r }: { respuesta: Respuesta }) {
  const titulo = `${r.codigo} · ${r.nombre}${r.tipoContenido ? ` · ${r.tipoContenido}` : ''}`;
  if (r.ejemplo === undefined) {
    return (
      <p className="text-sm">
        <code className="font-mono">{r.codigo}</code> {r.descripcion ?? r.nombre}
      </p>
    );
  }
  return (
    <div className="flex min-w-0 flex-col gap-1">
      {r.descripcion && <p className="text-sm text-atlas-texto-suave">{r.descripcion}</p>}
      <Codigo titulo={titulo} codigo={typeof r.ejemplo === 'string' ? r.ejemplo : JSON.stringify(r.ejemplo, null, 2)} />
    </div>
  );
}

type Ejemplos = Record<IdLenguaje, string>;
type ModuloEjemplos = typeof import('../spec/ejemplos');

// httpsnippet va en su propio chunk: solo se descarga al abrir una operación.
let cargaEjemplos: Promise<ModuloEjemplos> | null = null;
const cargarEjemplos = () => (cargaEjemplos ??= import('../spec/ejemplos'));

function Ejemplos({ operacion }: { operacion: OperacionOpenApi }) {
  const [modulo, setModulo] = useState<ModuloEjemplos | null>(null);
  const [lenguaje, setLenguaje] = useState<IdLenguaje>('curl');
  const [ejemplos, setEjemplos] = useState<Ejemplos | null>(null);
  useEffect(() => {
    let vigente = true;
    void cargarEjemplos().then((m) => {
      if (!vigente) return;
      setModulo(m);
      setEjemplos(m.ejemplosDe(operacion));
    });
    return () => {
      vigente = false;
    };
  }, [operacion]);

  if (!modulo || !ejemplos) return <div role="status" aria-label="Generando los ejemplos" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />;
  const elegido = modulo.LENGUAJES.find((l) => l.id === lenguaje)!;
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <Pestanas
        etiqueta="Lenguaje del ejemplo"
        compactas
        pestanas={modulo.LENGUAJES.map((l) => ({ id: l.id, titulo: l.etiqueta }))}
        activa={lenguaje}
        alElegir={(id) => setLenguaje(id as IdLenguaje)}
      >
        <Codigo titulo={elegido.etiqueta} codigo={ejemplos[lenguaje]} />
      </Pestanas>
      <p className="text-xs text-atlas-texto-suave">
        Cambia <code>{'{{baseUrl}}'}</code> por la URL del ambiente y <code>$TOKEN</code> por tu token; los valores del cuerpo son de ejemplo.
      </p>
    </div>
  );
}
