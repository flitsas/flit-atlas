using System.Text.RegularExpressions;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>
/// Un permiso de Atlas con forma <c>atlas.&lt;modulo&gt;.&lt;accion&gt;</c> (prompt maestro §7.4) y los roles que lo
/// reciben. Es la <b>única fuente</b> del mapeo rol → permisos (ATLAS-ADR-0023): la matriz, la base y las políticas
/// salen de aquí. <see cref="RolesAtlas.Superadmin"/> recibe todos sin declararlo.
/// </summary>
public sealed partial class PermisoAtlas
{
    /// <param name="codigo"><c>atlas.&lt;modulo&gt;.&lt;accion&gt;</c> en snake_case (se admiten más segmentos).</param>
    /// <param name="descripcion">Qué deja hacer, en una frase; se muestra en la matriz.</param>
    /// <param name="concedidoA">Códigos de <see cref="RolesAtlas"/> que lo reciben.</param>
    public PermisoAtlas(string codigo, string descripcion, params string[] concedidoA)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(descripcion);
        ArgumentNullException.ThrowIfNull(concedidoA);
        if (!Formato().IsMatch(codigo))
        {
            throw new ArgumentException($"«{codigo}» no sigue el formato atlas.<modulo>.<accion> en snake_case.", nameof(codigo));
        }
        Codigo = codigo;
        Descripcion = descripcion;
        ConcedidoA = [.. concedidoA.Distinct(StringComparer.Ordinal)];
    }

    public string Codigo { get; }

    public string Descripcion { get; }

    public IReadOnlyList<string> ConcedidoA { get; }

    public override string ToString() => Codigo;

    [GeneratedRegex("^atlas(\\.[a-z][a-z0-9_]*){2,}$")]
    private static partial Regex Formato();
}

/// <summary>
/// Manifiesto de permisos de un módulo (ATLAS-ADR-0016 y ATLAS-ADR-0023): sus permisos, a qué roles se conceden y los
/// tipos de alcance que agrega. Se registra con <see cref="ExtensionesPermisos.AgregarManifiestoPermisos"/>; agregar un
/// módulo no obliga a tocar los manifiestos de los demás.
/// </summary>
public sealed partial class ManifiestoPermisos
{
    public ManifiestoPermisos(string modulo, string nombre, IReadOnlyList<PermisoAtlas> permisos, IReadOnlyList<string>? tiposAlcance = null)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(permisos);
        if (!FormatoId().IsMatch(modulo))
        {
            throw new ArgumentException($"«{modulo}» no es un id de módulo válido (snake_case).", nameof(modulo));
        }
        foreach (var tipo in tiposAlcance ?? [])
        {
            if (!FormatoId().IsMatch(tipo))
            {
                throw new ArgumentException($"El tipo de alcance «{tipo}» de «{modulo}» no está en snake_case.", nameof(tiposAlcance));
            }
        }
        Modulo = modulo;
        Nombre = nombre;
        Permisos = permisos;
        TiposAlcance = tiposAlcance ?? [];
    }

    /// <summary>Id del módulo (<c>plataforma</c>, <c>soporte</c>, …).</summary>
    public string Modulo { get; }

    /// <summary>Nombre para mostrar en la matriz.</summary>
    public string Nombre { get; }

    public IReadOnlyList<PermisoAtlas> Permisos { get; }

    /// <summary>Tipos de alcance que este módulo agrega a las asignaciones (p. ej. <c>aplicativo</c> en el catálogo).</summary>
    public IReadOnlyList<string> TiposAlcance { get; }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex FormatoId();
}

/// <summary>Tipos de alcance del núcleo. Los módulos agregan los suyos en su manifiesto.</summary>
public static class TiposAlcance
{
    /// <summary>Sin restricción de recurso (dentro de lo que la organización de la persona puede ver).</summary>
    public const string Global = "global";

    /// <summary>Los recursos de una organización; el id es el de <c>plataforma_organizacion</c>.</summary>
    public const string Organizacion = "organizacion";

    /// <summary>Un ambiente (<c>qa</c>, <c>pdn</c>).</summary>
    public const string Ambiente = "ambiente";

    public const string Aplicativo = "aplicativo";
    public const string Paquete = "paquete";
    public const string Documento = "documento";
}
