using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Sandbox;

/// <summary>Microcks no respondió o rechazó la operación (el mensaje no trae la respuesta de Microcks: puede citar el spec).</summary>
internal sealed class ErrorMicrocks(string mensaje, HttpStatusCode? estado = null) : Exception(mensaje)
{
    public HttpStatusCode? Estado { get; } = estado;
}

/// <summary>
/// La API de Microcks que usa el sandbox (HU 4.7, ATLAS-ADR-0045), probada contra la 1.15.0: importar un spec
/// (<c>POST /api/artifact/upload?mainArtifact=true</c>, multipart con <c>file</c>; Microcks toma el nombre y la versión del
/// servicio de <c>info.title</c> e <c>info.version</c>), buscar un servicio por <c>nombre:versión</c>
/// (<c>GET /api/services/{nombre}:{versión}</c>), borrarlo por su id (<c>DELETE /api/services/{id}</c>) y llamar a sus
/// mocks (<c>/rest/{nombre}/{versión}/…</c>). Una interfaz para que las pruebas del portal usen uno en memoria.
/// </summary>
internal interface IClienteMicrocks
{
    bool Disponible { get; }

    /// <summary>Importa (o reemplaza) un servicio a partir de un OpenAPI en JSON cuyo <c>info</c> ya trae su nombre y su versión.</summary>
    Task ImportarAsync(string servicio, string version, string especificacionJson, CancellationToken ct);

    Task<bool> ExisteAsync(string servicio, string version, CancellationToken ct);

    /// <summary>Idempotente: si no existe, no hace nada.</summary>
    Task BorrarAsync(string servicio, string version, CancellationToken ct);

    /// <summary>Envía una petición a los mocks de un servicio: <paramref name="ruta"/> ya viene escapada por segmentos.</summary>
    Task<HttpResponseMessage> LlamarMockAsync(
        string servicio, string version, string ruta, string? consulta, HttpMethod metodo, IEnumerable<KeyValuePair<string, string[]>> cabeceras,
        byte[]? cuerpo, string? tipoContenido, CancellationToken ct);
}

/// <summary>Cliente HTTP de Microcks: el token de Keycloak (si lo pide) se cachea hasta un minuto antes de vencer.</summary>
internal sealed class ClienteMicrocks(IHttpClientFactory fabrica, IOptions<OpcionesSandbox> opciones, TimeProvider reloj) : IClienteMicrocks, IDisposable
{
    public const string NombreCliente = "contratos.microcks";

    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenVence;

    public bool Disponible => opciones.Value.Disponible;

    public void Dispose() => _candado.Dispose();

    public async Task ImportarAsync(string servicio, string version, string especificacionJson, CancellationToken ct)
    {
        using var contenido = new MultipartFormDataContent();
        var archivo = new StringContent(especificacionJson, Encoding.UTF8, "application/json");
        contenido.Add(archivo, "file", $"{servicio}-{version}.json");
        using var peticion = await PeticionApiAsync(HttpMethod.Post, "api/artifact/upload?mainArtifact=true", ct).ConfigureAwait(false);
        peticion.Content = contenido;
        using var respuesta = await EnviarAsync(peticion, ct).ConfigureAwait(false);
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new ErrorMicrocks($"Microcks rechazó la importación ({(int)respuesta.StatusCode}).", respuesta.StatusCode);
        }
        // Microcks responde «nombre:versión» del servicio que creó: si no coincide, el spec no traía lo que se le puso.
        var creado = (await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        if (!string.Equals(creado, $"{servicio}:{version}", StringComparison.Ordinal))
        {
            throw new ErrorMicrocks("Microcks importó un servicio con otro nombre o versión.");
        }
    }

    public async Task<bool> ExisteAsync(string servicio, string version, CancellationToken ct) =>
        await IdAsync(servicio, version, ct).ConfigureAwait(false) is not null;

