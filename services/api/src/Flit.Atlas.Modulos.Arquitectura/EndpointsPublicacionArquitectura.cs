using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Modulos.Arquitectura.Drift;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>
/// <c>POST /api/v1/arquitectura/publicaciones</c> (plan 01 §10.2, ATLAS-ADR-0056 §2): un modelo de LikeC4 o un snapshot de despliegue,
/// solo con el token OIDC de GitHub Actions. El sha, la rama y el repo salen del token, nunca del cuerpo. El servidor valida todo otra
/// vez y nunca confía en lo que revisó la CI.
/// </summary>
internal static partial class EndpointsPublicacionArquitectura
{
    /// <summary>El catálogo solo conoce <c>likec4</c>: los snapshots se autorizan con este tipo y además con <c>RepositoriosDespliegue</c>.</summary>
    public const string TipoFuente = "likec4";

    public const string RecursoAuditoria = "arquitectura.publicacion";
    public const string AccionPublicada = "arquitectura.publicacion_creada";

    private const long CuerpoMaximo = 16 * 1024 * 1024;

    private static readonly HashSet<string> Canales = new(["dev", "qa", "pdn"], StringComparer.Ordinal);

    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => CuerpoMaximo;
    }

    [Description("HerramientaArquitectura con la que se generó.")]
    public sealed record HerramientaArquitectura(string? Nombre, string? Version);

    [Description("Lo que publica un commit.")]
    public sealed record SolicitudPublicacionArquitectura(
        [property: Description("Código del proyecto (flit, flito, landscape, flit-gitops). Lo publica siempre el mismo repo.")] string? Proyecto,
        [property: Description("Slug del aplicativo en el catálogo; debe estar autorizado para el repo con el tipo likec4.")] string? Aplicativo,
        [property: Description("modelo o despliegue.")] string? Tipo,
        [property: Description("interno_ti (por defecto) o restringido.")] string? Visibilidad,
        [property: Description("arq-vMAJOR.MINOR.PATCH, opcional. La versión que cuenta es el sha.")] string? VersionPropuesta,
        DateTimeOffset? GeneradoEn,
        HerramientaArquitectura? Herramienta,
        [property: Description("Solo en despliegue: dev, qa o pdn.")] string? Ambiente,
        [property: Description("Solo en despliegue: id del clúster.")] string? Cluster,
        [property: Description("Solo en modelo: la salida de likec4 export json de un proyecto, con su layout.")] JsonElement? Modelo,
        [property: Description("Solo en despliegue: el snapshot del generador.")] SnapshotCrudo? Snapshot);

    /// <summary>El snapshot se valida a mano para responder 422 con la ruta del campo, no un 400 genérico.</summary>
    [Description("El snapshot, tal como lo escribe tools/arquitectura-gen.")]
    [System.Text.Json.Serialization.JsonConverter(typeof(ConvertidorSnapshotCrudo))]
    public sealed record SnapshotCrudo(JsonElement Valor);

    private sealed class ConvertidorSnapshotCrudo : System.Text.Json.Serialization.JsonConverter<SnapshotCrudo>
    {
        public override SnapshotCrudo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonElement.ParseValue(ref reader));

        public override void Write(Utf8JsonWriter writer, SnapshotCrudo value, JsonSerializerOptions options) => value.Valor.WriteTo(writer);
    }

    [Description("Resultado de una publicación.")]
    public sealed record ResultadoPublicacionArquitectura(
        Guid PublicacionId,
        [property: Description("creada o igual (el mismo sha con el mismo contenido: no crea nada).")] string Resultado,
        string Canal,
        string CommitSha,
        string? Version,
        [property: Description("borrador (desde una rama distinta de release) o publicado.")] string Estado,
        [property: Description("Cuántos cambios hay frente a la publicación anterior del canal; null si es la primera.")] ResumenDiffArquitectura? Diff);

    [Description("Cuántas altas, bajas y cambios.")]
    public sealed record ResumenDiffArquitectura(int Altas, int Bajas, int Cambios);

    [Description("Por qué no se publicó, con un código estable.")]
    public sealed record ErrorPublicacionArquitectura(
        [property: Description("fuente_no_autorizada, aplicativo_no_autorizado, tipo_no_autorizado, proyecto_de_otro_repo o despliegue_no_autorizado (403); sha_con_otro_contenido (409); rama_sin_ambiente, modelo_invalido, snapshot_invalido o contenido_sensible (422).")] string Codigo,
        string Mensaje,
        IReadOnlyList<string>? Errores = null);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/publicaciones", PublicarAsync)
            .RequierePublicacionCi()
            .WithMetadata(new LimiteCuerpo())
            .WithName("PublicarArquitectura")
            .WithSummary("Publicar un modelo de LikeC4 o un snapshot de despliegue desde la CI")
            .WithDescription(
                "Solo con el token OIDC de GitHub Actions del workflow reutilizable oficial, desde un repo, rama y workflow de la lista " +
                "blanca con el tipo likec4. Un snapshot exige además que el repo esté en Arquitectura__RepositoriosDespliegue. El sha sale " +
                "del token. Responde 201 con la versión nueva, 200 si el mismo sha ya publicó ese mismo contenido, y 409 si lo publicó con " +
                "otro: las versiones son inmutables. Se rechaza con 422 lo que parezca una IP o un secreto, sin citar el valor. La cookie de " +
                "la PWA no sirve aquí.")
            .Produces<ResultadoPublicacionArquitectura>(StatusCodes.Status201Created)
            .Produces<ResultadoPublicacionArquitectura>()
            .Produces<ErrorPublicacionArquitectura>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPublicacionArquitectura>(StatusCodes.Status409Conflict)
            .Produces<ErrorPublicacionArquitectura>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();
    }

    private sealed record Contenido(string Texto, string Sha256, ModeloLeido? Modelo, string Canal, string Cluster, DateTimeOffset? GeneradoEn);

    private static IResult Rechazo(int estado, string codigo, string mensaje, IReadOnlyList<string>? errores = null) =>
        Results.Json(new ErrorPublicacionArquitectura(codigo, mensaje, errores), statusCode: estado);

    private static async Task<IResult> PublicarAsync(
        [Description("Lo que se publica.")] SolicitudPublicacionArquitectura solicitud,
        HttpContext http, ArquitecturaDbContext contexto, PlataformaDbContext plataforma, IFuentesPublicacion fuentes,
        IOptions<OpcionesArquitectura> opciones, DisparadorDrift disparador, TimeProvider reloj, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(typeof(EndpointsPublicacionArquitectura));
        var publicador = http.Publicador();

        var delRepo = await fuentes.DelRepositorioAsync(publicador.Repositorio, ct);
        var validas = ListaBlancaPublicacion.DelPublicador(publicador, delRepo).Where(f => f.Tipos.Contains(TipoFuente, StringComparer.Ordinal)).ToList();
        if (validas.Count == 0)
        {
            Rechazado(log, publicador.Repositorio, "fuente_no_autorizada");
            return Rechazo(StatusCodes.Status403Forbidden, "fuente_no_autorizada",
                $"{publicador.Repositorio} no puede publicar arquitectura desde la rama «{publicador.Rama}» con este workflow.");
        }

        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (solicitud.Proyecto is null || !ReglasArquitectura.FormatoCodigo().IsMatch(solicitud.Proyecto))
        {
            errores["proyecto"] = ["Usa minúsculas, números y guiones."];
        }
        if (string.IsNullOrWhiteSpace(solicitud.Aplicativo))
        {
            errores["aplicativo"] = ["Di el aplicativo del catálogo."];
        }
        if (solicitud.Tipo is not ("modelo" or "despliegue"))
        {
            errores["tipo"] = ["Debe ser modelo o despliegue."];
        }
        var visibilidad = ReglasArquitectura.LeerVisibilidad(solicitud.Visibilidad);
        if (visibilidad is null)
        {
            errores["visibilidad"] = ["Debe ser interno_ti o restringido: lo de los clientes no se publica por aquí."];
        }
        if (solicitud.VersionPropuesta is { } propuesta && !ReglasArquitectura.FormatoVersion().IsMatch(propuesta))
        {
            errores["versionPropuesta"] = ["Debe ser arq-vMAJOR.MINOR.PATCH."];
        }
        if (solicitud.Herramienta?.Version is { } version && !ReglasArquitectura.FormatoVersionHerramienta().IsMatch(version))
        {
            errores["herramienta"] = ["La versión de la herramienta no es válida."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var autorizacion = ListaBlancaPublicacion.Autorizar(publicador, delRepo, solicitud.Aplicativo!, TipoFuente);
        if (!autorizacion.Autorizada)
        {
            Rechazado(log, publicador.Repositorio, autorizacion.Codigo!);
            return Rechazo(StatusCodes.Status403Forbidden, autorizacion.Codigo!, autorizacion.Mensaje!);
        }
        var tipo = solicitud.Tipo == "modelo" ? TipoPublicacion.Modelo : TipoPublicacion.Despliegue;
        if (tipo == TipoPublicacion.Despliegue && !opciones.Value.RepositoriosDespliegue.Contains(publicador.Repositorio, StringComparer.OrdinalIgnoreCase))
        {
            Rechazado(log, publicador.Repositorio, "despliegue_no_autorizado");
            return Rechazo(StatusCodes.Status403Forbidden, "despliegue_no_autorizado", $"{publicador.Repositorio} no puede publicar snapshots de despliegue.");
        }

        var (contenido, fallo) = tipo == TipoPublicacion.Modelo
            ? LeerModelo(solicitud, publicador, opciones.Value)
            : LeerSnapshot(solicitud, publicador);
        if (fallo is not null)
        {
            return fallo;
        }

        var aplicativoId = autorizacion.Fuente!.AplicativoId;
        var proyecto = await contexto.Proyectos.FirstOrDefaultAsync(p => p.Codigo == solicitud.Proyecto, ct);
        if (proyecto is not null && (!string.Equals(proyecto.Repositorio, publicador.Repositorio, StringComparison.OrdinalIgnoreCase) || proyecto.AplicativoId != aplicativoId))
        {
            Rechazado(log, publicador.Repositorio, "proyecto_de_otro_repo");
            return Rechazo(StatusCodes.Status403Forbidden, "proyecto_de_otro_repo", $"El proyecto «{solicitud.Proyecto}» lo publica otro repo o pertenece a otro aplicativo.");
        }

        if (await BuscarExistenteAsync(contexto, proyecto?.Id, tipo, contenido!, publicador.Commit, ct) is { } previa)
        {
            return Existente(previa, contenido!);
        }

        var ahora = reloj.GetUtcNow();
        proyecto ??= contexto.Proyectos.Add(new ProyectoArquitectura { Codigo = solicitud.Proyecto!, AplicativoId = aplicativoId, Repositorio = publicador.Repositorio }).Entity;
        var estado = tipo == TipoPublicacion.Despliegue || publicador.Rama == "release" ? EstadoPublicacion.Publicado : EstadoPublicacion.Borrador;
        var publicacion = new PublicacionArquitectura
        {
            ProyectoId = proyecto.Id, Tipo = tipo, Canal = contenido!.Canal, Cluster = contenido.Cluster, Rama = publicador.Rama, CommitSha = publicador.Commit,
            VersionPropuesta = solicitud.VersionPropuesta, Estado = estado, Visibilidad = visibilidad!.Value, HerramientaVersion = solicitud.Herramienta?.Version,
            Contenido = contenido.Texto, Sha256 = contenido.Sha256, Workflow = publicador.Workflow, WorkflowDelJob = publicador.WorkflowDelJob,
            Ejecucion = publicador.Ejecucion, PublicadoPor = publicador.Firma, PublicadoEn = ahora, GeneradoEn = contenido.GeneradoEn,
        };
        var anterior = await contexto.Publicaciones.AsNoTracking()
            .Where(p => p.ProyectoId == proyecto.Id && p.Tipo == tipo && p.Canal == contenido.Canal && p.Cluster == contenido.Cluster)
            .OrderByDescending(p => p.PublicadoEn).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        contexto.Publicaciones.Add(publicacion);
        if (contenido.Modelo is { } modelo)
        {
            Proyectar(contexto, publicacion.Id, modelo);
        }

        try
        {
            await GuardarConAuditoriaAsync(contexto, plataforma, http, publicacion, publicador, solicitud.Aplicativo!, ahora, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos ejecuciones del mismo commit a la vez: gana una; la otra responde como si hubiera llegado después.
            contexto.ChangeTracker.Clear();
            var delProyecto = await contexto.Proyectos.AsNoTracking().Where(p => p.Codigo == solicitud.Proyecto).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (await BuscarExistenteAsync(contexto, delProyecto, tipo, contenido, publicador.Commit, ct) is { } ganadora)
            {
                return Existente(ganadora, contenido);
            }
            throw;
        }

        // El drift corre fuera de la petición (§5.1): si la réplica se cae antes, lo cubre la vuelta programada.
        disparador.Pedir(contenido.Canal);
        var tipoTexto = ReglasArquitectura.Texto(tipo);
        Publicada(log, publicador.Repositorio, solicitud.Proyecto!, tipoTexto, contenido.Canal);
        return Results.Json(
            new ResultadoPublicacionArquitectura(publicacion.Id, "creada", publicacion.Canal, publicacion.CommitSha, publicacion.VersionPropuesta, ReglasArquitectura.Texto(estado), Resumir(tipo, anterior, publicacion)),
            statusCode: StatusCodes.Status201Created);
    }

    private static (Contenido? Contenido, IResult? Fallo) LeerModelo(SolicitudPublicacionArquitectura solicitud, PublicadorCi publicador, OpcionesArquitectura opciones)
    {
        if (!opciones.RamasPorCanal.TryGetValue(publicador.Rama, out var canal) || !Canales.Contains(canal))
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, "rama_sin_ambiente", $"La rama «{publicador.Rama}» no despliega ningún ambiente."));
        }
        if (solicitud.Modelo is not { } crudo)
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, "modelo_invalido", "Falta el modelo."));
        }
        var (modelo, codigo, detalle) = ReglasArquitectura.LeerModelo(crudo);
        if (modelo is null)
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, codigo!, "El modelo no se puede publicar.", detalle));
        }
        var texto = crudo.GetRawText();
        return (new Contenido(texto, ReglasArquitectura.Sha256(texto), modelo, canal, string.Empty, solicitud.GeneradoEn), null);
    }

    private static (Contenido? Contenido, IResult? Fallo) LeerSnapshot(SolicitudPublicacionArquitectura solicitud, PublicadorCi publicador)
    {
        if (solicitud.Ambiente is null || !Canales.Contains(solicitud.Ambiente) || solicitud.Cluster is null || !ReglasArquitectura.FormatoCluster().IsMatch(solicitud.Cluster))
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, "snapshot_invalido", "Un snapshot lleva ambiente (dev, qa o pdn) y clúster."));
        }
        if (solicitud.Snapshot is not { } crudo)
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, "snapshot_invalido", "Falta el snapshot."));
        }
        var (snapshot, codigo, detalle) = ReglasSnapshot.Leer(crudo.Valor, solicitud.Ambiente, solicitud.Cluster, publicador.Commit);
        if (snapshot is null)
        {
            return (null, Rechazo(StatusCodes.Status422UnprocessableEntity, codigo!, "El snapshot no se puede publicar.", detalle));
        }
        var limpio = snapshot with { GitopsSha = publicador.Commit };
        var (texto, sha) = ReglasSnapshot.Canonico(limpio);
        return (new Contenido(texto, sha, null, solicitud.Ambiente, solicitud.Cluster, snapshot.GeneradoEn ?? solicitud.GeneradoEn), null);
    }

    private sealed record Previa(Guid Id, string Canal, string CommitSha, string? VersionPropuesta, EstadoPublicacion Estado, string Sha256);

    private static async Task<Previa?> BuscarExistenteAsync(ArquitecturaDbContext contexto, Guid? proyecto, TipoPublicacion tipo, Contenido contenido, string commit, CancellationToken ct) =>
        proyecto is not { } id
            ? null
            : await contexto.Publicaciones.AsNoTracking()
                .Where(p => p.ProyectoId == id && p.Tipo == tipo && p.Canal == contenido.Canal && p.Cluster == contenido.Cluster && p.CommitSha == commit)
                .Select(p => new Previa(p.Id, p.Canal, p.CommitSha, p.VersionPropuesta, p.Estado, p.Sha256))
                .FirstOrDefaultAsync(ct);

    private static IResult Existente(Previa previa, Contenido contenido) =>
        previa.Sha256 == contenido.Sha256
            ? Results.Ok(new ResultadoPublicacionArquitectura(previa.Id, "igual", previa.Canal, previa.CommitSha, previa.VersionPropuesta, ReglasArquitectura.Texto(previa.Estado), null))
            : Rechazo(StatusCodes.Status409Conflict, "sha_con_otro_contenido", "Este commit ya publicó en este canal con otro contenido, y las versiones son inmutables.");

    private static void Proyectar(ArquitecturaDbContext contexto, Guid publicacion, ModeloLeido modelo)
    {
        foreach (var e in modelo.Elementos)
        {
            contexto.Elementos.Add(new ElementoArquitectura
            {
                PublicacionId = publicacion, ElementoId = e.Id, Kind = e.Kind, Titulo = e.Titulo, Descripcion = e.Descripcion, Tecnologia = e.Tecnologia,
                Etiquetas = [.. e.Etiquetas], Metadatos = JsonSerializer.Serialize(e.Metadatos), Enlaces = JsonSerializer.Serialize(e.Enlaces, JsonSerializerOptions.Web),
                K8sWorkload = e.Metadato("k8s_workload") is { Length: <= ArquitecturaDbContext.LargoSujeto } carga ? carga : null,
            });
        }
        foreach (var v in modelo.Vistas)
        {
            contexto.Vistas.Add(new VistaArquitectura { PublicacionId = publicacion, VistaId = v.Id, Tipo = v.Tipo, Categoria = v.Categoria, Titulo = v.Titulo });
        }
    }

    private static ResumenDiffArquitectura? Resumir(TipoPublicacion tipo, PublicacionArquitectura? anterior, PublicacionArquitectura actual)
    {
        if (anterior is null)
        {
            return null;
        }
        var diff = tipo == TipoPublicacion.Modelo
            ? ReglasArquitectura.DiffModelos(anterior.Contenido, actual.Contenido, conDespliegue: true)
            : ReglasSnapshot.Diff(ReglasSnapshot.Parsear(anterior.Contenido), ReglasSnapshot.Parsear(actual.Contenido));
        return new ResumenDiffArquitectura(diff.Altas.Count, diff.Bajas.Count, diff.Cambios.Count);
    }

    /// <summary>La publicación, sus proyecciones y su auditoría en una sola transacción. La auditoría no lleva el contenido.</summary>
    private static async Task GuardarConAuditoriaAsync(
        ArquitecturaDbContext contexto, PlataformaDbContext plataforma, HttpContext http, PublicacionArquitectura p, PublicadorCi publicador, string aplicativo,
        DateTimeOffset ahora, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        try
        {
            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = ahora,
                ActorTipo = TipoActor.Servicio,
                ActorId = publicador.Firma is { Length: > 200 } f ? f[..200] : publicador.Firma,
                OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
                Accion = AccionPublicada,
                RecursoTipo = RecursoAuditoria,
                RecursoId = p.Id.ToString(),
                Version = p.CommitSha,
                Ip = http.Connection.RemoteIpAddress,
                Datos = JsonSerializer.Serialize(new
                {
                    tipo = ReglasArquitectura.Texto(p.Tipo), aplicativo, canal = p.Canal, cluster = p.Cluster, sha256 = p.Sha256, visibilidad = ReglasArquitectura.VisibilidadTexto(p.Visibilidad),
                    publicador.Repositorio, publicador.Rama, commit = publicador.Commit, publicador.Workflow, publicador.WorkflowDelJob, publicador.Ejecucion,
                }, JsonSerializerOptions.Web),
                CorrelacionId = http.TraceIdentifier,
            });
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        finally
        {
            await plataforma.Database.UseTransactionAsync(null, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación de arquitectura: {Repositorio} rechazado ({Codigo})")]
    private static partial void Rechazado(ILogger log, string repositorio, string codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicación de arquitectura: {Repositorio} publicó {Proyecto} ({Tipo}) en {Canal}")]
    private static partial void Publicada(ILogger log, string repositorio, string proyecto, string tipo, string canal);
}
