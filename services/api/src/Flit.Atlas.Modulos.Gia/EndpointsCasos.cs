using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>La parte de la ficha que edita una persona (plan 02 §7.1). También es lo que guarda cada versión.</summary>
[Description("La ficha de un caso de uso (plan 02 §7.1). Lo obligatorio se exige al enviarla a revisión.")]
public sealed record FichaCaso(
    string Nombre,
    [property: Description("Aplicación o repo, como texto.")] string Aplicacion,
    [property: Description("Aplicativo del catálogo, si se conoce.")] Guid? AplicativoId,
    string? Repo,
    [property: Description("Persona dueña del caso.")] Guid? DuenoPersonaId,
    [property: Description("dev, qa o pdn.")] IReadOnlyList<string> Ambientes,
    string Proposito,
    [property: Description("ocr, chat, clasificacion, generacion, agente, embeddings u otro.")] string? Tipo,
    [property: Description("interno, cliente o ciudadano.")] IReadOnlyList<string> UsuariosAfectados,
    bool? DecisionSobrePersonas,
    string? DecisionDescripcion,
    [property: Description("ninguno, revision_posterior o aprobacion_previa.")] string? HumanoEnBucle,
    [property: Description("publico, interno, confidencial, personal o sensible.")] IReadOnlyList<string> CategoriasDato,
    IReadOnlyList<string> TiposPii,
    int? VolumenMensualEstimado,
    string? BaseLegal,
    string? BaseLegalRef,
    string? Minimizacion,
    bool? PresidioActivo,
    IReadOnlyList<string> PresidioEntidades,
    [property: Description("Alias del gateway; hoy, los propuestos.")] IReadOnlyList<string> AliasUsados,
    string? ProveedorActual,
    string? TransferenciaInternacional,
    string? MetricasCalidad,
    string? EvalsRuta,
    string? PromptsUbicacion,
    string? Controles,
    string? AvisoUsuario,
    string? PlanRollback,
    [property: Description("ADR, historias y PR relacionados.")] IReadOnlyList<string> Enlaces)
{
    public static FichaCaso De(CasoUso c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.Nombre, c.Aplicacion, c.AplicativoId, c.Repo, c.DuenoPersonaId, c.Ambientes, c.Proposito, c.Tipo, c.UsuariosAfectados,
            c.DecisionSobrePersonas, c.DecisionDescripcion, c.HumanoEnBucle, c.CategoriasDato, c.TiposPii, c.VolumenMensualEstimado, c.BaseLegal, c.BaseLegalRef,
            c.Minimizacion, c.PresidioActivo, c.PresidioEntidades, c.AliasUsados, c.ProveedorActual, c.TransferenciaInternacional, c.MetricasCalidad, c.EvalsRuta,
            c.PromptsUbicacion, c.Controles, c.AvisoUsuario, c.PlanRollback, c.Enlaces);
    }
}

/// <summary>Casos de uso en <c>/api/v1/gia</c> (HU 8.1, ATLAS-ADR-0055 §3). Cada cambio va con su auditoría en la misma transacción.</summary>
internal static class EndpointsCasos
{
    public const string Recurso = "gia.caso_uso";

    [Description("Un caso de uso en un listado.")]
    public sealed record CasoResumen(
        Guid Id, string Codigo, string Nombre, string Aplicacion,
        [property: Description("borrador, en_revision, aprobado, en_produccion, suspendido o retirado.")] string Estado,
        [property: Description("Nivel de la evaluación aprobada; vacío si no la hay.")] string? NivelRiesgo,
        [property: Description("Lo que estimó el plan: bajo, medio, medio_alto o alto. No es una evaluación.")] string? RiesgoPreliminar,
        PersonaGia? Dueno,
        DateOnly? ProximaRevision,
        [property: Description("Si la próxima revisión ya pasó.")] bool RevisionVencida);

    [Description("Casos de uso.")]
    public sealed record ListadoCasos(IReadOnlyList<CasoResumen> Elementos);

