using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Tokens;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 1.3c (ATLAS-ADR-0024): servidor de tokens de OpenIddict. Además de los dos CA (client_credentials contra un
/// endpoint con scope y revocación de la cadena al reutilizar un refresh), estas pruebas hacen de suite de
/// conformidad básica propia: descubrimiento, JWKS, código con PKCE, firma del id_token, audiencias y revocación.
/// Usan una base propia: la sincronización de clientes borra los que no están en su configuración.
/// </summary>
public partial class TokensTests(EntornoApi entorno)
{
    private const string Cli = "atlas-cli";
    private const string Servicio = "svc-prueba";
    private const string ServicioSinScopes = "svc-sin-scopes";
    private const string ServicioOtraAudiencia = "svc-render-prueba";
    private const string RedirectCli = "http://127.0.0.1:53682/callback";

    /// <summary>Secreto aleatorio de cada corrida: nunca un valor fijo en el repo.</summary>
    private static readonly string Secreto = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static readonly SemaphoreSlim CandadoBase = new(1, 1);
    private static string? _base;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"empleado.{Guid.NewGuid():N}@ejemplo.com";

    /// <summary>IP de documentación (2001:db8::/32) única por prueba, para encontrar sus eventos.</summary>
    private static string IpNueva() => $"2001:db8:7::{Random.Shared.Next(1, 0xffff):x}:{Random.Shared.Next(1, 0xffff):x}";

    private async Task<string> BaseAsync()
    {
        await CandadoBase.WaitAsync(Ct);
        try
        {
            return _base ??= await entorno.BaseNuevaAsync();
        }
        finally
        {
            CandadoBase.Release();
        }
    }

    public static Dictionary<string, string> ConfiguracionClientes(Dictionary<string, string>? extra = null)
    {
        var configuracion = new Dictionary<string, string>
        {
            // Sin margen: el segundo uso de un refresh ya es reutilización.
            [$"{OpcionesTokens.Seccion}:MargenReutilizacionRefresh"] = "00:00:00",
            [$"{OpcionesTokens.Seccion}:Clientes:{Servicio}:Nombre"] = "Servicio de prueba",
            [$"{OpcionesTokens.Seccion}:Clientes:{Servicio}:Tipo"] = "Confidencial",
            [$"{OpcionesTokens.Seccion}:Clientes:{Servicio}:Secreto"] = Secreto,
            [$"{OpcionesTokens.Seccion}:Clientes:{Servicio}:Flujos:0"] = "client_credentials",
            [$"{OpcionesTokens.Seccion}:Clientes:{Servicio}:Scopes:0"] = ScopesAtlas.Publicar,
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioSinScopes}:Tipo"] = "Confidencial",
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioSinScopes}:Secreto"] = Secreto,
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioSinScopes}:Flujos:0"] = "client_credentials",
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioOtraAudiencia}:Tipo"] = "Confidencial",
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioOtraAudiencia}:Secreto"] = Secreto,
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioOtraAudiencia}:Flujos:0"] = "client_credentials",
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioOtraAudiencia}:Scopes:0"] = ScopesAtlas.Publicar,
            [$"{OpcionesTokens.Seccion}:Clientes:{ServicioOtraAudiencia}:Audiencias:0"] = "atlas-render",
        };
        foreach (var (clave, valor) in extra ?? [])
        {
            configuracion[clave] = valor;
        }
        return configuracion;
    }

    private async Task<WebApplicationFactory<Program>> ApiAsync(Dictionary<string, string>? extra = null, TimeProvider? reloj = null) =>
        entorno.Api(baseDeDatos: await BaseAsync(), configuracion: ConfiguracionClientes(extra), reloj: reloj);

    private static HttpClient Cliente(WebApplicationFactory<Program> api, string? ip = null)
    {
        var http = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (ip is not null)
        {
            http.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        }
        return http;
    }

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> TokenAsync(HttpClient http, params (string Clave, string Valor)[] campos)
    {
        using var contenido = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Clave, c.Valor)));
        using var respuesta = await http.PostAsync(new Uri(ServidorTokens.RutaToken, UriKind.Relative), contenido, Ct);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (respuesta.StatusCode, cuerpo);
    }

    private static Task<(HttpStatusCode Estado, JsonElement Cuerpo)> CredencialesDeServicioAsync(
        HttpClient http, string cliente, string? scope = null, string? secreto = null)
    {
        var campos = new List<(string, string)> { ("grant_type", "client_credentials"), ("client_id", cliente), ("client_secret", secreto ?? Secreto) };
        if (scope is not null)
        {
            campos.Add(("scope", scope));
        }
        return TokenAsync(http, [.. campos]);
    }

    private static async Task<HttpResponseMessage> ConBearerAsync(HttpClient http, string ruta, string token, HttpMethod? metodo = null)
    {
        using var peticion = new HttpRequestMessage(metodo ?? HttpMethod.Get, ruta);
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(peticion, Ct);
    }

    private sealed record Pkce(string Verificador, string Reto)
    {
        public static Pkce Nuevo()
        {
            var verificador = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            return new(verificador, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verificador))));
        }
    }

    private static string UrlAutorizar(Pkce? pkce, string scope = "openid profile offline_access atlas.publicar",
        string redirect = RedirectCli, string cliente = Cli, string? nonce = null, string? prompt = null) =>
        QueryHelpers.AddQueryString(ServidorTokens.RutaAutorizar, new Dictionary<string, string?>
        {
            ["client_id"] = cliente,
            ["response_type"] = "code",
            ["redirect_uri"] = redirect,
            ["scope"] = scope,
            ["state"] = "estado-de-prueba",
            ["nonce"] = nonce,
            ["prompt"] = prompt,
            ["code_challenge"] = pkce?.Reto,
            ["code_challenge_method"] = pkce is null ? null : "S256",
        }.Where(p => p.Value is not null));

    private static Dictionary<string, string> ParametrosDe(Uri ubicacion) =>
        QueryHelpers.ParseQuery(ubicacion.Query).ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);

    /// <summary>Empleado con sesión del BFF que autoriza a atlas-cli: código con PKCE y canje por tokens.</summary>
    private static async Task<(JsonElement Tokens, Navegador Navegador, string Correo)> FlujoCodigoAsync(
        WebApplicationFactory<Program> api, string ip, string? nonce = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = ip;
        var correo = CorreoNuevo();
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Persona Demo")).StatusCode);

        var pkce = Pkce.Nuevo();
        var autorizacion = await navegador.GetAsync(UrlAutorizar(pkce, nonce: nonce));
        Assert.True(autorizacion.StatusCode == HttpStatusCode.Redirect, await autorizacion.Content.ReadAsStringAsync(Ct));
        var ubicacion = autorizacion.Headers.Location!;
        Assert.StartsWith(RedirectCli, ubicacion.ToString(), StringComparison.Ordinal);
        var parametros = ParametrosDe(ubicacion);
        Assert.Equal("estado-de-prueba", parametros["state"]);

        using var http = Cliente(api, ip);
        var (estado, tokens) = await TokenAsync(http,
            ("grant_type", "authorization_code"), ("client_id", Cli), ("code", parametros["code"]),
            ("redirect_uri", RedirectCli), ("code_verifier", pkce.Verificador));
        Assert.True(estado == HttpStatusCode.OK, tokens.ToString());
        return (tokens, navegador, correo);
    }

    private static Task<(HttpStatusCode Estado, JsonElement Cuerpo)> RefrescarAsync(HttpClient http, string refresh) =>
        TokenAsync(http, ("grant_type", "refresh_token"), ("client_id", Cli), ("refresh_token", refresh));

    private async Task<int> EventosAsync(string tipo, string ip)
    {
        await using var identidad = entorno.Identidad(await BaseAsync());
        var direccion = IPAddress.Parse(ip);
        return await identidad.EventosSeguridad.CountAsync(e => e.Tipo == tipo && e.Ip == direccion, Ct);
    }

    // --- CA 1: client_credentials contra un endpoint protegido por scope ---

    [Fact]
    public async Task Un_servicio_obtiene_un_token_con_client_credentials_y_llama_al_endpoint_protegido_por_scope()
    {
        await using var api = await ApiAsync();
        var ip = IpNueva();
        using var http = Cliente(api, ip);

        var (estado, cuerpo) = await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Publicar);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("Bearer", cuerpo.GetProperty("token_type").GetString());
        Assert.InRange(cuerpo.GetProperty("expires_in").GetInt32(), 1, 15 * 60);
        Assert.False(cuerpo.TryGetProperty("refresh_token", out _));
        var acceso = cuerpo.GetProperty("access_token").GetString()!;

        using var respuesta = await ConBearerAsync(http, "/api/v1/identidad/token/yo", acceso);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var portador = (await respuesta.Content.ReadFromJsonAsync<EndpointsTokens.PortadorToken>(Ct))!;
        Assert.Equal(Servicio, portador.Sub);
        Assert.Equal(Servicio, portador.Cliente);
        Assert.Equal("servicio", portador.TipoPrincipal);
        Assert.Equal([ScopesAtlas.Publicar], portador.Scopes);
        Assert.Equal([OpcionesTokens.AudienciaApi], portador.Audiencias);
        Assert.Null(portador.PersonaId);
        Assert.Equal(1, await EventosAsync(TiposEventoTokens.TokenEmitido, ip));
    }

    [Fact]
    public async Task Un_token_sin_el_scope_recibe_403_y_sin_token_401()
    {
        await using var api = await ApiAsync();
        using var http = Cliente(api);
        var (_, sinScopes) = await CredencialesDeServicioAsync(http, ServicioSinScopes);

        using var conToken = await ConBearerAsync(http, "/api/v1/identidad/token/yo", sinScopes.GetProperty("access_token").GetString()!);
        using var sinToken = await http.GetAsync(new Uri("/api/v1/identidad/token/yo", UriKind.Relative), Ct);
        using var tokenFalso = await ConBearerAsync(http, "/api/v1/identidad/token/yo", "no-es-un-token");

        Assert.Equal(HttpStatusCode.Forbidden, conToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenFalso.StatusCode);
    }

    [Fact]
    public async Task Un_servicio_no_puede_pedir_un_scope_que_no_tiene_permitido()
    {
        await using var api = await ApiAsync();
        using var http = Cliente(api);

        var (estado, cuerpo) = await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Mcp);

        // OpenIddict responde invalid_request (ID2051: el cliente no tiene permiso para ese scope).
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("invalid_request", cuerpo.GetProperty("error").GetString());
        Assert.EndsWith("ID2051", cuerpo.GetProperty("error_uri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_secreto_equivocado_se_rechaza_y_queda_auditado_como_cliente_rechazado()
    {
        await using var api = await ApiAsync();
        var ip = IpNueva();
        using var http = Cliente(api, ip);

        var (estado, cuerpo) = await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Publicar, secreto: "otro-secreto-que-no-es");

        Assert.Equal(HttpStatusCode.Unauthorized, estado);
        Assert.Equal("invalid_client", cuerpo.GetProperty("error").GetString());
        Assert.Equal(1, await EventosAsync(TiposEventoTokens.ClienteRechazado, ip));
    }

    [Fact]
    public async Task Un_token_para_otra_audiencia_no_sirve_en_atlas_api()
    {
        await using var api = await ApiAsync();
        using var http = Cliente(api);
        var (estado, cuerpo) = await CredencialesDeServicioAsync(http, ServicioOtraAudiencia, ScopesAtlas.Publicar);
        Assert.Equal(HttpStatusCode.OK, estado);

        using var respuesta = await ConBearerAsync(http, "/api/v1/identidad/token/yo", cuerpo.GetProperty("access_token").GetString()!);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_token_de_un_servicio_interno_va_firmado_sin_cifrar_y_se_valida_con_el_JWKS()
    {
        // ATLAS-ADR-0044: atlas-docs (o atlas-render) valida el token con el JWKS público, sin la llave de cifrado; los de
        // atlas-api siguen cifrados.
        await using var api = await ApiAsync();
        using var http = Cliente(api);
        var (_, servicio) = await CredencialesDeServicioAsync(http, ServicioOtraAudiencia, ScopesAtlas.Publicar);
        var (_, propio) = await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Publicar);
        var jws = servicio.GetProperty("access_token").GetString()!;

        Assert.Equal(3, jws.Split('.').Length);
        Assert.Equal(5, propio.GetProperty("access_token").GetString()!.Split('.').Length);
        var jwks = new JsonWebKeySet(await http.GetStringAsync(new Uri(ServidorTokens.RutaJwks, UriKind.Relative), Ct));
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(jws, new TokenValidationParameters
        {
            ValidAudience = "atlas-render",
            ValidIssuer = "https://localhost/",
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        Assert.True(resultado.IsValid, resultado.Exception?.Message);
        Assert.Contains(ScopesAtlas.Publicar, resultado.Claims["scope"].ToString(), StringComparison.Ordinal);
        var otraAudiencia = await new JsonWebTokenHandler().ValidateTokenAsync(jws, new TokenValidationParameters
        {
            ValidAudience = "atlas-docs",
            ValidIssuer = "https://localhost/",
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        Assert.False(otraAudiencia.IsValid);
    }

    [Fact]
    public async Task La_cookie_no_basta_para_un_endpoint_con_scope_y_un_Bearer_no_abre_la_sesion_del_BFF()
    {
        await using var api = await ApiAsync();
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Solo Cookie");
        using var http = Cliente(api);
        var (_, servicio) = await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Publicar);
        var acceso = servicio.GetProperty("access_token").GetString()!;

        var conCookie = await navegador.GetAsync("/api/v1/identidad/token/yo");
        using var bearerEnBff = await ConBearerAsync(http, "/api/v1/plataforma/yo", acceso);
        using var bearerEnSesiones = await ConBearerAsync(http, "/api/v1/identidad/sesiones", acceso);

        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, conCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bearerEnBff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bearerEnSesiones.StatusCode);
        Assert.False(bearerEnBff.Headers.Contains("Set-Cookie"));
    }

    // --- CA 2: reutilizar un refresh ya usado revoca la cadena completa ---

    [Fact]
    public async Task Reutilizar_un_refresh_ya_usado_revoca_la_cadena_completa_y_queda_auditado()
    {
        await using var api = await ApiAsync();
        var ip = IpNueva();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, ip);
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var refresh1 = tokens.GetProperty("refresh_token").GetString()!;

        // Rotación: cada canje entrega un refresh nuevo.
        var (estado2, tokens2) = await RefrescarAsync(http, refresh1);
        Assert.Equal(HttpStatusCode.OK, estado2);
        var refresh2 = tokens2.GetProperty("refresh_token").GetString()!;
        var acceso2 = tokens2.GetProperty("access_token").GetString()!;
        Assert.NotEqual(refresh1, refresh2);
        using (var antes = await ConBearerAsync(http, "/api/v1/identidad/token/yo", acceso2))
        {
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        }

        // Alguien presenta otra vez el refresh ya canjeado.
        var (estadoReuso, reuso) = await RefrescarAsync(http, refresh1);

        Assert.Equal(HttpStatusCode.BadRequest, estadoReuso);
        Assert.Equal("invalid_grant", reuso.GetProperty("error").GetString());
        // Toda la cadena quedó revocada: el refresh vigente y el token de acceso que se emitió con él.
        var (estadoVigente, vigente) = await RefrescarAsync(http, refresh2);
        Assert.Equal(HttpStatusCode.BadRequest, estadoVigente);
        Assert.Equal("invalid_grant", vigente.GetProperty("error").GetString());
        using var despues = await ConBearerAsync(http, "/api/v1/identidad/token/yo", acceso2);
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
        Assert.Equal(1, await EventosAsync(TiposEventoTokens.RefreshReutilizado, ip));
        // La sesión del navegador no depende de los tokens.
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task Dentro_del_margen_un_refresh_repetido_no_revoca_la_cadena()
    {
        var reloj = new RelojDePrueba();
        await using var api = await ApiAsync(new() { [$"{OpcionesTokens.Seccion}:MargenReutilizacionRefresh"] = "00:00:10" }, reloj);
        var ip = IpNueva();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, ip);
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var refresh1 = tokens.GetProperty("refresh_token").GetString()!;

        var (primero, _) = await RefrescarAsync(http, refresh1);
        var (concurrente, _) = await RefrescarAsync(http, refresh1);
        reloj.Adelantar(TimeSpan.FromSeconds(30));
        var (tarde, _) = await RefrescarAsync(http, refresh1);

        Assert.Equal(HttpStatusCode.OK, primero);
        Assert.Equal(HttpStatusCode.OK, concurrente);
        Assert.Equal(HttpStatusCode.BadRequest, tarde);
    }

    [Fact]
    public async Task Cada_evento_del_servidor_de_tokens_queda_en_la_auditoria_sin_tokens()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva, configuracion: ConfiguracionClientes());
        var ip = IpNueva();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, ip);                    // autorizacion_concedida, token_emitido
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var refresh = tokens.GetProperty("refresh_token").GetString()!;
        await RefrescarAsync(http, refresh);                                             // token_emitido
        await RefrescarAsync(http, refresh);                                             // refresh_reutilizado
        await CredencialesDeServicioAsync(http, Servicio, ScopesAtlas.Publicar, "otro-secreto"); // cliente_rechazado
        await TokenAsync(http, ("grant_type", "refresh_token"), ("client_id", Cli), ("refresh_token", "no-es-un-refresh")); // token_rechazado
        using (var anonimo = Navegador.De(api))
        {
            anonimo.Ip = ip;
            await anonimo.GetAsync(UrlAutorizar(Pkce.Nuevo(), prompt: "none"));         // autorizacion_rechazada
        }

        await using var identidad = entorno.Identidad(baseNueva);
        await using var auditoria = entorno.Plataforma(baseNueva);
        var eventos = await identidad.EventosSeguridad.ToListAsync(Ct);
        var acciones = await auditoria.Auditoria.Where(a => a.Accion.StartsWith(TiposEventoSeguridad.PrefijoAuditoria))
            .Select(a => new { a.Accion, a.OcurridoEn, a.Datos }).ToListAsync(Ct);
        var tipos = typeof(TiposEventoTokens).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

        // Ocurrieron todos los tipos de evento del servidor de tokens…
        var ocurridos = eventos.Select(e => e.Tipo).ToHashSet();
        Assert.All(tipos, t => Assert.Contains(t, ocurridos));
        // …y cada uno tiene su registro en la auditoría (misma acción y momento).
        Assert.All(eventos.Where(e => tipos.Contains(e.Tipo)), e => Assert.Contains(acciones, a =>
            a.Accion == TiposEventoSeguridad.PrefijoAuditoria + e.Tipo && a.OcurridoEn == e.OcurridoEn));
        // Ni los tokens ni los secretos llegan a los eventos.
        var textos = eventos.Select(e => e.Datos ?? string.Empty).Concat(acciones.Select(a => a.Datos ?? string.Empty)).ToList();
        Assert.DoesNotContain(textos, t => t.Contains(refresh, StringComparison.Ordinal)
            || t.Contains(tokens.GetProperty("access_token").GetString()!, StringComparison.Ordinal)
            || t.Contains(Secreto, StringComparison.Ordinal));
    }

    // --- Código con PKCE, id_token, userinfo, revocación y suspensión ---

    [Fact]
    public async Task El_flujo_de_codigo_con_PKCE_emite_tokens_del_usuario_de_la_sesion_con_audiencia_y_vida_corta()
    {
        await using var api = await ApiAsync();
        var ip = IpNueva();
        var (tokens, navegador, correo) = await FlujoCodigoAsync(api, ip, nonce: "nonce-de-prueba");
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var yo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);

        Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), 1, 15 * 60);
        Assert.Contains(ScopesAtlas.Publicar, tokens.GetProperty("scope").GetString()!.Split(' '));
        using var respuesta = await ConBearerAsync(http, "/api/v1/identidad/token/yo", tokens.GetProperty("access_token").GetString()!);
        var portador = (await respuesta.Content.ReadFromJsonAsync<EndpointsTokens.PortadorToken>(Ct))!;
        Assert.Equal(yo!.Sub.ToString(), portador.Sub);
        Assert.Equal(Cli, portador.Cliente);
        Assert.Equal("empleado", portador.TipoPrincipal);
        Assert.Equal(yo.PersonaId, portador.PersonaId);
        Assert.Equal(yo.OrganizacionId, portador.OrganizacionId);
        Assert.Equal([OpcionesTokens.AudienciaApi], portador.Audiencias);

        // El id_token va firmado con una llave del JWKS, para el cliente y con el nonce.
        var configuracion = await DescubrimientoAsync(http);
        var validacion = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.GetProperty("id_token").GetString(), new TokenValidationParameters
        {
            ValidIssuer = configuracion.Issuer,
            ValidAudience = Cli,
            IssuerSigningKeys = configuracion.JsonWebKeySet.GetSigningKeys(),
        });
        Assert.True(validacion.IsValid, validacion.Exception?.Message);
        Assert.Equal("nonce-de-prueba", validacion.Claims["nonce"]);
        Assert.Equal(yo.Sub.ToString(), validacion.Claims["sub"]);
        Assert.Equal("Persona Demo", validacion.Claims["name"]);
        Assert.False(validacion.Claims.ContainsKey(Identidad.Sesiones.ClaimsAtlas.SelloSeguridad));
        Assert.False(validacion.Claims.ContainsKey(Identidad.Sesiones.ClaimsAtlas.Rol));

        // userinfo mínimo: sub y nombre con profile; sin email (no se pidió).
        using var userinfo = await ConBearerAsync(http, ServidorTokens.RutaUserinfo, tokens.GetProperty("access_token").GetString()!);
        var datos = await userinfo.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(Ct);
        Assert.Equal(yo.Sub.ToString(), datos!["sub"].GetString());
        Assert.Equal("Persona Demo", datos["name"].GetString());
        Assert.False(datos.ContainsKey("email"));
        Assert.DoesNotContain(correo, datos.Values.Select(v => v.ToString()));
    }

    [Fact]
    public async Task Sin_PKCE_o_con_una_redirect_uri_ajena_la_autorizacion_se_rechaza()
    {
        await using var api = await ApiAsync();
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Sin Pkce");

        var sinPkce = await navegador.GetAsync(UrlAutorizar(pkce: null));
        var ajena = await navegador.GetAsync(UrlAutorizar(Pkce.Nuevo(), redirect: "https://ataque.ejemplo.com/callback"));
        var otroPuerto = await navegador.GetAsync(UrlAutorizar(Pkce.Nuevo(), redirect: "http://127.0.0.1:61001/callback"));
        var otraRuta = await navegador.GetAsync(UrlAutorizar(Pkce.Nuevo(), redirect: "http://127.0.0.1:61001/otra"));

        // OpenIddict rechaza la falta de PKCE antes de confiar en la redirect URI: responde aquí, sin redirigir.
        Assert.Equal(HttpStatusCode.BadRequest, sinPkce.StatusCode);
        Assert.Contains("code_challenge", await sinPkce.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        // Una redirect URI no registrada nunca recibe la respuesta.
        Assert.Equal(HttpStatusCode.BadRequest, ajena.StatusCode);
        Assert.Null(ajena.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, otraRuta.StatusCode);
        // Loopback: cualquier puerto, misma ruta (RFC 8252 §7.3).
        Assert.Equal(HttpStatusCode.Redirect, otroPuerto.StatusCode);
        Assert.True(ParametrosDe(otroPuerto.Headers.Location!).ContainsKey("code"));
    }

    [Fact]
    public async Task Un_codigo_solo_se_canjea_una_vez_y_con_su_verificador_PKCE()
    {
        await using var api = await ApiAsync();
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Codigo Unico");
        using var http = Cliente(api);
        var pkce = Pkce.Nuevo();
        var codigo = ParametrosDe((await navegador.GetAsync(UrlAutorizar(pkce))).Headers.Location!)["code"];

        var (otroVerificador, _) = await TokenAsync(http, ("grant_type", "authorization_code"), ("client_id", Cli), ("code", codigo),
            ("redirect_uri", RedirectCli), ("code_verifier", Pkce.Nuevo().Verificador));
        var codigo2 = ParametrosDe((await navegador.GetAsync(UrlAutorizar(pkce))).Headers.Location!)["code"];
        var (primero, _) = await TokenAsync(http, ("grant_type", "authorization_code"), ("client_id", Cli), ("code", codigo2),
            ("redirect_uri", RedirectCli), ("code_verifier", pkce.Verificador));
        var (segundo, error) = await TokenAsync(http, ("grant_type", "authorization_code"), ("client_id", Cli), ("code", codigo2),
            ("redirect_uri", RedirectCli), ("code_verifier", pkce.Verificador));

        Assert.Equal(HttpStatusCode.BadRequest, otroVerificador);
        Assert.Equal(HttpStatusCode.OK, primero);
        Assert.Equal(HttpStatusCode.BadRequest, segundo);
        Assert.Equal("invalid_grant", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Suspender_a_la_persona_corta_sus_Bearer_y_sus_refresh_de_inmediato()
    {
        await using var api = await ApiAsync();
        var ip = IpNueva();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, ip);
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var yo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        var acceso = tokens.GetProperty("access_token").GetString()!;
        using (var antes = await ConBearerAsync(http, "/api/v1/identidad/token/yo", acceso))
        {
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        }

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            await alcance.ServiceProvider.GetRequiredService<IInvalidadorSesiones>()
                .InvalidarSesionesAsync(yo!.PersonaId, MotivosInvalidacion.Suspension, ct: Ct);
        }

        using var despues = await ConBearerAsync(http, "/api/v1/identidad/token/yo", acceso);
        var (estadoRefresh, _) = await RefrescarAsync(http, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, estadoRefresh);
    }

    [Fact]
    public async Task Pasado_el_tope_de_la_concesion_el_refresh_se_rechaza()
    {
        var reloj = new RelojDePrueba();
        await using var api = await ApiAsync(reloj: reloj);
        var ip = IpNueva();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, ip);
        using var _n = navegador;
        using var http = Cliente(api, ip);
        var refresh = tokens.GetProperty("refresh_token").GetString()!;

        // Rotando cada 5 días, el refresh (7 días) nunca vence y la concesión (30 días) sigue vigente hasta el día 25.
        for (var dia = 5; dia <= 25; dia += 5)
        {
            reloj.Adelantar(TimeSpan.FromDays(5));
            var (estado, rotado) = await RefrescarAsync(http, refresh);
            Assert.True(estado == HttpStatusCode.OK, $"día {dia}: {rotado}");
            refresh = rotado.GetProperty("refresh_token").GetString()!;
        }
        // Día 31: el refresh del día 25 aún no vence, pero la concesión sí.
        reloj.Adelantar(TimeSpan.FromDays(6));
        var (treintaYUno, error) = await RefrescarAsync(http, refresh);

        Assert.Equal(HttpStatusCode.BadRequest, treintaYUno);
        Assert.Equal("invalid_grant", error.GetProperty("error").GetString());
        Assert.Contains("concesion_vencida", error.GetProperty("error_description").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// F4-SEC-19 (ATLAS-ADR-0049): un externo no estira su acceso con los refresh de atlas-cli más allá del tope absoluto de
    /// su sesión (24 h); un empleado conserva los 30 días de la concesión.
    /// </summary>
    [Fact]
    public async Task El_refresh_de_un_externo_no_pasa_del_tope_absoluto_de_su_sesion()
    {
        var reloj = new RelojDePrueba();
        await using var api = await ApiAsync(reloj: reloj);
        var ip = IpNueva();
        var externo = await entorno.ExternoAsync(api, Flit.Atlas.Plataforma.Permisos.RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddDays(60), await BaseAsync());
        using var navegador = Navegador.De(api);
        navegador.Ip = ip;
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(externo.UsuarioId)).StatusCode);
        var pkce = Pkce.Nuevo();
        var autorizacion = await navegador.GetAsync(UrlAutorizar(pkce));
        Assert.True(autorizacion.StatusCode == HttpStatusCode.Redirect, await autorizacion.Content.ReadAsStringAsync(Ct));
        var parametros = ParametrosDe(autorizacion.Headers.Location!);
        using var http = Cliente(api, ip);
        var (estado, tokens) = await TokenAsync(http,
            ("grant_type", "authorization_code"), ("client_id", Cli), ("code", parametros["code"]),
            ("redirect_uri", RedirectCli), ("code_verifier", pkce.Verificador));
        Assert.True(estado == HttpStatusCode.OK, tokens.ToString());

        // A las 23 h el refresh todavía rota.
        reloj.Adelantar(TimeSpan.FromHours(23));
        var (a23, rotado) = await RefrescarAsync(http, tokens.GetProperty("refresh_token").GetString()!);
        Assert.True(a23 == HttpStatusCode.OK, rotado.ToString());
        // A las 25 h, aunque ese refresh dure 7 días, la concesión del externo ya venció.
        reloj.Adelantar(TimeSpan.FromHours(2));
        var (a25, error) = await RefrescarAsync(http, rotado.GetProperty("refresh_token").GetString()!);

        Assert.Equal(HttpStatusCode.BadRequest, a25);
        Assert.Equal("invalid_grant", error.GetProperty("error").GetString());
        Assert.Contains("concesion_vencida", error.GetProperty("error_description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revocar_el_refresh_en_connect_revoke_lo_deja_inservible()
    {
        await using var api = await ApiAsync();
        var (tokens, navegador, _) = await FlujoCodigoAsync(api, IpNueva());
        using var _n = navegador;
        using var http = Cliente(api);
        var refresh = tokens.GetProperty("refresh_token").GetString()!;

        using var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = Cli,
            ["token"] = refresh,
            ["token_type_hint"] = "refresh_token",
        });
        using var revocacion = await http.PostAsync(new Uri(ServidorTokens.RutaRevocar, UriKind.Relative), contenido, Ct);
        var (estado, _) = await RefrescarAsync(http, refresh);

        Assert.Equal(HttpStatusCode.OK, revocacion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, estado);
    }

    // --- Ingreso desde /connect/authorize ---

    [Fact]
    public async Task Sin_sesion_y_con_prompt_none_se_responde_login_required_al_cliente()
    {
        await using var api = await ApiAsync();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.GetAsync(UrlAutorizar(Pkce.Nuevo(), prompt: "none"));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("login_required", ParametrosDe(respuesta.Headers.Location!)["error"]);
    }

    [Fact]
    public async Task Sin_sesion_y_sin_Entra_se_muestra_una_pagina_para_entrar_sin_scripts()
    {
        await using var api = await ApiAsync();
        using var navegador = Navegador.De(api);

        var respuesta = await navegador.GetAsync(UrlAutorizar(Pkce.Nuevo()));
        var html = await respuesta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("text/html", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'none'", respuesta.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/connect/authorize?", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_sesion_y_con_Entra_se_va_al_ingreso_con_Microsoft_y_se_vuelve_por_el_rebote()
    {
        var extra = new Dictionary<string, string>
        {
            [$"{ExtensionesIdentidad.SeccionEntra}:TenantId"] = "00000000-0000-0000-0000-00000000a71a",
            [$"{ExtensionesIdentidad.SeccionEntra}:ClientId"] = "00000000-0000-0000-0000-00000000c11e",
        };
        await using var api = (await ApiAsync(extra)).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<OpenIdConnectOptions>(Identidad.Sesiones.ClaimsAtlas.EsquemaEntra, o =>
            {
                // Sin red: metadatos fijos de un Entra ficticio.
                o.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://login.ejemplo.com/v2.0",
                    AuthorizationEndpoint = "https://login.ejemplo.com/authorize",
                    TokenEndpoint = "https://login.ejemplo.com/token",
                };
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(o.Configuration);
            })));
        using var navegador = Navegador.De(api);
        var url = UrlAutorizar(Pkce.Nuevo(), prompt: "login");

        var respuesta = await navegador.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var ubicacion = respuesta.Headers.Location!;
        Assert.StartsWith("https://login.ejemplo.com/authorize", ubicacion.ToString(), StringComparison.Ordinal);
        var parametros = ParametrosDe(ubicacion);
        Assert.Equal("https://localhost" + EndpointsIdentidad.RutaRetornoEntra, parametros["redirect_uri"]);
        var opciones = api.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(Identidad.Sesiones.ClaimsAtlas.EsquemaEntra);
        var propiedades = opciones.StateDataFormat.Unprotect(parametros["state"])!;
        Assert.StartsWith(ServidorTokens.RutaContinuar + "?", propiedades.RedirectUri, StringComparison.Ordinal);
        // Al volver no se vuelve a pedir el ingreso: el rebote quita prompt.
        Assert.DoesNotContain("prompt=", propiedades.RedirectUri, StringComparison.Ordinal);

        // El rebote es una página de Atlas que navega a /connect/authorize con la misma consulta (ahí sí viaja la
        // cookie SameSite=Strict).
        var rebote = await navegador.GetAsync(propiedades.RedirectUri!);
        var html = await rebote.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, rebote.StatusCode);
        Assert.Contains("http-equiv=\"refresh\"", html, StringComparison.Ordinal);
        Assert.Contains("url=/connect/authorize?", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    // --- Descubrimiento y JWKS (conformidad básica) ---

    private static async Task<OpenIdConnectConfiguration> DescubrimientoAsync(HttpClient http)
    {
        var json = await http.GetStringAsync(new Uri(ServidorTokens.RutaDescubrimiento, UriKind.Relative), Ct);
        var configuracion = OpenIdConnectConfiguration.Create(json);
        var jwks = await http.GetStringAsync(new Uri(configuracion.JwksUri), Ct);
        configuracion.JsonWebKeySet = new JsonWebKeySet(jwks);
        foreach (var llave in configuracion.JsonWebKeySet.GetSigningKeys())
        {
            configuracion.SigningKeys.Add(llave);
        }
        return configuracion;
    }

    [Fact]
    public async Task El_descubrimiento_cumple_OIDC_Discovery_y_anuncia_solo_lo_que_Atlas_permite()
    {
        await using var api = await ApiAsync();
        using var http = Cliente(api);

        var c = await DescubrimientoAsync(http);

        // Campos obligatorios de OpenID Connect Discovery 1.0 §3.
        Assert.Equal("https://localhost/", c.Issuer);
        Assert.Equal("https://localhost/connect/authorize", c.AuthorizationEndpoint);
        Assert.Equal("https://localhost/connect/token", c.TokenEndpoint);
        Assert.Equal("https://localhost/.well-known/jwks", c.JwksUri);
        Assert.Equal("https://localhost/connect/userinfo", c.UserInfoEndpoint);
        Assert.Contains("public", c.SubjectTypesSupported);
        Assert.Contains("RS256", c.IdTokenSigningAlgValuesSupported);
        Assert.Equal(["code"], c.ResponseTypesSupported);
        // Solo los flujos de Atlas: nada de implícito, híbrido ni contraseña.
        Assert.Equal(["authorization_code", "client_credentials", "refresh_token"], c.GrantTypesSupported.Order(StringComparer.Ordinal));
        Assert.Equal(["S256"], c.CodeChallengeMethodsSupported);
        Assert.Contains("atlas.mcp", c.ScopesSupported);
        Assert.Contains("atlas.publicar", c.ScopesSupported);
        Assert.Contains("atlas.widget", c.ScopesSupported);
        Assert.Contains("openid", c.ScopesSupported);
        Assert.Contains("offline_access", c.ScopesSupported);
        Assert.Contains("client_secret_basic", c.TokenEndpointAuthMethodsSupported);
        using var documento = JsonDocument.Parse(await http.GetStringAsync(new Uri(ServidorTokens.RutaDescubrimiento, UriKind.Relative), Ct));
        Assert.Equal("https://localhost/connect/revoke", documento.RootElement.GetProperty("revocation_endpoint").GetString());
        Assert.Equal("https://localhost/connect/introspect", documento.RootElement.GetProperty("introspection_endpoint").GetString());
        Assert.True(documento.RootElement.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
    }

    [Fact]
    public async Task El_JWKS_publica_solo_llaves_publicas_de_firma()
    {
        await using var api = await ApiAsync();
        using var http = Cliente(api);

        using var jwks = JsonDocument.Parse(await http.GetStringAsync(new Uri(ServidorTokens.RutaJwks, UriKind.Relative), Ct));

        var llaves = jwks.RootElement.GetProperty("keys").EnumerateArray().ToList();
        Assert.NotEmpty(llaves);
        foreach (var llave in llaves)
        {
            Assert.Equal("RSA", llave.GetProperty("kty").GetString());
            Assert.Equal("sig", llave.GetProperty("use").GetString());
            Assert.False(string.IsNullOrEmpty(llave.GetProperty("kid").GetString()));
            foreach (var privado in new[] { "d", "p", "q", "dp", "dq", "qi" })
            {
                Assert.False(llave.TryGetProperty(privado, out _), $"El JWKS expone «{privado}».");
            }
        }
    }

    // --- Llaves: certificados por configuración y rotación ---

    [Fact]
    public async Task Con_dos_certificados_de_firma_el_JWKS_publica_ambos_y_se_firma_con_el_vigente_que_vence_mas_tarde()
    {
        var ahora = DateTimeOffset.UtcNow;
        var carpeta = Certificados.Carpeta();
        try
        {
            var viejo = Certificados.Pfx(carpeta, "firma-vieja", X509KeyUsageFlags.DigitalSignature, ahora.AddDays(-30), ahora.AddDays(10));
            var nuevo = Certificados.Pfx(carpeta, "firma-nueva", X509KeyUsageFlags.DigitalSignature, ahora.AddDays(-1), ahora.AddDays(200));
            // Publicado antes de usarse: aún no vale, así que no firma.
            var futuro = Certificados.Pfx(carpeta, "firma-futura", X509KeyUsageFlags.DigitalSignature, ahora.AddDays(2), ahora.AddDays(400));
            var cifrado = Certificados.Pfx(carpeta, "cifrado", X509KeyUsageFlags.KeyEncipherment, ahora.AddDays(-1), ahora.AddDays(200));
            await using var api = await ApiAsync(new()
            {
                [OpcionesTokens.ClaveCertificadosFirma] = $"{viejo.Ruta};{nuevo.Ruta};{futuro.Ruta}",
                [OpcionesTokens.ClaveCertificadosCifrado] = cifrado.Ruta,
                [OpcionesTokens.ClaveClaveCertificados] = Certificados.Clave,
            });
            var (tokens, navegador, _) = await FlujoCodigoAsync(api, IpNueva());
            using var _n = navegador;
            using var http = Cliente(api);

            using var jwks = JsonDocument.Parse(await http.GetStringAsync(new Uri(ServidorTokens.RutaJwks, UriKind.Relative), Ct));
            var huellas = jwks.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("x5t").GetString()).ToList();
            Assert.Equal(3, huellas.Count);
            Assert.Contains(viejo.X5t, huellas);
            Assert.Contains(nuevo.X5t, huellas);
            Assert.Contains(futuro.X5t, huellas);

            var idToken = new JsonWebToken(tokens.GetProperty("id_token").GetString());
            var firmante = jwks.RootElement.GetProperty("keys").EnumerateArray().Single(k => k.GetProperty("kid").GetString() == idToken.Kid);
            Assert.Equal(nuevo.X5t, firmante.GetProperty("x5t").GetString());

            // Y el token de acceso (cifrado con el certificado de cifrado) vale en atlas-api.
            using var respuesta = await ConBearerAsync(http, "/api/v1/identidad/token/yo", tokens.GetProperty("access_token").GetString()!);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("pdn")]
    public void En_qa_y_pdn_sin_certificados_de_tokens_el_arranque_nombra_las_variables(string ambiente)
    {
        var carpeta = Certificados.Carpeta();
        try
        {
            var proteccion = Certificados.Pfx(carpeta, "proteccion", X509KeyUsageFlags.KeyEncipherment, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var api = entorno.Api(ambiente, simulado: false, configuracion: new Dictionary<string, string>
            {
                [$"{ExtensionesIdentidad.SeccionEntra}:TenantId"] = "00000000-0000-0000-0000-00000000a71a",
                [$"{ExtensionesIdentidad.SeccionEntra}:ClientId"] = "00000000-0000-0000-0000-00000000c11e",
                [ExtensionesIdentidad.ClaveCertificadoProteccion] = proteccion.Ruta,
                [ExtensionesIdentidad.ClaveClaveCertificadoProteccion] = Certificados.Clave,
            });

            var error = Assert.Throws<ConfiguracionFaltanteException>(() => api.CreateClient());

            Assert.Equal([OpcionesTokens.ClaveCertificadosFirma, OpcionesTokens.ClaveCertificadosCifrado], error.Claves);
            Assert.Contains("Identidad__Tokens__Certificados__Firma", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    // --- Configuración de clientes y scopes ---

    [Fact]
    public void Un_cliente_confidencial_sin_secreto_no_arranca_y_se_nombra_la_variable()
    {
        using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesTokens.Seccion}:Clientes:svc-sin-secreto:Tipo"] = "Confidencial",
            [$"{OpcionesTokens.Seccion}:Clientes:svc-sin-secreto:Flujos:0"] = "client_credentials",
            [$"{OpcionesTokens.Seccion}:Clientes:svc-sin-secreto:Scopes:0"] = "atlas.no_existe",
            [$"{OpcionesTokens.Seccion}:DuracionAcceso"] = "00:30:00",
        });

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateClient());

        Assert.Contains(error.Failures, f => f.Contains("Identidad__Tokens__Clientes__svc-sin-secreto__Secreto", StringComparison.Ordinal));
        Assert.Contains(error.Failures, f => f.Contains("atlas.no_existe", StringComparison.Ordinal));
        Assert.Contains(error.Failures, f => f.Contains("Identidad__Tokens__DuracionAcceso", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Los_clientes_y_scopes_se_sincronizan_al_arrancar_y_lo_que_sale_de_la_configuracion_se_borra()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var conTemporal = ConfiguracionClientes(new()
        {
            [$"{OpcionesTokens.Seccion}:Clientes:svc-temporal:Tipo"] = "Confidencial",
            [$"{OpcionesTokens.Seccion}:Clientes:svc-temporal:Secreto"] = Secreto,
            [$"{OpcionesTokens.Seccion}:Clientes:svc-temporal:Flujos:0"] = "client_credentials",
        });
        await using (var primera = entorno.Api(baseDeDatos: baseDeDatos, configuracion: conTemporal))
        {
            using var http = Cliente(primera);
            Assert.Equal(HttpStatusCode.OK, (await CredencialesDeServicioAsync(http, "svc-temporal")).Estado);
        }
        // Arrancar dos veces con la misma configuración no duplica nada.
        await using (var segunda = entorno.Api(baseDeDatos: baseDeDatos, configuracion: conTemporal))
        {
            segunda.CreateClient().Dispose();
        }
        await using (var tercera = entorno.Api(baseDeDatos: baseDeDatos, configuracion: ConfiguracionClientes()))
        {
            using var http = Cliente(tercera);
            Assert.Equal(HttpStatusCode.Unauthorized, (await CredencialesDeServicioAsync(http, "svc-temporal")).Estado);
        }

        await using var identidad = entorno.Identidad(baseDeDatos);
        var conexion = identidad.Database.GetDbConnection();
        await conexion.OpenAsync(Ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT client_id FROM atlas.identidad_oidc_aplicacion";
        var clientes = new List<string>();
        await using (var lector = await comando.ExecuteReaderAsync(Ct))
        {
            while (await lector.ReadAsync(Ct))
            {
                clientes.Add(lector.GetString(0));
            }
        }
        Assert.Equal(new[] { Cli, "atlas-mcp", Servicio, ServicioOtraAudiencia, ServicioSinScopes }.Order(StringComparer.Ordinal),
            clientes.Order(StringComparer.Ordinal));
        comando.CommandText = "SELECT count(*) FROM atlas.identidad_oidc_scope WHERE name IN ('atlas.mcp', 'atlas.publicar', 'atlas.widget')";
        Assert.Equal(3L, await comando.ExecuteScalarAsync(Ct));
        // El secreto no queda en claro.
        comando.CommandText = $"SELECT client_secret FROM atlas.identidad_oidc_aplicacion WHERE client_id = '{Servicio}'";
        var guardado = (string?)await comando.ExecuteScalarAsync(Ct);
        Assert.False(string.IsNullOrEmpty(guardado));
        Assert.DoesNotContain(Secreto, guardado, StringComparison.Ordinal);
    }

    [Fact]
    public void El_registro_de_scopes_exige_el_prefijo_atlas_y_no_admite_duplicados_entre_modulos()
    {
        Assert.Throws<InvalidOperationException>(() => new RegistroScopes([new Proveedor(new ScopeAtlas("otro.publicar", "x", "x"))]));
        Assert.Throws<InvalidOperationException>(() => new RegistroScopes([new Proveedor(new ScopeAtlas("atlas.Mayus", "x", "x"))]));
        Assert.Throws<InvalidOperationException>(() => new RegistroScopes(
            [new Proveedor(new ScopeAtlas("atlas.soporte", "soporte", "a")), new Proveedor(new ScopeAtlas("atlas.soporte", "equipo", "b"))]));

        var registro = new RegistroScopes([new ScopesAtlas.Base(), new Proveedor(new ScopeAtlas("atlas.soporte.leer", "soporte", "Leer turnos."))]);

        Assert.Equal(["atlas.mcp", "atlas.publicar", "atlas.soporte.leer", "atlas.widget"], registro.Nombres.Order(StringComparer.Ordinal));
    }

    private sealed class Proveedor(params ScopeAtlas[] scopes) : IProveedorScopes
    {
        public IEnumerable<ScopeAtlas> Scopes { get; } = scopes;
    }
}

/// <summary>Certificados autofirmados de un solo uso, como los que se generan con openssl para cada ambiente.</summary>
internal static class Certificados
{
    /// <summary>Clave de los .pfx de prueba, aleatoria por corrida.</summary>
    public static string Clave { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static string Carpeta()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"atlas-certificados-{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    public static (string Ruta, string X5t) Pfx(string carpeta, string nombre, X509KeyUsageFlags uso, DateTimeOffset desde, DateTimeOffset hasta)
    {
        using var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest($"CN=Atlas Pruebas {nombre}", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        solicitud.CertificateExtensions.Add(new X509KeyUsageExtension(uso, critical: true));
        using var certificado = solicitud.CreateSelfSigned(desde, hasta);
        var ruta = Path.Combine(carpeta, nombre + ".pfx");
        File.WriteAllBytes(ruta, certificado.Export(X509ContentType.Pfx, Clave));
        return (ruta, Base64Url.EncodeToString(certificado.GetCertHash()));
    }
}
