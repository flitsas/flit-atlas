using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Recordatorios;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// HU 2.11: recordatorios de turno por la bandeja, push, correo y Teams, N días antes y el mismo sábado; idempotentes
/// (no salen dos veces), tolerantes a fallos por canal, con las preferencias de cada persona y un solo nodo a la vez.
/// Contra Postgres real con el usuario del servicio y un reloj fijo en Bogotá.
/// </summary>
public class RecordatoriosSoporteTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Jueves 29/10/2026 a las 10:00 de Bogotá; el sábado es el 31/10/2026.</summary>
    private static readonly DateTimeOffset Jueves = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Sabado = new(2026, 10, 31);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class CorreoFalso : IEnviadorCorreo
    {
        public List<MensajeCorreo> Enviados { get; } = [];

        public int FallarVeces { get; set; }

        public bool Habilitado => true;

        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
        {
            if (FallarVeces > 0)
            {
                FallarVeces--;
                throw new CorreoNoEnviadoException("El servidor SMTP de prueba no respondió.");
            }
            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class TeamsFalso : INotificadorTeams
    {
        public List<(Guid Persona, AvisoTeams Aviso)> APersonas { get; } = [];

        public List<AvisoTeams> AlCanal { get; } = [];

        public bool Habilitado => true;

        public Task AvisarPersonaAsync(Guid personaId, AvisoTeams aviso, CancellationToken ct = default)
        {
            APersonas.Add((personaId, aviso));
            return Task.CompletedTask;
        }

        public Task AvisarCanalAsync(AvisoTeams aviso, CancellationToken ct = default)
        {
            AlCanal.Add(aviso);
            return Task.CompletedTask;
        }
    }

    private sealed record Entorno(BaseDePrueba Bd, CorreoFalso Correo, TeamsFalso Teams);

    private async Task<Entorno> EntornoAsync(int? diasAnticipacion = 2)
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var soporte = bd.Soporte();
        soporte.Configuraciones.Add(new ConfiguracionSoporte { DiasAnticipacionRecordatorio = diasAnticipacion });
        await soporte.SaveChangesAsync(Ct);
        return new Entorno(bd, new CorreoFalso(), new TeamsFalso());
    }

    private static async Task<Persona> PersonaAsync(BaseDePrueba bd, string nombre, EstadoPersona estado = EstadoPersona.Activa)
    {
        await using var plataforma = bd.Plataforma();
        var organizacion = await plataforma.Organizaciones.FirstOrDefaultAsync(o => o.Tipo == TipoOrganizacion.Interna, Ct);
        if (organizacion is null)
        {
            organizacion = new Organizacion { Nombre = "FLIT Demo", Tipo = TipoOrganizacion.Interna };
            plataforma.Organizaciones.Add(organizacion);
        }
        var persona = new Persona
        {
            Tipo = TipoPersona.Empleado, OrganizacionId = organizacion.Id, Nombre = nombre, Correo = $"demo.{Guid.NewGuid():N}@ejemplo.com",
            // Ficticio: el correo del recordatorio nunca lo lleva.
            Telefono = "+57 300 000 0000", Estado = estado,
        };
        plataforma.Personas.Add(persona);
        await plataforma.SaveChangesAsync(Ct);
        return persona;
    }

    private static async Task<Turno> TurnoAsync(BaseDePrueba bd, Guid persona, DateOnly fecha, EstadoTurno estado = EstadoTurno.Programado, Guid? equipo = null)
    {
        await using var soporte = bd.Soporte();
        var turno = new Turno
        {
            EquipoId = equipo ?? SoporteDbContext.EquipoDesarrolloTi, Fecha = fecha, PersonaId = persona, Estado = estado, Origen = OrigenTurno.Manual,
        };
        soporte.Turnos.Add(turno);
        await soporte.SaveChangesAsync(Ct);
        return turno;
    }

    private static async Task<ResultadoRecordatorios?> CorrerAsync(Entorno e, DateTimeOffset ahora, DisparoRecordatorios disparo = DisparoRecordatorios.Programada)
    {
        await using var soporte = e.Bd.Soporte();
        await using var plataforma = e.Bd.Plataforma();
        var reloj = new RelojFijo(ahora);
        using var http = new HttpClient();
        var push = new EnviadorPush(http, LlavesVapid.Deshabilitado, reloj, NullLogger<EnviadorPush>.Instance);
        var servicio = new RecordatoriosSoporte(soporte, plataforma, new ServicioNotificaciones(plataforma, push), LlavesVapid.Deshabilitado,
            e.Correo, e.Teams, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Atlas:UrlPublica"] = "https://atlas.ejemplo.com" }).Build(),
            reloj, NullLogger<RecordatoriosSoporte>.Instance);
        return await servicio.CorrerAsync(disparo, null, Ct);
    }

    private static async Task<List<Notificacion>> BandejaAsync(BaseDePrueba bd, Guid persona)
    {
        await using var plataforma = bd.Plataforma();
        return await plataforma.Notificaciones.Where(n => n.PersonaId == persona && n.Tipo == RecordatoriosSoporte.Tipo.Codigo).ToListAsync(Ct);
    }

    [Fact]
    public async Task N_dias_antes_sale_por_cada_canal_una_sola_vez_y_lo_fallido_se_reintenta()
    {
        var e = await EntornoAsync(diasAnticipacion: 2);
        var ana = await PersonaAsync(e.Bd, "Ana Demo Pruebas");
        var luis = await PersonaAsync(e.Bd, "Luis Demo Pruebas");
        var inactiva = await PersonaAsync(e.Bd, "Persona Inactiva Demo", EstadoPersona.Inactiva);
        var turno = await TurnoAsync(e.Bd, ana.Id, Sabado);
        await TurnoAsync(e.Bd, luis.Id, Sabado.AddDays(7)); // fuera de la anticipación
        await TurnoAsync(e.Bd, luis.Id, Sabado, EstadoTurno.Cancelado, SoporteDbContext.EquipoOperaciones);
        await TurnoAsync(e.Bd, inactiva.Id, Sabado, equipo: SoporteDbContext.EquipoOperaciones);
        e.Correo.FallarVeces = 1;

        var primera = await CorrerAsync(e, Jueves);

        // Bandeja y Teams salen; el correo falla sin detener los demás; push no tiene llaves ni dispositivos.
        Assert.NotNull(primera);
        Assert.Equal((2, 1, 1), (primera.Enviados, primera.Fallidos, primera.Omitidos));
        var aviso = Assert.Single(await BandejaAsync(e.Bd, ana.Id));
        Assert.Equal("Tu turno de soporte es el sábado 31/10/2026", aviso.Titulo);
        Assert.Contains("Desarrollo TI en 2 días", aviso.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("de 08:00 a 12:00", aviso.Cuerpo, StringComparison.Ordinal);
        Assert.Equal("/soporte", aviso.Enlace);
        Assert.Equal(ana.Id, Assert.Single(e.Teams.APersonas).Persona);
        Assert.Empty(e.Teams.AlCanal);
        Assert.Empty(e.Correo.Enviados);
        Assert.Empty(await BandejaAsync(e.Bd, luis.Id));
        Assert.Empty(await BandejaAsync(e.Bd, inactiva.Id));

        // La corrida programada de hoy ya se hizo: otra réplica (o un reinicio) no la repite.
        Assert.Null(await CorrerAsync(e, Jueves.AddHours(3)));

        // «Enviar ahora» solo manda lo que falta: el correo que falló, sin repetir la bandeja ni Teams.
        var manual = await CorrerAsync(e, Jueves.AddHours(4), DisparoRecordatorios.Manual);
        Assert.Equal((1, 0), (manual!.Enviados, manual.Fallidos));
        Assert.Single(await BandejaAsync(e.Bd, ana.Id));
        Assert.Single(e.Teams.APersonas);
        var correo = Assert.Single(e.Correo.Enviados);
        Assert.Equal(ana.Correo, correo.Para);
        Assert.Equal("Tu turno de soporte es el sábado 31/10/2026", correo.Asunto);
        Assert.Contains("Hola, Ana.", correo.Texto, StringComparison.Ordinal);
        Assert.Contains("https://atlas.ejemplo.com/soporte", correo.Html, StringComparison.Ordinal);
        Assert.Contains("Flit Atlas", correo.Html, StringComparison.Ordinal);
        // Sin datos personales de más: ni el teléfono ni otras personas del soporte.
        Assert.DoesNotContain("300 000", correo.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Luis", correo.Texto, StringComparison.Ordinal);

        // Al día siguiente no se repite nada de lo ya enviado.
        var viernes = await CorrerAsync(e, Jueves.AddDays(1));
        Assert.Equal(0, viernes!.Enviados);
        Assert.Single(e.Correo.Enviados);

        await using var soporte = e.Bd.Soporte();
        var registros = await soporte.Recordatorios.Where(r => r.TurnoId == turno.Id).ToListAsync(Ct);
        Assert.Equal(
            [CanalRecordatorio.App, CanalRecordatorio.Correo, CanalRecordatorio.Teams],
            registros.Select(r => r.Canal).Order());
        Assert.All(registros, r => Assert.Equal(EstadoRecordatorio.Enviado, r.Estado));
        Assert.Equal(2, registros.Single(r => r.Canal == CanalRecordatorio.Correo).Intentos);
    }

    [Fact]
    public async Task Respeta_las_preferencias_de_la_persona_por_canal()
    {
        var e = await EntornoAsync();
        var ana = await PersonaAsync(e.Bd, "Ana Demo Preferencias");
        await TurnoAsync(e.Bd, ana.Id, Sabado);
        await using (var plataforma = e.Bd.Plataforma())
        {
            plataforma.PreferenciasNotificacion.AddRange(
                new PreferenciaNotificacion { PersonaId = ana.Id, Tipo = RecordatoriosSoporte.Tipo.Codigo, Canal = CanalNotificacion.Correo, Activa = false },
                new PreferenciaNotificacion { PersonaId = ana.Id, Tipo = RecordatoriosSoporte.Tipo.Codigo, Canal = CanalNotificacion.Teams, Activa = false });
            await plataforma.SaveChangesAsync(Ct);
        }

        var resultado = await CorrerAsync(e, Jueves);

        // Solo la bandeja (no se puede apagar).
        Assert.Equal((1, 0), (resultado!.Enviados, resultado.Fallidos));
        Assert.Single(await BandejaAsync(e.Bd, ana.Id));
        Assert.Empty(e.Correo.Enviados);
        Assert.Empty(e.Teams.APersonas);
    }

    [Fact]
    public async Task El_mismo_sabado_avisa_a_cada_persona_de_turno_y_al_canal_del_equipo()
    {
        var e = await EntornoAsync(diasAnticipacion: null);
        var ana = await PersonaAsync(e.Bd, "Ana Demo Sabado");
        var luis = await PersonaAsync(e.Bd, "Luis Demo Sabado");
        await TurnoAsync(e.Bd, ana.Id, Sabado, EstadoTurno.Confirmado, SoporteDbContext.EquipoOperaciones);
        await TurnoAsync(e.Bd, luis.Id, Sabado, equipo: SoporteDbContext.EquipoOperaciones);
        // Sin anticipación configurada, el jueves no sale nada.
        Assert.Equal(0, (await CorrerAsync(e, Jueves))!.Enviados);

        var resultado = await CorrerAsync(e, new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

        // Dos personas × (bandeja, correo, Teams) + el canal del equipo.
        Assert.Equal(7, resultado!.Enviados);
        var aviso = Assert.Single(await BandejaAsync(e.Bd, ana.Id));
        Assert.Equal("Hoy tienes soporte de Operaciones", aviso.Titulo);
        var canal = Assert.Single(e.Teams.AlCanal);
        Assert.Equal("Soporte de hoy: Operaciones", canal.Titulo);
        Assert.Contains("Ana Demo Sabado", canal.Texto, StringComparison.Ordinal);
        Assert.Contains("Luis Demo Sabado", canal.Texto, StringComparison.Ordinal);
        Assert.Equal(2, e.Correo.Enviados.Count);
    }

    [Fact]
    public async Task Con_el_candado_en_otro_nodo_no_corre()
    {
        var e = await EntornoAsync();
        var ana = await PersonaAsync(e.Bd, "Ana Demo Candado");
        await TurnoAsync(e.Bd, ana.Id, Sabado);
        await using var otroNodo = new NpgsqlConnection(e.Bd.Servicio);
        await otroNodo.OpenAsync(Ct);
        await using (var tomar = new NpgsqlCommand($"SELECT pg_advisory_lock({RecordatoriosSoporte.Candado})", otroNodo))
        {
            await tomar.ExecuteNonQueryAsync(Ct);
        }

        Assert.Null(await CorrerAsync(e, Jueves));
        Assert.Empty(await BandejaAsync(e.Bd, ana.Id));

        await using (var soltar = new NpgsqlCommand($"SELECT pg_advisory_unlock({RecordatoriosSoporte.Candado})", otroNodo))
        {
            await soltar.ExecuteNonQueryAsync(Ct);
        }
        Assert.Equal(3, (await CorrerAsync(e, Jueves))!.Enviados);
    }
}
