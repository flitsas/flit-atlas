using System.ComponentModel;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Equipo.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// Historial versionado del reparto (HU 2.3, ATLAS-ADR-0031): importar el documento con vista previa, cambiar o retirar
/// un responsable, ver, comparar y restaurar versiones. Cada escritura crea una versión con autor y nota.
/// </summary>
internal static class EndpointsHistorial
{
    public const string RecursoReparto = "equipo.reparto";

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/versiones", ListarAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ListarVersionesReparto")
            .WithSummary("Historial del reparto")
            .WithDescription("Las versiones del reparto, de la más reciente a la más antigua, con su origen, autor, nota y huella. Requiere atlas.equipo.ver.")
            .Produces<IReadOnlyList<VersionResumenDto>>();

        grupo.MapGet("/versiones/comparacion", CompararAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("CompararVersionesReparto")
            .WithSummary("Comparar dos versiones del reparto")
            .WithDescription("Qué responsables se agregaron, se retiraron o cambiaron de persona o de datos de una versión a otra. 404 si alguna no existe. Requiere atlas.equipo.ver.")
            .Produces<ComparacionDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);

        grupo.MapGet("/versiones/{numero}", ObtenerAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerVersionReparto")
            .WithSummary("Una versión del reparto")
            .WithDescription("La foto completa de una versión, con las definiciones de los roles y sus tablas en Markdown. 404 si no existe. Requiere atlas.equipo.ver.")
            .Produces<VersionDetalleDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);

        grupo.MapPost("/versiones/{numero}/restauracion", RestaurarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("RestaurarVersionReparto")
            .WithSummary("Restaurar una versión del reparto")
            .WithDescription("Deja el reparto como en esa versión y crea una versión nueva igual a ella (misma huella), con la nota. Un secundario que cambia empieza en acompañamiento. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<VersionResumenDto>(StatusCodes.Status201Created)
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/responsabilidades", CambiarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("CambiarResponsable")
            .WithSummary("Cambiar o asignar un responsable")
            .WithDescription("Pone a la persona como principal o secundaria del aplicativo (reemplaza a la anterior) y crea la versión siguiente con autor y nota. Un secundario nuevo empieza en acompañamiento. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<VersionResumenDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapDelete("/responsabilidades/{id:guid}", RetirarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("RetirarResponsable")
            .WithSummary("Retirar un responsable")
            .WithDescription("Quita la responsabilidad del reparto y crea la versión siguiente con la nota. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<VersionResumenDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/importacion/vista-previa", PrevisualizarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("PrevisualizarImportacionResponsables")
            .WithSummary("Vista previa de la importación del documento")
            .WithDescription("Lee el documento de responsables (Markdown), lo enlaza con el catálogo y con las personas (nombre sin tildes ni mayúsculas, o correo) y dice qué quedaría, qué no se pudo enlazar y las inconsistencias. No guarda nada. Requiere atlas.equipo.gestionar.")
            .Produces<VistaPreviaDto>()
            .ProducesValidationProblem();

        grupo.MapPost("/importacion", ImportarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("ImportarResponsables")
            .WithSummary("Importar el documento de responsables")
            .WithDescription("Reemplaza el reparto por el del documento y crea la versión que dice el documento (debe ser mayor que la vigente), con sus definiciones y la próxima revisión. Exige la huella de la vista previa (409 si algo cambió) y que no quede nada sin enlazar. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<VersionResumenDto>(StatusCodes.Status201Created)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ListarAsync(EquipoDbContext equipo, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var versiones = await equipo.Versiones.AsNoTracking().OrderByDescending(v => v.Numero).ToListAsync(ct);
        var autores = await AutoresAsync(plataforma, versiones, ct);
        return Results.Ok(versiones.Select(v => VersionDto(v, autores)).ToList());
    }

    private static async Task<IResult> ObtenerAsync(
        [Description("Número de la versión.")] int numero, EquipoDbContext equipo, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var version = await equipo.Versiones.AsNoTracking().FirstOrDefaultAsync(v => v.Numero == numero, ct);
        if (version is null)
        {
            return NoEncontrado("La versión no existe.");
        }
        var foto = FotoReparto.DesdeJson(version.Contenido);
        var autores = await AutoresAsync(plataforma, [version], ct);
        return Results.Ok(new VersionDetalleDto(VersionDto(version, autores), Definiciones(version),
            [.. foto.Ordenadas().Select(r => new ResponsabilidadVersionDto(new AplicativoRefDto(r.AplicativoId, r.Slug, r.Aplicativo, r.TipoAplicativo),
                r.Tipo, r.PersonaId, r.Persona, r.Dedicacion, r.VigenteDesde, r.VigenteHasta))],
            EndpointsEquipo.Tablas(version.Numero, foto)));
    }

    private static async Task<IResult> CompararAsync(
        [Description("Versión base.")] int desde,
        [Description("Versión a comparar con la base.")] int hasta,
        EquipoDbContext equipo, CancellationToken ct)
    {
        var versiones = await equipo.Versiones.AsNoTracking().Where(v => v.Numero == desde || v.Numero == hasta).ToListAsync(ct);
        var a = versiones.FirstOrDefault(v => v.Numero == desde);
        var b = versiones.FirstOrDefault(v => v.Numero == hasta);
        if (a is null || b is null)
        {
            return NoEncontrado("Alguna de las versiones no existe.");
        }
        var antes = FotoReparto.DesdeJson(a.Contenido).Responsabilidades.ToDictionary(r => (r.AplicativoId, r.Tipo));
        var despues = FotoReparto.DesdeJson(b.Contenido).Responsabilidades.ToDictionary(r => (r.AplicativoId, r.Tipo));
        var cambios = new List<(int Orden, CambioVersionDto Cambio)>();
        foreach (var clave in antes.Keys.Union(despues.Keys))
        {
            var x = antes.GetValueOrDefault(clave);
            var y = despues.GetValueOrDefault(clave);
            var referencia = y ?? x!;
            var aplicativo = new AplicativoRefDto(referencia.AplicativoId, referencia.Slug, referencia.Aplicativo, referencia.TipoAplicativo);
            if (x is null)
            {
                cambios.Add((referencia.OrdenAplicativo, new CambioVersionDto(aplicativo, clave.Tipo, "agregada", null, y!.Persona, [])));
                continue;
            }
            if (y is null)
            {
                cambios.Add((referencia.OrdenAplicativo, new CambioVersionDto(aplicativo, clave.Tipo, "retirada", x.Persona, null, [])));
                continue;
            }
            var campos = new List<string>();
            if (x.Dedicacion != y.Dedicacion)
            {
                campos.Add("dedicacion");
            }
            if (x.VigenteDesde != y.VigenteDesde || x.VigenteHasta != y.VigenteHasta)
            {
                campos.Add("vigencia");
            }
            if (x.OrdenAplicativo != y.OrdenAplicativo || x.OrdenPersona != y.OrdenPersona || x.OrdenEnPersona != y.OrdenEnPersona)
            {
                campos.Add("orden");
            }
            if (x.PersonaId != y.PersonaId)
            {
                cambios.Add((referencia.OrdenAplicativo, new CambioVersionDto(aplicativo, clave.Tipo, "persona", x.Persona, y.Persona, campos)));
            }
            else if (campos.Count > 0)
            {
                cambios.Add((referencia.OrdenAplicativo, new CambioVersionDto(aplicativo, clave.Tipo, "datos", x.Persona, y.Persona, campos)));
            }
        }
        return Results.Ok(new ComparacionDto(desde, hasta, a.Huella == b.Huella,
            [.. cambios.OrderBy(c => c.Orden).ThenBy(c => c.Cambio.Tipo == "principal" ? 0 : 1).Select(c => c.Cambio)]));
    }

    private static async Task<IResult> RestaurarAsync(
        [Description("Número de la versión a restaurar.")] int numero,
        [Description("Nota de la restauración.")] SolicitudNota solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Textos.Limpiar(solicitud.Nota);
        if (nota is null || nota.Length > EquipoDbContext.LargoNota)
        {
            return Invalido("nota", $"La nota es obligatoria y va hasta {EquipoDbContext.LargoNota} caracteres.");
        }
        var origen = await equipo.Versiones.AsNoTracking().FirstOrDefaultAsync(v => v.Numero == numero, ct);
        if (origen is null)
        {
            return NoEncontrado("La versión no existe.");
        }
        var siguiente = await SiguienteAsync(equipo, ct);
        var version = await AplicadorReparto.AplicarAsync(equipo, FotoReparto.DesdeJson(origen.Contenido),
            new AplicadorReparto.DatosVersion(siguiente, OrigenVersion.Restauracion, origen.Numero, origen.FechaDocumento, null,
                PersonaDeSesion(http.User), nota, origen.Definiciones),
            AplicadorReparto.EstadoPorDefecto, ct);
        return await GuardarVersionAsync(http, equipo, plataforma, reloj, version, "reparto_restaurado", new { version = siguiente, restaurada = origen.Numero }, ct, creada: true);
    }

    private static async Task<IResult> CambiarAsync(
        [Description("Aplicativo, tipo, persona, dedicación, vigencia y nota.")] SolicitudCambioResponsable solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tipo = Textos.Leer<TipoResponsabilidad>(solicitud.Tipo);
        if (tipo is null)
        {
            errores["tipo"] = [$"El tipo es uno de: {Textos.Lista<TipoResponsabilidad>()}."];
        }
        var nota = Textos.Limpiar(solicitud.Nota);
        if (nota is null || nota.Length > EquipoDbContext.LargoNota)
        {
            errores["nota"] = [$"La nota es obligatoria y va hasta {EquipoDbContext.LargoNota} caracteres."];
        }
        if (solicitud.Dedicacion is not (null or (>= 1 and <= 100)))
        {
            errores["dedicacion"] = ["La dedicación va de 1 a 100 %."];
        }
        var hoy = CalendarioBogota.Hoy(reloj);
        var desde = solicitud.VigenteDesde ?? hoy;
        if (solicitud.VigenteHasta is { } hasta && hasta < desde)
        {
            errores["vigenteHasta"] = ["El fin de la vigencia no puede ser anterior a su inicio."];
        }
        var aplicativo = (await catalogo.ListarAsync(ct)).FirstOrDefault(a => a.Id == solicitud.AplicativoId);
        if (aplicativo is null)
        {
            errores["aplicativoId"] = ["El aplicativo no existe en el catálogo."];
        }
        var persona = await plataforma.Personas.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == solicitud.PersonaId && p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa, ct);
        if (persona is null)
        {
            errores["personaId"] = ["La persona debe ser un empleado activo."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        var otra = datos.Responsabilidades.FirstOrDefault(r => r.AplicativoId == aplicativo!.Id && r.Tipo != tipo && r.PersonaId == persona!.Id);
        if (otra is not null)
        {
            return Conflicto("misma_persona", "La misma persona no puede ser principal y secundaria del mismo aplicativo.");
        }
        var dedicacion = solicitud.Dedicacion ?? (tipo == TipoResponsabilidad.Secundario ? MapaCarga.DedicacionSecundarioPorDefecto : null);
        var objetivo = AplicadorReparto.ConCambio(datos.Foto(), aplicativo!, tipo!.Value, persona!.Id, persona.Nombre, dedicacion, desde, solicitud.VigenteHasta);
        var siguiente = await SiguienteAsync(equipo, ct);
        var version = await AplicadorReparto.AplicarAsync(equipo, objetivo,
            new AplicadorReparto.DatosVersion(siguiente, OrigenVersion.Cambio, null, null, null, PersonaDeSesion(http.User), nota!,
                datos.Version?.Definiciones ?? "[]"),
            AplicadorReparto.EstadoPorDefecto, ct);
        return await GuardarVersionAsync(http, equipo, plataforma, reloj, version, "responsable_cambiado",
            new { version = siguiente, aplicativo = aplicativo!.Id, tipo = Textos.De(tipo.Value), persona = persona.Id }, ct);
    }

    private static async Task<IResult> RetirarAsync(
        [Description("Id de la responsabilidad.")] Guid id,
        [Description("Por qué se retira (hasta 500).")] string? nota,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var limpia = Textos.Limpiar(nota);
        if (limpia is null || limpia.Length > EquipoDbContext.LargoNota)
        {
            return Invalido("nota", $"La nota es obligatoria y va hasta {EquipoDbContext.LargoNota} caracteres.");
        }
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        var responsabilidad = datos.Responsabilidades.FirstOrDefault(r => r.Id == id);
        if (responsabilidad is null)
        {
            return NoEncontrado("La responsabilidad no existe.");
        }
        var foto = datos.Foto();
        var objetivo = new FotoReparto([.. foto.Responsabilidades.Where(r => !(r.AplicativoId == responsabilidad.AplicativoId && r.Tipo == Textos.De(responsabilidad.Tipo)))]);
        var siguiente = await SiguienteAsync(equipo, ct);
        var version = await AplicadorReparto.AplicarAsync(equipo, objetivo,
            new AplicadorReparto.DatosVersion(siguiente, OrigenVersion.Cambio, null, null, null, PersonaDeSesion(http.User), limpia,
                datos.Version?.Definiciones ?? "[]"),
            AplicadorReparto.EstadoPorDefecto, ct);
        return await GuardarVersionAsync(http, equipo, plataforma, reloj, version, "responsable_retirado",
            new { version = siguiente, aplicativo = responsabilidad.AplicativoId, tipo = Textos.De(responsabilidad.Tipo) }, ct);
    }

    private static async Task<int> SiguienteAsync(EquipoDbContext equipo, CancellationToken ct) =>
        (await equipo.Versiones.MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1;

    private static async Task<IResult> GuardarVersionAsync(
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, TimeProvider reloj, VersionReparto version, string accion,
        object datos, CancellationToken ct, bool creada = false, IEnumerable<EventoAuditoria>? otros = null)
    {
        try
        {
            await GuardarAsync(equipo, plataforma, [Evento(http, reloj, accion, RecursoReparto, version.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture), datos), .. otros ?? []], ct);
        }
        catch (DbUpdateException error) when (EsVersionRepetida(error))
        {
            return Conflicto("version_repetida", "Otra persona cambió el reparto al mismo tiempo. Vuelve a cargar y repite el cambio.");
        }
        var autores = await AutoresAsync(plataforma, [version], ct);
        var dto = VersionDto(version, autores);
        return creada ? Results.Created($"/api/v1/equipo/versiones/{version.Numero}", dto) : Results.Ok(dto);
    }

    // --- Importación ---

    private static async Task<(PlanImportacion Plan, int? Actual)> PlanAsync(
        string markdown, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var documento = DocumentoResponsables.Leer(markdown);
        var aplicativos = await catalogo.ListarAsync(ct);
        var personas = await plataforma.Personas.AsNoTracking()
            .Where(p => p.Tipo == TipoPersona.Empleado && p.Estado != EstadoPersona.Suspendida)
            .Select(p => new PersonaCandidata(p.Id, p.Nombre, p.Correo, p.Estado == EstadoPersona.Activa))
            .ToListAsync(ct);
        var plan = PlanImportacion.Armar(documento, aplicativos, personas, CalendarioBogota.Hoy(reloj));
        var actual = await equipo.Versiones.MaxAsync(v => (int?)v.Numero, ct);
        return (plan, actual);
    }

    private static string? ErrorDeVersion(PlanImportacion plan, int? actual) =>
        plan.Documento.Version is { } v && actual is { } a && v <= a
            ? $"El documento es la versión {v}, pero Atlas ya va en la {a}: importa un documento con una versión mayor o cambia el reparto en Atlas."
            : null;

    private static async Task<IResult> PrevisualizarAsync(
        [Description("El documento de responsables en Markdown.")] SolicitudVistaPrevia solicitud,
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Markdown) || solicitud.Markdown.Length > DocumentoResponsables.TamanoMaximo)
        {
            return Invalido("markdown", "El documento es obligatorio y va hasta 100 KB.");
        }
        var (plan, actual) = await PlanAsync(solicitud.Markdown, equipo, plataforma, catalogo, reloj, ct);
        var errorVersion = ErrorDeVersion(plan, actual);
        var d = plan.Documento;
        return Results.Ok(new VistaPreviaDto(d.Version, d.Fecha, d.DefinidoPor, d.DescripcionVersion, d.DedicacionSecundario, d.ProximaRevision,
            [.. d.Definiciones.Select(x => new DefinicionRolDto(x.Rol, x.Titulo, x.Puntos))],
            [.. plan.Foto.Ordenadas().Select(r => new FilaVistaPreviaDto(new AplicativoRefDto(r.AplicativoId, r.Slug, r.Aplicativo, r.TipoAplicativo), r.Tipo, r.PersonaId, r.Persona, r.Dedicacion))],
            [.. plan.Pendientes.Select(p => new PendienteDto(p.Tipo, p.Nombre, p.Motivo, p.Detalle))],
            plan.Inconsistencias, plan.Avisos, errorVersion is null ? d.Errores : [.. d.Errores, errorVersion], actual,
            plan.PuedeConfirmar && errorVersion is null, plan.Huella()));
    }

    private static async Task<IResult> ImportarAsync(
        [Description("El mismo documento de la vista previa y su huella.")] SolicitudImportacion solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Markdown) || solicitud.Markdown.Length > DocumentoResponsables.TamanoMaximo)
        {
            return Invalido("markdown", "El documento es obligatorio y va hasta 100 KB.");
        }
        var traspaso = solicitud.Traspaso is null ? EstadoTraspaso.Habilitado : Textos.Leer<EstadoTraspaso>(solicitud.Traspaso);
        if (traspaso is null)
        {
            return Invalido("traspaso", $"El traspaso es uno de: {Textos.Lista<EstadoTraspaso>()}.");
        }
        var (plan, actual) = await PlanAsync(solicitud.Markdown, equipo, plataforma, catalogo, reloj, ct);
        if (!string.Equals(plan.Huella(), solicitud.Huella, StringComparison.Ordinal))
        {
            return Conflicto("vista_previa_cambio", "El documento, el catálogo o las personas cambiaron desde la vista previa. Vuelve a revisarla.");
        }
        if (ErrorDeVersion(plan, actual) is { } errorVersion)
        {
            return Conflicto("version_no_valida", errorVersion);
        }
        if (!plan.PuedeConfirmar)
        {
            return Conflicto("hay_pendientes", "Hay aplicativos o personas sin enlazar, o el documento tiene errores: resuélvelos y vuelve a revisar.");
        }

        var d = plan.Documento;
        var autor = PersonaDeSesion(http.User);
        var version = await AplicadorReparto.AplicarAsync(equipo, plan.Foto,
            new AplicadorReparto.DatosVersion(d.Version!.Value, OrigenVersion.Importacion, null, d.Fecha, d.DefinidoPor, autor,
                d.DescripcionVersion is { Length: > 0 } descripcion ? Recortar($"Importación del documento: {descripcion}") : "Importación del documento de responsables.",
                FotoReparto.DefinicionesAJson(d.Definiciones)),
            tipo => tipo == TipoResponsabilidad.Secundario ? traspaso.Value : EstadoTraspaso.Habilitado, ct);

        // La próxima revisión del documento, si no hay una programada.
        var eventos = new List<EventoAuditoria>();
        if (d.ProximaRevision is { } proxima && !await equipo.Revisiones.AnyAsync(r => r.Estado == EstadoRevision.Pendiente, ct))
        {
            var revision = new RevisionProgramada { Fecha = proxima };
            equipo.Revisiones.Add(revision);
            eventos.Add(Evento(http, reloj, "revision_programada", EndpointsGestion.RecursoRevision, revision.Id.ToString(), new { fecha = proxima }));
        }
        return await GuardarVersionAsync(http, equipo, plataforma, reloj, version, "reparto_importado",
            new { version = version.Numero, responsabilidades = plan.Foto.Responsabilidades.Count }, ct, creada: true, eventos);
    }

    private static string Recortar(string texto) => texto.Length <= EquipoDbContext.LargoNota ? texto : texto[..EquipoDbContext.LargoNota];
}
