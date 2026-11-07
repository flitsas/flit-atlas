using System.ComponentModel;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Políticas versionadas con aceptación (HU 8.3, ATLAS-ADR-0055 §7). Nada con la marca de pendiente llega a estar vigente.</summary>
internal static class EndpointsPoliticas
{
    private const string Recurso = "gia.politica";

    [Description("Una versión de una política, sin su texto.")]
    public sealed record PoliticaResumen(
        Guid Id, string Codigo, string Version, string Titulo,
        [property: Description("borrador, vigente o reemplazada.")] string Estado,
        bool RequiereAceptacion, DateOnly? VigenteDesde,
        [property: Description("Si su texto aún lleva la marca de pendiente de redacción.")] bool PendienteDeRedaccion,
        [property: Description("Si la persona de la sesión ya la aceptó.")] bool AceptadaPorMi);

    [Description("Una versión de una política con su texto en Markdown.")]
    public sealed record PoliticaDetalle(PoliticaResumen Politica, string Contenido, string? FuenteGit, PersonaGia? AprobadaPor);

    [Description("Crear una versión en borrador.")]
    public sealed record SolicitudPolitica(
        [property: Description("Código, por ejemplo POL-SGSI-IA-01. No cambia.")] string Codigo,
        [property: Description("Versión, por ejemplo 1.0. No cambia.")] string Version,
        string Titulo,
        [property: Description("Texto en Markdown, hasta 200 mil caracteres.")] string Contenido,
        [property: Description("Ruta y commit del texto en git, si lo hay.")] string? FuenteGit,
        bool RequiereAceptacion);

    [Description("Guardar un borrador.")]
    public sealed record SolicitudGuardarPolitica(string Titulo, string Contenido, string? FuenteGit, bool RequiereAceptacion);

    [Description("Quién aceptó una versión y cuántos faltan.")]
    public sealed record AceptacionesPolitica(
        IReadOnlyList<AceptacionResumen> Aceptaciones,
        [property: Description("Empleados activos sin aceptarla.")] IReadOnlyList<PersonaGia> Faltan);

    [Description("Una aceptación.")]
    public sealed record AceptacionResumen(PersonaGia Persona, DateTimeOffset AceptadaEn);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/politicas", ListarAsync)
            .RequierePermiso(PermisosGia.PoliticasVer)
            .WithName("ListarPoliticasIa").WithSummary("Políticas de IA")
            .WithDescription("Por código y versión, sin el texto. Con atlas.gia.ver, todas las versiones; solo con atlas.gia.politicas.ver, las vigentes.")
            .Produces<List<PoliticaResumen>>();

        grupo.MapPost("/politicas", CrearAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("CrearPoliticaIa").WithSummary("Crear una versión en borrador")
            .WithDescription("409 ya_existe si el código y la versión ya están. Requiere atlas.gia.gestionar.")
            .Produces<PoliticaDetalle>(StatusCodes.Status201Created)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/politicas/{id:guid}", DetalleAsync)
            .RequierePermiso(PermisosGia.PoliticasVer)
            .WithName("ObtenerPoliticaIa").WithSummary("Una versión con su texto")
            .WithDescription("404 si no existe o, sin atlas.gia.ver, si no está vigente. Requiere atlas.gia.politicas.ver.")
            .Produces<PoliticaDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);