    [Description("Un caso con su ficha y lo que falta para enviarlo a revisión.")]
    public sealed record CasoDetalle(
        CasoResumen Caso,
        FichaCaso Ficha,
        [property: Description("La ficha del plan 02 tal como está, con sus marcas [por verificar]; Markdown.")] string? DetallePlan,
        int VersionActual,
        PersonaGia? AprobadoPor,
        DateTimeOffset? AprobadoEn,
        [property: Description("Campos obligatorios sin llenar.")] IReadOnlyList<string> Faltantes,
        [property: Description("Si la persona puede editarlo y moverlo de estado (atlas.gia.gestionar).")] bool PuedeGestionar);

    [Description("Crear un caso en borrador.")]
    public sealed record SolicitudCrearCaso(
        [property: Description("Minúsculas, números y guiones (app-caso). No cambia.")] string Codigo,
        FichaCaso Ficha);

    [Description("Guardar la ficha: crea una versión nueva.")]
    public sealed record SolicitudGuardarCaso(
        FichaCaso Ficha,
        [property: Description("Mensaje del cambio, hasta 500 caracteres.")] string Motivo,
        [property: Description("La versión sobre la que se trabajó: 409 si ya hay otra más nueva.")] int VersionBase);

    [Description("Cambiar el estado de un caso.")]
    public sealed record SolicitudEstadoCaso(
        [property: Description("en_produccion, suspendido, borrador (reabrir) o retirado.")] string Estado,
        [property: Description("Por qué, hasta 500 caracteres.")] string Motivo);

