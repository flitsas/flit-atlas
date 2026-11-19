using System.Net;
using System.Net.Http.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

public class IdentidadTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"empleado.{Guid.NewGuid():N}@ejemplo.com";

    [Fact]
    public async Task El_ingreso_emite_una_cookie_host_httponly_secure_y_samesite_strict_sin_claims()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();

        var respuesta = await navegador.EntrarSimuladoAsync(correo, "Empleada Demo");

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        var cookie = Assert.Single(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-atlas=", StringComparison.Ordinal));
        var atributos = cookie.ToLowerInvariant();
        Assert.Contains("; secure", atributos, StringComparison.Ordinal);
        Assert.Contains("; httponly", atributos, StringComparison.Ordinal);
        Assert.Contains("; samesite=strict", atributos, StringComparison.Ordinal);
        Assert.Contains("; path=/", atributos, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", atributos, StringComparison.Ordinal);
        // La cookie solo lleva el id de la sesión cifrado: los claims viven en el servidor (ATLAS-ADR-0004 §6).
        Assert.True(navegador.Cookie!.Length < 400, $"La cookie mide {navegador.Cookie.Length} caracteres");
    }

    [Fact]
    public async Task El_primer_ingreso_crea_la_persona_y_el_segundo_la_reutiliza()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();

        await navegador.EntrarSimuladoAsync(correo, "Empleado Demo");
        var primero = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        await navegador.EntrarSimuladoAsync(correo, "Empleado Demo Actualizado");
        var segundo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);

        Assert.NotNull(primero);
        Assert.NotNull(segundo);
        Assert.Equal(ClaimsAtlas.TipoEmpleado, primero.TipoPrincipal);
        Assert.Equal("simulado", primero.Proveedor);
        Assert.Equal(primero.PersonaId, segundo.PersonaId);
        Assert.Equal(primero.Sub, segundo.Sub);
        Assert.Equal("Empleado Demo Actualizado", segundo.Nombre);

        await using var plataforma = entorno.Plataforma();
        var persona = await plataforma.Personas.SingleAsync(p => p.Correo == correo, Ct);
        Assert.Equal(TipoPersona.Empleado, persona.Tipo);
        var interna = await plataforma.Organizaciones.SingleAsync(o => o.Id == persona.OrganizacionId, Ct);
        Assert.Equal(TipoOrganizacion.Interna, interna.Tipo);
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "identidad.inicio_sesion" && a.ActorId == persona.Id.ToString(), Ct));
    }

    [Fact]
    public async Task Varios_primeros_ingresos_a_la_vez_no_fallan_ni_duplican_la_organizacion_interna()
    {
        // En el CI (Linux) el mismo correo en dos pestañas respondía 409: el candado por cuenta lo evita.
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        var correos = Enumerable.Range(0, 6).Select(_ => CorreoNuevo()).ToList();
        // Además de seis personas distintas, la primera entra tres veces a la vez (varias pestañas).
        correos.AddRange([correos[0], correos[0]]);

        var respuestas = await Task.WhenAll(correos.Select(async correo =>
        {
            using var navegador = Navegador.De(api);
            return (await navegador.EntrarSimuladoAsync(correo, "Primer Dia")).StatusCode;
        }));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.NoContent, r));
        await using var plataforma = entorno.Plataforma(baseNueva);
        Assert.Equal(1, await plataforma.Organizaciones.CountAsync(o => o.Tipo == TipoOrganizacion.Interna, Ct));
        Assert.Equal(6, await plataforma.Personas.CountAsync(Ct));
    }

    [Fact]
    public async Task Los_roles_salen_de_los_grupos_de_Entra_y_se_quitan_al_salir_del_grupo()
    {
        var grupo = await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador);
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();

        await navegador.EntrarSimuladoAsync(correo, "Con Grupo", grupo);
        var conGrupo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        await navegador.EntrarSimuladoAsync(correo, "Con Grupo");
        var sinGrupo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);

        // Todo empleado tiene atlas.lector; el grupo suma atlas.desarrollador y sus permisos (ATLAS-ADR-0023).
        Assert.Equal([RolesAtlas.Desarrollador, RolesAtlas.Lector], conGrupo!.Roles);
        Assert.Contains("atlas.contratos.ver_internos", conGrupo.Permisos);
        Assert.Equal([RolesAtlas.Lector], sinGrupo!.Roles);
        Assert.DoesNotContain("atlas.contratos.ver_internos", sinGrupo.Permisos);
        Assert.Contains("atlas.catalogo.ver", sinGrupo.Permisos);
    }

    [Fact]
    public async Task Cerrar_sesion_la_revoca_en_el_servidor_y_pide_al_navegador_borrar_sus_datos()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Sale Demo");
        var cookieVieja = navegador.Cookie;

        var salida = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");

        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);
        Assert.Equal("\"cache\", \"cookies\", \"storage\"", Assert.Single(salida.Headers.GetValues("Clear-Site-Data")));
        Assert.Null(navegador.Cookie);

        // Una copia de la cookie anterior ya no sirve: la sesión quedó revocada en identidad_sesion.
        navegador.Cookie = cookieVieja;
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_la_api_responde_401_y_no_redirige()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.GetAsync("/api/v1/plataforma/yo");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Null(respuesta.Headers.Location);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://sitio-ajeno.ejemplo.com")]
    public async Task Las_peticiones_que_cambian_estado_exigen_la_cabecera_y_el_mismo_origen(bool csrf, string? origen)
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/simulado/entrar",
            JsonContent.Create(new { correo = CorreoNuevo(), nombre = "Ataque" }), csrf, origen);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Null(navegador.Cookie);
    }

    [Fact]
    public async Task El_retorno_de_Entra_no_pasa_por_la_antifalsificacion()
    {
        // Microsoft entrega el código con un POST desde su origen y sin la cabecera propia de Atlas.
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.EnviarAsync(HttpMethod.Post, EndpointsIdentidad.RutaRetornoEntra,
            new FormUrlEncodedContent([new("code", "x"), new("state", "x")]), csrf: false, origen: "https://login.microsoftonline.com");

        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Los_proveedores_reflejan_la_configuracion()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);

        var proveedores = await (await navegador.GetAsync("/api/v1/identidad/proveedores")).Content.ReadFromJsonAsync<Dictionary<string, bool>>(Ct);

        Assert.Equal(new Dictionary<string, bool> { ["microsoft"] = false, ["simulado"] = true }, proveedores);
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("pdn")]
    public void El_proveedor_simulado_no_arranca_en_qa_ni_en_pdn(string ambiente)
    {
        using var api = entorno.Api(ambiente, simulado: true);

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());

        Assert.Contains("Identidad__Simulado__Habilitado", error.Message, StringComparison.Ordinal);
        Assert.Contains(ambiente, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_qa_sin_Entra_el_arranque_nombra_las_variables_que_faltan()
    {
        using var api = entorno.Api("qa", simulado: false);

        var error = Assert.Throws<ConfiguracionFaltanteException>(() => api.CreateClient());

        Assert.Equal(["Identidad:Entra:TenantId", "Identidad:Entra:ClientId"], error.Claves);
    }

    [Fact]
    public async Task Sin_el_proveedor_simulado_su_endpoint_no_existe()
    {
        await using var api = entorno.Api(simulado: false);
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Nadie");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
