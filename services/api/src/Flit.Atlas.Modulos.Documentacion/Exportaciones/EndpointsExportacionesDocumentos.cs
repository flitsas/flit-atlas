using System.ComponentModel;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Almacenamiento;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Documentacion.Exportaciones;

/// <summary>
/// Exportar un documento o una colección (HU 5.6, ATLAS-ADR-0053 §7) a PDF, Word, HTML y Markdown, con la cola de atlas-docs
/// (ATLAS-ADR-0042). Solicitar exige <c>atlas.docs.exportar</c> y un ingreso reciente, resuelve cada documento con el mismo
/// predicado de visibilidad que el lector (lo que no se ve responde 404 aunque sea uno solo) y congela la entrada en el S3,
/// con el MDX ya convertido y los componentes vivos con los valores que la persona puede ver. Cada persona ve y descarga
/// solo las suyas; la descarga es una URL firmada de 15 minutos. Solicitar, descargar y fallar quedan en la auditoría.
/// </summary>
internal static class EndpointsExportacionesDocumentos
{
    public const string RecursoAuditoria = "documentacion.exportacion";
    public const string AccionSolicitada = "documentacion.exportacion_solicitada";
    public const string AccionDescargada = "documentacion.exportacion_descargada";
    public const string AccionFallida = "documentacion.exportacion_fallida";
    public const string CodigoEnCurso = "exportaciones_en_curso";

    public const string OrganizacionInterna = "FLIT S.A.S. · uso interno";

    public const int MaximoDocumentos = 50;

    private const int LimiteListado = 20;
    private const int MaximoNombreEntrada = 300;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lo que se publica en la cola: la misma forma de <c>exportacion_solicitada</c> de contratos, sin su origen.</summary>
    internal sealed record DatosSolicitada(Guid ExportacionId, IReadOnlyList<string> Formatos, string EntradaClave);

    /// <summary>Entrada de atlas-docs (<c>EntradaDocumentos</c> de <c>services/docs</c>); no admite campos de más.</summary>
    internal sealed record Entrada(MetadatosEntrada Metadatos, IReadOnlyList<DocumentoEntrada> Documentos);

    internal sealed record MetadatosEntrada(string Nombre, string Organizacion, DateOnly Fecha);

    internal sealed record DocumentoEntrada(string Titulo, string Tipo, int Version, string Responsable, DateOnly Publicado, string Sha256, string Markdown);

