using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos.Portal;

/// <summary>
/// Portal de clientes (HU 4.4, plan 00 §6.2, §8.5 y §8.9, ATLAS-ADR-0043), en <c>/api/v1/portal</c>: el consolidado del
/// cliente, el detalle de un paquete, el spec <b>podado</b> por contrato, las guías, los cambios y la búsqueda dentro de lo
/// compartido. Todo sale de <see cref="AccesoPortal"/> (la organización de la sesión, el alcance de la persona y la
/// vigencia de la compartición); un paquete fuera de la vista responde 404 aunque exista, sin revelar nada. Toda vista
/// queda en la auditoría con la organización (§8.9). Requiere <c>atlas.portal.ver</c>.
/// </summary>
internal static class EndpointsPortal
{
    public const string RecursoAuditoria = "contratos.portal";
    public const string AccionInicio = "contratos.portal_inicio_visto";
    public const string AccionPaquete = "contratos.portal_paquete_visto";
    public const string AccionSpec = "contratos.portal_spec_visto";
    public const string AccionGuia = "contratos.portal_guia_vista";
    public const string AccionCambios = "contratos.portal_cambios_vistos";
    public const string AccionBusqueda = "contratos.portal_busqueda";
    public const string AccionNegado = "contratos.portal_acceso_negado";

    private const int CambiosMaximos = 500;
    private const int ResultadosMaximos = 30;

    // --- DTO ---

    [Description("Organización de la sesión.")]
    public sealed record OrganizacionDelPortal(Guid Id, string Nombre);

    [Description("Aplicativo del catálogo de un contrato compartido.")]
    public sealed record AplicativoDelPortal(Guid Id, [property: Description("Vacío si ya no está en el catálogo.")] string? Slug, string? Nombre);

    [Description("Un contrato compartido dentro de un paquete.")]
    public sealed record ContratoCompartido(
        Guid ContratoId,
        string Codigo,
        string Nombre,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        AplicativoDelPortal Aplicativo,
        Guid VersionId,
        [property: Description("Versión semántica que comparte el paquete.")] string Version,
        [property: Description("vigente, deprecada o retirada (retirada: sin spec ni operaciones).")] string EstadoVersion,
        [property: Description("Fecha de retiro si está deprecada.")] DateOnly? RetiroEn,
        [property: Description("resumen (sin detalle técnico) o tecnico.")] string Nivel,
        [property: Description("Operaciones compartidas; 0 con nivel resumen.")] int Operaciones,
        [property: Description("Guías compartidas; 0 con nivel resumen.")] int Guias);

    [Description("Un paquete compartido con la organización.")]
    public sealed record PaqueteCompartido(
        Guid Id,
        string Nombre,
        string? Descripcion,
        [property: Description("Revisión del paquete que ve la organización.")] int Revision,
        [property: Description("resumen o tecnico: lo que la persona ve según su rol y su alcance.")] string Nivel,
        [property: Description("Si la persona puede exportarlo (compartición con exportar y su permiso); la acción llega con la HU 4.5.")] bool PuedeExportar,
        [property: Description("Si puede usar el sandbox (compartición con sandbox y su permiso); la acción llega con la HU 4.7.")] bool PuedeSandbox,
        [property: Description("Hasta cuándo se comparte; vacío = sin vencimiento.")] DateTimeOffset? VigenteHasta,
        IReadOnlyList<ContratoCompartido> Contratos);

    [Description("Aviso de deprecación o retiro de una versión compartida.")]
    public sealed record AvisoDelPortal(
        [property: Description("deprecada o retirada.")] string Tipo,
        Guid PaqueteId,
        string Paquete,
        Guid ContratoId,
        string Contrato,
        string Version,
        DateOnly? RetiroEn,
        [property: Description("La versión estable vigente más alta posterior, si la hay (la que recomienda el aviso).")] string? Reemplazo,
        [property: Description("El mismo texto que el aviso por correo y push: versión, contrato, fecha de retiro dd/mm/aaaa y su reemplazo.")] string Mensaje);

    [Description("Documento funcional de un aplicativo (llega con la documentación, F5).")]
    public sealed record DocumentoDelAplicativo(Guid Id, string Titulo, string Tipo, string Ruta);

