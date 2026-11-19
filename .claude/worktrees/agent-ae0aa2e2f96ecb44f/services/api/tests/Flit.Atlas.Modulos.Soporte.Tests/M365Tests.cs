using System.Text;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Microsoft365;
using Flit.Atlas.Pruebas.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// HU 2.10 (ATLAS-ADR-0033) en el módulo: la marca de pendiente de cada turno en su transacción, la sincronización
/// idempotente con el calendario compartido (Graph simulado), los reintentos con el reloj falso, la cola de avisos de Teams
/// y el iCalendar de los feeds.
/// </summary>
public class M365Tests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Buzon = "soporte-sabados@ejemplo.com";

    private static readonly Guid Desarrollo = SoporteDbContext.EquipoDesarrolloTi;

    /// <summary>Un viernes a mediodía en Bogotá: el sábado siguiente es «de hoy en adelante».</summary>
    private static readonly DateTimeOffset Inicio = new(2026, 11, 6, 17, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Sabado = new(2026, 11, 7);

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false) { BaseAddress = ClienteGraph.Base };
    }

    private sealed class TeamsEnMemoria(bool activo = true) : INotificadorTeams
    {
        public List<MensajeTeams> Enviados { get; } = [];

        public int Fallas { get; set; }

        public bool Configurado(string canal) => activo && canal == "soporte";

        public Task EnviarAsync(string canal, MensajeTeams mensaje, CancellationToken ct = default)
        {
            if (Fallas > 0)
            {
                Fallas--;
                throw new AvisoTeamsNoEnviadoException("El flujo de Teams respondió 503.", System.Net.HttpStatusCode.ServiceUnavailable);
            }
            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class SinGraph : IAccesoGraph
    {
        public bool Disponible => false;

        public Task<string> CabeceraAsync(CancellationToken ct = default) => throw new InvalidOperationException("Sin Entra.");
    }

    private sealed record Escenario(BaseDePrueba Bd, FakeTimeProvider Reloj, GraphCalendarioSimulado Graph, TeamsEnMemoria Teams, Persona Ana, Persona Bruno)
    {
        public SoporteDbContext Soporte()
        {
            var opciones = new DbContextOptionsBuilder<SoporteDbContext>();
            SoporteDbContext.Configurar(opciones, Bd.Servicio);
            return new SoporteDbContext(opciones.Options, null, Reloj);
        }

        public async Task<CicloM365> CicloAsync(OpcionesM365Soporte? opciones = null, IAccesoGraph? acceso = null)
        {
            await using var soporte = Soporte();
            await using var plataforma = Bd.Plataforma();
            var graph = acceso ?? new AccesoGraphSimulado();
            var sincronizador = new SincronizadorM365(soporte, plataforma, graph, new CalendarioGraph(new Fabrica(Graph), graph), Teams,
                Options.Create(opciones ?? new OpcionesM365Soporte { Buzon = Buzon }), Reloj, NullLogger<SincronizadorM365>.Instance);
            return await sincronizador.SincronizarAsync(Ct);
        }

        public async Task<EventoCalendario> FilaAsync(Guid turno)
        {
            await using var soporte = Soporte();
            return await soporte.EventosCalendario.AsNoTracking().SingleAsync(e => e.TurnoId == turno, Ct);
        }
    }

    private async Task<Escenario> EscenarioAsync()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var plataforma = bd.Plataforma();
        var organizacion = new Organizacion { Nombre = "FLIT Demo", Tipo = TipoOrganizacion.Interna };
        var ana = new Persona { Tipo = TipoPersona.Empleado, OrganizacionId = organizacion.Id, Nombre = "Ana Demo", Correo = "ana.demo@ejemplo.com" };
        var bruno = new Persona { Tipo = TipoPersona.Empleado, OrganizacionId = organizacion.Id, Nombre = "Bruno Demo", Correo = "bruno.demo@ejemplo.com" };
        plataforma.AddRange(organizacion, ana, bruno);
        await plataforma.SaveChangesAsync(Ct);
        return new Escenario(bd, new FakeTimeProvider(Inicio), new GraphCalendarioSimulado(), new TeamsEnMemoria(), ana, bruno);
    }

    private static async Task<Turno> TurnoAsync(Escenario e, Guid persona, DateOnly? fecha = null)
    {
        await using var soporte = e.Soporte();
        var turno = new Turno { EquipoId = Desarrollo, Fecha = fecha ?? Sabado, PersonaId = persona, Origen = OrigenTurno.Manual, HoraInicio = new(8, 0), HoraFin = new(12, 0) };
        soporte.Turnos.Add(turno);
        await soporte.SaveChangesAsync(Ct);
        return turno;
    }

    [Fact]
    public async Task Cada_cambio_del_turno_lo_deja_pendiente_en_su_transaccion_y_la_observacion_no()
    {
        var e = await EscenarioAsync();
        var turno = await TurnoAsync(e, e.Ana.Id);
        var fila = await e.FilaAsync(turno.Id);
        Assert.True(fila.Pendiente);
        Assert.Equal(1, fila.Version);

        await using (var soporte = e.Soporte())
        {
            var t = await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct);
            t.Observacion = "Nota sin efecto en Outlook";
            await soporte.SaveChangesAsync(Ct);
        }
        Assert.Equal(1, (await e.FilaAsync(turno.Id)).Version);

        await using (var soporte = e.Soporte())
        {
            var t = await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct);
            t.HoraFin = new TimeOnly(13, 0);
            await soporte.SaveChangesAsync(Ct);
        }
        Assert.Equal(2, (await e.FilaAsync(turno.Id)).Version);

        // Un turno borrado (la generación o el importador) conserva su fila para cancelar el evento.
        await using (var soporte = e.Soporte())
        {
            soporte.Turnos.Remove(await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct));
            await soporte.SaveChangesAsync(Ct);
        }
        Assert.Equal((true, 3), ((await e.FilaAsync(turno.Id)).Pendiente, (await e.FilaAsync(turno.Id)).Version));
    }

    [Fact]
    public async Task Crea_actualiza_mueve_y_cancela_el_evento_sin_duplicar()
    {
        var e = await EscenarioAsync();
        var turno = await TurnoAsync(e, e.Ana.Id);

        Assert.Equal(1, (await e.CicloAsync()).Eventos);
        var evento = Assert.Single(e.Graph.VigentesDe(e.Ana.Correo));
        Assert.Equal("Soporte de los sábados: Desarrollo TI", evento.Asunto);
        Assert.Equal(("2026-11-07T08:00:00", "2026-11-07T12:00:00", CalendarioGraph.ZonaGraph), (evento.Inicio, evento.Fin, evento.Zona));
        Assert.Contains($"POST /v1.0/users/{Buzon}/events", e.Graph.Llamadas);
        var fila = await e.FilaAsync(turno.Id);
        Assert.Equal((false, evento.Id, e.Ana.Id), (fila.Pendiente, fila.EventoGraphId, fila.PersonaSincronizadaId));

        // Repetir el ciclo sin cambios no llama a Graph.
        var llamadas = e.Graph.Llamadas.Count;
        Assert.Equal(0, (await e.CicloAsync()).Eventos);
        Assert.Equal(llamadas, e.Graph.Llamadas.Count);

        // Cambio de horario: el mismo evento se actualiza.
        await using (var soporte = e.Soporte())
        {
            (await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct)).HoraInicio = new TimeOnly(9, 0);
            await soporte.SaveChangesAsync(Ct);
        }
        await e.CicloAsync();
        Assert.Equal(("2026-11-07T09:00:00", 1), (e.Graph.Eventos[evento.Id].Inicio, e.Graph.Eventos[evento.Id].Actualizaciones));

        // Reemplazo (HU 2.9): el original queda reemplazado y nace el del reemplazo; el evento pasa de Ana a Bruno.
        Guid nuevo;
        await using (var soporte = e.Soporte())
        {
            (await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct)).Estado = EstadoTurno.Reemplazado;
            await soporte.SaveChangesAsync(Ct);
            var t = new Turno { EquipoId = Desarrollo, Fecha = Sabado, PersonaId = e.Bruno.Id, Origen = OrigenTurno.Manual, TurnoReemplazadoId = turno.Id };
            soporte.Turnos.Add(t);
            await soporte.SaveChangesAsync(Ct);
            nuevo = t.Id;
        }
        Assert.Equal(2, (await e.CicloAsync()).Eventos);
        Assert.Empty(e.Graph.VigentesDe(e.Ana.Correo));
        Assert.Single(e.Graph.VigentesDe(e.Bruno.Correo));
        Assert.Null((await e.FilaAsync(turno.Id)).EventoGraphId);

        // Cancelar el turno cancela el evento.
        await using (var soporte = e.Soporte())
        {
            (await soporte.Turnos.SingleAsync(x => x.Id == nuevo, Ct)).Estado = EstadoTurno.Cancelado;
            await soporte.SaveChangesAsync(Ct);
        }
        await e.CicloAsync();
        Assert.Empty(e.Graph.VigentesDe(e.Bruno.Correo));
        Assert.Equal(2, e.Graph.Eventos.Count);
    }

    [Fact]
    public async Task Un_turno_que_ya_paso_no_toca_su_evento_al_quedar_aplicado()
    {
        var e = await EscenarioAsync();
        var turno = await TurnoAsync(e, e.Ana.Id);
        await e.CicloAsync();
        var evento = Assert.Single(e.Graph.VigentesDe(e.Ana.Correo));

        e.Reloj.Advance(TimeSpan.FromDays(2));
        await using (var soporte = e.Soporte())
        {
            var t = await soporte.Turnos.SingleAsync(x => x.Id == turno.Id, Ct);
            t.Estado = EstadoTurno.Confirmado;
            await soporte.SaveChangesAsync(Ct);
            t.Estado = EstadoTurno.Aplicado;
            await soporte.SaveChangesAsync(Ct);
        }
        var llamadas = e.Graph.Llamadas.Count;
        await e.CicloAsync();
        Assert.Equal(llamadas, e.Graph.Llamadas.Count);
        Assert.False(e.Graph.Eventos[evento.Id].Cancelado);
        Assert.Equal((false, evento.Id), ((await e.FilaAsync(turno.Id)).Pendiente, (await e.FilaAsync(turno.Id)).EventoGraphId));
    }

    [Fact]
    public async Task Un_fallo_se_reintenta_con_espera_y_el_reintento_no_duplica()
    {
        var e = await EscenarioAsync();
        var turno = await TurnoAsync(e, e.Ana.Id);
        e.Graph.FallasPendientes = 1;

        Assert.Equal(1, (await e.CicloAsync()).Fallos);
        var fila = await e.FilaAsync(turno.Id);
        Assert.Equal((true, 1, "Graph respondió 403."), (fila.Pendiente, fila.Intentos, fila.UltimoError));
        Assert.Equal(e.Reloj.GetUtcNow() + TimeSpan.FromSeconds(30), fila.ProximoIntentoEn);

        // Antes de la espera no se reintenta; después, sí, y queda un solo evento.
        Assert.Equal(0, (await e.CicloAsync()).Eventos);
        e.Reloj.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, (await e.CicloAsync()).Eventos);
        Assert.Single(e.Graph.Eventos);

        // Una creación repetida con el mismo transactionId (p. ej. la réplica cayó antes de guardar el id) no duplica.
        await using (var soporte = e.Soporte())
        {
            await soporte.EventosCalendario.Where(x => x.TurnoId == turno.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Pendiente, true).SetProperty(x => x.EventoGraphId, (string?)null), Ct);
        }
        await e.CicloAsync();
        Assert.Single(e.Graph.Eventos);
    }

    [Fact]
    public async Task No_crea_eventos_de_turnos_pasados_ni_de_personas_sin_buzon_y_sin_configuracion_no_hace_nada()
    {
        var e = await EscenarioAsync();
        await using (var plataforma = e.Bd.Plataforma())
        {
            var inactiva = await plataforma.Personas.SingleAsync(p => p.Id == e.Bruno.Id, Ct);
            inactiva.Estado = EstadoPersona.Inactiva;
            await plataforma.SaveChangesAsync(Ct);
        }
        var pasado = await TurnoAsync(e, e.Ana.Id, Sabado.AddDays(-7));
        var deInactiva = await TurnoAsync(e, e.Bruno.Id);

        // Sin buzón (local, dev) el calendario no llama a Graph y lo pendiente espera; sin Entra, tampoco.
        Assert.Equal(0, (await e.CicloAsync(new OpcionesM365Soporte())).Eventos);
        Assert.Equal(0, (await e.CicloAsync(acceso: new SinGraph())).Eventos);
        Assert.Empty(e.Graph.Llamadas);
        Assert.True((await e.FilaAsync(pasado.Id)).Pendiente);

        await e.CicloAsync();
        Assert.Empty(e.Graph.Eventos);
        Assert.False((await e.FilaAsync(pasado.Id)).Pendiente);
        Assert.False((await e.FilaAsync(deInactiva.Id)).Pendiente);
    }

    [Fact]
    public async Task Los_avisos_de_teams_salen_por_la_cola_con_reintentos_y_vencen()
    {
        var e = await EscenarioAsync();
        var opciones = Options.Create(new OpcionesM365Soporte { Buzon = Buzon });
        var cola = new ColaAvisosTeams(e.Teams, opciones, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Atlas:UrlPublica"] = "https://atlas.ejemplo.com" }).Build(), e.Reloj);
        Assert.Equal(new Uri("https://atlas.ejemplo.com/soporte"), cola.Enlace("/soporte"));
        await using (var soporte = e.Soporte())
        {
            Assert.True(cola.Encolar(soporte, TipoAvisoTeams.CambioAprobado, new MensajeTeams("Cambio de turno aprobado", "Pasa de Ana Demo a Bruno Demo.",
                [.. Enumerable.Range(1, 20).Select(i => new HechoTeams($"Dato {i}", "valor"))])));
            await soporte.SaveChangesAsync(Ct);
        }
        e.Teams.Fallas = 1;
        Assert.Equal(1, (await e.CicloAsync()).Fallos);
        Assert.Empty(e.Teams.Enviados);
        e.Reloj.Advance(TimeSpan.FromSeconds(30));
        await e.CicloAsync();
        var enviado = Assert.Single(e.Teams.Enviados);
        Assert.Equal("Cambio de turno aprobado", enviado.Titulo);
        Assert.Equal(15, enviado.Hechos!.Count);

        // Un aviso que no salió en 24 h se descarta; sin canal no se encola.
        await using (var soporte = e.Soporte())
        {
            cola.Encolar(soporte, TipoAvisoTeams.Recordatorio, new MensajeTeams("Recordatorio", "Mañana hay soporte."));
            await soporte.SaveChangesAsync(Ct);
        }
        e.Teams.Fallas = 1;
        await e.CicloAsync();
        e.Reloj.Advance(TimeSpan.FromHours(25));
        await e.CicloAsync();
        Assert.Single(e.Teams.Enviados);
        await using (var soporte = e.Soporte())
        {
            Assert.Equal("vencido", (await soporte.AvisosTeams.SingleAsync(a => a.Tipo == TipoAvisoTeams.Recordatorio, Ct)).UltimoError);
            Assert.False(new ColaAvisosTeams(new TeamsEnMemoria(activo: false), opciones, new ConfigurationBuilder().Build(), e.Reloj)
                .Encolar(soporte, TipoAvisoTeams.Recordatorio, new MensajeTeams("x", "y")));
        }
    }

    [Fact]
    public async Task El_trabajo_revisa_lo_pendiente_cada_intervalo_del_reloj()
    {
        var reloj = new FakeTimeProvider(Inicio);
        using var despertador = new DespertadorM365();
        var espera = despertador.EsperarAsync(TimeSpan.FromSeconds(15), reloj, Ct);
        reloj.Advance(TimeSpan.FromSeconds(14));
        Assert.False(espera.IsCompleted);
        reloj.Advance(TimeSpan.FromSeconds(1));
        await espera.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        // Una señal lo despierta sin esperar el intervalo, y no se pierde si llega antes de esperar.
        despertador.Despertar();
        despertador.Despertar();
        await despertador.EsperarAsync(TimeSpan.FromMinutes(5), reloj, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var otra = despertador.EsperarAsync(TimeSpan.FromSeconds(15), reloj, Ct);
        Assert.False(otra.IsCompleted);
        despertador.Despertar();
        await otra.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public void El_icalendar_cumple_rfc_5545_con_la_zona_de_bogota()
    {
        var id = Guid.Parse("0199a0c0-0000-7000-8000-0000000000aa");
        var ics = CalendarioIcs.Generar("Soporte TI sábados: mis turnos",
            [new EventoIcs(id, Sabado, new(8, 0), new(12, 0), "Soporte Desarrollo TI; equipo, \"demo\"",
                "Turno largo con tildes y eñes para plegar la línea: áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ.\nSegunda línea.", true, Inicio)],
            Inicio);

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("BEGIN:VTIMEZONE\r\nTZID:America/Bogota\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("TZOFFSETTO:-0500", ics, StringComparison.Ordinal);
        Assert.Contains($"UID:soporte-turno-{id:N}@flit-atlas\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("DTSTART;TZID=America/Bogota:20261107T080000\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("DTEND;TZID=America/Bogota:20261107T120000\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("DTSTAMP:20261106T170000Z\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Soporte Desarrollo TI\\; equipo\\, \"demo\"\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("STATUS:CONFIRMED", ics, StringComparison.Ordinal);
        // Ninguna línea pasa de 75 octetos y desplegar devuelve el texto escapado completo.
        Assert.All(ics.Split("\r\n"), l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, l));
        Assert.Contains("DESCRIPTION:Turno largo con tildes y eñes para plegar la línea: áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ áéíóú ñ.\\nSegunda línea.",
            ics.Replace("\r\n ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
