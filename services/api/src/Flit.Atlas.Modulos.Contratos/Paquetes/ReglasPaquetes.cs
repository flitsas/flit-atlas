using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Contratos.Paquetes;

/// <summary>Una guía dentro de un paquete: la guía y la revisión que se comparte (<c>guiaId@revision</c>, plan 00 §8.4).</summary>
internal sealed record GuiaEnPaquete(Guid GuiaId, int Revision);

/// <summary>Un contrato dentro de un paquete: su versión estable fijada, las operaciones elegidas y sus guías.</summary>
internal sealed record EntradaPaquete(Guid ContratoId, Guid VersionId, IReadOnlyList<string> Operaciones, IReadOnlyList<GuiaEnPaquete> Guias);

/// <summary>Una guía como llega en la solicitud: sin revisión, la publicada (se completa contra la base).</summary>
internal sealed record GuiaSolicitada(Guid GuiaId, int? Revision);

/// <summary>Un contrato como llega en la solicitud, ya normalizado.</summary>
internal sealed record EntradaSolicitada(Guid ContratoId, Guid VersionId, IReadOnlyList<string> Operaciones, IReadOnlyList<GuiaSolicitada> Guias);

/// <summary>Lo que se guarda en cada revisión de un paquete, ya normalizado y validado.</summary>
internal sealed record FotoPaquete(string Nombre, string? Descripcion, IReadOnlyList<EntradaPaquete> Definicion)
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SHA-256 de la foto (sin autor, fecha ni nota) en un JSON con orden fijo: la definición ya viene ordenada, así que
    /// guardar lo mismo en otro orden se detecta como «sin cambios».
    /// </summary>
    public string Huella()
    {
        var json = JsonSerializer.Serialize(new { nombre = Nombre, descripcion = Descripcion, definicion = Definicion }, Opciones);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>La definición en el JSON de <c>contratos_paquete_revision.definicion</c>.</summary>
    public string DefinicionJson() => JsonSerializer.Serialize(Definicion, Opciones);

    public static IReadOnlyList<EntradaPaquete> LeerDefinicion(string json) =>
        JsonSerializer.Deserialize<List<EntradaPaquete>>(json, Opciones) ?? [];

    public static FotoPaquete De(RevisionPaquete r) => new(r.Nombre, r.Descripcion, LeerDefinicion(r.Definicion));
}

/// <summary>
/// Reglas puras de los paquetes (HU 4.1, ATLAS-ADR-0041): normalización y validación de la forma de lo que llega, el
/// filtro de operaciones del asistente y quién puede armar un paquete. Lo que exige la base (que el contrato sea
/// compartible, la versión estable, las operaciones del índice, las guías aprobadas) lo revisa
/// <see cref="EndpointsPaquetes"/>.
/// </summary>
internal static class ReglasPaquetes
{
    /// <summary>Contratos por paquete.</summary>
    public const int MaximoContratos = 50;

    /// <summary>Operaciones por contrato (los contratos más grandes de FLIT tienen unos cientos).</summary>
    public const int MaximoOperaciones = 2000;

    /// <summary>Guías por contrato.</summary>
    public const int MaximoGuias = 50;

    public static string Texto(EstadoPaquete e) => e switch
    {
        EstadoPaquete.Borrador => "borrador",
        EstadoPaquete.Publicado => "publicado",
        _ => "archivado",
    };

    public static readonly IReadOnlyDictionary<string, EstadoPaquete> Estados = new Dictionary<string, EstadoPaquete>(StringComparer.Ordinal)
    {
        ["borrador"] = EstadoPaquete.Borrador,
        ["publicado"] = EstadoPaquete.Publicado,
        ["archivado"] = EstadoPaquete.Archivado,
    };

    /// <summary>
    /// Normaliza (recorta, quita repetidos, ordena) y valida la forma de un paquete: nombre, descripción, nota y
    /// definición. Devuelve el nombre, la descripción, la definición solicitada y la nota, o los errores por campo (400).
    /// </summary>
    public static (string? Nombre, string? Descripcion, IReadOnlyList<EntradaSolicitada>? Definicion, string? Nota, Dictionary<string, string[]> Errores) Leer(
        string? nombre, string? descripcion, IReadOnlyList<EndpointsPaquetes.EntradaSolicitud?>? definicion, string? nota)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var n = (nombre ?? string.Empty).Trim();
        if (n.Length is 0 or > ContratosDbContext.LargoNombrePaquete)
        {
            errores["nombre"] = [$"El nombre es obligatorio y va hasta {ContratosDbContext.LargoNombrePaquete} caracteres."];
        }
        var d = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (d is { Length: > ContratosDbContext.LargoDescripcion })
        {
            errores["descripcion"] = [$"La descripción va hasta {ContratosDbContext.LargoDescripcion} caracteres."];
        }
        var t = (nota ?? string.Empty).Trim();
        if (t.Length is 0 or > ContratosDbContext.LargoNotaPaquete)
        {
            errores["nota"] = [$"La nota del cambio es obligatoria y va hasta {ContratosDbContext.LargoNotaPaquete} caracteres."];
        }