        grupo.MapPut("/politicas/{id:guid}", GuardarAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("GuardarPoliticaIa").WithSummary("Guardar un borrador")
            .WithDescription("Solo en borrador (409 estado_invalido): lo vigente no se reescribe, se crea otra versión. Requiere atlas.gia.gestionar.")
            .Produces<PoliticaDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/politicas/{id:guid}/publicacion", PublicarAsync)
            .RequierePermiso(PermisosGia.Gestionar)
            .WithName("PublicarPoliticaIa").WithSummary("Publicar una versión")
            .WithDescription("Solo quien tenga el rol Comité (403 sin_rol_gobierno). La deja vigente y reemplaza la anterior. 422 politica_pendiente si su texto aún lleva la marca de pendiente de redacción. Requiere atlas.gia.gestionar.")
            .Produces<PoliticaDetalle>()
            .Produces<ErrorGia>(StatusCodes.Status403Forbidden)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict)
            .Produces<ErrorGia>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/politicas/{id:guid}/aceptacion", AceptarAsync)
            .RequierePermiso(PermisosGia.PoliticasVer)
            .WithName("AceptarPoliticaIa").WithSummary("Leí y acepto")
            .WithDescription("Registra persona, fecha e IP, una vez por versión. Solo sobre una versión vigente (409 estado_invalido); 409 ya_aceptada si ya la aceptó; sin atlas.gia.ver, lo que no está vigente responde 404. Requiere atlas.gia.politicas.ver.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorGia>(StatusCodes.Status404NotFound)
            .Produces<ErrorGia>(StatusCodes.Status409Conflict);

        grupo.MapGet("/politicas/{id:guid}/aceptaciones", AceptacionesAsync)
            .RequierePermiso(PermisosGia.Auditar)
            .WithName("ListarAceptacionesPoliticaIa").WithSummary("Quién aceptó y quién falta")
            .WithDescription("404 si no existe. Requiere atlas.gia.auditar.")
            .Produces<AceptacionesPolitica>()
            .Produces<ErrorGia>(StatusCodes.Status404NotFound);
    }

    /// <summary>Si la persona ve también los borradores y las versiones reemplazadas; si no, solo lo vigente.</summary>
    private static async Task<bool> VeTodasAsync(HttpContext http, IPermisosDeSesion permisosDeSesion, CancellationToken ct) =>
        (await permisosDeSesion.ObtenerAsync(http.User, ct)).TieneEn(PermisosGia.Ver, AlcanceAsignacion.Global);

    private static async Task<IResult> ListarAsync(HttpContext http, GiaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var persona = ApoyoGia.Persona(http.User);
        var aceptadas = await contexto.Aceptaciones.AsNoTracking().Where(a => a.PersonaId == persona).Select(a => a.PoliticaId).ToListAsync(ct);
        var consulta = contexto.Politicas.AsNoTracking();
        if (!await VeTodasAsync(http, permisosDeSesion, ct))
        {
            consulta = consulta.Where(p => p.Estado == ValoresGia.PoliticaVigente);
        }
        var politicas = await consulta.OrderBy(p => p.Codigo).ThenByDescending(p => p.Version).ToListAsync(ct);
        return Results.Ok(politicas.Select(p => Resumen(p, aceptadas.Contains(p.Id))).ToList());
    }

