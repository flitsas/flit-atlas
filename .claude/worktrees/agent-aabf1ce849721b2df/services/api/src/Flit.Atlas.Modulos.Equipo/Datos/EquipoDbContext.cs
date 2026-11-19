using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Equipo.Datos;

/// <summary>
/// Una responsabilidad vigente del reparto (prompt maestro §8.2): una persona es principal o secundaria de un aplicativo
/// del catálogo. Hay a lo sumo una principal y una secundaria por aplicativo. El aplicativo se guarda por id, sin llave
/// foránea a <c>catalogo_aplicativo</c> (un módulo no depende de las tablas de otro, ATLAS-ADR-0031): si se elimina del
/// catálogo, la responsabilidad queda huérfana y las vistas la marcan.
/// </summary>
public sealed class Responsabilidad : Entidad
{
    public required Guid AplicativoId { get; init; }

    public required TipoResponsabilidad Tipo { get; init; }

    public required Guid PersonaId { get; set; }

    /// <summary>Dedicación orientativa en porcentaje (el secundario ≈ 20 %); vacía si el reparto no la dice.</summary>
    public int? Dedicacion { get; set; }

    public required DateOnly VigenteDesde { get; set; }

    /// <summary>Último día de la responsabilidad (incluido); vacío, sin fin.</summary>
    public DateOnly? VigenteHasta { get; set; }

    /// <summary>Un secundario nuevo empieza en acompañamiento hasta completar el checklist (§8.2).</summary>
    public EstadoTraspaso EstadoTraspaso { get; set; } = EstadoTraspaso.Habilitado;

    /// <summary>Posición del aplicativo en las tablas por producto y por módulo (§3.1 y §3.2 del documento).</summary>
    public int OrdenAplicativo { get; set; }

    /// <summary>Posición de la persona en la tabla por persona (§2 del documento).</summary>
    public int OrdenPersona { get; set; }

    /// <summary>Posición del aplicativo en la lista «Principal de» o «Secundario de» de la persona.</summary>
    public int OrdenEnPersona { get; set; }
}

/// <summary>
/// Versión del reparto (§8.2, historial versionado): una foto completa de las responsabilidades, con autor y nota. La
/// importación del documento crea la versión que dice el documento (la 5); cada cambio y cada restauración, la siguiente.
/// Nunca se edita ni se borra.
/// </summary>
public sealed class VersionReparto : Entidad
{
    public required int Numero { get; init; }

    public required OrigenVersion Origen { get; init; }

    /// <summary>Versión restaurada, si <see cref="Origen"/> es <see cref="OrigenVersion.Restauracion"/>.</summary>
    public int? RestauradaDe { get; init; }

    /// <summary>Fecha del documento importado (o de la versión restaurada).</summary>
    public DateOnly? FechaDocumento { get; init; }

    /// <summary>Quién definió el reparto según el documento («Definió:»); solo en las importaciones.</summary>
    public string? DefinidoPor { get; init; }

    /// <summary>Persona de Atlas que hizo el cambio (importó, cambió o restauró).</summary>
    public Guid? AutorPersonaId { get; init; }

    public required string Nota { get; init; }

    /// <summary>Definiciones de cada rol tal como están en el §1 del documento (JSON).</summary>
    public required string Definiciones { get; init; }

    /// <summary>La foto de las responsabilidades (JSON, <see cref="FotoReparto"/>).</summary>
    public required string Contenido { get; init; }

    /// <summary>SHA-256 de la foto sin nombres: dos versiones con la misma huella tienen el mismo reparto.</summary>
    public required string Huella { get; init; }
}

/// <summary>Una actividad del traspaso acompañado (§8.2): despliegue o guardia del secundario con el principal.</summary>
public sealed class ActividadTraspaso : Entidad
{
    public required Guid ResponsabilidadId { get; init; }

    /// <summary>El secundario que hizo la actividad: si la responsabilidad cambia de persona, el checklist vuelve a cero.</summary>
    public required Guid PersonaId { get; init; }

    public required TipoActividadTraspaso Tipo { get; init; }

    public required DateOnly Fecha { get; init; }

    public string? Nota { get; init; }

    public Guid? RegistradaPorPersonaId { get; init; }
}

/// <summary>Revisión trimestral del reparto (§8.2, próxima: 20/01/2027) con su recordatorio al líder.</summary>
public sealed class RevisionProgramada : Entidad
{
    public required DateOnly Fecha { get; set; }

    public EstadoRevision Estado { get; set; } = EstadoRevision.Pendiente;

    /// <summary>Cuándo salió el recordatorio; lo marca una sola réplica (actualización condicionada).</summary>
    public DateTimeOffset? RecordatorioEnviadoEn { get; set; }

    public DateTimeOffset? HechaEn { get; set; }

    public Guid? HechaPorPersonaId { get; set; }

    public string? Nota { get; set; }
}

/// <summary>Usuario de GitHub de una persona, para el bloque CODEOWNERS (HU 2.4). Lo edita quien gestiona el equipo.</summary>
public sealed class UsuarioGitHub : Entidad
{
    public required Guid PersonaId { get; init; }

    public required string Usuario { get; set; }
}

