using System.ComponentModel;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Canales;

/// <summary>
/// Canales por ambiente (HU 3.3 y 3.4, plan 00 §8.2, regla 5, y ATLAS-ADR-0035 §7 y §8), con el token OIDC de GitHub
/// Actions del workflow reutilizable oficial y la lista blanca del catálogo:
/// <list type="bullet">
/// <item><c>GET /api/v1/contratos/publicaciones/base</c>: la versión que corre en el ambiente de referencia (pdn) o, si
/// aún no hay canal, la última estable; es la base del <c>oasdiff breaking</c> de la CI.</item>
/// <item><c>PUT /api/v1/contratos/canales/{ambiente}</c>: el CD avisa qué versión de un contrato quedó desplegada.</item>
/// </list>
/// Y para la PWA (<c>atlas.contratos.ver_internos</c>), <c>GET /api/v1/contratos/{id}/canales</c> con el historial.
/// </summary>
internal static partial class EndpointsCanales
{
    public const string AccionCanal = "contratos.canal_actualizado";
    public const string RecursoCanal = "contratos.canal";

    /// <summary>Filas de historial que devuelve la lectura de un contrato.</summary>
    public const int LimiteHistorial = 100;

    [Description("Qué versión de un contrato quedó desplegada en el ambiente de la ruta.")]
    public sealed record SolicitudCanal(
        [property: Description("Slug del aplicativo del catálogo.")] string Aplicativo,
        [property: Description("Código del contrato dentro del aplicativo.")] string Contrato,
        [property: Description("Versión ya publicada en Atlas (la que respondió POST /publicaciones).")] string Version);

    [Description("El canal después del aviso del CD.")]
    public sealed record CanalActualizado(
        Guid ContratoId,
        [property: Description("dev, qa o pdn (los de Contratos:Canales:Ambientes).")] string Ambiente,
        string Version,
        [property: Description("La que estaba antes; vacío la primera vez.")] string? VersionAnterior,
        DateTimeOffset DesplegadoEn,
        [property: Description("false si el canal ya tenía esa versión (idempotente, sin historial nuevo).")] bool Cambio);

    [Description("La versión base para comparar en la CI (oasdiff breaking y changelog).")]
    public sealed record BaseComparacion(
        Guid ContratoId,
        Guid VersionId,
        string Version,
        [property: Description("SHA-256 del spec (hexadecimal).")] string Sha256,
        [property: Description("canal: la versión que corre en el ambiente de referencia; ultima_estable: aún no hay canal en ese ambiente.")] string Origen,
        [property: Description("Ambiente de referencia (pdn).")] string Ambiente,
        [property: Description("El spec completo, tal como se publicó.")] string Spec);

    [Description("Versión de un contrato en un ambiente.")]
    public sealed record CanalDto(
        string Ambiente,
        Guid VersionId,
        string Version,
        bool EsEstable,
        DateTimeOffset DesplegadoEn,
        [property: Description("github:dueño/repo@rama del CD que avisó.")] string DesplegadoPor,
        [property: Description("Commit desplegado.")] string Commit,
        [property: Description("run_id de GitHub Actions.")] string? Ejecucion);

    [Description("Un cambio de canal.")]
    public sealed record HistorialCanalDto(
        string Ambiente,
        string Version,
        string? VersionAnterior,
        DateTimeOffset DesplegadoEn,
        string DesplegadoPor,
        string Repositorio,
        string Rama,
        string Commit,
        string? Ejecucion);