        var entradas = new List<EntradaSolicitada>();
        var lista = definicion ?? [];
        if (lista.Count > MaximoContratos)
        {
            errores["definicion"] = [$"Un paquete lleva hasta {MaximoContratos} contratos."];
        }
        var contratos = new HashSet<Guid>();
        for (var i = 0; i < lista.Count && i < MaximoContratos; i++)
        {
            var campo = $"definicion[{i}]";
            if (lista[i] is not { } e || e.ContratoId == Guid.Empty || e.VersionId == Guid.Empty)
            {
                errores[campo] = ["Cada contrato lleva contratoId y versionId."];
                continue;
            }
            if (!contratos.Add(e.ContratoId))
            {
                errores[$"{campo}.contratoId"] = ["El contrato está repetido: un paquete fija una sola versión de cada contrato."];
                continue;
            }
            var operaciones = (e.Operaciones ?? []).Select(o => o?.Trim()).ToList();
            if (operaciones.Any(o => string.IsNullOrEmpty(o) || o.Length > ContratosDbContext.LargoClave))
            {
                errores[$"{campo}.operaciones"] = [$"Cada operación es una clave del índice de hasta {ContratosDbContext.LargoClave} caracteres."];
                continue;
            }
            var unicas = operaciones.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (unicas.Count > MaximoOperaciones)
            {
                errores[$"{campo}.operaciones"] = [$"Hasta {MaximoOperaciones} operaciones por contrato."];
                continue;
            }
            var guias = (e.Guias ?? []).ToList();
            if (guias.Any(g => g is null || g.GuiaId == Guid.Empty || g.Revision is <= 0))
            {
                errores[$"{campo}.guias"] = ["Cada guía lleva guiaId y, si se da, una revisión desde 1."];
                continue;
            }
            if (guias.Count > MaximoGuias || guias.DistinctBy(g => g!.GuiaId).Count() != guias.Count)
            {
                errores[$"{campo}.guias"] = [$"Hasta {MaximoGuias} guías por contrato, sin repetir."];
                continue;
            }
            entradas.Add(new EntradaSolicitada(e.ContratoId, e.VersionId, unicas,
                [.. guias.Select(g => new GuiaSolicitada(g!.GuiaId, g.Revision)).OrderBy(g => g.GuiaId.ToString("D"), StringComparer.Ordinal)]));
        }
        return errores.Count > 0
            ? (null, null, null, null, errores)
            : (n, d, [.. entradas.OrderBy(e => e.ContratoId.ToString("D"), StringComparer.Ordinal)], t, errores);
    }

    /// <summary>¿Se puede publicar? Al menos un contrato con al menos una operación (HU 4.1).</summary>
    public static bool TieneAlgoQueCompartir(IReadOnlyList<EntradaPaquete> definicion) =>
        definicion.Any(e => e.Operaciones.Count > 0);

    /// <summary>
    /// Filtro del paso 2 del asistente: por etiqueta (sin distinguir mayúsculas) y, con <paramref name="soloPublicas"/>,
    /// solo las marcadas <c>x-public: true</c> o con la etiqueta <c>public</c>.
    /// </summary>
    public static IEnumerable<OperacionContrato> Filtrar(IEnumerable<OperacionContrato> operaciones, string? etiqueta, bool soloPublicas)
    {
        var e = string.IsNullOrWhiteSpace(etiqueta) ? null : etiqueta.Trim();
        return operaciones.Where(o => (!soloPublicas || o.Publica) && (e is null || o.Etiquetas.Contains(e, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// ¿Arma paquetes de cualquier contrato? <c>atlas.paquetes.gestionar</c> global: el líder técnico (prompt maestro §7.2).
    /// </summary>
    public static bool GestionaTodo(PermisosEfectivos permisos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        return permisos.TieneEn(PermisosContratos.PaquetesGestionar, AlcanceAsignacion.Global);
    }

    /// <summary>
    /// ¿Puede poner en un paquete los contratos de este aplicativo? Global, o con alcance al aplicativo o a uno de sus
    /// ancestros: el rol implícito del responsable principal o secundario arma paquetes <b>de sus contratos</b>
    /// (prompt maestro §7.2, ATLAS-ADR-0031), igual que edita sus guías.
    /// </summary>
    public static bool PuedeUsar(PermisosEfectivos permisos, Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        return GestionaTodo(permisos)
            || ReglasGuias.ConAncestros(aplicativoId, aplicativos)
                .Any(id => permisos.TieneEn(PermisosContratos.PaquetesGestionar, new AlcanceAsignacion(TiposAlcance.Aplicativo, id.ToString("D"))));
    }

    /// <summary>
    /// ¿Puede tocar un paquete (revisar, publicar, fijar, archivar)? Global, o todos sus contratos son de aplicativos
    /// suyos. Un paquete sin contratos solo lo toca quien lo creó (además de los gestores globales).
    /// </summary>
    public static bool PuedeGestionar(
        PermisosEfectivos permisos, IReadOnlyCollection<Guid> aplicativosDeSusContratos, Guid creadoPor, Guid persona,
        IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        ArgumentNullException.ThrowIfNull(aplicativosDeSusContratos);
        if (GestionaTodo(permisos))
        {
            return true;
        }
        if (!permisos.Tiene(PermisosContratos.PaquetesGestionar))
        {
            return false;
        }
        return aplicativosDeSusContratos.Count == 0
            ? creadoPor == persona
            : aplicativosDeSusContratos.All(a => PuedeUsar(permisos, a, aplicativos));
    }
}
