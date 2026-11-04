using System.Net;
using System.Net.Http.Json;
using Flit.Atlas.Modulos.Hola;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 1.9: el módulo de ejemplo «hola» montado por el host desde la lista única de módulos (ATLAS-ADR-0026): bandera
/// apagada por defecto (404 en el servidor), permisos por endpoint y aislamiento por organización. Cada prueba usa su
/// base: encender la bandera no debe tocar a las demás pruebas.
/// </summary>
public class ModuloHolaTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record SaludoLeido(Guid Id, string Texto, Guid OrganizacionId, DateTimeOffset CreadoEn);

    private static async Task<Navegador> EmpleadoAsync(WebApplicationFactory<Program> api, params Guid[] grupos)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::19:{Random.Shared.Next(1, 0xffff):x}";
        var respuesta = await navegador.EntrarSimuladoAsync($"hola.{Guid.NewGuid():N}@ejemplo.com", "Empleada Demo", grupos);
        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        return navegador;
    }

    private async Task EncenderAsync(string baseDeDatos, params Guid[] organizaciones)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var bandera = await plataforma.Modulos.SingleAsync(m => m.Id == ModuloHola.IdModulo, Ct);
        bandera.Activo = true;
        bandera.OrganizacionesHabilitadas = [.. organizaciones];
        await plataforma.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Apagado_por_defecto_el_servidor_responde_404_y_la_bandera_lo_dice()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        using var lector = await EmpleadoAsync(api);

        var banderas = await (await lector.GetAsync("/api/v1/plataforma/banderas")).Content.ReadFromJsonAsync<Dictionary<string, bool>>(Ct);
        var saludos = await lector.GetAsync("/api/v1/hola/saludos");
        var yo = await (await lector.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<Identidad.EndpointsIdentidad.Yo>(Ct);

        Assert.False(banderas!["modulo.hola"]);
        Assert.Equal(HttpStatusCode.NotFound, saludos.StatusCode);
        // Los permisos existen aunque el módulo esté apagado: la PWA lo oculta por la bandera.
        Assert.Contains(PermisosHola.Ver, yo!.Permisos);
    }

    [Fact]
    public async Task Encendido_cada_endpoint_exige_su_permiso_y_un_externo_solo_ve_su_organizacion()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await EncenderAsync(baseDeDatos);
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        using var desarrollador = await EmpleadoAsync(api, await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, baseDeDatos));
        using var lector = await EmpleadoAsync(api);

        var creado = await desarrollador.EnviarAsync(HttpMethod.Post, "/api/v1/hola/saludos", JsonContent.Create(new { texto = "  Hola, Organismo Demo  " }));
        var vacio = await desarrollador.EnviarAsync(HttpMethod.Post, "/api/v1/hola/saludos", JsonContent.Create(new { texto = " " }));
        var sinPermiso = await lector.EnviarAsync(HttpMethod.Post, "/api/v1/hola/saludos", JsonContent.Create(new { texto = "Hola" }));

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var saludo = (await creado.Content.ReadFromJsonAsync<SaludoLeido>(Ct))!;
        Assert.Equal("Hola, Organismo Demo", saludo.Texto);
        Assert.Equal($"/api/v1/hola/saludos/{saludo.Id}", creado.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);

        var delLector = await (await lector.GetAsync("/api/v1/hola/saludos")).Content.ReadFromJsonAsync<List<SaludoLeido>>(Ct);
        Assert.Equal([saludo.Id], delLector!.Select(s => s.Id));

        // Un integrador de otra organización tiene atlas.hola.ver, pero no ve los saludos de la organización interna.
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddHours(1), baseDeDatos);
        using var navegadorExterno = Navegador.De(api);
        navegadorExterno.Ip = $"2001:db8::19:{Random.Shared.Next(1, 0xffff):x}";
        Assert.Equal(HttpStatusCode.NoContent, (await navegadorExterno.EntrarComoAsync(externo.UsuarioId)).StatusCode);

        var delExterno = await (await navegadorExterno.GetAsync("/api/v1/hola/saludos")).Content.ReadFromJsonAsync<List<SaludoLeido>>(Ct);
        Assert.Empty(delExterno!);
        Assert.Equal(HttpStatusCode.NotFound, (await navegadorExterno.GetAsync($"/api/v1/hola/saludos/{saludo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"/api/v1/hola/saludos/{saludo.Id}")).StatusCode);
    }

    [Fact]
    public async Task La_bandera_se_puede_encender_solo_para_algunas_organizaciones()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddHours(1), baseDeDatos);
        await EncenderAsync(baseDeDatos, externo.OrganizacionId);
        using var empleado = await EmpleadoAsync(api);
        using var navegadorExterno = Navegador.De(api);
        navegadorExterno.Ip = $"2001:db8::19:{Random.Shared.Next(1, 0xffff):x}";
        await navegadorExterno.EntrarComoAsync(externo.UsuarioId);

        Assert.Equal(HttpStatusCode.NotFound, (await empleado.GetAsync("/api/v1/hola/saludos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegadorExterno.GetAsync("/api/v1/hola/saludos")).StatusCode);
    }
}
