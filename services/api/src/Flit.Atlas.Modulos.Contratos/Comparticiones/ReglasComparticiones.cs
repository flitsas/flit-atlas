using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Modulos.Contratos.Comparticiones;

/// <summary>
/// Reglas puras de las comparticiones (HU 4.3, ATLAS-ADR-0043): el estado calculado al leer y la forma de la vigencia. Lo
/// que exige la base (la organización, el paquete publicado, la revisión fijada) lo revisa
/// <see cref="EndpointsComparticiones"/>.
/// </summary>
internal static class ReglasComparticiones
{
    public const string Vigente = "vigente";
    public const string Programada = "programada";
    public const string Vencida = "vencida";
    public const string Revocada = "revocada";

    /// <summary>
    /// El estado se calcula al leer (como el de las invitaciones, ATLAS-ADR-0025): no depende de un proceso que lo marque, y
    /// el portal usa exactamente la misma regla para dar o quitar el acceso.
    /// </summary>
    public static string Estado(ComparticionPaquete c, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.RevocadoEn is not null)
        {
            return Revocada;
        }
        if (c.VigenteDesde > ahora)
        {
            return Programada;
        }
        return c.VigenteHasta is { } hasta && hasta <= ahora ? Vencida : Vigente;
    }

    public static bool EsVigente(ComparticionPaquete c, DateTimeOffset ahora) => Estado(c, ahora) == Vigente;

    /// <summary>
    /// La vigencia pedida: desde (por defecto, ahora) y hasta (opcional, después de desde y en el futuro; obligatoria si la
    /// organización es proveedora, como el acceso de sus personas, §6.2). Devuelve los errores por campo (400).
    /// </summary>
    public static (DateTimeOffset Desde, DateTimeOffset? Hasta, Dictionary<string, string[]> Errores) Vigencia(
        DateTimeOffset? desde, DateTimeOffset? hasta, TipoOrganizacion tipo, DateTimeOffset ahora)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var inicio = desde ?? ahora;
        if (hasta is { } fin)
        {
            if (fin <= inicio)
            {
                errores["vigenteHasta"] = ["La vigencia debe terminar después de empezar."];
            }
            else if (fin <= ahora)
            {
                errores["vigenteHasta"] = ["La vigencia debe terminar en el futuro."];
            }
        }
        else if (tipo == TipoOrganizacion.Proveedor)
        {
            errores["vigenteHasta"] = ["Con un proveedor, la compartición lleva vencimiento (prompt maestro §6.2)."];
        }
        return (inicio, hasta, errores);
    }

    public static string Texto(TipoOrganizacion tipo) => tipo switch
    {
        TipoOrganizacion.Interna => "interna",
        TipoOrganizacion.Cliente => "cliente",
        _ => "proveedor",
    };
}
