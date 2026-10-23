using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Soporte.Datos;

/// <summary>
/// Configuración global del módulo (una sola fila). Los valores por defecto son los que definió el líder técnico el
/// 23/10/2026: turno de 08:00 a 12:00 (Bogotá) y sin turno en los sábados festivos. Todo se puede cambiar.
/// </summary>
public sealed class ConfiguracionSoporte : Entidad
{
    public static readonly TimeOnly InicioPorDefecto = new(8, 0);
    public static readonly TimeOnly FinPorDefecto = new(12, 0);
    public const ReglaFestivos ReglaPorDefecto = Dominio.ReglaFestivos.SinTurno;

    /// <summary>Siempre <c>true</c>: con el índice único garantiza una sola fila.</summary>
    public bool Unica { get; init; } = true;

    /// <summary>Horario por defecto de los turnos (Bogotá); cada equipo lo puede cambiar y cada turno, también.</summary>
    public TimeOnly HoraInicioPorDefecto { get; set; } = InicioPorDefecto;

    public TimeOnly HoraFinPorDefecto { get; set; } = FinPorDefecto;

    /// <summary>Qué hace la generación con un sábado festivo de Colombia.</summary>
    public ReglaFestivos ReglaFestivos { get; set; } = ReglaPorDefecto;

    /// <summary>Días antes del turno en que salen los recordatorios (HU 2.11); vacío mientras no se defina.</summary>
    public int? DiasAnticipacionRecordatorio { get; set; }

    /// <summary>
    /// Canal de soporte del sábado que ven los externos (un correo, un número o un canal, sin nombres de personas).
    /// Vacío: los externos no ven nada (prompt maestro §8.3).
    /// </summary>
    public string? CanalExterno { get; set; }

    public Guid? ActualizadoPorPersonaId { get; set; }
}

/// <summary>
/// Equipo de soporte de los sábados de un área (p. ej. «Desarrollo TI», una persona por sábado, u «Operaciones», una o
/// dos). Cada equipo tiene sus participantes, su orden, su horario (si no, el global) y su generación equitativa.
/// Quién lo gestiona o aprueba sale de los permisos con alcance <c>equipo_soporte:&lt;id&gt;</c> (ATLAS-ADR-0023).
/// </summary>
public sealed class EquipoSoporte : Entidad
{
    public required string Nombre { get; set; }

    public string? Area { get; set; }

    /// <summary>Mínimo de personas por sábado: con menos disponibles, la generación lo avisa.</summary>
    public int PersonasMinimo { get; set; } = 1;

    /// <summary>Máximo de personas por sábado: la generación asigna hasta este número.</summary>
    public int PersonasMaximo { get; set; } = 1;

    /// <summary>Horario propio del equipo; vacío, el de la configuración global.</summary>
    public TimeOnly? HoraInicio { get; set; }

