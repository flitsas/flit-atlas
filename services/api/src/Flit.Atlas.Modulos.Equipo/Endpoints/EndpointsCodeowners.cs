using System.ComponentModel;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Modulos.Equipo.GitHub;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Flit.Atlas.Modulos.Equipo.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// CODEOWNERS sugerido por repo y carpeta, diferencias con el real de GitHub (solo lectura) y la «Ficha de responsables»
/// en Markdown para el README (HU 2.4, ATLAS-ADR-0031).
/// </summary>
internal static class EndpointsCodeowners
{
    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/codeowners", SugeridoAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerCodeownersSugerido")
            .WithSummary("CODEOWNERS sugerido")
            .WithDescription("Un bloque CODEOWNERS por repo, con el principal y el secundario de cada carpeta según el catálogo (repo y ruta) y el reparto vigente; marca a quien no tiene usuario de GitHub. Requiere atlas.equipo.ver.")
            .Produces<CodeownersDto>();

        grupo.MapGet("/codeowners/diferencias", DiferenciasAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("CompararCodeownersConGitHub")
            .WithSummary("Diferencias con el CODEOWNERS de GitHub")
            .WithDescription("Lee el CODEOWNERS real del repo con la API de GitHub (solo lectura; Plataforma:GitHub) y lo compara con el sugerido, patrón por patrón. Si GitHub no está configurado, lo dice y no falla. Requiere atlas.equipo.gestionar.")
            .Produces<DiferenciasCodeownersDto>()
            .ProducesValidationProblem();

        grupo.MapGet("/aplicativos/{slug}/ficha-responsables", FichaAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ObtenerFichaResponsables")
            .WithSummary("Ficha de responsables en Markdown")
            .WithDescription("La «Ficha de responsables» del aplicativo para su README: principal, secundario, cómo desplegar, a quién escalar y las definiciones de cada rol. 404 si el aplicativo no existe. Requiere atlas.equipo.ver.")
            .Produces<FichaResponsablesDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound);
    }

    private static async Task<(DatosReparto Datos, IReadOnlyList<BloqueCodeowners> Bloques)> BloquesAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        return (datos, Codeowners.Generar([.. datos.Aplicativos.Values], datos.PorAplicativo(), datos.Version?.Numero));
    }

    private static LineaCodeownersDto Linea(LineaCodeowners l) =>
        new(l.Patron, AplicativoDto(l.Aplicativo), l.Propietarios, l.SinUsuario, l.HeredadoDe is null ? null : AplicativoDto(l.HeredadoDe));

    private static async Task<IResult> SugeridoAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, ClienteGitHub github, TimeProvider reloj, CancellationToken ct)
    {
        var (datos, bloques) = await BloquesAsync(equipo, plataforma, catalogo, reloj, ct);
        var sinUsuario = datos.Responsabilidades.Where(datos.Vigente).Select(r => r.PersonaId).Distinct()
            .Where(id => !datos.UsuariosGitHub.ContainsKey(id))
            .Select(id => PersonaDto(datos, id))
            .OrderBy(p => p.Nombre, StringComparer.CurrentCulture)
            .ToList();
        return Results.Ok(new CodeownersDto(datos.Version?.Numero, github.Configurado, github.Modo,
            [.. bloques.Select(b => new BloqueCodeownersDto(b.Repo, b.Texto, [.. b.Lineas.Select(Linea)]))], sinUsuario));
    }

    private static async Task<IResult> DiferenciasAsync(
        [Description("Repo de GitHub como dueño/nombre (p. ej. flitsas/flit).")] string? repo,
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, ClienteGitHub github, TimeProvider reloj, CancellationToken ct)
    {
        var limpio = Textos.Limpiar(repo);
        if (limpio is null || limpio.Length > 140 || limpio.Count(c => c == '/') != 1 || limpio.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')))
        {
            return Invalido("repo", "El repo va como dueño/nombre (letras, números, guiones, puntos y guiones bajos).");
        }
        var (_, bloques) = await BloquesAsync(equipo, plataforma, catalogo, reloj, ct);
        var sugerido = bloques.FirstOrDefault(b => string.Equals(b.Repo, limpio, StringComparison.OrdinalIgnoreCase));
        if (!github.Configurado)
        {
            return Results.Ok(new DiferenciasCodeownersDto(limpio, false,
                "GitHub no está configurado en este ambiente (Plataforma:GitHub): se muestra solo el bloque sugerido.", null,
                [.. Codeowners.Comparar(sugerido, null).Select(Dto)]));
        }
        try
        {
            var real = await github.LeerCodeownersAsync(limpio, ct);
            return Results.Ok(new DiferenciasCodeownersDto(limpio, true,
                real.Ruta is null ? "El repo no tiene CODEOWNERS (ni en .github/, ni en la raíz, ni en docs/)." : null,
                real.Ruta, [.. Codeowners.Comparar(sugerido, real.Contenido).Select(Dto)]));
        }
        catch (Exception error) when (error is GitHubNoDisponibleException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            var mensaje = error is GitHubNoDisponibleException ? error.Message : "No se pudo hablar con GitHub; inténtalo más tarde.";
            return Results.Ok(new DiferenciasCodeownersDto(limpio, true, mensaje, null, []));
        }
    }

    private static DiferenciaDto Dto(DiferenciaCodeowners d) => new(d.Patron, d.Estado, d.EnAtlas, d.EnGitHub);

    private static async Task<IResult> FichaAsync(
        [Description("Slug del aplicativo en el catálogo.")] string slug,
        EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        var aplicativo = datos.Aplicativos.Values.FirstOrDefault(a => a.Slug == slug);
        if (aplicativo is null)
        {
            return NoEncontrado("El aplicativo no existe en el catálogo.");
        }
        var (responsables, heredado) = Codeowners.ResponsablesDe(aplicativo, datos.Aplicativos, datos.PorAplicativo());
        string? repo = null;
        for (var actual = aplicativo; actual is not null && repo is null; actual = actual.PadreId is { } p ? datos.Aplicativos.GetValueOrDefault(p) : null)
        {
            repo = actual.Repo;
        }
        var markdown = Codeowners.Ficha(aplicativo, responsables, heredado, repo,
            datos.Version is null ? [] : FotoReparto.DefinicionesDesdeJson(datos.Version.Definiciones),
            datos.Version?.Numero, datos.Version?.FechaDocumento ?? (datos.Version is null ? null : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(datos.Version.CreadoEn, CalendarioBogota.Zona).DateTime)));
        return Results.Ok(new FichaResponsablesDto(AplicativoDto(aplicativo), markdown));
    }
}