    [Description("Un aplicativo con lo que se comparte de él.")]
    public sealed record AplicativoCompartido(
        AplicativoDelPortal Aplicativo,
        [property: Description("Ids de los paquetes que traen contratos de este aplicativo.")] IReadOnlyList<Guid> Paquetes,
        [property: Description("Contratos compartidos del aplicativo (sin repetir entre paquetes).")] int Contratos,
        [property: Description("Documentación funcional (F5); vacía por ahora.")] IReadOnlyList<DocumentoDelAplicativo> Documentos);

    [Description("Inicio del cliente: sus aplicativos, sus paquetes y los avisos de deprecación de lo que usa.")]
    public sealed record InicioPortal(
        OrganizacionDelPortal Organizacion,
        IReadOnlyList<AplicativoCompartido> Aplicativos,
        IReadOnlyList<PaqueteCompartido> Paquetes,
        IReadOnlyList<AvisoDelPortal> Avisos);

    [Description("Operación compartida (del índice de la versión).")]
    public sealed record OperacionCompartida(
        [property: Description("GET /ruta, POST webhook:<nombre>, send <canal>…")] string Clave,
        string? Metodo,
        string? Ruta,
        string? OperationId,
        string? Resumen,
        IReadOnlyList<string> Etiquetas);

    [Description("Guía compartida: la revisión que fijó el paquete.")]
    public sealed record GuiaCompartida(Guid Id, string Titulo, [property: Description("Clave de la operación si es de una.")] string? Operacion, int Revision);

    [Description("Contrato compartido con sus operaciones y sus guías (nivel técnico).")]
    public sealed record ContratoDelPaquete(
        ContratoCompartido Contrato,
        IReadOnlyList<OperacionCompartida> Operaciones,
        IReadOnlyList<GuiaCompartida> Guias);

    [Description("Detalle de un paquete compartido.")]
    public sealed record DetallePaquetePortal(
        PaqueteCompartido Paquete,
        IReadOnlyList<ContratoDelPaquete> Contratos,
        IReadOnlyList<AvisoDelPortal> Avisos);

    [Description("Guía compartida con su cuerpo en Markdown.")]
    public sealed record GuiaPortal(
        Guid Id, Guid PaqueteId, Guid ContratoId, string Titulo, string? Operacion, int Revision,
        [property: Description("Markdown tal cual (la PWA lo muestra sanitizado, sin HTML).")] string Contenido);

    [Description("Un cambio de la versión compartida que toca una operación compartida.")]
    public sealed record CambioPortal(
        [property: Description("Regla de oasdiff.")] string Id,
        string Texto,
        [property: Description("1 info, 2 advertencia, 3 rompe compatibilidad.")] int Nivel,
        string? Metodo,
        string? Ruta);

    [Description("Cambios de la versión compartida (del changelog de la CI), solo los de operaciones compartidas.")]
    public sealed record CambiosPortal(
        Guid ContratoId,
        string Version,
        [property: Description("Versión contra la que se calcularon, si se conoce.")] string? Base,
        IReadOnlyList<CambioPortal> Elementos);

    [Description("Resultado de la búsqueda dentro de lo compartido.")]
    public sealed record ResultadoPortal(
        [property: Description("paquete, contrato u operacion.")] string Tipo,
        string Titulo,
        string? Detalle,
        Guid PaqueteId,
        Guid? ContratoId,
        [property: Description("Ruta de la PWA.")] string Ruta);

