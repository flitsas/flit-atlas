using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Flit.Atlas.Identidad.Personas;

/// <summary>
/// Directorio interno de empleados, su foto y la sincronización con Graph (HU 2.2, ATLAS-ADR-0030), bajo
/// <c>/api/v1/plataforma</c>. Ver el directorio y las fotos exige <c>atlas.equipo.ver</c> (todo empleado; los externos
/// nunca ven personas, §7.3); el teléfono solo sale con <c>atlas.equipo.ver_contacto</c>. La sincronización la ve y la
/// pide quien tiene <c>atlas.identidad.administrar</c> (superadmin).
/// </summary>
public static class EndpointsDirectorio
{
    public const string Etiqueta = "Directorio";

    public const string PermisoVer = "atlas.equipo.ver";
    public const string PermisoContacto = "atlas.equipo.ver_contacto";

    public const int LimitePorDefecto = 100;
    public const int LimiteMaximo = 200;

    [Description("Empleado del directorio interno. El teléfono solo viene con atlas.equipo.ver_contacto.")]
    public sealed record PersonaDirectorio(
        [property: Description("Id de la persona (plataforma_persona).")] Guid Id,
        [property: Description("Nombre, como está en Entra ID.")] string Nombre,
        [property: Description("Correo corporativo, en minúsculas.")] string Correo,
        [property: Description("Cargo (jobTitle de Entra).")] string? Cargo,
        [property: Description("Área (department de Entra).")] string? Area,
        [property: Description("activa, inactiva o suspendida.")] string Estado,
        [property: Description("URL relativa de la foto, versionada por su hash; vacía si no tiene.")] string? Foto,
        [property: Description("Teléfono; vacío si no tiene o si quien consulta no tiene atlas.equipo.ver_contacto.")] string? Telefono,
        [property: Description("Última lectura de la persona en Graph; vacía si nunca se sincronizó.")] DateTimeOffset? SincronizadaEn);

    [Description("Página del directorio, ordenada por nombre.")]
    public sealed record ResultadoDirectorio(
        [property: Description("Personas encontradas, hasta el límite pedido.")] IReadOnlyList<PersonaDirectorio> Personas,
        [property: Description("Hay más personas que el límite: afina la búsqueda.")] bool HayMas,
        [property: Description("Si quien consulta ve los teléfonos (atlas.equipo.ver_contacto).")] bool VeContacto);

    [Description("Estado de la sincronización diaria de personas con Graph.")]
    public sealed record VistaSincronizacion(
        [property: Description("Hay Entra ID configurado en este ambiente; sin él no se puede sincronizar.")] bool Disponible,
        [property: Description("El trabajo diario está habilitado (Identidad:Personas:Habilitada).")] bool Programada,
        [property: Description("enlazadas, grupo o tenant (Identidad:Personas:Origen).")] string Origen,
        [property: Description("Hora del día de la corrida, en la de Bogotá (HH:mm).")] string Hora,
        [property: Description("Próxima corrida programada; vacía si no hay.")] DateTimeOffset? Proxima,
        [property: Description("Hay una corrida en curso o pedida en esta réplica.")] bool EnCurso,
        [property: Description("Última corrida (en cualquier réplica); vacía si nunca corrió.")] EstadoSincronizacion? Ultima);

    [Description("Error de la sincronización.")]
    public sealed record ErrorSincronizacion(
        [property: Description("sincronizacion_no_disponible o sincronizacion_en_curso.")] string Error,
        string Mensaje);

    /// <summary>URL de la foto de una persona, versionada por su hash (cambia cuando cambia la foto).</summary>
    public static string? UrlFoto(Guid personaId, string? hash) =>
        hash is null ? null : $"/api/v1/plataforma/personas/{personaId}/foto?v={hash[..16]}";

