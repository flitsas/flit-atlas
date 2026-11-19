namespace Flit.Atlas.Modulos.Contratos.Canales;

/// <summary>Un ambiente con canal (plan 00 §8.1): qué ramas lo despliegan y si solo admite versiones estables.</summary>
public sealed class AmbienteCanal
{
    /// <summary><c>dev</c>, <c>qa</c> o <c>pdn</c>: minúsculas, números y guiones.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Ramas (exactas o con <c>*</c>) cuyo CD puede mover el canal: el token OIDC trae la rama que desplegó.</summary>
    public List<string> Ramas { get; set; } = [];

    /// <summary>Si solo acepta versiones sin prerelease (pdn: lo que ven los clientes, plan 00 §8.2, regla 3).</summary>
    public bool SoloEstables { get; set; }
}

/// <summary>
/// Canales por ambiente (HU 3.4, ATLAS-ADR-0035 §8). Sección <c>Contratos:Canales</c>. Por defecto, los del plan 00 y
/// ATLAS-ADR-0019: <c>dev</c> ← <c>develop</c>, <c>qa</c> ← <c>staging</c> y <c>pdn</c> ← <c>release</c> (solo estables).
/// El orden de la lista es el de la promoción (dev → qa → pdn): los desfases del Consolidado comparan ambientes vecinos.
/// </summary>
public sealed class OpcionesCanales
{
    public const string Seccion = "Contratos:Canales";

    /// <summary>Ambientes en orden de promoción. Vacía: dev, qa y pdn.</summary>
    public List<AmbienteCanal> Ambientes { get; set; } = [];

    /// <summary>
    /// El ambiente de referencia: su versión es la base de comparación que la CI baja de Atlas
    /// (<c>GET /publicaciones/base</c>) y contra la que el Consolidado calcula los breaking pendientes.
    /// </summary>
    public string Referencia { get; set; } = "pdn";

    /// <summary>El ambiente que se compara con la referencia en los breaking pendientes (lo que está por llegar a pdn).</summary>
    public string Candidato { get; set; } = "qa";

    /// <summary>
    /// Pone los ambientes por defecto si no se configuraron. Se hace después de leer la configuración porque el enlazador
    /// de .NET suma los elementos configurados a los de una lista ya llena.
    /// </summary>
    public void Normalizar()
    {
        if (Ambientes.Count == 0)
        {
            Ambientes.AddRange(
            [
                new AmbienteCanal { Nombre = "dev", Ramas = ["develop"] },
                new AmbienteCanal { Nombre = "qa", Ramas = ["staging"] },
                new AmbienteCanal { Nombre = "pdn", Ramas = ["release"], SoloEstables = true },
            ]);
        }
        foreach (var ambiente in Ambientes)
        {
            ambiente.Nombre = ambiente.Nombre.Trim().ToLowerInvariant();
        }
    }

    public AmbienteCanal? Buscar(string? nombre) =>
        Ambientes.FirstOrDefault(a => string.Equals(a.Nombre, nombre?.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> Nombres => [.. Ambientes.Select(a => a.Nombre)];
}
