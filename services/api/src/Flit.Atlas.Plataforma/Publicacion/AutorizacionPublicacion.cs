using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Publicacion;

/// <summary>Metadato de los endpoints de publicación desde la CI (lo leen el contrato OpenAPI y la prueba de autorización).</summary>
public sealed class MetadatoPublicacionCi;

/// <summary>
/// Requisito de un endpoint de publicación: un token de GitHub Actions ya validado cuyo dueño, evento, rama y workflow
/// reutilizable están permitidos en <see cref="OpcionesPublicacion"/>. La lista blanca por repo, rama, workflow,
/// aplicativo y tipo (<see cref="IFuentesPublicacion"/>) la revisa cada endpoint, que conoce el aplicativo y el tipo.
/// </summary>
public sealed class RequisitoPublicadorCi : IAuthorizationRequirement
{
    public override string ToString() => "Publicador de la CI (GitHub Actions)";
}

internal sealed partial class ManejadorPublicadorCi(IOptionsMonitor<OpcionesPublicacion> opciones, ILogger<ManejadorPublicadorCi> log)
    : AuthorizationHandler<RequisitoPublicadorCi>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RequisitoPublicadorCi requirement)
    {
        if (context.User.Identity?.AuthenticationType != ExtensionesPublicacion.Esquema)
        {
            return Task.CompletedTask;
        }
        var motivo = ListaBlancaPublicacion.MotivoRechazoGlobal(PublicadorCi.Desde(context.User), opciones.CurrentValue);
        if (motivo is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            PublicadorRechazado(log, PublicadorCi.Desde(context.User)?.Repositorio ?? "?", motivo);
            context.Fail(new AuthorizationFailureReason(this, motivo));
        }
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación desde la CI: {Repositorio} rechazado ({Motivo})")]
    private static partial void PublicadorRechazado(ILogger log, string repositorio, string motivo);
}

/// <summary>Resultado de comparar una publicación con la lista blanca.</summary>
/// <param name="Fuente">La fila que la autoriza, si la hay.</param>
/// <param name="Codigo"><c>null</c> si está autorizada; si no, un código estable para la respuesta 403.</param>
public sealed record AutorizacionFuente(FuentePublicacionLeida? Fuente, string? Codigo, string? Mensaje)
{
    public bool Autorizada => Fuente is not null;
}

/// <summary>
/// Reglas de la lista blanca (ATLAS-ADR-0009 y ATLAS-ADR-0035), como funciones puras para probarlas sin servidor. Repos y
/// workflows se comparan sin distinguir mayúsculas (GitHub no las distingue); las ramas, exactas o con <c>*</c>.
/// </summary>
public static partial class ListaBlancaPublicacion
{
    /// <summary>Lo que se revisa antes de mirar la lista blanca: dueño, evento y workflow reutilizable oficial.</summary>
    public static string? MotivoRechazoGlobal(PublicadorCi? publicador, OpcionesPublicacion opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        if (publicador is null)
        {
            return "claims_incompletos";
        }
        if (!opciones.Propietarios.Contains(publicador.Propietario, StringComparer.OrdinalIgnoreCase))
        {
            return "propietario_no_permitido";
        }
        if (!opciones.Eventos.Contains(publicador.Evento, StringComparer.Ordinal))
        {
            return "evento_no_permitido";
        }
        if (!opciones.WorkflowsReutilizables.Any(p => Coincide(p, publicador.WorkflowDelJob, distinguirMayusculas: false)))
        {
            return "workflow_reutilizable_no_oficial";
        }
        return null;
    }