    [Description("Canales de un contrato: la versión actual por ambiente y el historial, del más reciente al más antiguo.")]
    public sealed record CanalesContrato(
        [property: Description("Ambientes en orden de promoción.")] IReadOnlyList<string> Ambientes,
        IReadOnlyList<CanalDto> Canales,
        [property: Description("Hasta 100 cambios.")] IReadOnlyList<HistorialCanalDto> Historial);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/publicaciones/base", BaseAsync)
            .RequierePublicacionCi()
            .WithName("ObtenerBaseComparacionContrato")
            .WithSummary("Versión base para la CI (la de pdn)")
            .WithDescription(
                "Para el workflow reutilizable atlas-publicar (HU 3.3): la versión del contrato que corre hoy en el ambiente de " +
                "referencia (pdn) o, si aún no tiene canal allí, la última estable publicada, con su spec completo. Es la base de " +
                "«oasdiff breaking» y «oasdiff changelog». Solo con el token OIDC de GitHub Actions de un repo y rama de la lista " +
                "blanca para ese aplicativo y tipo. 404 si el contrato no existe o no tiene ninguna versión estable.")
            .Produces<BaseComparacion>()
            .Produces<EndpointsPublicacion.ErrorPublicacion>(StatusCodes.Status403Forbidden)
            .Produces<EndpointsPublicacion.ErrorPublicacion>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapPut("/canales/{ambiente}", ActualizarAsync)
            .RequierePublicacionCi()
            .WithName("ActualizarCanalContrato")
            .WithSummary("Marcar la versión desplegada en un ambiente")
            .WithDescription(
                "Lo llama el CD de cada repo al desplegar (HU 3.4, plan 00 §8.2, regla 5), dentro del workflow reutilizable " +
                "oficial. Solo con el token OIDC de GitHub Actions de un repo de la lista blanca para ese aplicativo y tipo, y " +
                "desde una rama que despliega ese ambiente (Contratos:Canales:Ambientes; por defecto develop → dev, staging → qa, " +
                "release → pdn). La versión debe estar publicada; pdn solo acepta estables. Guarda el historial y lo audita. 200 " +
                "con cambio=false si el canal ya tenía esa versión.")
            .Produces<CanalActualizado>()
            .Produces<EndpointsPublicacion.ErrorPublicacion>(StatusCodes.Status403Forbidden)
            .Produces<EndpointsPublicacion.ErrorPublicacion>(StatusCodes.Status404NotFound)
            .Produces<EndpointsPublicacion.ErrorPublicacion>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();

        grupo.MapGet("/{id:guid}/canales", CanalesAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarCanalesContrato")
            .WithSummary("Versiones de un contrato por ambiente")
            .WithDescription("Qué versión corre en cada ambiente (dev, qa, pdn) y el historial de cambios de los canales, con el CD que avisó. 404 si el contrato no existe. Requiere atlas.contratos.ver_internos.");
    }

