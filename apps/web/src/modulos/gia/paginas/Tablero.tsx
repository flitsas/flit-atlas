import { Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { TEXTO_ESTADO, TEXTO_RIESGO, useTablero, type Kpi } from '../api';
import { Cargando, Pagina } from '../componentes';

function Indicador({ kpi }: { kpi: Kpi }) {
  return (
    <li className="min-w-0 rounded-xl border border-border bg-card p-3">
      <p className="text-sm font-medium">{kpi.nombre}</p>
      {kpi.disponible ? (
        <p className="mt-1 text-xl font-semibold">{kpi.total === null ? kpi.valor : `${kpi.valor} de ${kpi.total}`}</p>
      ) : (
        <p className="mt-1 font-medium text-atlas-texto-suave">Sin datos</p>
      )}
      <p className="mt-1 text-xs text-atlas-texto-suave">Meta: {kpi.meta}</p>
      {kpi.nota && <p className="mt-1 text-xs text-atlas-texto-suave">{kpi.nota}</p>}
    </li>
  );
}

/** Tablero de gobierno de IA (HU 8.6, ATLAS-ADR-0055 §9). Lo que no se puede medir dice «Sin datos», nunca cero. */
export default function Tablero() {
  const tablero = useTablero();
  return (
    <Pagina titulo="Gobierno de IA" descripcion="Inventario, riesgo, políticas e incidentes. Este tablero no se envía a nadie.">
      {tablero.isPending && <Cargando etiqueta="Cargando el tablero" alto="h-48" />}
      {tablero.isError && <p role="alert">No pudimos cargar el tablero. Revisa tu conexión.</p>}
      {tablero.data && (
        <>
          <div className="grid gap-4 md:grid-cols-2">
            <Tarjeta titulo="Casos por estado">
              <ul className="flex flex-wrap gap-2">
                {tablero.data.casosPorEstado.map((c) => (
                  <li key={c.clave}>
                    <Insignia>{`${TEXTO_ESTADO[c.clave] ?? c.clave}: ${c.total}`}</Insignia>
                  </li>
                ))}
              </ul>
            </Tarjeta>
            <Tarjeta titulo="Casos por riesgo">
              <ul className="flex flex-wrap gap-2">
                {tablero.data.casosPorRiesgo.map((c) => (
                  <li key={c.clave}>
                    <Insignia>{`${TEXTO_RIESGO[c.clave] ?? c.clave}: ${c.total}`}</Insignia>
                  </li>
                ))}
              </ul>
              <p className="mt-2 text-xs text-atlas-texto-suave">Sin evaluación aprobada se usa el riesgo que estimó el plan.</p>
              <p className="mt-2 text-sm">Revisiones vencidas: {tablero.data.revisionesVencidas}</p>
            </Tarjeta>
          </div>
          <Tarjeta titulo="Indicadores">
            <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {tablero.data.kpis.map((k) => (
                <Indicador key={k.codigo} kpi={k} />
              ))}
            </ul>
          </Tarjeta>
        </>
      )}
    </Pagina>
  );
}
