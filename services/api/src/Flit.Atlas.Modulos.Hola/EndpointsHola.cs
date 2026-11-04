using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Flit.Atlas.Modulos.Hola.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Hola;

/// <summary>
/// Endpoints de <c>/api/v1/hola</c>. El host ya puso la etiqueta, la sesión y la bandera; aquí cada endpoint declara su
/// permiso. El predicado obligatorio del contexto deja ver solo los saludos de las organizaciones del alcance.
/// </summary>
internal static class EndpointsHola
{
    public const int LimiteListado = 50;

    [Description("Saludo de una organización (módulo de ejemplo).")]
    public sealed record SaludoDto(
        Guid Id,
        [property: Description("Texto del saludo."), MaxLength(HolaDbContext.LargoTexto)] string Texto,
        [property: Description("Organización a la que pertenece el saludo.")] Guid OrganizacionId,
        DateTimeOffset CreadoEn);

    [Description("Saludo nuevo; queda en la organización de la persona en sesión.")]
    public sealed record NuevoSaludo(
        [property: Description("Texto del saludo; se recortan los espacios de los extremos."), MinLength(1), MaxLength(HolaDbContext.LargoTexto)] string Texto);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        var saludos = grupo.MapGroup("/saludos");

        saludos.MapGet("", ListarAsync)
            .RequierePermiso(PermisosHola.Ver)
            .WithName("ListarSaludos")
            .WithSummary("Saludos de tu organización")
            .WithDescription($"Los saludos que puedes ver, del más reciente al más viejo (máximo {LimiteListado}). Un externo solo ve los de su organización.");

        saludos.MapGet("/{id:guid}", ObtenerAsync)
            .RequierePermiso(PermisosHola.Ver)
            .WithName("ObtenerSaludo")
            .WithSummary("Un saludo")
            .WithDescription("Un saludo por su id. 404 si no existe o es de una organización que no puedes ver.");

        saludos.MapPost("", CrearAsync)
            .RequierePermiso(PermisosHola.Gestionar)
            .WithName("CrearSaludo")
            .WithSummary("Escribir un saludo")
            .WithDescription("Crea un saludo en la organización de la persona en sesión.");
    }

    private static SaludoDto Dto(Saludo s) => new(s.Id, s.Texto, s.OrganizacionId, s.CreadoEn);

    private static async Task<List<SaludoDto>> ListarAsync(HolaDbContext contexto, CancellationToken ct) =>
        await contexto.Saludos.AsNoTracking()
            .OrderByDescending(s => s.CreadoEn)
            .Take(LimiteListado)
            .Select(s => new SaludoDto(s.Id, s.Texto, s.OrganizacionId, s.CreadoEn))
            .ToListAsync(ct);

    private static async Task<Results<Ok<SaludoDto>, NotFound>> ObtenerAsync(
        [Description("Id del saludo.")] Guid id, HolaDbContext contexto, CancellationToken ct)
    {
        var saludo = await contexto.Saludos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return saludo is null ? TypedResults.NotFound() : TypedResults.Ok(Dto(saludo));
    }

    private static async Task<Results<Created<SaludoDto>, ValidationProblem>> CrearAsync(
        [Description("Texto del saludo.")] NuevoSaludo nuevo, ClaimsPrincipal usuario, HolaDbContext contexto, CancellationToken ct)
    {
        var texto = (nuevo.Texto ?? string.Empty).Trim();
        if (texto.Length == 0 || texto.Length > HolaDbContext.LargoTexto)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["texto"] = [$"El saludo debe tener entre 1 y {HolaDbContext.LargoTexto} caracteres."],
            });
        }
        var saludo = new Saludo
        {
            OrganizacionId = Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion)!),
            AutorPersonaId = Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!),
            Texto = texto,
        };
        contexto.Saludos.Add(saludo);
        await contexto.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/hola/saludos/{saludo.Id}", Dto(saludo));
    }
}
