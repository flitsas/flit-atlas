using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Diagramas.Datos;

/// <summary>Los tres formatos de los diagramas que no viven en el código (ATLAS-ADR-0054 §2).</summary>
public enum TipoDiagrama
{
    Lienzo,
    Mermaid,
    Bpmn,
}

/// <summary>Niveles del prompt maestro §7.1 que acepta el módulo: <c>publico_clientes</c> espera al flujo de aprobación (ATLAS-ADR-0054 §3).</summary>
public enum VisibilidadDiagrama
{
    InternoNegocio,
    InternoTi,
    Restringido,
}

/// <summary>Cómo nació una versión.</summary>
public enum OrigenVersionDiagrama
{
    Creacion,
    Edicion,
    Restauracion,
}

/// <summary>Identidad estable de un diagrama, con la foto de su última versión. Tipo, código y aplicativo no cambian.</summary>
public sealed class Diagrama : Entidad
{
    /// <summary>Código único: el <c>id</c> de <c>&lt;Diagrama/&gt;</c> en los documentos.</summary>
    public required string Codigo { get; init; }

    /// <summary>Aplicativo del catálogo (no se mapea: un módulo no lee las tablas de otro, ATLAS-ADR-0031).</summary>
    public required Guid AplicativoId { get; init; }

    public required TipoDiagrama Tipo { get; init; }

    public required string Titulo { get; set; }

    public required VisibilidadDiagrama Visibilidad { get; set; }

    public int VersionActual { get; set; }
}

/// <summary>Una versión <b>inmutable</b>: un disparador en la base rechaza cualquier cambio o borrado.</summary>
public sealed class VersionDiagrama : Entidad
{
    public required Guid DiagramaId { get; init; }

    public required int Numero { get; init; }

    public required OrigenVersionDiagrama Origen { get; init; }

    public int? RestauradaDe { get; init; }

    public required string Titulo { get; init; }

    public required VisibilidadDiagrama Visibilidad { get; init; }

    /// <summary>Fuente Mermaid, XML BPMN ya limpio o JSON canónico del lienzo.</summary>
    public required string Fuente { get; init; }

    public required string Nota { get; init; }

    public required Guid AutorPersonaId { get; init; }

    public required DateTimeOffset GuardadaEn { get; init; }

    /// <summary>SHA-256 de título, visibilidad y fuente: restaurar la N da la misma huella que la N.</summary>
    public required string Huella { get; init; }
}

/// <summary>Un nodo del lienzo en una versión: proyección para consultar sin leer el JSON (ATLAS-ADR-0054 §2).</summary>
public sealed class NodoDiagrama : Entidad
{
    public required Guid VersionId { get; init; }

    public required string Clave { get; init; }

    public required string Tipo { get; init; }

    public required string Etiqueta { get; init; }

    /// <summary>Aplicativo del catálogo que representa, si lo hay.</summary>
    public Guid? AplicativoId { get; init; }

    /// <summary>Elemento de LikeC4 que representa, en solo lectura (A-04), si lo hay.</summary>
    public string? ElementoLikeC4 { get; init; }
}

/// <summary>Una relación del lienzo en una versión.</summary>
public sealed class RelacionDiagrama : Entidad
{
    public required Guid VersionId { get; init; }

    public required string Clave { get; init; }

    public required string Origen { get; init; }

    public required string Destino { get; init; }

    public string? Etiqueta { get; init; }
}

/// <summary>Comentario sobre una versión y, si se quiere, sobre un nodo.</summary>
public sealed class ComentarioDiagrama : Entidad
{
    public required Guid DiagramaId { get; init; }

    public required int VersionNumero { get; init; }

    public string? NodoClave { get; init; }

    public required Guid PersonaId { get; init; }

    public required string Texto { get; init; }

    public required DateTimeOffset EscritoEn { get; init; }
}

