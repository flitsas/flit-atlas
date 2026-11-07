using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>Lo que se guarda en una revisión o en una versión, ya normalizado y validado.</summary>
internal sealed record FotoDocumento(
    string Tipo, string Titulo, string Contenido, VisibilidadDocumento Visibilidad, IReadOnlyList<string> Audiencias, string Responsable)
{
    /// <summary>SHA-256 de la foto en un JSON con orden fijo: restaurar la N da la misma huella que la N.</summary>
    public string Huella() => ReglasDocumentos.Sha256(JsonSerializer.Serialize(new
    {
        tipo = Tipo,
        titulo = Titulo,
        contenido = Contenido,
        visibilidad = ReglasDocumentos.Texto(Visibilidad),
        audiencias = Audiencias,
        responsable = Responsable,
    }));
}

/// <summary>Reglas puras de los documentos (ATLAS-ADR-0053): valores, normalización y validación de campos.</summary>
internal static partial class ReglasDocumentos
{
    /// <summary>Tipos del prompt maestro §8.5.</summary>
    public static readonly IReadOnlySet<string> Tipos = new HashSet<string>(StringComparer.Ordinal)
    {
        "manual_usuario", "guia_funcional", "guia_integracion", "runbook", "ficha_comercial", "adr", "nota_version",
    };

    /// <summary>Tipos que solo llegan desde los repos: un ADR no se escribe en Atlas (prompt maestro §8.5).</summary>
    public static readonly IReadOnlySet<string> TiposSoloRepo = new HashSet<string>(StringComparer.Ordinal) { "adr" };

    public static readonly IReadOnlyDictionary<string, VisibilidadDocumento> Visibilidades = new Dictionary<string, VisibilidadDocumento>(StringComparer.Ordinal)
    {
        ["publico_clientes"] = VisibilidadDocumento.PublicoClientes,
        ["interno_negocio"] = VisibilidadDocumento.InternoNegocio,
        ["interno_ti"] = VisibilidadDocumento.InternoTi,
        ["restringido"] = VisibilidadDocumento.Restringido,
    };

    public static readonly IReadOnlyDictionary<string, EstadoDocumento> Estados = new Dictionary<string, EstadoDocumento>(StringComparer.Ordinal)
    {
        ["borrador"] = EstadoDocumento.Borrador,
        ["en_revision"] = EstadoDocumento.EnRevision,
        ["publicado"] = EstadoDocumento.Publicado,
        ["archivado"] = EstadoDocumento.Archivado,
    };

    public static string Texto(VisibilidadDocumento v) => Visibilidades.First(p => p.Value == v).Key;

    public static string Texto(EstadoDocumento e) => Estados.First(p => p.Value == e).Key;

    public static string Texto(OrigenDocumento o) => o == OrigenDocumento.Repo ? "repo" : "atlas";

    public static string Texto(OrigenRevisionDocumento o) => o switch
    {
        OrigenRevisionDocumento.Creacion => "creacion",
        OrigenRevisionDocumento.Edicion => "edicion",
        _ => "restauracion",
    };

    public static string Sha256(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    /// <summary>Saltos de línea <c>\n</c> y sin marca de orden de bytes: el mismo archivo en Windows y en Linux da el mismo hash.</summary>
    public static string Normalizar(string? texto) =>
        (texto ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>Valida los campos comunes y devuelve la foto o los errores por campo. El MDX se valida aparte.</summary>
    public static (FotoDocumento? Foto, Dictionary<string, string[]> Errores) Leer(
        string? tipo, string? titulo, string? contenido, string? visibilidad, IEnumerable<string>? audiencias, string? responsable)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var ti = (tipo ?? string.Empty).Trim();
        if (!Tipos.Contains(ti))
        {
            errores["tipo"] = [$"Valores válidos: {string.Join(", ", Tipos.Order(StringComparer.Ordinal))}."];
        }
        var t = (titulo ?? string.Empty).Trim();
        if (t.Length is 0 or > DocumentacionDbContext.LargoTitulo)
        {
            errores["titulo"] = [$"El título es obligatorio y va hasta {DocumentacionDbContext.LargoTitulo} caracteres."];
        }
        var c = Normalizar(contenido);
        if (string.IsNullOrWhiteSpace(c) || c.Length > DocumentacionDbContext.LargoContenido)
        {
            errores["contenido"] = [$"El cuerpo es obligatorio y va hasta {DocumentacionDbContext.LargoContenido / 1000} mil caracteres."];
        }
        VisibilidadDocumento? vis = Visibilidades.TryGetValue((visibilidad ?? string.Empty).Trim(), out var v) ? v : null;
        if (vis is null)
        {
            errores["visibilidad"] = [$"Valores válidos: {string.Join(", ", Visibilidades.Keys)}."];
        }
        var aud = (audiencias ?? []).Select(a => (a ?? string.Empty).Trim()).Where(a => a.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (aud.Count > DocumentacionDbContext.MaximoAudiencias || aud.Any(a => a.Length > DocumentacionDbContext.LargoAudiencia))
        {
            errores["audiencias"] = [$"Hasta {DocumentacionDbContext.MaximoAudiencias} audiencias de {DocumentacionDbContext.LargoAudiencia} caracteres."];
        }
        var r = (responsable ?? string.Empty).Trim();
        if (r.Length is 0 or > DocumentacionDbContext.LargoResponsable)
        {
            errores["responsable"] = [$"El responsable es obligatorio y va hasta {DocumentacionDbContext.LargoResponsable} caracteres."];
        }
        return errores.Count > 0 ? (null, errores) : (new FotoDocumento(ti, t, c, vis!.Value, aud, r), errores);
    }

    /// <summary>Ruta relativa del repo, sin subir de carpeta, en un archivo <c>.md</c> o <c>.mdx</c>.</summary>
    public static bool RutaValida(string? ruta) =>
        ruta is { Length: > 0 and <= DocumentacionDbContext.LargoRuta }
        && FormatoRuta().IsMatch(ruta)
        && !ruta.Split('/').Contains("..", StringComparer.Ordinal)
        && (ruta.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || ruta.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^[\p{L}\p{N}._\-][\p{L}\p{N}._\- /]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoRuta();

    /// <summary>El aplicativo y sus ancestros (tope de profundidad contra datos corruptos).</summary>
    public static IEnumerable<Guid> ConAncestros(Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        Guid? actual = aplicativoId;
        for (var i = 0; actual is { } id && i < 10; i++)
        {
            yield return id;
            actual = aplicativos.TryGetValue(id, out var a) ? a.PadreId : null;
        }
    }

    public static AlcanceAsignacion Alcance(Guid aplicativoId) => new(TiposAlcance.Aplicativo, aplicativoId.ToString("D"));
}
