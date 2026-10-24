using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Microsoft365;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Pruebas.Soporte;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.10 (ATLAS-ADR-0033) montada por el host: el calendario compartido con Graph simulado (un manejador HTTP en memoria
/// que registra las llamadas), el aviso en el canal de Teams (flujo de Workflows simulado), el estado que ve la pantalla y
/// los feeds .ics con token. El reloj de atlas-api es un <see cref="FakeTimeProvider"/>: nada avanza sin la prueba.
/// </summary>
public class SoporteM365Tests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Buzon = "soporte-sabados@ejemplo.com";

    private static readonly Guid Desarrollo = SoporteDbContext.EquipoDesarrolloTi;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private static DateOnly ProximoSabado
    {
        get
        {
            var manana = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime).AddDays(1);
            return manana.AddDays(((int)DayOfWeek.Saturday - (int)manana.DayOfWeek + 7) % 7);
        }
    }

    private sealed record Escenario(
        string Base, WebApplicationFactory<Program> Api, FakeTimeProvider Reloj, GraphCalendarioSimulado Graph, TeamsSimulado Teams, Navegador Lider)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Lider.Dispose();
            await Api.DisposeAsync();
        }
    }

    private async Task<Escenario> EscenarioAsync(bool conM365 = true)
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var graph = new GraphCalendarioSimulado();
        var teams = new TeamsSimulado();
        var configuracion = conM365
            ? new Dictionary<string, string> { ["Soporte:M365:Buzon"] = Buzon, ["Plataforma:Teams:Canales:soporte"] = TeamsSimulado.Url }
            : [];
        var api = entorno.Api(baseDeDatos: baseDeDatos, reloj: reloj, configuracion: configuracion, servicios: s =>
        {
            if (conM365)
            {
                s.Replace(ServiceDescriptor.Scoped<IAccesoGraph, AccesoGraphSimulado>());
            }
            s.AddHttpClient(ClienteGraph.Nombre).ConfigurePrimaryHttpMessageHandler(() => new ReenvioA(graph));
            s.AddHttpClient("atlas-teams").ConfigurePrimaryHttpMessageHandler(() => new ReenvioA(teams));
        });
        var (lider, _) = await EmpleadoAsync(api, baseDeDatos, "Líder Demo", "lider.demo", await entorno.GrupoConRolAsync(RolesAtlas.LiderTecnico, baseDeDatos));
        return new Escenario(baseDeDatos, api, reloj, graph, teams, lider);
    }

    private async Task<(Navegador Navegador, Guid Persona)> EmpleadoAsync(
        WebApplicationFactory<Program> api, string baseDeDatos, string nombre, string usuario, params Guid[] grupos)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::33:{Random.Shared.Next(1, 0xffff):x}";
        var correo = $"{usuario}.{Guid.NewGuid():N}@ejemplo.com";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, nombre, grupos)).StatusCode);
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        return (navegador, await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct));
    }

    private async Task<string> CorreoAsync(Escenario e, Guid persona)
    {
        await using var plataforma = entorno.Plataforma(e.Base);
        return await plataforma.Personas.Where(p => p.Id == persona).Select(p => p.Correo).SingleAsync(Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta)
    {
        Assert.True(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}: {await respuesta.Content.ReadAsStringAsync(Ct)}");
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static Task<HttpResponseMessage> Enviar(Navegador n, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        n.EnviarAsync(metodo, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    private static SoporteDbContext Soporte(Escenario e)
    {
        var opciones = new DbContextOptionsBuilder<SoporteDbContext>();
        SoporteDbContext.Configurar(opciones, e.Base);
        return new SoporteDbContext(opciones.Options);
    }

    [Fact]
    public async Task Aprobar_un_cambio_mueve_el_evento_de_una_persona_a_la_otra_en_menos_de_un_minuto()
    {
        await using var e = await EscenarioAsync();
        var grupo = await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, e.Base);
        var (ana, anaId) = await EmpleadoAsync(e.Api, e.Base, "Ana Demo", "ana.demo", grupo);
        var (bruno, brunoId) = await EmpleadoAsync(e.Api, e.Base, "Bruno Demo", "bruno.demo", grupo);
        using var _ = ana;
        using var __ = bruno;
        var (correoAna, correoBruno) = (await CorreoAsync(e, anaId), await CorreoAsync(e, brunoId));
        foreach (var persona in new[] { anaId, brunoId })
        {
            Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{Desarrollo}/participantes", new { personaId = persona })).StatusCode);
        }
        var turno = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = anaId }));
        var turnoId = turno.GetProperty("id").GetGuid();

        // El turno nuevo llega al Outlook de Ana sin mover el reloj: la acción despierta el trabajo.
        await Esperar.HastaAsync(() => e.Graph.VigentesDe(correoAna).Count == 1, "evento de Ana");
        var deAna = e.Graph.VigentesDe(correoAna)[0];
        Assert.Equal("Soporte de los sábados: Desarrollo TI", deAna.Asunto);
        Assert.Equal($"{ProximoSabado:yyyy-MM-dd}T08:00:00", deAna.Inicio);
        Assert.Equal(Buzon, deAna.Buzon);

        // Solicitud → aceptación → aprobación (HU 2.9).
        var solicitud = await JsonAsync(await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio",
            new { turnoId, reemplazoPersonaId = brunoId, llaveIdempotencia = Guid.NewGuid() }));
        var ruta = $"/api/v1/soporte/solicitudes-cambio/{solicitud.GetProperty("id").GetGuid()}";
        await JsonAsync(await Enviar(bruno, HttpMethod.Post, $"{ruta}/respuesta", new { acepta = true, llaveIdempotencia = Guid.NewGuid() }));
        var aprobadoEn = e.Reloj.GetUtcNow();
        await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, $"{ruta}/resolucion", new { aprueba = true, llaveIdempotencia = Guid.NewGuid() }));

        // CA de la HU 2.10: el evento sale del Outlook de Ana y entra al de Bruno en menos de un minuto (aquí, sin que el
        // reloj de la aplicación avance), y el canal de Teams recibe el aviso.
        await Esperar.HastaAsync(() => e.Graph.VigentesDe(correoAna).Count == 0 && e.Graph.VigentesDe(correoBruno).Count == 1, "evento movido a Bruno");
        Assert.True(e.Reloj.GetUtcNow() - aprobadoEn < TimeSpan.FromMinutes(1));
        Assert.True(e.Graph.Eventos[deAna.Id].Cancelado);
        Assert.Contains(e.Graph.Llamadas, l => l == $"POST /v1.0/users/{Buzon}/events/{deAna.Id}/cancel");
        await Esperar.HastaAsync(() => e.Teams.Titulos().Contains("Cambio de turno aprobado: Desarrollo TI"), "aviso en Teams");
        var tarjeta = e.Teams.Tarjetas.Single().ToJsonString();
        Assert.Contains("Bruno Demo", tarjeta, StringComparison.Ordinal);
        Assert.DoesNotContain("@ejemplo.com", tarjeta, StringComparison.Ordinal);
        Assert.Contains("application/vnd.microsoft.card.adaptive", tarjeta, StringComparison.Ordinal);

        // Sin la señal (p. ej. el cambio lo hizo otra réplica), la revisión periódica con el reloj de la aplicación lo
        // recoge antes de un minuto.
        await using (var soporte = Soporte(e))
        {
            var deBruno = await soporte.Turnos.SingleAsync(t => t.TurnoReemplazadoId == turnoId, Ct);
            deBruno.HoraInicio = new TimeOnly(9, 0);
            await soporte.SaveChangesAsync(Ct);
        }
        var desde = e.Reloj.GetUtcNow();
        await Esperar.HastaAsync(() => e.Graph.VigentesDe(correoBruno).SingleOrDefault()?.Inicio == $"{ProximoSabado:yyyy-MM-dd}T09:00:00",
            "horario nuevo en Outlook", mientras: () => e.Reloj.Advance(TimeSpan.FromSeconds(1)));
        Assert.True(e.Reloj.GetUtcNow() - desde < TimeSpan.FromMinutes(1), $"Tardó {(e.Reloj.GetUtcNow() - desde).TotalSeconds} s del reloj.");

        var estado = await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/microsoft-365"));
        Assert.True(estado.GetProperty("calendario").GetProperty("activo").GetBoolean());
        Assert.True(estado.GetProperty("teams").GetProperty("activo").GetBoolean());
        Assert.Equal(0, estado.GetProperty("calendario").GetProperty("pendientes").GetInt32());
    }

    [Fact]
    public async Task Sin_configuracion_todo_se_apaga_limpio_y_el_estado_lo_dice()
    {
        await using var e = await EscenarioAsync(conM365: false);
        Guid liderId;
        var estado = await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/microsoft-365"));
        var calendario = estado.GetProperty("calendario");
        Assert.False(calendario.GetProperty("activo").GetBoolean());
        Assert.Contains("Entra ID", calendario.GetProperty("motivo").GetString(), StringComparison.Ordinal);
        Assert.Equal("Soporte TI sábados", calendario.GetProperty("nombreCalendario").GetString());
        Assert.False(estado.GetProperty("teams").GetProperty("activo").GetBoolean());
        Assert.Contains("Plataforma__Teams__Canales__soporte", estado.GetProperty("teams").GetProperty("motivo").GetString(), StringComparison.Ordinal);

        // Turnos y cambios siguen funcionando; no sale nada a Graph ni a Teams y no se encolan avisos.
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            liderId = await plataforma.Personas.Where(p => p.Nombre == "Líder Demo").Select(p => p.Id).SingleAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{Desarrollo}/participantes", new { personaId = liderId })).StatusCode);
        await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = liderId }));
        e.Reloj.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(300, Ct);
        Assert.Empty(e.Graph.Llamadas);
        Assert.Empty(e.Teams.Tarjetas);
        await using var soporte = Soporte(e);
        Assert.Empty(await soporte.AvisosTeams.ToListAsync(Ct));
        Assert.True((await soporte.EventosCalendario.SingleAsync(Ct)).Pendiente);
    }

    [Fact]
    public async Task Feeds_ics_personal_y_de_equipo_con_token_revocable_y_sin_datos_de_contacto()
    {
        await using var e = await EscenarioAsync(conM365: false);
        var grupo = await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, e.Base);
        var (ana, anaId) = await EmpleadoAsync(e.Api, e.Base, "Ana Demo", "ana.feed", grupo);
        using var _ = ana;
        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{Desarrollo}/participantes", new { personaId = anaId })).StatusCode);
        await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = anaId }));

        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/feeds", new { tipo = "otro" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/feeds", new { tipo = "equipo" })).StatusCode);
        var creado = await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/feeds", new { tipo = "personal" });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var personal = await JsonAsync(creado);
        var url = new Uri(personal.GetProperty("url").GetString()!);
        Assert.StartsWith("webcal://", personal.GetProperty("urlWebcal").GetString(), StringComparison.Ordinal);
        Assert.EndsWith(".ics", url.AbsolutePath, StringComparison.Ordinal);
        var token = url.AbsolutePath.Split('/')[^1][..^4];

        // Sin sesión, con el token: iCalendar válido, sin correos ni teléfonos.
        using var calendario = e.Api.CreateClient();
        var respuesta = await calendario.GetAsync(url.AbsolutePath, Ct);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("text/calendar", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", respuesta.Content.Headers.ContentType.CharSet);
        var ics = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("BEGIN:VTIMEZONE", ics, StringComparison.Ordinal);
        Assert.Contains($"DTSTART;TZID=America/Bogota:{ProximoSabado.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}T080000", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Soporte Desarrollo TI", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("@", ics.Replace("@flit-atlas", "", StringComparison.Ordinal), StringComparison.Ordinal);

        // Solo el hash queda en la base.
        await using (var soporte = Soporte(e))
        {
            var fila = await soporte.FeedsIcs.AsNoTracking().SingleAsync(Ct);
            Assert.NotEqual(token, fila.TokenHash);
            Assert.Equal(64, fila.TokenHash.Length);
            Assert.NotNull(fila.UltimoUsoEn);
        }

        // De equipo: con el nombre de quien atiende.
        var equipo = await JsonAsync(await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/feeds", new { tipo = "equipo", equipoId = Desarrollo }));
        var icsEquipo = await calendario.GetStringAsync(new Uri(equipo.GetProperty("url").GetString()!).AbsolutePath, Ct);
        Assert.Contains("SUMMARY:Soporte Desarrollo TI: Ana Demo", icsEquipo, StringComparison.Ordinal);
        Assert.Equal(2, (await JsonAsync(await ana.GetAsync("/api/v1/soporte/feeds"))).GetArrayLength());

        // Regenerar revoca el anterior; revocar corta el nuevo; un token inventado no existe.
        var regenerado = await JsonAsync(await Enviar(ana, HttpMethod.Post, "/api/v1/soporte/feeds", new { tipo = "personal" }));
        Assert.Equal(HttpStatusCode.NotFound, (await calendario.GetAsync(url.AbsolutePath, Ct)).StatusCode);
        var nueva = new Uri(regenerado.GetProperty("url").GetString()!).AbsolutePath;
        Assert.Equal(HttpStatusCode.OK, (await calendario.GetAsync(nueva, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(e.Lider, HttpMethod.Delete, $"/api/v1/soporte/feeds/{regenerado.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Enviar(ana, HttpMethod.Delete, $"/api/v1/soporte/feeds/{regenerado.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await calendario.GetAsync(nueva, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await calendario.GetAsync($"/api/v1/soporte/ics/{new string('A', 43)}.ics", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await calendario.GetAsync("/api/v1/soporte/ics/corto.ics", Ct)).StatusCode);

        // Si la persona queda inactiva, sus feeds dejan de servir.
        var deEquipo = new Uri(equipo.GetProperty("url").GetString()!).AbsolutePath;
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            (await plataforma.Personas.SingleAsync(p => p.Id == anaId, Ct)).Estado = EstadoPersona.Inactiva;
            await plataforma.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await calendario.GetAsync(deEquipo, Ct)).StatusCode);

        await using var auditoria = entorno.Plataforma(e.Base);
        Assert.Equal(["soporte.feed_ics_creado", "soporte.feed_ics_creado", "soporte.feed_ics_regenerado", "soporte.feed_ics_revocado"],
            await auditoria.Auditoria.Where(a => a.RecursoTipo == "soporte_feed_ics").OrderBy(a => a.OcurridoEn).ThenBy(a => a.Id).Select(a => a.Accion).ToListAsync(Ct));
    }
}
