using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Contratos.Guias;

/// <summary>Lo que se guarda en cada revisión, ya normalizado y validado.</summary>
internal sealed record FotoGuia(
    string Titulo, string Contenido, string? Operacion, string? AplicaDesde, string? AplicaHasta, VisibilidadGuia Visibilidad)
{
    /// <summary>
    /// SHA-256 de la foto (sin autor, fecha ni nota), en un JSON con orden fijo: restaurar la N da la misma huella que la
    /// N, y guardar sin cambios se detecta sin comparar textos largos.
    /// </summary>
    public string Huella()
    {
        var json = JsonSerializer.Serialize(new
        {
            titulo = Titulo,
            contenido = Contenido,
            operacion = Operacion,
            aplicaDesde = AplicaDesde,
            aplicaHasta = AplicaHasta,
            visibilidad = ReglasGuias.Texto(Visibilidad),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static FotoGuia De(RevisionGuia r) => new(r.Titulo, r.Contenido, r.Operacion, r.AplicaDesde, r.AplicaHasta, r.Visibilidad);
}

/// <summary>
/// Reglas puras de las guías (HU 3.7, ATLAS-ADR-0040): normalización y validación de lo que llega, a qué versiones aplica
/// una guía y quién puede editarla o aprobarla.
/// </summary>
internal static class ReglasGuias
{
    public static readonly IReadOnlyDictionary<string, VisibilidadGuia> Visibilidades = new Dictionary<string, VisibilidadGuia>(StringComparer.Ordinal)
    {
        ["interna"] = VisibilidadGuia.Interna,
        ["compartible"] = VisibilidadGuia.Compartible,
    };

    public static string Texto(VisibilidadGuia v) => v == VisibilidadGuia.Interna ? "interna" : "compartible";

    public static string Texto(EstadoGuia e) => e == EstadoGuia.Borrador ? "borrador" : "publicada";

    public static string Texto(OrigenRevision o) => o switch
    {
        OrigenRevision.Creacion => "creacion",
        OrigenRevision.Edicion => "edicion",
        _ => "restauracion",
    };

    /// <summary>
    /// Normaliza (recorta, saltos de línea <c>\n</c>, vacío = sin valor) y valida los campos de una guía. Devuelve la foto
    /// o los errores por campo (400). La operación se valida contra el índice aparte, porque exige la base.
    /// </summary>
    public static (FotoGuia? Foto, string? Nota, Dictionary<string, string[]> Errores) Leer(
        string? titulo, string? contenido, string? operacion, string? aplicaDesde, string? aplicaHasta, string? visibilidad, string? nota)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var t = (titulo ?? string.Empty).Trim();
        if (t.Length is 0 or > ContratosDbContext.LargoTituloGuia)
        {
            errores["titulo"] = [$"El título es obligatorio y va hasta {ContratosDbContext.LargoTituloGuia} caracteres."];
        }
        var c = (contenido ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(c) || c.Length > ContratosDbContext.LargoContenidoGuia)
        {
            errores["contenido"] = [$"El cuerpo es obligatorio y va hasta {ContratosDbContext.LargoContenidoGuia / 1000} mil caracteres."];
        }
        var o = Vacio(operacion);
        if (o is { Length: > ContratosDbContext.LargoClave })
        {
            errores["operacion"] = [$"La operación va hasta {ContratosDbContext.LargoClave} caracteres."];
        }
        var desde = Vacio(aplicaDesde);
        var hasta = Vacio(aplicaHasta);
        var vDesde = desde is null ? null : VersionSemantica.Leer(desde);
        var vHasta = hasta is null ? null : VersionSemantica.Leer(hasta);
        if (desde is not null && vDesde is null)
        {
            errores["aplicaDesde"] = ["Debe ser una versión semántica (X.Y.Z o X.Y.Z-<prerelease>), o vacío para «desde la primera»."];
        }
        if (hasta is not null && vHasta is null)
        {
            errores["aplicaHasta"] = ["Debe ser una versión semántica (X.Y.Z o X.Y.Z-<prerelease>), o vacío para «hasta la última»."];
        }
        if (vDesde is not null && vHasta is not null && VersionSemantica.Comparar(vDesde, vHasta) > 0)
        {
            errores["aplicaHasta"] = ["La versión final no puede ser anterior a la inicial."];
        }
        VisibilidadGuia? vis = Visibilidades.TryGetValue((visibilidad ?? string.Empty).Trim(), out var v) ? v : null;
        if (vis is null)
        {
            errores["visibilidad"] = ["Valores válidos: interna, compartible."];
        }
        var n = (nota ?? string.Empty).Trim();
        if (n.Length is 0 or > ContratosDbContext.LargoNotaGuia)
        {
            errores["nota"] = [$"La nota del cambio es obligatoria y va hasta {ContratosDbContext.LargoNotaGuia} caracteres."];
        }
        return errores.Count > 0
            ? (null, null, errores)
            : (new FotoGuia(t, c, o, vDesde?.ToString(), vHasta?.ToString(), vis!.Value), n, errores);
    }

    /// <summary>¿La guía aplica a esa versión del contrato? Sin límites, a todas.</summary>
    public static bool AplicaA(string? desde, string? hasta, VersionSemantica version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return (VersionSemantica.Leer(desde) is not { } d || VersionSemantica.Comparar(version, d) >= 0)
            && (VersionSemantica.Leer(hasta) is not { } h || VersionSemantica.Comparar(version, h) <= 0);
    }

    /// <summary>
    /// ¿Puede editar las guías de un contrato del aplicativo? <c>atlas.contratos.guias.editar</c> global (líder técnico) o
    /// con alcance al aplicativo o a uno de sus ancestros (rol implícito del responsable, ATLAS-ADR-0031): el responsable de
    /// un producto también edita las guías de los contratos de sus módulos y servicios, como en el catálogo.
    /// </summary>
    public static bool PuedeEditar(PermisosEfectivos permisos, Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        return permisos.TieneEn(PermisosContratos.GuiasEditar, AlcanceAsignacion.Global)
            || ConAncestros(aplicativoId, aplicativos).Any(id => permisos.TieneEn(PermisosContratos.GuiasEditar, Alcance(id)));
    }

    /// <summary>
    /// ¿Puede aprobar (publicar) lo compartible con clientes? Solo el responsable principal o secundario del aplicativo o
    /// de uno de sus ancestros (prompt maestro §7.2: «lo que se publica como publico_clientes requiere aprobación del
    /// principal o del secundario»): el rol implícito con su alcance, no el permiso global de editar.
    /// </summary>
    public static bool PuedeAprobar(PermisosEfectivos permisos, Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        var alcances = ConAncestros(aplicativoId, aplicativos).Select(Alcance).ToHashSet();
        return permisos.Concesiones.Any(c => c.Rol == RolesAtlas.Responsable && alcances.Contains(c.Alcance));
    }

    private static AlcanceAsignacion Alcance(Guid id) => new(TiposAlcance.Aplicativo, id.ToString("D"));

    /// <summary>El aplicativo y sus ancestros (tope de profundidad contra datos corruptos).</summary>
    internal static IEnumerable<Guid> ConAncestros(Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        Guid? actual = aplicativoId;
        for (var i = 0; actual is { } id && i < 10; i++)
        {
            yield return id;
            actual = aplicativos.TryGetValue(id, out var a) ? a.PadreId : null;
        }
    }

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
