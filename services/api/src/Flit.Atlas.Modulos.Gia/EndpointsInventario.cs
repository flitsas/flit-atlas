using System.ComponentModel;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Herramientas, asientos e incidentes (HU 8.5, plan 02 §6.6 y §12, ATLAS-ADR-0055 §8).</summary>
internal static class EndpointsInventario
{
    private const string RecursoHerramienta = "gia.herramienta";
    private const string RecursoAsiento = "gia.asiento";
    private const string RecursoIncidente = "gia.incidente";

    [Description("Una herramienta de IA del equipo.")]
    public sealed record HerramientaResumen(
        Guid Id, string Codigo, string Nombre, [property: Description("aprobada, en_evaluacion o prohibida.")] string Estado, string? Plan, string? Proveedor,
        [property: Description("publico, interno, confidencial, personal o sensible.")] string NivelDatoMaximo, string? ConfiguracionRequerida, decimal? CostoMensualUsd, string? Notas,
        [property: Description("Asientos sin retirar.")] int AsientosActivos);

    [Description("Crear o editar una herramienta.")]
    public sealed record SolicitudHerramienta(
        [property: Description("Minúsculas, números y guiones. Al editar se ignora.")] string? Codigo,
        string Nombre, string Estado, string? Plan, string? Proveedor, string NivelDatoMaximo, string? ConfiguracionRequerida, decimal? CostoMensualUsd, string? Notas);

    [Description("Un asiento.")]
    public sealed record AsientoResumen(
        Guid Id, Guid HerramientaId, Guid? PersonaId, string UsuarioNombre, string Correo, [property: Description("corporativa o personal_autorizada.")] string TipoCuenta,
        DateOnly AsignadoEn, DateOnly? RetiradoEn, DateTimeOffset? ConfiguracionVerificadaEn, IReadOnlyList<string> ReposAutorizados);

    [Description("Crear o editar un asiento.")]
    public sealed record SolicitudAsiento(
        [property: Description("Al editar se ignora.")] Guid? HerramientaId,
        Guid? PersonaId, string UsuarioNombre, string Correo, string TipoCuenta, DateOnly AsignadoEn, DateOnly? RetiradoEn, DateTimeOffset? ConfiguracionVerificadaEn, IReadOnlyList<string>? ReposAutorizados);

    [Description("Un incidente de IA.")]
    public sealed record IncidenteResumen(
        Guid Id, string Codigo, [property: Description("INC-IA-01 a INC-IA-07.")] string Tipo, [property: Description("S1 a S4.")] string Severidad, Guid? CasoUsoId, string Descripcion,
        DateTimeOffset DetectadoEn, DateTimeOffset? ContenidoEn, DateTimeOffset? CerradoEn, IReadOnlyList<string> Acciones,
        [property: Description("Si una persona ya reportó a la SIC: Atlas no reporta.")] bool ReportadoSic, PersonaGia Responsable, string? Lecciones);

    [Description("Crear o editar un incidente.")]
    public sealed record SolicitudIncidente(
        string Tipo, string Severidad, Guid? CasoUsoId, string Descripcion, DateTimeOffset DetectadoEn, DateTimeOffset? ContenidoEn, DateTimeOffset? CerradoEn,
        IReadOnlyList<string>? Acciones, bool ReportadoSic, Guid ResponsablePersonaId, string? Lecciones);

    [Description("Un tipo de incidente del plan 02 §6.6.")]
    public sealed record TipoIncidenteCatalogo(string Codigo, string Tipo, string Ejemplo, [property: Description("Pasos del playbook mínimo, si el plan lo trae.")] IReadOnlyList<string> Playbook);

    [Description("Una severidad del plan 02 §6.6.")]
    public sealed record SeveridadCatalogo(string Codigo, string Nombre, string Criterio, string TiempoRespuesta, string NotificaA);

    [Description("Tipos, severidades y playbooks. Atlas no notifica por sí solo.")]
    public sealed record CatalogoIncidentes(IReadOnlyList<TipoIncidenteCatalogo> Tipos, IReadOnlyList<SeveridadCatalogo> Severidades);

