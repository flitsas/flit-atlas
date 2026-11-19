using System.Net;
using System.Net.Http.Json;
using System.Text;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Tokens;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// CA de la HU 1.4 (ATLAS-ADR-0023): pruebas de autorización parametrizadas que recorren <b>todos</b> los endpoints
/// (descubiertos de <see cref="EndpointDataSource"/>, no de una lista a mano) con <b>todos</b> los roles. Un endpoint
/// nuevo sin permiso declarado hace fallar la prueba, salvo que se agregue a propósito a <see cref="Publicos"/> o a
/// <see cref="SoloSesion"/>.
/// </summary>
public class AutorizacionTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Endpoints sin sesión. Agregar uno aquí es una decisión de seguridad: va en la revisión del PR.</summary>
    public static readonly IReadOnlySet<string> Publicos = new HashSet<string>(StringComparer.Ordinal)
    {
        "* /health/live",
        "* /health/ready",
        "GET /openapi/{documentName}.yaml",
        "GET /api/v1/version",
        "GET /api/v1/identidad/proveedores",
        "GET /api/v1/identidad/entrar/microsoft",
        "POST /api/v1/identidad/simulado/entrar",
        "POST /api/v1/identidad/salir",
        // HU 1.5 (ATLAS-ADR-0025): el camino para tener sesión de un externo. Sin sesión, con el límite de tasa de
        // externos; cada una valida su enlace de un solo uso, su paso temporal o sus credenciales.
        "GET /api/v1/identidad/terminos",
        "POST /api/v1/identidad/invitaciones/consultar",
        "POST /api/v1/identidad/invitaciones/passkey/opciones",
        "POST /api/v1/identidad/invitaciones/totp/clave",
        "POST /api/v1/identidad/invitaciones/aceptar",
        "POST /api/v1/identidad/entrar/local",
        "POST /api/v1/identidad/entrar/local/segundo-factor",
        "POST /api/v1/identidad/entrar/passkey/opciones",
        "POST /api/v1/identidad/entrar/passkey",
        "POST /api/v1/identidad/entrar/terminos",
        "POST /api/v1/identidad/recuperacion/",
        "POST /api/v1/identidad/recuperacion/consultar",
        "POST /api/v1/identidad/recuperacion/passkey/opciones",
        "POST /api/v1/identidad/recuperacion/totp/clave",
        "POST /api/v1/identidad/recuperacion/completar",
        // ATLAS-ADR-0027: activación de la cuenta de emergencia con el enlace de un solo uso que imprime el comando.
        "POST /api/v1/identidad/emergencia/consultar",
        "POST /api/v1/identidad/emergencia/passkey/opciones",
        "POST /api/v1/identidad/emergencia/totp/clave",
        "POST /api/v1/identidad/emergencia/activar",
        // Protocolo OAuth/OIDC (HU 1.3c, ATLAS-ADR-0024): OpenIddict autentica al cliente (secreto, PKCE, código o
        // refresh) antes de dejar pasar la petición; authorize toma al usuario de la sesión del BFF si la hay.
        "GET /connect/authorize",
        "POST /connect/authorize",
        "GET /connect/continuar",
        "POST /connect/token",
        "GET /connect/userinfo",
        "POST /connect/userinfo",
    };

    /// <summary>Endpoints que solo exigen sesión: actúan sobre los datos de la propia persona.</summary>
    public static readonly IReadOnlySet<string> SoloSesion = new HashSet<string>(StringComparer.Ordinal)
    {
        "GET /api/v1/plataforma/yo",
        "GET /api/v1/identidad/sesiones/",
        "DELETE /api/v1/identidad/sesiones/{id:guid}",
        "POST /api/v1/identidad/sesiones/cerrar-otras",
        "GET /api/v1/plataforma/banderas",
        "GET /api/v1/plataforma/notificaciones/",
        "POST /api/v1/plataforma/notificaciones/{id:guid}/leida",
        "POST /api/v1/plataforma/notificaciones/leidas",
        "GET /api/v1/plataforma/notificaciones/preferencias",
        "PUT /api/v1/plataforma/notificaciones/preferencias",
        "GET /api/v1/plataforma/push/clave",
        "POST /api/v1/plataforma/push/suscripciones",
        "POST /api/v1/plataforma/push/suscripciones/baja",
        "POST /api/v1/plataforma/push/prueba",
    };

    /// <param name="Clave"><c>MÉTODO patrón</c>, como en <see cref="Publicos"/>.</param>
    /// <param name="Permisos">Permisos que exigen sus políticas; vacío si es público o de solo sesión.</param>
    /// <param name="Scopes">
    /// Scopes que exige si solo acepta tokens Bearer (<c>RequiereScope</c>, ATLAS-ADR-0024). Con la cookie, cualquier rol
    /// recibe 401: el scope cuenta como su declaración.
    /// </param>
    public sealed record EndpointDescubierto(
        string Clave, string Metodo, string Patron, bool ExigeSesion, IReadOnlyList<string> Permisos, IReadOnlyList<string> Scopes);

    /// <summary>Todos los endpoints de la API, con lo que exige cada uno según su metadata y sus políticas.</summary>
    public static async Task<List<EndpointDescubierto>> DescubrirAsync(WebApplicationFactory<Program> api)
    {
        using var _ = api.CreateClient();
        var politicas = api.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var lista = new List<EndpointDescubierto>();
        foreach (var endpoint in api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var autorizacion = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var exige = autorizacion.Count > 0 && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
            var permisos = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var nombre in autorizacion.Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p)))
            {
                var politica = await politicas.GetPolicyAsync(nombre!);
                Assert.True(politica is not null, $"{endpoint.DisplayName}: la política «{nombre}» no existe (¿permiso fuera de los manifiestos?).");
                permisos.UnionWith(politica!.Requirements.OfType<RequisitoPermiso>().Select(r => r.Permiso));
            }
            var patron = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "?";
            foreach (var metodo in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
            {
                lista.Add(new EndpointDescubierto($"{metodo} {patron}", metodo, patron, exige, [.. permisos],
                    endpoint.Metadata.GetMetadata<MetadatoScope>()?.Scopes ?? []));
            }
        }
        return lista;
    }

    private static List<string> ProblemasDeDeclaracion(IEnumerable<EndpointDescubierto> endpoints) =>
    [
        .. endpoints.Select(e => (e.ExigeSesion, e.Permisos.Count > 0 || e.Scopes.Count > 0, Publicos.Contains(e.Clave), SoloSesion.Contains(e.Clave)) switch
        {
            (false, _, true, _) => null,
            (false, _, false, _) => $"{e.Clave}: no exige sesión y no está en la lista de públicos",
            (true, _, true, _) => $"{e.Clave}: está en la lista de públicos pero exige sesión",
            (true, false, _, true) => null,
            (true, false, _, false) => $"{e.Clave}: solo exige sesión y no declara un permiso (RequierePermiso) ni está en SoloSesion",
            (true, true, _, true) => $"{e.Clave}: declara permiso pero está en SoloSesion",
            _ => null,
        }).OfType<string>(),
    ];

    [Fact]
    public async Task Todo_endpoint_declara_un_permiso_o_esta_en_una_lista_explicita()
    {
        var catalogo = CatalogoPermisos.Crear(Composicion.ModulosAtlas.Manifiestos);
        // Las dos configuraciones: la de pruebas (simulado) y la de qa/pdn (Entra), que mapean endpoints distintos.
        await using var local = entorno.Api();
        await using var comoQa = ContratoOpenApiTests.ApiComoQa();
        var endpoints = (await DescubrirAsync(local)).Concat(await DescubrirAsync(comoQa)).DistinctBy(e => e.Clave).ToList();

        var problemas = ProblemasDeDeclaracion(endpoints);
        Assert.True(problemas.Count == 0, string.Join('\n', problemas));
        Assert.All(endpoints.SelectMany(e => e.Permisos), p => Assert.True(catalogo.Existe(p), p));
        // Las listas no guardan endpoints que ya no existen.
        var sobrantes = Publicos.Concat(SoloSesion).Except(endpoints.Select(e => e.Clave)).ToList();
        Assert.True(sobrantes.Count == 0, "En las listas pero no en la API: " + string.Join(", ", sobrantes));
        // Hay endpoints protegidos por permiso: si no, la prueba de roles no probaría nada.
        Assert.Contains(endpoints, e => e.Permisos.Contains(PermisosPlataforma.RolesGestionar));
        Assert.Contains(endpoints, e => e.Permisos.Contains(Identidad.Autorizacion.PermisosIdentidad.Administrar));
    }

    /// <summary>Todos los roles asignables del §7 más <c>atlas.lector</c> (empleado sin asignaciones).</summary>
    public static TheoryData<string> Roles() =>
        [.. RolesAtlas.Todos.Where(r => r.Tipo != TipoRol.Implicito).Select(r => r.Codigo)];

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Cada_rol_entra_exactamente_a_los_endpoints_de_sus_permisos(string rol)
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:PorUsuario"] = "1000",
            [$"{OpcionesLimiteTasa.Seccion}:PorIp"] = "1000",
        });
        var catalogo = api.Services.GetRequiredService<CatalogoPermisos>();
        var definicion = catalogo.Rol(rol)!;
        using var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::14:{Random.Shared.Next(1, 0xffff):x}";
        var esperados = new HashSet<string>(catalogo.PermisosDe(rol), StringComparer.Ordinal);
        if (definicion.Tipo == TipoRol.Externo)
        {
            // Vigencia larga (dentro del tope de 90 días): las pruebas que adelantan el reloj (RolesTests +2 h, TokensTests
            // +31 días) corren el vigilante de vencimientos sobre esta misma base y, con una vigencia corta, vencían esta
            // asignación e invalidaban la sesión a mitad de la prueba (401 al azar).
            var externo = await entorno.ExternoAsync(api, rol, DateTimeOffset.UtcNow.AddDays(80));
            Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(externo.UsuarioId)).StatusCode);
        }
        else
        {
            var correo = $"rol.{Guid.NewGuid():N}@ejemplo.com";
            Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Rol Demo")).StatusCode);
            if (rol != RolesAtlas.Lector)
            {
                await AsignarAsync(correo, rol);
            }
            esperados.UnionWith(catalogo.PermisosDe(RolesAtlas.Lector));
        }

        var yo = await (await navegador.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        Assert.Equal(esperados.Order(StringComparer.Ordinal), yo!.Permisos);

        var fallas = new List<string>();
        foreach (var endpoint in (await DescubrirAsync(api)).Where(e => e.ExigeSesion))
        {
            var estado = (await LlamarAsync(navegador, endpoint)).StatusCode;
            if (endpoint.Scopes.Count > 0)
            {
                // Solo Bearer: la cookie de ningún rol basta (TokensTests prueba el acceso con token).
                if (estado != HttpStatusCode.Unauthorized)
                {
                    fallas.Add($"{endpoint.Clave} [scope {string.Join(", ", endpoint.Scopes)}]: {(int)estado}, se esperaba 401 con la cookie");
                }
                continue;
            }
            var permitido = endpoint.Permisos.All(esperados.Contains);
            var negado = estado is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            if (permitido ? negado : estado != HttpStatusCode.Forbidden)
            {
                fallas.Add($"{endpoint.Clave} [{string.Join(", ", endpoint.Permisos)}]: {(int)estado}, se esperaba {(permitido ? "acceso" : "403")}");
            }
        }
        Assert.True(fallas.Count == 0, $"{rol}:\n" + string.Join('\n', fallas));
    }

    [Fact]
    public async Task Sin_sesion_todo_endpoint_protegido_responde_401()
    {
        await using var api = entorno.Api();
        using var anonimo = Navegador.De(api);
        anonimo.Ip = $"2001:db8::15:{Random.Shared.Next(1, 0xffff):x}";

        var fallas = new List<string>();
        foreach (var endpoint in (await DescubrirAsync(api)).Where(e => e.ExigeSesion))
        {
            var estado = (await LlamarAsync(anonimo, endpoint)).StatusCode;
            if (estado != HttpStatusCode.Unauthorized)
            {
                fallas.Add($"{endpoint.Clave}: {(int)estado}");
            }
        }

        Assert.Empty(fallas);
    }

    /// <summary>Llama al endpoint con ids nuevos en la ruta y, si cambia estado, un cuerpo JSON vacío.</summary>
    private static Task<HttpResponseMessage> LlamarAsync(Navegador navegador, EndpointDescubierto endpoint)
    {
        var ruta = new StringBuilder();
        foreach (var segmento in endpoint.Patron.Split('/'))
        {
            ruta.Append(segmento.StartsWith('{') ? Guid.NewGuid().ToString() : segmento).Append('/');
        }
        var url = ruta.ToString().TrimEnd('/');
        var metodo = new HttpMethod(endpoint.Metodo == "*" ? "GET" : endpoint.Metodo);
        var cuerpo = HttpMethods.IsGet(metodo.Method) || HttpMethods.IsDelete(metodo.Method)
            ? null
            : new StringContent("{}", Encoding.UTF8, "application/json");
        return navegador.EnviarAsync(metodo, url.Length == 0 ? "/" : url, cuerpo);
    }

    private async Task AsignarAsync(string correo, string rol)
    {
        await using var plataforma = entorno.Plataforma();
        var persona = await plataforma.Personas.SingleAsync(p => p.Correo == correo, Ct);
        var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync(Ct);
        plataforma.AsignacionesRol.Add(new AsignacionRol
        {
            PersonaId = persona.Id, RolId = rolId, OrganizacionId = persona.OrganizacionId, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await plataforma.SaveChangesAsync(Ct);
    }
}
