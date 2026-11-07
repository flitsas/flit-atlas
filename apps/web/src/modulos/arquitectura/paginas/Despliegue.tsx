import { useState } from 'react';
import { Link } from 'react-router';

import { Insignia } from '../../../app/ui/Tarjeta';
import { formatearRelativo } from '../../../app/ui/formato';
import { type Diff, type Publicacion, TEXTO_CANAL, useDiffSnapshots, useHistorialSnapshots, useSnapshot, useSnapshotsVigentes } from '../api';

const ListaCambios = ({ titulo, filas }: { titulo: string; filas: Diff['altas'] }) =>
  filas.length === 0 ? null : (
    <div className="min-w-0">
      <h4 className="text-sm font-medium">
        {titulo} ({filas.length})
      </h4>
      <ul className="list-disc pl-5 text-sm">
        {filas.map((c) => (
          <li key={`${c.tipo}|${c.id}`} className="break-words">
            {c.id}
            {c.detalle ? ` · ${c.detalle}` : ''}
          </li>
        ))}
      </ul>
    </div>
  );

function Detalle({ publicacion }: { publicacion: Publicacion }) {
  const snapshot = useSnapshot(publicacion.id);
  const historial = useHistorialSnapshots(publicacion.canal, publicacion.cluster);
  const anterior = historial.data?.elementos.find((p) => p.publicadoEn < publicacion.publicadoEn) ?? null;
  const [comparar, setComparar] = useState(false);
  const diff = useDiffSnapshots(comparar ? (anterior?.id ?? null) : null, comparar ? publicacion.id : null);

  if (snapshot.isPending) return <div role="status" aria-label="Cargando el snapshot" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />;
  if (snapshot.isError) return <p role="alert">No pudimos cargar el snapshot.</p>;
  const { cluster, fueraDeK3s } = snapshot.data.snapshot;
  return (
    <div className="mt-3 flex min-w-0 flex-col gap-3">
      {cluster.namespaces.map((n) => (
        <div key={n.nombre} className="min-w-0">
          <h4 className="break-words text-sm font-medium">{n.nombre}</h4>
          {n.ingressHosts.length > 0 && <p className="break-words text-xs text-atlas-texto-suave">Hosts: {n.ingressHosts.join(', ')}</p>}
          <ul className="mt-1 grid gap-1 lg:grid-cols-2">
            {n.workloads.map((w) => (
              <li key={`${w.kind}/${w.nombre}`} className="min-w-0 rounded-xl border border-border p-2 text-sm">
                <p className="break-words font-medium">{w.nombre}</p>
                <p className="text-xs text-atlas-texto-suave">
                  {w.kind}
                  {w.replicas !== null ? ` · ${w.replicas} réplicas` : ''}
                </p>
                {w.imagenes.map((i) => (
                  <p key={`${i.imagen}@${i.digest}`} className="break-all text-xs">
                    {i.imagen}
                    {i.digest ? `@${i.digest.slice(0, 19)}…` : ''}
                  </p>
                ))}
              </li>
            ))}
          </ul>
        </div>
      ))}
      {fueraDeK3s && fueraDeK3s.length > 0 && (
        <div className="min-w-0">
          <h4 className="text-sm font-medium">Fuera de k3s</h4>
          <ul className="list-disc pl-5 text-sm">
            {fueraDeK3s.map((f) => (
              <li key={f.nombre} className="break-words">
                {f.nombre}
                {f.metodo ? ` (${f.metodo})` : ''}
                {f.verificadoEn ? ` · verificado ${f.verificadoEn}` : ''}
              </li>
            ))}
          </ul>
        </div>
      )}
      {anterior && !comparar && (
        <button type="button" onClick={() => setComparar(true)} className="min-h-toque self-start rounded-xl border border-border bg-card px-3 text-sm font-medium">
          Comparar con el snapshot anterior
        </button>
      )}
      {diff.data && (
        <section aria-label="Cambios frente al snapshot anterior" className="flex min-w-0 flex-col gap-2 rounded-xl bg-atlas-activo p-3">
          {diff.data.altas.length + diff.data.bajas.length + diff.data.cambios.length === 0 && <p className="text-sm">Sin cambios frente al snapshot anterior.</p>}
          <ListaCambios titulo="Agregados" filas={diff.data.altas} />
          <ListaCambios titulo="Quitados" filas={diff.data.bajas} />
          <ListaCambios titulo="Cambiados" filas={diff.data.cambios} />
        </section>
      )}
    </div>
  );
}

function TarjetaSnapshot({ publicacion }: { publicacion: Publicacion }) {
  const [abierta, setAbierta] = useState(false);
  return (
    <li className="min-w-0 rounded-xl border border-border bg-card p-3">
      <button type="button" aria-expanded={abierta} onClick={() => setAbierta((a) => !a)} className="flex min-h-toque w-full flex-wrap items-center gap-2 text-left">
        <span className="font-medium">{TEXTO_CANAL[publicacion.canal]}</span>
        <Insignia tono="neutral">{publicacion.cluster}</Insignia>
        <span className="text-xs text-atlas-texto-suave">
          gitops {publicacion.commitSha.slice(0, 7)} · {formatearRelativo(new Date(publicacion.publicadoEn))}
        </span>
      </button>
      {abierta && <Detalle publicacion={publicacion} />}
    </li>
  );
}

/** Lo desplegado por ambiente y clúster según flit-gitops (HU 6.4), también en una fecha pasada. Solo con `ver_despliegue`. */
export default function DespliegueArquitectura() {
  const [en, setEn] = useState<string | null>(null);
  const lista = useSnapshotsVigentes(en);
  return (
    <section aria-labelledby="titulo-despliegue" className="flex min-w-0 flex-col gap-4">
      <header className="min-w-0">
        <Link to="/arquitectura" className="text-sm underline">
          Arquitectura
        </Link>
        <h1 id="titulo-despliegue" className="text-2xl font-semibold">
          Despliegue por ambiente
        </h1>
        <p className="text-sm text-atlas-texto-suave">Lo que dice flit-gitops, por commit. Sin secretos, IP ni hosts internos.</p>
      </header>
      <label className="min-w-0 self-start text-sm font-medium">
        ¿Qué corría el…?
        <input
          type="datetime-local"
          value={en ? new Date(en).toISOString().slice(0, 16) : ''}
          onChange={(e) => setEn(e.target.value ? new Date(`${e.target.value}Z`).toISOString() : null)}
          className="mt-1 block min-h-toque max-w-full rounded-xl border border-border bg-card px-3"
        />
        <span className="block text-xs font-normal text-atlas-texto-suave">Hora UTC</span>
      </label>
      {lista.isPending && <div role="status" aria-label="Cargando los snapshots" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />}
      {lista.isError && <p role="alert">No pudimos cargar los snapshots. Revisa tu conexión.</p>}
      {lista.data?.elementos.length === 0 && <p className="text-sm text-atlas-texto-suave">Todavía no hay snapshots publicados para esa fecha.</p>}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul className="flex min-w-0 flex-col gap-2">
          {lista.data.elementos.map((p) => (
            <TarjetaSnapshot key={p.id} publicacion={p} />
          ))}
        </ul>
      )}
    </section>
  );
}