    public TimeOnly? HoraFin { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>Persona interna en la rotación de un equipo, con su puesto en el orden y su estado.</summary>
public sealed class Participante : Entidad
{
    public required Guid EquipoId { get; init; }

    /// <summary>Id de <c>plataforma_persona</c> (llave foránea en la migración; el contexto no mapea el núcleo).</summary>
    public required Guid PersonaId { get; init; }

    /// <summary>Puesto en la rotación del equipo, desde 1.</summary>
    public int Orden { get; set; }

    /// <summary>Solo los activos entran en la generación; un inactivo conserva su historial.</summary>
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Turno de una persona de un equipo en un sábado, con su horario propio, su estado y su origen. El soporte del equipo
/// ese sábado son sus turnos vigentes (de 1 a <see cref="EquipoSoporte.PersonasMaximo"/> personas).
/// </summary>
public sealed class Turno : Entidad
{
    public required Guid EquipoId { get; init; }

    public required DateOnly Fecha { get; init; }

    /// <summary>Persona de turno (<c>plataforma_persona</c>).</summary>
    public required Guid PersonaId { get; set; }

    /// <summary>Horario de este turno (Bogotá); se copia del equipo o del global al crearlo y se puede cambiar.</summary>
    public TimeOnly? HoraInicio { get; set; }

    public TimeOnly? HoraFin { get; set; }

    public EstadoTurno Estado { get; set; } = EstadoTurno.Programado;

    public required OrigenTurno Origen { get; init; }

    /// <summary>Turno al que este reemplaza (el original queda <c>reemplazado</c>).</summary>
    public Guid? TurnoReemplazadoId { get; init; }

    /// <summary>Generación confirmada que lo creó o lo cambió (agrupa los turnos publicados juntos).</summary>
    public Guid? GeneracionId { get; set; }

    /// <summary>Nota libre: puede traer datos personales.</summary>
    public string? Observacion { get; set; }

    /// <summary>Marcado para revisión humana (lo usa el importador del Excel, HU 2.7, con lo ambiguo).</summary>
    public bool RequiereRevision { get; set; }

    public Guid? CreadoPorPersonaId { get; init; }
}

/// <summary>Vacaciones, incapacidad u otra ausencia de una persona (vale en todos sus equipos).</summary>
public sealed class Ausencia : Entidad
{
    public required Guid PersonaId { get; init; }

    public required DateOnly Desde { get; set; }

    /// <summary>Último día de la ausencia (incluido).</summary>
    public required DateOnly Hasta { get; set; }

    /// <summary>Motivo: dato sensible (una incapacidad es de salud); solo lo ve quien gestiona el soporte.</summary>
    public required TipoAusencia Tipo { get; set; }

    public Guid? RegistradaPorPersonaId { get; init; }
}

/// <summary>
/// Día no laborable adicional (p. ej. un día cívico decretado). Los festivos de ley no se guardan: los calcula
/// <see cref="FestivosColombia"/>.
/// </summary>
public sealed class FestivoAdicional : Entidad
{
    public required DateOnly Fecha { get; init; }

    public required string Nombre { get; set; }
}

/// <summary>
/// Día compensado por un turno (prompt maestro §8.3): a quién, qué día, quién la aprobó y si ya la tomó. La registra quien
/// tiene <c>atlas.soporte.aprobar</c> sobre el equipo del turno (HU 2.9) o el importador del Excel (HU 2.7). A lo sumo
/// una por turno y persona.
/// </summary>
public sealed class Compensacion : Entidad
{
    public required Guid TurnoId { get; init; }

    /// <summary>A quién se le compensa (<c>plataforma_persona</c>).</summary>
    public required Guid PersonaId { get; init; }

    /// <summary>Día que toma (vacío mientras no lo elija).</summary>
    public DateOnly? FechaCompensada { get; set; }

    public EstadoCompensacion Estado { get; set; } = EstadoCompensacion.Pendiente;

    public Guid? AprobadaPorPersonaId { get; set; }

    public string? Nota { get; set; }
}

/// <summary>
/// Solicitud de cambio de turno (HU 2.9, prompt maestro §8.3): la persona de turno la pide y propone un reemplazo del
/// mismo equipo → el reemplazo acepta (o rechaza) → alguien con <c>atlas.soporte.aprobar</c> sobre el equipo aprueba (o
/// rechaza). Al aprobar, el turno original queda <c>reemplazado</c> y se crea el del reemplazo. Cada paso queda en
/// <see cref="EventoSolicitudCambio"/>.
/// </summary>
public sealed class SolicitudCambio : Entidad
{
    public required Guid TurnoId { get; init; }

    /// <summary>Equipo del turno (copiado para filtrar por alcance sin unir con los turnos).</summary>
    public required Guid EquipoId { get; init; }

    public required DateOnly Fecha { get; init; }

    /// <summary>Persona de turno que pide el cambio (<c>plataforma_persona</c>).</summary>
    public required Guid SolicitantePersonaId { get; init; }

    /// <summary>Persona del mismo equipo que tomaría el turno.</summary>
    public required Guid ReemplazoPersonaId { get; init; }

    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.PendienteReemplazo;

    /// <summary>Motivo que escribe quien solicita: puede traer datos personales.</summary>
    public string? Motivo { get; init; }

    /// <summary>Turno creado para el reemplazo al aprobar.</summary>
    public Guid? TurnoNuevoId { get; set; }

    /// <summary>Quien aprobó o rechazó (con <c>atlas.soporte.aprobar</c>).</summary>
    public Guid? ResueltaPorPersonaId { get; set; }

    public DateTimeOffset? ResueltaEn { get; set; }
}

/// <summary>
/// Un paso de una solicitud de cambio (historial completo: quién, cuándo, qué y su nota). La llave de idempotencia la
/// manda el cliente con cada acción: si una acción hecha sin conexión se reenvía, el servidor la reconoce y no la repite
/// (ATLAS-ADR-0032).
/// </summary>
public sealed class EventoSolicitudCambio : Entidad
{
    public required Guid SolicitudId { get; init; }

    public required AccionSolicitud Accion { get; init; }

    public required Guid PersonaId { get; init; }

    /// <summary>Nota de quien actúa: puede traer datos personales.</summary>
    public string? Nota { get; init; }

    /// <summary>Única: la acción con esta llave ya se hizo.</summary>
    public required Guid LlaveIdempotencia { get; init; }

    /// <summary>Cuándo la hizo la persona en su dispositivo (puede ser antes de enviarla, si estaba sin conexión).</summary>
    public DateTimeOffset? HechaEn { get; init; }
}

/// <summary>
/// Tablas <c>soporte_*</c> del módulo Soporte de los sábados, con su historial <c>__ef_migraciones_soporte</c>
/// (ATLAS-ADR-0026 y ATLAS-ADR-0029). Solo mapea sus tablas: las personas se leen de <c>PlataformaDbContext</c> y las
/// llaves foráneas hacia <c>plataforma_persona</c> se crean a mano en la migración.
/// <para>
/// Ninguna tabla lleva <c>organizacion_id</c>: la rotación es de FLIT (empleados) y ningún externo ve sus filas; los
/// endpoints lo hacen cumplir con permisos que solo tienen los internos (§7.3).
/// </para>
/// </summary>
/// <param name="alcance">Alcance de datos de la petición (lo registra la identidad); sin él, el del sistema.</param>
/// <param name="reloj">Reloj de la aplicación para <c>creado_en</c> y <c>actualizado_en</c>.</param>
public sealed class SoporteDbContext(DbContextOptions<SoporteDbContext> opciones, IAlcanceDatos? alcance = null, TimeProvider? reloj = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloSoporte.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloSoporte.IdModulo);

    public const int LargoObservacion = 500;
    public const int LargoNombreFestivo = 100;
    public const int LargoCanal = 200;
    public const int LargoNombreEquipo = 100;
    public const int PersonasMaximasPorTurno = 10;

    /// <summary>Equipos que crea la migración inicial con lo que dijo el líder (se pueden renombrar o desactivar).</summary>
    public static readonly Guid EquipoDesarrolloTi = new("0199a0c0-0000-7000-8000-000000000001");
    public static readonly Guid EquipoOperaciones = new("0199a0c0-0000-7000-8000-000000000002");

    /// <summary>Comentario de las columnas con datos personales (prompt maestro §9).</summary>
    public const string Pii = PlataformaDbContext.Pii;

    public DbSet<ConfiguracionSoporte> Configuraciones => Set<ConfiguracionSoporte>();
    public DbSet<EquipoSoporte> Equipos => Set<EquipoSoporte>();
    public DbSet<Participante> Participantes => Set<Participante>();
    public DbSet<Turno> Turnos => Set<Turno>();
    public DbSet<Ausencia> Ausencias => Set<Ausencia>();
    public DbSet<FestivoAdicional> FestivosAdicionales => Set<FestivoAdicional>();
    public DbSet<Compensacion> Compensaciones => Set<Compensacion>();
    public DbSet<SolicitudCambio> SolicitudesCambio => Set<SolicitudCambio>();
    public DbSet<EventoSolicitudCambio> EventosSolicitud => Set<EventoSolicitudCambio>();

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Reloj = reloj ?? TimeProvider.System;
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<ConfiguracionSoporte>(e =>
        {
            e.ToTable(Prefijo + "configuracion", t =>
            {
                t.HasComment("Configuración global del soporte de los sábados (una fila).");
                t.HasCheckConstraint("ck_soporte_configuracion_unica", "unica");
                t.HasCheckConstraint("ck_soporte_configuracion_horario", "hora_fin_por_defecto > hora_inicio_por_defecto");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasIndex(c => c.Unica).IsUnique();
            Enum(e.Property(c => c.ReglaFestivos));
            e.Property(c => c.CanalExterno).HasMaxLength(LargoCanal).HasComment(Pii);
        });

        modelBuilder.Entity<EquipoSoporte>(e =>
        {
            e.ToTable(Prefijo + "equipo", t =>
            {
                t.HasComment("Equipos de soporte de los sábados por área, con cuántas personas van por sábado.");
                t.HasCheckConstraint("ck_soporte_equipo_personas", $"personas_minimo >= 1 AND personas_maximo >= personas_minimo AND personas_maximo <= {PersonasMaximasPorTurno}");
                t.HasCheckConstraint("ck_soporte_equipo_horario", "(hora_inicio IS NULL AND hora_fin IS NULL) OR hora_fin > hora_inicio");
            });
            e.Property(q => q.Id).ValueGeneratedNever();
            e.Property(q => q.Nombre).HasMaxLength(LargoNombreEquipo);
            e.Property(q => q.Area).HasMaxLength(LargoNombreEquipo);
            e.HasIndex(q => q.Nombre).IsUnique();
            e.HasData(
                new { Id = EquipoDesarrolloTi, Nombre = "Desarrollo TI", Area = "TI", PersonasMinimo = 1, PersonasMaximo = 1, Activo = true, CreadoEn = Semilla, ActualizadoEn = Semilla },
                new { Id = EquipoOperaciones, Nombre = "Operaciones", Area = "Operaciones", PersonasMinimo = 1, PersonasMaximo = 2, Activo = true, CreadoEn = Semilla, ActualizadoEn = Semilla });
        });

        modelBuilder.Entity<Participante>(e =>
        {
            e.ToTable(Prefijo + "participante", t => t.HasComment("Personas internas de la rotación de cada equipo, con su orden y estado."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.HasIndex(p => new { p.EquipoId, p.PersonaId }).IsUnique();
            e.HasOne<EquipoSoporte>().WithMany().HasForeignKey(p => p.EquipoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Turno>(e =>
        {
            e.ToTable(Prefijo + "turno", t =>
            {
                t.HasComment("Turnos de soporte de los sábados: una persona de un equipo en una fecha.");
                t.HasCheckConstraint("ck_soporte_turno_horario", "hora_fin IS NULL OR hora_inicio IS NULL OR hora_fin > hora_inicio");
            });
            e.Property(t => t.Id).ValueGeneratedNever();
            Enum(e.Property(t => t.Estado));
            Enum(e.Property(t => t.Origen));
            e.Property(t => t.Observacion).HasMaxLength(LargoObservacion).HasComment(Pii);
            // Una persona tiene a lo sumo un turno vigente por equipo y sábado; los reemplazados y cancelados quedan como
            // historia. El máximo de personas del equipo lo controla la aplicación.
            e.HasIndex(t => new { t.EquipoId, t.Fecha, t.PersonaId }).IsUnique().HasFilter("estado IN ('programado', 'confirmado', 'aplicado')")
                .HasDatabaseName("ux_soporte_turno_vigente");
            e.HasIndex(t => new { t.Fecha, t.EquipoId });
            e.HasIndex(t => new { t.PersonaId, t.Fecha });
            e.HasOne<EquipoSoporte>().WithMany().HasForeignKey(t => t.EquipoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Turno>().WithMany().HasForeignKey(t => t.TurnoReemplazadoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Ausencia>(e =>
        {
            e.ToTable(Prefijo + "ausencia", t =>
            {
                t.HasComment("Ausencias por persona y rango (vacaciones, incapacidades); la generación las salta.");
                t.HasCheckConstraint("ck_soporte_ausencia_rango", "hasta >= desde");
            });
            e.Property(a => a.Id).ValueGeneratedNever();
            Enum(e.Property(a => a.Tipo)).HasComment(Pii);
            e.HasIndex(a => new { a.PersonaId, a.Desde });
        });

        modelBuilder.Entity<FestivoAdicional>(e =>
        {
            e.ToTable(Prefijo + "festivo", t => t.HasComment("Días no laborables adicionales; los festivos de ley se calculan (Ley 51 de 1983 y Ley 2578 de 2026)."));
            e.Property(f => f.Id).ValueGeneratedNever();
            e.Property(f => f.Nombre).HasMaxLength(LargoNombreFestivo);
            e.HasIndex(f => f.Fecha).IsUnique();
        });

        modelBuilder.Entity<Compensacion>(e =>
        {
            e.ToTable(Prefijo + "compensacion", t =>
            {
                t.HasComment("Días compensados por turnos: a quién, qué día, quién aprobó y si ya se tomó (HU 2.9).");
                t.HasCheckConstraint("ck_soporte_compensacion_tomada", "estado <> 'tomada' OR fecha_compensada IS NOT NULL");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            Enum(e.Property(c => c.Estado));
            e.Property(c => c.Nota).HasMaxLength(LargoObservacion).HasComment(Pii);
            e.HasOne<Turno>().WithMany().HasForeignKey(c => c.TurnoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.PersonaId, c.Estado });
            // Una compensación por turno y persona: reimportar el Excel o repetir la acción no la duplica.
            e.HasIndex(c => new { c.TurnoId, c.PersonaId }).IsUnique();
        });

        modelBuilder.Entity<SolicitudCambio>(e =>
        {
            e.ToTable(Prefijo + "solicitud_cambio", t =>
            {
                t.HasComment("Solicitudes de cambio de turno: solicitud, aceptación del reemplazo y aprobación (HU 2.9).");
                t.HasCheckConstraint("ck_soporte_solicitud_cambio_personas", "solicitante_persona_id <> reemplazo_persona_id");
            });
            e.Property(s => s.Id).ValueGeneratedNever();
            Enum(e.Property(s => s.Estado));
            e.Property(s => s.Motivo).HasMaxLength(LargoObservacion).HasComment(Pii);
            e.HasOne<Turno>().WithMany().HasForeignKey(s => s.TurnoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Turno>().WithMany().HasForeignKey(s => s.TurnoNuevoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EquipoSoporte>().WithMany().HasForeignKey(s => s.EquipoId).OnDelete(DeleteBehavior.Restrict);
            // A lo sumo una solicitud abierta por turno.
            e.HasIndex(s => s.TurnoId).IsUnique().HasFilter("estado IN ('pendiente_reemplazo', 'pendiente_aprobacion')")
                .HasDatabaseName("ux_soporte_solicitud_cambio_abierta");
            e.HasIndex(s => new { s.EquipoId, s.Estado });
            e.HasIndex(s => s.SolicitantePersonaId);
            e.HasIndex(s => s.ReemplazoPersonaId);
        });

        modelBuilder.Entity<EventoSolicitudCambio>(e =>
        {
            e.ToTable(Prefijo + "solicitud_evento", t => t.HasComment("Historial de cada solicitud de cambio, con la llave de idempotencia de cada acción."));
            e.Property(v => v.Id).ValueGeneratedNever();
            Enum(e.Property(v => v.Accion));
            e.Property(v => v.Nota).HasMaxLength(LargoObservacion).HasComment(Pii);
            e.HasOne<SolicitudCambio>().WithMany().HasForeignKey(v => v.SolicitudId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => v.LlaveIdempotencia).IsUnique().HasDatabaseName("ux_soporte_solicitud_evento_llave");
            e.HasIndex(v => v.SolicitudId);
        });

        // Ninguna entidad tiene OrganizacionId: el predicado no agrega filtros, pero se llama como en todo módulo para que
        // una tabla futura con organización quede filtrada sin acordarse.
        AplicarPredicadoDeOrganizacion(modelBuilder);
    }

    private static readonly DateTimeOffset Semilla = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Enum como texto snake_case, como el núcleo (<see cref="ConvertidorEnumSnake{TEnum}"/>).</summary>
    private static PropertyBuilder<TEnum> Enum<TEnum>(PropertyBuilder<TEnum> propiedad) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(LargoEnum<TEnum>());

    private static int LargoEnum<TEnum>() where TEnum : struct, System.Enum => System.Enum.GetValues<TEnum>().Max(v => ReglasTurno.Texto(v).Length);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>: generar una migración no necesita una base real.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Soporte -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<SoporteDbContext>
{
    public SoporteDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<SoporteDbContext>();
        SoporteDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new SoporteDbContext(opciones.Options);
    }
}
