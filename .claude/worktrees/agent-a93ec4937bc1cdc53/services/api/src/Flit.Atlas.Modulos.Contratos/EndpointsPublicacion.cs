using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>
/// <c>POST /api/v1/contratos/publicaciones</c> (HU 3.2, plan 00 §8.3 y §8.5, ATLAS-ADR-0009 y ATLAS-ADR-0035). Solo con
/// el token OIDC de GitHub Actions (<c>RequierePublicacionCi</c>): la cookie de la PWA no vale aquí. Orden de las
/// respuestas: 401 token inválido o vencido; 403 dueño, evento o workflow reutilizable no permitidos, o repo, rama,
/// workflow, aplicativo o tipo fuera de la lista blanca; 400 petición mal formada; 422 spec inválido o versión que no
/// cuadra con el spec o con la rama; 409 la versión ya existe con otro hash (o el contrato es de otro tipo); 200 la misma
/// versión con el mismo hash (idempotente, no publica nada); 201 versión nueva.
/// </summary>
internal static partial class EndpointsPublicacion
{
    public const string AccionPublicada = "contratos.version_publicada";
    public const string RecursoAuditoria = "contratos.version";

    /// <summary>Tope del changelog de oasdiff (texto JSON).</summary>
    public const int ChangelogMaximo = 1024 * 1024;

