using System.Text.Json;
using Flit.Atlas.Modulos.Catalogo.Datos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo.Semilla;

/// <summary>Una entrada de <c>aplicativos.json</c>; los valores son los textos de la API (<c>producto</c>, <c>en_retiro</c>…).</summary>
/// <param name="Padre">Slug del padre, que debe aparecer antes en el archivo; vacío en los productos.</param>
/// <param name="Grupo">Solo en los productos: módulos y servicios heredan el de su padre.</param>
public sealed record EntradaSemilla(
    string Slug, string Nombre, string Tipo, string? Padre = null, string? Grupo = null, string? Descripcion = null,
    string? Estado = null, string? Criticidad = null, bool DatosPersonales = false, string? Repo = null, string? Ruta = null,
    IReadOnlyList<string>? Tecnologias = null, IReadOnlyList<string>? Ambientes = null, IReadOnlyList<string>? Etiquetas = null);

public sealed record ArchivoSemilla(int Version, IReadOnlyList<EntradaSemilla> Aplicativos);

/// <param name="Creados">Aplicativos nuevos.</param>
/// <param name="YaAplicados">Entradas que una corrida anterior ya aplicó (no se tocan, aunque se hayan editado o eliminado).</param>
/// <param name="Existentes">Entradas cuyo slug ya existía, creado a mano en Atlas: se marcan aplicadas sin cambiarlas.</param>
/// <param name="SinPadre">Entradas cuyo padre no existe (alguien lo eliminó): se omiten y se reintentan en la siguiente corrida.</param>
public sealed record ResultadoSemilla(int Creados, int YaAplicados, int Existentes, int SinPadre);

/// <summary>
/// Semilla del catálogo (HU 2.1, ATLAS-ADR-0028): los productos y módulos del documento de responsables y el inventario
/// del plan 00 §2.1. Idempotente por clave natural (slug) con un registro de lo aplicado (<c>catalogo_semilla</c>): correrla
/// dos veces no duplica, no pisa lo editado en Atlas y no revive lo eliminado. La corre el migrador.
/// </summary>
public static class SemillaCatalogo
{
    private const string Recurso = "Flit.Atlas.Modulos.Catalogo.Semilla.aplicativos.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lee y valida la semilla del ensamblado.</summary>
    public static ArchivoSemilla Cargar()
    {
        using var flujo = typeof(SemillaCatalogo).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso {Recurso}.");
        var archivo = JsonSerializer.Deserialize<ArchivoSemilla>(flujo, Json)
            ?? throw new InvalidOperationException("La semilla del catálogo está vacía.");
        Validar(archivo);
        return archivo;
    }