    private static readonly Dictionary<string, string> TiposLegibles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["manual_usuario"] = "Manual de usuario",
        ["guia_funcional"] = "Guía funcional",
        ["guia_integracion"] = "Guía de integración",
        ["runbook"] = "Runbook",
        ["ficha_comercial"] = "Ficha comercial",
        ["adr"] = "ADR",
        ["nota_version"] = "Nota de versión",
    };

    [Description("Un documento a exportar.")]
    public sealed record DocumentoAExportar(Guid DocumentoId, [property: Description("Número de versión; vacío = la vigente.")] int? Version);

    [Description("Qué exportar y en qué formatos.")]
    public sealed record SolicitudExportacionDocumentos(
        [property: Description("Uno o, como colección, hasta 50 documentos, en el orden en que irán.")] IReadOnlyList<DocumentoAExportar>? Documentos,
        [property: Description("Nombre de la colección, hasta 200 caracteres; de un solo documento, su título.")] string? Nombre,
        [property: Description("html, pdf, word, markdown o zip; el ZIP con todo siempre se agrega.")] IReadOnlyList<string?>? Formatos);

    [Description("Un archivo generado.")]
    public sealed record ArchivoExportacionDocumentos(
        [property: Description("Ruta dentro de la exportación; es el valor de archivo en la descarga.")] string Ruta,
        [property: Description("Nombre con que se descarga.")] string Nombre,
        [property: Description("html, pdf, word, markdown o zip.")] string Formato,
        long Bytes);

    [Description("Por qué falló una exportación.")]
    public sealed record ErrorDeExportacionDocumentos(
        [property: Description("pdf_no_disponible, entrada_invalida, tiempo_agotado o interno.")] string Codigo,
        string Mensaje);

    [Description("Una exportación de documentos y su estado.")]
    public sealed record ExportacionDocumentosDto(
        Guid Id,
        [property: Description("pendiente, procesando, lista, fallida o vencida.")] string Estado,
        string Nombre,
        [property: Description("El documento, si es de uno solo.")] Guid? DocumentoId,
        IReadOnlyList<string> Formatos,
        DateTimeOffset SolicitadaEn,
        DateTimeOffset? IniciadaEn,
        [property: Description("Cuándo quedó lista o fallida.")] DateTimeOffset? ListaEn,
        [property: Description("Hasta cuándo se puede descargar; después se borran los archivos.")] DateTimeOffset? VenceEn,
        [property: Description("SHA-256 del contenido exportado (va en la portada de cada formato).")] string? HashContenido,
        IReadOnlyList<ArchivoExportacionDocumentos> Archivos,
        ErrorDeExportacionDocumentos? Error);

    [Description("Las exportaciones de documentos de la persona, de la más reciente a la más antigua.")]
    public sealed record ListadoExportacionesDocumentos(IReadOnlyList<ExportacionDocumentosDto> Elementos);

    [Description("URL firmada para descargar un archivo.")]
    public sealed record DescargaExportacionDocumentos(
        [property: Description("URL del almacenamiento S3, firmada y de un solo archivo; deja de servir al vencer.")] Uri Url,
        string Nombre,
        long Bytes,
        [property: Description("Cuándo vence la URL (15 minutos).")] DateTimeOffset VenceEn);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorExportacionDocumentos(
        [property: Description("no_encontrado (404); sin_version, no_lista (409); vencida (410); almacenamiento_no_disponible (503).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/exportaciones", SolicitarAsync)
            .RequierePermiso(PermisosDocumentacion.Exportar)
            .RequiereReautenticacion()
            .ConLimiteDeExportacion()
            .WithName("SolicitarExportacionDocumentos")
            .WithSummary("Exportar un documento o una colección")
            .WithDescription("Deja la entrada en el S3, con el MDX convertido y los componentes vivos con los valores que la persona puede ver, y encola la generación: responde 202 sin esperarla. 404 si alguno de los documentos o versiones no existe o la persona no puede leerlo; 409 sin_version si un documento aún no tiene versión. 429 exportaciones_en_curso si ya tiene el máximo en proceso (2 por defecto), con Retry-After. Requiere atlas.docs.exportar y un ingreso reciente.")
            .Produces<ExportacionDocumentosDto>(StatusCodes.Status202Accepted)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        grupo.MapGet("/exportaciones", ListarAsync)
            .RequierePermiso(PermisosDocumentacion.Exportar)
            .WithName("ListarExportacionesDocumentos")
            .WithSummary("Mis exportaciones de documentos")
            .WithDescription("Las últimas 20 de la persona, de la más reciente a la más antigua. Requiere atlas.docs.exportar.")
            .Produces<ListadoExportacionesDocumentos>();

        grupo.MapGet("/exportaciones/{exportacionId:guid}", ObtenerAsync)
            .RequierePermiso(PermisosDocumentacion.Exportar)
            .WithName("ObtenerExportacionDocumentos")
            .WithSummary("Estado de una exportación de documentos")
            .WithDescription("pendiente, procesando, lista (con sus archivos), fallida (con el error) o vencida. Solo la ve quien la pidió (404 para los demás). Requiere atlas.docs.exportar.")
            .Produces<ExportacionDocumentosDto>()
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status404NotFound);

        grupo.MapGet("/exportaciones/{exportacionId:guid}/descarga", DescargarAsync)
            .RequierePermiso(PermisosDocumentacion.Exportar)
            .WithName("DescargarExportacionDocumentos")
            .WithSummary("URL firmada de descarga")
            .WithDescription("Una URL del S3 firmada por 15 minutos para el ZIP o, con archivo, para uno de sus archivos. 409 no_lista; 410 vencida. Auditado. Requiere atlas.docs.exportar.")
            .Produces<DescargaExportacionDocumentos>()
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status409Conflict)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status410Gone)
            .Produces<ErrorExportacionDocumentos>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> SolicitarAsync(
        [Description("Qué exportar y en qué formatos.")] SolicitudExportacionDocumentos solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen, IPermisosDeSesion permisosDeSesion,
        ILectorAplicativos lector, ILectorContratos contratos, ILectorResponsables responsables, ISoporteDeTurno soporte,
        IOptions<OpcionesExportacionesDocumentos> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var formatos = FormatosDocumentos.Normalizar(solicitud?.Formatos);
        if (formatos is null)
        {
            errores["formatos"] = [$"Elige uno o más de: {string.Join(", ", FormatosDocumentos.Todos)}."];
        }
        var pedidos = solicitud?.Documentos ?? [];
        if (pedidos.Count is 0 or > MaximoDocumentos || pedidos.Select(p => p.DocumentoId).Distinct().Count() != pedidos.Count)
        {
            errores["documentos"] = [$"Indica de 1 a {MaximoDocumentos} documentos, sin repetirlos."];
        }
        var nombre = solicitud?.Nombre?.Trim();
        if (nombre is { Length: > DocumentacionDbContext.LargoTitulo })
        {
            errores["nombre"] = [$"El nombre va hasta {DocumentacionDbContext.LargoTitulo} caracteres."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (!almacen.Disponible)
        {
            return SinAlmacen();
        }
        var persona = Persona(http.User);
        if (await EnCursoAsync(contexto, persona, ct) >= opciones.Value.MaximoEnCursoPorPersona)
        {
            return EnCurso(http, opciones.Value);
        }

        var acceso = await AccesoDocumentos.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        var ids = pedidos.Select(p => p.DocumentoId).ToArray();
        var documentos = await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        if (documentos.Count != pedidos.Count)
        {
            return NoEncontrado("Alguno de los documentos no existe o no tienes acceso.");
        }

        var ahora = reloj.GetUtcNow();
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, Bogota).DateTime);
        var permisos = await permisosDeSesion.ObtenerAsync(http.User, ct);
        var vivos = new ValoresVivos(permisos, acceso.Aplicativos, contratos, responsables, soporte, plataforma, hoy, ct);
        var entradas = new List<DocumentoEntrada>(pedidos.Count);
        var exportados = new List<object>(pedidos.Count);
        foreach (var pedido in pedidos)
        {
            var documento = documentos[pedido.DocumentoId];
            var numero = pedido.Version ?? documento.VersionVigente;
            if (numero is null)
            {
                return Error(StatusCodes.Status409Conflict, "sin_version", "Un documento sin versión publicada no se puede exportar.");
            }
            var version = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking(), documento.AplicativoId)
                .FirstOrDefaultAsync(v => v.DocumentoId == documento.Id && v.Numero == numero, ct);
            if (version is null)
            {
                return NoEncontrado("Alguno de los documentos no existe o no tienes acceso.");
            }
            entradas.Add(new DocumentoEntrada(version.Titulo, TiposLegibles.GetValueOrDefault(version.Tipo) ?? version.Tipo, version.Numero, version.Responsable,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.PublicadoEn, Bogota).DateTime), version.Sha256.Trim(),
                await ConversorMdx.ConvertirAsync(version.Contenido, vivos.ResolverAsync)));
            exportados.Add(new { documentoId = documento.Id, version = version.Numero, sha256 = version.Sha256, visibilidad = ReglasDocumentos.Texto(version.Visibilidad) });
        }

        var titulo = !string.IsNullOrEmpty(nombre) ? nombre
            : entradas.Count == 1 ? entradas[0].Titulo
            : $"Colección de {entradas.Count} documentos";
        var entrada = new Entrada(new MetadatosEntrada(titulo.Length <= MaximoNombreEntrada ? titulo : titulo[..MaximoNombreEntrada], OrganizacionInterna, hoy), entradas);
        var id = Guid.CreateVersion7();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entrada, Json);
        var exportacion = new ExportacionDocumentos
        {
            Id = id,
            SolicitadaPor = persona,
            Nombre = titulo,
            DocumentoId = pedidos.Count == 1 ? pedidos[0].DocumentoId : null,
            Documentos = JsonSerializer.Serialize(exportados, Json),
            Formatos = [.. formatos!],
            EntradaClave = ClaveEntrada(id),
            EntradaSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            SolicitadaEn = ahora,
        };

        try
        {
            await almacen.SubirAsync(exportacion.EntradaClave, bytes, "application/json", ct);
        }
        catch (AlmacenNoDisponibleException)
        {
            return SinAlmacen();
        }
        var confirmando = false;
        try
        {
            contexto.Exportaciones.Add(exportacion);
            await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
            // Dos solicitudes simultáneas se ordenan: la segunda ve la primera al contar.
            await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ClaseCandado}, {ClaveCandado(persona)})", ct);
            if (await EnCursoAsync(contexto, persona, ct) >= opciones.Value.MaximoEnCursoPorPersona)
            {
                await BorrarSubidoAsync(almacen, id);
                return EnCurso(http, opciones.Value);
            }
            await contexto.SaveChangesAsync(ct);
            plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
            await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
            plataforma.Auditoria.Add(Evento(http, ahora, AccionSolicitada, exportacion, new { formatos = exportacion.Formatos, documentos = exportados, entradaSha256 = exportacion.EntradaSha256 }));
            plataforma.EncolarEvento(EventosDocumentacion.ExportacionSolicitada, new DatosSolicitada(id, exportacion.Formatos, exportacion.EntradaClave));
            await plataforma.SaveChangesAsync(ct);
            confirmando = true;
            await transaccion.CommitAsync(ct);
            await plataforma.Database.UseTransactionAsync(null, ct);
        }
        catch when (!confirmando)
        {
            // La entrada no queda huérfana en el S3 (F4-SEC-28). Si falla al confirmar no se borra: el COMMIT es incierto.
            await BorrarSubidoAsync(almacen, id);
            throw;
        }
        return Results.Accepted($"/api/v1/documentacion/exportaciones/{id}", Dto(exportacion));
    }

    private static async Task<IResult> ListarAsync(HttpContext http, DocumentacionDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(http.User);
        var filas = await contexto.Exportaciones.AsNoTracking().Where(e => e.SolicitadaPor == persona)
            .OrderByDescending(e => e.SolicitadaEn).ThenByDescending(e => e.Id).Take(LimiteListado).ToListAsync(ct);
        return Results.Ok(new ListadoExportacionesDocumentos([.. filas.Select(Dto)]));
    }

    private static async Task<IResult> ObtenerAsync([Description("Id de la exportación.")] Guid exportacionId, HttpContext http, DocumentacionDbContext contexto, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, ct);
        return exportacion is null ? NoEncontrado("La exportación no existe.") : Results.Ok(Dto(exportacion));
    }

    private static async Task<IResult> DescargarAsync(
        [Description("Id de la exportación.")] Guid exportacionId,
        [Description("Ruta de un archivo de la exportación; vacío = el ZIP.")] string? archivo,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IAlmacenObjetos almacen,
        IOptions<OpcionesExportacionesDocumentos> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var exportacion = await PropiaAsync(contexto, exportacionId, http.User, ct);
        if (exportacion is null)
        {
            return NoEncontrado("La exportación no existe.");
        }
        if (exportacion.Estado == EstadoExportacionDocumentos.Vencida)
        {
            return Error(StatusCodes.Status410Gone, "vencida", "La exportación venció y sus archivos se borraron; solicítala de nuevo.");
        }
        if (exportacion.Estado != EstadoExportacionDocumentos.Lista)
        {
            return Error(StatusCodes.Status409Conflict, "no_lista", "La exportación aún no está lista.");
        }
        var archivos = LeerArchivos(exportacion.Archivos);
        var elegido = string.IsNullOrWhiteSpace(archivo)
            ? archivos.FirstOrDefault(a => a.Clave == exportacion.Objeto)
            : archivos.FirstOrDefault(a => a.Ruta == archivo);
        if (elegido is null)
        {
            return NoEncontrado("El archivo no está en esta exportación.");
        }
        if (!almacen.Disponible)
        {
            return SinAlmacen();
        }
        var ahora = reloj.GetUtcNow();
        var nombre = NombreArchivo(elegido.Ruta);
        var url = almacen.UrlFirmada(elegido.Clave, opciones.Value.DuracionUrl, nombre);
        plataforma.Auditoria.Add(Evento(http, ahora, AccionDescargada, exportacion, new { archivo = elegido.Ruta, formato = elegido.Formato, bytes = elegido.Bytes, sha256 = elegido.Sha256, hashContenido = exportacion.HashContenido }));
        await plataforma.SaveChangesAsync(ct);
        return Results.Ok(new DescargaExportacionDocumentos(url, nombre, elegido.Bytes, ahora + opciones.Value.DuracionUrl));
    }

    // --- Piezas comunes ---

    public static string Prefijo(Guid id) => $"exportaciones/{id:D}/";

    public static string ClaveEntrada(Guid id) => Prefijo(id) + "entrada.json";

    public static IReadOnlyList<ArchivoExportadoDocumentos> LeerArchivos(string json) =>
        JsonSerializer.Deserialize<List<ArchivoExportadoDocumentos>>(json, Json) ?? [];

    public static string Texto(EstadoExportacionDocumentos e) => e switch
    {
        EstadoExportacionDocumentos.Pendiente => "pendiente",
        EstadoExportacionDocumentos.Procesando => "procesando",
        EstadoExportacionDocumentos.Lista => "lista",
        EstadoExportacionDocumentos.Fallida => "fallida",
        _ => "vencida",
    };

    private static string NombreArchivo(string ruta)
    {
        var i = ruta.LastIndexOf('/');
        return i >= 0 ? ruta[(i + 1)..] : ruta;
    }

    private static Task<int> EnCursoAsync(DocumentacionDbContext contexto, Guid persona, CancellationToken ct) =>
        contexto.Exportaciones.AsNoTracking()
            .CountAsync(e => e.SolicitadaPor == persona && (e.Estado == EstadoExportacionDocumentos.Pendiente || e.Estado == EstadoExportacionDocumentos.Procesando), ct);

    /// <summary>Primera clave del candado de exportaciones en curso (<c>pg_advisory_xact_lock(int, int)</c>): «EXPD».</summary>
    private const int ClaseCandado = 0x45585044;

    private static int ClaveCandado(Guid id)
    {
        var bytes = id.ToByteArray();
        return BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4) ^ BitConverter.ToInt32(bytes, 8) ^ BitConverter.ToInt32(bytes, 12);
    }

    private static async Task BorrarSubidoAsync(IAlmacenObjetos almacen, Guid id)
    {
        try
        {
            await almacen.BorrarPrefijoAsync(Prefijo(id), CancellationToken.None);
        }
#pragma warning disable CA1031 // Limpieza de mejor esfuerzo: si el S3 tampoco responde, queda para el ciclo de vida del bucket.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static IResult EnCurso(HttpContext http, OpcionesExportacionesDocumentos opciones)
    {
        var segundos = Math.Max(1, (int)Math.Ceiling(opciones.EsperaEnCurso.TotalSeconds));
        http.Response.Headers.RetryAfter = segundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: $"Ya tienes {opciones.MaximoEnCursoPorPersona} exportaciones de documentos en curso. Espera a que terminen e intenta de nuevo.",
            extensions: new Dictionary<string, object?> { ["codigo"] = CodigoEnCurso, ["reintentarEnSegundos"] = segundos });
    }

    private static Task<ExportacionDocumentos?> PropiaAsync(DocumentacionDbContext contexto, Guid id, ClaimsPrincipal usuario, CancellationToken ct)
    {
        var persona = Persona(usuario);
        return contexto.Exportaciones.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.SolicitadaPor == persona, ct);
    }

    internal static ExportacionDocumentosDto Dto(ExportacionDocumentos e)
    {
        var archivos = e.Estado == EstadoExportacionDocumentos.Lista
            ? LeerArchivos(e.Archivos).Select(a => new ArchivoExportacionDocumentos(a.Ruta, NombreArchivo(a.Ruta), a.Formato, a.Bytes)).ToList()
            : [];
        return new ExportacionDocumentosDto(e.Id, Texto(e.Estado), e.Nombre, e.DocumentoId, e.Formatos, e.SolicitadaEn, e.IniciadaEn, e.TerminadaEn, e.VenceEn,
            e.HashContenido?.Trim(), archivos, e.ErrorCodigo is { } codigo ? new ErrorDeExportacionDocumentos(codigo, e.Error ?? string.Empty) : null);
    }

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    /// <summary>Auditoría: ids, números, formatos y hashes; nunca el título ni el cuerpo.</summary>
    private static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, ExportacionDocumentos exportacion, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = exportacion.Id.ToString(),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, Json),
        CorrelacionId = http.TraceIdentifier,
    };

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorExportacionDocumentos(codigo, mensaje), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult SinAlmacen() =>
        Error(StatusCodes.Status503ServiceUnavailable, "almacenamiento_no_disponible", "El almacenamiento de exportaciones no está disponible; intenta más tarde.");
}

