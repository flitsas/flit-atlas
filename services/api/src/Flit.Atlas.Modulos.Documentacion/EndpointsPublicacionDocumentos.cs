using System.ComponentModel;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// <c>POST /api/v1/documentacion/publicaciones</c> (HU 5.2, ATLAS-ADR-0053 §4): un lote de un commit, solo con el token
/// OIDC de GitHub Actions. Cada documento se resuelve por separado, en su propia transacción: que uno se rechace no frena a
/// los demás. El servidor valida todo otra vez y nunca confía en lo que revisó la CI.
/// </summary>
internal static partial class EndpointsPublicacionDocumentos
{
    public const string TipoFuente = "docs";
    public const int MaximoDocumentos = 100;
    public const int MaximoRutas = 5000;
    private const long CuerpoMaximo = 8 * 1024 * 1024;

    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => CuerpoMaximo;
    }

    [Description("Un archivo del repo.")]
    public sealed record ArchivoLote(
        [property: Description("Ruta en el repo; con el aplicativo, identifica al documento.")] string Ruta,
        [property: Description("El archivo completo (front-matter y MDX).")] string Contenido);

    [Description("Lo que publica un commit.")]
    public sealed record SolicitudLote(
        [property: Description("Hasta 100 archivos; el lote va hasta 8 MB. Un repo con más los manda en varios lotes.")] IReadOnlyList<ArchivoLote> Documentos,
        [property: Description("Opcional, solo en el último lote: todas las rutas que siguen en el repo. Un documento del repo que no esté aquí sale del canal de este ambiente y, sin canales, queda archivado; sus versiones no se borran. No puede ir vacía.")] IReadOnlyList<string>? RutasVigentes = null);

    [Description("Qué pasó con un archivo.")]
    public sealed record ResultadoArchivo(
        string Ruta,
        [property: Description("creada (versión nueva), igual (ya existía con el mismo hash: solo se mueve el canal) o rechazada.")] string Resultado,
        Guid? DocumentoId,
        [property: Description("Número de la versión que quedó en el canal.")] int? Version,
        [property: Description("Si se rechazó: front_matter_invalido, documento_invalido, mdx_invalido, ruta_invalida, ruta_de_otro_repo, aplicativo_no_autorizado, tipo_no_autorizado, fuente_no_autorizada, conflicto_concurrente o contenido_no_indexable.")] string? Codigo,
        IReadOnlyList<string>? Errores);

    [Description("Resultado del lote.")]
    public sealed record ResultadoLote(
        [property: Description("dev, qa o pdn, según la rama.")] string Ambiente,
        IReadOnlyList<ResultadoArchivo> Resultados,
        [property: Description("Rutas que salieron del canal de este ambiente porque ya no están en el repo.")] IReadOnlyList<string> Retiradas);

    [Description("Por qué no se publicó el lote, con un código estable.")]
    public sealed record ErrorLote(
        [property: Description("fuente_no_autorizada (403) o rama_sin_ambiente (422).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/publicaciones", PublicarAsync)
            .RequierePublicacionCi()
            .WithMetadata(new LimiteCuerpo())
            .WithName("PublicarLoteDocumentos")
            .WithSummary("Publicar un lote de documentos desde la CI")
            .WithDescription(
                "Solo con el token OIDC de GitHub Actions del workflow reutilizable oficial, desde un repo, rama y workflow de la " +
                "lista blanca con el tipo docs. El aplicativo de cada archivo, que dice su front-matter, debe estar autorizado para " +
                "el repo. El ambiente sale de la rama (develop → dev, staging → qa, release → pdn). Responde 200 con el resultado de " +
                "cada archivo: creada, igual (mismo hash: no crea versión, solo mueve el canal) o rechazada con su código. La cookie " +
                "de la PWA no sirve aquí.")
            .Produces<ResultadoLote>()
            .Produces<ErrorLote>(StatusCodes.Status403Forbidden)
            .Produces<ErrorLote>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> PublicarAsync(
        [Description("El lote.")] SolicitudLote solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IFuentesPublicacion fuentes,
        IOptions<OpcionesDocumentacion> opciones, TiempoReal.AvisosDocumentos avisos, TimeProvider reloj, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(typeof(EndpointsPublicacionDocumentos));
        var publicador = http.Publicador();

        var delRepo = await fuentes.DelRepositorioAsync(publicador.Repositorio, ct);
        var validas = ListaBlancaPublicacion.DelPublicador(publicador, delRepo).Where(f => f.Tipos.Contains(TipoFuente, StringComparer.Ordinal)).ToList();
        if (validas.Count == 0)
        {
            Rechazado(log, publicador.Repositorio);
            return Results.Json(new ErrorLote("fuente_no_autorizada",
                $"{publicador.Repositorio} no puede publicar documentación desde la rama «{publicador.Rama}» con este workflow."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var documentos = solicitud.Documentos ?? [];
        if (documentos.Count > MaximoDocumentos)
        {
            errores["documentos"] = [$"Un lote va hasta {MaximoDocumentos} archivos."];
        }
        if (documentos.Select(d => d.Ruta).Distinct(StringComparer.Ordinal).Count() != documentos.Count)
        {
            errores["documentos"] = ["Hay rutas repetidas en el lote."];
        }
        if (solicitud.RutasVigentes is { } vigentes && (vigentes.Count == 0 || vigentes.Count > MaximoRutas))
        {
            // Una lista vacía sacaría del canal todos los documentos del repo: casi siempre es un error de la CI.
            errores["rutasVigentes"] = [$"Si se manda, lleva entre 1 y {MaximoRutas} rutas."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var ambiente = opciones.Value.AmbienteDe(publicador.Rama);
        if (ambiente is null)
        {
            return Results.UnprocessableEntity(new ErrorLote("rama_sin_ambiente", $"La rama «{publicador.Rama}» no despliega ningún ambiente."));
        }

        var resultados = new List<ResultadoArchivo>(documentos.Count);
        foreach (var archivo in documentos)
        {
            resultados.Add(await PublicarArchivoAsync(archivo, ambiente, publicador, delRepo, http, contexto, plataforma, reloj, ct));
            contexto.ChangeTracker.Clear();
        }
        var retiradas = solicitud.RutasVigentes is { } rutas
            ? await RetirarAusentesAsync(rutas, ambiente, publicador, validas, http, contexto, plataforma, reloj, ct)
            : [];

        // Quien tenga abierto uno de estos documentos ve la versión nueva o el canal que se movió; sin nadie en el grupo no cuesta nada.
        foreach (var r in resultados.Where(r => r.Resultado != "rechazada"))
        {
            if (r.DocumentoId is { } documento && r.Version is { } numero)
            {
                await avisos.VersionNuevaAsync(documento, numero);
            }
        }

        var creadas = resultados.Count(r => r.Resultado == "creada");
        var iguales = resultados.Count(r => r.Resultado == "igual");
        var rechazadas = resultados.Count(r => r.Resultado == "rechazada");
        Publicado(log, publicador.Repositorio, ambiente, creadas, iguales, rechazadas, retiradas.Count);
        return Results.Ok(new ResultadoLote(ambiente, resultados, retiradas));
    }

    private static async Task<ResultadoArchivo> PublicarArchivoAsync(
        ArchivoLote archivo, string ambiente, PublicadorCi publicador, IReadOnlyList<FuentePublicacionLeida> delRepo, HttpContext http,
        DocumentacionDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var ruta = archivo.Ruta ?? string.Empty;
        ResultadoArchivo Rechazo(string codigo, params IReadOnlyList<string> detalle) => new(ruta, "rechazada", null, null, codigo, detalle);

        if (!ReglasDocumentos.RutaValida(ruta))
        {
            return Rechazo("ruta_invalida", "La ruta es relativa, sin «..», y termina en .md o .mdx.");
        }
        var texto = ReglasDocumentos.Normalizar(archivo.Contenido);
        var (leido, erroresFrente) = FrontMatter.Leer(texto);
        if (leido is null)
        {
            return Rechazo("front_matter_invalido", erroresFrente);
        }
        var aplicativo = leido.Texto("aplicativo") ?? string.Empty;
        var autorizacion = ListaBlancaPublicacion.Autorizar(publicador, delRepo, aplicativo, TipoFuente);
        if (!autorizacion.Autorizada)
        {
            return Rechazo(autorizacion.Codigo!, autorizacion.Mensaje!);
        }
        var (foto, erroresFoto) = ReglasDocumentos.Leer(
            leido.Texto("tipo"), leido.Texto("titulo"), leido.Cuerpo, leido.Texto("visibilidad"), leido.Lista("audiencias"), leido.Texto("responsable"));
        if (foto is null)
        {
            return Rechazo("documento_invalido", [.. erroresFoto.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}"))]);
        }
        var erroresMdx = ValidadorMdx.Validar(foto.Contenido);
        if (erroresMdx.Count > 0)
        {
            return Rechazo("mdx_invalido", erroresMdx);
        }

        var aplicativoId = autorizacion.Fuente!.AplicativoId;
        var sha = ReglasDocumentos.Sha256(texto);
        var ahora = reloj.GetUtcNow();
        try
        {
            var documento = await contexto.Documentos.FirstOrDefaultAsync(d => d.AplicativoId == aplicativoId && d.Ruta == ruta, ct);
            if (documento is not null && documento.Repositorio != publicador.Repositorio)
            {
                return Rechazo("ruta_de_otro_repo", "Este documento lo publica otro repo.");
            }
            if (documento is null)
            {
                documento = new Documento
                {
                    AplicativoId = aplicativoId, Origen = OrigenDocumento.Repo, Ruta = ruta, Repositorio = publicador.Repositorio, Tipo = foto.Tipo,
                    Titulo = foto.Titulo, Visibilidad = foto.Visibilidad, Audiencias = [.. foto.Audiencias], Responsable = foto.Responsable,
                };
                contexto.Documentos.Add(documento);
            }
            documento.Estado = EstadoDocumento.Publicado;
            var canales = await contexto.Canales.Where(c => c.DocumentoId == documento.Id).ToListAsync(ct);

            var existente = await contexto.Versiones.AsNoTracking().Where(v => v.DocumentoId == documento.Id && v.Sha256 == sha)
                .Select(v => (int?)v.Numero).FirstOrDefaultAsync(ct);
            if (existente is { } numero)
            {
                var cambio = PublicacionDocumentos.MoverCanal(contexto, canales, documento.Id, ambiente, numero);
                await PublicacionDocumentos.RecalcularVigenteAsync(contexto, documento, canales, ct);
                if (cambio)
                {
                    await PublicacionDocumentos.GuardarAsync(contexto, plataforma, PublicacionDocumentos.Evento(http, ahora, PublicacionDocumentos.AccionCanalActualizado,
                        documento.Id, numero, TipoActor.Servicio, publicador.Firma, new { ambiente, sha256 = sha, publicador.Repositorio, publicador.Rama, commit = publicador.Commit }),
                        null, ct);
                }
                else
                {
                    await contexto.SaveChangesAsync(ct);
                }
                return new ResultadoArchivo(ruta, "igual", documento.Id, numero, null, null);
            }

            // El estado del front-matter (plan 03 §18.3): lo no aprobado solo lo ven los roles técnicos y los responsables.
            var estado = leido.Texto("estado");
            var esBorrador = estado is "borrador" or "en_revision" || (foto.Visibilidad == VisibilidadDocumento.PublicoClientes && estado != "aprobado");
            documento.VersionActual++;
            var version = new VersionDocumento
            {
                DocumentoId = documento.Id, Numero = documento.VersionActual, Tipo = foto.Tipo, Titulo = foto.Titulo, Contenido = foto.Contenido,
                Visibilidad = foto.Visibilidad, Audiencias = [.. foto.Audiencias], Responsable = foto.Responsable, EsBorrador = esBorrador, Sha256 = sha,
                Ruta = ruta, Repositorio = publicador.Repositorio, Rama = publicador.Rama, CommitSha = publicador.Commit, Workflow = publicador.Workflow,
                WorkflowDelJob = publicador.WorkflowDelJob, Ejecucion = publicador.Ejecucion, PublicadoPor = publicador.Firma, PublicadoEn = ahora,
            };
            contexto.Versiones.Add(version);
            PublicacionDocumentos.MoverCanal(contexto, canales, documento.Id, ambiente, version.Numero);
            await PublicacionDocumentos.RecalcularVigenteAsync(contexto, documento, canales, ct);
            await PublicacionDocumentos.GuardarAsync(contexto, plataforma, PublicacionDocumentos.Evento(http, ahora, PublicacionDocumentos.AccionVersionPublicada,
                documento.Id, version.Numero, TipoActor.Servicio, publicador.Firma, new
                {
                    aplicativo, ambiente, ruta, sha256 = sha, visibilidad = ReglasDocumentos.Texto(foto.Visibilidad), esBorrador,
                    publicador.Repositorio, publicador.Rama, commit = publicador.Commit, publicador.Workflow, publicador.WorkflowDelJob, publicador.Ejecucion,
                }),
                new PublicacionDocumentos.DatosVersionPublicada(documento.Id, version.Id, version.Numero, aplicativoId, aplicativo, ReglasDocumentos.Texto(OrigenDocumento.Repo),
                    foto.Tipo, ReglasDocumentos.Texto(foto.Visibilidad), esBorrador, sha, ambiente, publicador.Repositorio, publicador.Rama, publicador.Commit), ct);
            return new ResultadoArchivo(ruta, "creada", documento.Id, version.Numero, null, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Rechazo("conflicto_concurrente", "Otra ejecución publicó este documento a la vez: vuelve a correr el workflow.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ProgramLimitExceeded })
        {
            return Rechazo("contenido_no_indexable", "El documento es demasiado grande para indexarlo: divídelo.");
        }
    }

    /// <summary>
    /// Los documentos del repo que ya no están en <paramref name="rutas"/> salen del canal de este ambiente; sin canales
    /// quedan archivados. No se borra ninguna versión. Solo toca aplicativos que el repo puede publicar.
    /// </summary>
    private static async Task<List<string>> RetirarAusentesAsync(
        IReadOnlyList<string> rutas, string ambiente, PublicadorCi publicador, IReadOnlyList<FuentePublicacionLeida> validas, HttpContext http,
        DocumentacionDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var aplicativos = validas.Select(f => f.AplicativoId).Distinct().ToArray();
        var vigentes = rutas.ToArray();
        var ausentes = await contexto.Documentos
            .Where(d => d.Origen == OrigenDocumento.Repo && d.Repositorio == publicador.Repositorio && aplicativos.Contains(d.AplicativoId) && !vigentes.Contains(d.Ruta!))
            .Where(d => contexto.Canales.Any(c => c.DocumentoId == d.Id && c.Ambiente == ambiente))
            .ToListAsync(ct);

        var retiradas = new List<string>(ausentes.Count);
        var ahora = reloj.GetUtcNow();
        foreach (var documento in ausentes)
        {
            var canales = await contexto.Canales.Where(c => c.DocumentoId == documento.Id).ToListAsync(ct);
            var canal = canales.Single(c => c.Ambiente == ambiente);
            contexto.Canales.Remove(canal);
            canales.Remove(canal);
            if (canales.Count == 0)
            {
                documento.Estado = EstadoDocumento.Archivado;
            }
            else
            {
                await PublicacionDocumentos.RecalcularVigenteAsync(contexto, documento, canales, ct);
            }
            await PublicacionDocumentos.GuardarAsync(contexto, plataforma, PublicacionDocumentos.Evento(http, ahora, PublicacionDocumentos.AccionCanalRetirado,
                documento.Id, null, TipoActor.Servicio, publicador.Firma, new { ambiente, archivado = canales.Count == 0, publicador.Repositorio, publicador.Rama, commit = publicador.Commit }),
                null, ct);
            retiradas.Add(documento.Ruta!);
        }
        return retiradas;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publicación de documentos: {Repositorio} rechazado por la lista blanca")]
    private static partial void Rechazado(ILogger log, string repositorio);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicación de documentos: {Repositorio} en {Ambiente}: {Creadas} creadas, {Iguales} iguales, {Rechazadas} rechazadas, {Retiradas} retiradas")]
    private static partial void Publicado(ILogger log, string repositorio, string ambiente, int creadas, int iguales, int rechazadas, int retiradas);
}
