using System.Globalization;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos.Deprecacion;

/// <summary>Configuración de los avisos de deprecación (sección <c>Contratos:AvisosDeprecacion</c>, ATLAS-ADR-0046).</summary>
public sealed class OpcionesAvisosDeprecacion
{
    public const string Seccion = "Contratos:AvisosDeprecacion";

    /// <summary>Apaga solo el trabajo programado; deprecar sigue funcionando y los avisos salen en la corrida siguiente.</summary>
    public bool Habilitados { get; set; } = true;

    /// <summary>Días antes del retiro en que sale un recordatorio. Vacío: 30 y 7.</summary>
    public int[]? DiasRecordatorio { get; set; }

    /// <summary>Cada cuánto revisa si toca avisar.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Los recordatorios válidos, de mayor a menor (sin repetidos ni ceros).</summary>
    public IReadOnlyList<int> Dias =>
        [.. (DiasRecordatorio is { Length: > 0 } dias ? dias : [30, 7]).Where(d => d is > 0 and <= 365).Distinct().OrderDescending()];
}

/// <summary>Reglas y textos de la deprecación (HU 4.8, plan 00 §8.2 regla 7 y §8.5, ATLAS-ADR-0046).</summary>
internal static class ReglasDeprecacion
{
    /// <summary>Hasta cuántos días adelante se puede fijar el retiro (dos años).</summary>
    public const int DiasMaximosRetiro = 730;

    public static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    /// <summary>Hoy en Bogotá (UTC−5), con el reloj inyectado.</summary>
    public static DateOnly Hoy(TimeProvider reloj) => Fecha(reloj.GetUtcNow());

