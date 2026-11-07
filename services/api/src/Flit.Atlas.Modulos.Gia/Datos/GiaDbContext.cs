using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Atlas.Modulos.Gia.Datos;

/// <summary>Un caso de uso de IA: la ficha del plan 02 §7.1 (ATLAS-ADR-0055 §3). Los enums son texto con CHECK.</summary>
public sealed class CasoUso : Entidad
{
    public required string Codigo { get; init; }

    public required string Nombre { get; set; }

    /// <summary>Aplicación o repo como texto: las fichas del plan citan rutas que no siempre son aplicativos del catálogo.</summary>
    public required string Aplicacion { get; set; }

    /// <summary>Aplicativo del catálogo, si se conoce (no se mapea: un módulo no lee las tablas de otro).</summary>
    public Guid? AplicativoId { get; set; }

    public string? Repo { get; set; }

    /// <summary>Dueño del caso: la semilla no lo llena, porque el plan no lo nombra.</summary>
    public Guid? DuenoPersonaId { get; set; }

    public string Estado { get; set; } = ValoresGia.EstadoBorrador;

    public List<string> Ambientes { get; set; } = [];

    public required string Proposito { get; set; }

    public string? Tipo { get; set; }

    public List<string> UsuariosAfectados { get; set; } = [];

    public bool? DecisionSobrePersonas { get; set; }

    public string? DecisionDescripcion { get; set; }

    public string? HumanoEnBucle { get; set; }

    public List<string> CategoriasDato { get; set; } = [];

    public List<string> TiposPii { get; set; } = [];

    public int? VolumenMensualEstimado { get; set; }

    public string? BaseLegal { get; set; }

    public string? BaseLegalRef { get; set; }

    public string? Minimizacion { get; set; }

    public bool? PresidioActivo { get; set; }

    public List<string> PresidioEntidades { get; set; } = [];

    public List<string> AliasUsados { get; set; } = [];

    /// <summary>Proveedor y modelo que usa hoy, como texto (ATLAS-ADR-0055 §8).</summary>
    public string? ProveedorActual { get; set; }

    public string? TransferenciaInternacional { get; set; }

    public string? MetricasCalidad { get; set; }

    public string? EvalsRuta { get; set; }

    public string? PromptsUbicacion { get; set; }

    public string? Controles { get; set; }

    public string? AvisoUsuario { get; set; }

    public string? PlanRollback { get; set; }

    public List<string> Enlaces { get; set; } = [];

    /// <summary>El riesgo que estimó el plan, no una evaluación: <c>bajo</c>, <c>medio</c>, <c>medio_alto</c> o <c>alto</c>.</summary>
    public string? RiesgoPreliminar { get; set; }

    /// <summary>La ficha del plan tal como está, con sus marcas <c>[por verificar]</c>.</summary>
    public string? DetallePlan { get; set; }

    /// <summary>Nivel de la evaluación aprobada; lo pone el servidor.</summary>
    public string? NivelRiesgo { get; set; }

    public Guid? AprobadoPor { get; set; }

    public DateTimeOffset? AprobadoEn { get; set; }

    public DateOnly? ProximaRevision { get; set; }

    public int VersionActual { get; set; } = 1;
}

/// <summary>Foto <b>inmutable</b> de la ficha: un disparador rechaza cualquier cambio o borrado.</summary>
public sealed class VersionCasoUso : Entidad
{
    public required Guid CasoUsoId { get; init; }

    public required int Numero { get; init; }

    public required string Foto { get; init; }

    public required string Motivo { get; init; }

    public Guid? AutorPersonaId { get; init; }

    public required DateTimeOffset GuardadaEn { get; init; }
}

/// <summary>Una evaluación de riesgo (plan 02 §8). Lo único que cambia después de crearla es su decisión.</summary>
public sealed class EvaluacionRiesgo : Entidad
{
    public required Guid CasoUsoId { get; init; }

    public required int Version { get; init; }

    public required string Dimensiones { get; init; }

