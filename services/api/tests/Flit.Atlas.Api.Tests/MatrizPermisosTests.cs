using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Flit.Atlas.Composicion;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// CA de la HU 1.4: la matriz rol × permiso sale del código, se publica en <c>GET /api/v1/plataforma/permisos/matriz</c>
/// y su copia en <c>docs/permisos/matriz-rol-permiso.md</c> coincide con el código. Para regenerarla:
/// <c>ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests</c>.
/// </summary>
public class MatrizPermisosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string RutaMatriz => Path.Combine(Repositorio.Raiz, "docs", "permisos", "matriz-rol-permiso.md");

    [Fact]
    public async Task La_matriz_versionada_coincide_con_la_del_codigo()
    {
        await using var api = ContratoOpenApiTests.ApiComoQa();
        var generada = api.Services.GetRequiredService<CatalogoPermisos>().MatrizEnMarkdown();

        if (Environment.GetEnvironmentVariable("ATLAS_ACTUALIZAR_CONTRATOS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaMatriz)!);
            await File.WriteAllTextAsync(RutaMatriz, generada, Ct);
            return;
        }
        Assert.True(File.Exists(RutaMatriz), $"No existe {RutaMatriz}. Genérala con ATLAS_ACTUALIZAR_CONTRATOS=1.");
        Assert.True((await File.ReadAllTextAsync(RutaMatriz, Ct)).ReplaceLineEndings("\n") == generada,
            "docs/permisos/matriz-rol-permiso.md no coincide con el código. Regenérala con ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test " +
            "--project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests.");
    }

    [Fact]
    public async Task Atlas_api_y_el_migrador_usan_los_mismos_manifiestos()
    {
        await using var api = ContratoOpenApiTests.ApiComoQa();
        var deLaApi = api.Services.GetRequiredService<CatalogoPermisos>();
        var delMigrador = CatalogoPermisos.Crear(ModulosAtlas.Manifiestos);

        Assert.Equal(delMigrador.MatrizEnMarkdown(), deLaApi.MatrizEnMarkdown());
    }

    [Fact]
    public async Task El_endpoint_publica_la_matriz_y_la_base_quedo_igual_al_codigo()
    {
        await using var api = entorno.Api();
        using var auditor = Navegador.De(api);
        auditor.Ip = "2001:db8::31:1";
        await auditor.EntrarSimuladoAsync($"auditor.{Guid.NewGuid():N}@ejemplo.com", "Auditora Demo", await entorno.GrupoConRolAsync(RolesAtlas.Auditor));
        var catalogo = api.Services.GetRequiredService<CatalogoPermisos>();

        var respuesta = await auditor.GetAsync("/api/v1/plataforma/permisos/matriz");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var matriz = (await respuesta.Content.ReadFromJsonAsync<MatrizPermisos>(Ct))!;
        Assert.Equal(catalogo.Roles.Select(r => r.Codigo), matriz.Roles.Select(r => r.Codigo));
        var esperadas = catalogo.Matriz().Modulos.SelectMany(m => m.Permisos).ToDictionary(p => p.Codigo, p => p.Roles);
        Assert.All(matriz.Modulos.SelectMany(m => m.Permisos), p => Assert.Equal(esperadas[p.Codigo], p.Roles));

        await using var plataforma = entorno.Plataforma();
        var enBase = await plataforma.RolesPermisos.Join(plataforma.Roles, rp => rp.RolId, r => r.Id, (rp, r) => r.Codigo + " " + rp.PermisoCodigo)
            .ToListAsync(Ct);
        var enCodigo = catalogo.Roles.SelectMany(r => catalogo.PermisosDe(r.Codigo).Select(p => $"{r.Codigo} {p}"));
        Assert.Equal(enCodigo.Order(StringComparer.Ordinal), enBase.Order(StringComparer.Ordinal));
    }

    private static ClaimsPrincipal Principal(params (string Tipo, string Valor)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Tipo, c.Valor)), ClaimsAtlas.EsquemaCookie));

    [Fact]
    public void El_alcance_de_datos_de_un_externo_es_su_organizacion_y_el_de_un_empleado_todas()
    {
        var organizacion = Guid.NewGuid();

        var externo = AlcanceDatosDeSesion.De(Principal((ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno), (ClaimsAtlas.OrganizacionId, organizacion.ToString())));
        var externoSinOrganizacion = AlcanceDatosDeSesion.De(Principal((ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno)));
        var empleado = AlcanceDatosDeSesion.De(Principal((ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoEmpleado)));
        var sinSesion = AlcanceDatosDeSesion.De(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(externo.Total);
        Assert.Equal([organizacion], externo.Organizaciones);
        Assert.Equal(AlcanceDatos.Ninguno, externoSinOrganizacion);
        Assert.True(empleado.Total);
        // ATLAS-ADR-0050 (F4-SEC-10): sin sesión no se ve nada; el sistema se pide explícitamente.
        Assert.Equal(AlcanceDatos.Ninguno, sinSesion);
    }

    [Fact]
    public void Sin_sesion_el_alcance_del_sistema_solo_lo_dan_el_endpoint_que_lo_pide_y_la_falta_de_peticion()
    {
        static HttpContext Peticion(ClaimsPrincipal usuario, bool pideSistema)
        {
            var http = new DefaultHttpContext { User = usuario };
            var metadata = pideSistema ? new EndpointMetadataCollection(AlcanceDelSistemaSinSesion.Instancia) : EndpointMetadataCollection.Empty;
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "prueba"));
            return http;
        }
        var organizacion = Guid.NewGuid();
        var externo = Principal((ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno), (ClaimsAtlas.OrganizacionId, organizacion.ToString()),
            (ClaimsAtlas.PersonaId, Guid.NewGuid().ToString()));
        // Autenticado pero sin persona de Atlas (un principal a medio ingreso): cuenta como sin sesión.
        var aMedias = Principal((ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoExterno), (ClaimsAtlas.OrganizacionId, organizacion.ToString()));
        var anonimo = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(AlcanceDatos.Sistema, AlcanceDatosDeSesion.De((HttpContext?)null));
        Assert.Equal(AlcanceDatos.Ninguno, AlcanceDatosDeSesion.De(Peticion(anonimo, pideSistema: false)));
        Assert.Equal(AlcanceDatos.Sistema, AlcanceDatosDeSesion.De(Peticion(anonimo, pideSistema: true)));
        Assert.Equal([organizacion], AlcanceDatosDeSesion.De(Peticion(externo, pideSistema: true)).Organizaciones);
        Assert.False(AlcanceDatosDeSesion.De(Peticion(externo, pideSistema: true)).Total);
        Assert.Equal(AlcanceDatos.Sistema, AlcanceDatosDeSesion.De(Peticion(aMedias, pideSistema: true)));
        Assert.Equal([organizacion], AlcanceDatosDeSesion.De(Peticion(aMedias, pideSistema: false)).Organizaciones);
    }
}
