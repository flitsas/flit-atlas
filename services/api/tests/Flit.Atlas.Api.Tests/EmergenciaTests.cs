using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Emergencia;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Cuenta de administrador de emergencia (ATLAS-ADR-0027): el comando crea la cuenta y emite un enlace de un solo uso; con
/// el enlace se activa con passkey o contraseña y TOTP; entra por el ingreso local con MFA y es superadmin; volver a correr
/// el comando invalida el enlace anterior, quita los factores y cierra las sesiones; el token no queda en los logs; un
/// empleado normal sigue sin poder usar el ingreso local. Cada prueba usa su propia base (la cuenta es una por ambiente).
/// </summary>
public partial class EmergenciaTests(EntornoApi entorno)
{
    /// <summary>Contraseña de prueba: larga, propia y no filtrada (la revisión de HIBP está apagada en las pruebas).</summary>
    private const string Contrasena = "Cordillera central al atardecer 2026";

    private const string Correo = "admin.emergencia@atlas.local";

    /// <summary>Tipos de evento de la cuenta de emergencia; <see cref="Todo_el_flujo_queda_en_la_auditoria_y_el_token_no_aparece_en_logs"/> los recorre.</summary>
    public static readonly string[] TiposDeEventoProbados =
    [
        TiposEventoSeguridad.EmergenciaCuentaCreada, TiposEventoSeguridad.EmergenciaEnlaceEmitido,
        TiposEventoSeguridad.EmergenciaActivada, TiposEventoSeguridad.EmergenciaIngreso,
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IpNueva() => $"2001:db8::27:{Random.Shared.Next(1, 0xffff):x}";

    private static Navegador Publico(WebApplicationFactory<Program> api)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        return navegador;
    }

    private static Task<HttpResponseMessage> PostAsync(Navegador navegador, string ruta, object cuerpo) =>
        navegador.EnviarAsync(HttpMethod.Post, ruta, JsonContent.Create(cuerpo), origen: "https://localhost");

    /// <summary>Corre el comando como en el contenedor (mismos servicios) y devuelve su salida y el fragmento del enlace.</summary>
    private static async Task<(int Codigo, string Salida, string Enlace)> ComandoAsync(WebApplicationFactory<Program> api)
    {
        using var salida = new StringWriter();
        using var errores = new StringWriter();
        var codigo = await ComandoEmergencia.EjecutarAsync(api.Services, ["emergencia", "activar"], salida, errores, Ct);
        var texto = salida.ToString();
        var enlace = EnlaceEnSalida().Match(texto);
        return (codigo, texto + errores, enlace.Success ? enlace.Groups[1].Value : string.Empty);
    }

    [GeneratedRegex("https://localhost/emergencia#([A-Za-z0-9_.\\-]+)")]
    private static partial Regex EnlaceEnSalida();