    private static async Task<IResult> CrearAsync(
        [Description("La versión.")] SolicitudPolitica solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var codigo = ApoyoGia.Texto(solicitud.Codigo, "codigo", GiaDbContext.LargoCodigo, errores);
        var version = ApoyoGia.Texto(solicitud.Version, "version", GiaDbContext.LargoEnum, errores);
        var titulo = ApoyoGia.Texto(solicitud.Titulo, "titulo", GiaDbContext.LargoNombre, errores);
        var contenido = Contenido(solicitud.Contenido, errores);
        var fuente = ApoyoGia.TextoOpcional(solicitud.FuenteGit, "fuenteGit", GiaDbContext.LargoCorto, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await contexto.Politicas.AnyAsync(p => p.Codigo == codigo && p.Version == version, ct))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya existe esa versión de la política.");
        }
        var politica = new Politica { Codigo = codigo, Version = version, Titulo = titulo, Contenido = contenido, FuenteGit = fuente, RequiereAceptacion = solicitud.RequiereAceptacion };
        contexto.Politicas.Add(politica);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.politica_creada", Recurso, politica.Id, version, new { codigo }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_existe", "Ya existe esa versión de la política.");
        }
        return Results.Created($"/api/v1/gia/politicas/{politica.Id}", await DetalleDeAsync(politica, false, plataforma, ct));
    }

    private static async Task<IResult> DetalleAsync(
        [Description("Id de la versión.")] Guid id, HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var politica = await contexto.Politicas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (politica is null || (politica.Estado != ValoresGia.PoliticaVigente && !await VeTodasAsync(http, permisosDeSesion, ct)))
        {
            return NoEncontrada();
        }
        var persona = ApoyoGia.Persona(http.User);
        var aceptada = await contexto.Aceptaciones.AnyAsync(a => a.PoliticaId == id && a.PersonaId == persona, ct);
        return Results.Ok(await DetalleDeAsync(politica, aceptada, plataforma, ct));
    }

    private static async Task<IResult> GuardarAsync(
        [Description("Id de la versión.")] Guid id,
        [Description("El borrador.")] SolicitudGuardarPolitica solicitud,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var politica = await contexto.Politicas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (politica is null)
        {
            return NoEncontrada();
        }
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var titulo = ApoyoGia.Texto(solicitud.Titulo, "titulo", GiaDbContext.LargoNombre, errores);
        var contenido = Contenido(solicitud.Contenido, errores);
        var fuente = ApoyoGia.TextoOpcional(solicitud.FuenteGit, "fuenteGit", GiaDbContext.LargoCorto, errores);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (politica.Estado != ValoresGia.PoliticaBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se edita un borrador: crea otra versión.");
        }
        politica.Titulo = titulo;
        politica.Contenido = contenido;
        politica.FuenteGit = fuente;
        politica.RequiereAceptacion = solicitud.RequiereAceptacion;
        await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, reloj.GetUtcNow(), "gia.politica_guardada", Recurso, politica.Id, politica.Version, new { politica.Codigo }), ct);
        return Results.Ok(await DetalleDeAsync(politica, false, plataforma, ct));
    }

    private static async Task<IResult> PublicarAsync(
        [Description("Id de la versión.")] Guid id,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var politica = await contexto.Politicas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (politica is null)
        {
            return NoEncontrada();
        }
        var persona = ApoyoGia.Persona(http.User);
        if (!await ApoyoGia.TieneRolAsync(contexto, persona, ValoresGia.RolComite, ct))
        {
            return ApoyoGia.Error(StatusCodes.Status403Forbidden, "sin_rol_gobierno", "Las políticas las aprueba el Comité de IA.");
        }
        if (politica.Estado != ValoresGia.PoliticaBorrador)
        {
            return ApoyoGia.Conflicto("estado_invalido", "Solo se publica un borrador.");
        }
        if (politica.Contenido.Contains(ReglasGia.MarcaPendiente, StringComparison.Ordinal))
        {
            return ApoyoGia.Invalido("politica_pendiente", "El texto aún lleva la marca de pendiente de redacción: redáctalo y quítala antes de publicar.");
        }
        var ahora = reloj.GetUtcNow();
        var anterior = await contexto.Politicas.FirstOrDefaultAsync(p => p.Codigo == politica.Codigo && p.Estado == ValoresGia.PoliticaVigente, ct);
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            // Primero se retira la anterior: el índice único solo admite una vigente por código.
            if (anterior is not null)
            {
                anterior.Estado = ValoresGia.PoliticaReemplazada;
                await contexto.SaveChangesAsync(ct);
            }
            politica.Estado = ValoresGia.PoliticaVigente;
            politica.VigenteDesde = EndpointsCasos.Hoy(reloj);
            politica.AprobadaPor = persona;
            await contexto.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        plataforma.Auditoria.Add(ApoyoGia.Evento(http, ahora, "gia.politica_publicada", Recurso, politica.Id, politica.Version, new { politica.Codigo, reemplaza = anterior?.Version }));
        await plataforma.SaveChangesAsync(ct);
        return Results.Ok(await DetalleDeAsync(politica, false, plataforma, ct));
    }

    private static async Task<IResult> AceptarAsync(
        [Description("Id de la versión.")] Guid id,
        HttpContext http, GiaDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, TimeProvider reloj, CancellationToken ct)
    {
        var politica = await contexto.Politicas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (politica is null)
        {
            return NoEncontrada();
        }
        if (politica.Estado != ValoresGia.PoliticaVigente)
        {
            if (!await VeTodasAsync(http, permisosDeSesion, ct))
            {
                return NoEncontrada();
            }
            return ApoyoGia.Conflicto("estado_invalido", "Solo se acepta una política vigente.");
        }
        var persona = ApoyoGia.Persona(http.User);
        var ahora = reloj.GetUtcNow();
        var aceptacion = new AceptacionPolitica { PoliticaId = id, PersonaId = persona, AceptadaEn = ahora, Ip = http.Connection.RemoteIpAddress };
        contexto.Aceptaciones.Add(aceptacion);
        try
        {
            await ApoyoGia.GuardarConAuditoriaAsync(contexto, plataforma, ApoyoGia.Evento(http, ahora, "gia.politica_aceptada", Recurso, id, politica.Version, new { politica.Codigo }), ct);
        }
        catch (DbUpdateException ex) when (ApoyoGia.EsUnica(ex))
        {
            return ApoyoGia.Conflicto("ya_aceptada", "Ya aceptaste esta versión.");
        }
        return Results.NoContent();
    }

    private static async Task<IResult> AceptacionesAsync(
        [Description("Id de la versión.")] Guid id, GiaDbContext contexto, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (!await contexto.Politicas.AnyAsync(p => p.Id == id, ct))
        {
            return NoEncontrada();
        }
        var aceptaciones = await contexto.Aceptaciones.AsNoTracking().Where(a => a.PoliticaId == id).OrderBy(a => a.AceptadaEn).ToListAsync(ct);
        var quienes = aceptaciones.Select(a => a.PersonaId).ToList();
        var faltan = await plataforma.Personas.AsNoTracking()
            .Where(p => p.Tipo == Plataforma.Entidades.TipoPersona.Empleado && p.Estado == Plataforma.Entidades.EstadoPersona.Activa && !quienes.Contains(p.Id))
            .OrderBy(p => p.Nombre).Select(p => new PersonaGia(p.Id, p.Nombre)).ToListAsync(ct);
        var nombres = await ApoyoGia.NombresAsync(plataforma, quienes.Select(q => (Guid?)q), ct);
        return Results.Ok(new AceptacionesPolitica([.. aceptaciones.Select(a => new AceptacionResumen(ApoyoGia.Ref(a.PersonaId, nombres)!, a.AceptadaEn))], faltan));
    }

    private static IResult NoEncontrada() => ApoyoGia.NoEncontrado("La política no existe.");

    private static string Contenido(string? valor, Dictionary<string, string[]> errores)
    {
        var limpio = (valor ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(limpio) || limpio.Length > GiaDbContext.LargoContenido)
        {
            errores["contenido"] = [$"Es obligatorio y va hasta {GiaDbContext.LargoContenido / 1000} mil caracteres."];
        }
        return limpio;
    }

    private static PoliticaResumen Resumen(Politica p, bool aceptada) => new(
        p.Id, p.Codigo, p.Version, p.Titulo, p.Estado, p.RequiereAceptacion, p.VigenteDesde, p.Contenido.Contains(ReglasGia.MarcaPendiente, StringComparison.Ordinal), aceptada);

    private static async Task<PoliticaDetalle> DetalleDeAsync(Politica p, bool aceptada, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var nombres = await ApoyoGia.NombresAsync(plataforma, [p.AprobadaPor], ct);
        return new PoliticaDetalle(Resumen(p, aceptada), p.Contenido, p.FuenteGit, ApoyoGia.Ref(p.AprobadaPor, nombres));
    }
}
