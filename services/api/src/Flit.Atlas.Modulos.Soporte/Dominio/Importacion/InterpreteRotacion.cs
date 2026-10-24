using System.Globalization;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Importacion;

/// <summary>Cómo quedó resuelto un nombre del Excel.</summary>
/// <param name="Clave">El nombre normalizado (sin tildes ni mayúsculas): agrupa las filas con el mismo nombre.</param>
/// <param name="ElegidaAlImportar">La eligió quien importa entre las candidatas de un nombre ambiguo.</param>
/// <param name="CrearInactiva">No existe en Atlas y quien importa confirmó crearla como persona inactiva.</param>
public sealed record NombreResuelto(
    string Texto, string Clave, TipoCoincidencia Tipo, PersonaConocida? Persona, IReadOnlyList<PersonaConocida> Candidatas,
    bool ElegidaAlImportar, bool CrearInactiva)
{
    /// <summary>Hay a quién asignarle el turno: una persona de Atlas o una nueva confirmada.</summary>
    public bool Asignable => Persona is not null || CrearInactiva;
}

/// <summary>Compensación que se lee de la columna «Compensado».</summary>
/// <param name="ParaReemplazo">Es para quien hizo el reemplazo (si no, para el responsable de la fila).</param>
/// <param name="Fecha">Día compensado; vacío si el Excel no lo dice.</param>
public sealed record CompensacionLeida(bool ParaReemplazo, DateOnly? Fecha, EstadoCompensacion Estado);

/// <summary>Lo que se entendió de una fila del Excel, con el detalle para el informe.</summary>
public sealed class FilaInterpretada
{
    public required int Numero { get; init; }

    public DateOnly? Fecha { get; set; }

    /// <summary>El valor original de la celda de la fecha, para el informe.</summary>
    public string? TextoFecha { get; init; }

    public NombreResuelto? Responsable { get; set; }

    /// <summary>Estado del turno que queda vigente: el del responsable o, si hubo reemplazo, el de quien lo tomó.</summary>
    public EstadoTurno Estado { get; set; } = EstadoTurno.Programado;

    /// <summary>Quien tomó el turno según la columna «Compensado» (texto con «reemplazo»).</summary>
    public NombreResuelto? Reemplazo { get; set; }

    public CompensacionLeida? Compensacion { get; set; }

    /// <summary>Observación del turno: la de la columna y lo que no se pudo interpretar.</summary>
    public string? Observacion { get; set; }

    public bool RequiereRevision { get; set; }

    public List<string> Interpretaciones { get; } = [];

    public List<string> Ambiguedades { get; } = [];

    /// <summary>Por qué la fila no se importa; vacío si se importa.</summary>
    public string? Omitida { get; set; }
}

/// <summary>Persona del Excel que no existe en Atlas: la vista previa propone crearla inactiva y quien importa confirma.</summary>
public sealed record PersonaNuevaPropuesta(string Clave, string Nombre, int Filas, DateOnly? UltimaFecha, bool Confirmada);

/// <summary>Nombre del Excel que encaja con varias personas: quien importa elige una.</summary>
public sealed record NombreAmbiguo(string Clave, string Texto, IReadOnlyList<PersonaConocida> Candidatas, Guid? Elegida);

public sealed record PlanRotacion(
    IReadOnlyList<FilaInterpretada> Filas, IReadOnlyList<PersonaNuevaPropuesta> PersonasNuevas, IReadOnlyList<NombreAmbiguo> NombresAmbiguos);

/// <summary>
/// Interpreta las filas del Excel de la rotación (HU 2.7, prompt maestro §8.3). Puro y determinista: sin base ni reloj.
/// <list type="bullet">
/// <item>Fechas en texto (<c>2026-06-13</c>, <c>13/06/2026</c>) o seriales de Excel; deben ser sábados.</item>
/// <item>Nombres sin tildes ni mayúsculas; un nombre parcial que encaja con una sola persona es esa persona; si encaja con
/// varias, queda para que quien importa elija; si no existe, se propone crearla inactiva.</item>
/// <item>«Compensado»: una fecha es una compensación del responsable; un texto con «reemplazo» y un nombre claro es un
/// turno reemplazado por esa persona, y con «compensa» también una compensación para ella; lo demás va a la observación
/// marcado para revisión.</item>
/// </list>
/// </summary>
public static class InterpreteRotacion
{
    /// <summary>Seriales de Excel aceptados: de 2000-01-01 a 2100-12-31.</summary>
    private const double SerialMinimo = 36526;
    private const double SerialMaximo = 73415;

    private static readonly string[] FormatosTexto = ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"];

