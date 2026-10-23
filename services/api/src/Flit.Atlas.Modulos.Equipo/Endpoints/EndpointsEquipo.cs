using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Equipo.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// Endpoints de <c>/api/v1/equipo</c> (HU 2.3, 2.4 y 2.5, ATLAS-ADR-0031). El host ya puso la etiqueta, la sesión y la
/// bandera. Leer exige <c>atlas.equipo.ver</c> (todo empleado); el teléfono, además, <c>atlas.equipo.ver_contacto</c>.
/// Cambiar el reparto, importar, restaurar, traspasos, revisiones, usuarios de GitHub y leer el CODEOWNERS real exigen
/// <c>atlas.equipo.gestionar</c>. Toda escritura queda en <c>plataforma_auditoria</c> en la misma transacción.
/// </summary>
internal static class EndpointsEquipo
{
    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/responsabilidades", RepartoAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerRepartoResponsables")
            .WithSummary("Reparto de responsables vigente")
            .WithDescription("Definiciones de cada rol, reparto por persona, por producto, por módulo y por servicio, con dedicación, vigencia y traspaso. Requiere atlas.equipo.ver; el teléfono, atlas.equipo.ver_contacto.")
            .Produces<RepartoDto>();

        grupo.MapGet("/responsabilidades/tablas", TablasAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerTablasReparto")
            .WithSummary("Tablas del reparto en Markdown")
            .WithDescription("Las tablas 2, 3.1 y 3.2 del documento de responsables reconstruidas desde los datos, con el mismo formato, para comparar o copiar. Requiere atlas.equipo.ver.")
            .Produces<TablasRepartoDto>();

        grupo.MapGet("/personas/{personaId:guid}/responsabilidades", DePersonaAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerResponsabilidadesDePersona")
            .WithSummary("Responsabilidades de una persona")
            .WithDescription("De qué aplicativos es principal y de cuáles secundaria, con su traspaso. 404 si la persona no es un empleado. Requiere atlas.equipo.ver.")
            .Produces<ResponsabilidadesPersonaDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);

        grupo.MapGet("/yo/responsabilidades", MiasAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerMisResponsabilidades")
            .WithSummary("Mis responsabilidades")
            .WithDescription("Las responsabilidades de la persona de la sesión y los traspasos que acompaña como principal (widget de Inicio). Requiere atlas.equipo.ver.")
            .Produces<ResponsabilidadesPersonaDto>();

        grupo.MapGet("/aplicativos/{slug}/responsables", DeAplicativoAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerResponsablesDeAplicativo")
            .WithSummary("Responsables de un aplicativo")
            .WithDescription("Principal y secundario del aplicativo (o, si no tiene, los del ancestro más cercano), las definiciones de cada rol y el traspaso del secundario. 404 si el aplicativo no existe. Requiere atlas.equipo.ver.")
            .Produces<ResponsablesAplicativoDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);

        grupo.MapGet("/aplicativos/{slug}/a-quien-llamo", AQuienLlamoAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerAQuienLlamo")
            .WithSummary("¿A quién llamo?")
            .WithDescription("Si hoy es sábado (Bogotá), el soporte de turno de cada equipo; además, el principal y el secundario del aplicativo. El contacto completo solo con atlas.equipo.ver_contacto. 404 si el aplicativo no existe. Requiere atlas.equipo.ver.")
            .Produces<AQuienLlamoDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);

        grupo.MapGet("/carga", CargaAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerMapaCarga")
            .WithSummary("Mapa de carga")
            .WithDescription("Cuántas responsabilidades tiene cada persona (principal cuenta 1; secundaria, su dedicación) y quién se sale de la media en más de un 30 %. Requiere atlas.equipo.ver.")
            .Produces<MapaCargaDto>();

        EndpointsHistorial.Mapear(grupo);
        EndpointsGestion.Mapear(grupo);
        EndpointsCodeowners.Mapear(grupo);
    }

    private static async Task<IResult> RepartoAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct);
        var autores = datos.Version is null ? [] : await AutoresAsync(plataforma, [datos.Version], ct);
        var foto = datos.Foto();
        var porId = datos.Responsabilidades.ToDictionary(r => (r.AplicativoId, Textos.De(r.Tipo)));
        IReadOnlyList<FilaRepartoDto> Filas(string tipoAplicativo) =>
        [
            .. foto.Ordenadas().Where(r => r.TipoAplicativo == tipoAplicativo).Select(r => r.AplicativoId).Distinct().Select(id => new FilaRepartoDto(
                AplicativoDto(datos.Aplicativos[id]),
                porId.TryGetValue((id, "principal"), out var p) ? ResponsableDto(datos, p) : null,
                porId.TryGetValue((id, "secundario"), out var s) ? ResponsableDto(datos, s) : null)),
        ];
        var porPersona = foto.Responsabilidades.GroupBy(r => r.PersonaId)
            .OrderBy(g => g.Min(r => r.OrdenPersona)).ThenBy(g => g.First().Persona, StringComparer.CurrentCulture)
            .Select(g => new PersonaRepartoDto(PersonaDto(datos, g.Key),
                [.. g.Where(r => r.Tipo == "principal").OrderBy(r => r.OrdenEnPersona).Select(r => AplicativoDto(datos.Aplicativos[r.AplicativoId]))],
                [.. g.Where(r => r.Tipo == "secundario").OrderBy(r => r.OrdenEnPersona).Select(r => AplicativoDto(datos.Aplicativos[r.AplicativoId]))]))
            .ToList();
        return Results.Ok(new RepartoDto(datos.Version is null ? null : VersionDto(datos.Version, autores), Definiciones(datos.Version),
            porPersona, Filas("producto"), Filas("modulo"), Filas("servicio"), datos.Huerfanas, datos.VeContacto,
            await GestionaAsync(permisos, usuario, ct)));
    }

    private static async Task<IResult> TablasAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        return Results.Ok(Tablas(datos.Version?.Numero, datos.Foto()));
    }

    public static TablasRepartoDto Tablas(int? version, FotoReparto foto)
    {
        var tablas = TablasReparto.Generar(foto);
        return new TablasRepartoDto(version, tablas.Personas, tablas.Productos, tablas.Modulos, tablas.Servicios, tablas.Completo());
    }

    private static async Task<IResult> DePersonaAsync(
        [Description("Id de la persona.")] Guid personaId,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        if (!await plataforma.Personas.AsNoTracking().AnyAsync(p => p.Id == personaId && p.Tipo == TipoPersona.Empleado, ct))
        {
            return NoEncontrado("La persona no existe o no es un empleado.");
        }
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct, [personaId]);
        return Results.Ok(DePersona(datos, personaId));
    }

    private static async Task<IResult> MiasAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var yo = PersonaDeSesion(usuario);
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct, [yo]);
        return Results.Ok(DePersona(datos, yo));
    }

    private static ResponsabilidadesPersonaDto DePersona(DatosReparto datos, Guid personaId)
    {
        var propias = datos.Responsabilidades.Where(r => r.PersonaId == personaId && datos.Aplicativos.ContainsKey(r.AplicativoId))
            .OrderBy(r => r.OrdenEnPersona).ThenBy(r => r.OrdenAplicativo).ToList();
        IReadOnlyList<ResponsabilidadDePersonaDto> De(TipoResponsabilidad tipo) =>
            [.. propias.Where(r => r.Tipo == tipo).Select(r => new ResponsabilidadDePersonaDto(AplicativoDto(datos.Aplicativos[r.AplicativoId]), ResponsableDto(datos, r)))];
        return new ResponsabilidadesPersonaDto(PersonaDto(datos, personaId), De(TipoResponsabilidad.Principal), De(TipoResponsabilidad.Secundario));
    }

    /// <summary>Los responsables propios del aplicativo o, si no tiene, los del ancestro más cercano que los tenga.</summary>
    internal static (Responsabilidad? Principal, Responsabilidad? Secundario, AplicativoLeido? HeredadoDe) ResponsablesDe(DatosReparto datos, AplicativoLeido aplicativo)
    {
        for (var actual = aplicativo; actual is not null;
             actual = actual.PadreId is { } padre ? datos.Aplicativos.GetValueOrDefault(padre) : null)
        {
            var propias = datos.Responsabilidades.Where(r => r.AplicativoId == actual.Id).ToList();
            if (propias.Count > 0)
            {
                return (propias.FirstOrDefault(r => r.Tipo == TipoResponsabilidad.Principal),
                    propias.FirstOrDefault(r => r.Tipo == TipoResponsabilidad.Secundario),
                    actual.Id == aplicativo.Id ? null : actual);
            }
        }
        return (null, null, null);
    }

    private static async Task<IResult> DeAplicativoAsync(
        [Description("Slug del aplicativo en el catálogo.")] string slug,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct);
        var aplicativo = datos.Aplicativos.Values.FirstOrDefault(a => a.Slug == slug);
        if (aplicativo is null)
        {
            return NoEncontrado("El aplicativo no existe en el catálogo.");
        }
        var (principal, secundario, heredado) = ResponsablesDe(datos, aplicativo);
        var traspaso = secundario is { EstadoTraspaso: EstadoTraspaso.EnAcompanamiento } && heredado is null
            ? await EndpointsGestion.TraspasoAsync(equipo, datos, secundario, principal, ct)
            : null;
        return Results.Ok(new ResponsablesAplicativoDto(AplicativoDto(aplicativo),
            principal is null ? null : ResponsableDto(datos, principal), secundario is null ? null : ResponsableDto(datos, secundario),
            heredado is null ? null : AplicativoDto(heredado), Definiciones(datos.Version), traspaso, datos.Version?.Numero,
            await GestionaAsync(permisos, usuario, ct), datos.VeContacto));
    }

    private static async Task<IResult> AQuienLlamoAsync(
        [Description("Slug del aplicativo en el catálogo.")] string slug,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, ISoporteDeTurno soporte, TimeProvider reloj, CancellationToken ct)
    {
        var veContacto = await VeContactoAsync(permisos, usuario, ct);
        var hoy = CalendarioBogota.Hoy(reloj);
        var esSabado = hoy.DayOfWeek == DayOfWeek.Saturday;
        var deTurno = esSabado ? await soporte.DelDiaAsync(hoy, ct) : null;
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, veContacto, ct,
            deTurno?.Equipos.SelectMany(e => e.Personas) ?? []);
        var aplicativo = datos.Aplicativos.Values.FirstOrDefault(a => a.Slug == slug);
        if (aplicativo is null)
        {
            return NoEncontrado("El aplicativo no existe en el catálogo.");
        }
        var (principal, secundario, heredado) = ResponsablesDe(datos, aplicativo);
        return Results.Ok(new AQuienLlamoDto(AplicativoDto(aplicativo), hoy, esSabado,
            deTurno is null ? null : new SoporteHoyDto(deTurno.Fecha, deTurno.Festivo,
                [.. deTurno.Equipos.Select(e => new EquipoTurnoDto(e.Id, e.Nombre, e.Area, e.HoraInicio, e.HoraFin, [.. e.Personas.Select(p => PersonaDto(datos, p))]))]),
            principal is null ? null : ResponsableDto(datos, principal), secundario is null ? null : ResponsableDto(datos, secundario),
            heredado is null ? null : AplicativoDto(heredado), veContacto));
    }

    private static async Task<IResult> CargaAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct);
        var vigentes = new FotoReparto([.. datos.Foto().Responsabilidades.Where(r => r.VigenteDesde <= datos.Hoy && (r.VigenteHasta is null || r.VigenteHasta >= datos.Hoy))]);
        var (media, personas) = MapaCarga.Calcular(vigentes);
        return Results.Ok(new MapaCargaDto(media, Math.Round(media * (1 + MapaCarga.Margen), 2), Math.Round(media * (1 - MapaCarga.Margen), 2),
            [.. personas.Select(p => new CargaPersonaDto(PersonaDto(datos, p.PersonaId), p.Principales, p.Secundarias, p.Puntaje, p.Nivel))]));
    }
}