    private static readonly CatalogoIncidentes Catalogo = new(
    [
        new("INC-IA-01", "Fuga de datos hacia un proveedor no aprobado", "Alguien pegó cédulas en un chat personal", []),
        new("INC-IA-02", "Llave de proveedor o virtual expuesta", "Llave en un commit, log o captura",
            ["Revocar en el gateway (llave virtual) o en la consola del proveedor (llave de proveedor).", "Emitir una nueva y actualizar el SealedSecret.", "Revisar el gasto del periodo.", "Purgar el historial si estaba en git.", "Registrar en Atlas."]),
        new("INC-IA-03", "Salida dañina o incorrecta con impacto", "OCR que lee mal una placa y genera un trámite erróneo; Dr. FLIT da una instrucción falsa",
            ["Desactivar el caso o volver al prompt anterior.", "Notificar a los usuarios afectados.", "Agregar el caso a los evals."]),
        new("INC-IA-04", "Inyección de prompt o abuso", "Un usuario manipula a Dr. FLIT para extraer el prompt o los datos de otros", []),
        new("INC-IA-05", "Consumo anómalo o costo", "Un bucle que gasta USD 500 en una hora",
            ["El presupuesto del gateway bloquea automáticamente.", "Investigar el caso.", "Ajustar el límite de tasa."]),
        new("INC-IA-06", "Caída del proveedor", "Anthropic indisponible y sin failover", []),
        new("INC-IA-07", "Acción indebida de un agente", "Un asistente ejecuta un comando destructivo", []),
    ],
    [
        new("S1", "Crítica", "PII expuesta a terceros no autorizados o llave de proveedor comprometida", "1 h", "Comité, OPD; evaluar reporte a la SIC [por verificar el plazo vigente]"),
        new("S2", "Alta", "Error con impacto en trámites de ciudadanos o costo > 3× el presupuesto", "4 h", "Responsable de IA, dueño del caso, Seguridad"),
        new("S3", "Media", "Degradación o costo anómalo contenido", "1 día hábil", "Responsable de IA"),
        new("S4", "Baja", "Hallazgo sin impacto", "1 semana", "Registro"),
    ]);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/herramientas", HerramientasAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarHerramientasIa").WithSummary("Herramientas del equipo")
            .WithDescription("Con el número de asientos activos. Requiere atlas.gia.ver.")
            .Produces<List<HerramientaResumen>>();

        grupo.MapPost("/herramientas", CrearHerramientaAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearHerramientaIa").WithSummary("Registrar una herramienta")
            .WithDescription("409 ya_existe si el código está en uso. Requiere atlas.gia.gestionar.")
            .Produces<HerramientaResumen>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/herramientas/{id:guid}", GuardarHerramientaAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarHerramientaIa").WithSummary("Editar una herramienta")
            .WithDescription("404 si no existe. Requiere atlas.gia.gestionar.")
            .Produces<HerramientaResumen>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapGet("/asientos", AsientosAsync)
            .RequierePermiso(PermisosGia.Auditar)
            .WithName("ListarAsientosIa").WithSummary("Asientos")
            .WithDescription("Con nombre y correo de cada persona. Requiere atlas.gia.auditar.")
            .Produces<List<AsientoResumen>>();

        grupo.MapPost("/asientos", CrearAsientoAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearAsientoIa").WithSummary("Registrar un asiento")
            .WithDescription("Requiere atlas.gia.gestionar.")
            .Produces<AsientoResumen>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        grupo.MapPut("/asientos/{id:guid}", GuardarAsientoAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarAsientoIa").WithSummary("Editar un asiento")
            .WithDescription("Sirve para retirarlo o verificar su configuración. 404 si no existe. Requiere atlas.gia.gestionar.")
            .Produces<AsientoResumen>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapGet("/incidentes/catalogo", () => Results.Ok(Catalogo))
            .RequierePermiso(PermisosGia.Ver)
            .WithName("CatalogoIncidentesIa").WithSummary("Tipos, severidades y playbooks")
            .WithDescription("La lista fija del plan 02 §6.6. Requiere atlas.gia.ver.")
            .Produces<CatalogoIncidentes>();

        grupo.MapGet("/incidentes", IncidentesAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ListarIncidentesIa").WithSummary("Incidentes de IA")
            .WithDescription("Del más reciente al más antiguo, hasta 200. Requiere atlas.gia.ver.")
            .Produces<List<IncidenteResumen>>();

        grupo.MapPost("/incidentes", CrearIncidenteAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearIncidenteIa").WithSummary("Registrar un incidente")
            .WithDescription("El servidor asigna el código INC-IA-<año>-<NNN>. Atlas no envía avisos ni reporta a la SIC. Requiere atlas.gia.gestionar.")
            .Produces<IncidenteResumen>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/incidentes/{id:guid}", GuardarIncidenteAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarIncidenteIa").WithSummary("Actualizar un incidente")
            .WithDescription("Contenerlo, cerrarlo, registrar acciones y lecciones. 404 si no existe. Requiere atlas.gia.gestionar.")
            .Produces<IncidenteResumen>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    // --- Herramientas ---

    private static async Task<IResult> HerramientasAsync(GiaDbContext contexto, CancellationToken ct)
    {
        var herramientas = await contexto.Herramientas.AsNoTracking().OrderBy(h => h.Nombre).ToListAsync(ct);
        var activos = await contexto.Asientos.AsNoTracking().Where(a => a.RetiradoEn == null).GroupBy(a => a.HerramientaId)
            .Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return Results.Ok(herramientas.Select(h => Resumen(h, activos.GetValueOrDefault(h.Id))).ToList());
    }

    private static async Task<IResult> CrearHerramientaAsync(
        [Description("La herramienta.")] SolicitudHerramienta solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var codigo = (solicitud.Codigo ?? string.Empty).Trim();
        if (!EndpointsCasos.CodigoValido(codigo))
        {
            errores["codigo"] = [$"Minúsculas, números y guiones, hasta {GiaDbContext.LargoCodigo} caracteres."];
        }
        var nombre = ApoyoGia.Texto(solicitud.Nombre, "nombre", GiaDbContext.LargoNombre, errores);
        var estado = Obligatorio(solicitud.Estado, "estado", ValoresGia.EstadosHerramienta, errores);
        var nivel = Obligatorio(solicitud.NivelDatoMaximo, "nivelDatoMaximo", ValoresGia.CategoriasDato, errores);
        var plan = ApoyoGia.TextoOpcional(solicitud.Plan, "plan", GiaDbContext.LargoNombre, errores);
        var proveedor = ApoyoGia.TextoOpcional(solicitud.Proveedor, "proveedor", GiaDbContext.LargoNombre, errores);
        var configuracion = ApoyoGia.TextoOpcional(solicitud.ConfiguracionRequerida, "configuracionRequerida", GiaDbContext.LargoMedio, errores);
        var notas = ApoyoGia.TextoOpcional(solicitud.Notas, "notas", GiaDbContext.LargoMedio, errores);
        if (solicitud.CostoMensualUsd is < 0 or > 99_999_999)
        {
            errores["costoMensualUsd"] = ["Debe estar entre 0 y 99.999.999."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await contexto.Herramientas.AnyAsync(h => h.Codigo == codigo, ct))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya hay una herramienta con ese código.");
        }
        var herramienta = new Herramienta { Codigo = codigo, Nombre = nombre, Estado = estado, NivelDatoMaximo = nivel, Plan = plan, Proveedor = proveedor, ConfiguracionRequerida = configuracion, CostoMensualUsd = solicitud.CostoMensualUsd, Notas = notas };
        contexto.Herramientas.Add(herramienta);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.herramienta_creada", RecursoHerramienta, herramienta.Id, null, new { codigo, estado }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya hay una herramienta con ese código.");
        }
        return Results.Created($"/api/v1/gia/herramientas/{herramienta.Id}", Resumen(herramienta, 0));
    }

    private static async Task<IResult> GuardarHerramientaAsync(
        [Description("Id de la herramienta.")] Guid id,
        [Description("La herramienta.")] SolicitudHerramienta solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var herramienta = await contexto.Herramientas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (herramienta is null)
        {
            return ApoyoGia.NoEncontrado("La herramienta no existe.");
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var nombre = ApoyoGia.Texto(solicitud.Nombre, "nombre", GiaDbContext.LargoNombre, errores);
        var estado = Obligatorio(solicitud.Estado, "estado", ValoresGia.EstadosHerramienta, errores);
        var nivel = Obligatorio(solicitud.NivelDatoMaximo, "nivelDatoMaximo", ValoresGia.CategoriasDato, errores);
        var plan = ApoyoGia.TextoOpcional(solicitud.Plan, "plan", GiaDbContext.LargoNombre, errores);
        var proveedor = ApoyoGia.TextoOpcional(solicitud.Proveedor, "proveedor", GiaDbContext.LargoNombre, errores);
        var configuracion = ApoyoGia.TextoOpcional(solicitud.ConfiguracionRequerida, "configuracionRequerida", GiaDbContext.LargoMedio, errores);
        var notas = ApoyoGia.TextoOpcional(solicitud.Notas, "notas", GiaDbContext.LargoMedio, errores);
        if (solicitud.CostoMensualUsd is < 0 or > 99_999_999)
        {
            errores["costoMensualUsd"] = ["Debe estar entre 0 y 99.999.999."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        herramienta.Nombre = nombre;
        herramienta.Estado = estado;
        herramienta.NivelDatoMaximo = nivel;
        herramienta.Plan = plan;
        herramienta.Proveedor = proveedor;
        herramienta.ConfiguracionRequerida = configuracion;
        herramienta.CostoMensualUsd = solicitud.CostoMensualUsd;
        herramienta.Notas = notas;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.herramienta_guardada", RecursoHerramienta, herramienta.Id, null, new { herramienta.Codigo, estado }), ct);
        var activos = await contexto.Asientos.CountAsync(a => a.HerramientaId == id && a.RetiradoEn == null, ct);
        return Results.Ok(Resumen(herramienta, activos));
    }

    // --- Asientos ---

    private static async Task<IResult> AsientosAsync(
        [Description("Solo los de esta herramienta.")] Guid? herramienta, GiaDbContext contexto, CancellationToken ct)
    {
        var consulta = contexto.Asientos.AsNoTracking();
        if (herramienta is { } id)
        {
            consulta = consulta.Where(a => a.HerramientaId == id);
        }
        return Results.Ok((await consulta.OrderBy(a => a.UsuarioNombre).ToListAsync(ct)).Select(Resumen).ToList());
    }

    private static async Task<IResult> CrearAsientoAsync(
        [Description("El asiento.")] SolicitudAsiento solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var campos = LeerAsiento(solicitud, errores);
        if (solicitud.HerramientaId is not { } herramientaId || !await contexto.Herramientas.AnyAsync(h => h.Id == herramientaId, ct))
        {
            errores["herramientaId"] = ["La herramienta no existe."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var asiento = new Asiento
        {
            HerramientaId = solicitud.HerramientaId!.Value,
            PersonaId = solicitud.PersonaId,
            UsuarioNombre = campos.Nombre,
            Correo = campos.Correo,
            TipoCuenta = campos.TipoCuenta,
            AsignadoEn = solicitud.AsignadoEn,
            RetiradoEn = solicitud.RetiradoEn,
            ConfiguracionVerificadaEn = solicitud.ConfiguracionVerificadaEn?.ToUniversalTime(),
            ReposAutorizados = campos.Repos,
        };
        contexto.Asientos.Add(asiento);
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.asiento_creado", RecursoAsiento, asiento.Id, null, new { herramienta = asiento.HerramientaId, asiento.TipoCuenta }), ct);
        return Results.Created($"/api/v1/gia/asientos/{asiento.Id}", Resumen(asiento));
    }

    private static async Task<IResult> GuardarAsientoAsync(
        [Description("Id del asiento.")] Guid id,
        [Description("El asiento.")] SolicitudAsiento solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var asiento = await contexto.Asientos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asiento is null)
        {
            return ApoyoGia.NoEncontrado("El asiento no existe.");
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var campos = LeerAsiento(solicitud, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        asiento.PersonaId = solicitud.PersonaId;
        asiento.UsuarioNombre = campos.Nombre;
        asiento.Correo = campos.Correo;
        asiento.TipoCuenta = campos.TipoCuenta;
        asiento.AsignadoEn = solicitud.AsignadoEn;
        asiento.RetiradoEn = solicitud.RetiradoEn;
        asiento.ConfiguracionVerificadaEn = solicitud.ConfiguracionVerificadaEn?.ToUniversalTime();
        asiento.ReposAutorizados = campos.Repos;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.asiento_guardado", RecursoAsiento, asiento.Id, null, new { herramienta = asiento.HerramientaId, retirado = asiento.RetiradoEn is not null }), ct);
        return Results.Ok(Resumen(asiento));
    }

    // --- Incidentes ---

    private static async Task<IResult> IncidentesAsync(GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var incidentes = await contexto.Incidentes.AsNoTracking().OrderByDescending(i => i.DetectadoEn).Take(200).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, incidentes.Select(i => (Guid?)i.ResponsablePersonaId), ct);
        return Results.Ok(incidentes.Select(i => Resumen(i, nombres)).ToList());
    }

    private static async Task<IResult> CrearIncidenteAsync(
        [Description("El incidente.")] SolicitudIncidente solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var campos = await LeerIncidenteAsync(solicitud, contexto, plataforma, errores, ct);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var ahora = reloj.GetUtcNow();
        var existentes = await contexto.Incidentes.AsNoTracking().Select(i => i.Codigo).ToListAsync(ct);
        var incidente = new Incidente
        {
            Codigo = ReglasGia.CodigoIncidente(EndpointsCasos.Hoy(reloj).Year, existentes),
            Tipo = campos.Tipo,
            Severidad = campos.Severidad,
            Descripcion = campos.Descripcion,
            DetectadoEn = solicitud.DetectadoEn.ToUniversalTime(),
            ResponsablePersonaId = solicitud.ResponsablePersonaId,
        };
        Aplicar(incidente, solicitud, campos);
        contexto.Incidentes.Add(incidente);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.incidente_creado", RecursoIncidente, incidente.Id, null, new { incidente.Codigo, incidente.Tipo, incidente.Severidad }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_existe", "Otro incidente tomó ese código al mismo tiempo: vuelve a intentarlo.");
        }
        var nombres = await ApoyoGia.NombresAsync(plataforma, [incidente.ResponsablePersonaId], ct);
        return Results.Created($"/api/v1/gia/incidentes/{incidente.Id}", Resumen(incidente, nombres));
    }

    private static async Task<IResult> GuardarIncidenteAsync(
        [Description("Id del incidente.")] Guid id,
        [Description("El incidente.")] SolicitudIncidente solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var incidente = await contexto.Incidentes.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incidente is null)
        {
            return ApoyoGia.NoEncontrado("El incidente no existe.");
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var campos = await LeerIncidenteAsync(solicitud, contexto, plataforma, errores, ct);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        Aplicar(incidente, solicitud, campos);
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.incidente_guardado", RecursoIncidente, incidente.Id, null,
            new { incidente.Codigo, incidente.Severidad, cerrado = incidente.CerradoEn is not null, incidente.ReportadoSic }), ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, [incidente.ResponsablePersonaId], ct);
        return Results.Ok(Resumen(incidente, nombres));
    }

    // --- Piezas comunes ---

    private static string Obligatorio(string? valor, string campo, IReadOnlyList<string> validos, Dictionary<string, string[]> errores)
    {
        var limpio = ApoyoGia.Valor(valor, campo, validos, errores);
        if (limpio is null)
        {
            errores[campo] = [$"Valores válidos: {string.Join(", ", validos)}."];
        }
        return limpio ?? string.Empty;
    }

    private sealed record CamposAsiento(string Nombre, string Correo, string TipoCuenta, List<string> Repos);

    private static CamposAsiento LeerAsiento(SolicitudAsiento s, Dictionary<string, string[]> errores)
    {
        var nombre = ApoyoGia.Texto(s.UsuarioNombre, "usuarioNombre", GiaDbContext.LargoNombre, errores);
        var correo = ApoyoGia.Texto(s.Correo, "correo", GiaDbContext.LargoNombre, errores);
        if (!correo.Contains('@', StringComparison.Ordinal))
        {
            errores["correo"] = ["Debe ser un correo."];
        }
        if (s.RetiradoEn < s.AsignadoEn)
        {
            errores["retiradoEn"] = ["No puede ser anterior a la asignación."];
        }
        return new(nombre, correo, Obligatorio(s.TipoCuenta, "tipoCuenta", ValoresGia.TiposCuenta, errores), ApoyoGia.Lista(s.ReposAutorizados, "reposAutorizados", errores, 100, GiaDbContext.LargoNombre));
    }

    private sealed record CamposIncidente(string Tipo, string Severidad, string Descripcion, string? Lecciones, List<string> Acciones);

    private static async Task<CamposIncidente> LeerIncidenteAsync(SolicitudIncidente s, GiaDbContext contexto, PlataformaDbContext plataforma, Dictionary<string, string[]> errores, CancellationToken ct)
    {
        var campos = new CamposIncidente(
            Obligatorio(s.Tipo, "tipo", ValoresGia.TiposIncidente, errores),
            Obligatorio(s.Severidad, "severidad", ValoresGia.Severidades, errores),
            ApoyoGia.Texto(s.Descripcion, "descripcion", GiaDbContext.LargoLargo, errores),
            ApoyoGia.TextoOpcional(s.Lecciones, "lecciones", GiaDbContext.LargoLargo, errores),
            ApoyoGia.Lista(s.Acciones, "acciones", errores, 100, GiaDbContext.LargoMedio));
        if (s.ContenidoEn < s.DetectadoEn)
        {
            errores["contenidoEn"] = ["No puede ser anterior a la detección."];
        }
        if (s.CerradoEn < s.DetectadoEn)
        {
            errores["cerradoEn"] = ["No puede ser anterior a la detección."];
        }
        if (s.CasoUsoId is { } caso && !await contexto.Casos.AnyAsync(c => c.Id == caso, ct))
        {
            errores["casoUsoId"] = ["El caso no existe."];
        }
        if (!await ApoyoGia.EmpleadoActivoAsync(plataforma, s.ResponsablePersonaId, ct))
        {
            errores["responsablePersonaId"] = ["Debe ser un empleado activo."];
        }
        return campos;
    }

    private static void Aplicar(Incidente i, SolicitudIncidente s, CamposIncidente c)
    {
        i.Tipo = c.Tipo;
        i.Severidad = c.Severidad;
        i.CasoUsoId = s.CasoUsoId;
        i.Descripcion = c.Descripcion;
        i.DetectadoEn = s.DetectadoEn.ToUniversalTime();
        i.ContenidoEn = s.ContenidoEn?.ToUniversalTime();
        i.CerradoEn = s.CerradoEn?.ToUniversalTime();
        i.Acciones = c.Acciones;
        i.ReportadoSic = s.ReportadoSic;
        i.ResponsablePersonaId = s.ResponsablePersonaId;
        i.Lecciones = c.Lecciones;
    }

    private static HerramientaResumen Resumen(Herramienta h, int activos) =>
        new(h.Id, h.Codigo, h.Nombre, h.Estado, h.Plan, h.Proveedor, h.NivelDatoMaximo, h.ConfiguracionRequerida, h.CostoMensualUsd, h.Notas, activos);

    private static AsientoResumen Resumen(Asiento a) =>
        new(a.Id, a.HerramientaId, a.PersonaId, a.UsuarioNombre, a.Correo, a.TipoCuenta, a.AsignadoEn, a.RetiradoEn, a.ConfiguracionVerificadaEn, a.ReposAutorizados);

    private static IncidenteResumen Resumen(Incidente i, IReadOnlyDictionary<Guid, string> nombres) =>
        new(i.Id, i.Codigo, i.Tipo, i.Severidad, i.CasoUsoId, i.Descripcion, i.DetectadoEn, i.ContenidoEn, i.CerradoEn, i.Acciones, i.ReportadoSic, ApoyoGia.Ref(i.ResponsablePersonaId, nombres)!, i.Lecciones);
}
