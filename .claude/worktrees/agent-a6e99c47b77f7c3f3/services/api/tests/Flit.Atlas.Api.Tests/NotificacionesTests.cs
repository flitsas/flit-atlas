using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 1.6: centro de notificaciones, preferencias, banderas y suscripciones Web Push por dispositivo, contra Postgres
/// real. Los servicios de push (FCM, Apple…) se reemplazan por <see cref="ServicioPushFalso"/>; el cifrado y la firma
/// VAPID sí corren con la librería real.
/// </summary>
public class NotificacionesTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"push.{Guid.NewGuid():N}@ejemplo.com";

    private static string EndpointNuevo(string host = "fcm.googleapis.com") => $"https://{host}/fcm/send/{Guid.NewGuid():N}";

    /// <summary>Llaves de un navegador ficticio: P-256 sin comprimir (65 bytes) y 16 bytes de auth, en base64url.</summary>
    private static (string P256dh, string Auth) LlavesNavegador()
    {
        using var curva = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = curva.ExportParameters(false).Q;
        return (Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private static object Suscripcion(string endpoint)
    {
        var (p256dh, auth) = LlavesNavegador();
        return new { endpoint, p256dh, auth, dispositivo = "Chrome en Android" };
    }

    private WebApplicationFactory<Program> Api(ServicioPushFalso? push = null) =>
        entorno.Api().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            if (push is not null)
            {
                s.AddHttpClient<EnviadorPush>().ConfigurePrimaryHttpMessageHandler(() => push);
            }
        }));

    private static async Task<(Navegador Navegador, Guid PersonaId)> EntrarAsync(WebApplicationFactory<Program> api, string? correo = null)
    {
        var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(correo ?? CorreoNuevo(), "Persona Push");
        var yo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        return (navegador, yo!.PersonaId);
    }

    private static Task<HttpResponseMessage> PostAsync(Navegador navegador, string ruta, object? cuerpo = null) =>
        navegador.EnviarAsync(HttpMethod.Post, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    [Fact]
    public void Los_claims_de_plataforma_son_los_de_identidad()
    {
        Assert.Equal(ClaimsAtlas.PersonaId, EndpointsPlataforma.ClaimPersona);
        Assert.Equal(ClaimsAtlas.OrganizacionId, EndpointsPlataforma.ClaimOrganizacion);
    }

    [Theory]
    [InlineData("GET", "/api/v1/plataforma/banderas")]
    [InlineData("GET", "/api/v1/plataforma/notificaciones")]
    [InlineData("GET", "/api/v1/plataforma/notificaciones/preferencias")]
    [InlineData("GET", "/api/v1/plataforma/push/clave")]
    [InlineData("POST", "/api/v1/plataforma/push/suscripciones")]
    [InlineData("POST", "/api/v1/plataforma/push/prueba")]
    public async Task Sin_sesion_responden_401(string metodo, string ruta)
    {
        await using var api = Api();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.EnviarAsync(new HttpMethod(metodo), ruta, metodo == "POST" ? JsonContent.Create(new { }) : null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task En_local_sin_llaves_configuradas_entrega_una_llave_publica_efimera_valida()
    {
        await using var api = Api();
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;

        var clave = await (await navegador.GetAsync("/api/v1/plataforma/push/clave")).Content
            .ReadFromJsonAsync<EndpointsPlataforma.ClavePushDto>(Ct);

        Assert.True(clave!.Habilitado);
        var bytes = Base64Url.DecodeFromChars(clave.ClavePublica!);
        Assert.Equal(65, bytes.Length);
        Assert.Equal(0x04, bytes[0]);
    }

    [Fact]
    public async Task Registrar_la_suscripcion_la_guarda_y_otra_persona_en_el_mismo_dispositivo_se_la_queda()
    {
        await using var api = Api();
        var (ana, anaId) = await EntrarAsync(api);
        var (beto, betoId) = await EntrarAsync(api);
        using var _a = ana;
        using var _b = beto;
        var endpoint = EndpointNuevo();

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(ana, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint))).StatusCode);
        await using (var bd = entorno.Plataforma())
        {
            var guardada = await bd.SuscripcionesPush.SingleAsync(s => s.Endpoint == endpoint, Ct);
            Assert.Equal(anaId, guardada.PersonaId);
            Assert.Equal("Chrome en Android", guardada.Dispositivo);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(beto, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint))).StatusCode);

        await using var plataforma = entorno.Plataforma();
        var suscripcion = await plataforma.SuscripcionesPush.SingleAsync(s => s.Endpoint == endpoint, Ct);
        Assert.Equal(betoId, suscripcion.PersonaId);
    }

    [Fact]
    public async Task La_baja_solo_borra_la_suscripcion_propia()
    {
        await using var api = Api();
        var (duena, _) = await EntrarAsync(api);
        var (otra, _) = await EntrarAsync(api);
        using var _d = duena;
        using var _o = otra;
        var endpoint = EndpointNuevo();
        await PostAsync(duena, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint));

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(otra, "/api/v1/plataforma/push/suscripciones/baja", new { endpoint })).StatusCode);
        await using (var bd = entorno.Plataforma())
        {
            Assert.True(await bd.SuscripcionesPush.AnyAsync(s => s.Endpoint == endpoint, Ct));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(duena, "/api/v1/plataforma/push/suscripciones/baja", new { endpoint })).StatusCode);
        await using var plataforma = entorno.Plataforma();
        Assert.False(await plataforma.SuscripcionesPush.AnyAsync(s => s.Endpoint == endpoint, Ct));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://atlas-api.atlas-qa.svc.cluster.local/api/v1/plataforma/yo")]
    [InlineData("https://fcm.googleapis.com.ejemplo.com/x")]
    [InlineData("no es una url")]
    public async Task Solo_acepta_endpoints_https_de_servicios_de_push_conocidos(string endpoint)
    {
        await using var api = Api();
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;

        var respuesta = await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Rechaza_llaves_del_navegador_mal_formadas()
    {
        await using var api = Api();
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;

        var respuesta = await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones",
            new { endpoint = EndpointNuevo(), p256dh = "corta", auth = "!!", dispositivo = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct);
        Assert.Contains("errors", problema!.Keys);
    }

    [Fact]
    public async Task La_prueba_crea_la_notificacion_la_cifra_con_VAPID_y_borra_las_suscripciones_vencidas()
    {
        var push = new ServicioPushFalso();
        await using var api = Api(push);
        var (navegador, personaId) = await EntrarAsync(api);
        using var _n = navegador;
        var vigente = EndpointNuevo();
        var vencida = EndpointNuevo("web.push.apple.com");
        push.Respuestas[vencida] = HttpStatusCode.Gone;
        await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones", Suscripcion(vigente));
        await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones", Suscripcion(vencida));

        var respuesta = await PostAsync(navegador, "/api/v1/plataforma/push/prueba");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(1, (await respuesta.Content.ReadFromJsonAsync<EndpointsPlataforma.PruebaPushDto>(Ct))!.Dispositivos);
        var peticion = Assert.Single(push.Recibidas, p => p.Url == vigente);
        Assert.StartsWith("vapid t=", peticion.Autorizacion, StringComparison.Ordinal);
        Assert.Equal("aes128gcm", peticion.Codificacion);
        Assert.Equal("86400", peticion.Ttl);
        // El cuerpo va cifrado: el título no viaja en claro.
        Assert.True(peticion.Cuerpo.AsSpan().IndexOf("Notificación de prueba"u8) < 0);

        await using var plataforma = entorno.Plataforma();
        Assert.False(await plataforma.SuscripcionesPush.AnyAsync(s => s.Endpoint == vencida, Ct));
        var usada = await plataforma.SuscripcionesPush.SingleAsync(s => s.Endpoint == vigente, Ct);
        Assert.NotNull(usada.UltimoUsoEn);
        var notificacion = await plataforma.Notificaciones.SingleAsync(n => n.PersonaId == personaId, Ct);
        Assert.Equal(TiposNotificacion.Prueba.Codigo, notificacion.Tipo);
        Assert.Equal("/notificaciones", notificacion.Enlace);
    }

    [Fact]
    public async Task Los_fallos_temporales_se_cuentan_y_al_tope_la_suscripcion_se_borra()
    {
        var push = new ServicioPushFalso();
        await using var api = Api(push);
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;
        var endpoint = EndpointNuevo();
        push.Respuestas[endpoint] = HttpStatusCode.InternalServerError;
        await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint));
        await using (var bd = entorno.Plataforma())
        {
            await bd.SuscripcionesPush.Where(s => s.Endpoint == endpoint)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FallosConsecutivos, EnviadorPush.TopeFallos - 2), Ct);
        }

        await PostAsync(navegador, "/api/v1/plataforma/push/prueba");
        await using (var bd = entorno.Plataforma())
        {
            var suscripcion = await bd.SuscripcionesPush.SingleAsync(s => s.Endpoint == endpoint, Ct);
            Assert.Equal(EnviadorPush.TopeFallos - 1, suscripcion.FallosConsecutivos);
            Assert.NotNull(suscripcion.UltimoFalloEn);
        }

        await PostAsync(navegador, "/api/v1/plataforma/push/prueba");
        await using var plataforma = entorno.Plataforma();
        Assert.False(await plataforma.SuscripcionesPush.AnyAsync(s => s.Endpoint == endpoint, Ct));
    }

    [Fact]
    public async Task Con_el_push_apagado_para_el_tipo_la_prueba_solo_queda_en_la_bandeja()
    {
        var push = new ServicioPushFalso();
        await using var api = Api(push);
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;
        var endpoint = EndpointNuevo();
        await PostAsync(navegador, "/api/v1/plataforma/push/suscripciones", Suscripcion(endpoint));

        var guardar = await navegador.EnviarAsync(HttpMethod.Put, "/api/v1/plataforma/notificaciones/preferencias",
            JsonContent.Create(new[] { new { tipo = TiposNotificacion.Prueba.Codigo, canal = "push", activa = false } }));
        var prueba = await PostAsync(navegador, "/api/v1/plataforma/push/prueba");

        Assert.Equal(HttpStatusCode.NoContent, guardar.StatusCode);
        Assert.Equal(0, (await prueba.Content.ReadFromJsonAsync<EndpointsPlataforma.PruebaPushDto>(Ct))!.Dispositivos);
        Assert.DoesNotContain(push.Recibidas, p => p.Url == endpoint);
        var preferencias = await (await navegador.GetAsync("/api/v1/plataforma/notificaciones/preferencias")).Content
            .ReadFromJsonAsync<List<EndpointsPlataforma.PreferenciaTipoDto>>(Ct);
        var tipoPrueba = Assert.Single(preferencias!, p => p.Tipo == TiposNotificacion.Prueba.Codigo);
        Assert.False(Assert.Single(tipoPrueba.Canales, c => c.Canal == "push").Activa);
        var app = Assert.Single(tipoPrueba.Canales, c => c.Canal == "app");
        Assert.True(app.Activa);
        Assert.False(app.Editable);
        Assert.False(Assert.Single(tipoPrueba.Canales, c => c.Canal == "correo").Disponible);
        var bandeja = await (await navegador.GetAsync("/api/v1/plataforma/notificaciones")).Content.ReadFromJsonAsync<EndpointsPlataforma.BandejaDto>(Ct);
        Assert.Equal(1, bandeja!.NoLeidas);
    }

    [Theory]
    [InlineData("plataforma.prueba", "app")]
    [InlineData("plataforma.prueba", "correo")]
    [InlineData("plataforma.prueba", "sms")]
    [InlineData("tipo.inexistente", "push")]
    public async Task No_se_pueden_cambiar_canales_fijos_no_disponibles_o_tipos_desconocidos(string tipo, string canal)
    {
        await using var api = Api();
        var (navegador, _) = await EntrarAsync(api);
        using var _n = navegador;

        var respuesta = await navegador.EnviarAsync(HttpMethod.Put, "/api/v1/plataforma/notificaciones/preferencias",
            JsonContent.Create(new[] { new { tipo, canal, activa = false } }));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task La_bandeja_lista_lo_mas_reciente_primero_y_marca_leidas_solo_las_propias()
    {
        await using var api = Api();
        var (navegador, personaId) = await EntrarAsync(api);
        var (otra, otraId) = await EntrarAsync(api);
        using var _n = navegador;
        using var _o = otra;
        Notificacion vieja, nueva, ajena;
        await using (var bd = entorno.Plataforma())
        {
            vieja = new Notificacion { PersonaId = personaId, Tipo = "plataforma.aviso", Titulo = "Vieja", Cuerpo = "1" };
            bd.Notificaciones.Add(vieja);
            await bd.SaveChangesAsync(Ct);
            nueva = new Notificacion { PersonaId = personaId, Tipo = "plataforma.aviso", Titulo = "Nueva", Cuerpo = "2", Enlace = "/soporte" };
            ajena = new Notificacion { PersonaId = otraId, Tipo = "plataforma.aviso", Titulo = "Ajena", Cuerpo = "3" };
            bd.Notificaciones.AddRange(nueva, ajena);
            await bd.SaveChangesAsync(Ct);
        }

        var bandeja = await (await navegador.GetAsync("/api/v1/plataforma/notificaciones")).Content.ReadFromJsonAsync<EndpointsPlataforma.BandejaDto>(Ct);
        Assert.Equal(["Nueva", "Vieja"], bandeja!.Elementos.Select(n => n.Titulo));
        Assert.Equal(2, bandeja.NoLeidas);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(navegador, $"/api/v1/plataforma/notificaciones/{ajena.Id}/leida")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(navegador, $"/api/v1/plataforma/notificaciones/{nueva.Id}/leida")).StatusCode);
        var trasUna = await (await navegador.GetAsync("/api/v1/plataforma/notificaciones")).Content.ReadFromJsonAsync<EndpointsPlataforma.BandejaDto>(Ct);
        Assert.Equal(1, trasUna!.NoLeidas);

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(navegador, "/api/v1/plataforma/notificaciones/leidas")).StatusCode);
        var trasTodas = await (await navegador.GetAsync("/api/v1/plataforma/notificaciones?limite=1")).Content.ReadFromJsonAsync<EndpointsPlataforma.BandejaDto>(Ct);
        Assert.Equal(0, trasTodas!.NoLeidas);
        Assert.Single(trasTodas.Elementos);

        await using var plataforma = entorno.Plataforma();
        Assert.Null((await plataforma.Notificaciones.SingleAsync(n => n.Id == ajena.Id, Ct)).LeidaEn);
    }

    [Fact]
    public async Task Las_banderas_reflejan_la_tabla_de_modulos_y_la_organizacion()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Con Banderas");
        var yo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        await using (var bd = entorno.Plataforma(baseNueva))
        {
            bd.Modulos.AddRange(
                new Modulo { Id = "soporte", Activo = true },
                new Modulo { Id = "contratos", Activo = false },
                new Modulo { Id = "equipo", Activo = true, OrganizacionesHabilitadas = [yo!.OrganizacionId] });
            // El catálogo ya es un módulo registrado (ATLAS-ADR-0028): su fila la creó el migrador, encendida.
            var catalogo = await bd.Modulos.SingleAsync(m => m.Id == "catalogo", Ct);
            catalogo.OrganizacionesHabilitadas = [Guid.NewGuid()];
            await bd.SaveChangesAsync(Ct);
        }

        var banderas = await (await navegador.GetAsync("/api/v1/plataforma/banderas")).Content.ReadFromJsonAsync<Dictionary<string, bool>>(Ct);

        Assert.Equal(
            new Dictionary<string, bool>
            {
                ["modulo.soporte"] = true,
                ["modulo.contratos"] = false,
                ["modulo.catalogo"] = false,
                ["modulo.equipo"] = true,
                // La crea el migrador para cada módulo registrado, apagada por defecto (ATLAS-ADR-0026).
                ["modulo.hola"] = false,
            },
            banderas);
    }

    [Fact]
    public void Las_llaves_VAPID_son_obligatorias_en_pareja_y_validas()
    {
        static IConfiguration Config(params (string, string)[] valores) =>
            new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => KeyValuePair.Create(v.Item1, (string?)v.Item2))).Build();
        var (publica, privada) = LlavesVapid.Generar();

        var sola = Assert.Throws<InvalidOperationException>(() =>
            LlavesVapid.Resolver(Config((LlavesVapid.ClavePublicaConfig, publica)), NullLogger.Instance));
        var invalida = Assert.Throws<InvalidOperationException>(() =>
            LlavesVapid.Resolver(Config((LlavesVapid.ClavePublicaConfig, "abc"), (LlavesVapid.ClavePrivadaConfig, privada)), NullLogger.Instance));
        var enQa = LlavesVapid.Resolver(Config(("Atlas:Ambiente", "qa")), NullLogger.Instance);
        var configuradas = LlavesVapid.Resolver(
            Config(("Atlas:Ambiente", "qa"), (LlavesVapid.ClavePublicaConfig, publica), (LlavesVapid.ClavePrivadaConfig, privada),
                (LlavesVapid.AsuntoConfig, "mailto:atlas@ejemplo.com")),
            NullLogger.Instance);

        Assert.Contains("Plataforma__Push__ClavePrivada", sola.Message, StringComparison.Ordinal);
        Assert.Contains("Plataforma__Push__ClavePublica", invalida.Message, StringComparison.Ordinal);
        Assert.False(enQa.Habilitado);
        Assert.True(configuradas.Habilitado);
        Assert.Equal(publica, configuradas.ClavePublica);
    }
}

/// <summary>Servicio de push de mentira: guarda lo que recibe y responde 201, o lo que diga <see cref="Respuestas"/>.</summary>
public sealed class ServicioPushFalso : HttpMessageHandler
{
    public sealed record Peticion(string Url, string? Autorizacion, string? Codificacion, string? Ttl, byte[] Cuerpo);

    public ConcurrentDictionary<string, HttpStatusCode> Respuestas { get; } = new();

    public ConcurrentBag<Peticion> Recibidas { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var cuerpo = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Recibidas.Add(new Peticion(
            url,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentEncoding.FirstOrDefault(),
            request.Headers.TryGetValues("TTL", out var ttl) ? ttl.First() : null,
            cuerpo));
        return new HttpResponseMessage(Respuestas.TryGetValue(url, out var estado) ? estado : HttpStatusCode.Created);
    }
}
