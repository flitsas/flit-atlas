using System.Security.Claims;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad;

/// <summary>
/// «Dispositivos conectados» y administración de sesiones y suspensiones (HU 1.3b, ATLAS-ADR-0022). Cerrar sesiones de
/// otros dispositivos y todo lo administrativo exige un ingreso reciente (<see cref="Reautenticacion"/>).
/// </summary>
public static class EndpointsSesiones
{
    public sealed record SesionDispositivo(
        Guid Id, string Dispositivo, string? Ip, DateTimeOffset CreadaEn, DateTimeOffset UltimoUsoEn, DateTimeOffset ExpiraEn,
        bool Actual);

    public sealed record ResultadoCierre(int Cerradas);

    internal static void MapSesiones(this RouteGroupBuilder identidad)
    {
        var sesiones = identidad.MapGroup("/sesiones").RequireAuthorization();

        sesiones.MapGet("/", async (ClaimsPrincipal usuario, AlmacenSesiones almacen, CancellationToken ct) =>
        {
            var actual = SesionActual(usuario);
            var lista = await almacen.ListarAsync(UsuarioId(usuario), ct);
            return lista.Select(s => new SesionDispositivo(
                s.Id, s.Dispositivo, s.Ip?.ToString(), s.CreadaEn, s.UltimoUsoEn, s.ExpiraEn, s.Id == actual)).ToList();
        });

        // Cerrar la sesión actual es salir; cerrar la de otro dispositivo es sensible.
        sesiones.MapDelete("/{id:guid}", async (Guid id, HttpContext http, AlmacenSesiones almacen, RegistroSeguridad registro,
            CancellationToken ct) =>
        {
            if (id == SesionActual(http.User))
            {
                await SalirAsync(http, registro, ct);
                return Results.NoContent();
            }
            if (!Reautenticacion.EsReciente(http))
            {
                return await Reautenticacion.RechazarAsync(http);
            }
            if (!await almacen.RevocarDeUsuarioAsync(id, UsuarioId(http.User), MotivosRevocacion.CierreRemoto, ct))
            {
                return Results.NotFound();
            }
            await registro.RegistrarAsync(TiposEventoSeguridad.CierreRemoto,
                DatosEventoSeguridad.DeSesion(http.User) with { Detalle = new { sesiones_cerradas = new[] { id } } }, ct);
            return Results.NoContent();
        });

        sesiones.MapPost("/cerrar-otras", async (ClaimsPrincipal usuario, AlmacenSesiones almacen, RegistroSeguridad registro,
            CancellationToken ct) =>
        {
            var cerradas = await almacen.RevocarTodasAsync(UsuarioId(usuario), MotivosRevocacion.CierreRemoto, SesionActual(usuario), ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.CierreRemoto,
                DatosEventoSeguridad.DeSesion(usuario) with { Detalle = new { otras = true, cerradas } }, ct);
            return new ResultadoCierre(cerradas);
        }).RequiereReautenticacion();

        // Administración (provisional hasta la HU 1.4: ver PoliticasIdentidad).
        var personas = identidad.MapGroup("/admin/personas/{personaId:guid}")
            .RequireAuthorization(PoliticasIdentidad.Administrar)
            .RequiereReautenticacion();

        personas.MapPost("/sesiones/cerrar", async (Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
            IInvalidadorSesiones invalidador, CancellationToken ct) =>
        {
            if (!await plataforma.Personas.AnyAsync(p => p.Id == personaId, ct))
            {
                return Results.NotFound();
            }
            var cerradas = await invalidador.InvalidarSesionesAsync(
                personaId, MotivosInvalidacion.CierreAdministrativo, PersonaId(usuario), ct);
            return Results.Ok(new ResultadoCierre(cerradas));
        });

        personas.MapPost("/suspender", async (Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
            IInvalidadorSesiones invalidador, RegistroSeguridad registro, CancellationToken ct) =>
        {
            var actor = PersonaId(usuario);
            if (personaId == actor)
            {
                return Results.Conflict(new { error = "no_puedes_suspenderte" });
            }
            var persona = await plataforma.Personas.FirstOrDefaultAsync(p => p.Id == personaId, ct);
            if (persona is null)
            {
                return Results.NotFound();
            }

            // Primero el estado y después el sello: un ingreso simultáneo o ve la persona suspendida o queda con el
            // sello viejo (ATLAS-ADR-0022).
            if (persona.Estado != EstadoPersona.Suspendida)
            {
                persona.Estado = EstadoPersona.Suspendida;
                await plataforma.SaveChangesAsync(ct);
                await registro.RegistrarAsync(TiposEventoSeguridad.PersonaSuspendida, new DatosEventoSeguridad
                {
                    PersonaId = personaId,
                    OrganizacionId = persona.OrganizacionId,
                    ActorPersonaId = actor,
                }, ct);
            }
            await invalidador.InvalidarSesionesAsync(personaId, MotivosInvalidacion.Suspension, actor, ct);
            return Results.NoContent();
        });

        personas.MapPost("/reactivar", async (Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
            RegistroSeguridad registro, CancellationToken ct) =>
        {
            var persona = await plataforma.Personas.FirstOrDefaultAsync(p => p.Id == personaId, ct);
            if (persona is null)
            {
                return Results.NotFound();
            }
            if (persona.Estado == EstadoPersona.Suspendida)
            {
                persona.Estado = EstadoPersona.Activa;
                await plataforma.SaveChangesAsync(ct);
                await registro.RegistrarAsync(TiposEventoSeguridad.PersonaReactivada, new DatosEventoSeguridad
                {
                    PersonaId = personaId,
                    OrganizacionId = persona.OrganizacionId,
                    ActorPersonaId = PersonaId(usuario),
                }, ct);
            }
            return Results.NoContent();
        });
    }

    /// <summary>Cierre de la sesión actual: la revoca en el servidor, borra la cookie y los datos del navegador.</summary>
    internal static async Task SalirAsync(HttpContext http, RegistroSeguridad registro, CancellationToken ct)
    {
        var autenticado = http.User.Identity?.IsAuthenticated == true;
        http.Items[AlmacenSesiones.ClaveMotivoRevocacion] = MotivosRevocacion.CierreDeSesion;
        // Revoca la sesión en el servidor (AlmacenSesiones.RemoveAsync) y borra la cookie.
        await http.SignOutAsync(ClaimsAtlas.EsquemaCookie);
        // El navegador borra la caché, el almacenamiento (IndexedDB, service worker) y las cookies del origen.
        http.Response.Headers["Clear-Site-Data"] = "\"cache\", \"cookies\", \"storage\"";
        if (autenticado)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.CierreSesion, DatosEventoSeguridad.DeSesion(http.User), ct);
        }
    }

    private static Guid UsuarioId(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.Sub)!);

    private static Guid? PersonaId(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var id) ? id : null;

    private static Guid? SesionActual(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.Sid), out var sid) ? sid : null;
}
