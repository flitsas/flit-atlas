using System.Net;
using Flit.Atlas.Plataforma.Permisos;
using static Flit.Atlas.Api.Tests.EscenarioPortal;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 4.9 (ATLAS-ADR-0034): los favoritos se guardan en el servidor por persona; el listado solo trae lo que la persona ve
/// hoy (al revocar la compartición desaparece) y marcar algo que no ve responde 404 (o 403 sin el detalle técnico). El
/// aislamiento entre organizaciones de estos endpoints lo cubre <see cref="AislamientoPortalTests"/>.
/// </summary>
public sealed class FavoritosContratosTests(EntornoApi entorno)
{
    [Fact]
    public async Task Un_empleado_marca_y_quita_contratos_favoritos()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider, "Aplicativo Favorito Interno");
        var (contrato, _) = await escenario.PublicarAsync(slug, "api-favorita", Spec("API Favorita", "favorita", "Favorita"));

        var marcado = await Ok(await Enviar(lider, HttpMethod.Put, $"/api/v1/contratos/favoritos/contratos/{contrato}"));
        Assert.Equal(contrato, marcado.GetProperty("contratoId").GetGuid());
        Assert.Equal("1.0.0", marcado.GetProperty("version").GetString());
        Assert.Equal($"/contratos/{contrato}", marcado.GetProperty("ruta").GetString());
        // Idempotente.
        Assert.Equal(marcado.GetProperty("id").GetGuid(),
            (await Ok(await Enviar(lider, HttpMethod.Put, $"/api/v1/contratos/favoritos/contratos/{contrato}"))).GetProperty("id").GetGuid());
        Assert.Single((await Ok(await lider.GetAsync("/api/v1/contratos/favoritos"))).EnumerateArray());

        // Los favoritos son de cada persona.
        var (otro, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador);
        Assert.Empty((await Ok(await otro.GetAsync("/api/v1/contratos/favoritos"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lider, HttpMethod.Put, $"/api/v1/contratos/favoritos/contratos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Enviar(lider, HttpMethod.Delete, $"/api/v1/contratos/favoritos/contratos/{contrato}")).StatusCode);
        Assert.Empty((await Ok(await lider.GetAsync("/api/v1/contratos/favoritos"))).EnumerateArray());
    }

    [Fact]
    public async Task En_el_portal_solo_se_marca_y_se_lista_lo_que_la_persona_ve()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (aplicativo, slug) = await AplicativoAsync(lider, "Aplicativo Favorito Portal");
        var (responsable, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador, responsableDe: aplicativo);
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-favorita-portal", Spec("API Favorita Portal", "favoritaportal", "FavoritaPortal"));
        var guia = await GuiaCompartibleAsync(lider, responsable, contrato, "Guía Favorita Demo", "Contenido.");
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Favorito Demo", contrato, version, ["GET /favoritaportal/{id}"], guia);
        var org = await escenario.OrganizacionAsync("Organismo Demo Favoritos");
        var comparticion = await CompartirAsync(lider, paquete, org);
        var integrador = await escenario.ExternoAsync(org, RolesAtlas.Integrador);
        var funcional = await escenario.ExternoAsync(org, RolesAtlas.ClienteFuncional);
        var n = integrador.Navegador;

        await Ok(await Enviar(n, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}"));
        var deContrato = await Ok(await Enviar(n, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}/contratos/{contrato}"));
        Assert.Equal("contrato", deContrato.GetProperty("tipo").GetString());
        Assert.Equal($"/portal/paquetes/{paquete}/contratos/{contrato}", deContrato.GetProperty("ruta").GetString());
        await Ok(await Enviar(n, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}/guias/{guia}"));
        var lista = (await Ok(await n.GetAsync("/api/v1/portal/favoritos"))).EnumerateArray().ToList();
        Assert.Equal(new[] { "guia", "contrato", "paquete" }, lista.Select(f => f.GetProperty("tipo").GetString()));

        // Sin el detalle técnico, solo el paquete.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(funcional.Navegador, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}/contratos/{contrato}")).StatusCode);
        await Ok(await Enviar(funcional.Navegador, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}"));

        // Revocada la compartición: el listado ya no los trae y no se pueden marcar; quitarlos sí (la PWA borra lo guardado).
        await Ok(await Enviar(lider, HttpMethod.Delete, $"/api/v1/contratos/paquetes/{paquete}/comparticiones/{comparticion}"));
        Assert.Empty((await Ok(await n.GetAsync("/api/v1/portal/favoritos"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(n, HttpMethod.Put, $"/api/v1/portal/favoritos/paquetes/{paquete}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Enviar(n, HttpMethod.Delete, $"/api/v1/portal/favoritos/paquetes/{paquete}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(n, HttpMethod.Delete, $"/api/v1/portal/favoritos/paquetes/{paquete}")).StatusCode);

        // Compartido otra vez, los que quedaron vuelven a verse.
        await CompartirAsync(lider, paquete, org);
        Assert.Equal(2, (await Ok(await n.GetAsync("/api/v1/portal/favoritos"))).GetArrayLength());
    }
}