    private static readonly string[] FormatosFechaHora = ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff"];

    /// <summary>Palabras que no son parte de un nombre en un texto como «X reemplazo y compensa».</summary>
    private static readonly HashSet<string> Relleno = new(StringComparer.Ordinal)
    {
        "y", "e", "por", "de", "del", "la", "el", "lo", "le", "con", "a", "al", "su", "hizo", "hace", "turno", "en", "quien",
    };

    public static PlanRotacion Interpretar(
        IReadOnlyList<FilaExcel> filas, IReadOnlyList<PersonaConocida> personas, DateOnly hoy,
        IReadOnlySet<string>? crearInactivas = null, IReadOnlyDictionary<string, Guid>? elegidas = null)
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(personas);
        crearInactivas ??= new HashSet<string>(StringComparer.Ordinal);
        elegidas ??= new Dictionary<string, Guid>(StringComparer.Ordinal);

        var resultado = new List<FilaInterpretada>();
        var ambiguos = new Dictionary<string, NombreAmbiguo>(StringComparer.Ordinal);
        var nuevas = new Dictionary<string, (string Nombre, int Filas, DateOnly? Ultima)>(StringComparer.Ordinal);
        var vistas = new HashSet<(DateOnly, string)>();

        NombreResuelto Resolver(string texto, bool puedeCrear)
        {
            var clave = NombresPersona.Normalizar(texto);
            var coincidencia = NombresPersona.Buscar(texto, personas);
            if (coincidencia.Tipo == TipoCoincidencia.Ambigua)
            {
                var elegida = elegidas.TryGetValue(clave, out var id) ? coincidencia.Candidatas.FirstOrDefault(c => c.Id == id) : null;
                ambiguos.TryAdd(clave, new NombreAmbiguo(clave, texto.Trim(), coincidencia.Candidatas, elegida?.Id));
                return new NombreResuelto(texto.Trim(), clave, coincidencia.Tipo, elegida, coincidencia.Candidatas, elegida is not null, false);
            }
            var crear = coincidencia.Tipo == TipoCoincidencia.Ninguna && puedeCrear && crearInactivas.Contains(clave);
            return new NombreResuelto(texto.Trim(), clave, coincidencia.Tipo, coincidencia.Persona, [], false, crear);
        }