    [Description("Por qué no se pudo, con un código estable.")]
    public sealed record ErrorPortal(
        [property: Description("no_encontrado (404); solo_resumen (403); no_soportado, sin_operaciones, ambiente_invalido o un código de la poda (422).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder portal)
    {
        portal.MapGet("/inicio", InicioAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ObtenerInicioPortal")
            .WithSummary("Inicio del cliente")
            .WithDescription("El consolidado de la organización de la sesión: sus aplicativos (con la documentación funcional cuando llegue, F5), los paquetes compartidos vigentes con sus contratos y los avisos de deprecación de las versiones que usa. Recortado al alcance y al rol de la persona. Auditado. Requiere atlas.portal.ver.")
            .Produces<InicioPortal>();

        portal.MapGet("/paquetes/{paqueteId:guid}", PaqueteAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ObtenerPaquetePortal")
            .WithSummary("Un paquete compartido")
            .WithDescription("El paquete con la revisión que ve la organización: sus contratos y, con nivel técnico, las operaciones y las guías compartidas, y si puede exportar o usar el sandbox. 404 si no está compartido con la organización, venció, se revocó o queda fuera del alcance de la persona. Auditado. Requiere atlas.portal.ver.")
            .Produces<DetallePaquetePortal>()
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapGet("/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}/spec", SpecAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ObtenerSpecPodadoPortal")
            .WithSummary("Spec podado de un contrato compartido")
            .Produces<string>(StatusCodes.Status200OK, "application/json", "application/yaml")
            .Produces<ErrorPortal>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound)
            .Produces<ErrorPortal>(StatusCodes.Status422UnprocessableEntity)
            .WithDescription("Nunca el spec completo: solo las operaciones del paquete, lo que ellas referencian, sin nada interno y con las URL públicas del ambiente (sin ellas, sin servers), en el formato en que se publicó (ATLAS-ADR-0038). El ETag es su SHA-256. 403 solo_resumen si el rol de la persona no ve el detalle técnico; 404 como el paquete; 422 si no se puede podar. Auditado. Requiere atlas.portal.ver.");

        portal.MapGet("/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}/cambios", CambiosAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ListarCambiosPortal")
            .WithSummary("Cambios de un contrato compartido")
            .WithDescription("Del changelog de la CI de la versión compartida, solo los cambios de operaciones compartidas (lo demás no se muestra). 403 solo_resumen; 404 como el paquete. Auditado. Requiere atlas.portal.ver.")
            .Produces<CambiosPortal>()
            .Produces<ErrorPortal>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapGet("/paquetes/{paqueteId:guid}/guias/{guiaId:guid}", GuiaAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ObtenerGuiaPortal")
            .WithSummary("Una guía compartida")
            .WithDescription("La revisión de la guía que fijó el paquete (la aprobada por el responsable), nunca la última. 403 solo_resumen; 404 si la guía no está en el paquete que ve la persona. Auditado. Requiere atlas.portal.ver.")
            .Produces<GuiaPortal>()
            .Produces<ErrorPortal>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapGet("/buscar", BuscarAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("BuscarEnPortal")
            .WithSummary("Buscar en lo compartido")
            .WithDescription($"Paquetes, contratos y (con nivel técnico) operaciones compartidas con la organización que coinciden con el texto (sin distinguir tildes ni mayúsculas), hasta {ResultadosMaximos}. Nunca devuelve nada fuera de la vista de la persona. Auditado (sin el texto). Requiere atlas.portal.ver.")
            .Produces<List<ResultadoPortal>>()
            .ProducesValidationProblem();
    }

    // --- Lectura ---

    private static async Task<IResult> InicioAsync(
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        IDocumentacionDelPortal documentacion, TimeProvider reloj, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        var reemplazos = await ReemplazosAsync(contexto, vista.Paquetes, ct);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var organizacion = await plataforma.Organizaciones.AsNoTracking().Where(o => o.Id == vista.OrganizacionId).Select(o => o.Nombre).FirstOrDefaultAsync(ct);
        var paquetes = vista.Paquetes.Select(p => Paquete(p, aplicativos)).ToList();
        var porAplicativo = vista.Paquetes
            .SelectMany(p => p.Contratos.Select(c => (Paquete: p.Paquete.Id, c.Contrato)))
            .GroupBy(x => x.Contrato.AplicativoId)
            .ToList();
        var documentos = (await documentacion.DeAplicativosAsync(http.User, vista.OrganizacionId, [.. porAplicativo.Select(g => g.Key)], ct))
            .ToLookup(d => d.AplicativoId);
        var tarjetas = porAplicativo
            .Select(g => new AplicativoCompartido(Aplicativo(g.Key, aplicativos), [.. g.Select(x => x.Paquete).Distinct()],
                g.Select(x => x.Contrato.Id).Distinct().Count(), [.. documentos[g.Key].Select(d => new DocumentoDelAplicativo(d.Id, d.Titulo, d.Tipo, d.Ruta))]))
            .OrderBy(a => a.Aplicativo.Nombre ?? "~", StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Aplicativo.Id)
            .ToList();
        await AuditarAsync(http, plataforma, reloj, AccionInicio, vista, null, null, new { paquetes = vista.Paquetes.Select(p => p.Paquete.Id).ToList() }, ct);
        return Results.Ok(new InicioPortal(new OrganizacionDelPortal(vista.OrganizacionId, organizacion ?? "Tu organización"), tarjetas, paquetes,
            [.. vista.Paquetes.SelectMany(p => Avisos(p, reemplazos))]));
    }

    private static async Task<IResult> PaqueteAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, ILectorAplicativos lector,
        TimeProvider reloj, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        if (vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == paqueteId) is not { } paquete)
        {
            return await NegadoAsync(http, plataforma, reloj, vista, paqueteId, null, ct);
        }
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        var versiones = paquete.Contratos.Where(c => c.Tecnico).Select(c => c.Version.Id).ToList();
        var indice = (await contexto.Operaciones.AsNoTracking().Where(o => versiones.Contains(o.VersionId)).ToListAsync(ct)).ToLookup(o => o.VersionId);
        var contratos = paquete.Contratos.Select(c =>
        {
            var claves = c.Entrada.Operaciones.ToHashSet(StringComparer.Ordinal);
            return new ContratoDelPaquete(Contrato(c, aplicativos),
                c.Tecnico
                    ? [.. indice[c.Version.Id].Where(o => claves.Contains(o.Clave)).OrderBy(o => o.Clave, StringComparer.Ordinal)
                        .Select(o => new OperacionCompartida(o.Clave, o.Metodo, o.Ruta, o.OperationId, o.Resumen, o.Etiquetas))]
                    : [],
                c.Tecnico ? [.. c.Guias.Select(g => new GuiaCompartida(g.GuiaId, g.Titulo, g.Operacion, g.Revision))] : []);
        }).ToList();
        await AuditarAsync(http, plataforma, reloj, AccionPaquete, vista, paquete.Paquete.Id.ToString(), paquete.Revision, new { nivel = Texto(paquete.Nivel) }, ct);
        var reemplazos = await ReemplazosAsync(contexto, [paquete], ct);
        return Results.Ok(new DetallePaquetePortal(Paquete(paquete, aplicativos), contratos, [.. Avisos(paquete, reemplazos)]));
    }

    private static async Task<IResult> SpecAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Id del contrato.")] Guid contratoId,
        [Description("sandbox, qa o pdn: las URL públicas que van en servers; vacío = el del ambiente de Atlas.")] string? ambiente,
        HttpContext http, AccesoPortal acceso, SpecsDelPortal specs, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, contrato, error) = await ContratoAsync(http, acceso, plataforma, reloj, paqueteId, contratoId, ct);
        if (error is not null)
        {
            return error;
        }
        var resultado = await specs.PodarAsync(paquete!, contrato!, ambiente, ct);
        if (resultado.Spec is not { } spec)
        {
            return Results.Json(new ErrorPortal(resultado.Codigo!, resultado.Mensaje ?? "No se pudo podar el spec."), statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(spec.Contenido)));
        await AuditarAsync(http, plataforma, reloj, AccionSpec, await acceso.VistaAsync(http.User, ct), paquete!.Paquete.Id.ToString(), paquete.Revision, new
        {
            contrato = contratoId, version = contrato!.Version.Version, operaciones = spec.Resumen.Operaciones.Count, sha256 = sha, desdeCache = resultado.DesdeCache,
        }, ct);
        http.Response.Headers.ETag = $"\"{sha}\"";
        http.Response.Headers.CacheControl = "private, no-cache";
        return Results.Text(spec.Contenido, spec.Formato == FormatoSpec.Json ? "application/json" : "application/yaml", System.Text.Encoding.UTF8);
    }

    private static async Task<IResult> CambiosAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Id del contrato.")] Guid contratoId,
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var (paquete, contrato, error) = await ContratoAsync(http, acceso, plataforma, reloj, paqueteId, contratoId, ct);
        if (error is not null)
        {
            return error;
        }
        var version = await contexto.Versiones.AsNoTracking().Where(v => v.Id == contrato!.Version.Id).Select(v => new { v.Changelog, v.ChangelogBase }).FirstAsync(ct);
        var cambios = FiltrarCambios(version.Changelog, contrato!.Entrada.Operaciones);
        await AuditarAsync(http, plataforma, reloj, AccionCambios, await acceso.VistaAsync(http.User, ct), paquete!.Paquete.Id.ToString(), paquete.Revision,
            new { contrato = contratoId, version = contrato.Version.Version, cambios = cambios.Count }, ct);
        return Results.Ok(new CambiosPortal(contratoId, contrato.Version.Version, version.ChangelogBase, cambios));
    }

    private static async Task<IResult> GuiaAsync(
        [Description("Id del paquete.")] Guid paqueteId,
        [Description("Id de la guía.")] Guid guiaId,
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        var paquete = vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == paqueteId);
        var contrato = paquete?.Contratos.FirstOrDefault(c => c.Guias.Any(g => g.GuiaId == guiaId));
        if (paquete is null || contrato is null)
        {
            return await NegadoAsync(http, plataforma, reloj, vista, paqueteId, $"guia:{guiaId}", ct);
        }
        if (!contrato.Tecnico)
        {
            return SoloResumen();
        }
        var guia = contrato.Guias.First(g => g.GuiaId == guiaId);
        var contenido = await contexto.RevisionesGuia.AsNoTracking().Where(r => r.GuiaId == guiaId && r.Numero == guia.Revision).Select(r => r.Contenido).FirstAsync(ct);
        await AuditarAsync(http, plataforma, reloj, AccionGuia, vista, paquete.Paquete.Id.ToString(), paquete.Revision, new { guia = guiaId, revision = guia.Revision }, ct);
        return Results.Ok(new GuiaPortal(guiaId, paqueteId, contrato.Contrato.Id, guia.Titulo, guia.Operacion, guia.Revision, contenido));
    }

    private static async Task<IResult> BuscarAsync(
        [Description("Texto a buscar (2 a 100 caracteres).")] string? q,
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var texto = FuncionesSql.NormalizarBusqueda(q ?? string.Empty);
        if (texto.Length is < 2 or > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Escribe entre 2 y 100 caracteres."] });
        }
        var vista = await acceso.VistaAsync(http.User, ct);
        var terminos = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Coincide(params string?[] campos) => terminos.All(t => campos.Any(c => c is not null && FuncionesSql.NormalizarBusqueda(c).Contains(t, StringComparison.Ordinal)));
        var resultados = new List<ResultadoPortal>();
        foreach (var p in vista.Paquetes)
        {
            if (Coincide(p.Paquete.Nombre, p.Paquete.Descripcion))
            {
                resultados.Add(new ResultadoPortal("paquete", p.Paquete.Nombre, p.Paquete.Descripcion, p.Paquete.Id, null, $"/portal/paquetes/{p.Paquete.Id}"));
            }
            foreach (var c in p.Contratos.Where(c => Coincide(c.Contrato.Nombre, c.Contrato.Codigo, c.Contrato.Descripcion)))
            {
                resultados.Add(new ResultadoPortal("contrato", c.Contrato.Nombre, $"{c.Contrato.Codigo} {c.Version.Version}", p.Paquete.Id, c.Contrato.Id,
                    $"/portal/paquetes/{p.Paquete.Id}/contratos/{c.Contrato.Id}"));
            }
        }
        // Operaciones: solo las compartidas, de los contratos con nivel técnico, desde el índice de cada versión.
        var tecnicos = vista.Paquetes.SelectMany(p => p.Contratos.Where(c => c.Tecnico).Select(c => (Paquete: p, Contrato: c))).ToList();
        var versiones = tecnicos.Select(x => x.Contrato.Version.Id).Distinct().ToList();
        var indice = (await contexto.Operaciones.AsNoTracking().Where(o => versiones.Contains(o.VersionId)).ToListAsync(ct)).ToLookup(o => o.VersionId);
        foreach (var (p, c) in tecnicos)
        {
            var claves = c.Entrada.Operaciones.ToHashSet(StringComparer.Ordinal);
            resultados.AddRange(indice[c.Version.Id]
                .Where(o => claves.Contains(o.Clave) && Coincide(o.Clave, o.OperationId, o.Resumen))
                .OrderBy(o => o.Clave, StringComparer.Ordinal)
                .Select(o => new ResultadoPortal("operacion", o.Clave, o.Resumen, p.Paquete.Id, c.Contrato.Id,
                    $"/portal/paquetes/{p.Paquete.Id}/contratos/{c.Contrato.Id}?operacion={Uri.EscapeDataString(o.Clave)}")));
        }
        var lista = resultados.Take(ResultadosMaximos).ToList();
        await AuditarAsync(http, plataforma, reloj, AccionBusqueda, vista, null, null, new { largo = texto.Length, resultados = lista.Count }, ct);
        return Results.Ok(lista);
    }

    // --- Piezas comunes ---

    /// <summary>El paquete y uno de sus contratos con nivel técnico y disponible; si no, 404 (auditado) o 403.</summary>
    private static async Task<(PaqueteDelPortal? Paquete, ContratoDelPortal? Contrato, IResult? Error)> ContratoAsync(
        HttpContext http, AccesoPortal acceso, PlataformaDbContext plataforma, TimeProvider reloj, Guid paqueteId, Guid contratoId, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        var paquete = vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == paqueteId);
        var contrato = paquete?.Contratos.FirstOrDefault(c => c.Contrato.Id == contratoId);
        if (paquete is null || contrato is null || !contrato.Disponible)
        {
            return (null, null, await NegadoAsync(http, plataforma, reloj, vista, paqueteId, $"contrato:{contratoId}", ct));
        }
        return contrato.Nivel == NivelPortal.Tecnico ? (paquete, contrato, null) : (null, null, SoloResumen());
    }

    /// <summary>
    /// Solo los cambios del changelog de oasdiff cuya operación (método y ruta) está compartida; los que no nombran una
    /// operación (componentes, seguridad global) no se muestran: podrían hablar de lo que no se comparte (falla cerrada).
    /// </summary>
    internal static List<CambioPortal> FiltrarCambios(string changelog, IReadOnlyList<string> operaciones)
    {
        var claves = operaciones.ToHashSet(StringComparer.Ordinal);
        var cambios = new List<CambioPortal>();
        try
        {
            using var documento = JsonDocument.Parse(changelog, new JsonDocumentOptions { MaxDepth = 64 });
            if (documento.RootElement.ValueKind != JsonValueKind.Array)
            {
                return cambios;
            }
            foreach (var c in documento.RootElement.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object || Texto(c, "operation") is not { } metodo || Texto(c, "path") is not { } ruta
                    || !claves.Contains($"{metodo.ToUpperInvariant()} {ruta}"))
                {
                    continue;
                }
                var nivel = c.TryGetProperty("level", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var x) ? x : 1;
                cambios.Add(new CambioPortal(Texto(c, "id") ?? "desconocida", Texto(c, "text") ?? string.Empty, nivel, metodo.ToUpperInvariant(), ruta));
                if (cambios.Count >= CambiosMaximos)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Un changelog que no se lee no muestra nada.
        }
        return [.. cambios.OrderByDescending(c => c.Nivel).ThenBy(c => c.Ruta, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal)];
    }

    private static string? Texto(JsonElement objeto, string propiedad) =>
        objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    /// <summary>El reemplazo de cada versión deprecada de los paquetes (HU 4.8: el aviso del portal dice lo mismo que el correo).</summary>
    private static Task<Dictionary<Guid, string?>> ReemplazosAsync(ContratosDbContext contexto, IEnumerable<PaqueteDelPortal> paquetes, CancellationToken ct) =>
        Deprecacion.ReglasDeprecacion.ReemplazosAsync(contexto,
            [.. paquetes.SelectMany(p => p.Contratos).Where(c => c.Version.Estado == EstadoVersion.Deprecada).Select(c => (c.Version.Id, c.Contrato.Id, c.Version.Version))], ct);

    private static IEnumerable<AvisoDelPortal> Avisos(PaqueteDelPortal p, IReadOnlyDictionary<Guid, string?> reemplazos) => p.Contratos
        .Where(c => c.Version.Estado != EstadoVersion.Vigente)
        .Select(c => c.Version.Estado == EstadoVersion.Deprecada
            ? new AvisoDelPortal("deprecada", p.Paquete.Id, p.Paquete.Nombre, c.Contrato.Id, c.Contrato.Nombre, c.Version.Version, c.Version.RetiroEn,
                reemplazos.GetValueOrDefault(c.Version.Id),
                Deprecacion.ReglasDeprecacion.MensajePortal(c.Version.Version, c.Contrato.Nombre, c.Version.RetiroEn, reemplazos.GetValueOrDefault(c.Version.Id)))
            : new AvisoDelPortal("retirada", p.Paquete.Id, p.Paquete.Nombre, c.Contrato.Id, c.Contrato.Nombre, c.Version.Version, c.Version.RetiroEn, null,
                $"La versión {c.Version.Version} de {c.Contrato.Nombre} se retiró: pide a FLIT la versión vigente."));

    private static PaqueteCompartido Paquete(PaqueteDelPortal p, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos) => new(
        p.Paquete.Id, p.RevisionPaquete.Nombre, p.RevisionPaquete.Descripcion, p.Revision, Texto(p.Nivel), p.PuedeExportar, p.PuedeSandbox,
        p.Comparticion.VigenteHasta, [.. p.Contratos.Select(c => Contrato(c, aplicativos))]);

    private static ContratoCompartido Contrato(ContratoDelPortal c, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos) => new(
        c.Contrato.Id, c.Contrato.Codigo, c.Contrato.Nombre, Valores.Texto(c.Contrato.Tipo), Aplicativo(c.Contrato.AplicativoId, aplicativos),
        c.Version.Id, c.Version.Version, Valores.Texto(c.Version.Estado), c.Version.RetiroEn, Texto(c.Nivel),
        c.Tecnico ? c.Entrada.Operaciones.Count : 0, c.Tecnico ? c.Guias.Count : 0);

    private static AplicativoDelPortal Aplicativo(Guid id, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        var a = aplicativos.GetValueOrDefault(id);
        return new AplicativoDelPortal(id, a?.Slug, a?.Nombre);
    }

    private static string Texto(NivelPortal nivel) => nivel == NivelPortal.Tecnico ? "tecnico" : "resumen";

    internal static IResult SoloResumen() => Results.Json(new ErrorPortal("solo_resumen",
        "Tu rol ve el resumen de este paquete, sin el detalle técnico. Si lo necesitas, pide a FLIT que te comparta el paquete."), statusCode: StatusCodes.Status403Forbidden);

    /// <summary>404 sin revelar si el paquete existe (de otra organización, vencido, revocado o fuera del alcance), auditado.</summary>
    internal static async Task<IResult> NegadoAsync(
        HttpContext http, PlataformaDbContext plataforma, TimeProvider reloj, VistaPortal vista, Guid paqueteId, string? detalle, CancellationToken ct)
    {
        await AuditarAsync(http, plataforma, reloj, AccionNegado, vista, paqueteId.ToString(), null, new { detalle }, ct);
        return Results.Json(new ErrorPortal("no_encontrado", "No tienes este paquete compartido."), statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>Toda vista de un externo queda en la auditoría (§8.9): quién, qué, versión, cuándo, IP y organización.</summary>
    internal static async Task AuditarAsync(
        HttpContext http, PlataformaDbContext plataforma, TimeProvider reloj, string accion, VistaPortal vista, string? recurso, int? revision, object datos,
        CancellationToken ct)
    {
        plataforma.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = reloj.GetUtcNow(),
            ActorTipo = TipoActor.Persona,
            ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
            OrganizacionId = vista.OrganizacionId == Guid.Empty ? null : vista.OrganizacionId,
            Accion = accion,
            RecursoTipo = RecursoAuditoria,
            RecursoId = recurso,
            Version = revision?.ToString(CultureInfo.InvariantCulture),
            Ip = http.Connection.RemoteIpAddress,
            Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
            CorrelacionId = http.TraceIdentifier,
        });
        await plataforma.SaveChangesAsync(ct);
    }
}
