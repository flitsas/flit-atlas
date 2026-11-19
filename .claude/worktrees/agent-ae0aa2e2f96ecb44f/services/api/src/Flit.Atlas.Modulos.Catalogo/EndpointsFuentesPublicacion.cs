using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>Reglas de una fuente de publicación, compartidas por la API y la semilla.</summary>
public static partial class ReglasFuente
{
    /// <summary>Valida y deja los errores en <paramref name="errores"/> (las claves son los campos de la API).</summary>
    public static void Validar(
        string? repositorio, IReadOnlyList<string>? ramas, string? workflow, IReadOnlyList<string>? tipos, string? nota,
        Dictionary<string, string[]> errores)
    {
        ArgumentNullException.ThrowIfNull(errores);
        var repo = (repositorio ?? string.Empty).Trim().ToLowerInvariant();
        if (!Valores.RepoValido(repo))
        {
            errores["repositorio"] = ["El repositorio va como dueño/nombre (por ejemplo flitsas/flit)."];
        }
        var listaRamas = Valores.Limpiar(ramas, minusculas: false);
        if (listaRamas.Count is 0 or > CatalogoDbContext.MaximoRamas || listaRamas.Any(r => !FormatoRama().IsMatch(r)))
        {
            errores["ramas"] = [$"Entre 1 y {CatalogoDbContext.MaximoRamas} ramas (letras, números, «-», «_», «.», «/» y «*» como comodín; hasta {CatalogoDbContext.LargoRama} caracteres)."];
        }
        if (!string.IsNullOrWhiteSpace(workflow) && !FormatoWorkflow().IsMatch(workflow.Trim()))
        {
            errores["workflow"] = ["El workflow es la ruta del archivo en el repo: .github/workflows/<nombre>.yml (o vacío para cualquiera)."];
        }
        var listaTipos = Valores.Limpiar(tipos, minusculas: true);
        if (listaTipos.Count == 0 || listaTipos.Any(t => !CatalogoDbContext.TiposFuente.Contains(t)))
        {
            errores["tipos"] = [$"Al menos un tipo: {string.Join(", ", CatalogoDbContext.TiposFuente)}."];
        }
        if ((nota?.Length ?? 0) > CatalogoDbContext.LargoNota)
        {
            errores["nota"] = [$"La nota va hasta {CatalogoDbContext.LargoNota} caracteres."];
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.*/-]{1,100}$")]
    private static partial Regex FormatoRama();

    [GeneratedRegex(@"^\.github/workflows/[A-Za-z0-9_.-]+\.ya?ml$")]
    private static partial Regex FormatoWorkflow();
}

/// <summary>
/// Administración de la lista blanca de publicación desde la CI en <c>/api/v1/catalogo/fuentes-publicacion</c> (HU 3.1,
/// ATLAS-ADR-0035). Es configuración de seguridad: verla y cambiarla exige <c>atlas.catalogo.gestionar</c>, y cada cambio
/// queda en <c>plataforma_auditoria</c> en la misma transacción.
/// </summary>
internal static class EndpointsFuentesPublicacion
{
    public const string AccionCreada = "catalogo.fuente_publicacion_creada";
    public const string AccionActualizada = "catalogo.fuente_publicacion_actualizada";
    public const string AccionEliminada = "catalogo.fuente_publicacion_eliminada";
    public const string RecursoAuditoria = "catalogo.fuente_publicacion";

    [Description("Fuente de la lista blanca: qué repo puede publicar qué tipos de fuente de un aplicativo desde la CI.")]
    public sealed record FuentePublicacionDto(
        Guid Id,
        [property: Description("Aplicativo del catálogo al que pertenece lo publicado.")] EndpointsCatalogo.ReferenciaAplicativo Aplicativo,
        [property: Description("Repositorio de GitHub como dueño/nombre, en minúsculas.")] string Repositorio,
        [property: Description("Ramas que pueden publicar, exactas o con * (release, hotfix/*).")] IReadOnlyList<string> Ramas,
        [property: Description("Ruta del workflow que llama (.github/workflows/<nombre>.yml); vacío: cualquiera del repo.")] string? Workflow,
        [property: Description("openapi, asyncapi, proto, likec4 o docs.")] IReadOnlyList<string> Tipos,
        string? Nota,
        DateTimeOffset CreadoEn,
        DateTimeOffset ActualizadoEn);

    [Description("Datos de una fuente de publicación (al editar se reemplazan todos).")]
    public sealed record SolicitudFuentePublicacion(
        [property: Description("Slug del aplicativo del catálogo."), MinLength(1), MaxLength(CatalogoDbContext.LargoSlug)] string Aplicativo,
        [property: Description("Repositorio de GitHub como dueño/nombre (flitsas/flit)."), MinLength(1), MaxLength(CatalogoDbContext.LargoRepo)] string Repositorio,
        [property: Description("Ramas que pueden publicar (1 a 10), exactas o con *.")] IReadOnlyList<string> Ramas,
        [property: Description("Ruta del workflow que llama; vacío para aceptar cualquiera del repo (el workflow reutilizable oficial se exige siempre)."), MaxLength(CatalogoDbContext.LargoWorkflow)] string? Workflow,
        [property: Description("Tipos que puede publicar: openapi, asyncapi, proto, likec4, docs.")] IReadOnlyList<string> Tipos,
        [property: Description("Por qué existe (hasta 500)."), MaxLength(CatalogoDbContext.LargoNota)] string? Nota);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        var fuentes = grupo.MapGroup("/fuentes-publicacion");