    /// <summary>
    /// Revisa la semilla con las mismas reglas de la API: slugs únicos y válidos, padres definidos antes, jerarquía,
    /// grupo en los productos y valores conocidos. Falla nombrando la entrada.
    /// </summary>
    public static void Validar(ArchivoSemilla archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var tipos = new Dictionary<string, TipoAplicativo>(StringComparer.Ordinal);
        foreach (var e in archivo.Aplicativos)
        {
            void Falla(string problema) => throw new InvalidOperationException($"Semilla del catálogo, «{e.Slug}»: {problema}");

            if (!Valores.SlugValido(e.Slug)) Falla("el slug no es válido (minúsculas, números y guiones).");
            if (tipos.ContainsKey(e.Slug)) Falla("el slug está repetido.");
            if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > CatalogoDbContext.LargoNombre) Falla("el nombre es obligatorio y corto.");
            if (!Valores.Tipos.TryGetValue(e.Tipo, out var tipo)) Falla($"tipo desconocido «{e.Tipo}».");
            TipoAplicativo? tipoPadre = null;
            if (e.Padre is not null)
            {
                if (!tipos.TryGetValue(e.Padre, out var t)) Falla($"el padre «{e.Padre}» debe aparecer antes.");
                tipoPadre = t;
            }
            if (!Valores.JerarquiaValida(tipo, tipoPadre)) Falla(Valores.ReglaJerarquia);
            if ((tipo == TipoAplicativo.Producto) != (e.Grupo is not null)) Falla("el grupo va solo en los productos (los demás heredan el de su padre).");
            if (e.Grupo is not null && !Valores.Grupos.ContainsKey(e.Grupo)) Falla($"grupo desconocido «{e.Grupo}».");
            if (e.Estado is not null && !Valores.Estados.ContainsKey(e.Estado)) Falla($"estado desconocido «{e.Estado}».");
            if (e.Criticidad is not null && !Valores.Criticidades.ContainsKey(e.Criticidad)) Falla($"criticidad desconocida «{e.Criticidad}».");
            if (e.Repo is not null && !Valores.RepoValido(e.Repo)) Falla("el repo va como dueño/nombre.");
            if ((e.Ambientes ?? []).Any(a => !Valores.Ambientes.Contains(a))) Falla($"ambientes válidos: {string.Join(", ", Valores.Ambientes)}.");
            if ((e.Descripcion?.Length ?? 0) > CatalogoDbContext.LargoDescripcion) Falla("la descripción es muy larga.");
            tipos[e.Slug] = tipo;
        }
    }

    /// <summary>Aplica la semilla en una transacción. Ver <see cref="ResultadoSemilla"/> para lo que cuenta cada número.</summary>
    public static async Task<ResultadoSemilla> AplicarAsync(CatalogoDbContext contexto, ArchivoSemilla archivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(archivo);
        Validar(archivo);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var aplicadas = (await contexto.SemillaAplicada.Select(s => s.Slug).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var existentes = await contexto.Aplicativos
            .Select(a => new { a.Slug, a.Id, a.Grupo })
            .ToDictionaryAsync(a => a.Slug, a => (a.Id, a.Grupo), StringComparer.Ordinal, ct).ConfigureAwait(false);

        int creados = 0, yaAplicados = 0, yaExistian = 0, sinPadre = 0;
        foreach (var e in archivo.Aplicativos)
        {
            if (aplicadas.Contains(e.Slug))
            {
                yaAplicados++;
                continue;
            }
            if (existentes.ContainsKey(e.Slug))
            {
                contexto.SemillaAplicada.Add(new SemillaAplicada { Slug = e.Slug, Version = archivo.Version });
                yaExistian++;
                continue;
            }
            (Guid Id, GrupoAplicativo Grupo)? padre = null;
            if (e.Padre is not null)
            {
                if (!existentes.TryGetValue(e.Padre, out var p))
                {
                    sinPadre++;
                    continue;
                }
                padre = p;
            }
            var aplicativo = new Aplicativo
            {
                Slug = e.Slug,
                Nombre = e.Nombre.Trim(),
                Tipo = Valores.Tipos[e.Tipo],
                PadreId = padre?.Id,
                // Módulos y servicios heredan el grupo de su padre tal como está hoy en la base.
                Grupo = padre?.Grupo ?? Valores.Grupos[e.Grupo!],
                Descripcion = e.Descripcion,
                Estado = e.Estado is null ? EstadoAplicativo.Activo : Valores.Estados[e.Estado],
                Criticidad = e.Criticidad is null ? Datos.Criticidad.Media : Valores.Criticidades[e.Criticidad],
                ManejaDatosPersonales = e.DatosPersonales,
                Repo = e.Repo,
                RutaEnRepo = e.Ruta,
                Tecnologias = Valores.Limpiar(e.Tecnologias, minusculas: false),
                Ambientes = Valores.Limpiar(e.Ambientes, minusculas: true),
                Etiquetas = Valores.Limpiar(e.Etiquetas, minusculas: true),
            };
            contexto.Aplicativos.Add(aplicativo);
            contexto.SemillaAplicada.Add(new SemillaAplicada { Slug = e.Slug, Version = archivo.Version });
            existentes[e.Slug] = (aplicativo.Id, aplicativo.Grupo);
            creados++;
        }

        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return new ResultadoSemilla(creados, yaAplicados, yaExistian, sinPadre);
    }
}
