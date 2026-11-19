using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// Todo lo que necesitan las vistas del reparto, leído una vez por petición: aplicativos del catálogo (por
/// <see cref="ILectorAplicativos"/>), responsabilidades, personas, usuarios de GitHub y la versión vigente.
/// </summary>
internal sealed record DatosReparto(
    IReadOnlyDictionary<Guid, AplicativoLeido> Aplicativos,
    IReadOnlyList<Responsabilidad> Responsabilidades,
    IReadOnlyDictionary<Guid, Persona> Personas,
    IReadOnlyDictionary<Guid, string> UsuariosGitHub,
    VersionReparto? Version,
    DateOnly Hoy,
    bool VeContacto)
{
    public bool Vigente(Responsabilidad r) => r.VigenteDesde <= Hoy && (r.VigenteHasta is null || r.VigenteHasta >= Hoy);

    public string NombrePersona(Guid id) => Personas.TryGetValue(id, out var p) ? p.Nombre : "Persona no disponible";

    /// <summary>Las responsabilidades por aplicativo, para heredar las del ancestro y para CODEOWNERS.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<ResponsableCodigo>> PorAplicativo() =>
        Responsabilidades.Where(Vigente).GroupBy(r => r.AplicativoId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResponsableCodigo>)
        [
            .. g.Select(r => new ResponsableCodigo(Textos.De(r.Tipo), r.PersonaId, NombrePersona(r.PersonaId), UsuariosGitHub.GetValueOrDefault(r.PersonaId))),
        ]);

    /// <summary>La foto del reparto vigente con los nombres de hoy; sin las huérfanas (su aplicativo ya no está).</summary>
    public FotoReparto Foto() => new(
    [
        .. Responsabilidades.Where(r => Aplicativos.ContainsKey(r.AplicativoId)).Select(r =>
        {
            var a = Aplicativos[r.AplicativoId];
            return new ResponsabilidadFoto(a.Id, a.Slug, a.Nombre, a.Tipo, Textos.De(r.Tipo), r.PersonaId, NombrePersona(r.PersonaId),
                r.Dedicacion, r.VigenteDesde, r.VigenteHasta, r.OrdenAplicativo, r.OrdenPersona, r.OrdenEnPersona);
        }),
    ]);

    public int Huerfanas => Responsabilidades.Count(r => !Aplicativos.ContainsKey(r.AplicativoId));
}