    internal static void MapDirectorio(this IEndpointRouteBuilder rutas)
    {
        var plataforma = rutas.MapGroup("/api/v1/plataforma").WithTags(Etiqueta);

        plataforma.MapGet("/directorio", ListarAsync)
            .RequierePermiso(PermisoVer)
            .WithName("ListarDirectorio")
            .WithSummary("Directorio interno")
            .WithDescription(
                "Empleados de FLIT con nombre, correo, cargo, área, estado y foto, sincronizados a diario con Microsoft Graph " +
                $"(ATLAS-ADR-0030), ordenados por nombre (hasta {LimiteMaximo}, sin paginación). Por defecto solo los activos. " +
                "La búsqueda no distingue mayúsculas ni tildes y mira nombre, correo, cargo y área. El teléfono solo viene con " +
                "atlas.equipo.ver_contacto. Nunca lista externos ni la cuenta de emergencia. Requiere atlas.equipo.ver.")
            .Produces<ResultadoDirectorio>()
            .ProducesValidationProblem();

        plataforma.MapGet("/directorio/{personaId:guid}", ObtenerAsync)
            .RequierePermiso(PermisoVer)
            .WithName("ObtenerPersonaDelDirectorio")
            .WithSummary("Persona del directorio")
            .WithDescription(
                "Un empleado del directorio (también inactivo). 404 si no existe o no es un empleado. El teléfono solo viene " +
                "con atlas.equipo.ver_contacto. Requiere atlas.equipo.ver.")
            .Produces<PersonaDirectorio>()
            .Produces(StatusCodes.Status404NotFound);

        plataforma.MapGet("/personas/{personaId:guid}/foto", FotoAsync)
            .RequierePermiso(PermisoVer)
            .WithName("ObtenerFotoDePersona")
            .WithSummary("Foto de una persona")
            .WithDescription(
                "Foto de perfil de un empleado, leída de Graph (JPEG, PNG, GIF o WebP, con tope de tamaño). Responde con ETag " +
                "(el hash de la foto) y caché privada: con If-None-Match igual, 304. 404 si no tiene foto. Requiere atlas.equipo.ver.")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/gif", "image/webp")
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status404NotFound);

        plataforma.MapGet("/directorio/sincronizacion", EstadoAsync)
            .RequierePermiso(PermisosIdentidad.Administrar)
            .WithName("ObtenerSincronizacionDePersonas")
            .WithSummary("Estado de la sincronización de personas")
            .WithDescription(
                "Configuración (origen, hora, si está programada), próxima corrida y resultado de la última con sus conteos " +
                "(creadas, actualizadas, deshabilitadas, reactivadas, omitidas, retenidas por el freno). Sin datos personales. " +
                "Requiere atlas.identidad.administrar.")
            .Produces<VistaSincronizacion>();