    [Description("Una versión de la ficha.")]
    public sealed record VersionCasoResumen(int Numero, string Motivo, PersonaGia? Autor, DateTimeOffset GuardadaEn, FichaCaso Ficha);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/casos", ListarAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarCasosIa").WithSummary("Casos de uso de IA")
            .WithDescription("El inventario, ordenado por código, con filtros por estado y revisión vencida. Requiere atlas.gia.ver.")
            .Produces<ListadoCasos>();

        grupo.MapPost("/casos", CrearAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearCasoIa").WithSummary("Registrar un caso de uso")
            .WithDescription("Crea el caso en borrador con su versión 1. 409 ya_existe si el código está en uso. Requiere atlas.gia.gestionar.")
            .Produces<CasoDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/casos/{id:guid}", DetalleAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ObtenerCasoIa").WithSummary("Un caso con su ficha")
            .WithDescription("La ficha completa y lo que falta para enviarla a revisión. 404 si no existe. Requiere atlas.gia.ver.")
            .Produces<CasoDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);

        grupo.MapPut("/casos/{id:guid}", GuardarAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarCasoIa").WithSummary("Guardar la ficha")
            .WithDescription("Crea una versión inmutable. Solo en borrador: lo aprobado se reabre antes (409 estado_invalido). 409 version_desactualizada si versionBase no es la última. Requiere atlas.gia.gestionar.")
            .Produces<CasoDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/casos/{id:guid}/versiones", VersionesAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarVersionesCasoIa").WithSummary("Historial de la ficha")
            .WithDescription("Las versiones de la ficha, de la más reciente a la más antigua. 404 si no existe. Requiere atlas.gia.ver.")
            .Produces<List<VersionCasoResumen>>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);

        grupo.MapPost("/casos/{id:guid}/envio-revision", EnviarAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("EnviarCasoIaRevision").WithSummary("Enviar a revisión")
            .WithDescription("De borrador a en_revision. 422 ficha_incompleta con los campos que faltan; 422 sin_evaluacion si no tiene una evaluación pendiente. Requiere atlas.gia.gestionar.")
            .Produces<CasoDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .Produces<ErrorGia>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/casos/{id:guid}/estado", CambiarEstadoAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CambiarEstadoCasoIa").WithSummary("Cambiar de estado")
            .WithDescription("Poner en producción, suspender, reabrir (borrador) o retirar, con motivo. Reabrir quita la aprobación. Aprobar se hace votando. 409 estado_invalido si la transición no aplica. Requiere atlas.gia.gestionar.")
            .Produces<CasoDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/personas", PersonasAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("BuscarPersonasGia").WithSummary("Buscar empleados")
            .WithDescription("Empleados activos por nombre, hasta 20, para elegir dueños, responsables y roles de gobierno. Sin datos de contacto. Requiere atlas.gia.gestionar.")
            .Produces<List<PersonaGia>>();
    }

    private static async Task<IResult> ListarAsync(
        [Description("Filtra por estado.")] string? estado,
        [Description("Solo los que tienen la revisión vencida.")] bool? vencidos,
        GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var consulta = contexto.Casos.AsNoTracking();
        if (ApoyoGia.Vacio(estado) is { } filtro)
        {
            consulta = consulta.Where(c => c.Estado == filtro);
        }
        var hoy = Hoy(reloj);
        if (vencidos == true)
        {
            consulta = consulta.Where(c => c.ProximaRevision < hoy);
        }
        var casos = await consulta.OrderBy(c => c.Codigo).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, casos.Select(c => c.DuenoPersonaId), ct);
        return Results.Ok(new ListadoCasos([.. casos.Select(c => Resumen(c, nombres, hoy))]));
    }

    private static async Task<IResult> CrearAsync(
        [Description("El caso.")] SolicitudCrearCaso solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var codigo = (solicitud.Codigo ?? string.Empty).Trim();
        if (!CodigoValido(codigo))
        {
            errores["codigo"] = [$"Minúsculas, números y guiones, hasta {GiaDbContext.LargoCodigo} caracteres."];
        }
        var ficha = await LeerAsync(solicitud.Ficha, plataforma, errores, ct);
        if (errores.Count > 0 || ficha is null)
        {
            return Results.ValidationProblem(errores);
        }
        if (await contexto.Casos.AnyAsync(c => c.Codigo == codigo, ct))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya hay un caso con ese código.");
        }
        var ahora = reloj.GetUtcNow();
        var persona = ApoyoGia.Persona(http.User);
        var caso = new CasoUso { Codigo = codigo, Nombre = ficha.Nombre, Aplicacion = ficha.Aplicacion, Proposito = ficha.Proposito };
        Aplicar(caso, ficha);
        contexto.Casos.Add(caso);
        contexto.VersionesCaso.Add(Version(caso, 1, ficha, "Creación", persona, ahora));
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.caso_creado", Recurso, caso.Id, "1", new { codigo }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya hay un caso con ese código.");
        }
        return Results.Created($"/api/v1/gia/casos/{caso.Id}", await DetalleDeAsync(caso, plataforma, reloj, ct));
    }

    private static async Task<IResult> DetalleAsync(
        [Description("Id del caso.")] Guid id, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return caso is null ? NoEncontrado() : Results.Ok(await DetalleDeAsync(caso, plataforma, reloj, ct));
    }

    private static async Task<IResult> GuardarAsync(
        [Description("Id del caso.")] Guid id,
        [Description("La ficha y su motivo.")] SolicitudGuardarCaso solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caso is null)
        {
            return NoEncontrado();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var motivo = ApoyoGia.Texto(solicitud.Motivo, "motivo", GiaDbContext.LargoCorto, errores);
        var ficha = await LeerAsync(solicitud.Ficha, plataforma, errores, ct);
        if (errores.Count > 0 || ficha is null)
        {
            return Results.ValidationProblem(errores);
        }
        if (caso.Estado != ValoresGia.EstadoBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se edita en borrador: reabre el caso primero.");
        }
        if (solicitud.VersionBase != caso.VersionActual)
        {
            return ApoyoGia.Conflicto("version_desactualizada", $"La ficha ya va en la versión {caso.VersionActual}: recárgala y vuelve a intentarlo.");
        }
        var ahora = reloj.GetUtcNow();
        Aplicar(caso, ficha);
        caso.VersionActual++;
        contexto.VersionesCaso.Add(Version(caso, caso.VersionActual, ficha, motivo, ApoyoGia.Persona(http.User), ahora));
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.caso_guardado", Recurso, caso.Id,
                caso.VersionActual.ToString(CultureInfo.InvariantCulture), new { caso.Codigo }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("version_desactualizada", "Alguien guardó al mismo tiempo: recarga la ficha y vuelve a intentarlo.");
        }
        return Results.Ok(await DetalleDeAsync(caso, plataforma, reloj, ct));
    }

    private static async Task<IResult> VersionesAsync(
        [Description("Id del caso.")] Guid id, GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (!await contexto.Casos.AnyAsync(c => c.Id == id, ct))
        {
            return NoEncontrado();
        }
        var versiones = await contexto.VersionesCaso.AsNoTracking().Where(v => v.CasoUsoId == id).OrderByDescending(v => v.Numero).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, versiones.Select(v => v.AutorPersonaId), ct);
        return Results.Ok(versiones.Select(v => new VersionCasoResumen(v.Numero, v.Motivo, ApoyoGia.Ref(v.AutorPersonaId, nombres), v.GuardadaEn,
            JsonSerializer.Deserialize<FichaCaso>(v.Foto, JsonSerializerOptions.Web)!)).ToList());
    }

    private static async Task<IResult> EnviarAsync(
        [Description("Id del caso.")] Guid id,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caso is null)
        {
            return NoEncontrado();
        }
        if (caso.Estado != ValoresGia.EstadoBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se envía a revisión un caso en borrador.");
        }
        var faltantes = ReglasGia.FaltantesFicha(caso);
        if (faltantes.Count > 0)
        {
            return ApoyoGia.Invalido("ficha_incompleta", "Faltan campos obligatorios de la ficha.", faltantes);
        }
        var ultima = await contexto.Evaluaciones.AsNoTracking().Where(e => e.CasoUsoId == id).OrderByDescending(e => e.Version).FirstOrDefaultAsync(ct);
        if (ultima?.Decision != ValoresGia.DecisionPendiente)
        {
            return ApoyoGia.Invalido("sin_evaluacion", "El caso necesita una evaluación de riesgo pendiente de decisión.");
        }
        var ahora = reloj.GetUtcNow();
        caso.Estado = ValoresGia.EstadoEnRevision;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.caso_en_revision", Recurso, caso.Id, null, new { caso.Codigo }), ct);
        return Results.Ok(await DetalleDeAsync(caso, plataforma, reloj, ct));
    }

    private static async Task<IResult> CambiarEstadoAsync(
        [Description("Id del caso.")] Guid id,
        [Description("El estado y el motivo.")] SolicitudEstadoCaso solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var caso = await contexto.Casos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (caso is null)
        {
            return NoEncontrado();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var destino = ApoyoGia.Texto(solicitud.Estado, "estado", GiaDbContext.LargoEnum, errores);
        var motivo = ApoyoGia.Texto(solicitud.Motivo, "motivo", GiaDbContext.LargoCorto, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (!ReglasGia.TransicionPermitida(caso.Estado, destino))
        {
            return ApoyoGia.Conflicto("estado_invalido", $"Un caso {caso.Estado} no puede pasar a {destino}.");
        }
        var ahora = reloj.GetUtcNow();
        var anterior = caso.Estado;
        caso.Estado = destino;
        if (destino == ValoresGia.EstadoBorrador)
        {
            caso.AprobadoPor = null;
            caso.AprobadoEn = null;
            caso.NivelRiesgo = null;
            caso.ProximaRevision = null;
        }
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.caso_estado", Recurso, caso.Id, null, new { caso.Codigo, anterior, destino }), ct);
        return Results.Ok(await DetalleDeAsync(caso, plataforma, reloj, ct));
    }

    private static async Task<IResult> PersonasAsync(
        [Description("Busca en el nombre.")] string? texto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var consulta = plataforma.Personas.AsNoTracking().Where(p => p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa);
        if (ApoyoGia.Vacio(texto) is { } termino)
        {
            var patron = FuncionesSql.PatronContiene(termino);
            consulta = consulta.Where(p => EF.Functions.ILike(p.Nombre, patron, FuncionesSql.EscapeLike));
        }
        return Results.Ok(await consulta.OrderBy(p => p.Nombre).Take(20).Select(p => new PersonaGia(p.Id, p.Nombre)).ToListAsync(ct));
    }

    // --- Piezas comunes ---

    internal static DateOnly Hoy(TimeProvider reloj) => DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(TimeSpan.FromHours(-5)).DateTime);

    internal static IResult NoEncontrado() => ApoyoGia.NoEncontrado("El caso no existe.");

    internal static bool CodigoValido(string codigo) =>
        codigo.Length is > 0 and <= GiaDbContext.LargoCodigo && codigo.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    internal static CasoResumen Resumen(CasoUso c, IReadOnlyDictionary<Guid, string> nombres, DateOnly hoy) => new(
        c.Id, c.Codigo, c.Nombre, c.Aplicacion, c.Estado, c.NivelRiesgo, c.RiesgoPreliminar, ApoyoGia.Ref(c.DuenoPersonaId, nombres), c.ProximaRevision, c.ProximaRevision < hoy);

    internal static async Task<CasoDetalle> DetalleDeAsync(CasoUso caso, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nombres = await ApoyoGia.NombresAsync(plataforma, [caso.DuenoPersonaId, caso.AprobadoPor], ct);
        return new CasoDetalle(Resumen(caso, nombres, Hoy(reloj)), FichaCaso.De(caso), caso.DetallePlan, caso.VersionActual,
            ApoyoGia.Ref(caso.AprobadoPor, nombres), caso.AprobadoEn, ReglasGia.FaltantesFicha(caso), PuedeGestionar: true);
    }

    private static VersionCasoUso Version(CasoUso caso, int numero, FichaCaso ficha, string motivo, Guid persona, DateTimeOffset ahora) => new()
    {
        CasoUsoId = caso.Id,
        Numero = numero,
        Foto = JsonSerializer.Serialize(ficha, JsonSerializerOptions.Web),
        Motivo = motivo,
        AutorPersonaId = persona,
        GuardadaEn = ahora,
    };

    private static void Aplicar(CasoUso c, FichaCaso f)
    {
        c.Nombre = f.Nombre;
        c.Aplicacion = f.Aplicacion;
        c.AplicativoId = f.AplicativoId;
        c.Repo = f.Repo;
        c.DuenoPersonaId = f.DuenoPersonaId;
        c.Ambientes = [.. f.Ambientes];
        c.Proposito = f.Proposito;
        c.Tipo = f.Tipo;
        c.UsuariosAfectados = [.. f.UsuariosAfectados];
        c.DecisionSobrePersonas = f.DecisionSobrePersonas;
        c.DecisionDescripcion = f.DecisionDescripcion;
        c.HumanoEnBucle = f.HumanoEnBucle;
        c.CategoriasDato = [.. f.CategoriasDato];
        c.TiposPii = [.. f.TiposPii];
        c.VolumenMensualEstimado = f.VolumenMensualEstimado;
        c.BaseLegal = f.BaseLegal;
        c.BaseLegalRef = f.BaseLegalRef;
        c.Minimizacion = f.Minimizacion;
        c.PresidioActivo = f.PresidioActivo;
        c.PresidioEntidades = [.. f.PresidioEntidades];
        c.AliasUsados = [.. f.AliasUsados];
        c.ProveedorActual = f.ProveedorActual;
        c.TransferenciaInternacional = f.TransferenciaInternacional;
        c.MetricasCalidad = f.MetricasCalidad;
        c.EvalsRuta = f.EvalsRuta;
        c.PromptsUbicacion = f.PromptsUbicacion;
        c.Controles = f.Controles;
        c.AvisoUsuario = f.AvisoUsuario;
        c.PlanRollback = f.PlanRollback;
        c.Enlaces = [.. f.Enlaces];
    }

    /// <summary>Normaliza y valida la ficha. El dueño debe ser un empleado activo. Lo obligatorio se exige al enviar a revisión.</summary>
    private static async Task<FichaCaso?> LeerAsync(FichaCaso? f, PlataformaDbContext plataforma, Dictionary<string, string[]> errores, CancellationToken ct)
    {
        if (f is null)
        {
            errores["ficha"] = ["La ficha es obligatoria."];
            return null;
        }
        const int Corto = GiaDbContext.LargoCorto;
        const int Medio = GiaDbContext.LargoMedio;
        var leida = new FichaCaso(
            ApoyoGia.Texto(f.Nombre, "nombre", GiaDbContext.LargoNombre, errores),
            ApoyoGia.Texto(f.Aplicacion, "aplicacion", Corto, errores),
            f.AplicativoId,
            ApoyoGia.TextoOpcional(f.Repo, "repo", GiaDbContext.LargoNombre, errores),
            f.DuenoPersonaId,
            ApoyoGia.Valores(f.Ambientes, "ambientes", ValoresGia.Ambientes, errores),
            ApoyoGia.Texto(f.Proposito, "proposito", Medio, errores),
            ApoyoGia.Valor(f.Tipo, "tipo", ValoresGia.TiposCaso, errores),
            ApoyoGia.Valores(f.UsuariosAfectados, "usuariosAfectados", ValoresGia.UsuariosAfectados, errores),
            f.DecisionSobrePersonas,
            ApoyoGia.TextoOpcional(f.DecisionDescripcion, "decisionDescripcion", Medio, errores),
            ApoyoGia.Valor(f.HumanoEnBucle, "humanoEnBucle", ValoresGia.HumanoEnBucle, errores),
            ApoyoGia.Valores(f.CategoriasDato, "categoriasDato", ValoresGia.CategoriasDato, errores),
            ApoyoGia.Lista(f.TiposPii, "tiposPii", errores),
            f.VolumenMensualEstimado,
            ApoyoGia.TextoOpcional(f.BaseLegal, "baseLegal", Corto, errores),
            ApoyoGia.TextoOpcional(f.BaseLegalRef, "baseLegalRef", Corto, errores),
            ApoyoGia.TextoOpcional(f.Minimizacion, "minimizacion", Medio, errores),
            f.PresidioActivo,
            ApoyoGia.Lista(f.PresidioEntidades, "presidioEntidades", errores),
            ApoyoGia.Lista(f.AliasUsados, "aliasUsados", errores),
            ApoyoGia.TextoOpcional(f.ProveedorActual, "proveedorActual", Corto, errores),
            ApoyoGia.TextoOpcional(f.TransferenciaInternacional, "transferenciaInternacional", Medio, errores),
            ApoyoGia.TextoOpcional(f.MetricasCalidad, "metricasCalidad", Medio, errores),
            ApoyoGia.TextoOpcional(f.EvalsRuta, "evalsRuta", Corto, errores),
            ApoyoGia.TextoOpcional(f.PromptsUbicacion, "promptsUbicacion", Corto, errores),
            ApoyoGia.TextoOpcional(f.Controles, "controles", Medio, errores),
            ApoyoGia.TextoOpcional(f.AvisoUsuario, "avisoUsuario", Medio, errores),
            ApoyoGia.TextoOpcional(f.PlanRollback, "planRollback", Medio, errores),
            ApoyoGia.Lista(f.Enlaces, "enlaces", errores));
        if (leida.VolumenMensualEstimado is < 0)
        {
            errores["volumenMensualEstimado"] = ["No puede ser negativo."];
        }
        if (leida.Enlaces.Any(e => !ApoyoGia.UrlSegura(e)))
        {
            errores["enlaces"] = ["Solo direcciones https."];
        }
        if (leida.DuenoPersonaId is { } dueno && !await ApoyoGia.EmpleadoActivoAsync(plataforma, dueno, ct))
        {
            errores["duenoPersonaId"] = ["Debe ser un empleado activo."];
        }
        return leida;
    }
}
