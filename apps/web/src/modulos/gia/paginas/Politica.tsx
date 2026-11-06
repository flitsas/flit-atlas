import { lazy, Suspense, useState } from 'react';
import { useParams } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { Insignia } from '../../../app/ui/Tarjeta';
import { TEXTO_ESTADO, useEnvio, usePolitica, type PoliticaDetalle } from '../api';
import { Aviso, Cargando, claseBoton, claseBotonSuave, claseCampo, ErrorEnvio, Pagina } from '../componentes';

const Markdown = lazy(() => import('../Markdown'));

function Borrador({ detalle }: { detalle: PoliticaDetalle }) {
  const envio = useEnvio();
  const [titulo, setTitulo] = useState(detalle.politica.titulo);
  const [contenido, setContenido] = useState(detalle.contenido);
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        envio.mutate({ ruta: `/politicas/${detalle.politica.id}`, metodo: 'PUT', cuerpo: { titulo, contenido, fuenteGit: detalle.fuenteGit, requiereAceptacion: detalle.politica.requiereAceptacion } });
      }}
    >
      <label className="text-sm font-medium">
        Título
        <input required value={titulo} onChange={(e) => setTitulo(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Texto en Markdown
        <textarea required rows={14} value={contenido} onChange={(e) => setContenido(e.target.value)} className={`${claseCampo} py-2 font-mono text-sm`} />
      </label>
      <ErrorEnvio error={envio.error} porDefecto="No pudimos guardar el borrador." />
      <button type="submit" disabled={envio.isPending} className={`${claseBotonSuave} self-start`}>
        Guardar el borrador
      </button>
    </form>
  );
}

/** Una versión de una política (HU 8.3): leer, aceptar, redactar el borrador y publicar con el rol Comité. */
export default function Politica() {
  const { id = '' } = useParams();
  const { acceso } = useSesion();
  const politica = usePolitica(id);
  const envio = useEnvio();
  const gestiona = tienePermiso('atlas.gia.gestionar', acceso.permisos);

  if (politica.isPending) return <Pagina titulo="Política"><Cargando etiqueta="Cargando la política" alto="h-48" /></Pagina>;
  if (politica.isError) return <Pagina titulo="Política"><p role="alert">No pudimos cargar la política.</p></Pagina>;
  const { data } = politica;
  const p = data.politica;
  return (
    <Pagina titulo={p.titulo} descripcion={`${p.codigo} · versión ${p.version}`}>
      <p className="flex flex-wrap gap-1">
        <Insignia>{TEXTO_ESTADO[p.estado] ?? p.estado}</Insignia>
        {p.vigenteDesde && <Insignia>{`Vigente desde ${p.vigenteDesde}`}</Insignia>}
      </p>
      {p.pendienteDeRedaccion && (
        <Aviso tono="info">Esta política no está redactada ni aprobada y no tiene vigencia. Para publicarla hay que redactarla y quitar la marca de pendiente.</Aviso>
      )}
      <Suspense fallback={<Cargando etiqueta="Cargando el texto" />}>
        <Markdown texto={data.contenido} etiqueta="Texto de la política" />
      </Suspense>
      {p.estado === 'vigente' && p.requiereAceptacion && (
        p.aceptadaPorMi ? (
          <Aviso tono="exito">Ya aceptaste esta versión.</Aviso>
        ) : (
          <button type="button" disabled={envio.isPending} onClick={() => envio.mutate({ ruta: `/politicas/${p.id}/aceptacion` })} className={`${claseBoton} self-start`}>
            Leí y acepto
          </button>
        )
      )}
      {gestiona && p.estado === 'borrador' && (
        <>
          <Borrador key={data.contenido} detalle={data} />
          <button type="button" disabled={envio.isPending || p.pendienteDeRedaccion} onClick={() => envio.mutate({ ruta: `/politicas/${p.id}/publicacion` })} className={`${claseBoton} self-start`}>
            Publicar como Comité
          </button>
        </>
      )}
      <ErrorEnvio error={envio.error} porDefecto="No pudimos completar la acción." />
    </Pagina>
  );
}
