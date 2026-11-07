using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Documentacion.Exportaciones;

/// <summary>
/// El texto de cada componente vivo al exportar (ATLAS-ADR-0053 §5 y §7): el valor vigente y solo si quien exporta tiene el
/// permiso de lo que muestra, igual que en el lector. Incrustar un componente no da permiso para verlo, y el archivo sale
/// de Atlas: lo que la persona no puede ver no entra.
/// </summary>
internal sealed partial class ValoresVivos(
    PermisosEfectivos permisos, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos, ILectorContratos contratos, ILectorResponsables responsables,
    ISoporteDeTurno soporte, PlataformaDbContext plataforma, DateOnly hoy, CancellationToken ct)
{
    /// <summary>Los códigos que exige cada dato; son de otros módulos (un módulo no referencia a otro, ATLAS-ADR-0026).</summary>
    public const string PermisoContratos = "atlas.contratos.ver_internos";
    public const string PermisoEquipo = "atlas.equipo.ver";
    public const string PermisoSoporte = "atlas.soporte.ver";

    public const string SinAcceso = "_No tienes acceso a este contenido._";

    private static string MalEscrito(string nombre) => $"_Este componente «{nombre}» está mal escrito en el documento._";

    public Task<string> ResolverAsync(ComponenteVivo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.Nombre switch
        {
            "Contrato" => ContratoAsync(c),
            "Responsables" => ResponsablesAsync(c),
            "SoporteDeTurno" => SoporteAsync(),
            "Diagrama" => Task.FromResult(Diagrama(c)),
            _ => Task.FromResult(string.Empty),
        };
    }

    private async Task<string> ContratoAsync(ComponenteVivo c)
    {
        var id = c.Atributos.GetValueOrDefault("id");
        if (id is null || !Codigo().IsMatch(id))
        {
            return MalEscrito(c.Nombre);
        }
        if (!permisos.Tiene(PermisoContratos))
        {
            return SinAcceso;
        }
        var contrato = await contratos.PorCodigoAsync(id, ct).ConfigureAwait(false);
        if (contrato is null)
        {
            return SinAcceso;
        }
        var version = contrato.UltimaVersion is null ? string.Empty : $" · versión {ConversorMdx.Escapar(contrato.UltimaVersion)}";
        var operacion = c.Atributos.TryGetValue("operacion", out var o) ? $" · operación `{o.Replace("`", string.Empty, StringComparison.Ordinal)}`" : string.Empty;
        return $"**Contrato:** {ConversorMdx.Escapar(contrato.Nombre)}{version}{operacion}";
    }

    private async Task<string> ResponsablesAsync(ComponenteVivo c)
    {
        var slug = c.Atributos.GetValueOrDefault("aplicativo");
        if (slug is null || !Codigo().IsMatch(slug))
        {
            return MalEscrito(c.Nombre);
        }
        if (!permisos.Tiene(PermisoEquipo))
        {
            return SinAcceso;
        }
        var aplicativo = aplicativos.Values.FirstOrDefault(a => a.Slug == slug);
        if (aplicativo is null)
        {
            return SinAcceso;
        }
        var porAplicativo = await responsables.DeAplicativosAsync([aplicativo.Id], ct).ConfigureAwait(false);
        var nombres = await NombresAsync(porAplicativo.GetValueOrDefault(aplicativo.Id) ?? []).ConfigureAwait(false);
        return $"**Responsables de {ConversorMdx.Escapar(aplicativo.Nombre)}:** {(nombres.Count == 0 ? "sin asignar" : string.Join(", ", nombres))}";
    }

    private async Task<string> SoporteAsync()
    {
        if (!permisos.Tiene(PermisoSoporte))
        {
            return SinAcceso;
        }
        var sabado = hoy.AddDays(((int)DayOfWeek.Saturday - (int)hoy.DayOfWeek + 7) % 7);
        var dia = await soporte.DelDiaAsync(sabado, ct).ConfigureAwait(false);
        var fecha = sabado.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (dia is null || dia.Equipos.Count == 0)
        {
            return $"**Soporte del sábado {fecha}:** no hay equipos de soporte activos.";
        }
        var partes = new List<string>(dia.Equipos.Count);
        foreach (var equipo in dia.Equipos)
        {
            var nombres = await NombresAsync(equipo.Personas).ConfigureAwait(false);
            partes.Add($"{ConversorMdx.Escapar(equipo.Nombre)}: {(nombres.Count == 0 ? "sin turno" : string.Join(", ", nombres))}");
        }
        return $"**Soporte del sábado {fecha}:** {string.Join("; ", partes)}";
    }

    private static string Diagrama(ComponenteVivo c) =>
        c.Atributos.TryGetValue("id", out var id)
            ? $"_Diagrama {ConversorMdx.Escapar(id)}: disponible en Atlas._"
            : "_Diagrama: disponible en Atlas._";

    private async Task<List<string>> NombresAsync(IReadOnlyList<Guid> personas)
    {
        if (personas.Count == 0)
        {
            return [];
        }
        var filas = await plataforma.Personas.AsNoTracking().Where(p => personas.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct).ConfigureAwait(false);
        return [.. personas.Where(filas.ContainsKey).Select(p => ConversorMdx.Escapar(filas[p]))];
    }

    /// <summary>Los mismos códigos que acepta el lector: un autor no puede cambiar lo que se busca.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex Codigo();
}