    public required int PuntajeInherente { get; init; }

    public required string NivelInherente { get; init; }

    public required int PuntajeResidual { get; init; }

    public required string NivelResidual { get; init; }

    public required string Principios { get; init; }

    public string? TransferenciaInternacional { get; init; }

    public string Decision { get; set; } = ValoresGia.DecisionPendiente;

    public string? Condiciones { get; set; }

    public required Guid EvaluadorPersonaId { get; init; }

    public required DateTimeOffset EvaluadaEn { get; init; }

    public DateTimeOffset? DecididaEn { get; set; }
}

/// <summary>Un voto sobre una evaluación, en la calidad de un rol de gobierno que la persona tiene.</summary>
public sealed class VotoEvaluacion : Entidad
{
    public required Guid EvaluacionId { get; init; }

    public required Guid PersonaId { get; init; }

    public required string Rol { get; init; }

    public required string Voto { get; init; }

    public string? Condiciones { get; init; }

    public string? Comentario { get; init; }

    public required DateTimeOffset VotadoEn { get; init; }
}

/// <summary>Rol de gobierno de una persona (plan 02 §4.1, ATLAS-ADR-0055 §2): la calidad en la que puede votar.</summary>
public sealed class RolGobierno : Entidad
{
    public required Guid PersonaId { get; init; }

    public required string Rol { get; init; }

    public required Guid AsignadoPor { get; init; }

    public required DateTimeOffset AsignadoEn { get; init; }
}

/// <summary>Evaluación de impacto en privacidad (plan 02 §8.4), una por caso.</summary>
public sealed class Eipd : Entidad
{
    public required Guid CasoUsoId { get; init; }

    public string Estado { get; set; } = ValoresGia.EipdBorrador;

    public required string Descripcion { get; set; }

    public required string Necesidad { get; set; }

    public required string Riesgos { get; set; }

    public required string Medidas { get; set; }

    public required string OpinionOpd { get; set; }

    public required string PlanAccion { get; set; }

    public string? DocumentoUrl { get; set; }

    public Guid? DecididaPor { get; set; }

    public DateTimeOffset? DecididaEn { get; set; }
}

/// <summary>Una versión de una política (plan 02 §6). Solo el borrador se edita.</summary>
public sealed class Politica : Entidad
{
    public required string Codigo { get; init; }

    public required string Version { get; init; }

    public required string Titulo { get; set; }

    public required string Contenido { get; set; }

    public string? FuenteGit { get; set; }

    public string Estado { get; set; } = ValoresGia.PoliticaBorrador;

    public bool RequiereAceptacion { get; set; } = true;

    public DateOnly? VigenteDesde { get; set; }

    public Guid? AprobadaPor { get; set; }
}

/// <summary>Quién aceptó una versión de una política, con fecha e IP.</summary>
public sealed class AceptacionPolitica : Entidad
{
    public required Guid PoliticaId { get; init; }

    public required Guid PersonaId { get; init; }

    public required DateTimeOffset AceptadaEn { get; init; }

    public System.Net.IPAddress? Ip { get; init; }
}

/// <summary>Herramienta de IA del equipo (plan 02 §12).</summary>
public sealed class Herramienta : Entidad
{
    public required string Codigo { get; init; }

    public required string Nombre { get; set; }

    public required string Estado { get; set; }

    public string? Plan { get; set; }

    public string? Proveedor { get; set; }

    public required string NivelDatoMaximo { get; set; }

    public string? ConfiguracionRequerida { get; set; }

    public decimal? CostoMensualUsd { get; set; }

    public string? Notas { get; set; }
}

/// <summary>Un asiento de una herramienta.</summary>
public sealed class Asiento : Entidad
{
    public required Guid HerramientaId { get; init; }

    public Guid? PersonaId { get; set; }

    public required string UsuarioNombre { get; set; }

    public required string Correo { get; set; }

    public required string TipoCuenta { get; set; }

    public required DateOnly AsignadoEn { get; set; }

