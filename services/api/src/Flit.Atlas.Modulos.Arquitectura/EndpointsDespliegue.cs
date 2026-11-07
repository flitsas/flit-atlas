using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Arquitectura.EndpointsArquitectura;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>
/// Snapshots de despliegue (HU 6.4, ATLAS-ADR-0056 §5): todos exigen <c>atlas.arquitectura.ver_despliegue</c>, porque traen los hosts de
/// ingress y las imágenes. Las IP y los hosts internos ni siquiera llegan a la base: se rechazan al publicar.
/// </summary>
internal static class EndpointsDespliegue
{
    private const int LimiteHistorial = 100;

    [Description("Publicaciones de snapshots.")]
    public sealed record ListadoSnapshots(IReadOnlyList<ResumenPublicacionArquitectura> Elementos);

    [Description("Un snapshot con su publicación.")]
    public sealed record DetalleSnapshot(ResumenPublicacionArquitectura Publicacion, SnapshotDespliegue Snapshot);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/despliegue/vigentes", VigentesAsync)
            .RequierePermiso(PermisosArquitectura.VerDespliegue)
            .WithName("ListarSnapshotsVigentes")
            .WithSummary("Listar el snapshot vigente de cada ambiente y clúster, también en una fecha pasada")
            .WithDescription("Responde a «¿qué corría en pdn el 11/10 a las 23:12?»: el último snapshot con fecha menor o igual a «en» por ambiente y clúster (plan 01 §9.2).");

        grupo.MapGet("/despliegue/publicaciones", HistorialAsync)
            .RequierePermiso(PermisosArquitectura.VerDespliegue)
            .WithName("ListarSnapshots")
            .WithSummary("Listar el historial de snapshots")
            .WithDescription("De la más reciente a la más antigua, hasta 100. Se puede filtrar por ambiente y por clúster.");

        grupo.MapGet("/despliegue/publicaciones/{id:guid}", DetalleAsync)
            .RequierePermiso(PermisosArquitectura.VerDespliegue)
            .WithName("ObtenerSnapshot")
            .WithSummary("Obtener un snapshot")
            .WithDescription("Workloads, imágenes, réplicas y hosts públicos de un ambiente y un clúster según un sha de gitops.");

        grupo.MapGet("/despliegue/diff/{a:guid}/{b:guid}", DiffAsync)
            .RequierePermiso(PermisosArquitectura.VerDespliegue)
            .WithName("CompararSnapshots")
            .WithSummary("Comparar dos snapshots")
            .WithDescription("Workloads y hosts agregados o quitados, e imágenes y réplicas cambiadas (plan 01 §9.3).");
    }

    private static IQueryable<PublicacionArquitectura> Snapshots(ArquitecturaDbContext contexto, AccesoArquitectura acceso) =>
        acceso.Filtrar(contexto.Publicaciones.AsNoTracking().Where(p => p.Tipo == TipoPublicacion.Despliegue));

    private static async Task<Ok<ListadoSnapshots>> VigentesAsync(
        DateTimeOffset? en, string? ambiente, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var consulta = Snapshots(contexto, acceso);
        if (en is { } fecha)
        {
            consulta = consulta.Where(p => p.PublicadoEn <= fecha);
        }
        if (ambiente is not null)
        {
            consulta = consulta.Where(p => p.Canal == ambiente);
        }
        var candidatas = consulta;
        var filas = await SinContenido(consulta
                .Where(p => !candidatas.Any(q => q.Canal == p.Canal && q.Cluster == p.Cluster
                    && (q.PublicadoEn > p.PublicadoEn || (q.PublicadoEn == p.PublicadoEn && q.Id.CompareTo(p.Id) > 0))))
                .OrderBy(p => p.Canal).ThenBy(p => p.Cluster))
            .ToListAsync(ct);
        return TypedResults.Ok(new ListadoSnapshots([.. filas.Select(Resumen)]));
    }

    private static async Task<Ok<ListadoSnapshots>> HistorialAsync(
        string? ambiente, string? cluster, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var consulta = Snapshots(contexto, acceso);
        if (ambiente is not null)
        {
            consulta = consulta.Where(p => p.Canal == ambiente);
        }
        if (cluster is not null)
        {
            consulta = consulta.Where(p => p.Cluster == cluster);
        }
        var filas = await SinContenido(consulta.OrderByDescending(p => p.PublicadoEn).ThenByDescending(p => p.Id).Take(LimiteHistorial)).ToListAsync(ct);
        return TypedResults.Ok(new ListadoSnapshots([.. filas.Select(Resumen)]));
    }

    private static async Task<Results<Ok<DetalleSnapshot>, NotFound>> DetalleAsync(
        Guid id, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var fila = await SinContenido(Snapshots(contexto, acceso).Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        if (fila is null)
        {
            return TypedResults.NotFound();
        }
        var contenido = await contexto.Publicaciones.AsNoTracking().Where(p => p.Id == id).Select(p => p.Contenido).FirstAsync(ct);
        return TypedResults.Ok(new DetalleSnapshot(Resumen(fila), ReglasSnapshot.Parsear(contenido)));
    }

    private static async Task<Results<Ok<DiffArquitectura>, NotFound>> DiffAsync(
        Guid a, Guid b, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var filas = await Snapshots(contexto, acceso).Where(p => p.Id == a || p.Id == b).Select(p => new { p.Id, p.Contenido }).ToListAsync(ct);
        var (antes, despues) = (filas.FirstOrDefault(f => f.Id == a), filas.FirstOrDefault(f => f.Id == b));
        return antes is null || despues is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ReglasSnapshot.Diff(ReglasSnapshot.Parsear(antes.Contenido), ReglasSnapshot.Parsear(despues.Contenido)));
    }
}
