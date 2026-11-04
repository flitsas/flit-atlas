using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;
using static Flit.Atlas.Modulos.Contratos.Portal.EndpointsPortal;

namespace Flit.Atlas.Modulos.Contratos.Favoritos;

/// <summary>
/// Favoritos de cada persona (HU 4.9, prompt maestro §11, ATLAS-ADR-0034): los contratos internos que marca un empleado
/// (<c>/api/v1/contratos/favoritos</c>, con <c>ver_internos</c>) y los paquetes, contratos y guías compartidos que marca
/// una persona del portal (<c>/api/v1/portal/favoritos</c>, con <c>portal.ver</c>). Se guardan en el servidor por persona;
/// la PWA guarda en el dispositivo lo que cada favorito muestra, solo si lo puede ver, y lo borra al quitarlo o al perder
/// el acceso. El listado solo trae lo que la persona ve hoy: un favorito de un paquete revocado o vencido no aparece (la
/// fila queda por si vuelve el acceso).
/// </summary>
internal static class EndpointsFavoritos
{
    public const string AccionAgregado = "contratos.portal_favorito_agregado";
    public const string AccionQuitado = "contratos.portal_favorito_quitado";

    [Description("Un contrato interno marcado como favorito.")]
    public sealed record FavoritoInterno(
        Guid Id,
        Guid ContratoId,
        string Codigo,
        string Nombre,
        [property: Description("openapi, asyncapi o proto.")] string Tipo,
        AplicativoDelContrato Aplicativo,
        [property: Description("La última publicada: la que abre la pantalla del contrato (y la que la PWA guarda sin conexión).")] string? Version,
        [property: Description("Ruta de la PWA.")] string Ruta,
        DateTimeOffset MarcadoEn);

    [Description("Lo compartido que la persona marcó como favorito y aún ve.")]
    public sealed record FavoritoPortal(
        Guid Id,
        [property: Description("paquete, contrato o guia.")] string Tipo,
        Guid PaqueteId,
        Guid? ContratoId,
        Guid? GuiaId,
        string Titulo,
        [property: Description("El paquete, en un contrato o una guía; la descripción, en un paquete.")] string? Detalle,
        [property: Description("Ruta de la PWA.")] string Ruta,
        DateTimeOffset MarcadoEn);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/favoritos", InternosAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("ListarFavoritosContratos")
            .WithSummary("Mis contratos favoritos")
            .WithDescription("Los contratos que la persona marcó como favoritos (la PWA los guarda para verlos sin conexión), del más reciente al más antiguo. Requiere atlas.contratos.ver_internos.")
            .Produces<List<FavoritoInterno>>();

        grupo.MapPut("/favoritos/contratos/{contratoId:guid}", MarcarInternoAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("MarcarContratoFavorito")
            .WithSummary("Marcar un contrato como favorito")
            .WithDescription("Idempotente: si ya era favorito, lo devuelve igual. 404 si el contrato no existe. Requiere atlas.contratos.ver_internos.")
            .Produces<FavoritoInterno>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapDelete("/favoritos/contratos/{contratoId:guid}", QuitarInternoAsync)
            .RequierePermiso(PermisosContratos.VerInternos)
            .WithName("QuitarContratoFavorito")
            .WithSummary("Quitar un contrato de los favoritos")
            .WithDescription("204 aunque no fuera favorito. Requiere atlas.contratos.ver_internos.")
            .Produces(StatusCodes.Status204NoContent);
    }

