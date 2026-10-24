using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Atlas.Plataforma.Publicacion;

/// <summary>
/// Claims del token OIDC de GitHub Actions que usa Atlas (<see href="https://docs.github.com/actions/security-for-github-actions/security-hardening-your-deployments/about-security-hardening-with-openid-connect"/>).
/// </summary>
public static class ClaimsGitHub
{
    public const string Repositorio = "repository";
    public const string Propietario = "repository_owner";
    public const string Ref = "ref";
    public const string TipoRef = "ref_type";
    public const string Sha = "sha";
    public const string Workflow = "workflow_ref";
    public const string WorkflowDelJob = "job_workflow_ref";
    public const string Evento = "event_name";
    public const string Ejecucion = "run_id";
    public const string Intento = "run_attempt";
}

/// <summary>
/// Valida el token OIDC de GitHub Actions (ATLAS-ADR-0035): firma RS256 con las llaves de <see cref="LlavesGitHubActions"/>,
/// emisor, audiencia del ambiente y vigencia (<c>exp</c>, <c>nbf</c>, <c>iat</c>) con el reloj de Atlas
/// (<see cref="TimeProvider"/>) y su margen. No mira repo, rama ni workflow: eso es autorización
/// (<see cref="RequisitoPublicadorCi"/>) y lista blanca.
/// </summary>
public sealed class ValidadorTokenGitHub(
    LlavesGitHubActions llaves, IOptionsMonitor<OpcionesPublicacion> opciones, IConfiguration configuracion, TimeProvider reloj)
{
    /// <summary>Un token de GitHub pesa ~1 KB; más que esto no es uno.</summary>
    public const int LargoMaximo = 16 * 1024;

    private static readonly JsonWebTokenHandler Manejador = new() { MapInboundClaims = false };

    /// <summary>El resultado: la identidad del token o el motivo del rechazo (para el log; nunca el token).</summary>
    public sealed record Resultado(ClaimsIdentity? Identidad, string? Motivo);

    public async Task<Resultado> ValidarAsync(string token, CancellationToken ct = default)
    {
        var o = opciones.CurrentValue;
        var audiencia = o.AudienciaEfectiva(configuracion[OpcionesPublicacion.ClaveUrlPublica]);
        if (audiencia is null)
        {
            return new(null, "sin_audiencia_configurada");
        }
        if (token.Length > LargoMaximo || !Manejador.CanReadToken(token))
        {
            return new(null, "token_ilegible");
        }
        var jwt = Manejador.ReadJsonWebToken(token);
        var vigentes = await llaves.ObtenerAsync(jwt.Kid, ct).ConfigureAwait(false);
        if (vigentes.Count == 0)
        {
            return new(null, "sin_llaves_de_github");
        }

        var parametros = new TokenValidationParameters
        {
            ValidIssuer = o.Emisor,
            ValidAudience = audiencia,
            IssuerSigningKeys = vigentes,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            // La vigencia se revisa abajo con el reloj de Atlas (TimeProvider), no con el del sistema.
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            TryAllIssuerSigningKeys = false,
        };
        var resultado = await Manejador.ValidateTokenAsync(token, parametros).ConfigureAwait(false);
        if (!resultado.IsValid)
        {
            return new(null, resultado.Exception?.GetType().Name ?? "token_invalido");
        }

        var motivo = MotivoVigencia(jwt, reloj.GetUtcNow(), o.MargenReloj, o.VidaMaximaToken);
        return motivo is null ? new(resultado.ClaimsIdentity, null) : new(null, motivo);
    }

    /// <summary><c>null</c> si el token está vigente; si no, por qué.</summary>
    internal static string? MotivoVigencia(JsonWebToken jwt, DateTimeOffset ahora, TimeSpan margen, TimeSpan vidaMaxima)
    {
        if (!jwt.TryGetPayloadValue<long>("exp", out var exp))
        {
            return "sin_exp";
        }
        if (!jwt.TryGetPayloadValue<long>("iat", out var iat))
        {
            return "sin_iat";
        }
        var vence = DateTimeOffset.FromUnixTimeSeconds(exp);
        var emitido = DateTimeOffset.FromUnixTimeSeconds(iat);
        if (ahora >= vence + margen)
        {
            return "token_vencido";
        }
        if (jwt.TryGetPayloadValue<long>("nbf", out var nbf) && ahora < DateTimeOffset.FromUnixTimeSeconds(nbf) - margen)
        {
            return "token_aun_no_vale";
        }
        if (emitido > ahora + margen)
        {
            return "token_emitido_en_el_futuro";
        }
        if (vence - emitido > vidaMaxima)
        {
            return "token_de_vida_demasiado_larga";
        }
        return null;
    }
}

