using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Correo;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// F4-SEC-31 (ATLAS-ADR-0049 §10): seguridad de la propia cuenta local. Cambiar la contraseña, generar códigos de
/// recuperación y agregar una passkey exigen sesión y reautenticación; la contraseña, además, la actual; cada cambio queda
/// en la auditoría y llega un aviso al titular sin enlaces. La lista local de contraseñas filtradas tiene más de 3000.
/// </summary>
public partial class CuentasExternasTests
{
    /// <summary>Tipos de evento de la seguridad de la propia cuenta (F4-SEC-31), probados con su auditoría en este archivo.</summary>
    public static readonly string[] TiposDeEventoSeguridadCuenta = [TiposEventoSeguridad.ContrasenaCambiada];

    private const string RutaCuenta = "/api/v1/identidad/cuenta";
    private const string ContrasenaNueva = "Nevado del Ruiz en la tarde 2026";

    private static Task<HttpResponseMessage> CambiarContrasenaAsync(Navegador navegador, string actual, string nueva) =>
        PostAsync(navegador, $"{RutaCuenta}/contrasena", new { contrasenaActual = actual, contrasenaNueva = nueva });

    private static async Task<string> ErrorAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error;

    /// <summary>Cuenta con contraseña y una sesión abierta con ella (el TOTP del paso siguiente al de la aceptación).</summary>
    private async Task<(string Correo, string Clave, CuentaLista Cuenta, Navegador Sesion)> CuentaConSesionAsync(WebApplicationFactory<Program> api)
    {
        var (correo, clave, cuenta) = await CuentaConContrasenaAsync(api);
        var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await EntrarConContrasenaAsync(navegador, correo, clave, desfase: 1)).StatusCode);
        return (correo, clave, cuenta, navegador);
    }

    private static void SinEnlaces(MensajeCorreo aviso)
    {
        Assert.DoesNotContain("http", aviso.Texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", aviso.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_pide_la_actual_cierra_las_demas_sesiones_y_avisa_al_titular_sin_enlaces()
    {
        await using var api = entorno.Api();
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var (correo, clave, _, sesion) = await CuentaConSesionAsync(api);
        using var _s = sesion;
        using var otra = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await EntrarConContrasenaAsync(otra, correo, clave, desfase: 2)).StatusCode);

        var errada = await CambiarContrasenaAsync(sesion, "no es la contraseña actual", ContrasenaNueva);
        Assert.Equal(HttpStatusCode.BadRequest, errada.StatusCode);
        Assert.Equal("contrasena_actual_invalida", await ErrorAsync(errada));
        foreach (var mala in new[] { "corta", "password1234", "qwertyqwerty", Contrasena })
        {
            var rechazo = await CambiarContrasenaAsync(sesion, Contrasena, mala);
            Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
            Assert.Equal("contrasena_invalida", await ErrorAsync(rechazo));
        }
        var correosAntes = buzon.Para(correo).Count;

        Assert.Equal(HttpStatusCode.NoContent, (await CambiarContrasenaAsync(sesion, Contrasena, ContrasenaNueva)).StatusCode);

        // Esta sesión sigue (renovada con el sello nuevo); la otra y la contraseña vieja ya no sirven.
        Assert.Equal(HttpStatusCode.OK, (await sesion.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otra.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using var conLaVieja = Publico(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(conLaVieja, RutaIngreso, new { correo, contrasena = Contrasena })).StatusCode);
        using var conLaNueva = Publico(api);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(conLaNueva, RutaIngreso, new { correo, contrasena = ContrasenaNueva })).StatusCode);

        var persona = (await PersonaIdAsync(correo)).ToString();
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.ContrasenaCambiada));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.SesionesInvalidadas, persona));
        var correos = buzon.Para(correo);
        Assert.Equal(correosAntes + 1, correos.Count);
        Assert.Equal("Cambiaste la contraseña de Flit Atlas", correos[^1].Asunto);
        SinEnlaces(correos[^1]);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_o_los_factores_exige_reautenticacion()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        var (_, _, _, sesion) = await CuentaConSesionAsync(api);
        using var _s = sesion;
        reloj.Adelantar(TimeSpan.FromMinutes(11));

        var respuestas = new[]
        {
            await CambiarContrasenaAsync(sesion, Contrasena, ContrasenaNueva),
            await PostAsync(sesion, $"{RutaCuenta}/codigos-recuperacion", new { }),
            await PostAsync(sesion, $"{RutaCuenta}/passkeys/opciones", new { }),
            await PostAsync(sesion, $"{RutaCuenta}/passkeys", new { estado = "AAAA", credencial = "{}" }),
        };

        foreach (var respuesta in respuestas)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(Ct));
            Assert.Equal(Reautenticacion.Codigo, cuerpo.RootElement.GetProperty("codigo").GetString());
        }
        // Ver los factores no es sensible.
        Assert.Equal(HttpStatusCode.OK, (await sesion.GetAsync($"{RutaCuenta}/seguridad")).StatusCode);
    }

    [Fact]
    public async Task La_seguridad_de_la_cuenta_exige_sesion_y_no_es_para_empleados()
    {
        await using var api = entorno.Api();
        using var anonimo = Publico(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"{RutaCuenta}/seguridad")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CambiarContrasenaAsync(anonimo, Contrasena, ContrasenaNueva)).StatusCode);

        using var empleado = await AdministradorAsync(api);
        var respuesta = await empleado.GetAsync($"{RutaCuenta}/seguridad");
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("cuenta_microsoft", await ErrorAsync(respuesta));
        Assert.Equal("cuenta_microsoft", await ErrorAsync(await PostAsync(empleado, $"{RutaCuenta}/codigos-recuperacion", new { })));
    }

    [Fact]
    public async Task Los_codigos_de_recuperacion_nuevos_reemplazan_a_los_anteriores_y_se_avisa_al_titular()
    {
        await using var api = entorno.Api();
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var (correo, _, cuenta, sesion) = await CuentaConSesionAsync(api);
        using var _s = sesion;

        var respuesta = await PostAsync(sesion, $"{RutaCuenta}/codigos-recuperacion", new { });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var nuevos = (await respuesta.Content.ReadFromJsonAsync<EndpointsSeguridadCuenta.CodigosRecuperacionNuevos>(Ct))!.CodigosRecuperacion;
        Assert.Equal(FactoresExternos.CodigosRecuperacion, nuevos.Count);
        Assert.Empty(nuevos.Intersect(cuenta.CodigosRecuperacion));
        Assert.Equal(HttpStatusCode.OK, (await sesion.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        var seguridad = (await (await sesion.GetAsync($"{RutaCuenta}/seguridad")).Content.ReadFromJsonAsync<EndpointsSeguridadCuenta.SeguridadCuenta>(Ct))!;
        Assert.Equal(FactoresExternos.CodigosRecuperacion, seguridad.CodigosRecuperacionRestantes);

        // Un código anterior ya no sirve; uno nuevo sí.
        using var conElViejo = Publico(api);
        var paso = await PasoTotpAsync(conElViejo, correo);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostAsync(conElViejo, RutaSegundoFactor, new { estado = paso, codigoRecuperacion = cuenta.CodigosRecuperacion[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PostAsync(conElViejo, RutaSegundoFactor, new { estado = paso, codigoRecuperacion = nuevos[0] })).StatusCode);

        var aviso = buzon.Para(correo)[^1];
        Assert.Equal("Generaste códigos de recuperación nuevos en Flit Atlas", aviso.Asunto);
        SinEnlaces(aviso);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.FactorRegistrado));
    }

    [Fact]
    public async Task Agregar_una_passkey_desde_la_cuenta_deja_entrar_con_ella_y_se_avisa_al_titular()
    {
        await using var api = entorno.Api();
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var (correo, _, _, sesion) = await CuentaConSesionAsync(api);
        using var _s = sesion;
        var antes = (await (await sesion.GetAsync($"{RutaCuenta}/seguridad")).Content.ReadFromJsonAsync<EndpointsSeguridadCuenta.SeguridadCuenta>(Ct))!;
        Assert.True(antes.Contrasena);
        Assert.True(antes.Totp);
        Assert.Empty(antes.Passkeys);

        using var autenticador = new AutenticadorDePrueba();
        var opciones = (await (await PostAsync(sesion, $"{RutaCuenta}/passkeys/opciones", new { })).Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        var credencial = autenticador.Registrar(opciones.Opciones);
        // Un paso de otro ámbito (aquí, inventado) no sirve.
        Assert.Equal("paso_vencido", await ErrorAsync(await PostAsync(sesion, $"{RutaCuenta}/passkeys", new { estado = "AAAAAAAA", credencial })));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(sesion, $"{RutaCuenta}/passkeys", new { estado = opciones.Estado, credencial })).StatusCode);
        // El paso es de un solo uso.
        Assert.Equal("paso_vencido", await ErrorAsync(await PostAsync(sesion, $"{RutaCuenta}/passkeys", new { estado = opciones.Estado, credencial })));

        Assert.Equal(HttpStatusCode.OK, (await sesion.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        var despues = (await (await sesion.GetAsync($"{RutaCuenta}/seguridad")).Content.ReadFromJsonAsync<EndpointsSeguridadCuenta.SeguridadCuenta>(Ct))!;
        Assert.Single(despues.Passkeys);

        using var conPasskey = Publico(api);
        var ingreso = (await (await PostAsync(conPasskey, "/api/v1/identidad/entrar/passkey/opciones", new { })).Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        Assert.Equal(HttpStatusCode.NoContent,
            (await PostAsync(conPasskey, "/api/v1/identidad/entrar/passkey", new { estado = ingreso.Estado, credencial = autenticador.Firmar(ingreso.Opciones) })).StatusCode);

        var aviso = buzon.Para(correo)[^1];
        Assert.Equal("Agregaste una passkey a Flit Atlas", aviso.Asunto);
        SinEnlaces(aviso);
    }

    // --- Lista local de contraseñas filtradas (V6.2.4) y HIBP caído ---

    [Fact]
    public void La_lista_local_tiene_al_menos_3000_contrasenas_filtradas_de_12_o_mas_caracteres()
    {
        Assert.True(ValidadorContrasenaFiltrada.TamanoListaLocal >= 3000, $"La lista local tiene {ValidadorContrasenaFiltrada.TamanoListaLocal}.");
        // De la fuente pública (SecLists) y de la lista propia en español.
        foreach (var filtrada in new[] { "1qaz2wsx3edc", "qwerty123456", "Q1W2E3R4T5Y6", "123456789012" })
        {
            Assert.True(ValidadorContrasenaFiltrada.EsComun(filtrada, "externo@ejemplo.com"), filtrada);
        }
        Assert.False(ValidadorContrasenaFiltrada.EsComun(Contrasena, "externo@ejemplo.com"));
    }

    [Fact]
    public async Task Si_HIBP_no_responde_la_revision_falla_abierto_contra_la_lista_local()
    {
        var validador = new ValidadorContrasenaFiltrada(new FabricaSinRed(), Options.Create(new OpcionesExternos()), NullLogger<ValidadorContrasenaFiltrada>.Instance);
        var usuario = new UsuarioAtlas { PersonaId = Guid.NewGuid(), Email = "externo@ejemplo.com" };

        // HIBP no responde: una contraseña propia pasa; una de la lista local no.
        Assert.True((await validador.ValidateAsync(null!, usuario, Contrasena)).Succeeded);
        var filtrada = await validador.ValidateAsync(null!, usuario, "qwertyqwerty");
        Assert.False(filtrada.Succeeded);
        Assert.Equal(ValidadorContrasenaFiltrada.CodigoComun, Assert.Single(filtrada.Errors).Code);
    }

    /// <summary>Cliente de HIBP que siempre falla como una red caída.</summary>
    private sealed class FabricaSinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SinRed()) { BaseAddress = new Uri("https://hibp.ejemplo.com/") };

        private sealed class SinRed : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Sin red (prueba).");
        }
    }
}
