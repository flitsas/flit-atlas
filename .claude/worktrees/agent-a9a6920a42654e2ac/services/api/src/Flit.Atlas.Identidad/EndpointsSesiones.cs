using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Emergencia;
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
    [Description("Sesión activa del usuario en un dispositivo («dispositivos conectados»). La IP es un dato personal.")]
    public sealed record SesionDispositivo(
        Guid Id, string Dispositivo, string? Ip, DateTimeOffset CreadaEn, DateTimeOffset UltimoUsoEn, DateTimeOffset ExpiraEn,
        bool Actual);

    [Description("Cuántas sesiones activas se cerraron.")]
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
        })
            .WithName("ListarMisSesiones")
            .WithSummary("Dispositivos conectados")
            .WithDescription("Sesiones activas del usuario, de la más reciente a la más antigua; «actual» marca la de esta petición.");

        // Cerrar la sesión actual es salir; cerrar la de otro dispositivo es sensible.
        sesiones.MapDelete("/{id:guid}", async ([Description("Id de la sesión, tal como lo lista «dispositivos conectados».")] Guid id, HttpContext http, AlmacenSesiones almacen, RegistroSeguridad registro,
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
        })
            .WithMetadata(new MetadatoReautenticacion())
            .WithName("CerrarMiSesion")
            .WithSummary("Cerrar una sesión")
            .WithDescription(
                "Cierra una sesión del propio usuario. Si es la actual equivale a salir (Clear-Site-Data); si es la de otro " +
                "dispositivo exige un ingreso de menos de 10 minutos (401 con codigo reautenticacion_requerida).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        sesiones.MapPost("/cerrar-otras", async (ClaimsPrincipal usuario, AlmacenSesiones almacen, RegistroSeguridad registro,
            CancellationToken ct) =>
        {
            var cerradas = await almacen.RevocarTodasAsync(UsuarioId(usuario), MotivosRevocacion.CierreRemoto, SesionActual(usuario), ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.CierreRemoto,
                DatosEventoSeguridad.DeSesion(usuario) with { Detalle = new { otras = true, cerradas } }, ct);
            return new ResultadoCierre(cerradas);
        })
            .RequiereReautenticacion()
            .WithName("CerrarMisOtrasSesiones")
            .WithSummary("Cerrar las demás sesiones")
            .WithDescription("Cierra todas las sesiones del usuario salvo la actual. Exige reautenticación.");

        // Administración: permiso atlas.identidad.administrar (PoliticasIdentidad, ATLAS-ADR-0023).
        var personas = identidad.MapGroup("/admin/personas/{personaId:guid}")
            .RequireAuthorization(PoliticasIdentidad.Administrar)
            .RequiereReautenticacion();

        personas.MapPost("/sesiones/cerrar", async ([Description("Persona de Atlas (plataforma_persona).")] Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
            IInvalidadorSesiones invalidador, CancellationToken ct) =>
        {
            if (!await plataforma.Personas.AnyAsync(p => p.Id == personaId, ct))
            {
                return Results.NotFound();
            }
            var cerradas = await invalidador.InvalidarSesionesAsync(
                personaId, MotivosInvalidacion.CierreAdministrativo, PersonaId(usuario), ct);
            return Results.Ok(new ResultadoCierre(cerradas));
        })
            .WithName("CerrarSesionesDePersona")
            .WithSummary("Cerrar todas las sesiones de una persona")
            .WithDescription(
                "Administración: requiere atlas.identidad.administrar. Cambia el sello de seguridad de la " +
                "persona y revoca todas sus sesiones, sin suspenderla. Exige reautenticación.")
            .Produces<ResultadoCierre>()
            .Produces(StatusCodes.Status404NotFound);

        personas.MapPost("/suspender", async ([Description("Persona de Atlas (plataforma_persona).")] Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
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
            if (await CuentaEmergencia.EsDeEmergenciaAsync(plataforma, personaId, ct))
            {
                // La cuenta de emergencia no se desarma desde Atlas: se rota con el comando de consola (ATLAS-ADR-0027).
                return Results.Conflict(new { error = CuentaEmergencia.CodigoProtegida });
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
        })
            .WithName("SuspenderPersona")
            .WithSummary("Suspender a una persona")
            .WithDescription(
                "Administración: requiere atlas.identidad.administrar. Marca la persona como suspendida, " +
                "invalida sus sesiones de inmediato y le impide volver a entrar. 409 si intentas suspenderte o si es la cuenta de " +
                "administrador de emergencia (se rota con el comando de consola). Exige " +
                "reautenticación.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        personas.MapPost("/reactivar", async ([Description("Persona de Atlas (plataforma_persona).")] Guid personaId, ClaimsPrincipal usuario, PlataformaDbContext plataforma,
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
        })
            .WithName("ReactivarPersona")
            .WithSummary("Reactivar a una persona")
            .WithDescription(
                "Administración: requiere atlas.identidad.administrar. Devuelve a activa una persona " +
                "suspendida; vuelve a entrar con un ingreso nuevo. Exige reautenticación.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
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
