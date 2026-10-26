using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Portal;

/// <summary>URL públicas de un contrato en un ambiente (las que ve un cliente en el spec podado).</summary>
public sealed class ServidoresPublicos
{
    /// <summary><c>&lt;slug del aplicativo&gt;/&lt;código del contrato&gt;</c>, p. ej. <c>flit2-core-ict/core-ict</c>.</summary>
    public string Contrato { get; set; } = string.Empty;

    /// <summary><c>sandbox</c>, <c>qa</c> o <c>pdn</c>.</summary>
    public string Ambiente { get; set; } = string.Empty;

    /// <summary>URL absolutas <c>https</c> con host público; las valida el podador (ATLAS-ADR-0038).</summary>
    public List<string> Urls { get; set; } = [];
}

/// <summary>
/// Portal de clientes (ATLAS-ADR-0043). Sección <c>Contratos:Portal</c>. Las URL públicas por contrato y ambiente
/// <b>[por verificar con el líder]</b>: sin ellas el spec podado sale sin <c>servers</c>, nunca con los internos.
/// </summary>
public sealed class OpcionesPortal
{
    public const string Seccion = "Contratos:Portal";

    /// <summary>Ambiente del spec podado cuando la persona no pide otro.</summary>
    public string Ambiente { get; set; } = AmbientesPoda.Pdn;

    public List<ServidoresPublicos> Servidores { get; set; } = [];

    /// <summary>Tope de la caché de specs podados, en MiB (la cuenta es el doble de los caracteres del texto).</summary>
    public int CacheMiB { get; set; } = 64;

    public IReadOnlyList<Uri> UrlsDe(string aplicativoSlug, string codigo, string ambiente)
    {
        var clave = $"{aplicativoSlug}/{codigo}";
        return
        [
            .. Servidores
                .Where(s => string.Equals(s.Contrato.Trim(), clave, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Ambiente.Trim(), ambiente, StringComparison.OrdinalIgnoreCase))
                .SelectMany(s => s.Urls)
                .Select(u => Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) ? uri : null)
                .OfType<Uri>(),
        ];
    }
}

/// <summary>El spec podado de un contrato del portal, o por qué no se pudo.</summary>
/// <param name="Codigo">Si falló: <c>no_soportado</c> (proto), <c>sin_operaciones</c>, <c>ambiente_invalido</c> o el código de la poda.</param>
internal sealed record ResultadoSpecPortal(SpecPodado? Spec, string? Codigo, string? Mensaje, bool DesdeCache);

/// <summary>Caché en memoria de los specs podados, con tope de tamaño (son inmutables por su clave).</summary>
internal sealed class CachePodas(IOptions<OpcionesPortal> opciones) : IDisposable
{
    public MemoryCache Cache { get; } = new(new MemoryCacheOptions { SizeLimit = Math.Max(1, opciones.Value.CacheMiB) * 1024L * 1024L });

    public void Dispose() => Cache.Dispose();
}

/// <summary>
/// Spec podado para el portal (HU 4.4, ATLAS-ADR-0038 y ATLAS-ADR-0043): los podadores de la HU 4.2 con las operaciones
/// de la revisión que ve la organización y las URL públicas del ambiente (sin ellas, sin <c>servers</c>). Nunca el spec
/// completo: si la poda falla, no hay resultado. Se cachea por (paquete, revisión, contrato, versión, ambiente, URL): las
/// versiones y las revisiones son inmutables, así que la clave fija el resultado.
/// </summary>
internal sealed class SpecsDelPortal(ContratosDbContext contexto, IOptions<OpcionesPortal> opciones, CachePodas cache, ILectorAplicativos lector)
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(6);

    public Task<ResultadoSpecPortal> PodarAsync(PaqueteDelPortal paquete, ContratoDelPortal contrato, string? ambiente, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        ArgumentNullException.ThrowIfNull(contrato);
        return PodarAsync(paquete.Paquete.Id, paquete.Revision, contrato.Contrato, contrato.Version.Id, contrato.Entrada.Operaciones, ambiente, ct);
    }

    /// <summary>
    /// Lo mismo a partir de las piezas (el sandbox, HU 4.7, poda la revisión de una compartición fuera de una petición del
    /// portal). Quien llama ya comprobó el acceso: aquí solo se poda.
    /// </summary>
    public async Task<ResultadoSpecPortal> PodarAsync(
        Guid paqueteId, int revision, Contrato contrato, Guid versionId, IReadOnlyList<string> operaciones, string? ambiente, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contrato);
        ArgumentNullException.ThrowIfNull(operaciones);
        var destino = string.IsNullOrWhiteSpace(ambiente) ? opciones.Value.Ambiente : ambiente.Trim().ToLowerInvariant();
        if (!AmbientesPoda.Todos.Contains(destino))
        {
            return new(null, "ambiente_invalido", $"Ambientes válidos: {string.Join(", ", AmbientesPoda.Todos.Order(StringComparer.Ordinal))}.", false);
        }
        if (Podadores.Para(contrato.Tipo) is not { } podador)
        {
            return new(null, "no_soportado", "Los contratos proto no se comparten podados todavía.", false);
        }
        if (operaciones.Count == 0)
        {
            return new(null, "sin_operaciones", "El paquete no comparte operaciones de este contrato (solo sus guías).", false);
        }
        var slug = (await lector.ListarAsync(ct).ConfigureAwait(false)).FirstOrDefault(a => a.Id == contrato.AplicativoId)?.Slug ?? string.Empty;
        var urls = opciones.Value.UrlsDe(slug, contrato.Codigo, destino);
        var clave = $"portal:spec:{paqueteId:N}:{revision}:{contrato.Id:N}:{versionId:N}:{destino}:{Huella(urls)}";
        if (cache.Cache.TryGetValue(clave, out SpecPodado? guardado) && guardado is not null)
        {
            return new(guardado, null, null, true);
        }
        var contenido = await contexto.Versiones.AsNoTracking().Where(v => v.Id == versionId && v.ContratoId == contrato.Id).Select(v => v.Contenido).FirstAsync(ct).ConfigureAwait(false);
        try
        {
            var podado = podador.Podar(new SolicitudPoda(contenido, operaciones, new DestinoPoda(destino, urls)));
            // Defensa: el podador ya verifica el resultado; aquí se comprueba otra vez que no sobre ninguna operación.
            if (podado.Resumen.Operaciones.Any(o => !operaciones.Contains(o, StringComparer.Ordinal)))
            {
                return new(null, "verificacion_fallida", "El spec podado traía operaciones fuera de la selección.", false);
            }
            cache.Cache.Set(clave, podado, new MemoryCacheEntryOptions { Size = Math.Max(1, podado.Contenido.Length * 2L), SlidingExpiration = Vigencia });
            return new(podado, null, null, false);
        }
        catch (PodaException ex)
        {
            return new(null, ex.Codigo, ex.Message, false);
        }
    }

    private static string Huella(IReadOnlyList<Uri> urls) =>
        urls.Count == 0 ? "-" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', urls.Select(u => u.AbsoluteUri)))))[..16];
}
