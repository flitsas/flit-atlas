using System.ComponentModel;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Autorizacion;

/// <summary>
/// Buscador de personas de la pantalla «Asignación de roles» (<c>GET /api/v1/plataforma/personas</c>): por nombre o
/// correo, sin distinguir mayúsculas ni tildes (<see cref="FuncionesSql"/>), o por id exacto. Exige
/// <c>atlas.roles.ver</c>, el mismo permiso de la pantalla y de las asignaciones que abre: <c>atlas.equipo.ver</c> lo tiene
/// todo empleado y le mostraría los correos de los externos, y los externos nunca ven personas (§7.3).
/// <para>
/// El aislamiento por organización no depende de este código: el predicado obligatorio y RLS de
/// <c>plataforma_persona</c> y <c>plataforma_organizacion</c> (ATLAS-ADR-0023) ya limitan a un externo a su organización.
/// </para>
/// </summary>
public static class BusquedaPersonas
{
    public const string Ruta = "/personas";

    /// <summary>Largo mínimo de la búsqueda (sin espacios a los lados): con una letra saldría media base.</summary>
    public const int LargoMinimo = 2;

    public const int LargoMaximo = 100;

    public const int LimitePorDefecto = 20;

    /// <summary>Tope de resultados por consulta: no hay paginación; si hay más, se pide afinar la búsqueda.</summary>
    public const int LimiteMaximo = 50;

    /// <summary>Palabras de la búsqueda que se tienen en cuenta (cada una debe estar en el nombre o en el correo).</summary>
    public const int TerminosMaximos = 5;

    [Description("Organización de la persona.")]
    public sealed record OrganizacionResumen(
        [property: Description("Id de la organización.")] Guid Id,
        [property: Description("Nombre de la organización.")] string Nombre);

    [Description("Persona encontrada: lo mínimo para elegirla y abrir sus asignaciones.")]
    public sealed record PersonaEncontrada(
        [property: Description("Id de la persona (plataforma_persona).")] Guid Id,
        [property: Description("Nombre de la persona.")] string Nombre,
        [property: Description("Correo de la persona, en minúsculas.")] string Correo,
        [property: Description("empleado o externo.")] string Tipo,
        [property: Description("Organización de la persona.")] OrganizacionResumen Organizacion,
        [property: Description("activa, inactiva o suspendida.")] string Estado,
        [property: Description("Si es la cuenta de administrador de emergencia (ATLAS-ADR-0027).")] bool Emergencia,
        [property: Description("URL relativa de la foto (empleados sincronizados con Graph, ATLAS-ADR-0030); vacía si no tiene.")] string? Foto,
        [property: Description("Códigos de los roles asignados y vigentes, ordenados. No incluye atlas.lector (lo tiene todo empleado) ni los implícitos.")] IReadOnlyList<string> Roles);

    [Description("Resultado de la búsqueda de personas, ordenado por nombre.")]
    public sealed record ResultadoBusquedaPersonas(
        [property: Description("Personas encontradas, hasta el límite pedido.")] IReadOnlyList<PersonaEncontrada> Personas,
        [property: Description("Si hay más personas que el límite: la búsqueda se debe afinar.")] bool HayMas);

