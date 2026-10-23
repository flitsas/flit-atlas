using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Api.Tests.EntornoApi))]

namespace Flit.Atlas.Api.Tests;

/// <summary>Postgres (con las migraciones aplicadas) y Redis reales, compartidos por las pruebas de la API.</summary>
public sealed class EntornoApi : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg16").Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:8.6").Build();

    private int _bases;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync());
        await MigrarAsync(Postgres.GetConnectionString());
    }

    /// <summary>Base nueva y migrada, para las pruebas que necesitan partir de cero (p. ej. el primer ingreso).</summary>
    public async Task<string> BaseNuevaAsync()
    {
        var nombre = $"atlas_api_{Interlocked.Increment(ref _bases)}";
        await using (var conexion = new NpgsqlConnection(Postgres.GetConnectionString()))
        {
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"CREATE DATABASE {nombre}", conexion);
            await comando.ExecuteNonQueryAsync();
        }
        var cadena = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = nombre }.ConnectionString;
        await MigrarAsync(cadena);
        return cadena;
    }

    private static async Task MigrarAsync(string cadena)
    {
        var servicios = new ServiceCollection()
            .Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity)
            .AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, cadena))
            .AddDbContext<IdentidadDbContext>(o => IdentidadDbContext.Configurar(o, cadena))
            .BuildServiceProvider();
        await using var alcance = servicios.CreateAsyncScope();
        var plataforma = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await plataforma.Database.MigrateAsync();
        await alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>().Database.MigrateAsync();
        // Lo mismo que hace el migrador después de las migraciones (ATLAS-ADR-0023).
        await SembradorPermisos.SembrarAsync(plataforma, CatalogoPermisos.Crear(PermisosIdentidad.ManifiestosDelNucleo));
    }

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Redis.DisposeAsync();
    }

    /// <summary>
    /// atlas-api contra los contenedores; el ambiente, las banderas, la configuración y el reloj se pueden cambiar por
    /// prueba.
    /// </summary>
    public WebApplicationFactory<Program> Api(
        string ambiente = "local", bool simulado = true, string? baseDeDatos = null,
        IReadOnlyDictionary<string, string>? configuracion = null, TimeProvider? reloj = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            foreach (var (clave, valor) in configuracion ?? new Dictionary<string, string>())
            {
                b.UseSetting(clave, valor);
            }
            b.ConfigureTestServices(s =>
            {
                // TestServer no trae IP remota: se simula un proxy de loopback para que X-Forwarded-For valga.
                s.AddSingleton<IStartupFilter, IpDeLoopback>();
                s.AddSingleton<IStartupFilter, IngresoDePrueba>();
                if (reloj is not null)
                {
                    s.AddSingleton(reloj);
                }
            });
            b.UseSetting("ConnectionStrings:atlas", baseDeDatos ?? Postgres.GetConnectionString());
            b.UseSetting("ConnectionStrings:redis", Redis.GetConnectionString());
            // No se usa en estas pruebas: la outbox solo abre la conexión cuando hay eventos por publicar.
            b.UseSetting("ConnectionStrings:rabbitmq", "amqp://x:x@127.0.0.1:1");
            b.UseSetting("Atlas:Ambiente", ambiente);
            b.UseSetting(ExtensionesIdentidad.ClaveSimulado, simulado ? "true" : "false");
        });

    /// <summary>Grupo de Entra nuevo que otorga <see cref="RolesAtlas.Superadmin"/> (administración de identidad y de roles).</summary>
    public Task<Guid> GrupoAdministradorAsync(string? baseDeDatos = null) => GrupoConRolAsync(RolesAtlas.Superadmin, baseDeDatos);

    /// <summary>Grupo de Entra nuevo que otorga un rol interno sembrado.</summary>
    public async Task<Guid> GrupoConRolAsync(string rol, string? baseDeDatos = null)
    {
        var grupo = Guid.NewGuid();
        await using var plataforma = Plataforma(baseDeDatos);
        var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync();
        plataforma.MapeosGrupoEntra.Add(new MapeoGrupoEntra { GrupoId = grupo, GrupoNombre = $"FLIT-Prueba-{rol}", RolId = rolId });
        await plataforma.SaveChangesAsync();
        return grupo;
    }

    /// <summary>
    /// Persona externa de una organización cliente nueva, con su usuario y una asignación del rol externo (las cuentas
    /// locales y la invitación llegan en la HU 1.5). Entra con <see cref="Navegador.EntrarComoAsync"/>.
    /// </summary>
    public async Task<Externo> ExternoAsync(
        WebApplicationFactory<Program> api, string rol, DateTimeOffset vigenteHasta, string? baseDeDatos = null)
    {
        await using var plataforma = Plataforma(baseDeDatos);
        var organizacion = new Organizacion { Nombre = $"Organismo Demo {Guid.NewGuid():N}", Tipo = TipoOrganizacion.Cliente };
        var persona = new Persona
        {
            Tipo = TipoPersona.Externo,
            OrganizacionId = organizacion.Id,
            Nombre = "Externa Demo",
            Correo = $"externa.{Guid.NewGuid():N}@ejemplo.com",
        };
        var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync();
        var asignacion = new AsignacionRol
        {
            PersonaId = persona.Id,
            RolId = rolId,
            OrganizacionId = organizacion.Id,
            VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
            VigenteHasta = vigenteHasta,
        };
        plataforma.AddRange(organizacion, persona, asignacion);
        await plataforma.SaveChangesAsync();

        await using var alcance = api.Services.CreateAsyncScope();
        var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
        var usuario = new UsuarioAtlas { PersonaId = persona.Id, UserName = persona.Correo, Email = persona.Correo, CreadoEn = DateTimeOffset.UtcNow };
        var creado = await usuarios.CreateAsync(usuario);
        Assert.True(creado.Succeeded);
        return new Externo(usuario.Id, persona.Id, organizacion.Id, asignacion.Id);
    }

    public IdentidadDbContext Identidad(string? baseDeDatos = null)
    {
        var servicios = new ServiceCollection().Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity).BuildServiceProvider();
        var opciones = new DbContextOptionsBuilder<IdentidadDbContext>().UseApplicationServiceProvider(servicios);
        IdentidadDbContext.Configurar(opciones, baseDeDatos ?? Postgres.GetConnectionString());
        return new IdentidadDbContext(opciones.Options);
    }

    private sealed class IpDeLoopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, siguiente) =>
            {
                http.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return siguiente(http);
            });
            next(app);
        };
    }

    /// <summary>
    /// Solo en pruebas: <c>POST /pruebas/entrar</c> con <c>{ usuarioId }</c> abre una sesión real del BFF (cookie y fila
    /// en <c>identidad_sesion</c>) para un usuario existente, empleado o externo. Los externos aún no tienen cómo entrar
    /// (HU 1.5).
    /// </summary>
    private sealed class IngresoDePrueba : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, siguiente) =>
            {
                if (!http.Request.Path.Equals(Navegador.RutaIngresoPrueba, StringComparison.Ordinal) || !HttpMethods.IsPost(http.Request.Method))
                {
                    await siguiente(http);
                    return;
                }
                var pedido = await http.Request.ReadFromJsonAsync<PedidoIngresoPrueba>();
                var usuarios = http.RequestServices.GetRequiredService<UserManager<UsuarioAtlas>>();
                var usuario = (await usuarios.FindByIdAsync(pedido!.UsuarioId.ToString()))!;
                var plataforma = http.RequestServices.GetRequiredService<PlataformaDbContext>();
                var persona = await plataforma.Personas.SingleAsync(p => p.Id == usuario.PersonaId);
                var ahora = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                var identidad = new ClaimsIdentity(ClaimsAtlas.EsquemaCookie, ClaimsAtlas.Nombre, ClaimsAtlas.Rol);
                identidad.AddClaim(new Claim(ClaimsAtlas.Sub, usuario.Id.ToString()));
                identidad.AddClaim(new Claim(ClaimsAtlas.PersonaId, persona.Id.ToString()));
                identidad.AddClaim(new Claim(ClaimsAtlas.TipoPrincipal,
                    persona.Tipo == TipoPersona.Externo ? ClaimsAtlas.TipoExterno : ClaimsAtlas.TipoEmpleado));
                identidad.AddClaim(new Claim(ClaimsAtlas.OrganizacionId, persona.OrganizacionId.ToString()));
                identidad.AddClaim(new Claim(ClaimsAtlas.Nombre, persona.Nombre));
                identidad.AddClaim(new Claim(ClaimsAtlas.Proveedor, "prueba"));
                identidad.AddClaim(new Claim(ClaimsAtlas.AuthTime, ahora.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
                identidad.AddClaim(new Claim(ClaimsAtlas.SelloSeguridad, usuario.SecurityStamp ?? string.Empty));
                await http.SignInAsync(ClaimsAtlas.EsquemaCookie, new ClaimsPrincipal(identidad), new AuthenticationProperties { IsPersistent = true });
                http.Response.StatusCode = StatusCodes.Status204NoContent;
            });
            next(app);
        };
    }

    public PlataformaDbContext Plataforma(string? baseDeDatos = null)
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, baseDeDatos ?? Postgres.GetConnectionString());
        return new PlataformaDbContext(opciones.Options);
    }
}