    /// <summary>Tope del cuerpo: el spec (que en JSON puede crecer al escaparse) más el changelog.</summary>
    private const long CuerpoMaximo = (AnalizadorSpec.TamanoMaximo * 2L) + ChangelogMaximo + (64 * 1024);

    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => CuerpoMaximo;
    }

    [Description("Una versión de contrato publicada desde la CI de su repo.")]
    public sealed record SolicitudPublicacion(
        [property: Description("Slug del aplicativo del catálogo al que pertenece el contrato (flit2-core-api).")] string Aplicativo,
        [property: Description("Código del contrato dentro del aplicativo: minúsculas, números y guiones (external-api).")] string Contrato,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        [property: Description("Versión semántica sin +metadatos. En OpenAPI y AsyncAPI, por defecto info.version; si se manda, debe ser info.version o info.version-<prerelease>. En proto es obligatoria y su MAJOR debe ser la del paquete (v1 → 1). release publica X.Y.Z; develop, X.Y.Z-dev.<sha7>; staging, X.Y.Z-rc.<sha7>.")] string? Version,
        [property: Description("El spec completo tal como está en el repo (JSON o YAML; proto en texto), hasta 5 MB. Su SHA-256 decide la idempotencia.")] string Spec,
        [property: Description("Salida JSON de «oasdiff changelog» contra la versión de pdn (texto con un arreglo), hasta 1 MB; vacío si no hay base.")] string? Changelog,
        [property: Description("Prerelease que agrega el workflow según la rama (dev.<sha7>, rc.<sha7>): la versión publicada es <version o info.version>-<prerelease>, y esa parte debe ser estable. Vacío: la versión tal cual.")] string? Prerelease = null,
        [property: Description("Versión contra la que se calculó el changelog (la que Atlas entregó en GET /publicaciones/base), si la hubo. El Consolidado usa el changelog guardado cuando coincide con la versión que corre en pdn.")] string? ChangelogBase = null);

    [Description("La versión publicada (o la que ya existía con el mismo hash).")]
    public sealed record VersionPublicada(
        Guid ContratoId,
        Guid VersionId,
        [property: Description("Slug del aplicativo.")] string Aplicativo,
        [property: Description("Código del contrato.")] string Contrato,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        string Version,
        bool EsEstable,
        [property: Description("SHA-256 del spec (hexadecimal).")] string Sha256,
        [property: Description("Operaciones, webhooks, canales o rpc del índice.")] int Operaciones,
        [property: Description("Si el changelog trae cambios que rompen compatibilidad (nivel 3 de oasdiff).")] bool TieneBreaking,
        [property: Description("Avisos de la validación que no impiden publicar (p. ej. imports de otros archivos en proto).")] IReadOnlyList<string> Advertencias,
        DateTimeOffset PublicadoEn,
        [property: Description("true si esta petición la creó (201); false si ya existía con el mismo hash (200).")] bool Creada);

    [Description("Por qué no se publicó, con un código estable.")]
    public sealed record ErrorPublicacion(
        [property: Description("fuente_no_autorizada, aplicativo_no_autorizado, tipo_no_autorizado, ambiente_no_permitido (403); sin_base, contrato_inexistente, version_inexistente, ambiente_inexistente (404); spec_invalida, version_invalida, version_distinta_del_spec, version_no_permitida_en_rama, version_no_estable (422); version_existente, tipo_distinto (409).")] string Codigo,
        string Mensaje,
        [property: Description("Detalle de la validación del spec (422), si lo hay.")] IReadOnlyList<string>? Errores);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/publicaciones", PublicarAsync)
            .RequierePublicacionCi()
            .WithMetadata(new LimiteCuerpo())
            .WithName("PublicarVersionContrato")
            .WithSummary("Publicar una versión de contrato desde la CI")
            .WithDescription(
                "Solo con el token OIDC de GitHub Actions del workflow reutilizable oficial (Authorization: Bearer, audiencia del " +
                "ambiente), desde un repo, rama y workflow de la lista blanca del catálogo para ese aplicativo y tipo. Valida el " +
                "spec (OpenAPI 3.x, AsyncAPI 2.x/3.x o proto), lo guarda inmutable con su SHA-256 y su procedencia (repo, rama, " +
                "workflow, commit y ejecución del token) y arma el índice de operaciones. 201 si es nueva; 200 si la misma versión " +
                "ya existe con el mismo hash (idempotente); 409 si existe con otro hash (sube la versión); 422 si el spec no es " +
                "válido. La cookie de la PWA no sirve aquí.")
            .Produces<VersionPublicada>(StatusCodes.Status201Created)
            .Produces<VersionPublicada>(StatusCodes.Status200OK)
            .Produces<ErrorPublicacion>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPublicacion>(StatusCodes.Status409Conflict)
            .Produces<ErrorPublicacion>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> PublicarAsync(
        [Description("La versión a publicar.")] SolicitudPublicacion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, IFuentesPublicacion fuentes,
        IOptionsMonitor<OpcionesPublicacion> opciones, TimeProvider reloj, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(typeof(EndpointsPublicacion));
        var publicador = http.Publicador();

        // 1. Lista blanca por repo, rama y workflow, antes de mirar el cuerpo.
        var delRepo = await fuentes.DelRepositorioAsync(publicador.Repositorio, ct);
        if (ListaBlancaPublicacion.DelPublicador(publicador, delRepo).Count == 0)
        {
            var rechazo = ListaBlancaPublicacion.Autorizar(publicador, delRepo, string.Empty, string.Empty);
            Rechazada(log, publicador.Repositorio, rechazo.Codigo!);
            return Prohibido(rechazo);
        }

        // 2. Forma de la petición.
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tipo = Valores.Tipo(solicitud.Tipo);
        if (tipo is null)
        {
            errores["tipo"] = [$"Valores válidos: {string.Join(", ", Valores.Tipos.Keys)}."];
        }
        var aplicativo = (solicitud.Aplicativo ?? string.Empty).Trim();
        if (!Valores.CodigoValido(aplicativo))
        {
            errores["aplicativo"] = ["El slug del aplicativo va en minúsculas, números y guiones."];
        }
        var codigo = (solicitud.Contrato ?? string.Empty).Trim();
        if (!Valores.CodigoValido(codigo))
        {
            errores["contrato"] = [$"El código del contrato va en minúsculas, números y guiones (por ejemplo external-api), hasta {ContratosDbContext.LargoCodigo} caracteres."];
        }
        var spec = solicitud.Spec ?? string.Empty;
        var tamano = Encoding.UTF8.GetByteCount(spec);
        if (string.IsNullOrWhiteSpace(spec) || tamano > AnalizadorSpec.TamanoMaximo)
        {
            errores["spec"] = [$"El spec es obligatorio y va hasta {AnalizadorSpec.TamanoMaximo / (1024 * 1024)} MB."];
        }
        var (changelog, tieneBreaking) = LeerChangelog(solicitud.Changelog, errores);
        var changelogBase = string.IsNullOrWhiteSpace(solicitud.ChangelogBase) ? null : solicitud.ChangelogBase.Trim();
        if (changelogBase is not null && VersionSemantica.Leer(changelogBase) is null)
        {
            errores["changelogBase"] = ["La versión base del changelog es semántica (X.Y.Z o X.Y.Z-<prerelease>)."];
        }
        if (errores.Count > 0 || tipo is null)
        {
            return TypedResults.ValidationProblem(errores);
        }

        // 3. Lista blanca por aplicativo y tipo.
        var autorizacion = ListaBlancaPublicacion.Autorizar(publicador, delRepo, aplicativo, Valores.Texto(tipo.Value));
        if (!autorizacion.Autorizada)
        {
            Rechazada(log, publicador.Repositorio, autorizacion.Codigo!);
            return Prohibido(autorizacion);
        }
        var fuente = autorizacion.Fuente!;

        // 4. El spec y su versión.
        var analizado = AnalizadorSpec.Analizar(tipo.Value, spec);
        if (!analizado.Valido)
        {
            return Invalido("spec_invalida", "El spec no es válido.", analizado.Errores);
        }
        var (version, problemaVersion) = Versionar(tipo.Value, solicitud.Version, solicitud.Prerelease, analizado);
        if (version is null)
        {
            return Invalido(problemaVersion!.Value.Codigo, problemaVersion.Value.Mensaje, null);
        }
        if (ListaBlancaPublicacion.MotivoVersionPorRama(publicador.Rama, version.Prerelease, opciones.CurrentValue) is { } motivoRama)
        {
            return Invalido("version_no_permitida_en_rama", motivoRama, null);
        }
        var sha = AnalizadorSpec.Sha256(spec);

        // 5. Inmutabilidad: misma versión y mismo hash → 200; otro hash → 409.
        var existente = await ExistenteAsync(contexto, fuente.AplicativoId, codigo, version.ToString(), ct);
        if (existente is { } previo)
        {
            return Repetida(previo.Contrato, previo.Version, aplicativo, tipo.Value, sha, await ContarAsync(contexto, previo.Version, ct));
        }

        var ahora = reloj.GetUtcNow();
        try
        {
            var publicada = await GuardarAsync(contexto, plataforma, http, publicador, fuente, aplicativo, codigo, tipo.Value, version, spec, tamano,
                sha, analizado, changelog, tieneBreaking, changelogBase, ahora, ct);
            if (publicada is Created<VersionPublicada> { Value: { } creada })
            {
                Publicada(log, publicador.Repositorio, aplicativo, codigo, creada.Version);
            }
            return publicada;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Otra ejecución publicó lo mismo a la vez: se responde como si hubiera llegado después.
            contexto.ChangeTracker.Clear();
            var ganadora = await ExistenteAsync(contexto, fuente.AplicativoId, codigo, version.ToString(), ct);
            if (ganadora is { } g)
            {
                return Repetida(g.Contrato, g.Version, aplicativo, tipo.Value, sha, await ContarAsync(contexto, g.Version, ct));
            }
            throw;
        }
    }

    private static async Task<IResult> GuardarAsync(
        ContratosDbContext contexto, PlataformaDbContext plataforma, HttpContext http, PublicadorCi publicador, FuentePublicacionLeida fuente,
        string aplicativo, string codigo, TipoContrato tipo, VersionSemantica version, string spec, int tamano, string sha,
        SpecAnalizado analizado, string changelog, bool tieneBreaking, string? changelogBase, DateTimeOffset ahora, CancellationToken ct)
    {
        var contrato = await contexto.Contratos.FirstOrDefaultAsync(c => c.AplicativoId == fuente.AplicativoId && c.Codigo == codigo, ct);
        if (contrato is not null && contrato.Tipo != tipo)
        {
            return TypedResults.Conflict(new ErrorPublicacion("tipo_distinto",
                $"El contrato «{codigo}» de «{aplicativo}» es {Valores.Texto(contrato.Tipo)}, no {Valores.Texto(tipo)}.", null));
        }
        var nombre = analizado.Titulo ?? codigo;
        if (contrato is null)
        {
            contrato = new Contrato { AplicativoId = fuente.AplicativoId, Codigo = codigo, Tipo = tipo, Nombre = nombre, Repositorio = publicador.Repositorio };
            contexto.Contratos.Add(contrato);
        }
        // Nombre y descripción siguen a la última versión publicada.
        contrato.Nombre = nombre;
        contrato.Descripcion = analizado.Descripcion;
        contrato.Repositorio = publicador.Repositorio;
        // La visibilidad la decide el repo (info.x-flit-visibilidad) y sigue a la última versión estable: una prerelease de
        // develop no cambia lo que puede ir en paquetes de clientes. Sin la marca, interna (HU 4.1, ATLAS-ADR-0041).
        if (version.EsEstable)
        {
            contrato.Visibilidad = analizado.VisibilidadDeclarada ?? VisibilidadContrato.Interna;
        }

        var nueva = new VersionContrato
        {
            ContratoId = contrato.Id,
            Version = version.ToString(),
            Mayor = version.Mayor,
            Menor = version.Menor,
            Parche = version.Parche,
            Prerelease = version.Prerelease,
            EsEstable = version.EsEstable,
            Sha256 = sha,
            Formato = analizado.Formato,
            Contenido = spec,
            Tamano = tamano,
            Repositorio = publicador.Repositorio,
            Rama = publicador.Rama,
            CommitSha = publicador.Commit,
            Workflow = publicador.Workflow,
            WorkflowDelJob = publicador.WorkflowDelJob,
            Ejecucion = publicador.Ejecucion,
            PublicadoPor = publicador.Firma,
            PublicadoEn = ahora,
            Changelog = changelog,
            TieneBreaking = tieneBreaking,
            ChangelogBase = changelogBase,
        };
        contexto.Versiones.Add(nueva);
        contexto.Operaciones.AddRange(analizado.Operaciones.Select(o => new OperacionContrato
        {
            VersionId = nueva.Id,
            Clave = o.Clave,
            Clase = o.Clase,
            Metodo = o.Metodo,
            Ruta = o.Ruta,
            OperationId = o.OperationId,
            Resumen = o.Resumen,
            Etiquetas = [.. o.Etiquetas],
            Publica = o.Publica,
        }));

        // La versión, su auditoría y su evento en una sola transacción (outbox, ATLAS-ADR-0018).
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            await contexto.SaveChangesAsync(ct);
            plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
            await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = ahora,
                ActorTipo = TipoActor.Servicio,
                ActorId = publicador.Firma.Length > 200 ? publicador.Firma[..200] : publicador.Firma,
                Accion = AccionPublicada,
                RecursoTipo = RecursoAuditoria,
                RecursoId = nueva.Id.ToString(),
                Version = nueva.Version,
                Ip = http.Connection.RemoteIpAddress,
                // Procedencia del token (ATLAS-ADR-0009): repo, rama, commit, workflow y ejecución; nunca el token.
                Datos = JsonSerializer.Serialize(new
                {
                    aplicativo, contrato = codigo, tipo = Valores.Texto(tipo), version = nueva.Version, sha256 = sha,
                    publicador.Repositorio, publicador.Rama, commit = publicador.Commit, publicador.Workflow, publicador.WorkflowDelJob,
                    publicador.Ejecucion, publicador.Intento, publicador.Evento,
                }, JsonSerializerOptions.Web),
                CorrelacionId = http.TraceIdentifier,
            });
            plataforma.EncolarEvento(EventosContratos.VersionPublicada, new DatosVersionPublicada(
                contrato.Id, nueva.Id, fuente.AplicativoId, aplicativo, codigo, Valores.Texto(tipo), nueva.Version, nueva.EsEstable, sha,
                publicador.Repositorio, publicador.Rama, publicador.Commit, tieneBreaking, analizado.Operaciones.Count));
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }

        return TypedResults.Created($"/api/v1/contratos/{contrato.Id}/versiones/{nueva.Version}",
            Respuesta(contrato, nueva, aplicativo, analizado.Operaciones.Count, analizado.Advertencias, creada: true));
    }

    /// <summary><c>data</c> de <c>atlas.contratos.version_publicada</c> v1 (contracts/asyncapi/atlas-events.v1.yaml).</summary>
    internal sealed record DatosVersionPublicada(
        Guid ContratoId, Guid VersionId, Guid AplicativoId, string Aplicativo, string Contrato, string Tipo, string Version, bool EsEstable,
        string Sha256, string Repositorio, string Rama, string Commit, bool TieneBreaking, int Operaciones);

    private static async Task<(Contrato Contrato, VersionContrato Version)?> ExistenteAsync(
        ContratosDbContext contexto, Guid aplicativoId, string codigo, string version, CancellationToken ct)
    {
        var fila = await contexto.Contratos.AsNoTracking()
            .Where(c => c.AplicativoId == aplicativoId && c.Codigo == codigo)
            .Join(contexto.Versiones.AsNoTracking().Where(v => v.Version == version), c => c.Id, v => v.ContratoId, (c, v) => new { c, v })
            .FirstOrDefaultAsync(ct);
        return fila is null ? null : (fila.c, fila.v);
    }

    private static Task<int> ContarAsync(ContratosDbContext contexto, VersionContrato version, CancellationToken ct) =>
        contexto.Operaciones.CountAsync(o => o.VersionId == version.Id, ct);

    private static IResult Repetida(Contrato contrato, VersionContrato version, string aplicativo, TipoContrato tipo, string sha, int operaciones)
    {
        if (contrato.Tipo != tipo)
        {
            return TypedResults.Conflict(new ErrorPublicacion("tipo_distinto",
                $"El contrato «{contrato.Codigo}» de «{aplicativo}» es {Valores.Texto(contrato.Tipo)}, no {Valores.Texto(tipo)}.", null));
        }
        if (!string.Equals(version.Sha256, sha, StringComparison.Ordinal))
        {
            return TypedResults.Conflict(new ErrorPublicacion("version_existente",
                $"La versión {version.Version} de «{contrato.Codigo}» ya está publicada con otro contenido (sha256 {version.Sha256[..12]}…). Las versiones son inmutables: sube la versión.",
                null));
        }
        return TypedResults.Ok(Respuesta(contrato, version, aplicativo, operaciones, [], creada: false));
    }

    private static VersionPublicada Respuesta(Contrato c, VersionContrato v, string aplicativo, int operaciones, IReadOnlyList<string> advertencias, bool creada) =>
        new(c.Id, v.Id, aplicativo, c.Codigo, Valores.Texto(c.Tipo), v.Version, v.EsEstable, v.Sha256, operaciones, v.TieneBreaking, advertencias,
            v.PublicadoEn, creada);

    /// <summary>
    /// Como <see cref="Versionar(TipoContrato, string?, SpecAnalizado)"/>, con el prerelease que agrega el workflow según la
    /// rama (HU 3.3): <c>&lt;version o info.version&gt;-&lt;prerelease&gt;</c>. La parte sin prerelease debe ser estable.
    /// </summary>
    internal static (VersionSemantica? Version, (string Codigo, string Mensaje)? Problema) Versionar(
        TipoContrato tipo, string? pedida, string? prerelease, SpecAnalizado spec)
    {
        if (string.IsNullOrWhiteSpace(prerelease))
        {
            return Versionar(tipo, pedida, spec);
        }
        var pre = prerelease.Trim();
        if (VersionSemantica.Leer($"0.0.0-{pre}") is null)
        {
            return (null, ("version_invalida",
                $"El prerelease «{pre}» no es válido en SemVer (identificadores [0-9A-Za-z-] separados por puntos; los numéricos, sin ceros a la izquierda)."));
        }
        var nucleo = string.IsNullOrWhiteSpace(pedida) ? (tipo == TipoContrato.Proto ? null : spec.VersionDelSpec) : pedida;
        var estable = VersionSemantica.Leer(nucleo);
        if (estable is null || !estable.EsEstable)
        {
            return (null, ("version_invalida", tipo == TipoContrato.Proto && string.IsNullOrWhiteSpace(pedida)
                ? "En proto la versión es obligatoria y semántica (X.Y.Z), también con prerelease."
                : $"Con prerelease, la versión base («{nucleo}») debe ser estable (X.Y.Z)."));
        }
        return Versionar(tipo, $"{estable.Nucleo}-{pre}", spec);
    }

    /// <summary>
    /// La versión que se publica: la de la petición o la del spec, que deben cuadrar (plan 00 §8.2, regla 1: la versión la
    /// decide el repo).
    /// </summary>
    internal static (VersionSemantica? Version, (string Codigo, string Mensaje)? Problema) Versionar(TipoContrato tipo, string? pedida, SpecAnalizado spec)
    {
        if (tipo == TipoContrato.Proto)
        {
            var deProto = VersionSemantica.Leer(pedida);
            if (deProto is null)
            {
                return (null, ("version_invalida", "En proto la versión es obligatoria y semántica (X.Y.Z o X.Y.Z-<prerelease>, sin +metadatos)."));
            }
            if (spec.VersionDelSpec is { } delPaquete && delPaquete != deProto.Mayor.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                return (null, ("version_distinta_del_spec", $"El paquete es v{delPaquete}: la versión debe ser {delPaquete}.x.y."));
            }
            return (deProto, null);
        }
        var delSpec = VersionSemantica.Leer(spec.VersionDelSpec);
        if (delSpec is null)
        {
            return (null, ("version_invalida", $"info.version («{spec.VersionDelSpec}») debe ser una versión semántica (X.Y.Z)."));
        }
        if (string.IsNullOrWhiteSpace(pedida))
        {
            return (delSpec, null);
        }
        var version = VersionSemantica.Leer(pedida);
        if (version is null)
        {
            return (null, ("version_invalida", "La versión debe ser semántica (X.Y.Z o X.Y.Z-<prerelease>, sin +metadatos)."));
        }
        var cuadra = version == delSpec || (delSpec.EsEstable && !version.EsEstable && version.Nucleo == delSpec.Nucleo);
        return cuadra
            ? (version, null)
            : (null, ("version_distinta_del_spec", $"La versión {version} no cuadra con info.version {delSpec}: debe ser {delSpec} o {delSpec}-<prerelease>."));
    }

    /// <summary>El changelog de oasdiff (o <c>[]</c>) y si trae algo de nivel 3 (rompe compatibilidad).</summary>
    private static (string Changelog, bool TieneBreaking) LeerChangelog(string? texto, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return ("[]", false);
        }
        if (texto.Length > ChangelogMaximo)
        {
            errores["changelog"] = ["El changelog va hasta 1 MB."];
            return ("[]", false);
        }
        try
        {
            using var documento = JsonDocument.Parse(texto, new JsonDocumentOptions { MaxDepth = 64 });
            if (documento.RootElement.ValueKind != JsonValueKind.Array)
            {
                errores["changelog"] = ["El changelog es la salida JSON de oasdiff changelog: un arreglo."];
                return ("[]", false);
            }
            var breaking = documento.RootElement.EnumerateArray().Any(c =>
                c.ValueKind == JsonValueKind.Object && c.TryGetProperty("level", out var nivel) && nivel.ValueKind == JsonValueKind.Number
                && nivel.TryGetInt32(out var n) && n >= 3);
            return (documento.RootElement.GetRawText(), breaking);
        }
        catch (JsonException)
        {
            errores["changelog"] = ["El changelog no es JSON válido."];
            return ("[]", false);
        }
    }

    private static JsonHttpResult<ErrorPublicacion> Prohibido(AutorizacionFuente rechazo) =>
        TypedResults.Json(new ErrorPublicacion(rechazo.Codigo!, rechazo.Mensaje!, null), statusCode: StatusCodes.Status403Forbidden);

    private static UnprocessableEntity<ErrorPublicacion> Invalido(string codigo, string mensaje, IReadOnlyList<string>? errores) =>
        TypedResults.UnprocessableEntity(new ErrorPublicacion(codigo, mensaje, errores));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación de contratos: {Repositorio} rechazado por la lista blanca ({Codigo})")]
    private static partial void Rechazada(ILogger log, string repositorio, string codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicación de contratos: {Repositorio} publicó {Aplicativo}/{Contrato} {Version}")]
    private static partial void Publicada(ILogger log, string repositorio, string aplicativo, string contrato, string version);
}