    public DateOnly? RetiradoEn { get; set; }

    public DateTimeOffset? ConfiguracionVerificadaEn { get; set; }

    public List<string> ReposAutorizados { get; set; } = [];
}

/// <summary>Un incidente de IA (plan 02 §6.6).</summary>
public sealed class Incidente : Entidad
{
    public required string Codigo { get; init; }

    public required string Tipo { get; set; }

    public required string Severidad { get; set; }

    public Guid? CasoUsoId { get; set; }

    public required string Descripcion { get; set; }

    public required DateTimeOffset DetectadoEn { get; set; }

    public DateTimeOffset? ContenidoEn { get; set; }

    public DateTimeOffset? CerradoEn { get; set; }

    public List<string> Acciones { get; set; } = [];

    /// <summary>Solo registra lo que hizo una persona: Atlas no reporta a la SIC.</summary>
    public bool ReportadoSic { get; set; }

    public required Guid ResponsablePersonaId { get; set; }

    public string? Lecciones { get; set; }
}

/// <summary>
/// Tablas <c>gia_*</c> (ATLAS-ADR-0055), con su historial <c>__ef_migraciones_gia</c>. Ninguna lleva <c>organizacion_id</c>: el
/// gobierno de IA es de FLIT y los externos nunca lo ven (prompt maestro §7.3).
/// </summary>
public sealed class GiaDbContext(DbContextOptions<GiaDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloGia.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloGia.IdModulo);

    public const int LargoCodigo = 100;
    public const int LargoNombre = 200;
    public const int LargoCorto = 500;
    public const int LargoMedio = 2000;
    public const int LargoLargo = 20_000;
    public const int LargoContenido = 200_000;
    public const int LargoEnum = 30;

    public DbSet<CasoUso> Casos => Set<CasoUso>();

    public DbSet<VersionCasoUso> VersionesCaso => Set<VersionCasoUso>();

    public DbSet<EvaluacionRiesgo> Evaluaciones => Set<EvaluacionRiesgo>();

    public DbSet<VotoEvaluacion> Votos => Set<VotoEvaluacion>();

    public DbSet<RolGobierno> RolesGobierno => Set<RolGobierno>();

    public DbSet<Eipd> Eipds => Set<Eipd>();

    public DbSet<Politica> Politicas => Set<Politica>();

    public DbSet<AceptacionPolitica> Aceptaciones => Set<AceptacionPolitica>();

    public DbSet<Herramienta> Herramientas => Set<Herramienta>();

    public DbSet<Asiento> Asientos => Set<Asiento>();

    public DbSet<Incidente> Incidentes => Set<Incidente>();

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    private static string Lista(IEnumerable<string> valores) => string.Join(", ", valores.Select(v => $"'{v}'"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<CasoUso>(e =>
        {
            e.ToTable(Prefijo + "caso_uso", t =>
            {
                t.HasComment("Casos de uso de IA: la ficha del plan 02 §7.1 (ATLAS-ADR-0055).");
                t.HasCheckConstraint("ck_gia_caso_uso_estado", $"estado IN ({Lista(ValoresGia.EstadosCaso)})");
                t.HasCheckConstraint("ck_gia_caso_uso_tipo", $"tipo IS NULL OR tipo IN ({Lista(ValoresGia.TiposCaso)})");
                t.HasCheckConstraint("ck_gia_caso_uso_humano", $"humano_en_bucle IS NULL OR humano_en_bucle IN ({Lista(ValoresGia.HumanoEnBucle)})");
                t.HasCheckConstraint("ck_gia_caso_uso_nivel", $"nivel_riesgo IS NULL OR nivel_riesgo IN ({Lista(ValoresGia.Niveles)})");
                t.HasCheckConstraint("ck_gia_caso_uso_preliminar", $"riesgo_preliminar IS NULL OR riesgo_preliminar IN ({Lista(ValoresGia.RiesgosPreliminares)})");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Codigo).HasMaxLength(LargoCodigo);
            e.Property(c => c.Nombre).HasMaxLength(LargoNombre);
            e.Property(c => c.Aplicacion).HasMaxLength(LargoCorto);
            e.Property(c => c.Repo).HasMaxLength(LargoNombre);
            e.Property(c => c.Estado).HasMaxLength(LargoEnum);
            e.Property(c => c.Proposito).HasMaxLength(LargoMedio);
            e.Property(c => c.Tipo).HasMaxLength(LargoEnum);
            e.Property(c => c.DecisionDescripcion).HasMaxLength(LargoMedio);
            e.Property(c => c.HumanoEnBucle).HasMaxLength(LargoEnum);
            e.Property(c => c.BaseLegal).HasMaxLength(LargoCorto);
            e.Property(c => c.BaseLegalRef).HasMaxLength(LargoCorto);
            e.Property(c => c.Minimizacion).HasMaxLength(LargoMedio);
            e.Property(c => c.ProveedorActual).HasMaxLength(LargoCorto);
            e.Property(c => c.TransferenciaInternacional).HasMaxLength(LargoMedio);
            e.Property(c => c.MetricasCalidad).HasMaxLength(LargoMedio);
            e.Property(c => c.EvalsRuta).HasMaxLength(LargoCorto);
            e.Property(c => c.PromptsUbicacion).HasMaxLength(LargoCorto);
            e.Property(c => c.Controles).HasMaxLength(LargoMedio);
            e.Property(c => c.AvisoUsuario).HasMaxLength(LargoMedio);
            e.Property(c => c.PlanRollback).HasMaxLength(LargoMedio);
            e.Property(c => c.RiesgoPreliminar).HasMaxLength(LargoEnum);
            e.Property(c => c.DetallePlan).HasMaxLength(LargoLargo);
            e.Property(c => c.NivelRiesgo).HasMaxLength(LargoEnum);
            e.HasIndex(c => c.Codigo).IsUnique();
            e.HasIndex(c => c.Estado);
            e.HasIndex(c => c.ProximaRevision);
        });

        modelBuilder.Entity<VersionCasoUso>(e =>
        {
            e.ToTable(Prefijo + "caso_uso_version", t => t.HasComment("Historial inmutable de la ficha de cada caso (ATLAS-ADR-0055)."));
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Foto).HasColumnType("jsonb");
            e.Property(v => v.Motivo).HasMaxLength(LargoCorto).HasComment(PlataformaDbContext.Pii);
            e.HasOne<CasoUso>().WithMany().HasForeignKey(v => v.CasoUsoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.CasoUsoId, v.Numero }).IsUnique();
        });

        modelBuilder.Entity<EvaluacionRiesgo>(e =>
        {
            e.ToTable(Prefijo + "evaluacion_riesgo", t =>
            {
                t.HasComment("Evaluaciones de riesgo por caso: seis dimensiones, nivel por la peor (plan 02 §8).");
                t.HasCheckConstraint("ck_gia_evaluacion_decision", $"decision IN ({Lista(ValoresGia.Decisiones)})");
                t.HasCheckConstraint("ck_gia_evaluacion_inherente", $"nivel_inherente IN ({Lista(ValoresGia.Niveles)})");
                t.HasCheckConstraint("ck_gia_evaluacion_residual", $"nivel_residual IN ({Lista(ValoresGia.Niveles)})");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Dimensiones).HasColumnType("jsonb");
            e.Property(v => v.Principios).HasColumnType("jsonb");
            e.Property(v => v.NivelInherente).HasMaxLength(LargoEnum);
            e.Property(v => v.NivelResidual).HasMaxLength(LargoEnum);
            e.Property(v => v.TransferenciaInternacional).HasMaxLength(LargoMedio);
            e.Property(v => v.Decision).HasMaxLength(LargoEnum);
            e.Property(v => v.Condiciones).HasMaxLength(LargoMedio);
            e.HasOne<CasoUso>().WithMany().HasForeignKey(v => v.CasoUsoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.CasoUsoId, v.Version }).IsUnique();
        });

        modelBuilder.Entity<VotoEvaluacion>(e =>
        {
            e.ToTable(Prefijo + "voto_evaluacion", t =>
            {
                t.HasComment("Votos sobre una evaluación, en la calidad de un rol de gobierno (ATLAS-ADR-0055 §5).");
                t.HasCheckConstraint("ck_gia_voto_rol", $"rol IN ({Lista(ValoresGia.RolesGobierno)})");
                t.HasCheckConstraint("ck_gia_voto_voto", $"voto IN ({Lista(ValoresGia.Votos)})");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Rol).HasMaxLength(LargoEnum);
            e.Property(v => v.Voto).HasMaxLength(LargoEnum);
            e.Property(v => v.Condiciones).HasMaxLength(LargoMedio);
            e.Property(v => v.Comentario).HasMaxLength(LargoMedio).HasComment(PlataformaDbContext.Pii);
            e.HasOne<EvaluacionRiesgo>().WithMany().HasForeignKey(v => v.EvaluacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.EvaluacionId, v.PersonaId, v.Rol }).IsUnique();
        });

        modelBuilder.Entity<RolGobierno>(e =>
        {
            e.ToTable(Prefijo + "rol_gobierno", t =>
            {
                t.HasComment("Quién tiene cada rol de gobierno: Responsable de IA, OPD, Comité o Seguridad (plan 02 §4.1).");
                t.HasCheckConstraint("ck_gia_rol_gobierno_rol", $"rol IN ({Lista(ValoresGia.RolesGobierno)})");
            });
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Rol).HasMaxLength(LargoEnum);
            e.HasIndex(r => new { r.PersonaId, r.Rol }).IsUnique();
        });

        modelBuilder.Entity<Eipd>(e =>
        {
            e.ToTable(Prefijo + "eipd", t =>
            {
                t.HasComment("Evaluación de impacto en privacidad de cada caso (plan 02 §8.4).");
                t.HasCheckConstraint("ck_gia_eipd_estado", $"estado IN ({Lista(ValoresGia.EstadosEipd)})");
            });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Estado).HasMaxLength(LargoEnum);
            e.Property(x => x.Descripcion).HasMaxLength(LargoLargo);
            e.Property(x => x.Necesidad).HasMaxLength(LargoLargo);
            e.Property(x => x.Riesgos).HasMaxLength(LargoLargo);
            e.Property(x => x.Medidas).HasMaxLength(LargoLargo);
            e.Property(x => x.OpinionOpd).HasMaxLength(LargoLargo);
            e.Property(x => x.PlanAccion).HasMaxLength(LargoLargo);
            e.Property(x => x.DocumentoUrl).HasMaxLength(LargoCorto);
            e.HasOne<CasoUso>().WithMany().HasForeignKey(x => x.CasoUsoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CasoUsoId).IsUnique();
        });

        modelBuilder.Entity<Politica>(e =>
        {
            e.ToTable(Prefijo + "politica", t =>
            {
                t.HasComment("Versiones de las políticas de IA (plan 02 §6). Solo el borrador se edita.");
                t.HasCheckConstraint("ck_gia_politica_estado", $"estado IN ({Lista(ValoresGia.EstadosPolitica)})");
            });
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Codigo).HasMaxLength(LargoCodigo);
            e.Property(p => p.Version).HasMaxLength(LargoEnum);
            e.Property(p => p.Titulo).HasMaxLength(LargoNombre);
            e.Property(p => p.Contenido).HasMaxLength(LargoContenido);
            e.Property(p => p.FuenteGit).HasMaxLength(LargoCorto);
            e.Property(p => p.Estado).HasMaxLength(LargoEnum);
            e.HasIndex(p => new { p.Codigo, p.Version }).IsUnique();
            // A lo sumo una versión vigente por política.
            e.HasIndex(p => p.Codigo).IsUnique().HasDatabaseName("ix_gia_politica_vigente").HasFilter("estado = 'vigente'");
        });

        modelBuilder.Entity<AceptacionPolitica>(e =>
        {
            e.ToTable(Prefijo + "aceptacion_politica", t => t.HasComment("Registro de aceptación de cada versión por persona (plan 02 §6)."));
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Ip).HasComment(PlataformaDbContext.Pii);
            e.HasOne<Politica>().WithMany().HasForeignKey(a => a.PoliticaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.PoliticaId, a.PersonaId }).IsUnique();
        });

        modelBuilder.Entity<Herramienta>(e =>
        {
            e.ToTable(Prefijo + "herramienta", t =>
            {
                t.HasComment("Herramientas de IA del equipo (plan 02 §12).");
                t.HasCheckConstraint("ck_gia_herramienta_estado", $"estado IN ({Lista(ValoresGia.EstadosHerramienta)})");
                t.HasCheckConstraint("ck_gia_herramienta_nivel", $"nivel_dato_maximo IN ({Lista(ValoresGia.CategoriasDato)})");
            });
            e.Property(h => h.Id).ValueGeneratedNever();
            e.Property(h => h.Codigo).HasMaxLength(LargoCodigo);
            e.Property(h => h.Nombre).HasMaxLength(LargoNombre);
            e.Property(h => h.Estado).HasMaxLength(LargoEnum);
            e.Property(h => h.Plan).HasMaxLength(LargoNombre);
            e.Property(h => h.Proveedor).HasMaxLength(LargoNombre);
            e.Property(h => h.NivelDatoMaximo).HasMaxLength(LargoEnum);
            e.Property(h => h.ConfiguracionRequerida).HasMaxLength(LargoMedio);
            e.Property(h => h.CostoMensualUsd).HasPrecision(10, 2);
            e.Property(h => h.Notas).HasMaxLength(LargoMedio);
            e.HasIndex(h => h.Codigo).IsUnique();
        });

        modelBuilder.Entity<Asiento>(e =>
        {
            e.ToTable(Prefijo + "asiento", t =>
            {
                t.HasComment("Asientos de cada herramienta (plan 02 §12.1).");
                t.HasCheckConstraint("ck_gia_asiento_cuenta", $"tipo_cuenta IN ({Lista(ValoresGia.TiposCuenta)})");
            });
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.UsuarioNombre).HasMaxLength(LargoNombre).HasComment(PlataformaDbContext.Pii);
            e.Property(a => a.Correo).HasMaxLength(LargoNombre).HasComment(PlataformaDbContext.Pii);
            e.Property(a => a.TipoCuenta).HasMaxLength(LargoEnum);
            e.HasOne<Herramienta>().WithMany().HasForeignKey(a => a.HerramientaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.HerramientaId);
        });

        modelBuilder.Entity<Incidente>(e =>
        {
            e.ToTable(Prefijo + "incidente", t =>
            {
                t.HasComment("Incidentes de IA (plan 02 §6.6).");
                t.HasCheckConstraint("ck_gia_incidente_tipo", $"tipo IN ({Lista(ValoresGia.TiposIncidente)})");
                t.HasCheckConstraint("ck_gia_incidente_severidad", $"severidad IN ({Lista(ValoresGia.Severidades)})");
            });
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.Codigo).HasMaxLength(LargoCodigo);
            e.Property(i => i.Tipo).HasMaxLength(LargoEnum);
            e.Property(i => i.Severidad).HasMaxLength(LargoEnum);
            e.Property(i => i.Descripcion).HasMaxLength(LargoLargo).HasComment(PlataformaDbContext.Pii);
            e.Property(i => i.Lecciones).HasMaxLength(LargoLargo);
            e.HasOne<CasoUso>().WithMany().HasForeignKey(i => i.CasoUsoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.Codigo).IsUnique();
            e.HasIndex(i => i.DetectadoEn);
        });
    }
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Gia -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<GiaDbContext>
{
    public GiaDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<GiaDbContext>();
        GiaDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new GiaDbContext(opciones.Options);
    }
}