    private static async Task<(string Clave, CuentaLista Cuenta)> ActivarConContrasenaAsync(Navegador navegador, string enlace)
    {
        var clave = (await (await PostAsync(navegador, "/api/v1/identidad/emergencia/totp/clave", new { enlace })).Content.ReadFromJsonAsync<ClaveTotp>(Ct))!;
        var respuesta = await PostAsync(navegador, "/api/v1/identidad/emergencia/activar", new
        {
            enlace,
            factor = new { metodo = "contrasena", estado = clave.Estado, contrasena = Contrasena, codigo = Totp.Codigo(clave.Clave) },
        });
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(Ct));
        return (clave.Clave, (await respuesta.Content.ReadFromJsonAsync<CuentaLista>(Ct))!);
    }

    private static async Task ActivarConPasskeyAsync(Navegador navegador, string enlace, AutenticadorDePrueba autenticador)
    {
        var opciones = (await (await PostAsync(navegador, "/api/v1/identidad/emergencia/passkey/opciones", new { enlace }))
            .Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        var respuesta = await PostAsync(navegador, "/api/v1/identidad/emergencia/activar", new
        {
            enlace,
            factor = new { metodo = "passkey", estado = opciones.Estado, credencial = autenticador.Registrar(opciones.Opciones) },
        });
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(Ct));
        Assert.True((await respuesta.Content.ReadFromJsonAsync<CuentaLista>(Ct))!.SesionIniciada);
    }

    private static async Task<HttpResponseMessage> EntrarConPasskeyAsync(Navegador navegador, AutenticadorDePrueba autenticador)
    {
        var ingreso = (await (await PostAsync(navegador, "/api/v1/identidad/entrar/passkey/opciones", new { })).Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        return await PostAsync(navegador, "/api/v1/identidad/entrar/passkey", new { estado = ingreso.Estado, credencial = autenticador.Firmar(ingreso.Opciones) });
    }

    private static async Task<HttpResponseMessage> EntrarConContrasenaAsync(Navegador navegador, string correo, string clave)
    {
        var paso = await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });
        if (paso.StatusCode != HttpStatusCode.OK)
        {
            return paso;
        }
        var estado = (await paso.Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        return await PostAsync(navegador, "/api/v1/identidad/entrar/local/segundo-factor", new { estado, codigo = Totp.Codigo(clave, desfasePasos: 1) });
    }

    private static async Task<EndpointsIdentidad.Yo> YoAsync(Navegador navegador)
    {
        var respuesta = await navegador.GetAsync("/api/v1/plataforma/yo");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct))!;
    }

    /// <summary>
    /// F4-SEC-21 (ATLAS-ADR-0049): el comando quitaba todos los proveedores de log y identidad.emergencia.cuenta_creada y
    /// .enlace_emitido no llegaban a Loki. Ahora queda solo el de OpenTelemetry (OTLP al colector), y solo para las
    /// advertencias de RegistroSeguridad (los eventos de alta severidad, con tipo e id): nada a la consola.
    /// </summary>
    [Fact]
    public void Los_logs_del_comando_solo_llevan_los_eventos_de_alta_severidad_a_OpenTelemetry()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging(l =>
        {
            l.AddConsole();
            ComandoEmergencia.ConfigurarLogs(l);
        });
        using var proveedor = servicios.BuildServiceProvider();

        var proveedores = proveedor.GetServices<ILoggerProvider>().Select(p => p.GetType().Name).ToList();
        Assert.Equal(["OpenTelemetryLoggerProvider"], proveedores);
        var fabrica = proveedor.GetRequiredService<ILoggerFactory>();
        Assert.True(fabrica.CreateLogger<RegistroSeguridad>().IsEnabled(LogLevel.Warning));
        Assert.False(fabrica.CreateLogger<RegistroSeguridad>().IsEnabled(LogLevel.Information));
        Assert.False(fabrica.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Warning));
        Assert.False(fabrica.CreateLogger<CuentaEmergencia>().IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public async Task El_comando_crea_la_cuenta_y_con_el_enlace_se_activa_con_contrasena_y_TOTP_y_entra_como_superadmin()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);

        var (codigo, salida, enlace) = await ComandoAsync(api);
        Assert.Equal(0, codigo);
        Assert.Contains(Correo, salida, StringComparison.Ordinal);
        Assert.Contains("Cuenta creada ahora", salida, StringComparison.Ordinal);
        Assert.NotEmpty(enlace);

        // Una persona de la organización interna con atlas.superadmin sin vencimiento, de origen emergencia.
        await using (var plataforma = entorno.Plataforma(baseNueva))
        {
            var persona = await plataforma.Personas.SingleAsync(p => p.Correo == Correo, Ct);
            Assert.Equal(TipoPersona.Empleado, persona.Tipo);
            Assert.Null(persona.EntraOid);
            Assert.True(await plataforma.Organizaciones.AnyAsync(o => o.Id == persona.OrganizacionId && o.Tipo == TipoOrganizacion.Interna, Ct));
            var asignacion = await plataforma.AsignacionesRol.SingleAsync(a => a.PersonaId == persona.Id, Ct);
            Assert.Equal(OrigenAsignacion.Emergencia, asignacion.Origen);
            Assert.Null(asignacion.VigenteHasta);
            Assert.Equal(RolesAtlas.Superadmin, await plataforma.Roles.Where(r => r.Id == asignacion.RolId).Select(r => r.Codigo).SingleAsync(Ct));
        }

        // Sin contraseña por defecto: antes de activar no hay con qué entrar.
        using (var antes = Publico(api))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(antes, "/api/v1/identidad/entrar/local", new { correo = Correo, contrasena = Contrasena })).StatusCode);
        }

        using var navegador = Publico(api);
        var consulta = await PostAsync(navegador, "/api/v1/identidad/emergencia/consultar", new { enlace });
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        Assert.Equal(Correo, (await consulta.Content.ReadFromJsonAsync<CuentaEmergenciaPublica>(Ct))!.Correo);

        var (clave, cuenta) = await ActivarConContrasenaAsync(navegador, enlace);
        Assert.True(cuenta.SesionIniciada);
        Assert.Equal(FactoresExternos.CodigosRecuperacion, cuenta.CodigosRecuperacion.Count);

        var yo = await YoAsync(navegador);
        Assert.True(yo.Emergencia);
        Assert.Equal(CuentaEmergencia.Proveedor, yo.Proveedor);
        Assert.Equal("empleado", yo.TipoPrincipal);
        Assert.Contains(RolesAtlas.Superadmin, yo.Roles);
        Assert.Contains(PermisosPlataforma.RolesGestionar, yo.Permisos);
        Assert.Contains(PermisosPlataforma.InvitacionesGestionar, yo.Permisos);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/invitaciones")).StatusCode);
        // El paso de arranque: mapear el grupo de Entra de administración a superadmin (exige reautenticación reciente).
        var mapeo = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/mapeos-grupos-entra", JsonContent.Create(new
        {
            grupoId = Guid.NewGuid(), grupoNombre = "FLIT-Atlas-Administradores", rol = RolesAtlas.Superadmin,
        }));
        Assert.Equal(HttpStatusCode.Created, mapeo.StatusCode);

        // El enlace es de un solo uso.
        using (var otro = Publico(api))
        {
            Assert.Equal(HttpStatusCode.Gone, (await PostAsync(otro, "/api/v1/identidad/emergencia/consultar", new { enlace })).StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await PostAsync(otro, "/api/v1/identidad/emergencia/activar", new
            {
                enlace,
                factor = new { metodo = "contrasena", estado = "x", contrasena = Contrasena, codigo = "000000" },
            })).StatusCode);
        }

        // Entra por el ingreso local, con contraseña y TOTP (nunca solo con la contraseña).
        await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");
        using var luego = Publico(api);
        var entrar = await EntrarConContrasenaAsync(luego, Correo, clave);
        Assert.Equal(HttpStatusCode.NoContent, entrar.StatusCode);
        var otraVez = await YoAsync(luego);
        Assert.True(otraVez.Emergencia);
        Assert.Equal(yo.PersonaId, otraVez.PersonaId);

        await using var identidad = entorno.Identidad(baseNueva);
        var ingresos = await identidad.EventosSeguridad.CountAsync(e => e.Tipo == TiposEventoSeguridad.EmergenciaIngreso, Ct);
        Assert.Equal(2, ingresos);
        // La sesión de la cuenta de emergencia tiene el tope corto de las cuentas locales.
        var sesion = await identidad.Sesiones.Where(s => s.UsuarioId == otraVez.Sub && s.RevocadaEn == null).SingleAsync(Ct);
        Assert.True(sesion.ExpiraAbsolutaEn - sesion.CreadaEn <= TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task Se_activa_con_passkey_y_entra_con_la_passkey()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        var (_, _, enlace) = await ComandoAsync(api);
        using var autenticador = new AutenticadorDePrueba();

        using var navegador = Publico(api);
        await ActivarConPasskeyAsync(navegador, enlace, autenticador);
        Assert.True((await YoAsync(navegador)).Emergencia);

        using var luego = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await EntrarConPasskeyAsync(luego, autenticador)).StatusCode);
        var yo = await YoAsync(luego);
        Assert.True(yo.Emergencia);
        Assert.Contains(RolesAtlas.Superadmin, yo.Roles);
    }

    [Fact]
    public async Task Volver_a_correr_el_comando_invalida_el_enlace_anterior_quita_los_factores_y_cierra_las_sesiones()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        var (_, _, primero) = await ComandoAsync(api);
        using var autenticador = new AutenticadorDePrueba();
        using var navegador = Publico(api);
        await ActivarConPasskeyAsync(navegador, primero, autenticador);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);

        // Rotar: la misma cuenta (no otra), sin la passkey anterior y con la sesión cerrada.
        var (codigo, salida, segundo) = await ComandoAsync(api);
        Assert.Equal(0, codigo);
        Assert.DoesNotContain("Cuenta creada ahora", salida, StringComparison.Ordinal);
        Assert.Contains("passkey", salida, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using (var conLaVieja = Publico(api))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarConPasskeyAsync(conLaVieja, autenticador)).StatusCode);
        }

        // Un tercer comando invalida el segundo enlace aunque nadie lo haya usado.
        var (_, _, tercero) = await ComandoAsync(api);
        using var publico = Publico(api);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(publico, "/api/v1/identidad/emergencia/consultar", new { enlace = primero })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(publico, "/api/v1/identidad/emergencia/consultar", new { enlace = segundo })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(publico, "/api/v1/identidad/emergencia/consultar", new { enlace = tercero })).StatusCode);

        await using var plataforma = entorno.Plataforma(baseNueva);
        Assert.Equal(1, await plataforma.AsignacionesRol.CountAsync(a => a.Origen == OrigenAsignacion.Emergencia, Ct));
        Assert.Equal(1, await plataforma.Personas.CountAsync(p => p.Correo == Correo, Ct));
    }

    [Fact]
    public async Task El_enlace_vence()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva, configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesEmergencia.Seccion}:VigenciaEnlace"] = "00:00:01",
        });
        var (_, _, enlace) = await ComandoAsync(api);
        await Task.Delay(TimeSpan.FromSeconds(2.5), Ct);
        using var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(navegador, "/api/v1/identidad/emergencia/consultar", new { enlace })).StatusCode);
    }

    [Fact]
    public async Task Todo_el_flujo_queda_en_la_auditoria_y_el_token_no_aparece_en_logs()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        var logs = new CapturaDeLogs();
        await using var base_ = entorno.Api(baseDeDatos: baseNueva);
        await using var api = base_.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Trace)));

        var (_, _, enlace) = await ComandoAsync(api);
        using var navegador = Publico(api);
        var (clave, _) = await ActivarConContrasenaAsync(navegador, enlace);
        await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");
        using var luego = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await EntrarConContrasenaAsync(luego, Correo, clave)).StatusCode);
        // Un intento con el enlace ya usado también queda registrado.
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(Publico(api), "/api/v1/identidad/emergencia/consultar", new { enlace })).StatusCode);

        foreach (var tipo in TiposDeEventoProbados)
        {
            Assert.True(await Auditoria.RegistradoAsync(entorno, tipo, baseDeDatos: baseNueva), tipo);
        }
        await using var identidad = entorno.Identidad(baseNueva);
        var emitido = await identidad.EventosSeguridad.FirstAsync(e => e.Tipo == TiposEventoSeguridad.EmergenciaEnlaceEmitido, Ct);
        using (var detalle = JsonDocument.Parse(emitido.Datos!))
        {
            Assert.Equal(CuentaEmergencia.Severidad, detalle.RootElement.GetProperty("severidad").GetString());
        }
        Assert.True(await identidad.EventosSeguridad.AnyAsync(e => e.Tipo == TiposEventoSeguridad.EnlaceRechazado, Ct));

        // El token (ni el enlace completo) no quedó en ningún log, ni en la auditoría, ni en los eventos.
        var token = enlace.Split('.')[1];
        Assert.NotEmpty(logs.Entradas);
        Assert.DoesNotContain(logs.Entradas, e => e.Contains(token, StringComparison.Ordinal));
        Assert.Contains(logs.Entradas, e => e.Contains("alta severidad: identidad.emergencia.activada", StringComparison.Ordinal));
        await using var plataforma = entorno.Plataforma(baseNueva);
        var datos = await plataforma.Auditoria.Select(a => a.Datos).ToListAsync(Ct);
        Assert.DoesNotContain(datos, d => d != null && d.Contains(token, StringComparison.Ordinal));
        var eventos = await identidad.EventosSeguridad.Select(e => e.Datos).ToListAsync(Ct);
        Assert.DoesNotContain(eventos, d => d != null && d.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_empleado_normal_no_puede_usar_el_ingreso_local_aunque_tenga_contrasena()
    {
        await using var api = entorno.Api();
        var correo = $"empleado.{Guid.NewGuid():N}@ejemplo.com";
        using (var empleado = Publico(api))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await empleado.EntrarSimuladoAsync(correo, "Empleada Demo")).StatusCode);
        }
        Guid personaId;
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            var usuario = (await usuarios.FindByEmailAsync(correo))!;
            personaId = usuario.PersonaId;
            Assert.True((await usuarios.AddPasswordAsync(usuario, Contrasena)).Succeeded);
            Assert.Equal(SesionExterna.PersonaNoExterna,
                await alcance.ServiceProvider.GetRequiredService<SesionExterna>().MotivoNoPermitidoAsync(personaId, Ct));
        }

        using var navegador = Publico(api);
        var respuesta = await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("credenciales_invalidas", (await respuesta.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error);
    }

    [Fact]
    public async Task La_administracion_no_puede_retirar_su_rol_ni_suspenderla()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        await ComandoAsync(api);
        using var admin = Publico(api);
        var grupo = await entorno.GrupoAdministradorAsync(baseNueva);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.EntrarSimuladoAsync($"admin.{Guid.NewGuid():N}@ejemplo.com", "Admin Demo", grupo)).StatusCode);

        Guid personaId, asignacionId;
        await using (var plataforma = entorno.Plataforma(baseNueva))
        {
            var asignacion = await plataforma.AsignacionesRol.SingleAsync(a => a.Origen == OrigenAsignacion.Emergencia, Ct);
            (personaId, asignacionId) = (asignacion.PersonaId, asignacion.Id);
        }
        var asignaciones = await admin.GetAsync($"/api/v1/plataforma/personas/{personaId}/asignaciones");
        Assert.Contains("\"emergencia\"", await asignaciones.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var retirar = await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/asignaciones/{asignacionId}");
        Assert.Equal(HttpStatusCode.Conflict, retirar.StatusCode);
        Assert.Contains(CuentaEmergencia.CodigoProtegida, await retirar.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var suspender = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/suspender");
        Assert.Equal(HttpStatusCode.Conflict, suspender.StatusCode);

        await using var despues = entorno.Plataforma(baseNueva);
        Assert.True(await despues.AsignacionesRol.AnyAsync(a => a.Id == asignacionId, Ct));
        Assert.Equal(EstadoPersona.Activa, await despues.Personas.Where(p => p.Id == personaId).Select(p => p.Estado).SingleAsync(Ct));
    }

    [Fact]
    public async Task El_comando_sin_argumentos_validos_no_hace_nada()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        using var salida = new StringWriter();
        using var errores = new StringWriter();
        Assert.Equal(2, await ComandoEmergencia.EjecutarAsync(api.Services, ["emergencia"], salida, errores, Ct));
        Assert.Contains(ComandoEmergencia.Uso, errores.ToString(), StringComparison.Ordinal);
        Assert.True(ComandoEmergencia.EsComando(["emergencia", "activar"]));
        Assert.False(ComandoEmergencia.EsComando([]));
        await using var plataforma = entorno.Plataforma(baseNueva);
        Assert.False(await plataforma.AsignacionesRol.AnyAsync(a => a.Origen == OrigenAsignacion.Emergencia, Ct));
    }

    /// <summary>Guarda todo lo que se escribe en el log (mensaje, valores y excepción) para buscar secretos.</summary>
    private sealed class CapturaDeLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entradas { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Registrador(CapturaDeLogs captura, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var valores = state is IEnumerable<KeyValuePair<string, object?>> pares ? string.Join(" ", pares.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                captura.Entradas.Enqueue($"{categoria} {formatter(state, exception)} {valores} {exception}");
            }
        }
    }
}
