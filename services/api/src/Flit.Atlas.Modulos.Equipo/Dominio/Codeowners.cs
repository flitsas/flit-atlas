using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Equipo.Dominio;

/// <summary>Responsables de un aplicativo para generar CODEOWNERS y fichas: principal y secundario, con su usuario de GitHub.</summary>
public sealed record ResponsableCodigo(string Tipo, Guid PersonaId, string Nombre, string? UsuarioGitHub);

/// <summary>Una línea del bloque CODEOWNERS sugerido.</summary>
/// <param name="Patron"><c>*</c> para el repo completo o <c>/carpeta/</c>.</param>
/// <param name="Propietarios">Los <c>@usuario</c> de GitHub de principal y secundario, en ese orden.</param>
/// <param name="SinUsuario">Responsables sin usuario de GitHub: la línea no los puede nombrar.</param>
/// <param name="HeredadoDe">Si los responsables son del padre (un servicio sin responsables propios).</param>
public sealed record LineaCodeowners(
    string Patron, AplicativoLeido Aplicativo, IReadOnlyList<string> Propietarios, IReadOnlyList<string> SinUsuario, AplicativoLeido? HeredadoDe);

/// <summary>El bloque sugerido de un repo.</summary>
public sealed record BloqueCodeowners(string Repo, IReadOnlyList<LineaCodeowners> Lineas, string Texto);

/// <summary>Diferencia entre lo que sugiere Atlas y lo que hay en el CODEOWNERS real de GitHub para un patrón.</summary>
/// <param name="Estado"><c>igual</c>, <c>distinto</c>, <c>falta_en_github</c> o <c>solo_en_github</c>.</param>
public sealed record DiferenciaCodeowners(string Patron, string Estado, IReadOnlyList<string> EnAtlas, IReadOnlyList<string> EnGitHub);