/// <summary>
/// Tablas <c>diagramas_*</c> (ATLAS-ADR-0054), con su historial <c>__ef_migraciones_diagramas</c>. Ninguna lleva
/// <c>organizacion_id</c>: los diagramas son de FLIT y ningún externo los ve hasta que exista compartir.
/// </summary>
public sealed class DiagramasDbContext(DbContextOptions<DiagramasDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloDiagramas.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloDiagramas.IdModulo);

    public const int LargoCodigo = 100;
    public const int LargoTitulo = 200;
    public const int LargoNota = 500;
    public const int LargoComentario = 2000;
    public const int LargoClave = 64;
    public const int LargoEtiqueta = 200;
    public const int LargoElemento = 200;
    public const int LargoTipoNodo = 20;

    public DbSet<Diagrama> Diagramas => Set<Diagrama>();

    public DbSet<VersionDiagrama> Versiones => Set<VersionDiagrama>();

    public DbSet<NodoDiagrama> Nodos => Set<NodoDiagrama>();

    public DbSet<RelacionDiagrama> Relaciones => Set<RelacionDiagrama>();

    public DbSet<ComentarioDiagrama> Comentarios => Set<ComentarioDiagrama>();

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Traducir))!).HasName("translate").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Minusculas))!).HasName("lower").IsBuiltIn();

        modelBuilder.Entity<Diagrama>(e =>
        {
            e.ToTable(Prefijo + "diagrama", t => t.HasComment("Identidad estable de cada diagrama, con la foto de su última versión (ATLAS-ADR-0054)."));
            e.Property(d => d.Id).ValueGeneratedNever();
            e.Property(d => d.Codigo).HasMaxLength(LargoCodigo);
            Enum(e.Property(d => d.Tipo), 20);
            e.Property(d => d.Titulo).HasMaxLength(LargoTitulo);
            Enum(e.Property(d => d.Visibilidad), 20);
            e.HasIndex(d => d.Codigo).IsUnique();
            e.HasIndex(d => d.AplicativoId);
        });

        modelBuilder.Entity<VersionDiagrama>(e =>
        {
            e.ToTable(Prefijo + "version", t =>
            {
                t.HasComment("Versiones inmutables de cada diagrama: fuente, foto, autor, nota y huella (ATLAS-ADR-0054).");
                t.HasCheckConstraint("ck_diagramas_version_huella", "huella ~ '^[0-9a-f]{64}$'");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            Enum(e.Property(v => v.Origen), 20);
            e.Property(v => v.Titulo).HasMaxLength(LargoTitulo);
            Enum(e.Property(v => v.Visibilidad), 20);
            e.Property(v => v.Nota).HasMaxLength(LargoNota).HasComment(PlataformaDbContext.Pii);
            e.Property(v => v.Huella).HasMaxLength(64).IsFixedLength();
            e.HasOne<Diagrama>().WithMany().HasForeignKey(v => v.DiagramaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.DiagramaId, v.Numero }).IsUnique();
        });

        modelBuilder.Entity<NodoDiagrama>(e =>
        {
            e.ToTable(Prefijo + "nodo", t => t.HasComment("Nodos del lienzo por versión: proyección del JSON (ATLAS-ADR-0054)."));
            e.Property(n => n.Id).ValueGeneratedNever();
            e.Property(n => n.Clave).HasMaxLength(LargoClave);
            e.Property(n => n.Tipo).HasMaxLength(LargoTipoNodo);
            e.Property(n => n.Etiqueta).HasMaxLength(LargoEtiqueta);
            e.Property(n => n.ElementoLikeC4).HasColumnName("elemento_likec4").HasMaxLength(LargoElemento);
            e.HasOne<VersionDiagrama>().WithMany().HasForeignKey(n => n.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(n => new { n.VersionId, n.Clave }).IsUnique();
            e.HasIndex(n => n.AplicativoId);
        });

        modelBuilder.Entity<RelacionDiagrama>(e =>
        {
            e.ToTable(Prefijo + "relacion", t => t.HasComment("Relaciones del lienzo por versión: proyección del JSON (ATLAS-ADR-0054)."));
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Clave).HasMaxLength(LargoClave);
            e.Property(r => r.Origen).HasMaxLength(LargoClave);
            e.Property(r => r.Destino).HasMaxLength(LargoClave);
            e.Property(r => r.Etiqueta).HasMaxLength(LargoEtiqueta);
            e.HasOne<VersionDiagrama>().WithMany().HasForeignKey(r => r.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => new { r.VersionId, r.Clave }).IsUnique();
        });

        modelBuilder.Entity<ComentarioDiagrama>(e =>
        {
            e.ToTable(Prefijo + "comentario", t => t.HasComment("Comentarios por versión y, si se quiere, por nodo (ATLAS-ADR-0054)."));
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.NodoClave).HasMaxLength(LargoClave);
            e.Property(c => c.Texto).HasMaxLength(LargoComentario).HasComment(PlataformaDbContext.Pii);
            e.HasOne<Diagrama>().WithMany().HasForeignKey(c => c.DiagramaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.DiagramaId, c.EscritoEn });
        });
    }

    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad, int largo) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(largo);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Diagramas -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<DiagramasDbContext>
{
    public DiagramasDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<DiagramasDbContext>();
        DiagramasDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new DiagramasDbContext(opciones.Options);
    }
}
