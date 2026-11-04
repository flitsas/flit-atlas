namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>Turno que ya ocupa un puesto del equipo en un sábado de la ventana y que la generación no toca.</summary>
/// <param name="Fecha">Sábado del turno.</param>
/// <param name="PersonaId">Persona de turno.</param>
public sealed record TurnoFijo(DateOnly Fecha, Guid PersonaId);

/// <summary>Una persona no disponible entre dos fechas (ambas incluidas): ausencia o turno en otro equipo.</summary>
public sealed record RangoAusencia(Guid PersonaId, DateOnly Desde, DateOnly Hasta)
{
    public bool Cubre(DateOnly fecha) => fecha >= Desde && fecha <= Hasta;
}

/// <param name="Desde">Primer día de la ventana (se toma desde el primer sábado en adelante).</param>
/// <param name="HastaExcluida">Día siguiente al último de la ventana.</param>
/// <param name="Participantes">Personas activas de la rotación del equipo, en su orden.</param>
/// <param name="Fijos">Turnos vigentes del equipo en la ventana que no se regeneran (aplicados, confirmados, importados, manuales).</param>
/// <param name="Ausencias">Ausencias de las personas (y sus turnos en otros equipos).</param>
/// <param name="Festivos">Festivos que pueden caer en la ventana (los de Colombia y los adicionales).</param>
/// <param name="Regla">Qué hacer con un sábado festivo.</param>
/// <param name="UltimaPersonaAntes">Quién tuvo el último turno del equipo antes de la ventana: la rotación sigue desde la siguiente.</param>
/// <param name="PersonasMinimo">Mínimo de personas del equipo por sábado.</param>
/// <param name="PersonasMaximo">Máximo de personas del equipo por sábado: se asignan hasta este número.</param>
public sealed record EntradaGeneracion(
    DateOnly Desde,
    DateOnly HastaExcluida,
    IReadOnlyList<Guid> Participantes,
    IReadOnlyList<TurnoFijo> Fijos,
    IReadOnlyList<RangoAusencia> Ausencias,
    IReadOnlyDictionary<DateOnly, string> Festivos,
    ReglaFestivos Regla,
    Guid? UltimaPersonaAntes = null,
    int PersonasMinimo = 1,
    int PersonasMaximo = 1);

/// <summary>Qué pasa con un sábado de la ventana.</summary>
public enum DecisionSabado
{
    /// <summary>Se proponen turnos nuevos (<see cref="SabadoPropuesto.Nuevas"/>), además de los fijos que haya.</summary>
    Asignado,

    /// <summary>Los turnos fijos ya llenan el sábado; no se agrega a nadie.</summary>
    Fijo,

    /// <summary>Festivo con la regla «sin turno».</summary>
    Festivo,

    /// <summary>No hay suficientes personas disponibles para llegar al mínimo: se completa a mano.</summary>
    SinDisponibles,
}

/// <param name="Fecha">Sábado.</param>
/// <param name="Decision">Qué se hace con él.</param>
/// <param name="Nuevas">Personas con turno nuevo, en el orden en que se eligieron.</param>
/// <param name="Fijas">Personas que ya tienen un turno que no cambia.</param>
/// <param name="Festivo">Nombre del festivo, si lo es.</param>
/// <param name="AusenteEnFijo">Un turno fijo es de una persona ausente ese día (conflicto para revisar).</param>
/// <param name="Faltan">Personas que faltan para el mínimo del equipo (0 si se cumple).</param>
public sealed record SabadoPropuesto(
    DateOnly Fecha, DecisionSabado Decision, IReadOnlyList<Guid> Nuevas, IReadOnlyList<Guid> Fijas, string? Festivo,
    bool AusenteEnFijo = false, int Faltan = 0)
{
    /// <summary>Todas las personas de turno ese sábado (fijas y nuevas).</summary>
    public IEnumerable<Guid> Personas => Fijas.Concat(Nuevas);
}

/// <param name="Sabados">Cada sábado de la ventana con su decisión, en orden.</param>
/// <param name="TurnosPorPersona">Turnos de cada participante en la ventana (fijos + nuevos).</param>
public sealed record ResultadoGeneracion(IReadOnlyList<SabadoPropuesto> Sabados, IReadOnlyDictionary<Guid, int> TurnosPorPersona)
{
    /// <summary>Diferencia entre quien más y quien menos turnos tiene en la ventana.</summary>
    public int Desbalance => TurnosPorPersona.Count == 0 ? 0 : TurnosPorPersona.Values.Max() - TurnosPorPersona.Values.Min();
}

