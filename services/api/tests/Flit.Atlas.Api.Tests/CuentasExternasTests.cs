using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// CA de la HU 1.5 (ATLAS-ADR-0025): invitar → aceptar → registrar el factor → entrar → vencer; un enlace usado o vencido
/// no sirve; una cuenta externa sin MFA no entra; todo queda en la auditoría; el administrador de la organización A no
/// ve ni invita en B.
/// </summary>
public partial class CuentasExternasTests(EntornoApi entorno)
{
    /// <summary>Contraseña de prueba: larga, propia y no filtrada (la revisión de HIBP está apagada en las pruebas).</summary>
    private const string Contrasena = "Rio Magdalena al amanecer 2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo(string prefijo = "externo") => $"{prefijo}.{Guid.NewGuid():N}@ejemplo.com";

    private static string IpNueva() => $"2001:db8::15:{Random.Shared.Next(1, 0xffff):x}";

    /// <summary>Tipos de evento de la HU 1.5; <see cref="Todos_los_eventos_de_cuentas_externas_quedan_en_las_dos_tablas"/> los recorre.</summary>
    public static readonly string[] TiposDeEventoProbados =
    [
        TiposEventoSeguridad.InvitacionCreada, TiposEventoSeguridad.InvitacionReenviada, TiposEventoSeguridad.InvitacionSuspendida,
        TiposEventoSeguridad.InvitacionRenovada, TiposEventoSeguridad.InvitacionRevocada, TiposEventoSeguridad.InvitacionAceptada,
        TiposEventoSeguridad.EnlaceRechazado, TiposEventoSeguridad.CorreoNoEnviado, TiposEventoSeguridad.CuentaCreada,
        TiposEventoSeguridad.FactorRegistrado, TiposEventoSeguridad.FactoresRetirados, TiposEventoSeguridad.MfaVerificado,
        TiposEventoSeguridad.MfaFallido, TiposEventoSeguridad.MfaRequerido, TiposEventoSeguridad.CodigoRecuperacionUsado,
        TiposEventoSeguridad.RecuperacionSolicitada, TiposEventoSeguridad.RecuperacionCompletada,
        TiposEventoSeguridad.RestablecimientoForzado, TiposEventoSeguridad.TerminosAceptados,
    ];

    private static readonly object[] Documentos = [.. DocumentosLegales.Vigentes.Select(d => new { documento = d.Documento, version = d.Version })];

    private Task<bool> AuditadoAsync(string tipo, string? id = null, string? baseDeDatos = null) => Auditoria.RegistradoAsync(entorno, tipo, id, baseDeDatos);

    /// <summary>¿El evento de una invitación quedó en la auditoría (recurso invitacion) y en los eventos de seguridad?</summary>
    private async Task<bool> AuditadoInvitacionAsync(string tipo, Guid invitacion)
    {
        await using var plataforma = entorno.Plataforma();
        await using var identidad = entorno.Identidad();
        var enAuditoria = await plataforma.Auditoria.AnyAsync(
            a => a.Accion == TiposEventoSeguridad.PrefijoAuditoria + tipo && a.RecursoTipo == "invitacion" && a.RecursoId == invitacion.ToString(), Ct);
        var datos = await identidad.EventosSeguridad.Where(e => e.Tipo == tipo).Select(e => e.Datos).ToListAsync(Ct);
        return enAuditoria && datos.Any(d => d != null && d.Contains(invitacion.ToString(), StringComparison.Ordinal));
    }

