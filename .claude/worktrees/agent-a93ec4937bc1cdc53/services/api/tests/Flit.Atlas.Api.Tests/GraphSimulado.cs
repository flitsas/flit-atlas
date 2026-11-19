using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Flit.Atlas.Identidad.Personas;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Microsoft Graph simulado para la sincronización de personas (ATLAS-ADR-0030): un manejador HTTP en memoria con los
/// usuarios, sus fotos y los miembros del grupo. Nada sale a Microsoft. Datos ficticios (<c>ejemplo.com</c>).
/// </summary>
public sealed class GraphSimulado : HttpMessageHandler
{
    public const string Token = "Bearer token-de-prueba";

    public sealed class Usuario
    {
        public required Guid Id { get; init; }
        public required string Nombre { get; set; }
        public string? Correo { get; set; }
        public string? Cargo { get; set; }
        public string? Area { get; set; }
        public string? Movil { get; set; }
        public List<string> TelefonosTrabajo { get; set; } = [];
        public bool Habilitada { get; set; } = true;
        public string Tipo { get; set; } = "Member";
        public byte[]? Foto { get; set; }
        public string TipoFoto { get; set; } = "image/jpeg";
        public bool EnGrupo { get; set; } = true;
    }

    public ConcurrentDictionary<Guid, Usuario> Usuarios { get; } = new();

    /// <summary>Respuestas 503 pendientes antes de volver a responder bien (Graph caído).</summary>
    public int FallasPendientes { get; set; }

    /// <summary>Peticiones atendidas, con su ruta y consulta (para verificar qué se pidió).</summary>
    public ConcurrentQueue<string> Peticiones { get; } = new();

    /// <summary>Tamaño de página de los listados: con usuarios de más se prueba el <c>@odata.nextLink</c>.</summary>
    public int TamanoPagina { get; set; } = 2;

    /// <summary>Si no es <c>null</c>, pedir el token lanza esta excepción (una falla que reintentar no arregla).</summary>
    public Exception? ErrorDelToken { get; set; }

    public Usuario Agregar(string nombre, string? correo = null, bool habilitada = true, byte[]? foto = null)
    {
        var usuario = new Usuario { Id = Guid.NewGuid(), Nombre = nombre, Correo = correo, Habilitada = habilitada, Foto = foto };
        Usuarios[usuario.Id] = usuario;
        return usuario;
    }

    /// <summary>El directorio de Atlas contra este Graph, con un token de aplicación ficticio.</summary>
    public DirectorioGraph Directorio() =>
        new(new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://graph.microsoft.com/") }, new AutorizacionSimulada(this));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var peticion = request;
        var ruta = peticion.RequestUri!.AbsolutePath;
        var consulta = Uri.UnescapeDataString(peticion.RequestUri.Query);
        Peticiones.Enqueue(ruta + consulta);
        if (peticion.Headers.Authorization?.ToString() != Token)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
        if (FallasPendientes > 0)
        {
            FallasPendientes--;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        var partes = ruta.Trim('/').Split('/');
        // v1.0/users/{id}/photos/120x120/$value o v1.0/users/{id}/photo/$value
        if (partes is ["v1.0", "users", var idFoto, "photos", _, "$value"])
        {
            return Task.FromResult(Foto(idFoto));
        }
        if (partes is ["v1.0", "users", _, "photo", "$value"])
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        if (partes is ["v1.0", "users", var id])
        {
            return Task.FromResult(Guid.TryParse(id, out var oid) && Usuarios.TryGetValue(oid, out var usuario)
                ? Json(Vista(usuario))
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        if (partes is ["v1.0", "users"])
        {
            Assert.True(consulta.Contains("$skiptoken=", StringComparison.Ordinal) || consulta.Contains("userType eq 'Member'", StringComparison.Ordinal));
            return Task.FromResult(Pagina(ruta, Usuarios.Values.Where(u => u.Tipo == "Member"), consulta));
        }
        if (partes is ["v1.0", "groups", _, "transitiveMembers", "microsoft.graph.user"])
        {
            return Task.FromResult(Pagina(ruta, Usuarios.Values.Where(u => u.EnGrupo), consulta));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage Foto(string id)
    {
        if (!Guid.TryParse(id, out var oid) || !Usuarios.TryGetValue(oid, out var usuario) || usuario.Foto is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        var contenido = new ByteArrayContent(usuario.Foto);
        contenido.Headers.ContentType = new MediaTypeHeaderValue(usuario.TipoFoto);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido };
    }

    /// <summary>Página ordenada por id; la siguiente se pide con <c>$skiptoken</c> en un enlace absoluto, como Graph.</summary>
    private HttpResponseMessage Pagina(string ruta, IEnumerable<Usuario> usuarios, string consulta)
    {
        const string Marca = "$skiptoken=";
        var indice = consulta.IndexOf(Marca, StringComparison.Ordinal);
        var desde = indice < 0 ? 0 : int.Parse(consulta[(indice + Marca.Length)..].Split('&')[0], System.Globalization.CultureInfo.InvariantCulture);
        var todos = usuarios.OrderBy(u => u.Id).ToList();
        var pagina = todos.Skip(desde).Take(TamanoPagina).Select(Vista).ToList();
        var siguiente = desde + TamanoPagina < todos.Count ? $"https://graph.microsoft.com{ruta}?{Marca}{desde + TamanoPagina}" : null;
        return Json(new Dictionary<string, object?> { ["value"] = pagina, ["@odata.nextLink"] = siguiente });
    }

    private static Dictionary<string, object?> Vista(Usuario u) => new()
    {
        ["id"] = u.Id,
        ["displayName"] = u.Nombre,
        ["mail"] = u.Correo,
        ["userPrincipalName"] = u.Correo ?? $"{u.Id:N}@ejemplo.onmicrosoft.com",
        ["jobTitle"] = u.Cargo,
        ["department"] = u.Area,
        ["mobilePhone"] = u.Movil,
        ["businessPhones"] = u.TelefonosTrabajo,
        ["accountEnabled"] = u.Habilitada,
        ["userType"] = u.Tipo,
    };

    private static HttpResponseMessage Json(object cuerpo) => new(HttpStatusCode.OK) { Content = JsonContent.Create(cuerpo) };

    private sealed class AutorizacionSimulada(GraphSimulado graph) : IAuthorizationHeaderProvider
    {
        public Task<string> CreateAuthorizationHeaderForAppAsync(string scopes, AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("https://graph.microsoft.com/.default", scopes);
            // Como Microsoft.Identity.Web fuera del callback de OIDC: sin esquema nombrado toma el de la cookie y falla.
            if (downstreamApiOptions?.AcquireTokenOptions?.AuthenticationOptionsName != ClaimsAtlas.EsquemaEntra)
            {
                throw new InvalidOperationException("IDW10503: Cannot determine the cloud Instance. The provided authentication scheme was ''.");
            }
            if (graph.ErrorDelToken is { } error)
            {
                throw error;
            }
            return Task.FromResult(Token);
        }

        public Task<string> CreateAuthorizationHeaderForUserAsync(IEnumerable<string> scopes, AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
            ClaimsPrincipal? claimsPrincipal = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("La sincronización usa el token de aplicación, nunca el del usuario.");

        public Task<string> CreateAuthorizationHeaderAsync(IEnumerable<string> scopes, AuthorizationHeaderProviderOptions? options = null,
            ClaimsPrincipal? claimsPrincipal = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("La sincronización usa el token de aplicación, nunca el del usuario.");
    }
}