        plataforma.MapPost("/directorio/sincronizacion", Solicitar)
            .RequierePermiso(PermisosIdentidad.Administrar)
            .WithName("SincronizarPersonasAhora")
            .WithSummary("Sincronizar ahora")
            .WithDescription(
                "Pide una corrida de la sincronización con Graph sin esperar a la hora programada; corre en segundo plano " +
                "(202) y su resultado aparece en GET /api/v1/plataforma/directorio/sincronizacion y en la auditoría " +
                "(identidad.personas_sincronizadas). 409 sincronizacion_no_disponible sin Entra ID configurado; 409 " +
                "sincronizacion_en_curso si ya hay una pedida o en curso. Requiere atlas.identidad.administrar.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorSincronizacion>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListarAsync(
        [Description("Texto a buscar en nombre, correo, cargo o área (mínimo 2 caracteres, máximo 100); vacío, todos.")] string? q,
        [Description("true para incluir las personas inactivas y suspendidas.")] bool? inactivas,
        [Description("Máximo de resultados, de 1 a 200 (por defecto 100).")] int? limite,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, PlataformaDbContext db, CancellationToken ct)
    {
        var texto = q?.Trim() ?? string.Empty;
        var errores = new Dictionary<string, string[]>();
        if (texto.Length is > 0 and < BusquedaPersonas.LargoMinimo)
        {
            errores["q"] = [$"Escribe al menos {BusquedaPersonas.LargoMinimo} caracteres."];
        }
        else if (texto.Length > BusquedaPersonas.LargoMaximo)
        {
            errores["q"] = [$"La búsqueda tiene máximo {BusquedaPersonas.LargoMaximo} caracteres."];
        }
        var tope = limite ?? LimitePorDefecto;
        if (tope is < 1 or > LimiteMaximo)
        {
            errores["limite"] = [$"El límite va de 1 a {LimiteMaximo}."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var personas = Empleados(db);
        if (inactivas != true)
        {
            personas = personas.Where(p => p.Estado == EstadoPersona.Activa);
        }
        foreach (var termino in FuncionesSql.NormalizarBusqueda(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Distinct(StringComparer.Ordinal).Take(BusquedaPersonas.TerminosMaximos))
        {
            var patron = FuncionesSql.PatronContiene(termino);
            personas = personas.Where(p =>
                EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(p.Nombre, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(p.Correo, patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(p.Cargo ?? string.Empty, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(p.Area ?? string.Empty, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike));
        }
        var filas = await personas.OrderBy(p => p.Nombre).ThenBy(p => p.Correo).Take(tope + 1).ToListAsync(ct);
        var hayMas = filas.Count > tope;
        if (hayMas)
        {
            filas.RemoveAt(tope);
        }
        var veContacto = (await permisos.ObtenerAsync(usuario, ct)).Tiene(PermisoContacto);
        return Results.Ok(new ResultadoDirectorio([.. filas.Select(p => Vista(p, veContacto))], hayMas, veContacto));
    }

    private static async Task<IResult> ObtenerAsync(
        [Description("Persona de Atlas (plataforma_persona).")] Guid personaId,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, PlataformaDbContext db, CancellationToken ct)
    {
        var persona = await Empleados(db).FirstOrDefaultAsync(p => p.Id == personaId, ct);
        if (persona is null)
        {
            return Results.NotFound();
        }
        var veContacto = (await permisos.ObtenerAsync(usuario, ct)).Tiene(PermisoContacto);
        return Results.Ok(Vista(persona, veContacto));
    }

    private static async Task<IResult> FotoAsync(
        [Description("Persona de Atlas (plataforma_persona).")] Guid personaId,
        [Description("Versión de la foto (los primeros caracteres de su hash); solo sirve para que la URL cambie con la foto.")] string? v,
        HttpContext http, PlataformaDbContext db, CancellationToken ct)
    {
        // Solo empleados: el predicado de organización ya oculta lo ajeno a un externo, que además no tiene atlas.equipo.ver.
        var foto = await db.FotosPersonas.AsNoTracking()
            .Where(f => f.PersonaId == personaId && db.Personas.Any(p => p.Id == f.PersonaId && p.Tipo == TipoPersona.Empleado))
            .FirstOrDefaultAsync(ct);
        if (foto is null)
        {
            return Results.NotFound();
        }
        // Caché privada (nunca en proxies compartidos): con la URL versionada por el hash, el navegador no vuelve a pedirla
        // en una semana; si la pide, el ETag le ahorra los bytes.
        http.Response.Headers.CacheControl = "private, max-age=604800";
        return Results.File(foto.Contenido, foto.TipoContenido, entityTag: new EntityTagHeaderValue($"\"{foto.Hash}\""));
    }

    private static async Task<IResult> EstadoAsync(
        IDirectorioEntra directorio, TrabajoSincronizacionPersonas trabajo, IOptions<OpcionesSincronizacionPersonas> opciones,
        PlataformaDbContext db, TimeProvider reloj, CancellationToken ct)
    {
        var config = opciones.Value;
        return Results.Ok(new VistaSincronizacion(
            directorio.Disponible,
            directorio.Disponible && config.Habilitada,
            SincronizadorPersonas.Texto(config.Origen),
            config.Hora.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture),
            directorio.Disponible && config.Habilitada ? TrabajoSincronizacionPersonas.ProximaCorrida(reloj.GetUtcNow(), config.Hora) : null,
            trabajo.EnCurso,
            await SincronizadorPersonas.LeerEstadoAsync(db, ct)));
    }

    private static IResult Solicitar(ClaimsPrincipal usuario, IDirectorioEntra directorio, TrabajoSincronizacionPersonas trabajo)
    {
        if (!directorio.Disponible)
        {
            return Results.Conflict(new ErrorSincronizacion("sincronizacion_no_disponible",
                "Este ambiente no tiene Entra ID configurado: no hay directorio que sincronizar."));
        }
        var actor = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var id) ? id : (Guid?)null;
        return trabajo.SolicitarAhora(actor)
            ? Results.Accepted("/api/v1/plataforma/directorio/sincronizacion")
            : Results.Conflict(new ErrorSincronizacion("sincronizacion_en_curso", "Ya hay una sincronización pedida o en curso."));
    }

    /// <summary>Empleados del directorio: sin externos ni la cuenta de emergencia (ATLAS-ADR-0027).</summary>
    private static IQueryable<Persona> Empleados(PlataformaDbContext db) =>
        db.Personas.AsNoTracking().Where(p => p.Tipo == TipoPersona.Empleado
            && !db.AsignacionesRol.Any(a => a.PersonaId == p.Id && a.Origen == OrigenAsignacion.Emergencia));

    private static PersonaDirectorio Vista(Persona p, bool veContacto) => new(
        p.Id, p.Nombre, p.Correo, p.Cargo, p.Area,
        p.Estado switch
        {
            EstadoPersona.Suspendida => "suspendida",
            EstadoPersona.Inactiva => "inactiva",
            _ => "activa",
        },
        UrlFoto(p.Id, p.FotoHash),
        veContacto ? p.Telefono : null,
        p.SincronizadaEn);
}
