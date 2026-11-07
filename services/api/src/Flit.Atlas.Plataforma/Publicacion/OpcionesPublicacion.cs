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
    /// Id numérico de cada dueño (<c>repository_owner_id</c>), como <c>nombre=id</c> (F4-SEC-27): un dueño de la lista con id
    /// solo vale si el token trae ese id, así una organización o usuario borrado cuyo nombre registra otro no publica. Vacía:
    /// <c>flitsas=284087596</c> (dato público de la API de GitHub).
    /// </summary>
    public List<string> IdsPropietarios { get; set; } = [];

    /// <summary>
    /// Id numérico de repos fijados (<c>repository_id</c>), como <c>dueño/nombre=id</c> (F4-SEC-27): un repo fijado solo vale
    /// si el token trae ese id, así un repo borrado y vuelto a crear con el mismo nombre (o uno renombrado que libera el
    /// nombre) no publica. Los repos sin id fijado se revisan por nombre y por el id del dueño. Vacía: <c>flitsas/flit</c> y
    /// <c>flitsas/flit-atlas</c>, los que hoy publican.
    /// </summary>
    public List<string> IdsRepositorios { get; set; } = [];

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

    /// <summary>
    /// Autopublicación (HU 3.3, ATLAS-ADR-0035 §7): el repo que aloja un workflow reutilizable oficial (flit-atlas) puede
    /// correrlo desde la misma rama que publica (<c>uses: ./.github/workflows/atlas-publicar.yml</c>, que GitHub reporta
    /// como <c>…/atlas-publicar.yml@refs/heads/&lt;rama&gt;</c>), no solo desde un tag. Vale solo si la referencia del
    /// workflow es exactamente la rama del token: el workflow que corre es el del commit que se publica, y esa rama sigue
    /// en la lista blanca. Se apaga con <c>Plataforma__Publicacion__AutopublicacionDesdeRama=false</c>.
    /// </summary>
    public bool AutopublicacionDesdeRama { get; set; } = true;

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

    /// <summary>El workflow reutilizable oficial de la documentación en cualquier tag <c>v*</c> (HU 5.2, ATLAS-ADR-0053 §4).</summary>
    public const string WorkflowOficialDocumentacion = "flitsas/flit-atlas/.github/workflows/atlas-publicar-docs.yml@refs/tags/v*";

    /// <summary>
    /// Pone los valores por defecto de las listas vacías. Se hace después de leer la configuración porque el enlazador
    /// de .NET agrega los elementos configurados a los de una lista ya llena en lugar de reemplazarlos.
    /// </summary>
    public void Normalizar()
    {
        if (Propietarios.Count == 0) Propietarios.Add("flitsas");
        if (IdsPropietarios.Count == 0) IdsPropietarios.Add("flitsas=284087596");
        if (IdsRepositorios.Count == 0) IdsRepositorios.AddRange(["flitsas/flit=1244856694", "flitsas/flit-atlas=1428322754"]);
        if (Eventos.Count == 0) Eventos.AddRange(["push", "workflow_dispatch"]);
        if (WorkflowsReutilizables.Count == 0) WorkflowsReutilizables.AddRange([WorkflowOficial, WorkflowOficialDocumentacion]);
        if (RamasEstables.Count == 0) RamasEstables.Add("release");
    }

    /// <summary>El id fijado para <paramref name="nombre"/> en una lista <c>nombre=id</c>, o <c>null</c> si no hay.</summary>
    public static string? IdFijado(IEnumerable<string> lista, string nombre)
    {
        ArgumentNullException.ThrowIfNull(lista);
        foreach (var entrada in lista)
        {
            var igual = entrada.LastIndexOf('=');
            if (igual > 0 && string.Equals(entrada[..igual].Trim(), nombre, StringComparison.OrdinalIgnoreCase))
            {
                return entrada[(igual + 1)..].Trim();
            }
        }
        return null;
    }

    /// <summary>Ambientes desplegados donde la audiencia debe ser la URL pública (F4-SEC-27).</summary>
    public static readonly string[] AmbientesConAudienciaFija = ["qa", "pdn"];

    /// <summary>
    /// En qa y pdn la audiencia es la URL pública del ambiente, la misma a la que el workflow envía el token (F4-SEC-27): un
    /// <c>Plataforma__Publicacion__Audiencia</c> distinto haría que un token pedido para otra URL valga aquí. Devuelve por
    /// qué la configuración no sirve, o <c>null</c>. En local y dev (pruebas, AppHost) se admite otra audiencia.
    /// </summary>
    public string? MotivoAudienciaIncoherente(string? urlPublica, string? ambiente)
    {
        if (!AmbientesConAudienciaFija.Contains(ambiente, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(Audiencia))
        {
            return null;
        }
        var url = string.IsNullOrWhiteSpace(urlPublica) ? null : urlPublica.Trim().TrimEnd('/');
        return string.Equals(Audiencia.Trim().TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Plataforma__Publicacion__Audiencia debe ir vacía o igual a Atlas__UrlPublica en {ambiente}: el workflow pide el token " +
              "para la URL a la que lo envía (F4-SEC-27).";
    }

    /// <summary>La audiencia efectiva: la configurada o la URL pública del ambiente.</summary>
    public string? AudienciaEfectiva(string? urlPublica)
    {
        var valor = string.IsNullOrWhiteSpace(Audiencia) ? urlPublica : Audiencia;
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().TrimEnd('/');
    }

    public string UrlLlavesEfectiva => string.IsNullOrWhiteSpace(UrlLlaves) ? $"{Emisor.TrimEnd('/')}/.well-known/jwks" : UrlLlaves;
}