    public async Task BorrarAsync(string servicio, string version, CancellationToken ct)
    {
        if (await IdAsync(servicio, version, ct).ConfigureAwait(false) is not { } id)
        {
            return;
        }
        using var peticion = await PeticionApiAsync(HttpMethod.Delete, $"api/services/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        using var respuesta = await EnviarAsync(peticion, ct).ConfigureAwait(false);
        if (!respuesta.IsSuccessStatusCode && respuesta.StatusCode != HttpStatusCode.NotFound)
        {
            throw new ErrorMicrocks($"Microcks no borró el servicio ({(int)respuesta.StatusCode}).", respuesta.StatusCode);
        }
    }

    public async Task<HttpResponseMessage> LlamarMockAsync(
        string servicio, string version, string ruta, string? consulta, HttpMethod metodo, IEnumerable<KeyValuePair<string, string[]>> cabeceras,
        byte[]? cuerpo, string? tipoContenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabeceras);
        var peticion = new HttpRequestMessage(metodo, $"rest/{Uri.EscapeDataString(servicio)}/{Uri.EscapeDataString(version)}/{ruta}{consulta}");
        foreach (var (nombre, valores) in cabeceras)
        {
            peticion.Headers.TryAddWithoutValidation(nombre, valores);
        }
        if (cuerpo is not null)
        {
            peticion.Content = new ByteArrayContent(cuerpo);
            if (tipoContenido is not null && MediaTypeHeaderValue.TryParse(tipoContenido, out var tipo))
            {
                peticion.Content.Headers.ContentType = tipo;
            }
        }
        try
        {
            return await Cliente().SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        finally
        {
            peticion.Dispose();
        }
    }

    private async Task<string?> IdAsync(string servicio, string version, CancellationToken ct)
    {
        using var peticion = await PeticionApiAsync(HttpMethod.Get,
            $"api/services/{Uri.EscapeDataString($"{servicio}:{version}")}?messages=false", ct).ConfigureAwait(false);
        using var respuesta = await EnviarAsync(peticion, ct).ConfigureAwait(false);
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new ErrorMicrocks($"Microcks no respondió la consulta del servicio ({(int)respuesta.StatusCode}).", respuesta.StatusCode);
        }
        var leido = await respuesta.Content.ReadFromJsonAsync<ServicioMicrocks>(ct).ConfigureAwait(false);
        return leido is { Id: { Length: > 0 } id } && leido.Name == servicio && leido.Version == version ? id : null;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        try
        {
            return await Cliente().SendAsync(peticion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorMicrocks($"Microcks no responde ({ex.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ErrorMicrocks("Microcks no respondió a tiempo.");
        }
    }

    private HttpClient Cliente()
    {
        var cliente = fabrica.CreateClient(NombreCliente);
        cliente.BaseAddress = opciones.Value.BaseMicrocks ?? throw new ErrorMicrocks("El sandbox no está configurado (Contratos:Sandbox:UrlMicrocks).");
        cliente.Timeout = opciones.Value.TiempoEspera;
        return cliente;
    }

    private async Task<HttpRequestMessage> PeticionApiAsync(HttpMethod metodo, string ruta, CancellationToken ct)
    {
        var peticion = new HttpRequestMessage(metodo, ruta);
        if (await TokenAsync(ct).ConfigureAwait(false) is { } token)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return peticion;
    }

    /// <summary>Token de la cuenta de servicio de Microcks en su Keycloak (<c>client_credentials</c>); sin <c>UrlToken</c>, ninguno.</summary>
    private async Task<string?> TokenAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        if (string.IsNullOrWhiteSpace(o.UrlToken))
        {
            return null;
        }
        await _candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && reloj.GetUtcNow() < _tokenVence)
            {
                return _token;
            }
            using var peticion = new HttpRequestMessage(HttpMethod.Post, o.UrlToken.Trim())
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = o.ClienteId ?? string.Empty,
                    ["client_secret"] = o.ClienteSecreto ?? string.Empty,
                }),
            };
            using var respuesta = await EnviarAsync(peticion, ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode || await respuesta.Content.ReadFromJsonAsync<TokenMicrocks>(ct).ConfigureAwait(false) is not { AccessToken: { Length: > 0 } token } leido)
            {
                throw new ErrorMicrocks($"El Keycloak de Microcks no entregó un token ({(int)respuesta.StatusCode}).", respuesta.StatusCode);
            }
            _token = token;
            _tokenVence = reloj.GetUtcNow().AddSeconds(Math.Max(0, leido.ExpiresIn - 60));
            return _token;
        }
        finally
        {
            _candado.Release();
        }
    }

    private sealed record ServicioMicrocks(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("version")] string? Version);

    private sealed record TokenMicrocks(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