/// <summary>Piezas que comparten los endpoints de equipo: sesión, datos, DTO, errores y auditoría.</summary>
internal static class Comunes
{
    public static Guid PersonaDeSesion(ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    public static Guid? OrganizacionDeSesion(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var id) ? id : null;

    public static IResult Error(int estado, string codigo, string mensaje) =>
        Results.Json(new ErrorEquipo(codigo, mensaje), statusCode: estado);

    public static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    public static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    public static IResult Invalido(string campo, string mensaje) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensaje] });

    public static async Task<bool> VeContactoAsync(IPermisosDeSesion permisos, ClaimsPrincipal usuario, CancellationToken ct) =>
        (await permisos.ObtenerAsync(usuario, ct)).TieneEn(PermisosEquipo.VerContacto, AlcanceAsignacion.Global);

    public static async Task<bool> GestionaAsync(IPermisosDeSesion permisos, ClaimsPrincipal usuario, CancellationToken ct) =>
        (await permisos.ObtenerAsync(usuario, ct)).TieneEn(PermisosEquipo.Gestionar, AlcanceAsignacion.Global);

    public static async Task<DatosReparto> CargarAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, bool veContacto,
        CancellationToken ct, IEnumerable<Guid>? personasExtra = null)
    {
        var aplicativos = (await catalogo.ListarAsync(ct)).ToDictionary(a => a.Id);
        var responsabilidades = await equipo.Responsabilidades.AsNoTracking().ToListAsync(ct);
        var version = await equipo.Versiones.AsNoTracking().OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
        var ids = responsabilidades.Select(r => r.PersonaId).Concat(personasExtra ?? []).Distinct().ToList();
        var personas = ids.Count == 0
            ? new Dictionary<Guid, Persona>()
            : await plataforma.Personas.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var github = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await equipo.UsuariosGitHub.AsNoTracking().Where(u => ids.Contains(u.PersonaId)).ToDictionaryAsync(u => u.PersonaId, u => u.Usuario, ct);
        return new DatosReparto(aplicativos, responsabilidades, personas, github, version, CalendarioBogota.Hoy(reloj), veContacto);
    }

    /// <summary>La URL versionada de la foto, la misma que publica el directorio (ATLAS-ADR-0030).</summary>
    public static string? UrlFoto(Guid personaId, string? hash) =>
        hash is null ? null : $"/api/v1/plataforma/personas/{personaId}/foto?v={hash[..16]}";

    public static PersonaEquipoDto PersonaDto(DatosReparto datos, Guid id) =>
        datos.Personas.TryGetValue(id, out var p)
            ? new PersonaEquipoDto(p.Id, p.Nombre, p.Correo, p.Cargo, UrlFoto(p.Id, p.FotoHash), datos.VeContacto ? p.Telefono : null,
                datos.UsuariosGitHub.GetValueOrDefault(p.Id), p.Estado == EstadoPersona.Activa)
            : new PersonaEquipoDto(id, "Persona no disponible", null, null, null, null, null, false);

    public static AplicativoRefDto AplicativoDto(AplicativoLeido a) => new(a.Id, a.Slug, a.Nombre, a.Tipo);

    public static ResponsableDto ResponsableDto(DatosReparto datos, Responsabilidad r) =>
        new(r.Id, Textos.De(r.Tipo), PersonaDto(datos, r.PersonaId), r.Dedicacion, r.VigenteDesde, r.VigenteHasta, datos.Vigente(r), Textos.De(r.EstadoTraspaso));

    public static IReadOnlyList<DefinicionRolDto> Definiciones(VersionReparto? version) =>
        version is null ? [] : [.. FotoReparto.DefinicionesDesdeJson(version.Definiciones).Select(d => new DefinicionRolDto(d.Rol, d.Titulo, d.Puntos))];

    public static VersionResumenDto VersionDto(VersionReparto v, IReadOnlyDictionary<Guid, string> autores) =>
        new(v.Numero, Textos.De(v.Origen), v.RestauradaDe, v.FechaDocumento, v.DefinidoPor,
            v.AutorPersonaId is { } a ? autores.GetValueOrDefault(a) ?? "Persona no disponible" : null,
            v.Nota, v.CreadoEn, v.Huella, FotoReparto.DesdeJson(v.Contenido).Responsabilidades.Count);

    public static async Task<Dictionary<Guid, string>> AutoresAsync(PlataformaDbContext plataforma, IEnumerable<VersionReparto> versiones, CancellationToken ct)
    {
        var ids = versiones.Select(v => v.AutorPersonaId).OfType<Guid>().Distinct().ToList();
        return ids.Count == 0 ? [] : await plataforma.Personas.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
    }

    /// <summary>Evento de auditoría de una acción de equipo hecha por la persona de la sesión (solo ids y números).</summary>
    public static EventoAuditoria Evento(HttpContext http, TimeProvider reloj, string accion, string recursoTipo, string? recursoId, object? datos = null) => new()
    {
        OcurridoEn = reloj.GetUtcNow(),
        ActorTipo = TipoActor.Persona,
        ActorId = PersonaDeSesion(http.User).ToString(),
        OrganizacionId = OrganizacionDeSesion(http.User),
        Accion = $"{ModuloEquipo.IdModulo}.{accion}",
        RecursoTipo = recursoTipo,
        RecursoId = recursoId,
        Ip = http.Connection.RemoteIpAddress,
        // Nunca notas ni nombres: pueden traer datos personales.
        Datos = datos is null ? null : JsonSerializer.Serialize(datos),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>
    /// Guarda los cambios de equipo y los eventos de auditoría en <b>una sola transacción</b>: el contexto de la
    /// plataforma usa la conexión y la transacción del de equipo.
    /// </summary>
    /// <param name="antes">Pasos que van dentro de la misma transacción antes de guardar (p. ej. un guardado intermedio).</param>
    public static async Task GuardarAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, IEnumerable<EventoAuditoria> eventos, CancellationToken ct, Func<Task>? antes = null)
    {
        await using var transaccion = await equipo.Database.BeginTransactionAsync(ct);
        if (antes is not null)
        {
            await antes();
        }
        await equipo.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(equipo.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        plataforma.Auditoria.AddRange(eventos);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        await plataforma.Database.UseTransactionAsync(null, ct);
    }

    /// <summary>¿La excepción es la del número de versión repetido (dos cambios a la vez)?</summary>
    public static bool EsVersionRepetida(DbUpdateException error) =>
        error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
