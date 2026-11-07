using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Persona de Atlas.</summary>
[Description("Persona de Atlas.")]
public sealed record PersonaGia(Guid Id, [property: Description("Nombre; «Persona no disponible» si ya no está.")] string Nombre);

/// <summary>Por qué no se hizo.</summary>
[Description("Por qué no se hizo, con un código estable.")]
public sealed record ErrorGia(
    [property: Description("no_encontrado (404); sin_rol_gobierno (403); version_desactualizada, estado_invalido, ya_existe, ya_voto, ya_aceptada (409); ficha_incompleta, requisitos_pendientes, riesgo_critico, rol_no_aplica, sin_evaluacion, politica_pendiente (422).")] string Codigo,
    string Mensaje,
    [property: Description("Lo que falta, si aplica.")] IReadOnlyList<string>? Detalle = null);

/// <summary>Piezas comunes de los endpoints del gobierno de IA.</summary>
internal static class ApoyoGia
{
    public static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    public static IResult Error(int estado, string codigo, string mensaje, IReadOnlyList<string>? detalle = null) =>
        Results.Json(new ErrorGia(codigo, mensaje, detalle), statusCode: estado);

    public static IResult NoEncontrado(string mensaje) => Error(StatusCodes.Status404NotFound, "no_encontrado", mensaje);

    public static IResult Conflicto(string codigo, string mensaje) => Error(StatusCodes.Status409Conflict, codigo, mensaje);

    public static IResult Invalido(string codigo, string mensaje, IReadOnlyList<string>? detalle = null) =>
        Error(StatusCodes.Status422UnprocessableEntity, codigo, mensaje, detalle);

    public static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Texto obligatorio con tope: si no cumple, deja el error del campo.</summary>
    public static string Texto(string? valor, string campo, int largo, Dictionary<string, string[]> errores)
    {
        var limpio = (valor ?? string.Empty).Trim();
        if (limpio.Length == 0 || limpio.Length > largo)
        {
            errores[campo] = [$"Es obligatorio y va hasta {largo} caracteres."];
        }
        return limpio;
    }

    /// <summary>Texto opcional con tope.</summary>
    public static string? TextoOpcional(string? valor, string campo, int largo, Dictionary<string, string[]> errores)
    {
        var limpio = Vacio(valor);
        if (limpio is { Length: var n } && n > largo)
        {
            errores[campo] = [$"Va hasta {largo} caracteres."];
        }
        return limpio;
    }

    /// <summary>Valor opcional de una lista cerrada.</summary>
    public static string? Valor(string? valor, string campo, IReadOnlyList<string> validos, Dictionary<string, string[]> errores)
    {
        var limpio = Vacio(valor);
        if (limpio is not null && !validos.Contains(limpio))
        {
            errores[campo] = [$"Valores válidos: {string.Join(", ", validos)}."];
        }
        return limpio;
    }

    /// <summary>Valores de una lista cerrada, sin repetidos.</summary>
    public static List<string> Valores(IEnumerable<string>? valores, string campo, IReadOnlyList<string> validos, Dictionary<string, string[]> errores)
    {
        var lista = (valores ?? []).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (lista.Any(v => !validos.Contains(v)))
        {
            errores[campo] = [$"Valores válidos: {string.Join(", ", validos)}."];
        }
        return lista;
    }

    /// <summary>Lista libre con topes.</summary>
    public static List<string> Lista(IEnumerable<string>? valores, string campo, Dictionary<string, string[]> errores, int maximo = 50, int largo = GiaDbContext.LargoCorto)
    {
        var lista = (valores ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (lista.Count > maximo || lista.Any(v => v.Length > largo))
        {
            errores[campo] = [$"Hasta {maximo} valores de hasta {largo} caracteres."];
        }
        return lista;
    }

    /// <summary>Solo direcciones <c>https</c> absolutas: el enlace se pinta como enlace en la PWA.</summary>
    public static bool UrlSegura(string? url) =>
        url is null || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

    public static async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(PlataformaDbContext plataforma, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var lista = ids.OfType<Guid>().Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await plataforma.Personas.AsNoTracking().Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
    }

    public static PersonaGia? Ref(Guid? id, IReadOnlyDictionary<Guid, string> nombres) =>
        id is { } valor ? new PersonaGia(valor, nombres.GetValueOrDefault(valor) ?? "Persona no disponible") : null;

    public static async Task<bool> EmpleadoActivoAsync(PlataformaDbContext plataforma, Guid personaId, CancellationToken ct) =>
        await plataforma.Personas.AsNoTracking().AnyAsync(p => p.Id == personaId && p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa, ct);

    public static Task<bool> TieneRolAsync(GiaDbContext contexto, Guid personaId, string rol, CancellationToken ct) =>
        contexto.RolesGobierno.AsNoTracking().AnyAsync(r => r.PersonaId == personaId && r.Rol == rol, ct);

    /// <summary>Evento de auditoría: ids, estados y códigos; nunca textos de la ficha, de la política ni del incidente.</summary>
    public static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, string recurso, Guid id, string? version, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = recurso,
        RecursoId = id.ToString(),
        Version = version,
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>Los cambios y su auditoría en <b>una sola transacción</b>.</summary>
    public static async Task GuardarConAuditoriaAsync(GiaDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria evento, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        plataforma.Auditoria.Add(evento);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        await plataforma.Database.UseTransactionAsync(null, ct);
    }

    public static bool EsUnica(DbUpdateException ex) => ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
