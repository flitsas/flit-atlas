using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Atlas.Modulos.Equipo.GitHub;

/// <summary>
/// Acceso de solo lectura a GitHub (HU 2.4, ATLAS-ADR-0031), sección <c>Plataforma:GitHub</c>: con un token (de una
/// GitHub App o uno de grano fino con permiso de solo lectura de contenidos) o con la GitHub App (id, instalación y llave
/// privada en PEM, que en qa y pdn van en un SealedSecret). Sin nada configurado, Atlas lo dice y no falla.
/// </summary>
public sealed class OpcionesGitHub
{
    public const string Seccion = "Plataforma:GitHub";

    /// <summary>Base de la API REST (cámbiala solo para GitHub Enterprise o para pruebas).</summary>
    public Uri UrlApi { get; set; } = new("https://api.github.com/");

    /// <summary>Token de solo lectura. Si está, se usa antes que la App.</summary>
    public string? Token { get; set; }

    /// <summary>Id de la GitHub App.</summary>
    public string? AppId { get; set; }

    /// <summary>Id de la instalación de la App en la organización.</summary>
    public string? InstalacionId { get; set; }

    /// <summary>Llave privada de la App (PEM, RSA).</summary>
    public string? LlavePrivada { get; set; }

    /// <summary><c>token</c>, <c>app</c> o <c>ninguno</c>.</summary>
    public string Modo =>
        !string.IsNullOrWhiteSpace(Token) ? "token"
        : !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(InstalacionId) && !string.IsNullOrWhiteSpace(LlavePrivada) ? "app"
        : "ninguno";
}

/// <summary>El CODEOWNERS de un repo tal como está en GitHub.</summary>
/// <param name="Ruta">Dónde lo encontró (<c>.github/CODEOWNERS</c>, <c>CODEOWNERS</c> o <c>docs/CODEOWNERS</c>); vacío si no hay.</param>
public sealed record CodeownersReal(string? Ruta, string? Contenido);

/// <summary>GitHub respondió algo que no es un CODEOWNERS (sin acceso, repo inexistente, límite de peticiones…).</summary>
public sealed class GitHubNoDisponibleException(string mensaje) : Exception(mensaje);

/// <summary>Token de instalación de la App en memoria hasta poco antes de vencer (no es estado del negocio: se puede perder).</summary>
public sealed class CacheTokenGitHub : IDisposable
{
    private readonly SemaphoreSlim _candado = new(1, 1);

    internal string? Token { get; set; }

    internal DateTimeOffset VenceEn { get; set; }

    internal SemaphoreSlim Candado => _candado;

    public void Dispose() => _candado.Dispose();
}

/// <summary>Cliente HTTP de solo lectura contra la API REST de GitHub.</summary>
public sealed class ClienteGitHub(HttpClient http, IOptions<OpcionesGitHub> opciones, CacheTokenGitHub cache, TimeProvider reloj)
{
    /// <summary>Dónde busca GitHub el archivo, en su orden.</summary>
    public static readonly IReadOnlyList<string> Rutas = [".github/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS"];

    private OpcionesGitHub Opciones => opciones.Value;

    public bool Configurado => Opciones.Modo != "ninguno";

    public string Modo => Opciones.Modo;

    /// <summary>El primer CODEOWNERS que encuentre en el repo (rama por defecto); sin archivo, ruta y contenido vacíos.</summary>
    public async Task<CodeownersReal> LeerCodeownersAsync(string repo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        var token = await TokenAsync(ct).ConfigureAwait(false);
        foreach (var ruta in Rutas)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, new Uri(Opciones.UrlApi, $"repos/{repo}/contents/{ruta}"));
            Cabeceras(peticion, token);
            peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
            using var respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new GitHubNoDisponibleException(Mensaje(respuesta.StatusCode));
            }
            return new CodeownersReal(ruta, await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        return new CodeownersReal(null, null);
    }

    private static string Mensaje(HttpStatusCode estado) => estado switch
    {
        HttpStatusCode.Unauthorized => "GitHub rechazó la credencial configurada (401): revisa el token o la llave de la App.",
        HttpStatusCode.Forbidden => "GitHub negó el acceso al repo o se agotó el límite de peticiones (403).",
        _ => $"GitHub respondió {(int)estado}.",
    };

    private static void Cabeceras(HttpRequestMessage peticion, string token)
    {
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        peticion.Headers.UserAgent.Add(new ProductInfoHeaderValue("flit-atlas", "1"));
        peticion.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        var o = Opciones;
        if (o.Modo == "token")
        {
            return o.Token!.Trim();
        }
        if (o.Modo != "app")
        {
            throw new GitHubNoDisponibleException("GitHub no está configurado en este ambiente.");
        }
        await cache.Candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cache.Token is not null && cache.VenceEn > reloj.GetUtcNow().AddMinutes(5))
            {
                return cache.Token;
            }
            using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(o.UrlApi, $"app/installations/{o.InstalacionId!.Trim()}/access_tokens"));
            Cabeceras(peticion, JwtDeLaApp(o));
            peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new GitHubNoDisponibleException(Mensaje(respuesta.StatusCode));
            }
            var instalacion = await respuesta.Content.ReadFromJsonAsync<TokenInstalacion>(ct).ConfigureAwait(false)
                ?? throw new GitHubNoDisponibleException("GitHub no devolvió el token de la instalación.");
            cache.Token = instalacion.Token;
            cache.VenceEn = instalacion.ExpiresAt;
            return instalacion.Token;
        }
        finally
        {
            cache.Candado.Release();
        }
    }

    /// <summary>JWT RS256 de la App (emisor = id de la App, 9 minutos), firmado con la librería de Microsoft.</summary>
    private string JwtDeLaApp(OpcionesGitHub o)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(o.LlavePrivada!.Replace("\\n", "\n", StringComparison.Ordinal));
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.AppId!.Trim(),
            // GitHub pide iat un poco en el pasado por la deriva de los relojes.
            IssuedAt = ahora.AddSeconds(-60),
            NotBefore = ahora.AddSeconds(-60),
            Expires = ahora.AddMinutes(9),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    private sealed record TokenInstalacion(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
