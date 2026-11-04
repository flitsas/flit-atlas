using System.Globalization;
using System.Text;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;

/// <summary>Un turno en un feed .ics: sin correos ni teléfonos (solo el equipo y, en el feed de equipo, el nombre).</summary>
public sealed record EventoIcs(Guid Id, DateOnly Fecha, TimeOnly Inicio, TimeOnly Fin, string Resumen, string? Descripcion, bool Confirmado, DateTimeOffset Modificado);

/// <summary>
/// Genera un iCalendar (RFC 5545) de turnos (HU 2.10): <c>VCALENDAR</c> con <c>METHOD:PUBLISH</c>, la zona
/// <c>America/Bogota</c> en un <c>VTIMEZONE</c> (UTC−5 fijo desde 1993) y un <c>VEVENT</c> por turno con UID estable,
/// para que Outlook, Google Calendar y el calendario de iOS lo refresquen sin duplicar. Líneas con CRLF, texto escapado y
/// plegado a 75 octetos. Puro: sin reloj ni base.
/// </summary>
public static class CalendarioIcs
{
    public const string TipoContenido = "text/calendar; charset=utf-8";
    public const string Zona = "America/Bogota";

    public static string Generar(string nombre, IEnumerable<EventoIcs> eventos, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(eventos);
        var sb = new StringBuilder();
        void Linea(string linea) => Plegar(sb, linea);

        Linea("BEGIN:VCALENDAR");
        Linea("VERSION:2.0");
        Linea("PRODID:-//FLIT S.A.S.//Flit Atlas//ES");
        Linea("CALSCALE:GREGORIAN");
        Linea("METHOD:PUBLISH");
        Linea("X-WR-CALNAME:" + Escapar(nombre));
        Linea("X-WR-TIMEZONE:" + Zona);
        Linea("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Linea("X-PUBLISHED-TTL:PT1H");
        Linea("BEGIN:VTIMEZONE");
        Linea("TZID:" + Zona);
        Linea("BEGIN:STANDARD");
        Linea("DTSTART:19930404T000000");
        Linea("TZOFFSETFROM:-0500");
        Linea("TZOFFSETTO:-0500");
        Linea("TZNAME:-05");
        Linea("END:STANDARD");
        Linea("END:VTIMEZONE");
        foreach (var e in eventos)
        {
            Linea("BEGIN:VEVENT");
            Linea($"UID:soporte-turno-{e.Id:N}@flit-atlas");
            Linea("DTSTAMP:" + Utc(ahora));
            Linea("LAST-MODIFIED:" + Utc(e.Modificado));
            Linea($"DTSTART;TZID={Zona}:{Local(e.Fecha, e.Inicio)}");
            Linea($"DTEND;TZID={Zona}:{Local(e.Fecha, e.Fin)}");
            Linea("SUMMARY:" + Escapar(e.Resumen));
            if (!string.IsNullOrWhiteSpace(e.Descripcion))
            {
                Linea("DESCRIPTION:" + Escapar(e.Descripcion));
            }
            Linea("STATUS:" + (e.Confirmado ? "CONFIRMED" : "TENTATIVE"));
            Linea("TRANSP:OPAQUE");
            Linea("END:VEVENT");
        }
        Linea("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset momento) => momento.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Local(DateOnly fecha, TimeOnly hora) => fecha.ToDateTime(hora).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    /// <summary>TEXT de RFC 5545 §3.3.11: barra invertida, punto y coma, coma y saltos de línea.</summary>
    internal static string Escapar(string texto) => texto
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal);

    /// <summary>RFC 5545 §3.1: líneas de a lo sumo 75 octetos; la continuación empieza con un espacio. No parte un carácter UTF-8.</summary>
    private static void Plegar(StringBuilder sb, string linea)
    {
        var octetos = 0;
        const int limite = 75;
        foreach (var rune in linea.EnumerateRunes())
        {
            var largo = rune.Utf8SequenceLength;
            if (octetos + largo > limite)
            {
                sb.Append("\r\n ");
                octetos = 1;
            }
            sb.Append(rune.ToString());
            octetos += largo;
        }
        sb.Append("\r\n");
    }
}