/// <summary>
/// Cliente HTTPS que maneja la cookie a mano: así las pruebas ven sus atributos y pueden reusar una cookie vieja.
/// </summary>
public sealed class Navegador(HttpClient http) : IDisposable
{
    public string? Cookie { get; set; }

    /// <summary>IP del cliente que el proxy de loopback informa en <c>X-Forwarded-For</c> (usa IPs de documentación).</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent del navegador simulado.</summary>
    public string? Agente { get; set; }

    public HttpResponseMessage? UltimaRespuesta { get; private set; }

    public static Navegador De(WebApplicationFactory<Program> api) =>
        new(api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        }));

    public Task<HttpResponseMessage> EntrarSimuladoAsync(string correo, string nombre, params Guid[] grupos) =>
        EnviarAsync(HttpMethod.Post, "/api/v1/identidad/simulado/entrar", JsonContent.Create(new { correo, nombre, grupos }));

    /// <summary>Vuelve a entrar porque una acción sensible pidió reautenticación.</summary>
    public Task<HttpResponseMessage> ReingresarSimuladoAsync(string correo, string nombre, params Guid[] grupos) =>
        EnviarAsync(HttpMethod.Post, "/api/v1/identidad/simulado/entrar",
            JsonContent.Create(new { correo, nombre, grupos, reautenticar = true }));

    public Task<HttpResponseMessage> GetAsync(string ruta) => EnviarAsync(HttpMethod.Get, ruta);

    /// <summary>Ruta del ingreso de prueba (solo existe en <see cref="EntornoApi.Api"/>).</summary>
    public const string RutaIngresoPrueba = "/pruebas/entrar";

    /// <summary>Abre una sesión para un usuario existente (empleado o externo) con el ingreso de prueba.</summary>
    public Task<HttpResponseMessage> EntrarComoAsync(Guid usuarioId) =>
        EnviarAsync(HttpMethod.Post, RutaIngresoPrueba, JsonContent.Create(new PedidoIngresoPrueba(usuarioId)));

    public async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string ruta, HttpContent? cuerpo = null, bool csrf = true, string? origen = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        if (csrf)
        {
            peticion.Headers.Add(EndpointsIdentidad.CabeceraCsrf, "1");
        }
        if (origen is not null)
        {
            peticion.Headers.Add("Origin", origen);
        }
        if (Cookie is not null)
        {
            peticion.Headers.Add("Cookie", Cookie);
        }
        if (Ip is not null)
        {
            peticion.Headers.Add("X-Forwarded-For", Ip);
        }
        if (Agente is not null)
        {
            peticion.Headers.TryAddWithoutValidation("User-Agent", Agente);
        }
        var respuesta = await http.SendAsync(peticion, TestContext.Current.CancellationToken);
        if (respuesta.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var atlas = cookies.FirstOrDefault(c => c.StartsWith(Identidad.Sesiones.ClaimsAtlas.NombreCookie + "=", StringComparison.Ordinal));
            if (atlas is not null)
            {
                var par = atlas.Split(';')[0];
                Cookie = par.EndsWith('=') ? null : par;
            }
        }
        UltimaRespuesta = respuesta;
        return respuesta;
    }

    public void Dispose() => http.Dispose();
}

public sealed record PedidoIngresoPrueba(Guid UsuarioId);

public sealed record Externo(Guid UsuarioId, Guid PersonaId, Guid OrganizacionId, Guid AsignacionId);

/// <summary>Reloj real con un desfase que la prueba puede adelantar (p. ej. para vencer la ventana de reautenticación).</summary>
public sealed class RelojDePrueba : TimeProvider
{
    private TimeSpan _desfase;

    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + _desfase;

    public void Adelantar(TimeSpan tiempo) => _desfase += tiempo;
}