    private async Task<Navegador> AdministradorAsync(WebApplicationFactory<Program> api, string? baseDeDatos = null)
    {
        var admin = Navegador.De(api);
        admin.Ip = IpNueva();
        var grupo = await entorno.GrupoAdministradorAsync(baseDeDatos);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.EntrarSimuladoAsync(CorreoNuevo("admin"), "Admin Demo", grupo)).StatusCode);
        return admin;
    }

    private async Task<Guid> OrganizacionAsync(TipoOrganizacion tipo = TipoOrganizacion.Cliente, bool admin = false, string? baseDeDatos = null)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var organizacion = new Organizacion { Nombre = $"Organismo Demo {Guid.NewGuid():N}", Tipo = tipo, AdminOrganizacionHabilitado = admin };
        plataforma.Organizaciones.Add(organizacion);
        await plataforma.SaveChangesAsync(Ct);
        return organizacion.Id;
    }

    private static async Task<InvitacionEnviada> InvitarAsync(
        Navegador admin, Guid organizacion, string correo, string rol = RolesAtlas.Integrador, object? extra = null, DateTimeOffset? vence = null,
        object[]? alcance = null)
    {
        var respuesta = await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/invitaciones", JsonContent.Create(new
        {
            correo, nombre = "Externa Demo", organizacionId = organizacion, rol, alcance, accesoVenceEn = vence, permisosExtra = extra,
        }));
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(Ct));
        return (await respuesta.Content.ReadFromJsonAsync<InvitacionEnviada>(Ct))!;
    }

    private static Navegador Publico(WebApplicationFactory<Program> api)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        return navegador;
    }

    private static Task<HttpResponseMessage> PostAsync(Navegador navegador, string ruta, object cuerpo) =>
        navegador.EnviarAsync(HttpMethod.Post, ruta, JsonContent.Create(cuerpo), origen: "https://localhost");

    /// <summary>Acepta con contraseña y TOTP; devuelve la clave TOTP y la cuenta lista.</summary>
    private static async Task<(string Clave, CuentaLista Cuenta)> AceptarConContrasenaAsync(Navegador navegador, string token)
    {
        var clave = (await (await PostAsync(navegador, "/api/v1/identidad/invitaciones/totp/clave", new { token })).Content.ReadFromJsonAsync<ClaveTotp>(Ct))!;
        var respuesta = await PostAsync(navegador, "/api/v1/identidad/invitaciones/aceptar", new
        {
            token,
            nombre = "Externa Demo",
            documentos = Documentos,
            factor = new { metodo = "contrasena", estado = clave.Estado, contrasena = Contrasena, codigo = Totp.Codigo(clave.Clave) },
        });
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(Ct));
        return (clave.Clave, (await respuesta.Content.ReadFromJsonAsync<CuentaLista>(Ct))!);
    }

    private static async Task<HttpResponseMessage> EntrarConContrasenaAsync(Navegador navegador, string correo, string clave, int desfase = 0, string? codigo = null)
    {
        var paso = await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });
        if (paso.StatusCode != HttpStatusCode.OK)
        {
            return paso;
        }
        var estado = (await paso.Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        return await PostAsync(navegador, "/api/v1/identidad/entrar/local/segundo-factor", new { estado, codigo = codigo ?? Totp.Codigo(clave, desfasePasos: desfase) });
    }

    private static async Task<EndpointsIdentidad.Yo> YoAsync(Navegador navegador)
    {
        var respuesta = await navegador.GetAsync("/api/v1/plataforma/yo");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct))!;
    }

    private async Task<Guid> PersonaIdAsync(string correo, string? baseDeDatos = null)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        return await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task Invitar_envia_un_enlace_de_un_solo_uso_y_en_la_base_solo_queda_su_hash()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var organizacion = await OrganizacionAsync();
        var correo = CorreoNuevo();

        var enviada = await InvitarAsync(admin, organizacion, correo, extra: new[] { "exportar" });

        Assert.True(enviada.CorreoEnviado);
        Assert.Equal(EstadosInvitacion.Pendiente, enviada.Invitacion.Estado);
        Assert.Equal(["exportar"], enviada.Invitacion.PermisosExtra);
        var token = api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion");
        var mensaje = api.Services.GetRequiredService<BuzonDePrueba>().Para(correo).Single();
        Assert.Contains("Aceptar la invitación", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains("/invitacion#" + token, mensaje.Html, StringComparison.Ordinal);
        // El token no está en claro en la base ni en la auditoría; la fila guarda su SHA-256.
        await using var plataforma = entorno.Plataforma();
        var fila = await plataforma.Invitaciones.SingleAsync(i => i.Id == enviada.Invitacion.Id, Ct);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(token)), fila.TokenHash);
        var datos = await plataforma.Auditoria.Where(a => a.OrganizacionId == organizacion).Select(a => a.Datos).ToListAsync(Ct);
        Assert.NotEmpty(datos);
        Assert.DoesNotContain(datos, d => d != null && d.Contains(token, StringComparison.Ordinal));
        Assert.True(await AuditadoInvitacionAsync(TiposEventoSeguridad.InvitacionCreada, enviada.Invitacion.Id));
        Assert.True(fila.TokenVenceEn - fila.CreadoEn is { TotalHours: > 71.9 and < 72.1 });
    }

    [Fact]
    public async Task Aceptar_con_contrasena_y_TOTP_crea_la_cuenta_externa_entra_y_el_enlace_ya_no_sirve()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var organizacion = await OrganizacionAsync();
        var correo = CorreoNuevo();
        var invitacion = (await InvitarAsync(admin, organizacion, correo, alcance: [new { tipo = "paquete", id = "api-externa" }])).Invitacion;
        var token = api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion");
        using var invitado = Publico(api);

        var consulta = await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token });
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        Assert.Equal(correo, (await consulta.Content.ReadFromJsonAsync<InvitacionPublica>(Ct))!.Correo);
        var (_, cuenta) = await AceptarConContrasenaAsync(invitado, token);

        Assert.Equal(10, cuenta.CodigosRecuperacion.Count);
        Assert.True(cuenta.SesionIniciada);
        var yo = await YoAsync(invitado);
        Assert.Equal("externo", yo.TipoPrincipal);
        Assert.Equal(organizacion, yo.OrganizacionId);
        Assert.Equal("local", yo.Proveedor);
        Assert.Equal([RolesAtlas.Integrador], yo.Roles);
        Assert.Contains("atlas.portal.ver", yo.Permisos);

        // Asignación de invitación con su alcance y su vencimiento; evidencia de términos con versión y hash.
        await using var plataforma = entorno.Plataforma();
        var asignacion = await plataforma.AsignacionesRol.SingleAsync(a => a.PersonaId == yo.PersonaId, Ct);
        Assert.Equal(OrigenAsignacion.Invitacion, asignacion.Origen);
        Assert.Equal("paquete", asignacion.AlcanceTipo);
        Assert.Equal("api-externa", asignacion.AlcanceId);
        Assert.Equal(invitacion.AccesoVenceEn!.Value, asignacion.VigenteHasta!.Value, TimeSpan.FromSeconds(1));
        var aceptaciones = await plataforma.AceptacionesTerminos.Where(a => a.PersonaId == yo.PersonaId).ToListAsync(Ct);
        Assert.Equal(2, aceptaciones.Count);
        Assert.All(aceptaciones, a => Assert.Equal(64, a.HashContenido!.Length));
        Assert.Equal(EstadoInvitacion.Aceptada, (await plataforma.Invitaciones.SingleAsync(i => i.Id == invitacion.Id, Ct)).Estado);

        // Un enlace usado no sirve, ni para ver ni para aceptar.
        using var otro = Publico(api);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(otro, "/api/v1/identidad/invitaciones/consultar", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(otro, "/api/v1/identidad/invitaciones/totp/clave", new { token })).StatusCode);

        foreach (var tipo in new[] { TiposEventoSeguridad.CuentaCreada, TiposEventoSeguridad.FactorRegistrado, TiposEventoSeguridad.TerminosAceptados,
            TiposEventoSeguridad.InicioSesion })
        {
            Assert.True(await AuditadoAsync(tipo, yo.Sub.ToString()), tipo);
        }
        Assert.True(await AuditadoInvitacionAsync(TiposEventoSeguridad.InvitacionAceptada, invitacion.Id));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.EnlaceRechazado));
        // Los códigos de recuperación y la clave TOTP no quedan en claro.
        await using var identidad = entorno.Identidad();
        var secretos = await identidad.UserTokens.Where(t => t.UserId == yo.Sub).Select(t => t.Value).ToListAsync(Ct);
        Assert.DoesNotContain(secretos, v => cuenta.CodigosRecuperacion.Any(c => v!.Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Aceptar_con_passkey_crea_la_cuenta_y_luego_entra_con_la_passkey()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo, RolesAtlas.ClienteFuncional);
        var token = api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion");
        using var autenticador = new AutenticadorDePrueba();
        using var invitado = Publico(api);

        var opciones = (await (await PostAsync(invitado, "/api/v1/identidad/invitaciones/passkey/opciones", new { token, nombre = "Externa Demo" }))
            .Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        using (var json = JsonDocument.Parse(opciones.Opciones))
        {
            Assert.Equal("required", json.RootElement.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
            Assert.Equal("required", json.RootElement.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
        }
        var aceptar = await PostAsync(invitado, "/api/v1/identidad/invitaciones/aceptar", new
        {
            token, nombre = "Externa Demo", documentos = Documentos,
            factor = new { metodo = "passkey", estado = opciones.Estado, credencial = autenticador.Registrar(opciones.Opciones) },
        });
        Assert.True(aceptar.StatusCode == HttpStatusCode.OK, await aceptar.Content.ReadAsStringAsync(Ct));
        var yo = await YoAsync(invitado);
        Assert.Equal([RolesAtlas.ClienteFuncional], yo.Roles);
        await invitado.EnviarAsync(HttpMethod.Post, "/api/v1/identidad/salir");

        using var luego = Publico(api);
        var ingreso = (await (await PostAsync(luego, "/api/v1/identidad/entrar/passkey/opciones", new { })).Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        var entrar = await PostAsync(luego, "/api/v1/identidad/entrar/passkey", new { estado = ingreso.Estado, credencial = autenticador.Firmar(ingreso.Opciones) });

        Assert.Equal(HttpStatusCode.NoContent, entrar.StatusCode);
        Assert.Equal(yo.PersonaId, (await YoAsync(luego)).PersonaId);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.MfaVerificado, yo.Sub.ToString()));
        // El mismo desafío no sirve dos veces.
        using var tercero = Publico(api);
        var repetido = await PostAsync(tercero, "/api/v1/identidad/entrar/passkey", new { estado = ingreso.Estado, credencial = autenticador.Firmar(ingreso.Opciones) });
        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
    }

    [Fact]
    public async Task El_ingreso_con_contrasena_exige_TOTP_no_acepta_un_codigo_dos_veces_y_acepta_un_codigo_de_recuperacion_una_vez()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        var (clave, cuenta) = await AceptarConContrasenaAsync(invitado, api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion"));

        // La contraseña sola nunca abre sesión: responde el paso del TOTP.
        using var navegador = Publico(api);
        var paso = await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });
        Assert.Equal(HttpStatusCode.OK, paso.StatusCode);
        Assert.Null(navegador.Cookie);
        var estado = (await paso.Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        // El código que ya se usó al aceptar no vale otra vez; uno del paso siguiente sí.
        var siguiente = Totp.Codigo(clave, desfasePasos: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(navegador, "/api/v1/identidad/entrar/local/segundo-factor", new { estado, codigo = siguiente })).StatusCode);
        Assert.Equal("externo", (await YoAsync(navegador)).TipoPrincipal);
        using var replay = Publico(api);
        // El mismo código, no uno recalculado: si entre tanto cambió la ventana de 30 s, el recalculado sería otro y válido.
        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarConContrasenaAsync(replay, correo, clave, codigo: siguiente)).StatusCode);

        // Contraseña equivocada: 401 genérico; correo desconocido: el mismo 401.
        using var intruso = Publico(api);
        var mala = await PostAsync(intruso, "/api/v1/identidad/entrar/local", new { correo, contrasena = "otra contraseña cualquiera" });
        var desconocido = await PostAsync(intruso, "/api/v1/identidad/entrar/local", new { correo = CorreoNuevo(), contrasena = Contrasena });
        Assert.Equal(HttpStatusCode.Unauthorized, mala.StatusCode);
        Assert.Equal(await mala.Content.ReadAsStringAsync(Ct), await desconocido.Content.ReadAsStringAsync(Ct));

        // Un código de recuperación sirve una sola vez.
        using var recuperacion = Publico(api);
        var otroPaso = (await (await PostAsync(recuperacion, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena }))
            .Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        var codigo = cuenta.CodigosRecuperacion[0];
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(recuperacion, "/api/v1/identidad/entrar/local/segundo-factor", new { estado = otroPaso, codigoRecuperacion = codigo })).StatusCode);
        using var deNuevo = Publico(api);
        var tercerPaso = (await (await PostAsync(deNuevo, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena }))
            .Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(deNuevo, "/api/v1/identidad/entrar/local/segundo-factor", new { estado = tercerPaso, codigoRecuperacion = codigo })).StatusCode);
        var personaId = await PersonaIdAsync(correo);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.CodigoRecuperacionUsado, personaId.ToString()));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.MfaFallido, personaId.ToString()));
    }

    [Fact]
    public async Task Una_cuenta_externa_sin_MFA_no_puede_entrar()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        await AceptarConContrasenaAsync(invitado, api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion"));
        // Se le quita el TOTP por fuera del flujo: queda solo con contraseña.
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            var usuario = (await usuarios.FindByEmailAsync(correo))!;
            await usuarios.RemoveAuthenticationTokenAsync(usuario, "[AspNetUserStore]", "AuthenticatorKey");
            await usuarios.SetTwoFactorEnabledAsync(usuario, false);
        }

        using var navegador = Publico(api);
        var respuesta = await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal("mfa_requerido", (await respuesta.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error);
        Assert.Null(navegador.Cookie);
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.MfaRequerido, (await PersonaIdAsync(correo)).ToString()));
    }

    [Fact]
    public async Task Un_enlace_vencido_no_sirve()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        var token = api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion");

        reloj.Adelantar(TimeSpan.FromHours(72) + TimeSpan.FromMinutes(1));
        using var invitado = Publico(api);

        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(invitado, "/api/v1/identidad/invitaciones/aceptar", new
        {
            token, nombre = "Externa Demo", documentos = Documentos, factor = new { metodo = "contrasena", estado = "x", contrasena = Contrasena, codigo = "000000" },
        })).StatusCode);
        using var otroAdmin = await AdministradorAsync(api);
        var lista = await (await otroAdmin.GetAsync("/api/v1/plataforma/invitaciones")).Content.ReadFromJsonAsync<List<InvitacionVista>>(Ct);
        Assert.Equal(EstadosInvitacion.EnlaceVencido, lista!.Single(i => i.Correo == correo).Estado);
        // Un token inventado responde lo mismo.
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token = "no-es-un-token" })).StatusCode);
    }

    [Fact]
    public async Task Al_vencer_el_acceso_se_corta_sin_intervencion_y_ya_no_puede_entrar()
    {
        var reloj = new RelojDePrueba();
        // Base propia: el vigilante corre con el reloj adelantado y no debe vencer las asignaciones de otras pruebas.
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(reloj: reloj, baseDeDatos: baseNueva);
        using var admin = await AdministradorAsync(api, baseNueva);
        var correo = CorreoNuevo();
        var vence = DateTimeOffset.UtcNow.AddMinutes(10);
        await InvitarAsync(admin, await OrganizacionAsync(baseDeDatos: baseNueva), correo, vence: vence);
        using var invitado = Publico(api);
        var (clave, _) = await AceptarConContrasenaAsync(invitado, api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion"));
        Assert.Contains(RolesAtlas.Integrador, (await YoAsync(invitado)).Roles);

        reloj.Adelantar(TimeSpan.FromMinutes(11));

        // Sin que nadie intervenga, en la petición siguiente ya no tiene roles; el vigilante cierra la sesión.
        Assert.Empty((await YoAsync(invitado)).Permisos);
        await api.Services.GetRequiredService<VigilanteVencimientos>().ProcesarAsync(Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invitado.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using var luego = Publico(api);
        var paso = await PostAsync(luego, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });
        Assert.Equal(HttpStatusCode.Forbidden, paso.StatusCode);
        Assert.Equal("ingreso_no_permitido", (await paso.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error);
        Assert.Contains(EstadosInvitacion.AccesoVencido,
            (await (await admin.GetAsync("/api/v1/plataforma/invitaciones")).Content.ReadFromJsonAsync<List<InvitacionVista>>(Ct))!
                .Where(i => i.Correo == correo).Select(i => i.Estado));
        Assert.True(await AuditadoAsync(TiposEventoSeguridad.SesionesInvalidadas, (await PersonaIdAsync(correo, baseNueva)).ToString(), baseNueva));
        _ = clave;
    }

    [Fact]
    public async Task La_sesion_de_un_externo_es_mas_corta_que_la_de_un_empleado()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        await AceptarConContrasenaAsync(invitado, api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion"));
        var yo = await YoAsync(invitado);

        await using var identidad = entorno.Identidad();
        var sesion = await identidad.Sesiones.SingleAsync(s => s.UsuarioId == yo.Sub && s.RevocadaEn == null, Ct);
        Assert.InRange(sesion.ExpiraEn - sesion.CreadaEn, TimeSpan.FromMinutes(119), TimeSpan.FromMinutes(121));
        Assert.InRange(sesion.ExpiraAbsolutaEn - sesion.CreadaEn, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    [Fact]
    public async Task Reenviar_invalida_el_enlace_anterior_suspender_y_revocar_cortan_el_acceso_y_renovar_lo_devuelve()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var correo = CorreoNuevo();
        var id = (await InvitarAsync(admin, await OrganizacionAsync(), correo)).Invitacion.Id;
        var primero = buzon.UltimoEnlace(correo, "/invitacion");

        var reenvio = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/reenviar");
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        var segundo = buzon.UltimoEnlace(correo, "/invitacion");
        using var invitado = Publico(api);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token = primero })).StatusCode);

        // Suspendida antes de aceptar: el enlace vigente tampoco sirve; renovarla manda uno nuevo.
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/suspender")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token = segundo })).StatusCode);
        var renovada = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar", JsonContent.Create(new { }));
        Assert.Equal(EstadosInvitacion.Pendiente, (await renovada.Content.ReadFromJsonAsync<InvitacionEnviada>(Ct))!.Invitacion.Estado);
        var (clave, _) = await AceptarConContrasenaAsync(invitado, buzon.UltimoEnlace(correo, "/invitacion"));

        // Suspender una aceptada corta la sesión; renovar la reactiva; revocar es definitivo.
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/suspender")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invitado.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar",
            JsonContent.Create(new { accesoVenceEn = DateTimeOffset.UtcNow.AddDays(30) }))).StatusCode);
        using var deNuevo = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await EntrarConContrasenaAsync(deNuevo, correo, clave, desfase: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/revocar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deNuevo.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using var final = Publico(api);
        Assert.Equal(HttpStatusCode.Forbidden, (await EntrarConContrasenaAsync(final, correo, clave, desfase: 2)).StatusCode);

        foreach (var tipo in new[] { TiposEventoSeguridad.InvitacionReenviada, TiposEventoSeguridad.InvitacionSuspendida,
            TiposEventoSeguridad.InvitacionRenovada, TiposEventoSeguridad.InvitacionRevocada })
        {
            Assert.True(await AuditadoInvitacionAsync(tipo, id), tipo);
        }
    }

    [Fact]
    public async Task Las_reglas_de_la_invitacion_se_validan_en_el_servidor()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string> { ["Identidad:Externos:VigenciaMaximaAcceso"] = "30.00:00:00" });
        using var admin = await AdministradorAsync(api);
        var organizacion = await OrganizacionAsync(TipoOrganizacion.Proveedor);

        async Task<HttpResponseMessage> Invitar(object cuerpo) =>
            await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/invitaciones", JsonContent.Create(cuerpo));

        // Proveedor: vencimiento y alcance obligatorios.
        Assert.Equal(HttpStatusCode.BadRequest, (await Invitar(new { correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Proveedor })).StatusCode);
        // Vencimiento máximo configurable (aquí, 30 días).
        Assert.Equal(HttpStatusCode.BadRequest, (await Invitar(new
        {
            correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Integrador, accesoVenceEn = DateTimeOffset.UtcNow.AddDays(40),
        })).StatusCode);
        // Sin vencimiento, el máximo.
        var porDefecto = await Invitar(new { correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Integrador });
        var vista = (await porDefecto.Content.ReadFromJsonAsync<InvitacionEnviada>(Ct))!.Invitacion;
        Assert.InRange(vista.AccesoVenceEn!.Value - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));
        // Rol interno, organización interna, permiso extra desconocido y correo repetido.
        Assert.Equal(HttpStatusCode.BadRequest, (await Invitar(new { correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Auditor })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Invitar(new { correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Integrador, permisosExtra = new[] { "todo" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Invitar(new { correo = vista.Correo, organizacionId = organizacion, rol = RolesAtlas.Integrador })).StatusCode);
        // ext.admin_organizacion solo si la organización lo habilita.
        Assert.Equal(HttpStatusCode.BadRequest, (await Invitar(new { correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.AdminOrganizacion })).StatusCode);
        var proveedor = await Invitar(new
        {
            correo = CorreoNuevo(), organizacionId = organizacion, rol = RolesAtlas.Proveedor, accesoVenceEn = DateTimeOffset.UtcNow.AddDays(10),
            alcance = new[] { new { tipo = "documento", id = "manual-integracion" } },
        });
        Assert.Equal(HttpStatusCode.Created, proveedor.StatusCode);
    }

    [Fact]
    public async Task Una_contrasena_comun_se_rechaza_al_aceptar()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        var token = api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion");
        using var invitado = Publico(api);
        var clave = (await (await PostAsync(invitado, "/api/v1/identidad/invitaciones/totp/clave", new { token })).Content.ReadFromJsonAsync<ClaveTotp>(Ct))!;

        foreach (var contrasena in new[] { "password1234", "corta" })
        {
            var respuesta = await PostAsync(invitado, "/api/v1/identidad/invitaciones/aceptar", new
            {
                token, nombre = "Externa Demo", documentos = Documentos,
                factor = new { metodo = "contrasena", estado = clave.Estado, contrasena, codigo = Totp.Codigo(clave.Clave) },
            });
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("contrasena_invalida", (await respuesta.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error);
        }
        // Un código TOTP equivocado deshace todo: ni persona ni usuario.
        var malCodigo = await PostAsync(invitado, "/api/v1/identidad/invitaciones/aceptar", new
        {
            token, nombre = "Externa Demo", documentos = Documentos,
            factor = new { metodo = "contrasena", estado = clave.Estado, contrasena = Contrasena, codigo = "000000" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, malCodigo.StatusCode);
        await using var plataforma = entorno.Plataforma();
        Assert.False(await plataforma.Personas.AnyAsync(p => p.Correo == correo, Ct));
        // Sin aceptar los términos tampoco.
        var sinTerminos = await PostAsync(invitado, "/api/v1/identidad/invitaciones/aceptar", new
        {
            token, nombre = "Externa Demo", documentos = Array.Empty<object>(),
            factor = new { metodo = "contrasena", estado = clave.Estado, contrasena = Contrasena, codigo = Totp.Codigo(clave.Clave) },
        });
        Assert.Equal("terminos_no_aceptados", (await sinTerminos.Content.ReadFromJsonAsync<ErrorExternos>(Ct))!.Error);
    }

    [Fact]
    public async Task El_admin_de_la_organizacion_A_no_ve_ni_invita_en_B_ni_amplia_su_alcance()
    {
        await using var api = entorno.Api();
        using var adminAtlas = await AdministradorAsync(api);
        var vence = DateTimeOffset.UtcNow.AddDays(20);
        var adminA = await entorno.ExternoAsync(api, RolesAtlas.AdminOrganizacion, vence);
        var organizacionB = await OrganizacionAsync(admin: true);
        var deB = (await InvitarAsync(adminAtlas, organizacionB, CorreoNuevo())).Invitacion;
        using var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(adminA.UsuarioId)).StatusCode);

        // Invita en su organización, dentro de su vencimiento.
        var propia = await InvitarAsync(navegador, adminA.OrganizacionId, CorreoNuevo(), vence: vence.AddDays(-1));
        Assert.Equal(adminA.OrganizacionId, propia.Invitacion.OrganizacionId);
        // No ve las de B, ni la organización B en sus opciones.
        var lista = (await (await navegador.GetAsync("/api/v1/plataforma/invitaciones")).Content.ReadFromJsonAsync<List<InvitacionVista>>(Ct))!;
        Assert.All(lista, i => Assert.Equal(adminA.OrganizacionId, i.OrganizacionId));
        var opciones = (await (await navegador.GetAsync("/api/v1/plataforma/invitaciones/opciones")).Content.ReadFromJsonAsync<OpcionesInvitacion>(Ct))!;
        Assert.Equal([adminA.OrganizacionId], opciones.Organizaciones.Select(o => o.Id));
        Assert.DoesNotContain(opciones.Roles, r => r.Codigo == RolesAtlas.AdminOrganizacion);
        // No invita en B ni toca sus invitaciones.
        var enB = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/invitaciones", JsonContent.Create(new
        {
            correo = CorreoNuevo(), organizacionId = organizacionB, rol = RolesAtlas.Integrador, accesoVenceEn = vence.AddDays(-1),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, enB.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await navegador.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{deB.Id}/revocar")).StatusCode);
        // Ni amplía: otro administrador, un vencimiento posterior al suyo o permisos extra que no tiene.
        foreach (var cuerpo in new object[]
        {
            new { correo = CorreoNuevo(), organizacionId = adminA.OrganizacionId, rol = RolesAtlas.AdminOrganizacion, accesoVenceEn = vence.AddDays(-1) },
            new { correo = CorreoNuevo(), organizacionId = adminA.OrganizacionId, rol = RolesAtlas.Integrador, accesoVenceEn = vence.AddDays(5) },
            new { correo = CorreoNuevo(), organizacionId = adminA.OrganizacionId, rol = RolesAtlas.Integrador, accesoVenceEn = vence.AddDays(-1), permisosExtra = new[] { "sandbox" } },
        })
        {
            var respuesta = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/invitaciones", JsonContent.Create(cuerpo));
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
        // Con la organización sin el rol habilitado, pierde el permiso en la petición siguiente.
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.Organizaciones.Where(o => o.Id == adminA.OrganizacionId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.AdminOrganizacionHabilitado, false), Ct);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await navegador.GetAsync("/api/v1/plataforma/invitaciones")).StatusCode);
    }

    /// <summary>
    /// F4-SEC-14 (ATLAS-ADR-0049): un administrador de organización no deshace con «renovar» una suspensión hecha por FLIT
    /// (sí la suya), y crear, suspender, renovar y revocar invitaciones piden reautenticación.
    /// </summary>
    [Fact]
    public async Task Un_admin_de_organizacion_no_reactiva_lo_que_suspendio_FLIT_y_las_invitaciones_piden_reautenticacion()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        using var adminAtlas = await AdministradorAsync(api);
        var vence = DateTimeOffset.UtcNow.AddDays(20);
        var adminA = await entorno.ExternoAsync(api, RolesAtlas.AdminOrganizacion, vence);
        using var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(adminA.UsuarioId)).StatusCode);
        var id = (await InvitarAsync(navegador, adminA.OrganizacionId, CorreoNuevo(), vence: vence.AddDays(-1))).Invitacion.Id;
        var renovacion = new { accesoVenceEn = vence.AddDays(-2) };

        Assert.Equal(HttpStatusCode.OK, (await adminAtlas.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/suspender")).StatusCode);
        var renovar = await navegador.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar", JsonContent.Create(renovacion));
        Assert.Equal(HttpStatusCode.Forbidden, renovar.StatusCode);
        Assert.Contains("suspension_de_flit", await renovar.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        // FLIT sí la reactiva; lo que suspende el propio administrador, él también.
        Assert.Equal(HttpStatusCode.OK, (await adminAtlas.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar", JsonContent.Create(renovacion))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/suspender")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar", JsonContent.Create(renovacion))).StatusCode);

        // Con el ingreso de hace más de 10 minutos, las cuatro acciones piden reautenticación (también a FLIT).
        reloj.Adelantar(TimeSpan.FromMinutes(11));
        foreach (var (actor, metodo, ruta, cuerpo) in new (Navegador, string, string, object?)[]
        {
            (navegador, "crear", "/api/v1/plataforma/invitaciones",
                new { correo = CorreoNuevo(), organizacionId = adminA.OrganizacionId, rol = RolesAtlas.Integrador, accesoVenceEn = vence.AddDays(-1) }),
            (navegador, "suspender", $"/api/v1/plataforma/invitaciones/{id}/suspender", null),
            (navegador, "renovar", $"/api/v1/plataforma/invitaciones/{id}/renovar", renovacion),
            (adminAtlas, "revocar", $"/api/v1/plataforma/invitaciones/{id}/revocar", null),
        })
        {
            var respuesta = await actor.EnviarAsync(HttpMethod.Post, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));
            Assert.True(respuesta.StatusCode == HttpStatusCode.Unauthorized, metodo);
            Assert.Contains(Reautenticacion.Codigo, await respuesta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Recuperar_la_cuenta_por_correo_cambia_el_factor_cierra_las_sesiones_y_el_enlace_sirve_una_vez()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        await AceptarConContrasenaAsync(invitado, buzon.UltimoEnlace(correo, "/invitacion"));

        using var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(navegador, "/api/v1/identidad/recuperacion", new { correo })).StatusCode);
        // Un correo desconocido responde igual y no envía nada.
        var desconocido = CorreoNuevo();
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(navegador, "/api/v1/identidad/recuperacion", new { correo = desconocido })).StatusCode);
        Assert.Empty(buzon.Para(desconocido));
        var enlace = buzon.UltimoEnlace(correo, "/recuperar");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(navegador, "/api/v1/identidad/recuperacion/consultar", new { enlace })).StatusCode);

        using var autenticador = new AutenticadorDePrueba();
        var opciones = (await (await PostAsync(navegador, "/api/v1/identidad/recuperacion/passkey/opciones", new { enlace })).Content.ReadFromJsonAsync<OpcionesPasskey>(Ct))!;
        var completar = await PostAsync(navegador, "/api/v1/identidad/recuperacion/completar", new
        {
            enlace, factor = new { metodo = "passkey", estado = opciones.Estado, credencial = autenticador.Registrar(opciones.Opciones) },
        });
        Assert.True(completar.StatusCode == HttpStatusCode.OK, await completar.Content.ReadAsStringAsync(Ct));
        Assert.True((await completar.Content.ReadFromJsonAsync<CuentaLista>(Ct))!.SesionIniciada);

        // La sesión anterior se cerró, la contraseña vieja ya no sirve y el enlace tampoco.
        Assert.Equal(HttpStatusCode.Unauthorized, (await invitado.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using var viejo = Publico(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(viejo, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(viejo, "/api/v1/identidad/recuperacion/consultar", new { enlace })).StatusCode);
        Assert.Equal("externo", (await YoAsync(navegador)).TipoPrincipal);
        var persona = (await PersonaIdAsync(correo)).ToString();
        foreach (var tipo in new[] { TiposEventoSeguridad.RecuperacionSolicitada, TiposEventoSeguridad.FactoresRetirados, TiposEventoSeguridad.RecuperacionCompletada })
        {
            Assert.True(await AuditadoAsync(tipo, persona), tipo);
        }
    }

    [Fact]
    public async Task Un_administrador_puede_forzar_el_restablecimiento_de_una_cuenta_externa()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        await AceptarConContrasenaAsync(invitado, buzon.UltimoEnlace(correo, "/invitacion"));
        var persona = await PersonaIdAsync(correo);

        var respuesta = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{persona}/restablecer");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True((await respuesta.Content.ReadFromJsonAsync<EndpointsCuentasExternas.ResultadoRestablecimiento>(Ct))!.CorreoEnviado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invitado.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        using var navegador = Publico(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena })).StatusCode);
        Assert.NotNull(buzon.UltimoEnlace(correo, "/recuperar"));
        await using (var identidad = entorno.Identidad())
        {
            var usuarioId = await identidad.Users.Where(u => u.PersonaId == persona).Select(u => u.Id).SingleAsync(Ct);
            Assert.True(await AuditadoAsync(TiposEventoSeguridad.RestablecimientoForzado, usuarioId.ToString()));
        }
        // A un empleado no se le restablece desde aquí.
        await using var plataforma = entorno.Plataforma();
        var empleado = await plataforma.Personas.Where(p => p.Tipo == TipoPersona.Empleado).Select(p => p.Id).FirstAsync(Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{empleado}/restablecer")).StatusCode);
    }

    [Fact]
    public async Task Las_versiones_nuevas_de_los_documentos_se_aceptan_en_el_siguiente_ingreso()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var correo = CorreoNuevo();
        await InvitarAsync(admin, await OrganizacionAsync(), correo);
        using var invitado = Publico(api);
        var (clave, _) = await AceptarConContrasenaAsync(invitado, api.Services.GetRequiredService<BuzonDePrueba>().UltimoEnlace(correo, "/invitacion"));
        var persona = await PersonaIdAsync(correo);
        // Como si hubiera aceptado una versión anterior.
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.AceptacionesTerminos.Where(a => a.PersonaId == persona).ExecuteDeleteAsync(Ct);
        }

        using var navegador = Publico(api);
        var respuesta = await EntrarConContrasenaAsync(navegador, correo, clave, desfase: 1);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var pendientes = (await respuesta.Content.ReadFromJsonAsync<EndpointsCuentasExternas.TerminosPendientes>(Ct))!;
        Assert.Equal(2, pendientes.Documentos.Count);
        Assert.Null(navegador.Cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(navegador, "/api/v1/identidad/entrar/terminos", new { estado = pendientes.Estado, documentos = Documentos })).StatusCode);
        Assert.Equal("externo", (await YoAsync(navegador)).TipoPrincipal);
    }

    [Fact]
    public async Task Las_rutas_publicas_de_externos_tienen_un_limite_de_tasa_mas_estricto()
    {
        await using var api = entorno.Api();
        using var navegador = Publico(api);
        var limite = new OpcionesLimiteTasa().ExternosPorIp;

        var estados = new List<HttpStatusCode>();
        for (var i = 0; i <= limite; i++)
        {
            estados.Add((await PostAsync(navegador, "/api/v1/identidad/invitaciones/consultar", new { token = "x" })).StatusCode);
        }

        Assert.All(estados.Take(limite), e => Assert.Equal(HttpStatusCode.Gone, e));
        Assert.Equal(HttpStatusCode.TooManyRequests, estados[^1]);
        Assert.True(limite < new OpcionesLimiteTasa().IngresoPorIp);
    }

    [Fact]
    public async Task Organizaciones_se_crean_editan_suspenden_y_eliminan_con_auditoria()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var nombre = $"Organismo Demo {Guid.NewGuid():N}";

        var creada = await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/organizaciones",
            JsonContent.Create(new { nombre, tipo = "cliente", adminOrganizacionHabilitado = false }));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var vista = (await creada.Content.ReadFromJsonAsync<EndpointsAdministracionExternos.OrganizacionVista>(Ct))!;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/organizaciones",
            JsonContent.Create(new { nombre, tipo = "cliente", adminOrganizacionHabilitado = false }))).StatusCode);
        var editada = await admin.EnviarAsync(HttpMethod.Put, $"/api/v1/plataforma/organizaciones/{vista.Id}",
            JsonContent.Create(new { nombre = nombre + " SAS", tipo = "proveedor", adminOrganizacionHabilitado = true }));
        Assert.True((await editada.Content.ReadFromJsonAsync<EndpointsAdministracionExternos.OrganizacionVista>(Ct))!.AdminOrganizacionHabilitado);

        // Suspender una organización corta las sesiones de sus personas.
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddDays(1));
        using var navegador = Publico(api);
        await navegador.EntrarComoAsync(externo.UsuarioId);
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/organizaciones/{externo.OrganizacionId}/suspender")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/organizaciones/{externo.OrganizacionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/organizaciones/{externo.OrganizacionId}/reactivar")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/organizaciones/{vista.Id}")).StatusCode);
        var lista = (await (await admin.GetAsync("/api/v1/plataforma/organizaciones")).Content.ReadFromJsonAsync<List<EndpointsAdministracionExternos.OrganizacionVista>>(Ct))!;
        Assert.DoesNotContain(lista, o => o.Id == vista.Id);
        Assert.Equal("interna", lista[0].Tipo);
        await using var plataforma = entorno.Plataforma();
        var acciones = await plataforma.Auditoria.Where(a => a.RecursoId == vista.Id.ToString() || a.RecursoId == externo.OrganizacionId.ToString())
            .Select(a => a.Accion).ToListAsync(Ct);
        Assert.Contains(EndpointsAdministracionExternos.AccionOrganizacionCreada, acciones);
        Assert.Contains(EndpointsAdministracionExternos.AccionOrganizacionActualizada, acciones);
        Assert.Contains(EndpointsAdministracionExternos.AccionOrganizacionSuspendida, acciones);
        Assert.Contains(EndpointsAdministracionExternos.AccionOrganizacionEliminada, acciones);
    }

    [Fact]
    public async Task Todos_los_eventos_de_cuentas_externas_quedan_en_las_dos_tablas()
    {
        // Base propia: al final se comparan todas las filas de las dos tablas.
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        using var admin = Publico(api);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.EntrarSimuladoAsync(CorreoNuevo("admin"), "Admin Demo", await entorno.GrupoAdministradorAsync(baseNueva))).StatusCode);
        Guid organizacion;
        await using (var plataforma = entorno.Plataforma(baseNueva))
        {
            var nueva = new Organizacion { Nombre = "Organismo Demo", Tipo = TipoOrganizacion.Cliente };
            plataforma.Organizaciones.Add(nueva);
            await plataforma.SaveChangesAsync(Ct);
            organizacion = nueva.Id;
        }
        var correo = CorreoNuevo();
        buzon.Fallar = true;
        var id = (await InvitarAsync(admin, organizacion, correo)).Invitacion.Id;                                    // invitacion_creada, correo_no_enviado
        buzon.Fallar = false;
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/reenviar");                  // invitacion_reenviada
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/suspender");                 // invitacion_suspendida
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/renovar", JsonContent.Create(new { }));  // invitacion_renovada
        var token = buzon.UltimoEnlace(correo, "/invitacion");
        using var invitado = Publico(api);
        var (clave, cuenta) = await AceptarConContrasenaAsync(invitado, token);                                     // cuenta_creada, factor_registrado, terminos_aceptados, invitacion_aceptada
        await PostAsync(invitado, "/api/v1/identidad/invitaciones/consultar", new { token });                        // enlace_rechazado
        using var navegador = Publico(api);
        var estado = (await (await PostAsync(navegador, "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena }))
            .Content.ReadFromJsonAsync<EndpointsCuentasExternas.PasoIngreso>(Ct))!.Estado;
        await PostAsync(navegador, "/api/v1/identidad/entrar/local/segundo-factor", new { estado, codigo = "000000" });                     // mfa_fallido
        await PostAsync(navegador, "/api/v1/identidad/entrar/local/segundo-factor", new { estado, codigoRecuperacion = cuenta.CodigosRecuperacion[0] }); // mfa_verificado, codigo_recuperacion_usado
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            await usuarios.SetTwoFactorEnabledAsync((await usuarios.FindByEmailAsync(correo))!, false);
        }
        await PostAsync(Publico(api), "/api/v1/identidad/entrar/local", new { correo, contrasena = Contrasena });     // mfa_requerido
        await PostAsync(navegador, "/api/v1/identidad/recuperacion", new { correo });                                // recuperacion_solicitada
        var enlace = buzon.UltimoEnlace(correo, "/recuperar");
        var totp = (await (await PostAsync(navegador, "/api/v1/identidad/recuperacion/totp/clave", new { enlace })).Content.ReadFromJsonAsync<ClaveTotp>(Ct))!;
        await PostAsync(navegador, "/api/v1/identidad/recuperacion/completar", new
        {
            enlace, factor = new { metodo = "contrasena", estado = totp.Estado, contrasena = Contrasena, codigo = Totp.Codigo(totp.Clave) },
        });                                                                                                          // factores_retirados, recuperacion_completada
        Guid persona;
        await using (var plataforma = entorno.Plataforma(baseNueva))
        {
            persona = await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
        }
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{persona}/restablecer");        // restablecimiento_forzado
        await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{id}/revocar");                   // invitacion_revocada
        _ = clave;

        await using var identidad = entorno.Identidad(baseNueva);
        await using var auditoria = entorno.Plataforma(baseNueva);
        var eventos = await identidad.EventosSeguridad.ToListAsync(Ct);
        var acciones = await auditoria.Auditoria.Where(a => a.Accion.StartsWith(TiposEventoSeguridad.PrefijoAuditoria))
            .Select(a => new { a.Accion, a.OcurridoEn }).ToListAsync(Ct);

        Assert.Empty(TiposDeEventoProbados.Except(eventos.Select(e => e.Tipo)));
        Assert.Equal(eventos.Count, acciones.Count);
        Assert.All(eventos, e => Assert.Contains(acciones, a => a.Accion == TiposEventoSeguridad.PrefijoAuditoria + e.Tipo && a.OcurridoEn == e.OcurridoEn));
    }

    [Fact]
    public void En_qa_sin_correo_ni_url_publica_el_arranque_nombra_las_variables()
    {
        var carpeta = Certificados.Carpeta();
        try
        {
            var proteccion = Certificados.Pfx(carpeta, "proteccion", System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment,
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var api = entorno.Api("qa", simulado: false, configuracion: new Dictionary<string, string>
            {
                [$"{ExtensionesIdentidad.SeccionEntra}:TenantId"] = "00000000-0000-0000-0000-00000000a71a",
                [$"{ExtensionesIdentidad.SeccionEntra}:ClientId"] = "00000000-0000-0000-0000-00000000c11e",
                [ExtensionesIdentidad.ClaveCertificadoProteccion] = proteccion.Ruta,
                [ExtensionesIdentidad.ClaveClaveCertificadoProteccion] = Certificados.Clave,
                ["Atlas:UrlPublica"] = "",
                ["Plataforma:Correo:Proveedor"] = "",
                ["Plataforma:Correo:Remitente"] = "",
            });

            var error = Assert.Throws<Flit.Atlas.ServiceDefaults.ConfiguracionFaltanteException>(() => api.CreateClient());

            Assert.Equal(["Atlas:UrlPublica", "Plataforma:Correo:Proveedor", "Plataforma:Correo:Remitente"], error.Claves);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public async Task Si_el_correo_no_sale_la_invitacion_queda_y_se_puede_reenviar()
    {
        await using var api = entorno.Api();
        using var admin = await AdministradorAsync(api);
        var buzon = api.Services.GetRequiredService<BuzonDePrueba>();
        var correo = CorreoNuevo();
        buzon.Fallar = true;
        var enviada = await InvitarAsync(admin, await OrganizacionAsync(), correo);
        buzon.Fallar = false;

        Assert.False(enviada.CorreoEnviado);
        Assert.Null(enviada.Invitacion.EnviadaEn);
        Assert.True(await AuditadoInvitacionAsync(TiposEventoSeguridad.CorreoNoEnviado, enviada.Invitacion.Id));
        var reenvio = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/invitaciones/{enviada.Invitacion.Id}/reenviar");
        Assert.True((await reenvio.Content.ReadFromJsonAsync<InvitacionEnviada>(Ct))!.CorreoEnviado);
        Assert.Single(buzon.Para(correo));
    }
}
