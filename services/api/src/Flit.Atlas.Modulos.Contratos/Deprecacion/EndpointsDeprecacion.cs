using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;

namespace Flit.Atlas.Modulos.Contratos.Deprecacion;

/// <summary>
/// Deprecar una versión con su fecha de retiro (HU 4.8, plan 00 §8.2 regla 7 y §8.5, ATLAS-ADR-0046), en
/// <c>/api/v1/contratos</c>. Exige <c>atlas.contratos.publicar_manual</c>. Cambia solo el estado y la fecha de retiro (el
/// trigger de la base rechaza tocar lo demás), audita en la misma transacción, encola
/// <c>atlas.contratos.version_deprecada</c> y pide los avisos a las organizaciones afectadas y a los responsables, que
/// salen fuera de la petición (<see cref="AvisosDeprecacion"/>).
/// </summary>
internal static class EndpointsDeprecacion
{
    public const string AccionDeprecada = "contratos.version_deprecada";

    [Description("Deprecar una versión: la fecha en que se retira (hora de Bogotá).")]
    public sealed record SolicitudDeprecacion(
        [property: Description("Fecha de retiro (aaaa-mm-dd): después de hoy y a lo sumo dentro de dos años. Deprecar otra vez cambia la fecha y avisa de nuevo.")] DateOnly? RetiroEn);

    [Description("Resultado de deprecar una versión.")]
    public sealed record ResultadoDeprecacion(
        VersionResumen Version,
        [property: Description("La versión estable vigente más alta posterior, si la hay: la que los avisos recomiendan.")] string? Reemplazo,
        [property: Description("Falso si ya estaba deprecada con esa misma fecha (no se repite nada).")] bool Cambio);

    [Description("Por qué no se pudo deprecar, con un código estable.")]
    public sealed record ErrorDeprecacion(
        [property: Description("no_encontrado (404) o retirada (409).")] string Codigo,
        string Mensaje);

    /// <summary><c>data</c> de <c>atlas.contratos.version_deprecada</c> v1 (contracts/asyncapi/atlas-events.v1.yaml).</summary>
    internal sealed record DatosVersionDeprecada(
        Guid ContratoId, Guid VersionId, Guid AplicativoId, string Contrato, string Version, DateOnly RetiroEn, DateOnly? RetiroAnterior, string? Reemplazo);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/{id:guid}/versiones/{version}/deprecar", DeprecarAsync)
            .RequierePermiso(PermisosContratos.PublicarManual)
            .WithName("DeprecarVersionContrato")
            .WithSummary("Deprecar una versión con su fecha de retiro")
            .WithDescription($"Marca la versión como deprecada con su fecha de retiro (después de hoy en Bogotá y a lo sumo {ReglasDeprecacion.DiasMaximosRetiro} días adelante); deprecarla otra vez cambia la fecha. El portal la muestra con el aviso y Atlas avisa, fuera de la petición, a las personas de las organizaciones con un paquete compartido vigente que la incluye (con las reglas del portal) y a los responsables del aplicativo, por la app, push y correo; después, 30 y 7 días antes del retiro. 409 retirada si ya se retiró. Auditado y con el evento atlas.contratos.version_deprecada. Requiere atlas.contratos.publicar_manual.")
            .Produces<ResultadoDeprecacion>()
            .Produces<ErrorDeprecacion>(StatusCodes.Status404NotFound)
            .Produces<ErrorDeprecacion>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> DeprecarAsync(
        [Description("Id del contrato.")] Guid id,
        [Description("Versión semántica (3.3.0).")] string version,
        [Description("La fecha de retiro.")] SolicitudDeprecacion solicitud,
        HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma, DisparadorAvisos disparador, TimeProvider reloj, CancellationToken ct)
    {
        var fila = await contexto.Versiones.FirstOrDefaultAsync(v => v.ContratoId == id && v.Version == version, ct);
        var contrato = fila is null ? null : await contexto.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (fila is null || contrato is null)
        {
            return Results.Json(new ErrorDeprecacion("no_encontrado", "La versión no existe."), statusCode: StatusCodes.Status404NotFound);
        }
        if (fila.Estado == EstadoVersion.Retirada)
        {
            return Results.Json(new ErrorDeprecacion("retirada", $"La versión {fila.Version} ya se retiró: no se puede deprecar."), statusCode: StatusCodes.Status409Conflict);
        }
        var hoy = ReglasDeprecacion.Hoy(reloj);
        if (solicitud?.RetiroEn is not { } retiro || retiro <= hoy || retiro > hoy.AddDays(ReglasDeprecacion.DiasMaximosRetiro))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["retiroEn"] = [$"Elige una fecha de retiro después de hoy ({ReglasDeprecacion.Texto(hoy)}) y a lo sumo dentro de dos años."],
            });
        }
        var reemplazo = (await ReglasDeprecacion.ReemplazosAsync(contexto, [(fila.Id, fila.ContratoId, fila.Version)], ct)).GetValueOrDefault(fila.Id);
        if (fila.Estado == EstadoVersion.Deprecada && fila.RetiroEn == retiro)
        {
            return Results.Ok(new ResultadoDeprecacion(EndpointsContratos.Version(SinContenidoDe(fila)), reemplazo, Cambio: false));
        }

        var anterior = fila.Estado == EstadoVersion.Deprecada ? fila.RetiroEn : null;
        fila.Estado = EstadoVersion.Deprecada;
        fila.RetiroEn = retiro;
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            await contexto.SaveChangesAsync(ct);
            plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
            await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = reloj.GetUtcNow(),
                ActorTipo = TipoActor.Persona,
                ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
                OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
                Accion = AccionDeprecada,
                RecursoTipo = AvisosDeprecacion.RecursoAuditoria,
                RecursoId = fila.Id.ToString(),
                Version = fila.Version,
                Ip = http.Connection.RemoteIpAddress,
                Datos = JsonSerializer.Serialize(new { contrato = id, retiroEn = retiro, retiroAnterior = anterior, reemplazo }, JsonSerializerOptions.Web),
                CorrelacionId = http.TraceIdentifier,
            });
            plataforma.EncolarEvento(EventosContratos.VersionDeprecada,
                new DatosVersionDeprecada(contrato.Id, fila.Id, contrato.AplicativoId, contrato.Codigo, fila.Version, retiro, anterior, reemplazo));
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
            await plataforma.Database.UseTransactionAsync(null, ct);
        }
        disparador.Pedir(fila.Id);
        return Results.Ok(new ResultadoDeprecacion(EndpointsContratos.Version(SinContenidoDe(fila)), reemplazo, Cambio: true));
    }

    private static VersionContratoSinContenido SinContenidoDe(VersionContrato v) => new(
        v.Id, v.ContratoId, v.Version, v.EsEstable, v.Sha256, v.Formato, v.Tamano, v.Repositorio, v.Rama, v.CommitSha, v.Workflow, v.WorkflowDelJob,
        v.Ejecucion, v.PublicadoPor, v.PublicadoEn, v.TieneBreaking, v.Estado, v.RetiroEn);
}