    internal static void MapBusquedaPersonas(this RouteGroupBuilder plataforma)
    {
        plataforma.MapGet(Ruta, BuscarAsync)
            .RequierePermiso(PermisosPlataforma.RolesVer)
            .WithName("BuscarPersonas")
            .WithSummary("Buscar personas")
            .WithDescription(
                $"Personas de Atlas por nombre o correo, sin distinguir mayúsculas ni tildes (cada palabra debe estar en el " +
                $"nombre o en el correo), o por su id exacto. Mínimo {LargoMinimo} caracteres; hasta {LimiteMaximo} resultados " +
                "ordenados por nombre, sin paginación (hayMas pide afinar la búsqueda). Devuelve lo mínimo para abrir las " +
                "asignaciones de la persona, con sus roles asignados vigentes. Un externo solo vería personas de su " +
                "organización (predicado obligatorio y RLS). Requiere atlas.roles.ver.")
            .Produces<ResultadoBusquedaPersonas>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> BuscarAsync(
        [Description("Texto a buscar en el nombre o el correo (mínimo 2 caracteres, máximo 100), o el id de la persona.")] string? q,
        [Description("empleado o externo; vacío, ambos.")] string? tipo,
        [Description("Id de la organización de las personas; vacío, todas las visibles.")] Guid? organizacion,
        [Description("Máximo de resultados, de 1 a 50 (por defecto 20).")] int? limite,
        PlataformaDbContext db, TimeProvider reloj, CancellationToken ct)
    {
        var texto = q?.Trim() ?? string.Empty;
        var errores = new Dictionary<string, string[]>();
        if (texto.Length < LargoMinimo)
        {
            errores["q"] = [$"Escribe al menos {LargoMinimo} caracteres."];
        }
        else if (texto.Length > LargoMaximo)
        {
            errores["q"] = [$"La búsqueda tiene máximo {LargoMaximo} caracteres."];
        }
        TipoPersona? tipoPersona = null;
        switch (tipo?.Trim().ToLowerInvariant())
        {
            case null or "":
                break;
            case "empleado":
                tipoPersona = TipoPersona.Empleado;
                break;
            case "externo":
                tipoPersona = TipoPersona.Externo;
                break;
            default:
                errores["tipo"] = ["El tipo es empleado o externo."];
                break;
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
        return Results.Ok(await ConsultarAsync(db, texto, tipoPersona, organizacion, tope, reloj.GetUtcNow(), ct));
    }

    /// <summary>
    /// La consulta, ya validados los parámetros. Lee con el alcance de datos del contexto: lo que el predicado de
    /// organización oculta no aparece (lo prueba <c>BusquedaPersonasTests</c> con el alcance de un externo).
    /// </summary>
    internal static async Task<ResultadoBusquedaPersonas> ConsultarAsync(
        PlataformaDbContext db, string texto, TipoPersona? tipoPersona, Guid? organizacion, int tope, DateTimeOffset ahora,
        CancellationToken ct)
    {
        var personas = db.Personas.AsNoTracking();
        if (Guid.TryParse(texto, out var id))
        {
            personas = personas.Where(p => p.Id == id);
        }
        else
        {
            foreach (var termino in FuncionesSql.NormalizarBusqueda(texto).Split(' ').Distinct(StringComparer.Ordinal).Take(TerminosMaximos))
            {
                var patron = FuncionesSql.PatronContiene(termino);
                personas = personas.Where(p =>
                    EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(p.Nombre, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                    || EF.Functions.Like(p.Correo, patron, FuncionesSql.EscapeLike));
            }
        }
        if (tipoPersona is { } t)
        {
            personas = personas.Where(p => p.Tipo == t);
        }
        if (organizacion is { } o)
        {
            personas = personas.Where(p => p.OrganizacionId == o);
        }

        var filas = await personas
            .Join(db.Organizaciones, p => p.OrganizacionId, org => org.Id, (p, org) => new
            {
                p.Id,
                p.Nombre,
                p.Correo,
                p.Tipo,
                p.Estado,
                p.FotoHash,
                OrganizacionId = org.Id,
                OrganizacionNombre = org.Nombre,
                Emergencia = db.AsignacionesRol.Any(a => a.PersonaId == p.Id && a.Origen == OrigenAsignacion.Emergencia),
            })
            .OrderBy(x => x.Nombre).ThenBy(x => x.Correo)
            .Take(tope + 1)
            .ToListAsync(ct);
        var hayMas = filas.Count > tope;
        if (hayMas)
        {
            filas.RemoveAt(tope);
        }

        // Una consulta más para los roles vigentes de las (a lo sumo 50) personas encontradas.
        var ids = filas.Select(f => f.Id).ToList();
        var roles = (await db.AsignacionesRol.AsNoTracking()
                .Where(a => ids.Contains(a.PersonaId) && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
                .Join(db.Roles, a => a.RolId, r => r.Id, (a, r) => new { a.PersonaId, r.Codigo })
                .ToListAsync(ct))
            .ToLookup(x => x.PersonaId, x => x.Codigo);

        return new ResultadoBusquedaPersonas(
            [
                .. filas.Select(f => new PersonaEncontrada(
                    f.Id, f.Nombre, f.Correo,
                    f.Tipo == TipoPersona.Externo ? "externo" : "empleado",
                    new OrganizacionResumen(f.OrganizacionId, f.OrganizacionNombre),
                    f.Estado switch
                    {
                        EstadoPersona.Suspendida => "suspendida",
                        EstadoPersona.Inactiva => "inactiva",
                        _ => "activa",
                    },
                    f.Emergencia,
                    Personas.EndpointsDirectorio.UrlFoto(f.Id, f.FotoHash),
                    [.. roles[f.Id].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)])),
            ],
            hayMas);
    }
}