        foreach (var excel in filas)
        {
            var fila = new FilaInterpretada { Numero = excel.Numero, TextoFecha = excel.Fecha.Original };
            resultado.Add(fila);

            // Fecha.
            var (fecha, notaFecha, errorFecha) = LeerFecha(excel.Fecha);
            if (fecha is null)
            {
                fila.Omitida = errorFecha;
                continue;
            }
            fila.Fecha = fecha;
            if (notaFecha is not null)
            {
                fila.Interpretaciones.Add(notaFecha);
            }
            if (fecha.Value.DayOfWeek != DayOfWeek.Saturday)
            {
                fila.Omitida = $"El {Dia(fecha.Value)} no es sábado.";
                continue;
            }

            // Responsable.
            var textoResponsable = excel.Responsable.Original;
            if (string.IsNullOrWhiteSpace(textoResponsable))
            {
                fila.Omitida = "La fila no tiene responsable.";
                continue;
            }
            var responsable = Resolver(textoResponsable, puedeCrear: true);
            fila.Responsable = responsable;
            switch (responsable.Tipo)
            {
                case TipoCoincidencia.Exacta when !string.Equals(responsable.Persona!.Nombre, responsable.Texto, StringComparison.Ordinal):
                    fila.Interpretaciones.Add($"«{responsable.Texto}» es {responsable.Persona.Nombre} (sin tildes ni mayúsculas).");
                    break;
                case TipoCoincidencia.Parcial:
                    fila.Interpretaciones.Add($"«{responsable.Texto}» es {responsable.Persona!.Nombre} (nombre parcial que encaja con una sola persona).");
                    break;
                case TipoCoincidencia.Ambigua when responsable.Persona is not null:
                    fila.Interpretaciones.Add($"«{responsable.Texto}» es {responsable.Persona.Nombre}: la eligió quien importa entre {responsable.Candidatas.Count} personas.");
                    break;
                case TipoCoincidencia.Ambigua:
                    fila.Ambiguedades.Add($"«{responsable.Texto}» encaja con {responsable.Candidatas.Count} personas: elige cuál es.");
                    fila.Omitida = "El responsable es ambiguo.";
                    break;
                case TipoCoincidencia.Ninguna:
                    var previa = nuevas.GetValueOrDefault(responsable.Clave);
                    nuevas[responsable.Clave] = (previa.Nombre ?? responsable.Texto, previa.Filas + 1,
                        previa.Ultima is { } u && u > fecha.Value ? u : fecha.Value);
                    if (responsable.CrearInactiva)
                    {
                        fila.Interpretaciones.Add($"«{responsable.Texto}» no existe en Atlas: se crea como persona inactiva y no entra en la generación.");
                    }
                    else
                    {
                        fila.Omitida = $"«{responsable.Texto}» no existe en Atlas: confirma crearla como persona inactiva para importar la fila.";
                    }
                    break;
            }
            if (fila.Omitida is not null)
            {
                continue;
            }
            if (!vistas.Add((fecha.Value, responsable.Clave)))
            {
                fila.Omitida = $"Fila repetida: {responsable.Texto} ya tiene una fila el {Dia(fecha.Value)}.";
                continue;
            }

            // Estado.
            var textoEstado = excel.Estado.Original;
            var estado = LeerEstado(textoEstado);
            if (estado is null)
            {
                fila.Estado = EstadoTurno.Programado;
                fila.Ambiguedades.Add($"El estado «{textoEstado}» no se reconoce: queda programado.");
                AgregarObservacion(fila, $"Estado en el Excel: {textoEstado}");
            }
            else
            {
                fila.Estado = estado.Value;
                if (string.IsNullOrWhiteSpace(textoEstado) && fecha.Value < hoy)
                {
                    fila.Ambiguedades.Add("La fecha ya pasó y la fila no tiene estado: queda programado para revisar si se aplicó.");
                }
            }
            if (fila.Estado is EstadoTurno.Reemplazado)
            {
                // «Reemplazado» sin saber por quién: queda programado y para revisión.
                fila.Estado = EstadoTurno.Programado;
                fila.Ambiguedades.Add("El estado dice reemplazado pero no quién lo tomó: queda programado.");
            }

            // Compensado.
            InterpretarCompensado(fila, excel.Compensado, responsable, hoy, Resolver);

            // Observación.
            if (excel.Observacion.Original is { } observacion)
            {
                AgregarObservacion(fila, observacion);
            }
            if (fila.Observacion is { Length: > Datos.SoporteDbContext.LargoObservacion })
            {
                fila.Observacion = fila.Observacion[..(Datos.SoporteDbContext.LargoObservacion - 1)] + "…";
                fila.Ambiguedades.Add($"La observación pasa de {Datos.SoporteDbContext.LargoObservacion} caracteres: se recortó.");
            }
            fila.RequiereRevision = fila.Ambiguedades.Count > 0;
        }

