using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>HU 1.3b: dispositivos conectados, sello de seguridad, suspensión y reautenticación.</summary>
public class SesionesTests(EntornoApi entorno)
{
    private const string Android = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36";
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:131.0) Gecko/20100101 Firefox/131.0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"empleado.{Guid.NewGuid():N}@ejemplo.com";

    private static async Task<List<EndpointsSesiones.SesionDispositivo>> SesionesAsync(Navegador navegador)
    {
        var respuesta = await navegador.GetAsync("/api/v1/identidad/sesiones");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<List<EndpointsSesiones.SesionDispositivo>>(Ct))!;
    }

    private async Task<Guid> PersonaIdAsync(string correo)
    {
        await using var plataforma = entorno.Plataforma();
        return await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
    }

    private async Task<Navegador> AdministradorAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api)
    {
        var grupo = await entorno.GrupoAdministradorAsync();
        var admin = Navegador.De(api);
        admin.Ip = "192.0.2.50";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.EntrarSimuladoAsync(CorreoNuevo(), "Admin Demo", grupo)).StatusCode);
        return admin;
    }

    /// <summary>¿El evento quedó en identidad_evento_seguridad y en plataforma_auditoria (por usuario o persona)?</summary>
    private Task<bool> AuditadoAsync(string tipo, string? id = null) => Auditoria.RegistradoAsync(entorno, tipo, id);

    [Fact]
    public async Task Dispositivos_conectados_lista_las_sesiones_con_dispositivo_e_ip_y_marca_la_actual()
    {
        await using var api = entorno.Api();
        using var movil = Navegador.De(api);
        using var escritorio = Navegador.De(api);
        movil.Agente = Android;
        movil.Ip = "198.51.100.10";
        escritorio.Agente = Windows;
        escritorio.Ip = "198.51.100.11";
        var correo = CorreoNuevo();
        await movil.EntrarSimuladoAsync(correo, "Dos Dispositivos");
        await escritorio.EntrarSimuladoAsync(correo, "Dos Dispositivos");

        var sesiones = await SesionesAsync(escritorio);

        Assert.Equal(2, sesiones.Count);
        var actual = Assert.Single(sesiones, s => s.Actual);
        Assert.Equal("Firefox en Windows", actual.Dispositivo);
        Assert.Equal("198.51.100.11", actual.Ip);
        var otra = Assert.Single(sesiones, s => !s.Actual);
        Assert.Equal("Chrome en Android", otra.Dispositivo);
        Assert.Equal("198.51.100.10", otra.Ip);
    }

    [Fact]
    public async Task Cerrar_otro_dispositivo_corta_su_sesion_y_queda_auditado()
    {
        await using var api = entorno.Api();
        using var movil = Navegador.De(api);
        using var escritorio = Navegador.De(api);
        var correo = CorreoNuevo();
        await movil.EntrarSimuladoAsync(correo, "Cierre Remoto");
        await escritorio.EntrarSimuladoAsync(correo, "Cierre Remoto");
        var idMovil = (await SesionesAsync(escritorio)).Single(s => !s.Actual).Id;

        var cierre = await escritorio.EnviarAsync(HttpMethod.Delete, $"/api/v1/identidad/sesiones/{idMovil}");

        Assert.Equal(HttpStatusCode.NoContent, cierre.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await movil.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Single(await SesionesAsync(escritorio));
        // La sesión de otro usuario no se puede cerrar: 404 aunque el id exista.
        using var ajeno = Navegador.De(api);
        await ajeno.EntrarSimuladoAsync(CorreoNuevo(), "Ajeno");
        var idEscritorio = (await SesionesAsync(escritorio)).Single().Id;
        Assert.Equal(HttpStatusCode.NotFound, (await ajeno.EnviarAsync(HttpMethod.Delete, $"/api/v1/identidad/sesiones/{idEscritorio}")).StatusCode);

        await using var identidad = entorno.Identidad();
        var fila = await identidad.Sesiones.SingleAsync(s => s.Id == idMovil, Ct);
        Assert.Equal(MotivosRevocacion.CierreRemoto, fila.MotivoRevocacion);
        var usuario = await identidad.Sesiones.Where(s => s.Id == idMovil).Select(s => s.UsuarioId).SingleAsync(Ct);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.CierreRemoto, usuario.ToString()));
    }

    [Fact]
    public async Task Cerrar_las_otras_sesiones_exige_reautenticacion_y_responde_un_codigo_para_la_PWA()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        using var a = Navegador.De(api);
        using var b = Navegador.De(api);
        using var c = Navegador.De(api);
        var correo = CorreoNuevo();
        await a.EntrarSimuladoAsync(correo, "Reautentica Demo");
        await b.EntrarSimuladoAsync(correo, "Reautentica Demo");
        await c.EntrarSimuladoAsync(correo, "Reautentica Demo");
        var idAnteriorDeA = (await SesionesAsync(a)).Single(s => s.Actual).Id;
        reloj.Adelantar(TimeSpan.FromMinutes(11));

        var rechazo = await a.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/sesiones/cerrar-otras");
        var otroDispositivo = (await SesionesAsync(a)).First(s => !s.Actual).Id;
        var rechazoUno = await a.EnviarAsync(HttpMethod.Delete, $"/api/v1/identidad/sesiones/{otroDispositivo}");

        Assert.Equal(HttpStatusCode.Unauthorized, rechazo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rechazoUno.StatusCode);
        using (var cuerpo = JsonDocument.Parse(await rechazo.Content.ReadAsStringAsync(Ct)))
        {
            Assert.Equal(Reautenticacion.Codigo, cuerpo.RootElement.GetProperty("codigo").GetString());
            Assert.Equal(600, cuerpo.RootElement.GetProperty("maxEdadSegundos").GetInt32());
        }
        Assert.Contains("reautenticacion_requerida", rechazo.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        // El 401 de reautenticación no cierra la sesión: la PWA sigue funcionando hasta que el usuario vuelva a entrar.
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/plataforma/yo")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await a.ReingresarSimuladoAsync(correo, "Reautentica Demo")).StatusCode);
        var cierre = await a.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/sesiones/cerrar-otras");

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        // El reingreso reemplazó la sesión anterior de A (id nuevo al autenticarse); quedaban B y C.
        Assert.Equal(2, (await cierre.Content.ReadFromJsonAsync<EndpointsSesiones.ResultadoCierre>(Ct))!.Cerradas);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        await using var identidad = entorno.Identidad();
        Assert.Equal(MotivosRevocacion.ReemplazadaPorIngreso,
            await identidad.Sesiones.Where(s => s.Id == idAnteriorDeA).Select(s => s.MotivoRevocacion).SingleAsync(Ct));
        var usuario = await identidad.Sesiones.Where(s => s.Id == idAnteriorDeA).Select(s => s.UsuarioId).SingleAsync(Ct);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.ReautenticacionRequerida, usuario.ToString()));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.Reautenticacion, usuario.ToString()));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.CierreRemoto, usuario.ToString()));
    }

    [Fact]
    public async Task Suspender_a_una_persona_corta_su_sesion_activa_en_menos_de_un_minuto()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        using var victima = Navegador.De(api);
        var correo = CorreoNuevo();
        await victima.EntrarSimuladoAsync(correo, "Persona Suspendida");
        Assert.Equal(HttpStatusCode.OK, (await victima.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        var personaId = await PersonaIdAsync(correo);

        var cronometro = Stopwatch.StartNew();
        var suspension = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/suspender");
        var despues = await victima.GetAsync("/api/v1/plataforma/yo");
        cronometro.Stop();

        Assert.Equal(HttpStatusCode.NoContent, suspension.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
        Assert.True(cronometro.Elapsed < TimeSpan.FromMinutes(1), $"La sesión se cortó en {cronometro.Elapsed}");
        // No puede volver a entrar mientras esté suspendida, y la respuesta no revela por qué.
        var reingreso = await victima.EntrarSimuladoAsync(correo, "Persona Suspendida");
        Assert.Equal(HttpStatusCode.Forbidden, reingreso.StatusCode);
        Assert.Contains("ingreso_no_permitido", await reingreso.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        await using (var plataforma = entorno.Plataforma())
        {
            Assert.Equal(EstadoPersona.Suspendida, (await plataforma.Personas.SingleAsync(p => p.Id == personaId, Ct)).Estado);
        }
        await using (var identidad = entorno.Identidad())
        {
            var usuarioId = await identidad.Users.Where(u => u.PersonaId == personaId).Select(u => u.Id).SingleAsync(Ct);
            Assert.All(await identidad.Sesiones.Where(s => s.UsuarioId == usuarioId).ToListAsync(Ct), s =>
            {
                Assert.NotNull(s.RevocadaEn);
                Assert.Equal(MotivosInvalidacion.Suspension, s.MotivoRevocacion);
            });
            Assert.True(await AuditadoAsync(TiposEventoSeguridad.SesionesInvalidadas, usuarioId.ToString()));
        }
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.PersonaSuspendida, personaId.ToString()));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.InicioRechazado));

        // Al reactivarla vuelve a entrar.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/reactivar")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await victima.EntrarSimuladoAsync(correo, "Persona Suspendida")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await victima.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.PersonaReactivada, personaId.ToString()));
    }

    [Fact]
    public async Task Una_suspension_en_medio_de_varios_ingresos_no_deja_ninguna_sesion_viva()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        using (var primero = Navegador.De(api))
        {
            await primero.EntrarSimuladoAsync(correo, "Carrera Demo");
        }
        var personaId = await PersonaIdAsync(correo);
        var navegadores = Enumerable.Range(0, 8).Select(_ => Navegador.De(api)).ToList();

        var ingresos = navegadores.Select(n => n.EntrarSimuladoAsync(correo, "Carrera Demo")).ToList();
        var suspension = admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/suspender");
        await Task.WhenAll([.. ingresos, suspension]);

        Assert.Equal(HttpStatusCode.NoContent, (await suspension).StatusCode);
        foreach (var navegador in navegadores)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
            navegador.Dispose();
        }
    }

    [Fact]
    public async Task El_administrador_cierra_todas_las_sesiones_de_una_persona_sin_suspenderla()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        using var uno = Navegador.De(api);
        using var dos = Navegador.De(api);
        var correo = CorreoNuevo();
        await uno.EntrarSimuladoAsync(correo, "Cierre Total");
        await dos.EntrarSimuladoAsync(correo, "Cierre Total");
        var personaId = await PersonaIdAsync(correo);

        var cierre = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/sesiones/cerrar");

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(2, (await cierre.Content.ReadFromJsonAsync<EndpointsSesiones.ResultadoCierre>(Ct))!.Cerradas);
        Assert.Equal(HttpStatusCode.Unauthorized, (await uno.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dos.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await uno.EntrarSimuladoAsync(correo, "Cierre Total")).StatusCode);
    }

    [Theory]
    [InlineData("sesiones/cerrar")]
    [InlineData("suspender")]
    [InlineData("reactivar")]
    public async Task Sin_el_rol_de_administracion_los_endpoints_administrativos_responden_403(string accion)
    {
        await using var api = entorno.Api();
        using var comun = Navegador.De(api);
        using var otra = Navegador.De(api);
        await comun.EntrarSimuladoAsync(CorreoNuevo(), "Sin Rol");
        var correo = CorreoNuevo();
        await otra.EntrarSimuladoAsync(correo, "Objetivo");

        var respuesta = await comun.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{await PersonaIdAsync(correo)}/{accion}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otra.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task La_administracion_tambien_exige_reautenticacion()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        using var admin = await AdministradorAsync(api);
        using var otra = Navegador.De(api);
        var correo = CorreoNuevo();
        await otra.EntrarSimuladoAsync(correo, "Objetivo Demo");
        reloj.Adelantar(TimeSpan.FromMinutes(11));

        var respuesta = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{await PersonaIdAsync(correo)}/suspender");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains(Reautenticacion.Codigo, await respuesta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await otra.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task Un_cambio_del_sello_de_seguridad_invalida_la_sesion_en_la_siguiente_peticion()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();
        await navegador.EntrarSimuladoAsync(correo, "Sello Demo");
        var personaId = await PersonaIdAsync(correo);

        // Solo el sello, sin revocar las filas: así se comprueba la validación en cada petición.
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            var usuario = await usuarios.Users.SingleAsync(u => u.PersonaId == personaId, Ct);
            Assert.True((await usuarios.UpdateSecurityStampAsync(usuario)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        await using var identidad = entorno.Identidad();
        var usuarioId = await identidad.Users.Where(u => u.PersonaId == personaId).Select(u => u.Id).SingleAsync(Ct);
        Assert.Equal(MotivosRevocacion.SelloDeSeguridad,
            await identidad.Sesiones.Where(s => s.UsuarioId == usuarioId).Select(s => s.MotivoRevocacion).SingleAsync(Ct));
    }

    [Fact]
    public async Task El_invalidador_que_usara_la_HU_1_4_corta_las_sesiones_al_cambiar_roles()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();
        await navegador.EntrarSimuladoAsync(correo, "Cambio De Roles");
        var personaId = await PersonaIdAsync(correo);

        int revocadas;
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            revocadas = await alcance.ServiceProvider.GetRequiredService<IInvalidadorSesiones>()
                .InvalidarSesionesAsync(personaId, MotivosInvalidacion.CambioDeRoles, ct: Ct);
        }

        Assert.Equal(1, revocadas);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        // A diferencia de la suspensión, puede volver a entrar (con sus roles nuevos).
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Cambio De Roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.SesionesInvalidadas, personaId.ToString()));
    }

    [Fact]
    public async Task Cerrar_sesion_queda_auditado()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        var correo = CorreoNuevo();
        await navegador.EntrarSimuladoAsync(correo, "Sale Auditado");
        var personaId = await PersonaIdAsync(correo);

        await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");

        Assert.True(await AuditadoAsync(TiposEventoSeguridad.CierreSesion, personaId.ToString()));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.InicioSesion, personaId.ToString()));
    }
}