    public static void MapearPortal(RouteGroupBuilder portal)
    {
        portal.MapGet("/favoritos", PortalAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ListarFavoritosPortal")
            .WithSummary("Mis favoritos del portal")
            .WithDescription("Los paquetes, contratos y guías compartidos que la persona marcó y todavía ve (su organización, su alcance, la vigencia de la compartición y, en contratos y guías, el detalle técnico). Lo que ya no ve no aparece. Requiere atlas.portal.ver.")
            .Produces<List<FavoritoPortal>>();

        portal.MapPut("/favoritos/paquetes/{paqueteId:guid}", ([Description("Id del paquete.")] Guid paqueteId, HttpContext http, AccesoPortal acceso, ContratosDbContext contexto,
                PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct) =>
                MarcarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.Paquete, paqueteId, null, null, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("MarcarPaqueteFavoritoPortal")
            .WithSummary("Marcar un paquete compartido como favorito")
            .WithDescription("Idempotente. 404 si no está compartido con la organización de la persona o queda fuera de su alcance (auditado). Requiere atlas.portal.ver.")
            .Produces<FavoritoPortal>()
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapPut("/favoritos/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}", ([Description("Id del paquete.")] Guid paqueteId, [Description("Id del contrato.")] Guid contratoId, HttpContext http, AccesoPortal acceso,
                ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct) =>
                MarcarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.ContratoCompartido, paqueteId, contratoId, null, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("MarcarContratoFavoritoPortal")
            .WithSummary("Marcar un contrato compartido como favorito")
            .WithDescription("Idempotente. 404 como el paquete o si el contrato no está en él; 403 solo_resumen si el rol de la persona no ve el detalle técnico. Requiere atlas.portal.ver.")
            .Produces<FavoritoPortal>()
            .Produces<ErrorPortal>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapPut("/favoritos/paquetes/{paqueteId:guid}/guias/{guiaId:guid}", ([Description("Id del paquete.")] Guid paqueteId, [Description("Id de la guía.")] Guid guiaId, HttpContext http, AccesoPortal acceso,
                ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct) =>
                MarcarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.Guia, paqueteId, null, guiaId, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("MarcarGuiaFavoritaPortal")
            .WithSummary("Marcar una guía compartida como favorita")
            .WithDescription("Idempotente. 404 como el paquete o si la guía no está en él; 403 solo_resumen. Requiere atlas.portal.ver.")
            .Produces<FavoritoPortal>()
            .Produces<ErrorPortal>(StatusCodes.Status403Forbidden)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapDelete("/favoritos/paquetes/{paqueteId:guid}", ([Description("Id del paquete.")] Guid paqueteId, HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma,
                AccesoPortal acceso, TimeProvider reloj, CancellationToken ct) =>
                QuitarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.Paquete, paqueteId, null, null, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("QuitarPaqueteFavoritoPortal")
            .WithSummary("Quitar un paquete de los favoritos")
            .WithDescription("Quita el favorito de la persona aunque ya no vea el paquete. 404 si no lo tenía. Requiere atlas.portal.ver.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapDelete("/favoritos/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}", ([Description("Id del paquete.")] Guid paqueteId, [Description("Id del contrato.")] Guid contratoId, HttpContext http,
                ContratosDbContext contexto, PlataformaDbContext plataforma, AccesoPortal acceso, TimeProvider reloj, CancellationToken ct) =>
                QuitarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.ContratoCompartido, paqueteId, contratoId, null, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("QuitarContratoFavoritoPortal")
            .WithSummary("Quitar un contrato compartido de los favoritos")
            .WithDescription("Quita el favorito de la persona aunque ya no vea el contrato. 404 si no lo tenía. Requiere atlas.portal.ver.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);

        portal.MapDelete("/favoritos/paquetes/{paqueteId:guid}/guias/{guiaId:guid}", ([Description("Id del paquete.")] Guid paqueteId, [Description("Id de la guía.")] Guid guiaId, HttpContext http,
                ContratosDbContext contexto, PlataformaDbContext plataforma, AccesoPortal acceso, TimeProvider reloj, CancellationToken ct) =>
                QuitarPortalAsync(http, acceso, contexto, plataforma, reloj, TipoFavorito.Guia, paqueteId, null, guiaId, ct))
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("QuitarGuiaFavoritaPortal")
            .WithSummary("Quitar una guía compartida de los favoritos")
            .WithDescription("Quita el favorito de la persona aunque ya no vea la guía. 404 si no lo tenía. Requiere atlas.portal.ver.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorPortal>(StatusCodes.Status404NotFound);
    }

    // --- Internos ---

    private static async Task<IResult> InternosAsync(ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, CancellationToken ct)
    {
        var persona = Persona(usuario);
        var favoritos = await contexto.Favoritos.AsNoTracking().Where(f => f.PersonaId == persona && f.Tipo == TipoFavorito.Contrato)
            .OrderByDescending(f => f.CreadoEn).ThenByDescending(f => f.Id).ToListAsync(ct);
        return Results.Ok(await InternosDeAsync(favoritos, contexto, lector, ct));
    }

    private static async Task<List<FavoritoInterno>> InternosDeAsync(
        IReadOnlyList<FavoritoContrato> favoritos, ContratosDbContext contexto, ILectorAplicativos lector, CancellationToken ct)
    {
        var ids = favoritos.Select(f => f.ContratoId!.Value).Distinct().ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var versiones = (await SinContenido(contexto.Versiones.AsNoTracking().Where(v => ids.Contains(v.ContratoId))).ToListAsync(ct)).ToLookup(v => v.ContratoId);
        var aplicativos = (await lector.ListarAsync(ct)).ToDictionary(a => a.Id);
        return [.. favoritos.Where(f => contratos.ContainsKey(f.ContratoId!.Value)).Select(f =>
        {
            var c = contratos[f.ContratoId!.Value];
            var resumen = Resumen(c, aplicativos.GetValueOrDefault(c.AplicativoId), [.. versiones[c.Id]]);
            return new FavoritoInterno(f.Id, c.Id, c.Codigo, c.Nombre, resumen.Tipo, resumen.Aplicativo, resumen.UltimaVersion?.Version,
                $"/contratos/{c.Id}", f.CreadoEn);
        })];
    }

    private static async Task<IResult> MarcarInternoAsync(
        [Description("Id del contrato.")] Guid contratoId, ClaimsPrincipal usuario, ContratosDbContext contexto, ILectorAplicativos lector, CancellationToken ct)
    {
        if (!await contexto.Contratos.AnyAsync(c => c.Id == contratoId, ct))
        {
            return Results.NotFound();
        }
        var favorito = await GuardarAsync(contexto, usuario, TipoFavorito.Contrato, null, contratoId, null, ct);
        return Results.Ok((await InternosDeAsync([favorito], contexto, lector, ct)).Single());
    }

    private static async Task<IResult> QuitarInternoAsync(
        [Description("Id del contrato.")] Guid contratoId, ClaimsPrincipal usuario, ContratosDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(usuario);
        await contexto.Favoritos.Where(f => f.PersonaId == persona && f.Tipo == TipoFavorito.Contrato && f.ContratoId == contratoId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    // --- Portal ---

    private static async Task<IResult> PortalAsync(HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(http.User);
        var vista = await acceso.VistaAsync(http.User, ct);
        var favoritos = await contexto.Favoritos.AsNoTracking()
            .Where(f => f.PersonaId == persona && f.OrganizacionId == vista.OrganizacionId && f.Tipo != TipoFavorito.Contrato)
            .OrderByDescending(f => f.CreadoEn).ThenByDescending(f => f.Id).ToListAsync(ct);
        return Results.Ok(favoritos.Select(f => Visible(f, vista)).OfType<FavoritoPortal>().ToList());
    }

    /// <summary>El favorito tal como lo ve hoy la persona, o <c>null</c> si ya no lo ve (revocado, vencido, fuera de su alcance o sin detalle técnico).</summary>
    private static FavoritoPortal? Visible(FavoritoContrato f, VistaPortal vista)
    {
        if (vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == f.PaqueteId) is not { } paquete)
        {
            return null;
        }
        var nombre = paquete.RevisionPaquete.Nombre;
        switch (f.Tipo)
        {
            case TipoFavorito.Paquete:
                return new FavoritoPortal(f.Id, "paquete", paquete.Paquete.Id, null, null, nombre, paquete.RevisionPaquete.Descripcion,
                    $"/portal/paquetes/{paquete.Paquete.Id}", f.CreadoEn);
            case TipoFavorito.ContratoCompartido when paquete.Contratos.FirstOrDefault(c => c.Contrato.Id == f.ContratoId) is { Tecnico: true } contrato:
                return new FavoritoPortal(f.Id, "contrato", paquete.Paquete.Id, contrato.Contrato.Id, null, $"{contrato.Contrato.Nombre} {contrato.Version.Version}", nombre,
                    $"/portal/paquetes/{paquete.Paquete.Id}/contratos/{contrato.Contrato.Id}", f.CreadoEn);
            case TipoFavorito.Guia when paquete.Contratos.FirstOrDefault(c => c.Guias.Any(g => g.GuiaId == f.GuiaId)) is { Tecnico: true } deLaGuia:
                var guia = deLaGuia.Guias.First(g => g.GuiaId == f.GuiaId);
                return new FavoritoPortal(f.Id, "guia", paquete.Paquete.Id, null, guia.GuiaId, guia.Titulo, nombre,
                    $"/portal/paquetes/{paquete.Paquete.Id}/guias/{guia.GuiaId}", f.CreadoEn);
            default:
                return null;
        }
    }

    private static async Task<IResult> MarcarPortalAsync(
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj,
        TipoFavorito tipo, Guid paqueteId, Guid? contratoId, Guid? guiaId, CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        var paquete = vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == paqueteId);
        var contrato = tipo switch
        {
            TipoFavorito.ContratoCompartido => paquete?.Contratos.FirstOrDefault(c => c.Contrato.Id == contratoId && c.Disponible),
            TipoFavorito.Guia => paquete?.Contratos.FirstOrDefault(c => c.Guias.Any(g => g.GuiaId == guiaId)),
            _ => null,
        };
        if (paquete is null || (tipo != TipoFavorito.Paquete && contrato is null))
        {
            return await NegadoAsync(http, plataforma, reloj, vista, paqueteId, $"favorito:{Texto(tipo)}", ct);
        }
        if (contrato is { Tecnico: false })
        {
            return SoloResumen();
        }
        var favorito = await GuardarAsync(contexto, http.User, tipo, paqueteId, contratoId, guiaId, ct);
        await AuditarAsync(http, plataforma, reloj, AccionAgregado, vista, paqueteId.ToString(), paquete.Revision,
            new { tipo = Texto(tipo), contrato = contratoId, guia = guiaId }, ct);
        return Results.Ok(Visible(favorito, vista));
    }

    private static async Task<IResult> QuitarPortalAsync(
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj,
        TipoFavorito tipo, Guid paqueteId, Guid? contratoId, Guid? guiaId, CancellationToken ct)
    {
        var persona = Persona(http.User);
        var vista = await acceso.VistaAsync(http.User, ct);
        var borrados = await contexto.Favoritos
            .Where(f => f.PersonaId == persona && f.OrganizacionId == vista.OrganizacionId && f.Tipo == tipo && f.PaqueteId == paqueteId
                && f.ContratoId == contratoId && f.GuiaId == guiaId)
            .ExecuteDeleteAsync(ct);
        if (borrados == 0)
        {
            return Results.Json(new ErrorPortal("no_encontrado", "No tenías ese favorito."), statusCode: StatusCodes.Status404NotFound);
        }
        await AuditarAsync(http, plataforma, reloj, AccionQuitado, vista, paqueteId.ToString(), null, new { tipo = Texto(tipo), contrato = contratoId, guia = guiaId }, ct);
        return Results.NoContent();
    }

    // --- Piezas comunes ---

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    private static string Texto(TipoFavorito tipo) => tipo switch
    {
        TipoFavorito.Contrato => "contrato_interno",
        TipoFavorito.Paquete => "paquete",
        TipoFavorito.ContratoCompartido => "contrato",
        _ => "guia",
    };

    /// <summary>El favorito de la persona (lo crea si no existía; dos marcas a la vez quedan en una por la unicidad).</summary>
    private static async Task<FavoritoContrato> GuardarAsync(
        ContratosDbContext contexto, ClaimsPrincipal usuario, TipoFavorito tipo, Guid? paquete, Guid? contrato, Guid? guia, CancellationToken ct)
    {
        var persona = Persona(usuario);
        IQueryable<FavoritoContrato> Existente() => contexto.Favoritos.AsNoTracking()
            .Where(f => f.PersonaId == persona && f.Tipo == tipo && f.PaqueteId == paquete && f.ContratoId == contrato && f.GuiaId == guia);
        if (await Existente().FirstOrDefaultAsync(ct) is { } ya)
        {
            return ya;
        }
        var organizacion = Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion)!);
        var nuevo = new FavoritoContrato { PersonaId = persona, OrganizacionId = organizacion, Tipo = tipo, PaqueteId = paquete, ContratoId = contrato, GuiaId = guia };
        contexto.Favoritos.Add(nuevo);
        try
        {
            await contexto.SaveChangesAsync(ct);
            return nuevo;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            contexto.Entry(nuevo).State = EntityState.Detached;
            return await Existente().FirstAsync(ct);
        }
    }
}
