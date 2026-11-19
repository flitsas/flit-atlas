namespace Flit.Atlas.Plataforma.Publicacion;

/// <summary>
/// Publicación desde la CI con el token OIDC de GitHub Actions (prompt maestro A-08, ATLAS-ADR-0009 y ATLAS-ADR-0035).
/// Sección <c>Plataforma:Publicacion</c>. Lo único que cambia por ambiente es la audiencia, y por defecto sale de
/// <c>Atlas:UrlPublica</c>: no hay secretos que configurar.
/// </summary>
public sealed class OpcionesPublicacion
{
    public const string Seccion = "Plataforma:Publicacion";

    /// <summary>Clave de la URL pública del ambiente; la audiencia por defecto (la misma de <c>OpcionesExternos</c>).</summary>
    public const string ClaveUrlPublica = "Atlas:UrlPublica";

    /// <summary>Emisor de los tokens de GitHub Actions.</summary>
    public string Emisor { get; set; } = "https://token.actions.githubusercontent.com";

    /// <summary>Llaves públicas del emisor. Vacío: <c>&lt;Emisor&gt;/.well-known/jwks</c>.</summary>
    public string? UrlLlaves { get; set; }

    /// <summary>
    /// Audiencia que debe traer el token (<c>aud</c>): el workflow la pide con <c>core.getIDToken(audiencia)</c>. Una por
    /// ambiente, para que un token de dev no sirva en pdn. Vacía: <c>Atlas:UrlPublica</c> sin la barra final
    /// (<c>https://atlas.flitsas.com</c>). Sin audiencia, ningún token vale.
    /// </summary>
    public string? Audiencia { get; set; }

    /// <summary>Dueños de repositorio aceptados (<c>repository_owner</c>), sin distinguir mayúsculas. Vacía: <c>flitsas</c>.</summary>
    public List<string> Propietarios { get; set; } = [];

    /// <summary>
    /// Eventos de GitHub que pueden publicar (<c>event_name</c>). Nunca <c>pull_request</c>: el código de un PR (incluso de
    /// un fork) aún no está revisado. Vacía: <c>push</c> y <c>workflow_dispatch</c>.
    /// </summary>
    public List<string> Eventos { get; set; } = [];

    /// <summary>
    /// Workflows reutilizables oficiales (<c>job_workflow_ref</c>): <c>dueño/repo/ruta@ref</c>, con <c>*</c> como comodín.
    /// Debe ser una referencia fijada (tag o sha) para que un repo no publique con un workflow propio ni con uno que cambie
    /// debajo (ATLAS-ADR-0009). Vacía: <see cref="WorkflowOficial"/>.
    /// </summary>
    public List<string> WorkflowsReutilizables { get; set; } = [];

    /// <summary>Ramas que publican versiones estables (sin prerelease). Vacía: <c>release</c>, que despliega en pdn.</summary>
    public List<string> RamasEstables { get; set; } = [];

    /// <summary>
    /// Prefijo de prerelease obligatorio por rama (plan 00 §8.2): <c>develop</c> publica <c>X.Y.Z-dev.&lt;sha7&gt;</c> y
    /// <c>staging</c> <c>X.Y.Z-rc.&lt;sha7&gt;</c>. Una rama que no esté aquí ni en <see cref="RamasEstables"/> solo
    /// publica prereleases.
    /// </summary>
    public Dictionary<string, string> PrefijosPrerelease { get; set; } = new(StringComparer.Ordinal)
    {
        ["develop"] = "dev",
        ["staging"] = "rc",
    };

    /// <summary>Tolerancia del reloj para <c>exp</c>, <c>nbf</c> e <c>iat</c>.</summary>
    public TimeSpan MargenReloj { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Vida máxima aceptada de un token (<c>exp</c> − <c>iat</c>); los de GitHub viven minutos.</summary>
    public TimeSpan VidaMaximaToken { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Cada cuánto se vuelven a pedir las llaves aunque no haya un <c>kid</c> nuevo.</summary>
    public TimeSpan RefrescoLlaves { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Espera mínima entre dos pedidos de llaves (un <c>kid</c> desconocido o GitHub caído): un token inventado no puede
    /// hacer que Atlas consulte a GitHub en cada petición.
    /// </summary>
    public TimeSpan EsperaMinimaLlaves { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>El workflow reutilizable oficial en cualquier tag <c>v*</c> (HU 3.3).</summary>
    public const string WorkflowOficial = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v*";

    /// <summary>
    /// Pone los valores por defecto de las listas vacías. Se hace después de leer la configuración porque el enlazador
    /// de .NET agrega los elementos configurados a los de una lista ya llena en lugar de reemplazarlos.
    /// </summary>
    public void Normalizar()
    {
        if (Propietarios.Count == 0) Propietarios.Add("flitsas");
        if (Eventos.Count == 0) Eventos.AddRange(["push", "workflow_dispatch"]);
        if (WorkflowsReutilizables.Count == 0) WorkflowsReutilizables.Add(WorkflowOficial);
        if (RamasEstables.Count == 0) RamasEstables.Add("release");
    }

    /// <summary>La audiencia efectiva: la configurada o la URL pública del ambiente.</summary>
    public string? AudienciaEfectiva(string? urlPublica)
    {
        var valor = string.IsNullOrWhiteSpace(Audiencia) ? urlPublica : Audiencia;
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().TrimEnd('/');
    }

    public string UrlLlavesEfectiva => string.IsNullOrWhiteSpace(UrlLlaves) ? $"{Emisor.TrimEnd('/')}/.well-known/jwks" : UrlLlaves;
}
