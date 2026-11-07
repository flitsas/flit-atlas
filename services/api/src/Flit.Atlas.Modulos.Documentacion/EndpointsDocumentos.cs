using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Modulos.Documentacion.TiempoReal;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Lectura y escritura de documentos (HU 5.1, ATLAS-ADR-0053 §2, §3 y §6). Toda consulta pasa por
/// <see cref="AccesoDocumentos"/>: lo que la persona no puede ver responde 404, antes que 403, para no revelar nada. Los
/// documentos de repo no se editan (409 <c>documento_de_repo</c>).
/// </summary>
internal static class EndpointsDocumentos
{
    private const int TamanoMaximo = 100;

    [Description("Versión de un documento en un ambiente.")]
    public sealed record CanalDocumentoDto([property: Description("dev, qa o pdn.")] string Ambiente, [property: Description("Número de la versión.")] int Version);

    [Description("Un documento, sin el cuerpo.")]
    public sealed record DocumentoResumen(
        Guid Id,
        Guid AplicativoId,
        [property: Description("repo (no se edita en Atlas) o atlas.")] string Origen,
        [property: Description("manual_usuario, guia_funcional, guia_integracion, runbook, ficha_comercial, adr o nota_version.")] string Tipo,
        string Titulo,
        [property: Description("publico_clientes, interno_negocio, interno_ti o restringido.")] string Visibilidad,
        IReadOnlyList<string> Audiencias,
        [property: Description("Responsable funcional.")] string Responsable,
        [property: Description("borrador, en_revision, publicado o archivado.")] string Estado,
        [property: Description("Ruta en el repo, si es de origen repo.")] string? Ruta,
        [property: Description("La versión que se lee por defecto; vacío si no tiene.")] int? VersionVigente,
        [property: Description("Número de la última revisión; 0 en los de repo.")] int RevisionActual,
        DateTimeOffset ActualizadoEn);

    [Description("Una página de documentos.")]
    public sealed record ListadoDocumentos(int Total, IReadOnlyList<DocumentoResumen> Elementos);

    [Description("Versión inmutable, sin el cuerpo.")]
    public sealed record ResumenVersionDocumento(
        int Numero,
        string Titulo,
        [property: Description("publico_clientes, interno_negocio, interno_ti o restringido.")] string Visibilidad,
        [property: Description("De repo y sin aprobar: solo la ven los roles técnicos y los responsables.")] bool EsBorrador,
        string Sha256,
        string? Repositorio,
        string? Rama,
        string? CommitSha,
        [property: Description("Revisión congelada, si se escribió en Atlas.")] int? RevisionOrigen,
        DateTimeOffset PublicadoEn);

    [Description("Versión inmutable con su cuerpo en MDX.")]
    public sealed record DetalleVersionDocumento(ResumenVersionDocumento Version, [property: Description("Cuerpo en MDX, sin evaluar.")] string Contenido);

    [Description("Un documento con la versión pedida y lo que la persona puede hacer.")]
    public sealed record DocumentoDetalle(
        DocumentoResumen Documento,
        IReadOnlyList<CanalDocumentoDto> Canales,
        [property: Description("La versión pedida o la vigente; vacío si aún no tiene una que la persona pueda leer.")] DetalleVersionDocumento? Version,
        [property: Description("Si puede editarlo: nunca en los de repo.")] bool PuedeEditar,
        [property: Description("Si puede aprobar lo que está en revisión; si es publico_clientes, solo el principal o el secundario (o, sin responsables, el líder técnico).")] bool PuedeAprobar);

    [Description("Revisión inmutable, sin el cuerpo.")]
    public sealed record ResumenRevisionDocumento(
        int Numero,
        [property: Description("creacion, edicion o restauracion.")] string Origen,
        int? RestauradaDe,
        string Titulo,
        string Visibilidad,
        string Nota,
        Guid AutorPersonaId,
        DateTimeOffset GuardadaEn,
        string Huella);

    [Description("Revisión inmutable con su cuerpo.")]
    public sealed record DetalleRevisionDocumento(ResumenRevisionDocumento Revision, string Tipo, string Contenido, IReadOnlyList<string> Audiencias, string Responsable);

