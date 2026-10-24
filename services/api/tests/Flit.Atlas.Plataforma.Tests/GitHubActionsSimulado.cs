using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// El emisor OIDC de GitHub Actions simulado (ATLAS-ADR-0035): llaves RSA generadas en la prueba, su JWKS servido por un
/// manejador HTTP en memoria y tokens firmados con los claims de un workflow. Nada sale a GitHub. Repos y datos ficticios
/// (<c>flitsas-demo/...</c>).
/// </summary>
public sealed class GitHubActionsSimulado : HttpMessageHandler
{
    public const string Emisor = "https://token.actions.githubusercontent.com";
    public const string Audiencia = "https://atlas.ejemplo.com";
    public const string Propietario = "flitsas-demo";
    public const string WorkflowOficial = "flitsas-demo/plantillas/.github/workflows/atlas-publicar.yml@refs/tags/v1";

    /// <summary>Patrón de <c>WorkflowsReutilizables</c> que acepta <see cref="WorkflowOficial"/>.</summary>
    public const string PatronWorkflowOficial = "flitsas-demo/plantillas/.github/workflows/atlas-publicar.yml@refs/tags/v*";

    private readonly List<(string Kid, RSA Llave)> _llaves = [];

    public GitHubActionsSimulado() => Rotar();

    /// <summary>Cuántas veces se pidió el JWKS.</summary>
    public int PedidosJwks { get; private set; }

    /// <summary>Si está caído, el JWKS responde 503.</summary>
    public bool Caido { get; set; }

    /// <summary>La llave con la que se firma ahora.</summary>
    public string KidActual => _llaves[^1].Kid;

    /// <summary>GitHub agrega una llave nueva y firma con ella; con <paramref name="retirarAnteriores"/>, deja de publicar las viejas.</summary>
    public void Rotar(bool retirarAnteriores = false)
    {
        if (retirarAnteriores)
        {
            _llaves.Clear();
        }
        _llaves.Add(($"kid-{Guid.NewGuid():N}", RSA.Create(2048)));
    }

    /// <summary>Claims de un push a <paramref name="rama"/> de <paramref name="repositorio"/> con el workflow oficial.</summary>
    public static Dictionary<string, object> Claims(string repositorio = "flitsas-demo/api-demo", string rama = "develop", string evento = "push",
        string workflow = ".github/workflows/ci.yml", string? jobWorkflow = WorkflowOficial)
    {
        var dueno = repositorio.Split('/')[0];
        var claims = new Dictionary<string, object>
        {
            ["sub"] = $"repo:{repositorio}:ref:refs/heads/{rama}",
            ["repository"] = repositorio,
            ["repository_owner"] = dueno,
            ["ref"] = $"refs/heads/{rama}",
            ["ref_type"] = "branch",
            ["sha"] = "0123456789abcdef0123456789abcdef01234567",
            ["workflow_ref"] = $"{repositorio}/{workflow}@refs/heads/{rama}",
            ["event_name"] = evento,
            ["run_id"] = "1234567890",
            ["run_attempt"] = "1",
        };
        if (jobWorkflow is not null)
        {
            claims["job_workflow_ref"] = jobWorkflow;
        }
        return claims;
    }

    /// <summary>Un token firmado con la llave actual (o con una ajena), emitido en <paramref name="emitido"/> y válido <paramref name="vida"/>.</summary>
    public string Token(DateTimeOffset emitido, Dictionary<string, object>? claims = null, TimeSpan? vida = null, string audiencia = Audiencia,
        string emisor = Emisor, bool llaveAjena = false, DateTimeOffset? noAntes = null)
    {
        var todos = new Dictionary<string, object>(claims ?? Claims())
        {
            ["iat"] = emitido.ToUnixTimeSeconds(),
            ["nbf"] = (noAntes ?? emitido).ToUnixTimeSeconds(),
            ["exp"] = (emitido + (vida ?? TimeSpan.FromMinutes(5))).ToUnixTimeSeconds(),
        };
        var (kid, llave) = _llaves[^1];
        var firma = llaveAjena ? RSA.Create(2048) : llave;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            Claims = todos,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(firma) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>Un token sin firma (<c>alg: none</c>), que nunca debe valer.</summary>
    public static string SinFirma(DateTimeOffset emitido)
    {
        static string B64(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var claims = new Dictionary<string, object>(Claims())
        {
            ["iss"] = Emisor, ["aud"] = Audiencia, ["iat"] = emitido.ToUnixTimeSeconds(), ["exp"] = emitido.AddMinutes(5).ToUnixTimeSeconds(),
        };
        return $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(JsonSerializer.Serialize(claims))}.";
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri != $"{Emisor}/.well-known/jwks")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        PedidosJwks++;
        if (Caido)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
        var llaves = _llaves.Select(l =>
        {
            var p = l.Llave.ExportParameters(includePrivateParameters: false);
            return new Dictionary<string, string>
            {
                ["kty"] = "RSA", ["use"] = "sig", ["alg"] = "RS256", ["kid"] = l.Kid,
                ["n"] = Base64UrlEncoder.Encode(p.Modulus), ["e"] = Base64UrlEncoder.Encode(p.Exponent),
            };
        });
        var json = JsonSerializer.Serialize(new { keys = llaves });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (_, llave) in _llaves)
            {
                llave.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
