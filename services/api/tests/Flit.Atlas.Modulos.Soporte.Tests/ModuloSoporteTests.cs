using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// El módulo cumple el contrato de módulo (ATLAS-ADR-0026) y su modelo de datos (ATLAS-ADR-0029), probado con el usuario
/// del servicio <c>flit_atlas</c>: tablas <c>soporte_*</c>, llaves hacia <c>plataforma_persona</c>, un solo turno vigente
/// por sábado, una sola configuración y las columnas con datos personales marcadas.
/// </summary>
public class ModuloSoporteTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_usa_los_permisos_base_del_nucleo()
    {
        var modulo = new ModuloSoporte();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("soporte", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(SoporteDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_soporte", SoporteDbContext.TablaHistorial);
        Assert.Equal(
            [PermisosSoporte.Ver, PermisosSoporte.SolicitarCambio, PermisosSoporte.Aprobar, PermisosSoporte.Gestionar, PermisosSoporte.VerCanal],
            modulo.Permisos.Permisos.Select(p => p.Codigo));
        Assert.Equal(ManifiestosNucleo.SoporteSabados.Permisos.Select(p => p.Codigo), modulo.Permisos.Permisos.Take(4).Select(p => p.Codigo));
        // El núcleo ya no registra «soporte»: si lo hiciera, el catálogo fallaría por el módulo repetido.
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, modulo.Permisos]);
        // Solo los externos ven el canal; ningún externo ve el calendario.
        Assert.All(RolesAtlas.Externos, r =>
        {
            Assert.Contains(PermisosSoporte.VerCanal, catalogo.PermisosDe(r));
            Assert.DoesNotContain(PermisosSoporte.Ver, catalogo.PermisosDe(r));
        });
        Assert.Contains(PermisosSoporte.Ver, catalogo.PermisosDe(RolesAtlas.Lector));
        Assert.Contains(PermisosSoporte.Aprobar, catalogo.PermisosDe(RolesAtlas.LiderTecnico));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Soporte();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Soporte -o Datos/Migraciones");
    }

    [Fact]
    public async Task Se_migra_despues_del_nucleo_con_el_usuario_del_servicio_y_repetirlo_no_cambia_nada()
    {
        var bd = await contenedores.CrearBaseAsync(migrar: false);
        await using (var plataforma = bd.Plataforma())
        {
            await plataforma.Database.MigrateAsync(Ct);
        }
        var delNucleo = await Tablas(bd);

        await using var soporte = bd.Soporte();
        await soporte.Database.MigrateAsync(Ct);
        await soporte.Database.MigrateAsync(Ct);

        Assert.Empty(await soporte.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(
            ["__ef_migraciones_soporte", "soporte_ausencia", "soporte_aviso_teams", "soporte_compensacion", "soporte_configuracion", "soporte_equipo",
                "soporte_evento_calendario", "soporte_feed_ics", "soporte_festivo", "soporte_participante", "soporte_recordatorio",
                "soporte_recordatorio_corrida", "soporte_solicitud_cambio", "soporte_solicitud_evento", "soporte_turno"],
            (await Tablas(bd)).Except(delNucleo).Order(StringComparer.Ordinal));
    }

    private static Task<List<string>> Tablas(BaseDePrueba bd) =>
        Contenedores.ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");

    [Fact]
    public async Task Las_columnas_con_datos_personales_estan_marcadas()
    {
        var bd = await contenedores.CrearBaseAsync();

        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || '.' || a.attname FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            WHERE c.relname LIKE 'soporte_%' AND col_description(a.attrelid, a.attnum) = '@pii'
            """);

        Assert.Equal(
            ["soporte_ausencia.tipo", "soporte_aviso_teams.contenido", "soporte_compensacion.nota", "soporte_configuracion.canal_externo", "soporte_solicitud_cambio.motivo",
                "soporte_solicitud_evento.nota", "soporte_turno.observacion"],
            pii.Order(StringComparer.Ordinal));
    }

    private static async Task<Persona> PersonaAsync(BaseDePrueba bd)
    {
        await using var plataforma = bd.Plataforma();
        var organizacion = new Organizacion { Nombre = "FLIT Demo", Tipo = TipoOrganizacion.Interna };
        var persona = new Persona
        {
            Tipo = TipoPersona.Empleado, OrganizacionId = organizacion.Id, Nombre = "Persona Demo", Correo = $"demo.{Guid.NewGuid():N}@ejemplo.com",
        };
        plataforma.AddRange(organizacion, persona);
        await plataforma.SaveChangesAsync(Ct);
        return persona;
    }

    private static readonly Guid Desarrollo = SoporteDbContext.EquipoDesarrolloTi;
    private static readonly Guid Operaciones = SoporteDbContext.EquipoOperaciones;

    [Fact]
    public async Task La_migracion_crea_los_equipos_de_desarrollo_ti_y_operaciones()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var soporte = bd.Soporte();

        var equipos = await soporte.Equipos.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(Ct);

        Assert.Equal(["Desarrollo TI", "Operaciones"], equipos.Select(e => e.Nombre));
        Assert.Equal((1, 1), (equipos[0].PersonasMinimo, equipos[0].PersonasMaximo));
        Assert.Equal((1, 2), (equipos[1].PersonasMinimo, equipos[1].PersonasMaximo));
        // Sin horario propio: heredan el global (08:00 a 12:00).
        Assert.All(equipos, e => Assert.Null(e.HoraInicio));
    }

    [Fact]
    public async Task Un_participante_o_un_turno_no_puede_apuntar_a_una_persona_que_no_existe()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var soporte = bd.Soporte();

        soporte.Participantes.Add(new Participante { EquipoId = Desarrollo, PersonaId = Guid.NewGuid(), Orden = 1 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);

        soporte.ChangeTracker.Clear();
        soporte.Turnos.Add(new Turno { EquipoId = Desarrollo, Fecha = new DateOnly(2026, 10, 24), PersonaId = Guid.NewGuid(), Origen = OrigenTurno.Manual });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Una_persona_tiene_a_lo_sumo_un_turno_vigente_por_equipo_y_sabado_pero_puede_estar_en_dos_equipos()
    {
        var bd = await contenedores.CrearBaseAsync();
        var persona = await PersonaAsync(bd);
        var sabado = new DateOnly(2026, 10, 24);
        await using var soporte = bd.Soporte();
        soporte.Turnos.Add(new Turno { EquipoId = Desarrollo, Fecha = sabado, PersonaId = persona.Id, Origen = OrigenTurno.Manual, Estado = EstadoTurno.Reemplazado });
        soporte.Turnos.Add(new Turno { EquipoId = Desarrollo, Fecha = sabado, PersonaId = persona.Id, Origen = OrigenTurno.Manual, Estado = EstadoTurno.Cancelado });
        soporte.Turnos.Add(new Turno { EquipoId = Desarrollo, Fecha = sabado, PersonaId = persona.Id, Origen = OrigenTurno.Manual });
        soporte.Turnos.Add(new Turno { EquipoId = Operaciones, Fecha = sabado, PersonaId = persona.Id, Origen = OrigenTurno.Manual });
        await soporte.SaveChangesAsync(Ct);

        soporte.Turnos.Add(new Turno { EquipoId = Desarrollo, Fecha = sabado, PersonaId = persona.Id, Origen = OrigenTurno.Generado, Estado = EstadoTurno.Confirmado });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        var estados = await Contenedores.ConsultarAsync(bd.Servicio, "SELECT estado FROM atlas.soporte_turno ORDER BY estado");
        Assert.Equal(["cancelado", "programado", "programado", "reemplazado"], estados);
    }

    [Fact]
    public async Task Solo_hay_una_configuracion_y_arranca_con_el_horario_y_la_regla_por_defecto()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var soporte = bd.Soporte();
        soporte.Configuraciones.Add(new ConfiguracionSoporte());
        await soporte.SaveChangesAsync(Ct);

        var guardada = await soporte.Configuraciones.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(new TimeOnly(8, 0), guardada.HoraInicioPorDefecto);
        Assert.Equal(new TimeOnly(12, 0), guardada.HoraFinPorDefecto);
        Assert.Equal(ReglaFestivos.SinTurno, guardada.ReglaFestivos);
        Assert.Equal(["sin_turno"], await Contenedores.ConsultarAsync(bd.Servicio, "SELECT regla_festivos FROM atlas.soporte_configuracion"));

        soporte.Configuraciones.Add(new ConfiguracionSoporte());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task El_horario_y_las_personas_por_sabado_deben_ser_coherentes()
    {
        var bd = await contenedores.CrearBaseAsync();
        var persona = await PersonaAsync(bd);
        await using var soporte = bd.Soporte();

        soporte.Turnos.Add(new Turno
        {
            EquipoId = Desarrollo, Fecha = new DateOnly(2026, 10, 31), PersonaId = persona.Id, Origen = OrigenTurno.Manual,
            HoraInicio = new TimeOnly(12, 0), HoraFin = new TimeOnly(8, 0),
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);

        soporte.ChangeTracker.Clear();
        soporte.Equipos.Add(new EquipoSoporte { Nombre = "Equipo Demo", PersonasMinimo = 3, PersonasMaximo = 2 });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => soporte.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