/// <summary>Los valores del módulo como texto (los de la API y la base son los mismos, en snake_case).</summary>
internal static partial class Valores
{
    public static readonly IReadOnlyDictionary<string, TipoContrato> Tipos = new Dictionary<string, TipoContrato>(StringComparer.Ordinal)
    {
        ["openapi"] = TipoContrato.Openapi,
        ["asyncapi"] = TipoContrato.Asyncapi,
        ["proto"] = TipoContrato.Proto,
    };

    public static TipoContrato? Tipo(string? texto) => texto is not null && Tipos.TryGetValue(texto.Trim(), out var tipo) ? tipo : null;

    public static string Texto(TipoContrato tipo) => Tipos.First(p => p.Value == tipo).Key;

    public static string Texto(FormatoSpec formato) => formato switch
    {
        FormatoSpec.Json => "json",
        FormatoSpec.Yaml => "yaml",
        _ => "proto",
    };

    public static string Texto(EstadoVersion estado) => estado switch
    {
        EstadoVersion.Vigente => "vigente",
        EstadoVersion.Deprecada => "deprecada",
        _ => "retirada",
    };

    public static string Texto(VisibilidadContrato visibilidad) => visibilidad == VisibilidadContrato.Interna ? "interna" : "compartible";

    public static bool CodigoValido(string? codigo) => codigo is { Length: > 0 and <= ContratosDbContext.LargoCodigo } && FormatoCodigo().IsMatch(codigo);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FormatoCodigo();
}