    private static async Task<IResult> BaseAsync(
        [Description("Slug del aplicativo.")] string? aplicativo,
        [Description("Código del contrato.")] string? contrato,
        HttpContext http, ContratosDbContext contexto, IFuentesPublicacion fuentes, IOptionsMonitor<OpcionesCanales> opciones,
        ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(typeof(EndpointsCanales));
        var publicador = http.Publicador();
        var delRepo = await fuentes.DelRepositorioAsync(publicador.Repositorio, ct);
        if (Rechazo(publicador, delRepo, log) is { } rechazo)
        {
            return rechazo;
        }
        var errores = Validar(aplicativo, contrato, version: null, pedirVersion: false);
        if (errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }
        var (encontrado, problema) = await ContratoAutorizadoAsync(contexto, publicador, delRepo, aplicativo!.Trim(), contrato!.Trim(), log, ct);
        if (encontrado is null)
        {
            return problema!;
        }

        var referencia = opciones.CurrentValue.Referencia;
        var enCanal = await contexto.Canales.AsNoTracking().Where(c => c.ContratoId == encontrado.Id && c.Ambiente == referencia)
            .Select(c => (Guid?)c.VersionId).FirstOrDefaultAsync(ct);
        var consulta = contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == encontrado.Id);
        var version = enCanal is { } id
            ? await consulta.FirstOrDefaultAsync(v => v.Id == id, ct)
            : await consulta.Where(v => v.EsEstable).OrderByDescending(v => v.Mayor).ThenByDescending(v => v.Menor).ThenByDescending(v => v.Parche)
                .FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return TypedResults.NotFound(new EndpointsPublicacion.ErrorPublicacion("sin_base",
                $"«{encontrado.Codigo}» aún no tiene versión en {referencia} ni versiones estables: no hay contra qué comparar.", null));
        }
        return TypedResults.Ok(new BaseComparacion(encontrado.Id, version.Id, version.Version, version.Sha256,
            enCanal is null ? "ultima_estable" : "canal", referencia, version.Contenido));
    }

    private static async Task<IResult> ActualizarAsync(
        [Description("dev, qa o pdn.")] string ambiente,
        [Description("El contrato y la versión desplegados.")] SolicitudCanal solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IFuentesPublicacion fuentes,
        IOptionsMonitor<OpcionesCanales> opciones, TimeProvider reloj, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(typeof(EndpointsCanales));
        var publicador = http.Publicador();
        var delRepo = await fuentes.DelRepositorioAsync(publicador.Repositorio, ct);
        if (Rechazo(publicador, delRepo, log) is { } rechazo)
        {
            return rechazo;
        }

        var configurado = opciones.CurrentValue.Buscar(ambiente);
        if (configurado is null)
        {
            return TypedResults.NotFound(new EndpointsPublicacion.ErrorPublicacion("ambiente_inexistente",
                $"«{ambiente}» no es un ambiente con canal ({string.Join(", ", opciones.CurrentValue.Nombres)}).", null));
        }
        if (!configurado.Ramas.Any(r => ListaBlancaPublicacion.Coincide(r, publicador.Rama, distinguirMayusculas: true)))
        {
            Rechazado(log, publicador.Repositorio, "ambiente_no_permitido");
            return Prohibido("ambiente_no_permitido",
                $"La rama «{publicador.Rama}» no despliega {configurado.Nombre} (lo despliegan: {string.Join(", ", configurado.Ramas)}).");
        }
        var errores = Validar(solicitud.Aplicativo, solicitud.Contrato, solicitud.Version, pedirVersion: true);
        if (errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }
        var aplicativo = solicitud.Aplicativo.Trim();
        var (contrato, problema) = await ContratoAutorizadoAsync(contexto, publicador, delRepo, aplicativo, solicitud.Contrato.Trim(), log, ct);
        if (contrato is null)
        {
            return problema!;
        }
        var texto = solicitud.Version.Trim();
        var version = await contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == contrato.Id && v.Version == texto)
            .Select(v => new { v.Id, v.Version, v.EsEstable }).FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return TypedResults.NotFound(new EndpointsPublicacion.ErrorPublicacion("version_inexistente",
                $"La versión {texto} de «{contrato.Codigo}» no está publicada: publícala antes de marcar el canal.", null));
        }
        if (configurado.SoloEstables && !version.EsEstable)
        {
            return TypedResults.UnprocessableEntity(new EndpointsPublicacion.ErrorPublicacion("version_no_estable",
                $"{configurado.Nombre} solo acepta versiones estables (sin prerelease); {version.Version} no lo es.", null));
        }

        for (var intento = 0; ; intento++)
        {
            try
            {
                return await GuardarAsync(contexto, plataforma, http, publicador, contrato, aplicativo, configurado.Nombre, version.Id, version.Version,
                    reloj.GetUtcNow(), log, ct);
            }
            catch (DbUpdateException ex) when (intento == 0 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Otro CD creó el canal a la vez: se repite una vez, ya como actualización.
                contexto.ChangeTracker.Clear();
                plataforma.ChangeTracker.Clear();
            }
        }
    }

    private static async Task<IResult> GuardarAsync(
        ContratosDbContext contexto, PlataformaDbContext plataforma, HttpContext http, PublicadorCi publicador, Contrato contrato, string aplicativo,
        string ambiente, Guid versionId, string version, DateTimeOffset ahora, ILogger log, CancellationToken ct)
    {
        var canal = await contexto.Canales.FirstOrDefaultAsync(c => c.ContratoId == contrato.Id && c.Ambiente == ambiente, ct);
        if (canal is not null && canal.VersionId == versionId)
        {
            return TypedResults.Ok(new CanalActualizado(contrato.Id, ambiente, version, version, canal.DesplegadoEn, Cambio: false));
        }
        var anteriorId = canal?.VersionId;
        var anterior = anteriorId is { } a
            ? await contexto.Versiones.AsNoTracking().Where(v => v.Id == a).Select(v => v.Version).FirstOrDefaultAsync(ct)
            : null;
        var firma = Recortar(publicador.Firma, ContratosDbContext.LargoRepo + ContratosDbContext.LargoRama + 10);
        if (canal is null)
        {
            canal = new CanalContrato
            {
                ContratoId = contrato.Id, Ambiente = ambiente, VersionId = versionId, DesplegadoEn = ahora, DesplegadoPor = firma,
                CommitSha = publicador.Commit, Ejecucion = publicador.Ejecucion,
            };
            contexto.Canales.Add(canal);
        }
        else
        {
            canal.VersionId = versionId;
            canal.DesplegadoEn = ahora;
            canal.DesplegadoPor = firma;
            canal.CommitSha = publicador.Commit;
            canal.Ejecucion = publicador.Ejecucion;
        }
        contexto.HistorialCanales.Add(new HistorialCanal
        {
            ContratoId = contrato.Id, Ambiente = ambiente, VersionId = versionId, VersionAnteriorId = anteriorId, DesplegadoEn = ahora,
            DesplegadoPor = firma, Repositorio = publicador.Repositorio, Rama = publicador.Rama, CommitSha = publicador.Commit,
            WorkflowDelJob = Recortar(publicador.WorkflowDelJob, ContratosDbContext.LargoWorkflow), Ejecucion = publicador.Ejecucion,
        });

        // El canal, su historial, la auditoría y el evento en una sola transacción (outbox, ATLAS-ADR-0018).
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            await contexto.SaveChangesAsync(ct);
            plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
            await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = ahora,
                ActorTipo = TipoActor.Servicio,
                ActorId = Recortar(publicador.Firma, 200),
                Accion = AccionCanal,
                RecursoTipo = RecursoCanal,
                RecursoId = canal.Id.ToString(),
                Version = version,
                Ip = http.Connection.RemoteIpAddress,
                Datos = JsonSerializer.Serialize(new
                {
                    aplicativo, contrato = contrato.Codigo, ambiente, version, versionAnterior = anterior,
                    publicador.Repositorio, publicador.Rama, commit = publicador.Commit, publicador.WorkflowDelJob, publicador.Ejecucion, publicador.Intento,
                }, JsonSerializerOptions.Web),
                CorrelacionId = http.TraceIdentifier,
            });
            plataforma.EncolarEvento(EventosContratos.CanalActualizado, new DatosCanalActualizado(
                contrato.Id, versionId, contrato.AplicativoId, aplicativo, contrato.Codigo, ambiente, version, anterior, publicador.Repositorio,
                publicador.Rama, publicador.Commit));
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        CanalMovido(log, publicador.Repositorio, aplicativo, contrato.Codigo, ambiente, version);
        return TypedResults.Ok(new CanalActualizado(contrato.Id, ambiente, version, anterior, ahora, Cambio: true));
    }

    /// <summary><c>data</c> de <c>atlas.contratos.canal_actualizado</c> v1 (contracts/asyncapi/atlas-events.v1.yaml).</summary>
    internal sealed record DatosCanalActualizado(
        Guid ContratoId, Guid VersionId, Guid AplicativoId, string Aplicativo, string Contrato, string Ambiente, string Version, string? VersionAnterior,
        string Repositorio, string Rama, string Commit);

    private static async Task<Results<Ok<CanalesContrato>, NotFound>> CanalesAsync(
        [Description("Id del contrato.")] Guid id, ContratosDbContext contexto, IOptionsMonitor<OpcionesCanales> opciones, CancellationToken ct)
    {
        if (!await contexto.Contratos.AnyAsync(c => c.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        var ambientes = opciones.CurrentValue.Nombres;
        var canales = await contexto.Canales.AsNoTracking().Where(c => c.ContratoId == id)
            .Join(contexto.Versiones.AsNoTracking(), c => c.VersionId, v => v.Id, (c, v) => new { c, v.Version, v.EsEstable })
            .ToListAsync(ct);
        var historial = await contexto.HistorialCanales.AsNoTracking().Where(h => h.ContratoId == id)
            .OrderByDescending(h => h.DesplegadoEn).ThenByDescending(h => h.Id).Take(LimiteHistorial)
            .Join(contexto.Versiones.AsNoTracking(), h => h.VersionId, v => v.Id, (h, v) => new { h, v.Version })
            .GroupJoin(contexto.Versiones.AsNoTracking(), x => x.h.VersionAnteriorId, v => v.Id, (x, anteriores) => new { x.h, x.Version, anteriores })
            .SelectMany(x => x.anteriores.DefaultIfEmpty(), (x, anterior) => new { x.h, x.Version, Anterior = anterior == null ? null : anterior.Version })
            .ToListAsync(ct);
        return TypedResults.Ok(new CanalesContrato(
            ambientes,
            [
                .. canales.OrderBy(c => Orden(ambientes, c.c.Ambiente))
                    .Select(c => new CanalDto(c.c.Ambiente, c.c.VersionId, c.Version, c.EsEstable, c.c.DesplegadoEn, c.c.DesplegadoPor, c.c.CommitSha, c.c.Ejecucion)),
            ],
            [
                .. historial.OrderByDescending(x => x.h.DesplegadoEn).ThenByDescending(x => x.h.Id)
                    .Select(x => new HistorialCanalDto(x.h.Ambiente, x.Version, x.Anterior, x.h.DesplegadoEn, x.h.DesplegadoPor, x.h.Repositorio, x.h.Rama,
                        x.h.CommitSha, x.h.Ejecucion)),
            ]));
    }

    private static int Orden(IReadOnlyList<string> ambientes, string ambiente)
    {
        var i = ambientes.ToList().IndexOf(ambiente);
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>Lista blanca por repo, rama y workflow, antes de leer el cuerpo (como la publicación).</summary>
    private static JsonHttpResult<EndpointsPublicacion.ErrorPublicacion>? Rechazo(PublicadorCi publicador, IReadOnlyList<FuentePublicacionLeida> delRepo, ILogger log)
    {
        if (ListaBlancaPublicacion.DelPublicador(publicador, delRepo).Count > 0)
        {
            return null;
        }
        var rechazo = ListaBlancaPublicacion.Autorizar(publicador, delRepo, string.Empty, string.Empty);
        Rechazado(log, publicador.Repositorio, rechazo.Codigo!);
        return Prohibido(rechazo.Codigo!, rechazo.Mensaje!);
    }

    /// <summary>
    /// El contrato, si el publicador puede publicar contratos de su tipo para ese aplicativo. Sin la fuente del
    /// aplicativo es 403 (no se revela si el contrato existe); sin el contrato, 404.
    /// </summary>
    private static async Task<(Contrato? Contrato, IResult? Problema)> ContratoAutorizadoAsync(
        ContratosDbContext contexto, PublicadorCi publicador, IReadOnlyList<FuentePublicacionLeida> delRepo, string aplicativo, string codigo, ILogger log,
        CancellationToken ct)
    {
        var validas = ListaBlancaPublicacion.DelPublicador(publicador, delRepo)
            .Where(f => string.Equals(f.AplicativoSlug, aplicativo, StringComparison.Ordinal)).ToList();
        if (validas.Count == 0)
        {
            Rechazado(log, publicador.Repositorio, "aplicativo_no_autorizado");
            return (null, Prohibido("aplicativo_no_autorizado", $"{publicador.Repositorio} no puede publicar para el aplicativo «{aplicativo}»."));
        }
        var aplicativoId = validas[0].AplicativoId;
        var contrato = await contexto.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.AplicativoId == aplicativoId && c.Codigo == codigo, ct);
        if (contrato is null)
        {
            return (null, TypedResults.NotFound(new EndpointsPublicacion.ErrorPublicacion("contrato_inexistente",
                $"«{aplicativo}» no tiene un contrato «{codigo}» publicado.", null)));
        }
        var autorizacion = ListaBlancaPublicacion.Autorizar(publicador, delRepo, aplicativo, Valores.Texto(contrato.Tipo));
        if (!autorizacion.Autorizada)
        {
            Rechazado(log, publicador.Repositorio, autorizacion.Codigo!);
            return (null, Prohibido(autorizacion.Codigo!, autorizacion.Mensaje!));
        }
        return (contrato, null);
    }

    private static Dictionary<string, string[]> Validar(string? aplicativo, string? contrato, string? version, bool pedirVersion)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Valores.CodigoValido(aplicativo?.Trim()))
        {
            errores["aplicativo"] = ["El slug del aplicativo va en minúsculas, números y guiones."];
        }
        if (!Valores.CodigoValido(contrato?.Trim()))
        {
            errores["contrato"] = ["El código del contrato va en minúsculas, números y guiones (por ejemplo external-api)."];
        }
        if (pedirVersion && Specs.VersionSemantica.Leer(version) is null)
        {
            errores["version"] = ["La versión es semántica (X.Y.Z o X.Y.Z-<prerelease>, sin +metadatos)."];
        }
        return errores;
    }

    private static string Recortar(string texto, int largo) => texto.Length > largo ? texto[..largo] : texto;

    private static JsonHttpResult<EndpointsPublicacion.ErrorPublicacion> Prohibido(string codigo, string mensaje) =>
        TypedResults.Json(new EndpointsPublicacion.ErrorPublicacion(codigo, mensaje, null), statusCode: StatusCodes.Status403Forbidden);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canales de contratos: {Repositorio} rechazado ({Codigo})")]
    private static partial void Rechazado(ILogger log, string repositorio, string codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Canales de contratos: {Repositorio} dejó {Aplicativo}/{Contrato} en {Ambiente} con {Version}")]
    private static partial void CanalMovido(ILogger log, string repositorio, string aplicativo, string contrato, string ambiente, string version);
}
