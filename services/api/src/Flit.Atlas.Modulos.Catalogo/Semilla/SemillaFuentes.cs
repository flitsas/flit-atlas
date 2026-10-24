using System.Text.Json;
using Flit.Atlas.Modulos.Catalogo.Datos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo.Semilla;

/// <summary>Una entrada de <c>fuentes-publicacion.json</c>.</summary>
/// <param name="Aplicativo">Slug del aplicativo (debe existir: lo crea la semilla del catálogo o alguien en Atlas).</param>
public sealed record EntradaFuente(
    string Aplicativo, string Repositorio, IReadOnlyList<string> Ramas, IReadOnlyList<string> Tipos, string? Workflow = null, string? Nota = null);

public sealed record ArchivoFuentes(int Version, IReadOnlyList<EntradaFuente> Fuentes);

/// <param name="Creadas">Fuentes nuevas.</param>
/// <param name="YaAplicadas">Entradas que ya se aplicaron (no se tocan, aunque se hayan editado o eliminado).</param>
/// <param name="SinAplicativo">Entradas cuyo aplicativo no existe: se omiten y se reintentan en la siguiente corrida.</param>
public sealed record ResultadoSemillaFuentes(int Creadas, int YaAplicadas, int SinAplicativo);

/// <summary>
/// Lista blanca inicial de publicación (HU 3.1, ATLAS-ADR-0035), con la misma regla de la semilla del catálogo
/// (ATLAS-ADR-0028): cada entrada se aplica una sola vez, registrada en <c>catalogo_semilla</c> con la clave
/// <c>fuente:&lt;aplicativo&gt;:&lt;repo&gt;</c>. Revocar una fuente en Atlas es definitivo: la semilla no la revive.
/// </summary>
public static class SemillaFuentes
{
    private const string Recurso = "Flit.Atlas.Modulos.Catalogo.Semilla.fuentes-publicacion.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Clave(EntradaFuente entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return $"fuente:{entrada.Aplicativo}:{entrada.Repositorio.ToLowerInvariant()}";
    }

    public static ArchivoFuentes Cargar()
    {
        using var flujo = typeof(SemillaFuentes).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso {Recurso}.");
        var archivo = JsonSerializer.Deserialize<ArchivoFuentes>(flujo, Json)
            ?? throw new InvalidOperationException("La lista blanca inicial está vacía.");
        Validar(archivo);
        return archivo;
    }

    /// <summary>Las mismas reglas de la API (<see cref="ReglasFuente"/>); falla nombrando la entrada.</summary>
    public static void Validar(ArchivoFuentes archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in archivo.Fuentes)
        {
            var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
            ReglasFuente.Validar(e.Repositorio, e.Ramas, e.Workflow, e.Tipos, e.Nota, errores);
            if (!Valores.SlugValido(e.Aplicativo))
            {
                errores["aplicativo"] = ["El slug del aplicativo no es válido."];
            }
            if (Clave(e).Length > CatalogoDbContext.LargoSlug)
            {
                errores["aplicativo"] = [$"La clave de la entrada pasa de {CatalogoDbContext.LargoSlug} caracteres."];
            }
            if (!claves.Add(Clave(e)))
            {
                errores["repositorio"] = ["La entrada está repetida."];
            }
            if (errores.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Lista blanca inicial, «{Clave(e)}»: {string.Join(" ", errores.SelectMany(p => p.Value))}");
            }
        }
    }

    public static async Task<ResultadoSemillaFuentes> AplicarAsync(CatalogoDbContext contexto, ArchivoFuentes archivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(archivo);
        Validar(archivo);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var aplicadas = (await contexto.SemillaAplicada.Select(s => s.Slug).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var aplicativos = await contexto.Aplicativos.ToDictionaryAsync(a => a.Slug, a => a.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);

        int creadas = 0, yaAplicadas = 0, sinAplicativo = 0;
        foreach (var e in archivo.Fuentes)
        {
            var clave = Clave(e);
            if (aplicadas.Contains(clave))
            {
                yaAplicadas++;
                continue;
            }
            if (!aplicativos.TryGetValue(e.Aplicativo, out var aplicativoId))
            {
                sinAplicativo++;
                continue;
            }
            var repositorio = e.Repositorio.Trim().ToLowerInvariant();
            var workflow = string.IsNullOrWhiteSpace(e.Workflow) ? null : e.Workflow.Trim();
            // Si alguien ya la creó a mano, se respeta tal como está.
            if (!await contexto.FuentesPublicacion.AnyAsync(f => f.AplicativoId == aplicativoId && f.Repositorio == repositorio && f.Workflow == workflow, ct).ConfigureAwait(false))
            {
                contexto.FuentesPublicacion.Add(new FuentePublicacion
                {
                    AplicativoId = aplicativoId,
                    Repositorio = repositorio,
                    Ramas = Valores.Limpiar(e.Ramas, minusculas: false),
                    Workflow = workflow,
                    Tipos = Valores.Limpiar(e.Tipos, minusculas: true),
                    Nota = e.Nota,
                });
                creadas++;
            }
            contexto.SemillaAplicada.Add(new SemillaAplicada { Slug = clave, Version = archivo.Version });
        }

        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return new ResultadoSemillaFuentes(creadas, yaAplicadas, sinAplicativo);
    }
}
