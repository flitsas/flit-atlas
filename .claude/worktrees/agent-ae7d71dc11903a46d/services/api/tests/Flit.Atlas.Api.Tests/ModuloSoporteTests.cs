using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.6 y 2.8 (ATLAS-ADR-0029): el módulo de soporte montado por el host. Configuración con los valores por defecto,
/// equipos (Desarrollo TI de una persona y Operaciones de una o dos), participantes, generación por equipo con vista
/// previa y confirmación, permisos con alcance al equipo, estados, reemplazos, contacto según permisos y lo que ve un
/// externo. Cada prueba usa su base.
/// </summary>
public class ModuloSoporteTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private static readonly Guid Desarrollo = SoporteDbContext.EquipoDesarrolloTi;
    private static readonly Guid Operaciones = SoporteDbContext.EquipoOperaciones;

    private static DateOnly Hoy => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime);

    /// <summary>El primer sábado después de hoy.</summary>
    private static DateOnly ProximoSabado
    {
        get
        {
            var manana = Hoy.AddDays(1);
            return manana.AddDays(((int)DayOfWeek.Saturday - (int)manana.DayOfWeek + 7) % 7);
        }
    }

    private static string Iso(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Escenario(string Base, WebApplicationFactory<Program> Api, Navegador Lider, Guid OrganizacionInterna) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Lider.Dispose();
            await Api.DisposeAsync();
        }
    }

    private static async Task<Navegador> EmpleadoAsync(WebApplicationFactory<Program> api, string? correo = null, params Guid[] grupos)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::26:{Random.Shared.Next(1, 0xffff):x}";
        var respuesta = await navegador.EntrarSimuladoAsync(correo ?? $"soporte.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo", grupos);
        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        return navegador;
    }

    private async Task<Escenario> EscenarioAsync()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var api = entorno.Api(baseDeDatos: baseDeDatos);
        var correo = $"lider.{Guid.NewGuid():N}@ejemplo.com";
        var lider = await EmpleadoAsync(api, correo, await entorno.GrupoConRolAsync(RolesAtlas.LiderTecnico, baseDeDatos));
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var organizacion = await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.OrganizacionId).SingleAsync(Ct);
        return new Escenario(baseDeDatos, api, lider, organizacion);
    }

    /// <summary>Personas internas ficticias, creadas directamente en la base.</summary>
    private async Task<List<Guid>> PersonasAsync(Escenario e, int cuantas, string? telefono = null)
    {
        await using var plataforma = entorno.Plataforma(e.Base);
        var personas = Enumerable.Range(1, cuantas).Select(i => new Persona
        {
            Tipo = TipoPersona.Empleado,
            OrganizacionId = e.OrganizacionInterna,
            Nombre = $"Persona Demo {i}",
            Correo = $"persona.{i}.{Guid.NewGuid():N}@ejemplo.com",
            Telefono = telefono,
        }).ToList();
        plataforma.Personas.AddRange(personas);
        await plataforma.SaveChangesAsync(Ct);
        return [.. personas.Select(p => p.Id)];
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static Task<HttpResponseMessage> Enviar(Navegador n, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        n.EnviarAsync(metodo, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    private static async Task<List<Guid>> AgregarParticipantesAsync(Navegador lider, Guid equipo, IEnumerable<Guid> personas)
    {
        var ids = new List<Guid>();
        foreach (var persona in personas)
        {
            var respuesta = await Enviar(lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{equipo}/participantes", new { personaId = persona });
            Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
            ids.Add((await JsonAsync(respuesta)).GetProperty("id").GetGuid());
        }
        return ids;
    }

    private static string RutaTurnos(DateOnly desde, DateOnly hasta, Guid? equipo = null) =>
        $"/api/v1/soporte/turnos?desde={Iso(desde)}&hasta={Iso(hasta)}" + (equipo is { } e ? $"&equipo={e}" : string.Empty);

    [Fact]
    public async Task La_configuracion_trae_el_horario_y_la_regla_por_defecto_y_se_puede_cambiar()
    {
        await using var e = await EscenarioAsync();

        var configuracion = await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/configuracion"));
        Assert.Equal("08:00", configuracion.GetProperty("horaInicioPorDefecto").GetString());
        Assert.Equal("12:00", configuracion.GetProperty("horaFinPorDefecto").GetString());
        Assert.Equal("sin_turno", configuracion.GetProperty("reglaFestivos").GetString());

        var malo = await Enviar(e.Lider, HttpMethod.Put, "/api/v1/soporte/configuracion",
            new { horaInicioPorDefecto = "12:00", horaFinPorDefecto = "08:00", reglaFestivos = "inventada" });
        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        var errores = (await JsonAsync(malo)).GetProperty("errors");
        Assert.True(errores.TryGetProperty("horaFinPorDefecto", out _));
        Assert.True(errores.TryGetProperty("reglaFestivos", out _));

        var bueno = await Enviar(e.Lider, HttpMethod.Put, "/api/v1/soporte/configuracion",
            new { horaInicioPorDefecto = "07:30", horaFinPorDefecto = "13:00", reglaFestivos = "turno_normal" });
        Assert.Equal(HttpStatusCode.OK, bueno.StatusCode);
        var equipos = (await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/equipos"))).EnumerateArray().ToList();
        Assert.Equal(["Desarrollo TI", "Operaciones"], equipos.Select(q => q.GetProperty("nombre").GetString()));
        Assert.All(equipos, q => Assert.Equal("07:30 a 13:00", q.GetProperty("horarioEfectivo").GetString()));
        Assert.All(equipos, q => Assert.True(q.GetProperty("puedeAprobar").GetBoolean()));
        await using var plataforma = entorno.Plataforma(e.Base);
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "soporte.configuracion_actualizada", Ct));
    }

    [Fact]
    public async Task La_generacion_por_equipo_es_equitativa_salta_ausencias_y_solo_publica_lo_que_se_confirma()
    {
        await using var e = await EscenarioAsync();
        var personas = await PersonasAsync(e, 5);
        await AgregarParticipantesAsync(e.Lider, Desarrollo, personas.Take(4));
        await AgregarParticipantesAsync(e.Lider, Operaciones, personas.Skip(1));
        // Persona 2 de vacaciones las dos primeras semanas de la ventana (vale en sus dos equipos).
        var ausencia = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/ausencias",
            new { personaId = personas[1], desde = ProximoSabado, hasta = ProximoSabado.AddDays(8), tipo = "vacaciones" });
        Assert.Equal(HttpStatusCode.Created, ausencia.StatusCode);

        var previa = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 4 }));
        var sabados = previa.GetProperty("sabados").EnumerateArray().ToList();

        Assert.InRange(previa.GetProperty("desbalance").GetInt32(), 0, 1);
        Assert.DoesNotContain(sabados, s => DateOnly.Parse(s.GetProperty("fecha").GetString()!, CultureInfo.InvariantCulture) <= ProximoSabado.AddDays(8)
            && s.GetProperty("personas").EnumerateArray().Any(p => p.GetProperty("personaId").GetGuid() == personas[1]));
        Assert.All(sabados.Where(s => s.GetProperty("decision").GetString() == "asignado"), s => Assert.Equal(1, s.GetProperty("personas").GetArrayLength()));
        Assert.Empty((await JsonAsync(await e.Lider.GetAsync(RutaTurnos(ProximoSabado, ProximoSabado.AddMonths(4))))).GetProperty("turnos").EnumerateArray());

        var otraHuella = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/confirmar",
            new { equipoId = Desarrollo, meses = 4, desde = previa.GetProperty("desde").GetString(), huella = new string('0', 64) });
        Assert.Equal(HttpStatusCode.Conflict, otraHuella.StatusCode);
        var confirmada = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/confirmar",
            new { equipoId = Desarrollo, meses = 4, desde = previa.GetProperty("desde").GetString(), huella = previa.GetProperty("huella").GetString() });
        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);
        var nuevos = previa.GetProperty("nuevos").GetInt32();
        Assert.Equal(nuevos, (await JsonAsync(confirmada)).GetProperty("creados").GetInt32());

        // Operaciones (una a dos por sábado): dos personas por sábado, sin repetir a quien ya está en Desarrollo ese día.
        var deDesarrollo = (await JsonAsync(await e.Lider.GetAsync(RutaTurnos(ProximoSabado, ProximoSabado.AddMonths(4), Desarrollo))))
            .GetProperty("turnos").EnumerateArray().ToList();
        Assert.Equal(nuevos, deDesarrollo.Count);
        Assert.All(deDesarrollo, t =>
        {
            Assert.Equal("generado", t.GetProperty("origen").GetString());
            Assert.Equal("08:00", t.GetProperty("horaInicio").GetString());
            Assert.Equal("Desarrollo TI", t.GetProperty("equipo").GetProperty("nombre").GetString());
        });
        var operaciones = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Operaciones, meses = 4 }));
        Assert.InRange(operaciones.GetProperty("desbalance").GetInt32(), 0, 1);
        foreach (var s in operaciones.GetProperty("sabados").EnumerateArray().Where(s => s.GetProperty("decision").GetString() == "asignado"))
        {
            var fecha = s.GetProperty("fecha").GetString();
            var enDesarrollo = deDesarrollo.Where(t => t.GetProperty("fecha").GetString() == fecha).Select(t => t.GetProperty("persona").GetProperty("id").GetGuid()).ToHashSet();
            Assert.InRange(s.GetProperty("personas").GetArrayLength(), 1, 2);
            Assert.DoesNotContain(s.GetProperty("personas").EnumerateArray(), p => enDesarrollo.Contains(p.GetProperty("personaId").GetGuid()));
        }
        await using var plataforma = entorno.Plataforma(e.Base);
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "soporte.generacion_confirmada", Ct));
    }

    [Fact]
    public async Task Regenerar_no_toca_los_turnos_confirmados()
    {
        await using var e = await EscenarioAsync();
        var personas = await PersonasAsync(e, 3);
        var participantes = await AgregarParticipantesAsync(e.Lider, Desarrollo, personas);
        var previa = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 2 }));
        await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/confirmar",
            new { equipoId = Desarrollo, meses = 2, desde = previa.GetProperty("desde").GetString(), huella = previa.GetProperty("huella").GetString() });
        var ruta = RutaTurnos(ProximoSabado, ProximoSabado.AddMonths(2), Desarrollo);
        var primero = (await JsonAsync(await e.Lider.GetAsync(ruta))).GetProperty("turnos")[0];
        var idPrimero = primero.GetProperty("id").GetGuid();
        var personaPrimero = primero.GetProperty("persona").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/turnos/{idPrimero}/estado", new { estado = "confirmado" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/turnos/{idPrimero}/estado", new { estado = "aplicado" })).StatusCode);

        // La persona del turno confirmado sale de la rotación: la regeneración reparte de nuevo los programados, pero el
        // confirmado sigue igual.
        await Enviar(e.Lider, HttpMethod.Put, $"/api/v1/soporte/participantes/{participantes[personas.IndexOf(personaPrimero)]}", new { activo = false });
        var nueva = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 2 }));
        Assert.Equal("fijo", nueva.GetProperty("sabados")[0].GetProperty("decision").GetString());
        var confirmada = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/confirmar",
            new { equipoId = Desarrollo, meses = 2, desde = nueva.GetProperty("desde").GetString(), huella = nueva.GetProperty("huella").GetString() });
        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);

        var turnos = (await JsonAsync(await e.Lider.GetAsync(ruta))).GetProperty("turnos").EnumerateArray().ToList();
        var conservado = turnos.Single(t => t.GetProperty("id").GetGuid() == idPrimero);
        Assert.Equal("confirmado", conservado.GetProperty("estado").GetString());
        Assert.DoesNotContain(turnos.Where(t => t.GetProperty("id").GetGuid() != idPrimero), t => t.GetProperty("persona").GetProperty("id").GetGuid() == personaPrimero);
    }

    [Fact]
    public async Task Gestionar_y_aprobar_con_alcance_a_un_equipo_solo_valen_en_ese_equipo()
    {
        await using var e = await EscenarioAsync();
        await AgregarParticipantesAsync(e.Lider, Desarrollo, await PersonasAsync(e, 2));
        await AgregarParticipantesAsync(e.Lider, Operaciones, await PersonasAsync(e, 2));
        // Una persona de Operaciones con atlas.lider_tecnico solo sobre ese equipo (alcance equipo_soporte:<id>).
        var correo = $"operaciones.{Guid.NewGuid():N}@ejemplo.com";
        using var deOperaciones = await EmpleadoAsync(e.Api, correo);
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            var persona = await plataforma.Personas.SingleAsync(p => p.Correo == correo, Ct);
            var rol = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.LiderTecnico).Select(r => r.Id).SingleAsync(Ct);
            plataforma.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = persona.Id, RolId = rol, OrganizacionId = persona.OrganizacionId, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
                AlcanceTipo = "equipo_soporte", AlcanceId = Operaciones.ToString(),
            });
            await plataforma.SaveChangesAsync(Ct);
        }

        var propia = await JsonAsync(await Enviar(deOperaciones, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Operaciones, meses = 1 }));
        Assert.Equal(HttpStatusCode.OK, (await Enviar(deOperaciones, HttpMethod.Post, "/api/v1/soporte/generacion/confirmar",
            new { equipoId = Operaciones, meses = 1, desde = propia.GetProperty("desde").GetString(), huella = propia.GetProperty("huella").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(deOperaciones, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(deOperaciones, HttpMethod.Put, "/api/v1/soporte/configuracion",
            new { horaInicioPorDefecto = "08:00", horaFinPorDefecto = "12:00", reglaFestivos = "sin_turno" })).StatusCode);
        var equipos = (await JsonAsync(await deOperaciones.GetAsync("/api/v1/soporte/equipos"))).EnumerateArray()
            .ToDictionary(q => q.GetProperty("id").GetGuid(), q => q.GetProperty("puedeAprobar").GetBoolean());
        Assert.True(equipos[Operaciones]);
        Assert.False(equipos[Desarrollo]);

        using var desarrollador = await EmpleadoAsync(e.Api, null, await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, e.Base));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(desarrollador, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desarrollador.GetAsync($"/api/v1/soporte/equipos/{Desarrollo}/participantes")).StatusCode);
    }

    [Fact]
    public async Task Turnos_a_mano_cupo_del_equipo_reemplazo_y_contacto_segun_permisos()
    {
        await using var e = await EscenarioAsync();
        var personas = await PersonasAsync(e, 3, telefono: "3000000000");
        await AgregarParticipantesAsync(e.Lider, Desarrollo, personas.Take(2));
        await AgregarParticipantesAsync(e.Lider, Operaciones, personas);

        var noSabado = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado.AddDays(1), personaId = personas[0] });
        Assert.Equal(HttpStatusCode.BadRequest, noSabado.StatusCode);
        var creado = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos",
            new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = personas[0], horaInicio = "09:00", horaFin = "13:00" });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var turno = await JsonAsync(creado);
        Assert.Equal("manual", turno.GetProperty("origen").GetString());
        Assert.Equal("09:00", turno.GetProperty("horaInicio").GetString());
        // Desarrollo TI admite una persona por sábado; Operaciones, dos.
        var completo = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = personas[1] });
        Assert.Equal(HttpStatusCode.Conflict, completo.StatusCode);
        Assert.Equal("equipo_completo", (await JsonAsync(completo)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Operaciones, fecha = ProximoSabado, personaId = personas[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Operaciones, fecha = ProximoSabado, personaId = personas[2] })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Operaciones, fecha = ProximoSabado, personaId = personas[0] })).StatusCode);

        var reemplazo = await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/turnos/{turno.GetProperty("id").GetGuid()}/reemplazo", new { personaId = personas[1] });
        Assert.Equal(HttpStatusCode.Created, reemplazo.StatusCode);
        var nuevo = await JsonAsync(reemplazo);
        Assert.Equal(turno.GetProperty("id").GetGuid(), nuevo.GetProperty("turnoReemplazadoId").GetGuid());
        Assert.Equal("09:00", nuevo.GetProperty("horaInicio").GetString());

        // El widget de Inicio: a quién llamar por equipo; el líder (atlas.equipo.ver_contacto) ve el teléfono, un lector no.
        using var lector = await EmpleadoAsync(e.Api);
        var paraLider = await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/este-sabado"));
        var paraLector = await JsonAsync(await lector.GetAsync("/api/v1/soporte/este-sabado"));
        Assert.Equal(Iso(ProximoSabado), paraLector.GetProperty("fecha").GetString());
        var equipos = paraLector.GetProperty("equipos").EnumerateArray().ToDictionary(q => q.GetProperty("equipo").GetProperty("nombre").GetString()!);
        Assert.Equal([personas[1]], equipos["Desarrollo TI"].GetProperty("turnos").EnumerateArray().Select(t => t.GetProperty("persona").GetProperty("id").GetGuid()));
        Assert.Equal(2, equipos["Operaciones"].GetProperty("turnos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, equipos["Operaciones"].GetProperty("turnos")[0].GetProperty("persona").GetProperty("telefono").ValueKind);
        Assert.Equal("3000000000", paraLider.GetProperty("equipos")[0].GetProperty("turnos")[0].GetProperty("persona").GetProperty("telefono").GetString());

        var indicadores = await JsonAsync(await e.Lider.GetAsync($"/api/v1/soporte/indicadores?desde={Iso(ProximoSabado)}&hasta={Iso(ProximoSabado)}&equipo={Desarrollo}"));
        Assert.Equal(1, indicadores.GetProperty("turnos").GetInt32());
        Assert.Equal(1, indicadores.GetProperty("reemplazos").GetInt32());

        // Las ausencias (dato sensible) solo con atlas.soporte.gestionar.
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync("/api/v1/soporte/ausencias")).StatusCode);
    }

    [Fact]
    public async Task Un_externo_solo_ve_el_canal_configurado_sin_nombres()
    {
        await using var e = await EscenarioAsync();
        var externo = await entorno.ExternoAsync(e.Api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddDays(10), e.Base);
        using var navegador = Navegador.De(e.Api);
        navegador.Ip = $"2001:db8::26:{Random.Shared.Next(1, 0xffff):x}";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(externo.UsuarioId)).StatusCode);

        var sinCanal = await JsonAsync(await navegador.GetAsync("/api/v1/soporte/canal"));
        Assert.False(sinCanal.GetProperty("configurado").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sinCanal.GetProperty("canal").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await Enviar(e.Lider, HttpMethod.Put, "/api/v1/soporte/configuracion",
            new { horaInicioPorDefecto = "08:00", horaFinPorDefecto = "12:00", reglaFestivos = "sin_turno", canalExterno = "soporte@ejemplo.com" })).StatusCode);
        var personas = await PersonasAsync(e, 1);
        await AgregarParticipantesAsync(e.Lider, Desarrollo, personas);
        await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = CalendarioDe(), personaId = personas[0] });
        var respuesta = await navegador.GetAsync("/api/v1/soporte/canal");
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        var canal = JsonDocument.Parse(texto).RootElement;
        Assert.True(canal.GetProperty("configurado").GetBoolean());
        Assert.Equal("soporte@ejemplo.com", canal.GetProperty("canal").GetString());
        Assert.DoesNotContain("Persona Demo", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Desarrollo", texto, StringComparison.Ordinal);

        foreach (var ruta in new[] { "/api/v1/soporte/turnos", "/api/v1/soporte/este-sabado", "/api/v1/soporte/equipos", "/api/v1/soporte/indicadores" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await navegador.GetAsync(ruta)).StatusCode);
        }
    }

    /// <summary>El sábado que muestra el canal: hoy si es sábado, si no el próximo.</summary>
    private static DateOnly CalendarioDe() => Hoy.AddDays(((int)DayOfWeek.Saturday - (int)Hoy.DayOfWeek + 7) % 7);

    [Fact]
    public async Task Los_festivos_del_anio_salen_calculados_y_se_suman_los_adicionales()
    {
        await using var e = await EscenarioAsync();

        var festivos = (await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/festivos?anio=2027"))).EnumerateArray().ToList();
        Assert.Equal(19, festivos.Count);
        Assert.Contains(festivos, f => f.GetProperty("fecha").GetString() == "2027-08-07" && f.GetProperty("tipo").GetString() == "fijo");

        var agregado = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/festivos", new { fecha = "2027-12-24", nombre = "Día cívico de prueba" });
        Assert.Equal(HttpStatusCode.Created, agregado.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/festivos", new { fecha = "2027-12-24", nombre = "Otra vez" })).StatusCode);
        Assert.Equal(20, (await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/festivos?anio=2027"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Lider.GetAsync("/api/v1/soporte/festivos?anio=1900")).StatusCode);
    }
}