    public static DateOnly Fecha(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, Bogota).DateTime);

    /// <summary><c>dd/mm/aaaa</c> (prompt maestro §1).</summary>
    public static string Texto(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// La versión que reemplaza a una deprecada: la estable vigente más alta del mismo contrato posterior a ella (por
    /// SemVer). <c>null</c> si no la hay todavía.
    /// </summary>
    public static string? Reemplazo(string version, IEnumerable<(string Version, bool EsEstable, EstadoVersion Estado)> delContrato)
    {
        if (VersionSemantica.Leer(version) is not { } actual)
        {
            return null;
        }
        return delContrato
            .Where(v => v.EsEstable && v.Estado == EstadoVersion.Vigente)
            .Select(v => VersionSemantica.Leer(v.Version))
            .OfType<VersionSemantica>()
            .Where(v => VersionSemantica.Comparar(v, actual) > 0)
            .OrderDescending(Comparer<VersionSemantica>.Create(VersionSemantica.Comparar))
            .FirstOrDefault()?.ToString();
    }

    /// <summary>El reemplazo de cada versión deprecada pedida (por id), en una consulta.</summary>
    public static async Task<Dictionary<Guid, string?>> ReemplazosAsync(
        ContratosDbContext contexto, IReadOnlyCollection<(Guid VersionId, Guid ContratoId, string Version)> deprecadas, CancellationToken ct)
    {
        if (deprecadas.Count == 0)
        {
            return [];
        }
        var contratos = deprecadas.Select(d => d.ContratoId).Distinct().ToList();
        var versiones = (await contexto.Versiones.AsNoTracking().Where(v => contratos.Contains(v.ContratoId))
                .Select(v => new { v.ContratoId, v.Version, v.EsEstable, v.Estado }).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(v => v.ContratoId);
        return deprecadas.DistinctBy(d => d.VersionId).ToDictionary(d => d.VersionId,
            d => Reemplazo(d.Version, versiones[d.ContratoId].Select(v => (v.Version, v.EsEstable, v.Estado))));
    }

    /// <summary>«faltan 30 días», «falta 1 día», «es hoy».</summary>
    public static string Faltan(DateOnly retiro, DateOnly hoy)
    {
        var dias = retiro.DayNumber - hoy.DayNumber;
        return dias switch
        {
            <= 0 => "es hoy",
            1 => "falta 1 día",
            _ => string.Create(CultureInfo.InvariantCulture, $"faltan {dias} días"),
        };
    }

    /// <summary>«La reemplaza la versión 2.0.0.» o la falta de reemplazo.</summary>
    public static string TextoReemplazo(string? reemplazo) => reemplazo is null
        ? "Todavía no hay una versión estable que la reemplace; FLIT te avisará cuando la publique."
        : $"La reemplaza la versión {reemplazo}.";

    /// <summary>
    /// El aviso del portal (Inicio y detalle del paquete) con el mismo texto que la notificación: versión, contrato, fecha de
    /// retiro y su reemplazo.
    /// </summary>
    public static string MensajePortal(string version, string contrato, DateOnly? retiro, string? reemplazo) => retiro is { } fecha
        ? $"La versión {version} de {contrato} está deprecada y se retira el {Texto(fecha)}. {TextoReemplazo(reemplazo)}"
        : $"La versión {version} de {contrato} está deprecada. {TextoReemplazo(reemplazo)}";

    /// <summary>«el paquete «A»», «los paquetes «A» y «B»».</summary>
    public static string Paquetes(IReadOnlyList<string> nombres) => nombres.Count switch
    {
        0 => "tus paquetes compartidos",
        1 => $"el paquete «{nombres[0]}»",
        _ => $"los paquetes {string.Join(", ", nombres.Take(nombres.Count - 1).Select(n => $"«{n}»"))} y «{nombres[^1]}»",
    };

    /// <summary>Título y cuerpo para una persona externa: solo lo suyo (sus paquetes), nunca otras organizaciones.</summary>
    public static (string Titulo, string Cuerpo) TextoExterno(
        MomentoAviso momento, string version, string contrato, DateOnly retiro, DateOnly hoy, string? reemplazo, IReadOnlyList<string> paquetes)
    {
        var titulo = momento == MomentoAviso.Deprecacion
            ? $"Se retira la versión {version} de {contrato} el {Texto(retiro)}"
            : $"Recordatorio: la versión {version} de {contrato} se retira el {Texto(retiro)}";
        var cuerpo = $"La versión {version} de {contrato}, que usas en {Paquetes(paquetes)}, está deprecada y se retira el {Texto(retiro)} ({Faltan(retiro, hoy)}). "
            + $"{TextoReemplazo(reemplazo)} Revisa el contrato en el portal de Flit Atlas y planea el cambio antes de esa fecha.";
        return (titulo, cuerpo);
    }

    /// <summary>Título y cuerpo para un responsable interno: cuántas organizaciones se avisaron, sin nombrarlas.</summary>
    public static (string Titulo, string Cuerpo) TextoInterno(
        MomentoAviso momento, string version, string contrato, DateOnly retiro, DateOnly hoy, string? reemplazo, int organizaciones)
    {
        var titulo = momento == MomentoAviso.Deprecacion
            ? $"Se deprecó la versión {version} de {contrato}: se retira el {Texto(retiro)}"
            : $"Recordatorio: la versión {version} de {contrato} se retira el {Texto(retiro)}";
        var quienes = organizaciones switch
        {
            0 => "Ninguna organización tiene hoy un paquete compartido con esta versión.",
            1 => "Atlas le avisa a 1 organización con un paquete compartido que la usa.",
            _ => string.Create(CultureInfo.InvariantCulture, $"Atlas les avisa a {organizaciones} organizaciones con paquetes compartidos que la usan."),
        };
        var cuerpo = $"Eres responsable del aplicativo de {contrato}. La versión {version} está deprecada y se retira el {Texto(retiro)} ({Faltan(retiro, hoy)}). "
            + $"{(reemplazo is null ? "Todavía no hay una versión estable que la reemplace." : $"La reemplaza la versión {reemplazo}.")} {quienes}";
        return (titulo, cuerpo);
    }

    /// <summary>Clave del aviso: versión, fecha de retiro, momento, persona y canal (ATLAS-ADR-0046).</summary>
    public static string Clave(Guid version, DateOnly retiro, MomentoAviso momento, int? dias, Guid persona, CanalAviso canal) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{version:N}:{retiro:yyyyMMdd}:{(momento == MomentoAviso.Deprecacion ? "deprecacion" : $"recordatorio-{dias}")}:{persona:N}:{Texto(canal)}");

    public static string Texto(CanalAviso canal) => canal switch
    {
        CanalAviso.App => "app",
        CanalAviso.Push => "push",
        _ => "correo",
    };

    /// <summary>
    /// El recordatorio que toca hoy, si alguno: el de menos días cuya ventana ya empezó (faltan esos días o menos) y que no
    /// se cubrió con el aviso de la deprecación (que salió antes de entrar a esa ventana). Así, deprecar a 5 días del retiro
    /// manda un solo aviso, y una corrida atrasada no manda los recordatorios que ya pasaron.
    /// </summary>
    public static int? RecordatorioDeHoy(DateOnly retiro, DateOnly hoy, DateOnly avisadaEl, IReadOnlyList<int> dias)
    {
        var faltan = retiro.DayNumber - hoy.DayNumber;
        int? elegido = null;
        foreach (var d in dias)
        {
            if (faltan <= d && avisadaEl < retiro.AddDays(-d) && (elegido is null || d < elegido))
            {
                elegido = d;
            }
        }
        return elegido;
    }
}