/// <summary>
/// Contexto del módulo de equipo (ATLAS-ADR-0031): tablas <c>equipo_*</c> en el esquema <c>atlas</c> y su historial
/// <c>__ef_migraciones_equipo</c>. Sin <c>organizacion_id</c>: el reparto es de empleados de FLIT y ningún externo tiene
/// permisos de equipo.
/// </summary>
public sealed class EquipoDbContext(DbContextOptions<EquipoDbContext> opciones, IAlcanceDatos? alcance = null, TimeProvider? reloj = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloEquipo.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloEquipo.IdModulo);

    public const int LargoNota = 500;
    public const int LargoDefinidoPor = 200;
    public const int LargoUsuarioGitHub = 39;

    public const string Pii = PlataformaDbContext.Pii;

    public DbSet<Responsabilidad> Responsabilidades => Set<Responsabilidad>();
    public DbSet<VersionReparto> Versiones => Set<VersionReparto>();
    public DbSet<ActividadTraspaso> ActividadesTraspaso => Set<ActividadTraspaso>();
    public DbSet<RevisionProgramada> Revisiones => Set<RevisionProgramada>();
    public DbSet<UsuarioGitHub> UsuariosGitHub => Set<UsuarioGitHub>();

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

        modelBuilder.Entity<Responsabilidad>(e =>
        {
            e.ToTable(Prefijo + "responsabilidad", t =>
            {
                t.HasComment("Responsables principal y secundario de cada aplicativo del catálogo (reparto vigente).");
                t.HasCheckConstraint("ck_equipo_responsabilidad_dedicacion", "dedicacion IS NULL OR (dedicacion BETWEEN 1 AND 100)");
                t.HasCheckConstraint("ck_equipo_responsabilidad_vigencia", "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
            });
            e.Property(r => r.Id).ValueGeneratedNever();
            Enum(e.Property(r => r.Tipo));
            Enum(e.Property(r => r.EstadoTraspaso));
            e.HasIndex(r => new { r.AplicativoId, r.Tipo }).IsUnique();
            e.HasIndex(r => r.PersonaId);
        });

        modelBuilder.Entity<VersionReparto>(e =>
        {
            e.ToTable(Prefijo + "responsabilidad_version", t =>
            {
                t.HasComment("Historial del reparto de responsables: cada versión es una foto completa, con autor y nota.");
                t.HasCheckConstraint("ck_equipo_responsabilidad_version_numero", "numero >= 1");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            Enum(e.Property(v => v.Origen));
            e.HasIndex(v => v.Numero).IsUnique();
            e.Property(v => v.DefinidoPor).HasMaxLength(LargoDefinidoPor).HasComment(Pii);
            e.Property(v => v.Nota).HasMaxLength(LargoNota).HasComment(Pii);
            e.Property(v => v.Definiciones).HasColumnType("jsonb");
            e.Property(v => v.Contenido).HasColumnType("jsonb").HasComment(Pii);
            e.Property(v => v.Huella).HasMaxLength(64).IsFixedLength();
        });

        modelBuilder.Entity<ActividadTraspaso>(e =>
        {
            e.ToTable(Prefijo + "traspaso_checklist", t => t.HasComment("Checklist del traspaso acompañado: despliegues y guardias del secundario con el principal."));
            e.Property(a => a.Id).ValueGeneratedNever();
            Enum(e.Property(a => a.Tipo));
            e.Property(a => a.Nota).HasMaxLength(LargoNota).HasComment(Pii);
            e.HasOne<Responsabilidad>().WithMany().HasForeignKey(a => a.ResponsabilidadId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.ResponsabilidadId, a.PersonaId });
        });

        modelBuilder.Entity<RevisionProgramada>(e =>
        {
            e.ToTable(Prefijo + "revision_programada", t => t.HasComment("Revisiones trimestrales del reparto y su recordatorio."));
            e.Property(r => r.Id).ValueGeneratedNever();
            Enum(e.Property(r => r.Estado));
            e.Property(r => r.Nota).HasMaxLength(LargoNota).HasComment(Pii);
            // A lo sumo una revisión pendiente: la próxima.
            e.HasIndex(r => r.Estado).IsUnique().HasFilter("estado = 'pendiente'").HasDatabaseName("ux_equipo_revision_pendiente");
        });

        modelBuilder.Entity<UsuarioGitHub>(e =>
        {
            e.ToTable(Prefijo + "usuario_github", t => t.HasComment("Usuario de GitHub de cada persona, para el bloque CODEOWNERS."));
            e.Property(u => u.Id).ValueGeneratedNever();
            e.Property(u => u.Usuario).HasMaxLength(LargoUsuarioGitHub).HasComment(Pii);
            e.HasIndex(u => u.PersonaId).IsUnique();
        });

        // Ninguna entidad tiene OrganizacionId: el predicado no agrega filtros, pero se llama como en todo módulo para que
        // una tabla futura con organización quede filtrada sin acordarse.
        AplicarPredicadoDeOrganizacion(modelBuilder);
    }

    private static PropertyBuilder<TEnum> Enum<TEnum>(PropertyBuilder<TEnum> propiedad) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(System.Enum.GetValues<TEnum>().Max(v => Textos.De(v).Length));
}

internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<EquipoDbContext>
{
    public EquipoDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<EquipoDbContext>();
        EquipoDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new EquipoDbContext(opciones.Options);
    }
}