    [Description("Lo que se guarda en una revisión nueva.")]
    public sealed record SolicitudDocumento(
        [property: Description("Al crear: el aplicativo. Al editar se ignora.")] Guid? AplicativoId,
        [property: Description("manual_usuario, guia_funcional, guia_integracion, runbook, ficha_comercial o nota_version; adr solo llega desde los repos.")] string Tipo,
        [property: Description("Hasta 200 caracteres.")] string Titulo,
        [property: Description("Cuerpo en MDX, hasta 300 mil caracteres. La lista blanca se revisa al enviar a revisión y al publicar.")] string Contenido,
        [property: Description("publico_clientes, interno_negocio, interno_ti o restringido: uno que la persona alcance a ver.")] string Visibilidad,
        IReadOnlyList<string>? Audiencias,
        [property: Description("Responsable funcional, hasta 200 caracteres.")] string Responsable,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("Al editar: la revisión sobre la que se trabajó (409 si hay otra más nueva).")] int? RevisionBase);

    [Description("Restaurar una revisión: crea otra igual a ella.")]
    public sealed record SolicitudRestauracionDocumento(
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Nota,
        [property: Description("La última revisión que vio la persona.")] int RevisionBase);

    [Description("Un cambio de estado sobre la última revisión.")]
    public sealed record SolicitudEstadoDocumento([property: Description("La revisión sobre la que se actúa: debe ser la última (409 si no).")] int Revision);

    [Description("Por qué no se hizo, con un código estable.")]
    public sealed record ErrorDocumento(
        [property: Description("no_encontrado (404); sin_permiso, aprobacion_requerida (403); documento_de_repo, documento_archivado, revision_desactualizada, sin_cambios, estado_invalido, ya_publicada (409); mdx_invalido (422).")] string Codigo,
        string Mensaje,
        IReadOnlyList<string>? Errores = null);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/documentos", ListarAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("ListarDocumentos")
            .WithSummary("Documentos que la persona puede ver")
            .WithDescription("Con el nivel de visibilidad aplicado en la consulta; la búsqueda (texto) es full-text en español sobre la versión vigente, sin distinguir tildes. Los archivados solo aparecen con estado=archivado y para quien puede editarlos. Requiere atlas.docs.ver.")
            .Produces<ListadoDocumentos>()
            .ProducesValidationProblem();

        grupo.MapPost("/documentos", CrearAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("CrearDocumento")
            .WithSummary("Crear un documento en Atlas")
            .WithDescription("Lo crea en borrador con su revisión 1, auditado en la misma transacción. Requiere atlas.docs.editar global o sobre el aplicativo (responsable); si no, 403.")
            .Produces<DocumentoDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        grupo.MapGet("/documentos/{id:guid}", ObtenerAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("ObtenerDocumento")
            .WithSummary("Un documento con una de sus versiones")
            .WithDescription("Por defecto, la versión vigente (la del canal pdn o, sin él, la última); con version o canal, esa. Una versión que la persona no puede leer responde 404. Requiere atlas.docs.ver.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound);

        grupo.MapPut("/documentos/{id:guid}", GuardarAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("GuardarDocumento")
            .WithSummary("Guardar un documento (revisión nueva)")
            .WithDescription("Cada guardado crea una revisión inmutable y deja el documento en borrador; lo publicado no cambia hasta volver a publicar. 409 revision_desactualizada, sin_cambios, documento_de_repo o documento_archivado. Requiere atlas.docs.editar sobre el aplicativo.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/documentos/{id:guid}/versiones", VersionesAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("ListarVersionesDocumento")
            .WithSummary("Versiones de un documento")
            .WithDescription("Las que la persona puede leer, de la más reciente a la más antigua, sin los cuerpos. Requiere atlas.docs.ver.")
            .Produces<List<ResumenVersionDocumento>>()
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound);

        grupo.MapGet("/documentos/{id:guid}/versiones/{numero}", VersionAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("ObtenerVersionDocumento")
            .WithSummary("Una versión con su cuerpo")
            .WithDescription("La PWA compara dos versiones con estos cuerpos. 404 si no existe o la persona no puede leerla. Requiere atlas.docs.ver.")
            .Produces<DetalleVersionDocumento>()
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound);

        grupo.MapGet("/documentos/{id:guid}/revisiones", RevisionesAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("ListarRevisionesDocumento")
            .WithSummary("Historial de revisiones")
            .WithDescription("De la más reciente a la más antigua, sin los cuerpos. Requiere atlas.docs.editar sobre el aplicativo.")
            .Produces<List<ResumenRevisionDocumento>>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound);

        grupo.MapGet("/documentos/{id:guid}/revisiones/{numero}", RevisionAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("ObtenerRevisionDocumento")
            .WithSummary("Una revisión con su cuerpo")
            .WithDescription("La revisión tal como se guardó. Requiere atlas.docs.editar sobre el aplicativo.")
            .Produces<DetalleRevisionDocumento>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound);

        grupo.MapPost("/documentos/{id:guid}/revisiones/{numero}/restauracion", RestaurarAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("RestaurarRevisionDocumento")
            .WithSummary("Restaurar una revisión")
            .WithDescription("Crea una revisión nueva igual a la elegida, sin reescribir la historia, y deja el documento en borrador. Requiere atlas.docs.editar sobre el aplicativo.")
            .Produces<DocumentoDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/documentos/{id:guid}/envio-revision", EnviarAsync)
            .RequierePermiso(PermisosDocumentacion.Editar)
            .WithName("EnviarDocumentoARevision")
            .WithSummary("Enviar a revisión")
            .WithDescription("De borrador a en_revision. Revisa la lista blanca de MDX: 422 mdx_invalido con el detalle. Requiere atlas.docs.editar sobre el aplicativo.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict)
            .Produces<ErrorDocumento>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/documentos/{id:guid}/devolucion", DevolverAsync)
            .RequierePermiso(PermisosDocumentacion.Aprobar)
            .WithName("DevolverDocumento")
            .WithSummary("Devolver a borrador")
            .WithDescription("De en_revision a borrador. Requiere atlas.docs.aprobar sobre el aplicativo.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict);

        grupo.MapPost("/documentos/{id:guid}/publicacion", PublicarAsync)
            .RequierePermiso(PermisosDocumentacion.Aprobar)
            .WithName("PublicarDocumento")
            .WithSummary("Aprobar y publicar")
            .WithDescription("De en_revision a publicado: congela la última revisión en una versión inmutable y la deja en el canal pdn. Lo publico_clientes solo lo aprueba el principal o el secundario; si el aplicativo no tiene responsables, el líder técnico. 403 aprobacion_requerida para los demás. Auditado, con el evento en la misma transacción. Requiere atlas.docs.aprobar sobre el aplicativo.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict)
            .Produces<ErrorDocumento>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/documentos/{id:guid}/archivo", ArchivarAsync)
            .RequierePermiso(PermisosDocumentacion.Aprobar)
            .WithName("ArchivarDocumento")
            .WithSummary("Archivar")
            .WithDescription("Deja de listarse a los lectores; sus versiones y revisiones siguen ahí. Los de repo se archivan desde la CI (409 documento_de_repo). Requiere atlas.docs.aprobar sobre el aplicativo.")
            .Produces<DocumentoDetalle>()
            .Produces<ErrorDocumento>(StatusCodes.Status403Forbidden)
            .Produces<ErrorDocumento>(StatusCodes.Status404NotFound)
            .Produces<ErrorDocumento>(StatusCodes.Status409Conflict);
    }

    // --- Lectura ---

    private static async Task<IResult> ListarAsync(
        [Description("Solo de este aplicativo.")] Guid? aplicativo,
        [Description("Solo de este tipo.")] string? tipo,
        [Description("borrador, en_revision, publicado o archivado. Vacío: todos menos los archivados.")] string? estado,
        [Description("Búsqueda full-text en español.")] string? texto,
        [Description("Desde 1.")] int? pagina,
        [Description("De 1 a 100; por defecto 25.")] int? tamano,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        EstadoDocumento? filtroEstado = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (ReglasDocumentos.Estados.TryGetValue(estado.Trim(), out var leido))
            {
                filtroEstado = leido;
            }
            else
            {
                errores["estado"] = [$"Valores válidos: {string.Join(", ", ReglasDocumentos.Estados.Keys)}."];
            }
        }
        if (pagina is < 1)
        {
            errores["pagina"] = ["Desde 1."];
        }
        if (tamano is < 1 or > TamanoMaximo)
        {
            errores["tamano"] = [$"De 1 a {TamanoMaximo}."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var consulta = string.IsNullOrWhiteSpace(texto)
            ? acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).Select(d => new BusquedaDocumentacion.ResultadoTexto { Documento = d, Rango = 0f })
            : BusquedaDocumentacion.Vigentes(contexto, acceso, texto);
        if (aplicativo is { } aplicativoId)
        {
            consulta = consulta.Where(x => x.Documento.AplicativoId == aplicativoId);
        }
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var t = tipo.Trim();
            consulta = consulta.Where(x => x.Documento.Tipo == t);
        }
        consulta = filtroEstado is { } e
            ? consulta.Where(x => x.Documento.Estado == e)
            : consulta.Where(x => x.Documento.Estado != EstadoDocumento.Archivado);

        var total = await consulta.CountAsync(ct);
        var cuantos = tamano ?? 25;
        var ordenada = string.IsNullOrWhiteSpace(texto) ? consulta.OrderBy(x => x.Documento.Titulo) : consulta.OrderByDescending(x => x.Rango).ThenBy(x => x.Documento.Titulo);
        var filas = await ordenada.ThenBy(x => x.Documento.Id)
            .Skip(((pagina ?? 1) - 1) * cuantos).Take(cuantos).Select(x => x.Documento).ToListAsync(ct);
        return Results.Ok(new ListadoDocumentos(total, [.. filas.Select(Resumen)]));
    }

    private static async Task<IResult> ObtenerAsync(
        [Description("Id del documento.")] Guid id,
        [Description("Número de versión.")] int? version,
        [Description("dev, qa o pdn: la versión de ese canal.")] string? canal,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, ILectorResponsables responsables, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
        {
            return NoEncontrado("El documento no existe.");
        }
        var numero = documento.VersionVigente;
        if (version is { } v)
        {
            numero = v;
        }
        else if (!string.IsNullOrWhiteSpace(canal))
        {
            var ambiente = canal.Trim();
            numero = await contexto.Canales.AsNoTracking().Where(c => c.DocumentoId == id && c.Ambiente == ambiente).Select(c => (int?)c.VersionNumero).FirstOrDefaultAsync(ct);
            if (numero is null)
            {
                return NoEncontrado("El documento no está en ese canal.");
            }
        }
        var detalle = await DetalleAsync(documento, numero, acceso, contexto, responsables, ct);
        return (version is not null || !string.IsNullOrWhiteSpace(canal)) && detalle.Version is null
            ? NoEncontrado("La versión no existe.")
            : Results.Ok(detalle);
    }

    private static async Task<IResult> VersionesAsync(
        [Description("Id del documento.")] Guid id,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
        {
            return NoEncontrado("El documento no existe.");
        }
        var filas = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(v => v.DocumentoId == id), documento.AplicativoId)
            .OrderByDescending(v => v.Numero)
            .Select(v => new { v.Numero, v.Titulo, v.Visibilidad, v.EsBorrador, v.Sha256, v.Repositorio, v.Rama, v.CommitSha, v.RevisionOrigen, v.PublicadoEn })
            .ToListAsync(ct);
        return Results.Ok(filas.Select(v => new ResumenVersionDocumento(v.Numero, v.Titulo, ReglasDocumentos.Texto(v.Visibilidad), v.EsBorrador, v.Sha256, v.Repositorio, v.Rama,
            v.CommitSha, v.RevisionOrigen, v.PublicadoEn)).ToList());
    }

    private static async Task<IResult> VersionAsync(
        [Description("Id del documento.")] Guid id,
        [Description("Número de la versión.")] int numero,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = documento is null ? null : await LeerVersionAsync(id, numero, documento.AplicativoId, acceso, contexto, ct);
        return version is null ? NoEncontrado("La versión no existe.") : Results.Ok(version);
    }

    private static async Task<DetalleVersionDocumento?> LeerVersionAsync(Guid id, int numero, Guid aplicativoId, AccesoDocumentos acceso, DocumentacionDbContext contexto, CancellationToken ct)
    {
        var v = await acceso.FiltrarVersiones(contexto.Versiones.AsNoTracking().Where(x => x.DocumentoId == id && x.Numero == numero), aplicativoId)
            .Select(x => new { x.Numero, x.Titulo, x.Visibilidad, x.EsBorrador, x.Sha256, x.Repositorio, x.Rama, x.CommitSha, x.RevisionOrigen, x.PublicadoEn, x.Contenido })
            .FirstOrDefaultAsync(ct);
        return v is null
            ? null
            : new DetalleVersionDocumento(new ResumenVersionDocumento(v.Numero, v.Titulo, ReglasDocumentos.Texto(v.Visibilidad), v.EsBorrador, v.Sha256, v.Repositorio, v.Rama, v.CommitSha,
                v.RevisionOrigen, v.PublicadoEn), v.Contenido);
    }

    private static async Task<IResult> RevisionesAsync(
        [Description("Id del documento.")] Guid id,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var (documento, _, error) = await ParaEditarAsync(id, usuario, contexto, permisosDeSesion, lector, soloLectura: true, ct);
        if (documento is null)
        {
            return error!;
        }
        var filas = await contexto.Revisiones.AsNoTracking().Where(r => r.DocumentoId == id).OrderByDescending(r => r.Numero)
            .Select(r => new { r.Numero, r.Origen, r.RestauradaDe, r.Titulo, r.Visibilidad, r.Nota, r.AutorPersonaId, r.GuardadaEn, r.Huella })
            .ToListAsync(ct);
        return Results.Ok(filas.Select(r => new ResumenRevisionDocumento(r.Numero, ReglasDocumentos.Texto(r.Origen), r.RestauradaDe, r.Titulo, ReglasDocumentos.Texto(r.Visibilidad),
            r.Nota, r.AutorPersonaId, r.GuardadaEn, r.Huella)).ToList());
    }

    private static async Task<IResult> RevisionAsync(
        [Description("Id del documento.")] Guid id,
        [Description("Número de la revisión.")] int numero,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var (documento, _, error) = await ParaEditarAsync(id, usuario, contexto, permisosDeSesion, lector, soloLectura: true, ct);
        if (documento is null)
        {
            return error!;
        }
        var r = await contexto.Revisiones.AsNoTracking().FirstOrDefaultAsync(x => x.DocumentoId == id && x.Numero == numero, ct);
        return r is null
            ? NoEncontrado("La revisión no existe.")
            : Results.Ok(new DetalleRevisionDocumento(new ResumenRevisionDocumento(r.Numero, ReglasDocumentos.Texto(r.Origen), r.RestauradaDe, r.Titulo, ReglasDocumentos.Texto(r.Visibilidad), r.Nota,
                r.AutorPersonaId, r.GuardadaEn, r.Huella), r.Tipo, r.Contenido, r.Audiencias, r.Responsable));
    }

    // --- Escritura ---

    private static async Task<IResult> CrearAsync(
        [Description("El documento y su primera revisión.")] SolicitudDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(http.User, permisosDeSesion, lector, ct);
        if (solicitud.AplicativoId is not { } aplicativoId || !acceso.Aplicativos.ContainsKey(aplicativoId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["aplicativoId"] = ["El aplicativo es obligatorio y debe existir en el catálogo."] });
        }
        if (!acceso.PuedeEditar(aplicativoId))
        {
            return SinPermiso();
        }
        var (foto, nota, errores) = LeerSolicitud(solicitud, aplicativoId, acceso);
        if (foto is null)
        {
            return Results.ValidationProblem(errores);
        }
        var ahora = reloj.GetUtcNow();
        var documento = new Documento
        {
            AplicativoId = aplicativoId, Origen = OrigenDocumento.Atlas, Tipo = foto.Tipo, Titulo = foto.Titulo, Visibilidad = foto.Visibilidad,
            Audiencias = [.. foto.Audiencias], Responsable = foto.Responsable, RevisionActual = 1,
        };
        contexto.Documentos.Add(documento);
        var revision = Revision(documento.Id, 1, OrigenRevisionDocumento.Creacion, null, foto, nota!, Persona(http.User), ahora);
        contexto.Revisiones.Add(revision);
        await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, PublicacionDocumentos.AccionCreado, documento.Id, 1, new
        {
            revision = 1, visibilidad = ReglasDocumentos.Texto(foto.Visibilidad), huella = revision.Huella,
        }), null, ct);
        return Results.Created($"/api/v1/documentacion/documentos/{documento.Id}", await DetalleAsync(documento, documento.VersionVigente, acceso, contexto, responsables, ct));
    }

    private static async Task<IResult> GuardarAsync(
        [Description("Id del documento.")] Guid id,
        [Description("La revisión nueva.")] SolicitudDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaEditarAsync(id, http.User, contexto, permisosDeSesion, lector, soloLectura: false, ct);
        if (documento is null)
        {
            return error!;
        }
        var (foto, nota, errores) = LeerSolicitud(solicitud, documento.AplicativoId, acceso!);
        if (foto is null)
        {
            return Results.ValidationProblem(errores);
        }
        if (solicitud.RevisionBase != documento.RevisionActual)
        {
            return Desactualizada(documento);
        }
        var ultima = await contexto.Revisiones.AsNoTracking().Where(r => r.DocumentoId == id && r.Numero == documento.RevisionActual).Select(r => r.Huella).FirstAsync(ct);
        if (ultima == foto.Huella())
        {
            return Conflicto("sin_cambios", $"No hay cambios frente a la revisión {documento.RevisionActual}.");
        }
        return await NuevaRevisionAsync(http, contexto, plataforma, acceso!, responsables, reloj, documento, foto, nota!, OrigenRevisionDocumento.Edicion, null,
            PublicacionDocumentos.AccionRevisado, creado: false, ct);
    }

    private static async Task<IResult> RestaurarAsync(
        [Description("Id del documento.")] Guid id,
        [Description("Revisión a restaurar.")] int numero,
        [Description("La nota y la última revisión que se vio.")] SolicitudRestauracionDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaEditarAsync(id, http.User, contexto, permisosDeSesion, lector, soloLectura: false, ct);
        if (documento is null)
        {
            return error!;
        }
        var origen = await contexto.Revisiones.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentoId == id && r.Numero == numero, ct);
        if (origen is null)
        {
            return NoEncontrado("La revisión no existe.");
        }
        var nota = (solicitud.Nota ?? string.Empty).Trim();
        if (nota.Length is 0 or > DocumentacionDbContext.LargoNota)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["nota"] = [$"La nota del cambio es obligatoria y va hasta {DocumentacionDbContext.LargoNota} caracteres."] });
        }
        if (!acceso!.AlcanzaNivel(documento.AplicativoId, origen.Visibilidad))
        {
            return Error(StatusCodes.Status403Forbidden, "sin_permiso", "Esa revisión tiene un nivel de visibilidad que no alcanzas.");
        }
        if (solicitud.RevisionBase != documento.RevisionActual)
        {
            return Desactualizada(documento);
        }
        var foto = Foto(origen);
        var ultima = await contexto.Revisiones.AsNoTracking().Where(r => r.DocumentoId == id && r.Numero == documento.RevisionActual).Select(r => r.Huella).FirstAsync(ct);
        if (ultima == foto.Huella())
        {
            return Conflicto("sin_cambios", $"La revisión {documento.RevisionActual} ya es igual a la {numero}.");
        }
        return await NuevaRevisionAsync(http, contexto, plataforma, acceso, responsables, reloj, documento, foto, nota, OrigenRevisionDocumento.Restauracion, numero,
            PublicacionDocumentos.AccionRestaurado, creado: true, ct);
    }

    private static async Task<IResult> EnviarAsync(
        [Description("Id del documento.")] Guid id,
        [Description("La revisión que se envía.")] SolicitudEstadoDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaEditarAsync(id, http.User, contexto, permisosDeSesion, lector, soloLectura: false, ct);
        if (documento is null)
        {
            return error!;
        }
        if (solicitud.Revision != documento.RevisionActual)
        {
            return Desactualizada(documento);
        }
        if (documento.Estado != EstadoDocumento.Borrador)
        {
            return Conflicto("estado_invalido", "Solo se envía a revisión un borrador.");
        }
        var contenido = await contexto.Revisiones.AsNoTracking().Where(r => r.DocumentoId == id && r.Numero == documento.RevisionActual).Select(r => r.Contenido).FirstAsync(ct);
        if (ErrorMdx(contenido) is { } mdx)
        {
            return mdx;
        }
        documento.Estado = EstadoDocumento.EnRevision;
        var ahora = reloj.GetUtcNow();
        await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, PublicacionDocumentos.AccionEnviado, id, documento.RevisionActual, new { revision = documento.RevisionActual }), null, ct);
        return Results.Ok(await DetalleAsync(documento, documento.VersionVigente, acceso!, contexto, responsables, ct));
    }

    private static async Task<IResult> DevolverAsync(
        [Description("Id del documento.")] Guid id,
        [Description("La revisión que se devuelve.")] SolicitudEstadoDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaAprobarAsync(id, http.User, contexto, permisosDeSesion, lector, ct);
        if (documento is null)
        {
            return error!;
        }
        if (solicitud.Revision != documento.RevisionActual)
        {
            return Desactualizada(documento);
        }
        if (documento.Estado != EstadoDocumento.EnRevision)
        {
            return Conflicto("estado_invalido", "Solo se devuelve lo que está en revisión.");
        }
        documento.Estado = EstadoDocumento.Borrador;
        var ahora = reloj.GetUtcNow();
        await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, PublicacionDocumentos.AccionDevuelto, id, documento.RevisionActual, new { revision = documento.RevisionActual }), null, ct);
        return Results.Ok(await DetalleAsync(documento, documento.VersionVigente, acceso!, contexto, responsables, ct));
    }

    private static async Task<IResult> PublicarAsync(
        [Description("Id del documento.")] Guid id,
        [Description("La revisión que se publica.")] SolicitudEstadoDocumento solicitud,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, AvisosDocumentos avisos, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaAprobarAsync(id, http.User, contexto, permisosDeSesion, lector, ct);
        if (documento is null)
        {
            return error!;
        }
        if (solicitud.Revision != documento.RevisionActual)
        {
            return Desactualizada(documento);
        }
        if (documento.RevisionPublicada == documento.RevisionActual)
        {
            return Conflicto("ya_publicada", $"La revisión {documento.RevisionActual} ya está publicada.");
        }
        if (documento.Estado != EstadoDocumento.EnRevision)
        {
            return Conflicto("estado_invalido", "Solo se publica lo que está en revisión.");
        }
        var revision = await contexto.Revisiones.AsNoTracking().FirstAsync(r => r.DocumentoId == id && r.Numero == documento.RevisionActual, ct);
        if (revision.Visibilidad == VisibilidadDocumento.PublicoClientes && !await PuedeAprobarPublicoAsync(documento.AplicativoId, acceso!, responsables, ct))
        {
            return Error(StatusCodes.Status403Forbidden, "aprobacion_requerida",
                "Lo que ven los clientes solo lo aprueba el responsable principal o secundario del aplicativo.");
        }
        if (ErrorMdx(revision.Contenido) is { } mdx)
        {
            return mdx;
        }

        var ahora = reloj.GetUtcNow();
        var persona = Persona(http.User);
        var foto = Foto(revision);
        var sha = foto.Huella();
        var canales = await contexto.Canales.Where(c => c.DocumentoId == id).ToListAsync(ct);
        var existente = await contexto.Versiones.AsNoTracking().Where(v => v.DocumentoId == id && v.Sha256 == sha).Select(v => (int?)v.Numero).FirstOrDefaultAsync(ct);
        VersionDocumento? nueva = null;
        var numero = existente ?? documento.VersionActual + 1;
        if (existente is null)
        {
            documento.VersionActual = numero;
            nueva = new VersionDocumento
            {
                DocumentoId = id, Numero = numero, Tipo = foto.Tipo, Titulo = foto.Titulo, Contenido = foto.Contenido, Visibilidad = foto.Visibilidad,
                Audiencias = [.. foto.Audiencias], Responsable = foto.Responsable, EsBorrador = false, Sha256 = sha, RevisionOrigen = revision.Numero,
                AprobadaPorPersonaId = persona, PublicadoEn = ahora,
            };
            contexto.Versiones.Add(nueva);
        }
        documento.Estado = EstadoDocumento.Publicado;
        documento.RevisionPublicada = revision.Numero;
        PublicacionDocumentos.MoverCanal(contexto, canales, id, OpcionesDocumentacion.CanalVigente, numero);
        await PublicacionDocumentos.RecalcularVigenteAsync(contexto, documento, canales, ct);
        var slug = acceso!.Aplicativos.TryGetValue(documento.AplicativoId, out var aplicativo) ? aplicativo.Slug : string.Empty;
        await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, PublicacionDocumentos.AccionVersionPublicada, id, numero, new
        {
            revision = revision.Numero, sha256 = sha, visibilidad = ReglasDocumentos.Texto(foto.Visibilidad), nueva = nueva is not null,
        }),
            nueva is null ? null : new PublicacionDocumentos.DatosVersionPublicada(id, nueva.Id, numero, documento.AplicativoId, slug, ReglasDocumentos.Texto(OrigenDocumento.Atlas),
                foto.Tipo, ReglasDocumentos.Texto(foto.Visibilidad), false, sha, OpcionesDocumentacion.CanalVigente, null, null, null), ct);
        await avisos.VersionNuevaAsync(id, numero);
        return Results.Ok(await DetalleAsync(documento, documento.VersionVigente, acceso, contexto, responsables, ct));
    }

    private static async Task<IResult> ArchivarAsync(
        [Description("Id del documento.")] Guid id,
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector,
        ILectorResponsables responsables, TimeProvider reloj, CancellationToken ct)
    {
        var (documento, acceso, error) = await ParaAprobarAsync(id, http.User, contexto, permisosDeSesion, lector, ct);
        if (documento is null)
        {
            return error!;
        }
        documento.Estado = EstadoDocumento.Archivado;
        var ahora = reloj.GetUtcNow();
        await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, PublicacionDocumentos.AccionArchivado, id, documento.VersionVigente, new { version = documento.VersionVigente }), null, ct);
        return Results.Ok(await DetalleAsync(documento, documento.VersionVigente, acceso!, contexto, responsables, ct));
    }

    // --- Piezas comunes ---

    private static async Task<(Documento? Documento, AccesoDocumentos? Acceso, IResult? Error)> ParaEditarAsync(
        Guid id, ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, bool soloLectura, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
        {
            return (null, null, NoEncontrado("El documento no existe."));
        }
        if (!acceso.PuedeEditar(documento.AplicativoId))
        {
            return (null, null, SinPermiso());
        }
        if (soloLectura)
        {
            return (documento, acceso, null);
        }
        return (Bloqueo(documento) is { } bloqueo) ? (null, null, bloqueo) : (documento, acceso, null);
    }

    private static async Task<(Documento? Documento, AccesoDocumentos? Acceso, IResult? Error)> ParaAprobarAsync(
        Guid id, ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
        {
            return (null, null, NoEncontrado("El documento no existe."));
        }
        if (!acceso.PuedeAprobar(documento.AplicativoId))
        {
            return (null, null, Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo el líder técnico o el responsable del aplicativo aprueban sus documentos (atlas.docs.aprobar)."));
        }
        return (Bloqueo(documento) is { } bloqueo) ? (null, null, bloqueo) : (documento, acceso, null);
    }

    private static IResult? Bloqueo(Documento documento) => documento switch
    {
        { Origen: OrigenDocumento.Repo } => Conflicto("documento_de_repo", "Este documento vive en su repo: se cambia con un PR."),
        { Estado: EstadoDocumento.Archivado } => Conflicto("documento_archivado", "Este documento está archivado."),
        _ => null,
    };

    private static async Task<IResult> NuevaRevisionAsync(
        HttpContext http, DocumentacionDbContext contexto, PlataformaDbContext plataforma, AccesoDocumentos acceso, ILectorResponsables responsables, TimeProvider reloj,
        Documento documento, FotoDocumento foto, string nota, OrigenRevisionDocumento origen, int? restauradaDe, string accion, bool creado, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var numero = documento.RevisionActual + 1;
        var revision = Revision(documento.Id, numero, origen, restauradaDe, foto, nota, Persona(http.User), ahora);
        contexto.Revisiones.Add(revision);
        documento.RevisionActual = numero;
        documento.Estado = EstadoDocumento.Borrador;
        if (documento.VersionVigente is null)
        {
            // Sin versiones, la foto es la de la última revisión; con versiones, lo publicado no cambia hasta volver a publicar.
            documento.Tipo = foto.Tipo;
            documento.Titulo = foto.Titulo;
            documento.Visibilidad = foto.Visibilidad;
            documento.Audiencias = [.. foto.Audiencias];
            documento.Responsable = foto.Responsable;
        }
        try
        {
            await PublicacionDocumentos.GuardarAsync(contexto, plataforma, Auditoria(http, ahora, accion, documento.Id, numero, new
            {
                revision = numero, restauradaDe, visibilidad = ReglasDocumentos.Texto(foto.Visibilidad), huella = revision.Huella,
            }), null, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflicto("revision_desactualizada", "Alguien guardó otra revisión al mismo tiempo: recarga y vuelve a intentarlo.");
        }
        var detalle = await DetalleAsync(documento, documento.VersionVigente, acceso, contexto, responsables, ct);
        return creado ? Results.Created($"/api/v1/documentacion/documentos/{documento.Id}/revisiones/{numero}", detalle) : Results.Ok(detalle);
    }

    private static (FotoDocumento? Foto, string? Nota, Dictionary<string, string[]> Errores) LeerSolicitud(SolicitudDocumento solicitud, Guid aplicativoId, AccesoDocumentos acceso)
    {
        var (foto, errores) = ReglasDocumentos.Leer(solicitud.Tipo, solicitud.Titulo, solicitud.Contenido, solicitud.Visibilidad, solicitud.Audiencias, solicitud.Responsable);
        if (foto is not null && ReglasDocumentos.TiposSoloRepo.Contains(foto.Tipo))
        {
            errores["tipo"] = ["Un ADR solo llega desde su repo."];
        }
        if (foto is not null && !acceso.AlcanzaNivel(aplicativoId, foto.Visibilidad))
        {
            errores["visibilidad"] = ["Elige un nivel que alcances a ver."];
        }
        var nota = (solicitud.Nota ?? string.Empty).Trim();
        if (nota.Length is 0 or > DocumentacionDbContext.LargoNota)
        {
            errores["nota"] = [$"La nota del cambio es obligatoria y va hasta {DocumentacionDbContext.LargoNota} caracteres."];
        }
        return errores.Count > 0 ? (null, null, errores) : (foto, nota, errores);
    }

    /// <summary>
    /// §7.2: lo que ven los clientes lo aprueba el principal o el secundario. Un aplicativo sin responsables no se queda sin
    /// quien apruebe: lo hace quien tiene <c>atlas.docs.aprobar</c> global (ATLAS-ADR-0053, «por verificar con el líder»).
    /// </summary>
    private static async Task<bool> PuedeAprobarPublicoAsync(Guid aplicativoId, AccesoDocumentos acceso, ILectorResponsables responsables, CancellationToken ct)
    {
        if (acceso.EsResponsableDe(aplicativoId))
        {
            return true;
        }
        var cadena = ReglasDocumentos.ConAncestros(aplicativoId, acceso.Aplicativos).ToList();
        return (await responsables.DeAplicativosAsync(cadena, ct)).Count == 0;
    }

    private static FotoDocumento Foto(RevisionDocumento r) => new(r.Tipo, r.Titulo, r.Contenido, r.Visibilidad, [.. r.Audiencias], r.Responsable);

    private static RevisionDocumento Revision(Guid documentoId, int numero, OrigenRevisionDocumento origen, int? restauradaDe, FotoDocumento foto, string nota, Guid autor, DateTimeOffset ahora) => new()
    {
        DocumentoId = documentoId, Numero = numero, Origen = origen, RestauradaDe = restauradaDe, Tipo = foto.Tipo, Titulo = foto.Titulo, Contenido = foto.Contenido,
        Visibilidad = foto.Visibilidad, Audiencias = [.. foto.Audiencias], Responsable = foto.Responsable, Nota = nota, AutorPersonaId = autor, GuardadaEn = ahora, Huella = foto.Huella(),
    };

    private static async Task<DocumentoDetalle> DetalleAsync(
        Documento documento, int? numero, AccesoDocumentos acceso, DocumentacionDbContext contexto, ILectorResponsables responsables, CancellationToken ct)
    {
        var canales = await contexto.Canales.AsNoTracking().Where(c => c.DocumentoId == documento.Id).OrderBy(c => c.Ambiente)
            .Select(c => new CanalDocumentoDto(c.Ambiente, c.VersionNumero)).ToListAsync(ct);
        var version = numero is { } n ? await LeerVersionAsync(documento.Id, n, documento.AplicativoId, acceso, contexto, ct) : null;
        var deAtlas = documento.Origen == OrigenDocumento.Atlas;
        var editar = deAtlas && documento.Estado != EstadoDocumento.Archivado && acceso.PuedeEditar(documento.AplicativoId);
        var aprobar = deAtlas && documento.Estado != EstadoDocumento.Archivado && acceso.PuedeAprobar(documento.AplicativoId)
            && (documento.Visibilidad != VisibilidadDocumento.PublicoClientes || await PuedeAprobarPublicoAsync(documento.AplicativoId, acceso, responsables, ct));
        return new DocumentoDetalle(Resumen(documento), canales, version, editar, aprobar);
    }

    private static DocumentoResumen Resumen(Documento d) => new(
        d.Id, d.AplicativoId, ReglasDocumentos.Texto(d.Origen), d.Tipo, d.Titulo, ReglasDocumentos.Texto(d.Visibilidad), d.Audiencias, d.Responsable,
        ReglasDocumentos.Texto(d.Estado), d.Ruta, d.VersionVigente, d.RevisionActual, d.ActualizadoEn);

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    private static EventoAuditoria Auditoria(HttpContext http, DateTimeOffset ahora, string accion, Guid documentoId, int? numero, object datos) =>
        PublicacionDocumentos.Evento(http, ahora, accion, documentoId, numero, TipoActor.Persona, http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona), datos);

    private static IResult? ErrorMdx(string contenido)
    {
        var errores = ValidadorMdx.Validar(contenido);
        return errores.Count == 0
            ? null
            : Results.Json(new ErrorDocumento("mdx_invalido", "El contenido usa algo que la lista blanca no admite.", errores), statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorDocumento(codigo, mensaje), statusCode: estado);

    private static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, "sin_permiso", "Solo quien tiene atlas.docs.editar sobre el aplicativo edita sus documentos.");

    private static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    private static IResult Desactualizada(Documento documento) =>
        Conflicto("revision_desactualizada", $"El documento ya va en la revisión {documento.RevisionActual}: recárgalo y vuelve a intentarlo.");
}