/// <summary>Formatos de un documento (ATLAS-ADR-0053 §7), en su orden.</summary>
internal static class FormatosDocumentos
{
    public const string Zip = "zip";

    public static readonly IReadOnlyList<string> Todos = ["html", "pdf", "word", "markdown", Zip];

    /// <summary>Sin repetidos, en el orden de <see cref="Todos"/> y siempre con el ZIP; null si alguno no existe o no hay ninguno.</summary>
    public static IReadOnlyList<string>? Normalizar(IReadOnlyList<string?>? pedidos)
    {
        var lista = (pedidos ?? []).Select(f => f?.Trim().ToLowerInvariant()).ToList();
        if (lista.Count == 0 || lista.Any(f => f is null || !Todos.Contains(f)))
        {
            return null;
        }
        return [.. Todos.Where(f => f == Zip || lista.Contains(f))];
    }
}

/// <summary>Exportaciones de documentos (sección <c>Documentacion:Exportaciones</c>, con los mismos valores que las de contratos).</summary>
public sealed class OpcionesExportacionesDocumentos
{
    public const string Seccion = "Documentacion:Exportaciones";

    /// <summary>Tope del plan 00 §6.3 para la URL firmada de descarga.</summary>
    public static readonly TimeSpan MaximoUrl = TimeSpan.FromMinutes(15);

    public TimeSpan DuracionUrl { get; set; } = MaximoUrl;

    public TimeSpan Retencion { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan IntervaloVencimiento { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromHours(2);

    public int MaximoEnCursoPorPersona { get; set; } = 2;

    public TimeSpan EsperaEnCurso { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Lo que no se cumple, vacío si está bien.</summary>
    public string? Problema() =>
        DuracionUrl <= TimeSpan.Zero || DuracionUrl > MaximoUrl ? $"{Seccion}:DuracionUrl debe estar entre 1 segundo y {MaximoUrl} (plan 00 §6.3)."
        : Retencion < TimeSpan.FromMinutes(1) ? $"{Seccion}:Retencion debe ser de al menos 1 minuto."
        : IntervaloVencimiento <= TimeSpan.Zero || TiempoMaximo <= TimeSpan.Zero ? $"{Seccion}:IntervaloVencimiento y TiempoMaximo deben ser positivos."
        : MaximoEnCursoPorPersona < 1 ? $"{Seccion}:MaximoEnCursoPorPersona debe ser de al menos 1."
        : null;
}
