using System.Net;
using System.Reflection;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;

namespace Flit.Atlas.Api.Tests;

/// <summary>HU 1.3b: bloqueo progresivo, limitación de tasa y eventos de seguridad.</summary>
public class SeguridadTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"empleado.{Guid.NewGuid():N}@ejemplo.com";

    /// <summary>IP de documentación (2001:db8::/32) única por prueba, para encontrar sus eventos.</summary>
    private static string IpNueva() => $"2001:db8::{Random.Shared.Next(1, 0xffff):x}:{Random.Shared.Next(1, 0xffff):x}";

    /// <summary>Cuenta local con contraseña (las de los externos llegan en la HU 1.5), creada con UserManager.</summary>
    private static async Task<(UsuarioAtlas Usuario, string Clave)> CuentaLocalAsync(IServiceProvider servicios)
    {
        var usuarios = servicios.GetRequiredService<UserManager<UsuarioAtlas>>();
        var correo = CorreoNuevo();
        var usuario = new UsuarioAtlas { PersonaId = Guid.NewGuid(), UserName = correo, Email = correo, CreadoEn = DateTimeOffset.UtcNow };
        var clave = $"Prueba-{Guid.NewGuid():N}-1A";
        var creado = await usuarios.CreateAsync(usuario, clave);
        Assert.True(creado.Succeeded, string.Join("; ", creado.Errors.Select(e => e.Code)));
        return (usuario, clave);
    }

    [Fact]
    public async Task Diez_intentos_fallidos_bloquean_la_cuenta_y_quedan_auditados()
    {
        await using var api = entorno.Api();
        await using var alcance = api.Services.CreateAsyncScope();
        var verificador = alcance.ServiceProvider.GetRequiredService<VerificadorContrasena>();
        var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
        var (usuario, clave) = await CuentaLocalAsync(alcance.ServiceProvider);

        for (var intento = 1; intento < IdentidadDbContext.IntentosAntesDelBloqueo; intento++)
        {
            Assert.Equal(ResultadoVerificacion.Invalida, await verificador.VerificarAsync(usuario, "no-es-la-clave", Ct));
        }
        Assert.False(await usuarios.IsLockedOutAsync(usuario));
        var decimo = await verificador.VerificarAsync(usuario, "no-es-la-clave", Ct);
        // Bloqueada, la contraseña correcta tampoco entra.
        var conLaCorrecta = await verificador.VerificarAsync(usuario, clave, Ct);

        Assert.Equal(ResultadoVerificacion.Bloqueada, decimo);
        Assert.Equal(ResultadoVerificacion.Bloqueada, conLaCorrecta);
        Assert.True(await usuarios.IsLockedOutAsync(usuario));
        var fin = await usuarios.GetLockoutEndDateAsync(usuario);
        Assert.InRange(fin!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        await using var identidad = entorno.Identidad();
        var eventos = await identidad.EventosSeguridad.Where(e => e.UsuarioId == usuario.Id).Select(e => e.Tipo).ToListAsync(Ct);
        Assert.Equal(10, eventos.Count(t => t == TiposEventoSeguridad.InicioFallido));
        Assert.Equal(1, eventos.Count(t => t == TiposEventoSeguridad.CuentaBloqueada));
        Assert.Equal(1, eventos.Count(t => t == TiposEventoSeguridad.InicioRechazado));
        await using var plataforma = entorno.Plataforma();
        var acciones = await plataforma.Auditoria.Where(a => a.RecursoId == usuario.Id.ToString()).Select(a => a.Accion).ToListAsync(Ct);
        Assert.Equal(10, acciones.Count(a => a == "identidad.inicio_fallido"));
        Assert.Single(acciones, a => a == "identidad.cuenta_bloqueada");
    }

    [Fact]
    public async Task El_bloqueo_es_progresivo_y_el_conteo_se_reinicia_al_terminar_el_ingreso_no_con_la_contrasena()
    {
        await using var api = entorno.Api();
        await using var alcance = api.Services.CreateAsyncScope();
        var verificador = alcance.ServiceProvider.GetRequiredService<VerificadorContrasena>();
        var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
        var (usuario, clave) = await CuentaLocalAsync(alcance.ServiceProvider);

        // Nueve fallos y la contraseña correcta: el conteo sigue (F4-SEC-01, ATLAS-ADR-0049); vuelve a cero solo al terminar
        // el ingreso con el segundo factor.
        for (var i = 0; i < 9; i++)
        {
            await verificador.VerificarAsync(usuario, "mal", Ct);
        }
        Assert.Equal(ResultadoVerificacion.Valida, await verificador.VerificarAsync(usuario, clave, Ct));
        Assert.Equal(9, await usuarios.GetAccessFailedCountAsync(usuario));
        await verificador.IngresoCompletadoAsync(usuario, Ct);
        Assert.Equal(0, await usuarios.GetAccessFailedCountAsync(usuario));

        for (var i = 0; i < 10; i++)
        {
            await verificador.VerificarAsync(usuario, "mal", Ct);
        }
        // Termina el primer bloqueo (15 min) y vuelve a fallar diez veces: el segundo dura el doble.
        await usuarios.SetLockoutEndDateAsync(usuario, DateTimeOffset.UtcNow.AddSeconds(-1));
        for (var i = 0; i < 10; i++)
        {
            await verificador.VerificarAsync(usuario, "mal", Ct);
        }

        var fin = await usuarios.GetLockoutEndDateAsync(usuario);
        Assert.InRange(fin!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(6, 960)]
    [InlineData(7, 1440)]
    [InlineData(40, 1440)]
    public void La_duracion_del_bloqueo_se_duplica_hasta_24_horas(int previos, int minutos) =>
        Assert.Equal(TimeSpan.FromMinutes(minutos), VerificadorContrasena.DuracionBloqueo(previos));

    [Fact]
    public async Task Una_cuenta_bloqueada_tampoco_entra_con_Microsoft_o_el_simulado()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();
        await navegador.EntrarSimuladoAsync(correo, "Bloqueada Demo");
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            var usuario = await usuarios.FindByEmailAsync(correo);
            await usuarios.SetLockoutEndDateAsync(usuario!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var respuesta = await navegador.EntrarSimuladoAsync(correo, "Bloqueada Demo");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Pasado_el_limite_por_IP_responde_429_con_Retry_After_y_lo_audita_una_vez()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string> { [$"{OpcionesLimiteTasa.Seccion}:PorIp"] = "3" });
        using var cliente = Navegador.De(api);
        using var otroCliente = Navegador.De(api);
        cliente.Ip = IpNueva();
        otroCliente.Ip = IpNueva();

        var permitidas = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            permitidas.Add((await cliente.GetAsync("/api/v1/identidad/proveedores")).StatusCode);
        }
        var rechazada = await cliente.GetAsync("/api/v1/identidad/proveedores");
        var otraRechazada = await cliente.GetAsync("/api/v1/plataforma/yo");

        Assert.All(permitidas, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, otraRechazada.StatusCode);
        var espera = Assert.Single(rechazada.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(espera, System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        Assert.Contains(LimitacionTasa.Codigo, await rechazada.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        // Otra IP no se ve afectada, ni las rutas que no son de identidad.
        Assert.Equal(HttpStatusCode.OK, (await otroCliente.GetAsync("/api/v1/identidad/proveedores")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/v1/version")).StatusCode);

        var ip = IPAddress.Parse(cliente.Ip);
        await using var plataforma = entorno.Plataforma();
        Assert.Equal(1, await plataforma.Auditoria.CountAsync(a => a.Accion == "identidad.limite_tasa" && a.Ip == ip, Ct));
        await using var identidad = entorno.Identidad();
        Assert.Equal(1, await identidad.EventosSeguridad.CountAsync(e => e.Tipo == TiposEventoSeguridad.LimiteTasa && e.Ip == ip, Ct));
    }

    [Fact]
    public async Task El_limite_por_usuario_aplica_aunque_cambie_de_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:PorUsuario"] = "2",
        });
        using var navegador = Navegador.De(api);
        using var otro = Navegador.De(api);
        navegador.Ip = IpNueva();
        otro.Ip = navegador.Ip;
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Limite Usuario");
        await otro.EntrarSimuladoAsync(CorreoNuevo(), "Otro Usuario");

        var respuestas = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            navegador.Ip = IpNueva();
            respuestas.Add((await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], respuestas);
        Assert.Equal(HttpStatusCode.OK, (await otro.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task Los_ingresos_tienen_un_limite_propio_por_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:IngresoPorIp"] = "2",
        });
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var respuestas = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            respuestas.Add((await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Fuerza Bruta")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests], respuestas);
        // Las demás rutas de identidad siguen respondiendo a esa IP.
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public void Reautenticar_con_Microsoft_obliga_a_Entra_a_pedir_las_credenciales()
    {
        var propiedades = new AuthenticationProperties();
        propiedades.Items[IngresoEntra.ClaveReautenticar] = "1";
        var esquema = new AuthenticationScheme("Entra", null, typeof(OpenIdConnectHandler));
        var conMarca = new RedirectContext(new DefaultHttpContext(), esquema, new OpenIdConnectOptions(), propiedades)
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };
        var normal = new RedirectContext(new DefaultHttpContext(), esquema, new OpenIdConnectOptions(), new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };

        IngresoEntra.AlRedirigirAsync(conMarca);
        IngresoEntra.AlRedirigirAsync(normal);

        Assert.Equal("login", conMarca.ProtocolMessage.Prompt);
        Assert.Null(normal.ProtocolMessage.Prompt);
    }

    [Fact]
    public async Task Los_eventos_de_seguridad_no_se_pueden_modificar_ni_borrar()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Solo Insercion");

        await using var conexion = new NpgsqlConnection(entorno.Postgres.GetConnectionString());
        await conexion.OpenAsync(Ct);
        foreach (var sql in new[]
        {
            "UPDATE atlas.identidad_evento_seguridad SET tipo = 'otro'",
            "DELETE FROM atlas.identidad_evento_seguridad",
            "TRUNCATE atlas.identidad_evento_seguridad",
        })
        {
            await using var comando = new NpgsqlCommand(sql, conexion);
            var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(Ct));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Cada_evento_de_seguridad_queda_en_la_auditoria()
    {
        // Base propia: al final se comparan todas las filas de las dos tablas.
        var baseNueva = await entorno.BaseNuevaAsync();
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(baseDeDatos: baseNueva, reloj: reloj,
            configuracion: new Dictionary<string, string> { [$"{OpcionesLimiteTasa.Seccion}:PorIp"] = "8" });
        var grupo = await entorno.GrupoAdministradorAsync(baseNueva);
        using var admin = Navegador.De(api);
        using var uno = Navegador.De(api);
        using var dos = Navegador.De(api);
        using var atacante = Navegador.De(api);
        admin.Ip = IpNueva();
        uno.Ip = IpNueva();
        dos.Ip = IpNueva();
        atacante.Ip = IpNueva();
        var correo = CorreoNuevo();
        var correoAdmin = CorreoNuevo();

        await admin.EntrarSimuladoAsync(correoAdmin, "Admin Demo", grupo);                 // inicio_sesion
        await uno.EntrarSimuladoAsync(correo, "Persona Demo");
        await dos.EntrarSimuladoAsync(correo, "Persona Demo");
        await dos.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");                  // cierre_sesion
        await dos.EntrarSimuladoAsync(correo, "Persona Demo");
        reloj.Adelantar(TimeSpan.FromMinutes(11));
        await uno.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/sesiones/cerrar-otras"); // reautenticacion_requerida
        await uno.ReingresarSimuladoAsync(correo, "Persona Demo");                          // reautenticacion
        await uno.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/sesiones/cerrar-otras"); // cierre_remoto
        await admin.ReingresarSimuladoAsync(correoAdmin, "Admin Demo", grupo);
        Guid personaId;
        await using (var plataforma = entorno.Plataforma(baseNueva))
        {
            personaId = await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
        }
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/suspender"); // persona_suspendida, sesiones_invalidadas
        await uno.EntrarSimuladoAsync(correo, "Persona Demo");                              // inicio_rechazado
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/reactivar"); // persona_reactivada
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var verificador = alcance.ServiceProvider.GetRequiredService<VerificadorContrasena>();
            var (local, _) = await CuentaLocalAsync(alcance.ServiceProvider);
            for (var i = 0; i < IdentidadDbContext.IntentosAntesDelBloqueo; i++)
            {
                await verificador.VerificarAsync(local, "mal", Ct);                           // inicio_fallido, cuenta_bloqueada
            }
        }
        for (var i = 0; i < 9; i++)
        {
            await atacante.GetAsync("/api/v1/identidad/proveedores");                        // limite_tasa
        }

        await using var identidad = entorno.Identidad(baseNueva);
        await using var auditoria = entorno.Plataforma(baseNueva);
        var eventos = await identidad.EventosSeguridad.ToListAsync(Ct);
        var acciones = await auditoria.Auditoria.Where(a => a.Accion.StartsWith(TiposEventoSeguridad.PrefijoAuditoria))
            .Select(a => new { a.Accion, a.OcurridoEn }).ToListAsync(Ct);
        var tipos = typeof(TiposEventoSeguridad).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name is not (nameof(TiposEventoSeguridad.PrefijoAuditoria) or nameof(TiposEventoSeguridad.PrefijoAltaSeveridad)))
            .Select(f => (string)f.GetRawConstantValue()!)
            // Los de las cuentas externas (HU 1.5) y los de la cuenta de emergencia (ATLAS-ADR-0027) se prueban de punta a
            // punta, con su auditoría, en CuentasExternasTests y EmergenciaTests.
            .Except(CuentasExternasTests.TiposDeEventoProbados)
            .Except(EmergenciaTests.TiposDeEventoProbados)
            .Except(PentestSesionesTests.TiposDeEventoProbados)
            .ToHashSet();

        // Ocurrieron todos los tipos de evento…
        Assert.Equal(tipos.Order(), eventos.Select(e => e.Tipo).ToHashSet().Order());
        // …y cada evento tiene su registro en la auditoría (misma acción y momento), uno a uno.
        Assert.Equal(eventos.Count, acciones.Count);
        Assert.All(eventos, e => Assert.Contains(acciones, a =>
            a.Accion == TiposEventoSeguridad.PrefijoAuditoria + e.Tipo && a.OcurridoEn == e.OcurridoEn));
    }
}