/// <summary>
/// Rotación equitativa de los sábados de un equipo (HU 2.8, prompt maestro §8.3). Es pura y determinista: con la misma
/// entrada da la misma propuesta, así la vista previa y la confirmación coinciden.
/// <para>
/// Recorre los sábados en orden. Uno festivo con la regla <see cref="ReglaFestivos.SinTurno"/> se salta. En los demás
/// respeta los turnos fijos (cuentan para su persona) y llena los puestos que falten hasta el máximo del equipo
/// eligiendo, entre los participantes sin ausencia ese día y sin turno fijo ese sábado, al que lleve <b>menos turnos en
/// la ventana</b> y, si empatan, al que sigue en el orden de la rotación después del último elegido. Sin ausencias ni
/// fijos eso es la rotación en orden (de uno en uno o de dos en dos) y nadie queda con más de un turno de diferencia;
/// con ausencias, quien vuelve se pone al día primero.
/// </para>
/// </summary>
public static class GeneradorRotacion
{
    public static ResultadoGeneracion Generar(EntradaGeneracion entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var minimo = Math.Max(1, entrada.PersonasMinimo);
        var maximo = Math.Max(minimo, entrada.PersonasMaximo);
        var orden = entrada.Participantes.Distinct().ToList();
        var posicion = orden.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var conteo = orden.ToDictionary(p => p, _ => 0);
        var fijos = entrada.Fijos.GroupBy(f => f.Fecha).ToDictionary(g => g.Key, g => g.Select(f => f.PersonaId).Distinct().ToList());
        var sabados = CalendarioBogota.Sabados(entrada.Desde, entrada.HastaExcluida).ToList();
        foreach (var persona in sabados.Where(fijos.ContainsKey).SelectMany(s => fijos[s]).Where(conteo.ContainsKey))
        {
            conteo[persona]++;
        }

        var puntero = entrada.UltimaPersonaAntes is { } ultima && posicion.TryGetValue(ultima, out var p0) ? p0 : -1;
        var resultado = new List<SabadoPropuesto>(sabados.Count);
        foreach (var sabado in sabados)
        {
            var festivo = entrada.Festivos.TryGetValue(sabado, out var nombre) ? nombre : null;
            var deTurno = fijos.GetValueOrDefault(sabado) ?? [];
            var ausenteEnFijo = deTurno.Any(p => Ausente(entrada, p, sabado));
            foreach (var pf in deTurno.Where(posicion.ContainsKey))
            {
                puntero = posicion[pf];
            }
            if (festivo is not null && entrada.Regla == ReglaFestivos.SinTurno && deTurno.Count == 0)
            {
                resultado.Add(new SabadoPropuesto(sabado, DecisionSabado.Festivo, [], [], festivo));
                continue;
            }
            if (deTurno.Count >= maximo || (festivo is not null && entrada.Regla == ReglaFestivos.SinTurno))
            {
                // Lleno, o festivo con turnos puestos a mano: se respetan y no se agrega a nadie.
                resultado.Add(new SabadoPropuesto(sabado, DecisionSabado.Fijo, [], deTurno, festivo, ausenteEnFijo));
                continue;
            }
            var nuevas = new List<Guid>();
            while (deTurno.Count + nuevas.Count < maximo)
            {
                var actual = puntero;
                var elegido = orden
                    .Where(p => !deTurno.Contains(p) && !nuevas.Contains(p) && !Ausente(entrada, p, sabado))
                    .Select(p => (Persona: p, Turnos: conteo[p], Distancia: Distancia(actual, posicion[p], orden.Count)))
                    .OrderBy(c => c.Turnos).ThenBy(c => c.Distancia)
                    .Select(c => (Guid?)c.Persona)
                    .FirstOrDefault();
                if (elegido is not { } persona)
                {
                    break;
                }
                nuevas.Add(persona);
                conteo[persona]++;
                puntero = posicion[persona];
            }
            var faltan = Math.Max(0, minimo - deTurno.Count - nuevas.Count);
            var decision = faltan > 0 ? DecisionSabado.SinDisponibles : nuevas.Count > 0 ? DecisionSabado.Asignado : DecisionSabado.Fijo;
            resultado.Add(new SabadoPropuesto(sabado, decision, nuevas, deTurno, festivo, ausenteEnFijo, faltan));
        }
        return new ResultadoGeneracion(resultado, conteo);
    }

    private static bool Ausente(EntradaGeneracion entrada, Guid persona, DateOnly fecha) =>
        entrada.Ausencias.Any(a => a.PersonaId == persona && a.Cubre(fecha));

    /// <summary>Cuántos puestos después del último elegido está la posición (0 = el siguiente; el mismo queda de último).</summary>
    private static int Distancia(int puntero, int posicion, int total) =>
        puntero < 0 ? posicion : ((posicion - puntero - 1 + total) % total);
}