/// <summary>
/// Esquema de autenticación <see cref="ExtensionesPublicacion.Esquema"/>: <c>Authorization: Bearer &lt;token de GitHub&gt;</c>.
/// Solo lo usan los endpoints con <see cref="ExtensionesPublicacion.RequierePublicacionCi{TBuilder}"/>; nunca abre la sesión
/// del BFF ni vale en otro endpoint (el esquema por defecto sigue siendo la cookie).
/// </summary>
internal sealed partial class ManejadorGitHubActions(
    IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory logs, UrlEncoder codificador, ValidadorTokenGitHub validador)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logs, codificador)
{
    private const string Prefijo = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cabecera = Request.Headers.Authorization.ToString();
        if (!cabecera.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }
        var resultado = await validador.ValidarAsync(cabecera[Prefijo.Length..].Trim(), Context.RequestAborted).ConfigureAwait(false);
        if (resultado.Identidad is null)
        {
            TokenRechazado(Logger, resultado.Motivo ?? "desconocido");
            return AuthenticateResult.Fail(resultado.Motivo ?? "token_invalido");
        }
        var identidad = new ClaimsIdentity(resultado.Identidad.Claims, Scheme.Name, ClaimsGitHub.Repositorio, null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación desde la CI: token de GitHub Actions rechazado ({Motivo})")]
    private static partial void TokenRechazado(ILogger log, string motivo);
}

/// <summary>Lo que dice el token sobre quién publica (ya validado), para la lista blanca y la auditoría.</summary>
/// <param name="Repositorio"><c>dueño/nombre</c>, en minúsculas.</param>
/// <param name="Rama">Nombre de la rama (sin <c>refs/heads/</c>).</param>
/// <param name="Workflow">Ruta del workflow que llamó (<c>.github/workflows/ci.yml</c>), sacada de <c>workflow_ref</c>.</param>
/// <param name="WorkflowDelJob"><c>job_workflow_ref</c>: el workflow reutilizable que corre el job.</param>
public sealed record PublicadorCi(
    string Repositorio, string Propietario, string Rama, string Commit, string? Workflow, string WorkflowDelJob, string Evento,
    string? Ejecucion, string? Intento)
{
    /// <summary><c>github:dueño/repo@rama</c> (plan 00 §8.4, <c>publicado_por</c>).</summary>
    public string Firma => $"github:{Repositorio}@{Rama}";

    /// <summary>Lee los claims del token; <c>null</c> si falta alguno obligatorio o la referencia no es una rama.</summary>
    public static PublicadorCi? Desde(ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        string? Valor(string tipo) => usuario.FindFirst(tipo)?.Value is { Length: > 0 } v ? v : null;
        var repositorio = Valor(ClaimsGitHub.Repositorio)?.ToLowerInvariant();
        var propietario = Valor(ClaimsGitHub.Propietario)?.ToLowerInvariant();
        var referencia = Valor(ClaimsGitHub.Ref);
        var sha = Valor(ClaimsGitHub.Sha);
        var deljob = Valor(ClaimsGitHub.WorkflowDelJob);
        var evento = Valor(ClaimsGitHub.Evento);
        if (repositorio is null || propietario is null || referencia is null || sha is null || deljob is null || evento is null
            || !referencia.StartsWith("refs/heads/", StringComparison.Ordinal)
            || (Valor(ClaimsGitHub.TipoRef) is { } tipo && tipo != "branch")
            || !repositorio.StartsWith(propietario + "/", StringComparison.Ordinal))
        {
            return null;
        }
        return new PublicadorCi(repositorio, propietario, referencia["refs/heads/".Length..], sha.ToLowerInvariant(),
            RutaWorkflow(Valor(ClaimsGitHub.Workflow), repositorio), deljob, evento, Valor(ClaimsGitHub.Ejecucion), Valor(ClaimsGitHub.Intento));
    }

    /// <summary><c>dueño/repo/.github/workflows/ci.yml@refs/heads/develop</c> → <c>.github/workflows/ci.yml</c>.</summary>
    internal static string? RutaWorkflow(string? workflowRef, string repositorio)
    {
        if (workflowRef is null)
        {
            return null;
        }
        var arroba = workflowRef.LastIndexOf('@');
        var sinRef = arroba < 0 ? workflowRef : workflowRef[..arroba];
        var prefijo = repositorio + "/";
        return sinRef.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? sinRef[prefijo.Length..] : null;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Firma} ({Commit[..Math.Min(7, Commit.Length)]})");
}
