using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Atlas.Plataforma.Publicacion;

/// <summary>
/// Llaves públicas (JWKS) del emisor de GitHub Actions, en memoria (ATLAS-ADR-0035). Se piden la primera vez, cada
/// <see cref="OpcionesPublicacion.RefrescoLlaves"/> y cuando llega un token firmado con un <c>kid</c> que no está (GitHub
/// rotó sus llaves), nunca más de una vez cada <see cref="OpcionesPublicacion.EsperaMinimaLlaves"/>. Si GitHub no
/// responde, se siguen usando las últimas llaves buenas. La firma la verifica <c>JsonWebTokenHandler</c>: aquí solo se
/// guardan llaves.
/// </summary>
public sealed partial class LlavesGitHubActions(
    IHttpClientFactory clientes, IOptionsMonitor<OpcionesPublicacion> opciones, TimeProvider reloj, ILogger<LlavesGitHubActions> log)
    : IDisposable
{
    public const string NombreCliente = "publicacion-github-actions";

    /// <summary>Un JWKS real pesa unos pocos KB; más que esto no es un JWKS.</summary>
    private const int TamanoMaximo = 256 * 1024;

    private readonly SemaphoreSlim _candado = new(1, 1);
    private List<SecurityKey> _llaves = [];
    private DateTimeOffset _obtenidasEn = DateTimeOffset.MinValue;
    private DateTimeOffset _ultimoIntento = DateTimeOffset.MinValue;

    /// <summary>
    /// Las llaves vigentes. Las pide a GitHub si nunca se pidieron, si ya toca refrescarlas o si <paramref name="kid"/> no
    /// está entre ellas, respetando la espera mínima entre pedidos.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> ObtenerAsync(string? kid, CancellationToken ct = default)
    {
        if (!HayQuePedir(kid))
        {
            return _llaves;
        }
        await _candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Otra petición pudo traerlas mientras esta esperaba.
            if (HayQuePedir(kid))
            {
                await PedirAsync(ct).ConfigureAwait(false);
            }
            return _llaves;
        }
        finally
        {
            _candado.Release();
        }
    }

    private bool HayQuePedir(string? kid)
    {
        var o = opciones.CurrentValue;
        var ahora = reloj.GetUtcNow();
        if (ahora - _ultimoIntento < o.EsperaMinimaLlaves)
        {
            return false;
        }
        var desconocido = kid is not null && !_llaves.Any(l => string.Equals(l.KeyId, kid, StringComparison.Ordinal));
        return _llaves.Count == 0 || desconocido || ahora - _obtenidasEn >= o.RefrescoLlaves;
    }

    private async Task PedirAsync(CancellationToken ct)
    {
        _ultimoIntento = reloj.GetUtcNow();
        var url = opciones.CurrentValue.UrlLlavesEfectiva;
        try
        {
            using var cliente = clientes.CreateClient(NombreCliente);
            using var respuesta = await cliente.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            respuesta.EnsureSuccessStatusCode();
            if (respuesta.Content.Headers.ContentLength > TamanoMaximo)
            {
                throw new InvalidOperationException("El JWKS es demasiado grande.");
            }
            var json = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (json.Length > TamanoMaximo)
            {
                throw new InvalidOperationException("El JWKS es demasiado grande.");
            }
            // Solo llaves RSA de firma con kid: GitHub firma con RS256.
            var llaves = new JsonWebKeySet(json).Keys
                .Where(k => k.Kty == JsonWebAlgorithmsKeyTypes.RSA && !string.IsNullOrEmpty(k.Kid) && k.Use is null or "sig")
                .Cast<SecurityKey>()
                .ToList();
            if (llaves.Count == 0)
            {
                throw new InvalidOperationException("El JWKS no trae llaves RSA de firma.");
            }
            _llaves = llaves;
            _obtenidasEn = reloj.GetUtcNow();
            LlavesObtenidas(log, llaves.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // GitHub caído o una respuesta rara no tumban la validación: quedan las últimas llaves buenas.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FalloAlPedir(log, _llaves.Count, ex);
        }
    }

    public void Dispose() => _candado.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicación desde la CI: {Llaves} llaves de GitHub Actions obtenidas")]
    private static partial void LlavesObtenidas(ILogger log, int llaves);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación desde la CI: no se pudieron pedir las llaves de GitHub Actions; se siguen usando las {Llaves} anteriores")]
    private static partial void FalloAlPedir(ILogger log, int llaves, Exception ex);
}
