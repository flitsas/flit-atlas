using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>HU 1.4: administración de asignaciones con alcance y vigencia, mapeos de grupos de Entra y vencimientos.</summary>
public class RolesTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CorreoNuevo() => $"empleado.{Guid.NewGuid():N}@ejemplo.com";

    private static string IpNueva() => $"2001:db8::23:{Random.Shared.Next(1, 0xffff):x}";

    private async Task<(Navegador Navegador, string Correo)> EntrarConRolAsync(WebApplicationFactory<Program> api, string? rol)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        var correo = CorreoNuevo();
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Persona Demo", grupos)).StatusCode);
        return (navegador, correo);
    }

    private async Task<Guid> PersonaIdAsync(string correo)
    {
        await using var plataforma = entorno.Plataforma();
        return await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
    }

    private static async Task<EndpointsIdentidad.Yo> YoAsync(Navegador navegador)
    {
        var respuesta = await navegador.GetAsync("/api/v1/plataforma/yo");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct))!;
    }

    private static Task<HttpResponseMessage> AsignarAsync(Navegador admin, Guid persona, object solicitud) =>
        admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/personas/{persona}/asignaciones", JsonContent.Create(solicitud));

    private async Task<List<string>> AccionesAsync(string recursoId)
    {
        await using var plataforma = entorno.Plataforma();
        return await plataforma.Auditoria.Where(a => a.RecursoId == recursoId).Select(a => a.Accion).ToListAsync(Ct);
    }

    [Fact]
    public async Task Asignar_un_rol_con_vigencia_se_suma_desde_la_siguiente_peticion_y_queda_auditado()
    {
        await using var api = entorno.Api();
        var (admin, correoAdmin) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (persona, correo) = await EntrarConRolAsync(api, null);
        var personaId = await PersonaIdAsync(correo);
        var vence = DateTimeOffset.UtcNow.AddDays(7);

        var respuesta = await AsignarAsync(admin, personaId, new { rol = RolesAtlas.Auditor, vigenteHasta = vence });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var asignacion = (await respuesta.Content.ReadFromJsonAsync<EndpointsRoles.AsignacionVista>(Ct))!;
        Assert.Equal(RolesAtlas.Auditor, asignacion.Rol);
        Assert.Equal("manual", asignacion.Origen);
        Assert.Equal(TiposAlcance.Global, asignacion.Alcance.Tipo);
        Assert.True(asignacion.Vigente);
        Assert.Equal(await PersonaIdAsync(correoAdmin), asignacion.AsignadoPor);
        // Sin volver a entrar: los permisos se calculan en cada petición.
        var yo = await YoAsync(persona);
        Assert.Contains(RolesAtlas.Auditor, yo.Roles);
        Assert.Contains(PermisosPlataforma.AuditoriaVer, yo.Permisos);
        Assert.Contains(AccionesAuditoriaRoles.AsignacionCreada, await AccionesAsync(asignacion.Id.ToString()));
        // Las manuales se suman a las de grupos de Entra y se listan juntas.
        var lista = await admin.GetAsync($"/api/v1/plataforma/personas/{personaId}/asignaciones");
        Assert.Single((await lista.Content.ReadFromJsonAsync<List<EndpointsRoles.AsignacionVista>>(Ct))!, a => a.Id == asignacion.Id);
    }

    [Fact]
    public async Task Retirar_una_asignacion_invalida_las_sesiones_y_queda_auditado()
    {
        await using var api = entorno.Api();
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (persona, correo) = await EntrarConRolAsync(api, null);
        var personaId = await PersonaIdAsync(correo);
        var creada = await (await AsignarAsync(admin, personaId, new { rol = RolesAtlas.Negocio }))
            .Content.ReadFromJsonAsync<EndpointsRoles.AsignacionVista>(Ct);

        var respuesta = await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/asignaciones/{creada!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await persona.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Contains(AccionesAuditoriaRoles.AsignacionRetirada, await AccionesAsync(creada.Id.ToString()));
        await using var identidad = entorno.Identidad();
        Assert.True(await identidad.EventosSeguridad.AnyAsync(
            e => e.PersonaId == personaId && e.Tipo == TiposEventoSeguridad.SesionesInvalidadas, Ct));
    }

    [Fact]
    public async Task Una_asignacion_de_grupo_de_Entra_no_se_retira_a_mano()
    {
        await using var api = entorno.Api();
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (_, correo) = await EntrarConRolAsync(api, RolesAtlas.Desarrollador);
        var personaId = await PersonaIdAsync(correo);
        await using var plataforma = entorno.Plataforma();
        var deGrupo = await plataforma.AsignacionesRol.Where(a => a.PersonaId == personaId && a.Origen == OrigenAsignacion.GrupoEntra)
            .Select(a => a.Id).SingleAsync(Ct);

        var respuesta = await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/asignaciones/{deGrupo}");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("asignacion_de_grupo_entra", (await respuesta.Content.ReadFromJsonAsync<EndpointsRoles.ErrorRoles>(Ct))!.Error);
        // La sincronización con Entra también quedó en la auditoría.
        Assert.Contains(AccionesAuditoriaRoles.AsignacionCreada, await AccionesAsync(deGrupo.ToString()));
    }

    public static TheoryData<string, string> Invalidas() => new()
    {
        { """{"rol":"ext.integrador","vigenteHasta":"2099-01-01T00:00:00Z"}""", "rol_no_corresponde_a_la_persona" },
        { """{"rol":"atlas.responsable"}""", "rol_no_asignable" },
        { """{"rol":"atlas.lector"}""", "rol_no_asignable" },
        { """{"rol":"atlas.inventado"}""", "rol_no_asignable" },
        { """{"rol":"atlas.auditor","alcance":{"tipo":"aplicativo"}}""", "alcance_invalido" },
        { """{"rol":"atlas.auditor","alcance":{"tipo":"planeta","id":"x"}}""", "alcance_desconocido" },
        { """{"rol":"atlas.auditor","vigenteHasta":"2000-01-01T00:00:00Z"}""", "vigencia_invalida" },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Una_asignacion_invalida_responde_400_con_su_codigo(string solicitud, string codigo)
    {
        await using var api = entorno.Api();
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (_, correo) = await EntrarConRolAsync(api, null);

        var respuesta = await admin.EnviarAsync(HttpMethod.Post, $"/api/v1/plataforma/personas/{await PersonaIdAsync(correo)}/asignaciones",
            new StringContent(solicitud, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(Ct));
        Assert.Equal(codigo, problema.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_rol_externo_exige_vencimiento_de_maximo_90_dias()
    {
        await using var api = entorno.Api();
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddDays(1));

        var sinVencimiento = await AsignarAsync(admin, externo.PersonaId, new { rol = RolesAtlas.Proveedor });
        var muyLargo = await AsignarAsync(admin, externo.PersonaId, new { rol = RolesAtlas.Proveedor, vigenteHasta = DateTimeOffset.UtcNow.AddDays(91) });
        var otraOrganizacion = await AsignarAsync(admin, externo.PersonaId,
            new { rol = RolesAtlas.Proveedor, organizacionId = Guid.NewGuid(), vigenteHasta = DateTimeOffset.UtcNow.AddDays(30) });
        var valida = await AsignarAsync(admin, externo.PersonaId, new
        {
            rol = RolesAtlas.Proveedor,
            alcance = new { tipo = TiposAlcance.Aplicativo, id = "flito" },
            vigenteHasta = DateTimeOffset.UtcNow.AddDays(30),
        });

        Assert.Equal(HttpStatusCode.BadRequest, sinVencimiento.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, muyLargo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otraOrganizacion.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valida.StatusCode);
        var creada = (await valida.Content.ReadFromJsonAsync<EndpointsRoles.AsignacionVista>(Ct))!;
        Assert.Equal(externo.OrganizacionId, creada.OrganizacionId);
        Assert.Equal(new EndpointsRoles.AlcanceVista(TiposAlcance.Aplicativo, "flito"), creada.Alcance);
    }

    [Fact]
    public async Task Cambiar_roles_exige_reautenticacion()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (_, correo) = await EntrarConRolAsync(api, null);
        reloj.Adelantar(TimeSpan.FromMinutes(11));

        var respuesta = await AsignarAsync(admin, await PersonaIdAsync(correo), new { rol = RolesAtlas.Negocio });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(Ct));
        Assert.Equal(Reautenticacion.Codigo, problema.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task El_lider_tecnico_ve_la_matriz_pero_no_asigna_roles_ni_suspende()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EntrarConRolAsync(api, RolesAtlas.LiderTecnico);
        var (_, correo) = await EntrarConRolAsync(api, null);
        var personaId = await PersonaIdAsync(correo);

        Assert.Equal(HttpStatusCode.OK, (await lider.GetAsync("/api/v1/plataforma/permisos/matriz")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AsignarAsync(lider, personaId, new { rol = RolesAtlas.Negocio })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await lider.EnviarAsync(HttpMethod.Post, $"/api/v1/identidad/admin/personas/{personaId}/suspender")).StatusCode);
    }

    [Fact]
    public async Task Eliminar_un_mapeo_de_grupo_retira_el_rol_e_invalida_las_sesiones()
    {
        await using var api = entorno.Api();
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var grupo = Guid.NewGuid();
        // Un rol que ningún otro grupo de las pruebas otorga.
        var rol = RolesAtlas.GobiernoIa;
        await using (var plataforma = entorno.Plataforma())
        {
            var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync(Ct);
            await plataforma.MapeosGrupoEntra.Where(m => m.RolId == rolId).ExecuteDeleteAsync(Ct);
        }

        var creado = await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/mapeos-grupos-entra",
            JsonContent.Create(new EndpointsRoles.SolicitudMapeoGrupo(grupo, "FLIT-Gobierno-IA-Demo", rol)));
        var aExterno = await admin.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/mapeos-grupos-entra",
            JsonContent.Create(new EndpointsRoles.SolicitudMapeoGrupo(Guid.NewGuid(), "FLIT-Externo-Demo", RolesAtlas.Integrador)));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, aExterno.StatusCode);
        var mapeo = (await creado.Content.ReadFromJsonAsync<EndpointsRoles.MapeoGrupoVista>(Ct))!;

        using var miembro = Navegador.De(api);
        miembro.Ip = IpNueva();
        var correo = CorreoNuevo();
        await miembro.EntrarSimuladoAsync(correo, "Miembro Demo", grupo);
        Assert.Contains(rol, (await YoAsync(miembro)).Roles);

        var eliminado = await admin.EnviarAsync(HttpMethod.Delete, $"/api/v1/plataforma/mapeos-grupos-entra/{mapeo.Id}");

        Assert.Equal(HttpStatusCode.NoContent, eliminado.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await miembro.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        await miembro.EntrarSimuladoAsync(correo, "Miembro Demo", grupo);
        Assert.DoesNotContain(rol, (await YoAsync(miembro)).Roles);
        Assert.Equal([AccionesAuditoriaRoles.MapeoCreado, AccionesAuditoriaRoles.MapeoEliminado], (await AccionesAsync(mapeo.Id.ToString())).Order());
    }

    [Fact]
    public async Task Un_rol_externo_vencido_pierde_el_acceso_sin_intervencion()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddHours(1));
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(externo.UsuarioId)).StatusCode);
        var antes = await YoAsync(navegador);

        reloj.Adelantar(TimeSpan.FromHours(2));
        // Nadie hizo nada: la sesión sigue viva, pero el rol ya no cuenta.
        var despues = await YoAsync(navegador);

        Assert.Equal([RolesAtlas.Integrador], antes.Roles);
        Assert.Contains("atlas.portal.ver", antes.Permisos);
        Assert.Empty(despues.Roles);
        Assert.Empty(despues.Permisos);

        // El vigilante (cada minuto en atlas-api) deja el vencimiento en la auditoría e invalida la sesión.
        await api.Services.GetRequiredService<VigilanteVencimientos>().ProcesarAsync(Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal([AccionesAuditoriaRoles.AsignacionVencida],
            (await AccionesAsync(externo.AsignacionId.ToString())).Where(a => a == AccionesAuditoriaRoles.AsignacionVencida));
        Assert.True(await Auditoria.RegistradoAsync(entorno, TiposEventoSeguridad.SesionesInvalidadas, externo.PersonaId.ToString()));
        // Procesarlo otra vez no lo repite.
        await api.Services.GetRequiredService<VigilanteVencimientos>().ProcesarAsync(Ct);
        Assert.Single(await AccionesAsync(externo.AsignacionId.ToString()), a => a == AccionesAuditoriaRoles.AsignacionVencida);
    }

    [Fact]
    public async Task Un_rol_interno_vencido_deja_de_abrir_los_endpoints_de_su_permiso()
    {
        var reloj = new RelojDePrueba();
        await using var api = entorno.Api(reloj: reloj);
        var (admin, _) = await EntrarConRolAsync(api, RolesAtlas.Superadmin);
        var (auditor, correo) = await EntrarConRolAsync(api, null);
        Assert.Equal(HttpStatusCode.Created, (await AsignarAsync(admin, await PersonaIdAsync(correo),
            new { rol = RolesAtlas.Auditor, vigenteHasta = DateTimeOffset.UtcNow.AddHours(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/api/v1/plataforma/permisos/matriz")).StatusCode);

        reloj.Adelantar(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.GetAsync("/api/v1/plataforma/permisos/matriz")).StatusCode);
    }
}
