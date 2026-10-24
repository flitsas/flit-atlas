using System.Net;
using System.Security.Claims;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Api.Tests.EscenarioPortal;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// <b>CA bloqueante de la HU 4.4</b> (prompt maestro §12, ATLAS-ADR-0043): con dos organizaciones A y B y paquetes
/// distintos, una persona de A nunca obtiene datos, specs, guías, cambios ni resultados de búsqueda de B, ni de paquetes de
/// A fuera de su alcance, ni tras revocar o vencer la compartición o su asignación. Se prueba por la API (predicado y
/// <c>AccesoPortal</c>); RLS en SQL directo lo prueba <c>PortalTests</c> del módulo con el usuario del servicio.
/// <para>
/// Cada endpoint de <c>/api/v1/portal</c> tiene su caso en <see cref="Casos"/>, y
/// <see cref="Todo_endpoint_del_portal_tiene_su_caso_de_aislamiento"/> recorre los endpoints registrados: uno nuevo sin
/// caso hace fallar la suite.
/// </para>
/// </summary>
public sealed class AislamientoPortalTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Lo que se le puede pedir al portal de un paquete: el paquete, uno de sus contratos y una de sus guías.</summary>
    private sealed record Recursos(Guid Paquete, Guid Contrato, Guid Guia, IReadOnlyList<string> Marcas);

    /// <summary>Quién pregunta, lo suyo (si aún lo tiene) y lo que no debe ver nunca.</summary>
    private sealed record Perspectiva(string Nombre, Navegador Quien, Recursos? Propio, IReadOnlyList<Recursos> Ajenos, IReadOnlyList<string> Prohibidos);

    private enum Esperado
    {
        /// <summary>200 con lo propio.</summary>
        Propio,

        /// <summary>No 200: 404 (o 401/403 si ya no tiene el permiso).</summary>
        Ajeno,

        /// <summary>Un listado: 200 (o 401/403 si ya no tiene el permiso) y nada prohibido.</summary>
        Lista,
    }

    private sealed record Llamada(string Url, Esperado Esperado);

    /// <summary>Términos que encuentran algo de cada organización y de lo que está fuera del alcance.</summary>
    private static readonly string[] Terminos = ["alfa", "fuera", "exclusiva", "demo", "api", "pagos", "obtener", "guía"];

    /// <summary>
    /// Casos de aislamiento por endpoint del portal (método y patrón, como los descubre <see cref="AutorizacionTests"/>). Cada
    /// uno pide lo propio, lo ajeno y las mezclas (paquete propio con contrato o guía ajenos y al revés). Agregar un
    /// endpoint al portal exige agregar aquí su caso.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Perspectiva, IEnumerable<Llamada>>> Casos =
        new Dictionary<string, Func<Perspectiva, IEnumerable<Llamada>>>(StringComparer.Ordinal)
        {
            ["GET /api/v1/portal/inicio"] = _ => [new("/api/v1/portal/inicio", Esperado.Lista)],
            ["GET /api/v1/portal/paquetes/{paqueteId:guid}"] = p => Combinar(p, (paquete, _, _) => $"/api/v1/portal/paquetes/{paquete}", conMezclas: false),
            ["GET /api/v1/portal/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}/spec"] =
                p => Combinar(p, (paquete, contrato, _) => $"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/spec"),
            ["GET /api/v1/portal/paquetes/{paqueteId:guid}/contratos/{contratoId:guid}/cambios"] =
                p => Combinar(p, (paquete, contrato, _) => $"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/cambios"),
            ["GET /api/v1/portal/paquetes/{paqueteId:guid}/guias/{guiaId:guid}"] =
                p => Combinar(p, (paquete, _, guia) => $"/api/v1/portal/paquetes/{paquete}/guias/{guia}"),
            ["GET /api/v1/portal/buscar"] = _ => Terminos.Select(t => new Llamada($"/api/v1/portal/buscar?q={Uri.EscapeDataString(t)}", Esperado.Lista)),
        };

    private static IEnumerable<Llamada> Combinar(Perspectiva p, Func<Guid, Guid, Guid, string> url, bool conMezclas = true)
    {
        if (p.Propio is { } propio)
        {
            yield return new(url(propio.Paquete, propio.Contrato, propio.Guia), Esperado.Propio);
        }
        foreach (var ajeno in p.Ajenos)
        {
            yield return new(url(ajeno.Paquete, ajeno.Contrato, ajeno.Guia), Esperado.Ajeno);
            if (conMezclas && p.Propio is { } mio)
            {
                yield return new(url(mio.Paquete, ajeno.Contrato, ajeno.Guia), Esperado.Ajeno);
                yield return new(url(ajeno.Paquete, mio.Contrato, mio.Guia), Esperado.Ajeno);
            }
        }
        yield return new(url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Esperado.Ajeno);
    }

    [Fact]
    public async Task Todo_endpoint_del_portal_tiene_su_caso_de_aislamiento()
    {
        await using var local = entorno.Api();
        await using var comoQa = ContratoOpenApiTests.ApiComoQa();
        var portal = (await AutorizacionTests.DescubrirAsync(local)).Concat(await AutorizacionTests.DescubrirAsync(comoQa))
            .Where(e => e.Patron.StartsWith(ExtensionesModulos.RutaPortal, StringComparison.Ordinal))
            .DistinctBy(e => e.Clave)
            .ToList();

        Assert.NotEmpty(portal);
        var sinCaso = portal.Select(e => e.Clave).Except(Casos.Keys).ToList();
        Assert.True(sinCaso.Count == 0, "Endpoints del portal sin prueba de aislamiento (agrégalos a AislamientoPortalTests.Casos): " + string.Join(", ", sinCaso));
        var sobrantes = Casos.Keys.Except(portal.Select(e => e.Clave)).ToList();
        Assert.True(sobrantes.Count == 0, "Casos de aislamiento de endpoints que ya no existen: " + string.Join(", ", sobrantes));
        // Todo el portal exige atlas.portal.ver (además de su recorte por organización y alcance).
        Assert.All(portal, e => Assert.Contains(Flit.Atlas.Modulos.Contratos.PermisosContratos.PortalVer, e.Permisos));
    }

    [Fact]
    public async Task Nadie_obtiene_del_portal_datos_de_otra_organizacion_ni_fuera_de_su_alcance_ni_tras_revocar_o_vencer()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (aplicativoA, slugA) = await AplicativoAsync(lider, "Aplicativo Alfa Demo");
        var (aplicativoB, slugB) = await AplicativoAsync(lider, "Aplicativo Exclusivo Demo");
        var (responsableA, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador, responsableDe: aplicativoA);
        var (responsableB, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador, responsableDe: aplicativoB);
        const string Changelog = """
            [{"id":"response-property-added","text":"cambio compartido","level":1,"operation":"GET","path":"/{0}/{id}"},
             {"id":"new-required-request-property","text":"cambio de pagos internos","level":3,"operation":"POST","path":"/{0}-pagos-internos"}]
            """;

        async Task<Recursos> PaqueteAsync(string slug, Navegador responsable, string codigo, string titulo, string prefijo, string esquema, string paquete, string guia)
        {
            var (contrato, version) = await escenario.PublicarAsync(slug, codigo, Spec(titulo, prefijo, esquema),
                changelog: Changelog.Replace("{0}", prefijo, StringComparison.Ordinal));
            var guiaId = await GuiaCompartibleAsync(lider, responsable, contrato, guia, $"Contenido de {guia}.");
            var paqueteId = await PaquetePublicadoAsync(lider, paquete, contrato, version, [$"GET /{prefijo}/{{id}}"], guiaId);
            return new Recursos(paqueteId, contrato, guiaId, [paqueteId.ToString(), contrato.ToString(), guiaId.ToString(), codigo, titulo, paquete, guia, $"/{prefijo}/"]);
        }

        var alfa = await PaqueteAsync(slugA, responsableA, "api-alfa", "API Alfa Demo", "alfa", "Alfa", "Paquete Alfa Demo", "Guía Alfa Demo");
        var fuera = await PaqueteAsync(slugA, responsableA, "api-fuera", "API Fuera Demo", "fuera", "Fuera", "Paquete Fuera Demo", "Guía Fuera Demo");
        var exclusivo = await PaqueteAsync(slugB, responsableB, "api-exclusiva", "API Exclusiva Demo", "exclusiva", "Exclusiva", "Paquete Exclusivo Demo", "Guía Exclusiva Demo");
        var orgA = await escenario.OrganizacionAsync("Organismo Demo A");
        var orgB = await escenario.OrganizacionAsync("Organismo Demo B");
        var alfaConA = await CompartirAsync(lider, alfa.Paquete, orgA, sandbox: true);
        await CompartirAsync(lider, fuera.Paquete, orgA);
        await CompartirAsync(lider, exclusivo.Paquete, orgB);

        // Lo que nunca sale para nadie: las operaciones no compartidas, sus esquemas y los servidores internos.
        string[] internos = ["pagos-internos", "PagoInterno", "servidor-interno", "cambio de pagos internos"];
        string[] DeOrganizacion(Guid org) => [org.ToString()];
        // A con alcance solo al paquete alfa; A con toda la organización; B con toda la suya.
        var a1 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, [(TiposAlcance.Paquete, alfa.Paquete)]);
        var aTodo = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);
        var b1 = await escenario.ExternoAsync(orgB, RolesAtlas.Integrador);
        // Un aplicativo de A en la invitación no da los paquetes de B aunque sean del mismo tipo de alcance.
        var aPorAplicativo = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, [(TiposAlcance.Aplicativo, aplicativoB)]);

        var fallas = new List<string>();
        async Task Revisar(string fase, params Perspectiva[] perspectivas)
        {
            foreach (var p in perspectivas)
            {
                foreach (var (endpoint, caso) in Casos)
                {
                    foreach (var llamada in caso(p))
                    {
                        var respuesta = await p.Quien.GetAsync(llamada.Url);
                        var cuerpo = await respuesta.Content.ReadAsStringAsync(Ct);
                        var donde = $"[{fase} · {p.Nombre}] {endpoint} → {llamada.Url}: {(int)respuesta.StatusCode}";
                        var prohibido = p.Prohibidos.Concat(internos).FirstOrDefault(m => cuerpo.Contains(m, StringComparison.OrdinalIgnoreCase));
                        if (prohibido is not null)
                        {
                            fallas.Add($"{donde} trae «{prohibido}»");
                        }
                        var estado = respuesta.StatusCode;
                        var negado = estado is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
                        switch (llamada.Esperado)
                        {
                            case Esperado.Propio when estado != HttpStatusCode.OK || !p.Propio!.Marcas.Any(m => cuerpo.Contains(m, StringComparison.OrdinalIgnoreCase)):
                                fallas.Add($"{donde}: se esperaba 200 con lo propio");
                                break;
                            case Esperado.Ajeno when !negado:
                                fallas.Add($"{donde}: se esperaba 404");
                                break;
                            case Esperado.Lista when estado != HttpStatusCode.OK && !negado:
                                fallas.Add($"{donde}: se esperaba 200 o un rechazo");
                                break;
                        }
                    }
                }
            }
        }

        // 1. Vigente: cada quien ve lo suyo y nada más.
        await Revisar("vigente",
            new("A con alcance al paquete alfa", a1.Navegador, alfa, [exclusivo, fuera], [.. exclusivo.Marcas, .. fuera.Marcas, .. DeOrganizacion(orgB)]),
            new("A con toda la organización", aTodo.Navegador, fuera, [exclusivo], [.. exclusivo.Marcas, .. DeOrganizacion(orgB)]),
            new("B con toda la organización", b1.Navegador, exclusivo, [alfa, fuera], [.. alfa.Marcas, .. fuera.Marcas, .. DeOrganizacion(orgA)]),
            new("A con alcance a un aplicativo de B", aPorAplicativo.Navegador, null, [exclusivo, alfa, fuera],
                [.. exclusivo.Marcas, .. alfa.Marcas, .. fuera.Marcas, .. DeOrganizacion(orgB)]));

        // 2. Revocada la compartición del paquete alfa con A: a1 pierde todo; aTodo conserva el otro paquete.
        await Ok(await Enviar(lider, HttpMethod.Delete, $"/api/v1/contratos/paquetes/{alfa.Paquete}/comparticiones/{alfaConA}"));
        await Revisar("revocada",
            new("A con alcance al paquete alfa", a1.Navegador, null, [alfa, exclusivo, fuera], [.. alfa.Marcas, .. exclusivo.Marcas, .. fuera.Marcas]),
            new("A con toda la organización", aTodo.Navegador, fuera, [alfa, exclusivo], [.. alfa.Marcas, .. exclusivo.Marcas]));

        // 3. Vencida la compartición de B y la asignación de aTodo (sin que nadie intervenga).
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE atlas.contratos_comparticion SET vigente_desde = now() - interval '2 days', vigente_hasta = now() - interval '1 second' WHERE paquete_id = {exclusivo.Paquete}", Ct);
            await plataforma.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE atlas.plataforma_asignacion_rol SET vigente_hasta = now() - interval '1 second' WHERE persona_id = {aTodo.PersonaId}", Ct);
        }
        await Revisar("vencida",
            new("B con la compartición vencida", b1.Navegador, null, [exclusivo, alfa, fuera], [.. exclusivo.Marcas, .. alfa.Marcas, .. fuera.Marcas]),
            new("A con la asignación vencida", aTodo.Navegador, null, [fuera, alfa, exclusivo], [.. fuera.Marcas, .. alfa.Marcas, .. exclusivo.Marcas]));

        Assert.True(fallas.Count == 0, string.Join('\n', fallas));
    }

    [Fact]
    public async Task Los_proveedores_de_la_busqueda_global_no_devuelven_nada_de_otra_organizacion_a_un_externo()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider, "Aplicativo Exclusivo Busqueda");
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-exclusiva-busqueda", Spec("API Exclusiva Busqueda", "exclusivabusqueda", "ExclusivaBusqueda"));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Exclusivo Busqueda", contrato, version, ["GET /exclusivabusqueda/{id}"]);
        var orgA = await escenario.OrganizacionAsync("Organismo Demo Busqueda A");
        var orgB = await escenario.OrganizacionAsync("Organismo Demo Busqueda B");
        await CompartirAsync(lider, paquete, orgB);
        var a1 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);

        // Los proveedores con la sesión del externo de A (su alcance de datos es su organización).
        await using var alcance = escenario.Api.Services.CreateAsyncScope();
        var accesor = alcance.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimsAtlas.PersonaId, a1.PersonaId.ToString()), new Claim(ClaimsAtlas.OrganizacionId, orgA.ToString()),
            new Claim(ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno),
        ], ClaimsAtlas.EsquemaCookie));
        accesor.HttpContext = new DefaultHttpContext { User = principal, RequestServices = alcance.ServiceProvider };
        var proveedores = alcance.ServiceProvider.GetServices<IProveedorBusqueda>().ToList();
        Assert.NotEmpty(proveedores);
        foreach (var proveedor in proveedores)
        {
            foreach (var termino in new[] { "exclusiva", "busqueda", "api", "demo", "organismo" })
            {
                var resultados = await proveedor.BuscarAsync(new ConsultaBusqueda(termino, principal, 50), Ct);
                Assert.True(resultados.Count == 0, $"{proveedor.Proveedor} devolvió {resultados.Count} resultados a un externo para «{termino}»");
            }
        }
        // Y la búsqueda del portal tampoco trae lo de B.
        var busqueda = await Ok(await a1.Navegador.GetAsync("/api/v1/portal/buscar?q=exclusiva"));
        Assert.Empty(busqueda.EnumerateArray());
    }
}
