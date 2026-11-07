using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Favoritos de cada persona (HU 5.4, prompt maestro §11, ATLAS-ADR-0034): los documentos que marca para leer sin conexión.
/// Se guardan en el servidor por persona; la PWA guarda en el dispositivo lo que cada uno muestra, solo si lo puede ver, y lo
/// borra al quitarlo o al perder el acceso. El listado solo trae lo que la persona ve hoy: la fila queda por si vuelve el acceso.
/// </summary>
internal static class EndpointsFavoritosDocumentos
{
    [Description("Un documento marcado como favorito.")]
    public sealed record FavoritoDocumentoDto(
        Guid Id,
        Guid DocumentoId,
        string Titulo,
        [property: Description("manual_usuario, guia_funcional, guia_integracion, runbook, ficha_comercial, adr o nota_version.")] string Tipo,
        [property: Description("La versión vigente: la que la PWA guarda sin conexión.")] int? VersionVigente,
        [property: Description("Ruta de la PWA.")] string Ruta,
        DateTimeOffset MarcadoEn);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/favoritos", ListarAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("ListarFavoritosDocumentos")
            .WithSummary("Mis documentos favoritos")
            .WithDescription("Los que la persona marcó y todavía puede ver, del más reciente al más antiguo. Requiere atlas.docs.ver.")
            .Produces<List<FavoritoDocumentoDto>>();

        grupo.MapPut("/favoritos/{documentoId:guid}", MarcarAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("MarcarDocumentoFavorito")
            .WithSummary("Marcar un documento como favorito")
            .WithDescription("Idempotente. 404 si no existe o la persona no puede verlo. Requiere atlas.docs.ver.")
            .Produces<FavoritoDocumentoDto>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapDelete("/favoritos/{documentoId:guid}", QuitarAsync)
            .RequierePermiso(PermisosDocumentacion.Ver)
            .WithName("QuitarDocumentoFavorito")
            .WithSummary("Quitar un documento de los favoritos")
            .WithDescription("204 aunque no fuera favorito o la persona ya no pueda verlo. Requiere atlas.docs.ver.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ListarAsync(
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var persona = Persona(usuario);
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var filas = await contexto.Favoritos.AsNoTracking().Where(f => f.PersonaId == persona)
            .Join(acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()), f => f.DocumentoId, d => d.Id, (f, d) => new { Favorito = f, Documento = d })
            .OrderByDescending(x => x.Favorito.CreadoEn).ThenByDescending(x => x.Favorito.Id)
            .ToListAsync(ct);
        return Results.Ok(filas.Select(x => Dto(x.Favorito, x.Documento)).ToList());
    }

    private static async Task<IResult> MarcarAsync(
        [Description("Id del documento.")] Guid documentoId,
        ClaimsPrincipal usuario, DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        var documento = await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (documento is null)
        {
            return Results.NotFound();
        }
        var persona = Persona(usuario);
        var favorito = await contexto.Favoritos.AsNoTracking().FirstOrDefaultAsync(f => f.PersonaId == persona && f.DocumentoId == documentoId, ct);
        if (favorito is null)
        {
            favorito = new FavoritoDocumento { PersonaId = persona, DocumentoId = documentoId };
            contexto.Favoritos.Add(favorito);
            try
            {
                await contexto.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Otra petición de la misma persona lo marcó a la vez.
                contexto.ChangeTracker.Clear();
                favorito = await contexto.Favoritos.AsNoTracking().FirstAsync(f => f.PersonaId == persona && f.DocumentoId == documentoId, ct);
            }
        }
        return Results.Ok(Dto(favorito, documento));
    }

    private static async Task<IResult> QuitarAsync([Description("Id del documento.")] Guid documentoId, ClaimsPrincipal usuario, DocumentacionDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(usuario);
        await contexto.Favoritos.Where(f => f.PersonaId == persona && f.DocumentoId == documentoId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static FavoritoDocumentoDto Dto(FavoritoDocumento f, Documento d) =>
        new(f.Id, d.Id, d.Titulo, d.Tipo, d.VersionVigente, $"/documentacion/{d.Id}", f.CreadoEn);

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);
}