/// <summary>
/// CODEOWNERS y ficha de responsables (HU 2.4, ATLAS-ADR-0031). El repo y la carpeta salen del catálogo (el repo se
/// hereda del ancestro más cercano que lo tenga); los responsables, del aplicativo o, si no tiene, de su ancestro más
/// cercano con responsables. En GitHub gana la última línea que coincide: por eso el repo completo (<c>*</c>) va primero.
/// </summary>
public static partial class Codeowners
{
    public static IReadOnlyList<BloqueCodeowners> Generar(
        IReadOnlyList<AplicativoLeido> aplicativos, IReadOnlyDictionary<Guid, IReadOnlyList<ResponsableCodigo>> responsables, int? version)
    {
        ArgumentNullException.ThrowIfNull(aplicativos);
        ArgumentNullException.ThrowIfNull(responsables);
        var porId = aplicativos.ToDictionary(a => a.Id);
        var lineas = new List<(string Repo, LineaCodeowners Linea)>();
        foreach (var aplicativo in aplicativos.Where(a => a.Estado != "retirado"))
        {
            var repo = RepoDe(aplicativo, porId);
            if (repo is null)
            {
                continue;
            }
            string? patron = aplicativo.RutaEnRepo is { Length: > 0 } ruta ? $"/{ruta.Trim('/')}/"
                : aplicativo.Repo is { Length: > 0 } ? "*"
                : null;
            if (patron is null)
            {
                // Un módulo sin carpeta propia: su código está en las carpetas de sus servicios.
                continue;
            }
            var (duenos, heredado) = ResponsablesDe(aplicativo, porId, responsables);
            if (duenos.Count == 0)
            {
                continue;
            }
            lineas.Add((repo, new LineaCodeowners(patron, aplicativo,
                [.. duenos.Where(d => d.UsuarioGitHub is not null).Select(d => "@" + d.UsuarioGitHub)],
                [.. duenos.Where(d => d.UsuarioGitHub is null).Select(d => d.Nombre)],
                heredado)));
        }
        return
        [
            .. lineas.GroupBy(l => l.Repo, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                // Una carpeta repetida (dos aplicativos con la misma ruta) se queda con el primero.
                var delRepo = g.Select(l => l.Linea).DistinctBy(l => l.Patron, StringComparer.Ordinal)
                    .OrderBy(l => l.Patron == "*" ? 0 : 1).ThenBy(l => l.Patron, StringComparer.Ordinal).ToList();
                return new BloqueCodeowners(g.Key, delRepo, Texto(g.Key, delRepo, version));
            }),
        ];
    }

    private static string? RepoDe(AplicativoLeido aplicativo, IReadOnlyDictionary<Guid, AplicativoLeido> porId)
    {
        for (var actual = aplicativo; actual is not null; actual = actual.PadreId is { } padre ? porId.GetValueOrDefault(padre) : null)
        {
            if (actual.Repo is { Length: > 0 } repo)
            {
                return repo;
            }
        }
        return null;
    }

    /// <summary>Los responsables del aplicativo o, si no tiene, los de su ancestro más cercano que los tenga.</summary>
    public static (IReadOnlyList<ResponsableCodigo> Responsables, AplicativoLeido? HeredadoDe) ResponsablesDe(
        AplicativoLeido aplicativo, IReadOnlyDictionary<Guid, AplicativoLeido> porId, IReadOnlyDictionary<Guid, IReadOnlyList<ResponsableCodigo>> responsables)
    {
        ArgumentNullException.ThrowIfNull(aplicativo);
        ArgumentNullException.ThrowIfNull(porId);
        ArgumentNullException.ThrowIfNull(responsables);
        for (var actual = aplicativo; actual is not null; actual = actual.PadreId is { } padre ? porId.GetValueOrDefault(padre) : null)
        {
            if (responsables.TryGetValue(actual.Id, out var lista) && lista.Count > 0)
            {
                return ([.. lista.OrderBy(r => r.Tipo == "principal" ? 0 : 1)], actual.Id == aplicativo.Id ? null : actual);
            }
        }
        return ([], null);
    }

    private static string Texto(string repo, IReadOnlyList<LineaCodeowners> lineas, int? version)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# CODEOWNERS sugerido por Flit Atlas para {repo}")
            .Append(version is { } v ? string.Create(CultureInfo.InvariantCulture, $" (reparto versión {v})") : string.Empty).Append('\n');
        sb.Append("# Principal y secundario de cada carpeta; cámbialos en Atlas, no a mano.\n");
        foreach (var linea in lineas)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n# {linea.Aplicativo.Nombre}").Append(linea.HeredadoDe is { } h ? $" (responsables de {h.Nombre})" : string.Empty).Append('\n');
            if (linea.Propietarios.Count > 0)
            {
                sb.Append(linea.Patron).Append(' ').Append(string.Join(' ', linea.Propietarios)).Append('\n');
            }
            if (linea.SinUsuario.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"# Sin usuario de GitHub en Atlas: {string.Join(", ", linea.SinUsuario)}\n");
                if (linea.Propietarios.Count == 0)
                {
                    sb.Append("# ").Append(linea.Patron).Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Las reglas de un CODEOWNERS real: patrón y propietarios en minúsculas, sin comentarios.</summary>
    public static IReadOnlyList<(string Patron, IReadOnlyList<string> Propietarios)> Leer(string contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var reglas = new List<(string, IReadOnlyList<string>)>();
        foreach (var crudo in contenido.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var linea = crudo.Split('#', 2)[0].Trim();
            if (linea.Length == 0)
            {
                continue;
            }
            var partes = Espacios().Split(linea);
            reglas.Add((partes[0], [.. partes.Skip(1).Select(p => p.ToLowerInvariant())]));
        }
        return reglas;
    }

    /// <summary>Compara el bloque sugerido con el CODEOWNERS real, patrón por patrón (la última regla de un patrón es la que vale).</summary>
    public static IReadOnlyList<DiferenciaCodeowners> Comparar(BloqueCodeowners? sugerido, string? real)
    {
        var enAtlas = (sugerido?.Lineas ?? []).ToDictionary(l => l.Patron, l => l.Propietarios.Select(p => p.ToLowerInvariant()).ToList(), StringComparer.Ordinal);
        var enGitHub = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (patron, propietarios) in real is null ? [] : Leer(real))
        {
            enGitHub[Normalizar(patron)] = [.. propietarios];
        }
        var patrones = enAtlas.Keys.Concat(enGitHub.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(p => p == "*" ? 0 : 1).ThenBy(p => p, StringComparer.Ordinal);
        return
        [
            .. patrones.Select(p =>
            {
                var a = enAtlas.GetValueOrDefault(p);
                var g = enGitHub.GetValueOrDefault(p);
                var estado = (a, g) switch
                {
                    (not null, null) => "falta_en_github",
                    (null, not null) => "solo_en_github",
                    _ => a!.ToHashSet(StringComparer.Ordinal).SetEquals(g!) ? "igual" : "distinto",
                };
                return new DiferenciaCodeowners(p, estado, a ?? [], g ?? []);
            }),
        ];
    }

    /// <summary><c>services/x</c>, <c>/services/x</c> y <c>/services/x/</c> son la misma carpeta para comparar.</summary>
    private static string Normalizar(string patron) =>
        patron == "*" || patron.Contains('*', StringComparison.Ordinal) || patron.Contains('.', StringComparison.Ordinal)
            ? patron
            : $"/{patron.Trim('/')}/";

    /// <summary>La «Ficha de responsables» en Markdown para el README del aplicativo (§4 del documento).</summary>
    public static string Ficha(AplicativoLeido aplicativo, IReadOnlyList<ResponsableCodigo> responsables, AplicativoLeido? heredadoDe,
        string? repo, IReadOnlyList<DefinicionRol> definiciones, int? version, DateOnly? fechaVersion)
    {
        ArgumentNullException.ThrowIfNull(aplicativo);
        ArgumentNullException.ThrowIfNull(responsables);
        ArgumentNullException.ThrowIfNull(definiciones);
        var sb = new StringBuilder("## Ficha de responsables\n\n");
        sb.Append("| Rol | Persona | GitHub |\n|---|---|---|\n");
        foreach (var tipo in new[] { "principal", "secundario" })
        {
            var r = responsables.FirstOrDefault(x => x.Tipo == tipo);
            sb.Append(CultureInfo.InvariantCulture,
                $"| Responsable {tipo} | {r?.Nombre ?? TablasReparto.SinResponsable} | {(r?.UsuarioGitHub is { } u ? "@" + u : TablasReparto.SinResponsable)} |\n");
        }
        if (heredadoDe is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nResponsables de {heredadoDe.Nombre}, al que pertenece {aplicativo.Nombre}.\n");
        }
        sb.Append("\n**Cómo desplegar:** sigue el manual de operación del repo");
        sb.Append(repo is null ? "." : string.Create(CultureInfo.InvariantCulture, $" `{repo}`")).Append(repo is null ? "\n" : ".\n");
        sb.Append("\n**A quién escalar:** primero al responsable principal; si no responde, al secundario. Los sábados, al soporte de turno (Flit Atlas, «¿A quién llamo?»).\n");
        foreach (var definicion in definiciones)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n**{definicion.Titulo}**\n");
            foreach (var punto in definicion.Puntos)
            {
                sb.Append("- ").Append(punto).Append('\n');
            }
        }
        if (version is { } v)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n_Reparto versión {v}")
                .Append(fechaVersion is { } f ? $" del {Textos.Fecha(f)}" : string.Empty)
                .Append(", generado por Flit Atlas._\n");
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();
}