        var personasNuevas = nuevas.Select(n => new PersonaNuevaPropuesta(n.Key, n.Value.Nombre, n.Value.Filas, n.Value.Ultima, crearInactivas.Contains(n.Key)))
            .OrderBy(p => p.Clave, StringComparer.Ordinal).ToList();
        return new PlanRotacion(resultado, personasNuevas, [.. ambiguos.Values.OrderBy(a => a.Clave, StringComparer.Ordinal)]);
    }

    private static void InterpretarCompensado(
        FilaInterpretada fila, CeldaExcel celda, NombreResuelto responsable, DateOnly hoy, Func<string, bool, NombreResuelto> resolver)
    {
        if (celda.EstaVacia)
        {
            return;
        }
        var original = celda.Original!;
        var (fecha, _, _) = LeerFecha(celda);
        if (fecha is not null)
        {
            var estado = fecha.Value <= hoy ? EstadoCompensacion.Tomada : EstadoCompensacion.Pendiente;
            fila.Compensacion = new CompensacionLeida(false, fecha, estado);
            fila.Interpretaciones.Add($"Compensado con fecha {Dia(fecha.Value)}: compensación de {NombreDe(responsable)} " +
                (estado == EstadoCompensacion.Tomada ? "ya tomada (la fecha pasó)." : "pendiente."));
            return;
        }
        if (celda.Texto is null)
        {
            Ambiguo(fila, original, "es un número que no es una fecha");
            return;
        }
        var palabras = NombresPersona.Palabras(celda.Texto);
        var reemplazo = palabras.Any(p => p.StartsWith("reemplaz", StringComparison.Ordinal));
        var compensa = palabras.Any(p => p.StartsWith("compens", StringComparison.Ordinal));
        var nombre = string.Join(' ', palabras.Where(p =>
            !p.StartsWith("reemplaz", StringComparison.Ordinal) && !p.StartsWith("compens", StringComparison.Ordinal) && !Relleno.Contains(p)));
        if (!reemplazo && !compensa)
        {
            Ambiguo(fila, original, "no es una fecha ni dice «reemplazo»");
            return;
        }
        if (!reemplazo)
        {
            if (nombre.Length > 0)
            {
                Ambiguo(fila, original, "dice «compensa» con un nombre pero no «reemplazo»");
                return;
            }
            fila.Compensacion = new CompensacionLeida(false, null, EstadoCompensacion.Pendiente);
            fila.Interpretaciones.Add($"Compensado «{original}»: compensación pendiente de {NombreDe(responsable)}, sin fecha.");
            return;
        }
        if (nombre.Length == 0)
        {
            Ambiguo(fila, original, "dice «reemplazo» pero no quién lo hizo");
            return;
        }
        var quien = resolver(nombre, false);
        if (quien.Persona is null)
        {
            Ambiguo(fila, original, quien.Tipo == TipoCoincidencia.Ambigua
                ? $"«{nombre}» encaja con {quien.Candidatas.Count} personas"
                : $"«{nombre}» no es una persona de Atlas");
            return;
        }
        if (responsable.Persona?.Id == quien.Persona.Id)
        {
            Ambiguo(fila, original, "el reemplazo es el mismo responsable");
            return;
        }
        fila.Reemplazo = quien;
        var texto = $"Compensado «{original}»: el turno de {NombreDe(responsable)} quedó reemplazado; lo tomó {quien.Persona.Nombre}";
        if (compensa)
        {
            fila.Compensacion = new CompensacionLeida(true, null, EstadoCompensacion.Pendiente);
            texto += $", con una compensación pendiente para {quien.Persona.Nombre}, sin fecha";
        }
        fila.Interpretaciones.Add(texto + ".");
    }

    private static void Ambiguo(FilaInterpretada fila, string original, string porque)
    {
        fila.Ambiguedades.Add($"Compensado «{original}» {porque}: queda en la observación para revisión.");
        AgregarObservacion(fila, $"Compensado: {original}");
    }

    private static void AgregarObservacion(FilaInterpretada fila, string texto) =>
        fila.Observacion = string.IsNullOrEmpty(fila.Observacion) ? texto.Trim() : $"{fila.Observacion}\n{texto.Trim()}";

    private static string NombreDe(NombreResuelto persona) => persona.Persona?.Nombre ?? persona.Texto;

    /// <summary>«dd/mm/aaaa».</summary>
    private static string Dia(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Estado del Excel; vacío es programado; <c>null</c> si no se reconoce.</summary>
    internal static EstadoTurno? LeerEstado(string? texto) => NombresPersona.Normalizar(texto) switch
    {
        "" or "programado" or "pendiente" => EstadoTurno.Programado,
        "confirmado" => EstadoTurno.Confirmado,
        "aplicado" or "hecho" or "realizado" => EstadoTurno.Aplicado,
        "cancelado" => EstadoTurno.Cancelado,
        "reemplazado" => EstadoTurno.Reemplazado,
        _ => null,
    };

    /// <summary>
    /// Fecha de una celda: serial de Excel (número) o texto en <c>aaaa-mm-dd</c> o <c>dd/mm/aaaa</c>. Devuelve la fecha,
    /// la interpretación hecha (si hubo que convertir) o el error.
    /// </summary>
    internal static (DateOnly? Fecha, string? Interpretacion, string? Error) LeerFecha(CeldaExcel celda)
    {
        if (celda.Numero is { } numero)
        {
            if (numero is < SerialMinimo or > SerialMaximo)
            {
                return (null, null, $"La fecha «{celda.Original}» no es una fecha de Excel válida.");
            }
            var fecha = DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(numero)));
            return (fecha, $"Fecha serial de Excel {Math.Floor(numero).ToString(CultureInfo.InvariantCulture)} → {Dia(fecha)}.", null);
        }
        var texto = celda.Texto?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            return (null, null, "La fila no tiene fecha.");
        }
        if (DateOnly.TryParseExact(texto, FormatosTexto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var leida))
        {
            var iso = texto.Length == 10 && texto[4] == '-';
            return (leida, iso ? null : $"Fecha en texto «{texto}» → {Dia(leida)} (día/mes/año).", null);
        }
        if (DateTime.TryParseExact(texto, FormatosFechaHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var conHora))
        {
            var fecha = DateOnly.FromDateTime(conHora);
            return (fecha, $"Fecha con hora «{texto}» → {Dia(fecha)}.", null);
        }
        return (null, null, $"«{texto}» no es una fecha (se espera aaaa-mm-dd, dd/mm/aaaa o una fecha de Excel).");
    }
}