        fuentes.MapGet("", ListarAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("ListarFuentesPublicacion")
            .WithSummary("Lista blanca de publicación desde la CI")
            .WithDescription("Repos que pueden publicar contratos, documentación o LikeC4 de cada aplicativo con el token OIDC de GitHub Actions, con sus ramas, su workflow y sus tipos (ATLAS-ADR-0035). Ordenada por repo y aplicativo. Requiere atlas.catalogo.gestionar.");

        fuentes.MapPost("", CrearAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("CrearFuentePublicacion")
            .WithSummary("Agregar una fuente a la lista blanca")
            .WithDescription("Autoriza a un repo a publicar fuentes de un aplicativo desde la CI. 409 si ya hay una para el mismo aplicativo, repo y workflow. Queda en la auditoría. Requiere atlas.catalogo.gestionar.");

        fuentes.MapPut("/{id:guid}", ActualizarAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("ActualizarFuentePublicacion")
            .WithSummary("Editar una fuente de la lista blanca")
            .WithDescription("Reemplaza sus datos. Queda en la auditoría con los campos cambiados. Requiere atlas.catalogo.gestionar.");

        fuentes.MapDelete("/{id:guid}", EliminarAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("EliminarFuentePublicacion")
            .WithSummary("Revocar una fuente de la lista blanca")
            .WithDescription("Desde ese momento el repo ya no publica para ese aplicativo; lo ya publicado no cambia. La semilla no la revive. Queda en la auditoría. Requiere atlas.catalogo.gestionar.");
    }

    private static async Task<Ok<List<FuentePublicacionDto>>> ListarAsync(
        [Description("Solo las del aplicativo con este slug.")] string? aplicativo,
        CatalogoDbContext contexto, CancellationToken ct)
    {
        var consulta = contexto.FuentesPublicacion.AsNoTracking()
            .Join(contexto.Aplicativos.AsNoTracking(), f => f.AplicativoId, a => a.Id, (f, a) => new { Fuente = f, Aplicativo = a });
        if (!string.IsNullOrWhiteSpace(aplicativo))
        {
            consulta = consulta.Where(x => x.Aplicativo.Slug == aplicativo.Trim());
        }
        var filas = await consulta.OrderBy(x => x.Fuente.Repositorio).ThenBy(x => x.Aplicativo.Slug).ToListAsync(ct);
        return TypedResults.Ok(filas.Select(x => Dto(x.Fuente, x.Aplicativo)).ToList());
    }

    private static async Task<Results<Created<FuentePublicacionDto>, ValidationProblem, Conflict<EndpointsCatalogo.ErrorCatalogo>>> CrearAsync(
        [Description("La fuente nueva.")] SolicitudFuentePublicacion solicitud,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var (datos, aplicativo, errores) = await ValidarAsync(solicitud, contexto, ct);
        if (datos is null || aplicativo is null)
        {
            return TypedResults.ValidationProblem(errores);
        }
        if (await ExisteAsync(contexto, aplicativo.Id, datos.Repositorio, datos.Workflow, null, ct))
        {
            return Repetida();
        }
        var fuente = new FuentePublicacion
        {
            AplicativoId = aplicativo.Id,
            Repositorio = datos.Repositorio,
            Ramas = datos.Ramas,
            Workflow = datos.Workflow,
            Tipos = datos.Tipos,
            Nota = datos.Nota,
        };
        contexto.FuentesPublicacion.Add(fuente);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionCreada, fuente, aplicativo, null), ct);
        return TypedResults.Created($"/api/v1/catalogo/fuentes-publicacion/{fuente.Id}", Dto(fuente, aplicativo));
    }

    private static async Task<Results<Ok<FuentePublicacionDto>, ValidationProblem, NotFound, Conflict<EndpointsCatalogo.ErrorCatalogo>>> ActualizarAsync(
        [Description("Id de la fuente.")] Guid id,
        [Description("Todos los datos de la fuente, con los cambios.")] SolicitudFuentePublicacion solicitud,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var fuente = await contexto.FuentesPublicacion.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fuente is null)
        {
            return TypedResults.NotFound();
        }
        var (datos, aplicativo, errores) = await ValidarAsync(solicitud, contexto, ct);
        if (datos is null || aplicativo is null)
        {
            return TypedResults.ValidationProblem(errores);
        }
        if (await ExisteAsync(contexto, aplicativo.Id, datos.Repositorio, datos.Workflow, id, ct))
        {
            return Repetida();
        }
        var campos = new List<string>();
        void Si(bool cambia, string campo)
        {
            if (cambia) campos.Add(campo);
        }
        Si(fuente.AplicativoId != aplicativo.Id, "aplicativo");
        Si(fuente.Repositorio != datos.Repositorio, "repositorio");
        Si(!fuente.Ramas.SequenceEqual(datos.Ramas), "ramas");
        Si(fuente.Workflow != datos.Workflow, "workflow");
        Si(!fuente.Tipos.SequenceEqual(datos.Tipos), "tipos");
        Si(fuente.Nota != datos.Nota, "nota");
        fuente.AplicativoId = aplicativo.Id;
        fuente.Repositorio = datos.Repositorio;
        fuente.Ramas = datos.Ramas;
        fuente.Workflow = datos.Workflow;
        fuente.Tipos = datos.Tipos;
        fuente.Nota = datos.Nota;
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionActualizada, fuente, aplicativo, campos), ct);
        return TypedResults.Ok(Dto(fuente, aplicativo));
    }

    private static async Task<Results<NoContent, NotFound>> EliminarAsync(
        [Description("Id de la fuente.")] Guid id,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var fuente = await contexto.FuentesPublicacion.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fuente is null)
        {
            return TypedResults.NotFound();
        }
        var aplicativo = await contexto.Aplicativos.AsNoTracking().FirstAsync(a => a.Id == fuente.AplicativoId, ct);
        contexto.FuentesPublicacion.Remove(fuente);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionEliminada, fuente, aplicativo, null), ct);
        return TypedResults.NoContent();
    }

    private sealed record DatosFuente(string Repositorio, List<string> Ramas, string? Workflow, List<string> Tipos, string? Nota);

    private static async Task<(DatosFuente? Datos, Aplicativo? Aplicativo, Dictionary<string, string[]> Errores)> ValidarAsync(
        SolicitudFuentePublicacion s, CatalogoDbContext contexto, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ReglasFuente.Validar(s.Repositorio, s.Ramas, s.Workflow, s.Tipos, s.Nota, errores);
        var slug = (s.Aplicativo ?? string.Empty).Trim();
        var aplicativo = Valores.SlugValido(slug) ? await contexto.Aplicativos.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug, ct) : null;
        if (aplicativo is null)
        {
            errores["aplicativo"] = ["El aplicativo no existe en el catálogo."];
        }
        if (errores.Count > 0)
        {
            return (null, null, errores);
        }
        var nota = string.IsNullOrWhiteSpace(s.Nota) ? null : s.Nota.Trim();
        var workflow = string.IsNullOrWhiteSpace(s.Workflow) ? null : s.Workflow.Trim();
        return (new DatosFuente(s.Repositorio.Trim().ToLowerInvariant(), Valores.Limpiar(s.Ramas, minusculas: false), workflow,
            Valores.Limpiar(s.Tipos, minusculas: true), nota), aplicativo, errores);
    }

    private static Task<bool> ExisteAsync(CatalogoDbContext contexto, Guid aplicativo, string repositorio, string? workflow, Guid? excepto, CancellationToken ct) =>
        contexto.FuentesPublicacion.AnyAsync(f => f.AplicativoId == aplicativo && f.Repositorio == repositorio && f.Workflow == workflow
            && (excepto == null || f.Id != excepto), ct);

    private static Conflict<EndpointsCatalogo.ErrorCatalogo> Repetida() =>
        TypedResults.Conflict(new EndpointsCatalogo.ErrorCatalogo("fuente_repetida", "Ya hay una fuente para ese aplicativo, repositorio y workflow."));

    private static FuentePublicacionDto Dto(FuentePublicacion f, Aplicativo a) => new(
        f.Id, new EndpointsCatalogo.ReferenciaAplicativo(a.Id, a.Slug, a.Nombre), f.Repositorio, f.Ramas, f.Workflow, f.Tipos, f.Nota,
        f.CreadoEn, f.ActualizadoEn);

    private static EventoAuditoria Evento(HttpContext http, TimeProvider reloj, string accion, FuentePublicacion fuente, Aplicativo aplicativo, List<string>? campos) => new()
    {
        OcurridoEn = reloj.GetUtcNow(),
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var organizacion) ? organizacion : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = fuente.Id.ToString(),
        Ip = http.Connection.RemoteIpAddress,
        // Configuración de la empresa (repo, ramas, tipos): nunca datos de personas.
        Datos = JsonSerializer.Serialize(new
        {
            aplicativo = aplicativo.Slug, fuente.Repositorio, fuente.Ramas, fuente.Workflow, fuente.Tipos, campos,
        }, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>El cambio y su auditoría en una sola transacción, como en el resto del catálogo.</summary>
    private static async Task GuardarConAuditoriaAsync(CatalogoDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria evento, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        plataforma.Auditoria.Add(evento);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
    }
}
