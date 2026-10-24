import { useId, useState } from 'react';

import { Boton, cn } from '../../../app/ui/Tarjeta';
import { type CamposGuia, TEXTO_VISIBILIDAD, type Visibilidad } from './api';
import { type Aviso, claseCampo, MostrarAviso, VistaMarkdown } from './componentes';

const VISIBILIDADES: Visibilidad[] = ['interna', 'compartible'];

/**
 * Editor de una guía (HU 3.7): un `<textarea>` de Markdown con vista previa sanitizada. En el móvil, «Escribir» y «Vista
 * previa» se alternan; desde `lg`, van lado a lado. Cada guardado es una revisión nueva con su nota. Va en su propio chunk.
 */
export default function Editor({
  inicial,
  titulo,
  guardando,
  aviso,
  alGuardar,
  alCancelar,
}: {
  inicial: CamposGuia;
  titulo: string;
  guardando: boolean;
  aviso: Aviso;
  alGuardar: (campos: CamposGuia) => void;
  alCancelar: () => void;
}) {
  const [campos, setCampos] = useState(inicial);
  const [pestana, setPestana] = useState<'escribir' | 'vista'>('escribir');
  const id = useId();
  const cambiar = <K extends keyof CamposGuia>(clave: K, valor: CamposGuia[K]) => setCampos((c) => ({ ...c, [clave]: valor }));
  const completo = campos.titulo.trim() && campos.contenido.trim() && campos.nota.trim();

  return (
    <form
      aria-label={titulo}
      className="flex min-w-0 flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (completo) alGuardar(campos);
      }}
    >
      <label className="text-sm font-medium">
        Título
        <input required maxLength={200} value={campos.titulo} onChange={(e) => cambiar('titulo', e.target.value)} className={claseCampo} />
      </label>

      <div className="flex gap-2 lg:hidden" role="group" aria-label="Modo del editor">
        {(['escribir', 'vista'] as const).map((p) => (
          <Boton key={p} variante={pestana === p ? 'primario' : 'secundario'} aria-pressed={pestana === p} onClick={() => setPestana(p)}>
            {p === 'escribir' ? 'Escribir' : 'Vista previa'}
          </Boton>
        ))}
      </div>
      <div className="grid min-w-0 gap-3 lg:grid-cols-2">
        <label className={cn('flex min-w-0 flex-col text-sm font-medium', pestana !== 'escribir' && 'hidden lg:flex')}>
          Cuerpo en Markdown
          <textarea
            required
            maxLength={200_000}
            rows={16}
            spellCheck
            value={campos.contenido}
            onChange={(e) => cambiar('contenido', e.target.value)}
            aria-describedby={`${id}-ayuda`}
            className={`${claseCampo} min-h-72 py-2 font-mono text-sm`}
          />
          <span id={`${id}-ayuda`} className="mt-1 text-xs font-normal text-atlas-texto-suave">
            Markdown con tablas y bloques de código. El HTML no se interpreta. Usa variables como $TOKEN, nunca credenciales reales.
          </span>
        </label>
        <section
          aria-label="Vista previa"
          className={cn('min-w-0 rounded-xl border border-border p-3', pestana !== 'vista' && 'hidden lg:block')}
        >
          <p className="mb-2 text-xs font-medium text-atlas-texto-suave">Vista previa</p>
          {campos.contenido.trim() ? (
            <VistaMarkdown texto={campos.contenido} etiqueta="Vista previa de la guía" />
          ) : (
            <p className="text-sm text-atlas-texto-suave">Escribe el cuerpo para ver cómo queda.</p>
          )}
        </section>
      </div>

      <fieldset className="min-w-0">
        <legend className="mb-1 text-sm font-medium">Visibilidad</legend>
        <div className="flex flex-wrap gap-2">
          {VISIBILIDADES.map((v) => (
            <label
              key={v}
              className={cn(
                'inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm',
                campos.visibilidad === v ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
              )}
            >
              <input
                type="radio"
                name={`${id}-visibilidad`}
                value={v}
                checked={campos.visibilidad === v}
                onChange={() => cambiar('visibilidad', v)}
                className="size-4 accent-[var(--flit-brand-ink)]"
              />
              {TEXTO_VISIBILIDAD[v]}
            </label>
          ))}
        </div>
        {campos.visibilidad === 'compartible' && (
          <p className="mt-1 text-xs text-atlas-texto-suave">
            Para publicarla la aprueba el responsable principal o secundario del aplicativo. Podrá ir en paquetes para clientes.
          </p>
        )}
      </fieldset>

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="text-sm font-medium">
          Aplica desde la versión
          <input
            inputMode="text"
            placeholder="Vacío: desde la primera"
            maxLength={100}
            value={campos.aplicaDesde}
            onChange={(e) => cambiar('aplicaDesde', e.target.value)}
            className={claseCampo}
          />
        </label>
        <label className="text-sm font-medium">
          Hasta la versión
          <input
            placeholder="Vacío: hasta la última"
            maxLength={100}
            value={campos.aplicaHasta}
            onChange={(e) => cambiar('aplicaHasta', e.target.value)}
            className={claseCampo}
          />
        </label>
      </div>
      <label className="text-sm font-medium">
        Operación (opcional)
        <input
          placeholder="Por ejemplo GET /tramites/{id}; vacío si es de todo el contrato"
          maxLength={600}
          value={campos.operacion}
          onChange={(e) => cambiar('operacion', e.target.value)}
          className={claseCampo}
        />
      </label>
      <label className="text-sm font-medium">
        Nota del cambio
        <textarea
          required
          maxLength={500}
          rows={2}
          value={campos.nota}
          onChange={(e) => cambiar('nota', e.target.value)}
          className={`${claseCampo} py-2`}
        />
      </label>

      <MostrarAviso aviso={aviso} />
      <div className="flex flex-wrap gap-2">
        <Boton type="submit" disabled={!completo || guardando}>
          {guardando ? 'Guardando…' : 'Guardar revisión'}
        </Boton>
        <Boton variante="secundario" onClick={alCancelar}>
          Cancelar
        </Boton>
      </div>
    </form>
  );
}