    /// <summary>Las fuentes del repo que valen para la rama y el workflow del token (sin mirar aplicativo ni tipo).</summary>
    public static IReadOnlyList<FuentePublicacionLeida> DelPublicador(PublicadorCi publicador, IEnumerable<FuentePublicacionLeida> fuentes)
    {
        ArgumentNullException.ThrowIfNull(publicador);
        return
        [
            .. fuentes.Where(f => string.Equals(f.Repositorio, publicador.Repositorio, StringComparison.OrdinalIgnoreCase)
                && f.Ramas.Any(r => Coincide(r, publicador.Rama, distinguirMayusculas: true))
                && (f.Workflow is null || string.Equals(f.Workflow, publicador.Workflow, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    /// <summary>
    /// ¿Puede el publicador publicar un <paramref name="tipo"/> del aplicativo <paramref name="aplicativo"/> (slug)? Dice
    /// qué falta: repo, rama o workflow (<c>fuente_no_autorizada</c>), aplicativo (<c>aplicativo_no_autorizado</c>) o tipo
    /// (<c>tipo_no_autorizado</c>).
    /// </summary>
    public static AutorizacionFuente Autorizar(PublicadorCi publicador, IEnumerable<FuentePublicacionLeida> fuentes, string aplicativo, string tipo)
    {
        ArgumentNullException.ThrowIfNull(publicador);
        var delRepo = fuentes.Where(f => string.Equals(f.Repositorio, publicador.Repositorio, StringComparison.OrdinalIgnoreCase)).ToList();
        if (delRepo.Count == 0)
        {
            return new(null, "fuente_no_autorizada", $"El repo {publicador.Repositorio} no está en la lista blanca de publicación.");
        }
        var validas = DelPublicador(publicador, delRepo);
        if (validas.Count == 0)
        {
            return new(null, "fuente_no_autorizada",
                $"La rama «{publicador.Rama}» o el workflow «{publicador.Workflow}» de {publicador.Repositorio} no están en la lista blanca de publicación.");
        }
        var delAplicativo = validas.Where(f => string.Equals(f.AplicativoSlug, aplicativo, StringComparison.Ordinal)).ToList();
        if (delAplicativo.Count == 0)
        {
            return new(null, "aplicativo_no_autorizado", $"{publicador.Repositorio} no puede publicar para el aplicativo «{aplicativo}».");
        }
        var fuente = delAplicativo.FirstOrDefault(f => f.Tipos.Contains(tipo, StringComparer.Ordinal));
        return fuente is null
            ? new(null, "tipo_no_autorizado", $"{publicador.Repositorio} no puede publicar contratos «{tipo}» del aplicativo «{aplicativo}».")
            : new(fuente, null, null);
    }

    /// <summary>Comparación con comodín <c>*</c> (cualquier secuencia, incluso con <c>/</c>); sin comodín, exacta.</summary>
    public static bool Coincide(string patron, string valor, bool distinguirMayusculas)
    {
        ArgumentNullException.ThrowIfNull(patron);
        ArgumentNullException.ThrowIfNull(valor);
        if (!patron.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(patron, valor, distinguirMayusculas ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        var regex = "^" + string.Join(".*", patron.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(valor, regex, (distinguirMayusculas ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Regla de versiones por rama (plan 00 §8.2): las ramas estables publican sin prerelease; <c>develop</c> y
    /// <c>staging</c>, con el prerelease <c>dev.</c> y <c>rc.</c>; las demás, con cualquier prerelease. <c>null</c> si la
    /// versión vale para la rama.
    /// </summary>
    public static string? MotivoVersionPorRama(string rama, string? prerelease, OpcionesPublicacion opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        if (opciones.RamasEstables.Contains(rama, StringComparer.Ordinal))
        {
            return prerelease is null ? null : $"La rama «{rama}» publica versiones estables, sin prerelease.";
        }
        if (prerelease is null)
        {
            return $"La rama «{rama}» solo publica prereleases (las estables salen de {string.Join(", ", opciones.RamasEstables)}).";
        }
        return opciones.PrefijosPrerelease.TryGetValue(rama, out var prefijo) && !prerelease.StartsWith(prefijo + ".", StringComparison.Ordinal)
            ? $"La rama «{rama}» publica versiones X.Y.Z-{prefijo}.<sha7>."
            : null;
    }
}

public static class ExtensionesPublicacion
{
    /// <summary>Esquema de autenticación del token OIDC de GitHub Actions.</summary>
    public const string Esquema = "GitHubActions";

    /// <summary>
    /// Exige un token OIDC de GitHub Actions válido (401 si falta, venció o no es para este ambiente) de un dueño, evento
    /// y workflow reutilizable permitidos (403). Se autentica <b>solo</b> con ese esquema: la cookie de la PWA no vale
    /// aquí, y el token no vale en ningún otro endpoint. No pasa por la antifalsificación (lleva <c>Authorization:
    /// Bearer</c>, ATLAS-ADR-0024).
    /// </summary>
    public static TBuilder RequierePublicacionCi<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithMetadata(new MetadatoPublicacionCi())
            .RequireAuthorization(p => p
                .AddAuthenticationSchemes(Esquema)
                .RequireAuthenticatedUser()
                .AddRequirements(new RequisitoPublicadorCi()));
    }

    /// <summary>El publicador de la petición (solo en endpoints con <see cref="RequierePublicacionCi{TBuilder}"/>).</summary>
    public static PublicadorCi Publicador(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return PublicadorCi.Desde(http.User) ?? throw new InvalidOperationException("La petición no trae un token de GitHub Actions autorizado.");
    }

    /// <summary>
    /// Esquema, validador, llaves y requisito de la publicación desde la CI. Lo llama <c>AgregarPlataforma</c>: vale para
    /// todas las fuentes (contratos en F3, documentación en F5 y LikeC4 en F6, prompt maestro A-08).
    /// </summary>
    internal static IServiceCollection AgregarPublicacionCi(this IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesPublicacion>().BindConfiguration(OpcionesPublicacion.Seccion).PostConfigure(o => o.Normalizar());
        servicios.AddHttpClient(LlavesGitHubActions.NombreCliente, c => c.Timeout = TimeSpan.FromSeconds(10));
        servicios.TryAddSingleton<LlavesGitHubActions>();
        servicios.TryAddSingleton<ValidadorTokenGitHub>();
        servicios.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, ManejadorPublicadorCi>());
        servicios.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ManejadorGitHubActions>(Esquema, "GitHub Actions (OIDC)", null);
        return servicios;
    }
}
