using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Arquitectura.Datos;

/// <summary>Qué publica la CI: el modelo lógico (LikeC4) o un snapshot de despliegue (ATLAS-ADR-0056 §2).</summary>
public enum TipoPublicacion
{
    Modelo,
    Despliegue,
}

/// <summary>Niveles del prompt maestro §7.1 que acepta el módulo: ningún externo ve arquitectura (ATLAS-ADR-0056 §3).</summary>
public enum VisibilidadArquitectura
{
    InternoTi,
    Restringido,
}

/// <summary>Una publicación desde <c>release</c> o de despliegue está publicada; desde otra rama, es borrador (plan 01 §10.2).</summary>
public enum EstadoPublicacion
{
    Borrador,
    Publicado,
}

/// <summary>Qué par de capas se comparó: modelo con gitops, o gitops con Argo CD (ATLAS-ADR-0056 §6).</summary>
public enum CapaDrift
{
    ModeloGitops,
    GitopsArgo,
}

public enum SeveridadDrift
{
    Media,
    Alta,
}

public enum EstadoHallazgo
{
    Abierto,
    Resuelto,
}

/// <summary>Cómo terminó la última lectura de Argo CD de un clúster.</summary>
public enum ResultadoLecturaArgo
{
    Correcta,
    SinConfigurar,
    Error,
}

/// <summary>Un proyecto LikeC4 (<c>flit</c>, <c>flito</c>, <c>landscape</c>) o la fuente de los snapshots. Solo lo publica su repo.</summary>
public sealed class ProyectoArquitectura : Entidad
{
    public required string Codigo { get; init; }

    /// <summary>Aplicativo del catálogo (no se mapea: un módulo no lee las tablas de otro, ATLAS-ADR-0031).</summary>
    public required Guid AplicativoId { get; init; }

    /// <summary><c>dueño/nombre</c> en minúsculas, el del primer publicador.</summary>
    public required string Repositorio { get; init; }
}

/// <summary>Una versión <b>inmutable</b>: un disparador en la base rechaza cualquier cambio o borrado.</summary>
public sealed class PublicacionArquitectura : Entidad
{
    public required Guid ProyectoId { get; init; }

    public required TipoPublicacion Tipo { get; init; }

    /// <summary><c>dev</c>, <c>qa</c> o <c>pdn</c>: de la rama en un modelo y del snapshot en un despliegue.</summary>
    public required string Canal { get; init; }

    /// <summary>Clúster del snapshot; vacío en un modelo (una cadena vacía para que el índice único la cuente).</summary>
    public required string Cluster { get; init; }

    public required string Rama { get; init; }

    /// <summary>Sale del token OIDC, nunca del cuerpo.</summary>
    public required string CommitSha { get; init; }

    public string? VersionPropuesta { get; init; }

    public required EstadoPublicacion Estado { get; init; }

    public required VisibilidadArquitectura Visibilidad { get; init; }

    public string? HerramientaVersion { get; init; }

    /// <summary>JSON validado: el de <c>likec4 export json</c> o el snapshot reescrito por el servidor, solo con los campos conocidos.</summary>
    public required string Contenido { get; init; }

    /// <summary>SHA-256 del contenido; un snapshot lo calcula sin <c>generadoEn</c>.</summary>
    public required string Sha256 { get; init; }

    public string? Workflow { get; init; }

    public string? WorkflowDelJob { get; init; }

    public string? Ejecucion { get; init; }

    public required string PublicadoPor { get; init; }

    public required DateTimeOffset PublicadoEn { get; init; }

    public DateTimeOffset? GeneradoEn { get; init; }
}

/// <summary>Un elemento del modelo en una publicación: proyección para la ficha y el drift, sin leer el JSON.</summary>
public sealed class ElementoArquitectura : Entidad
{
    public required Guid PublicacionId { get; init; }

    public required string ElementoId { get; init; }

    public required string Kind { get; init; }

    public required string Titulo { get; init; }

    public string? Descripcion { get; init; }

    public string? Tecnologia { get; init; }

    public required List<string> Etiquetas { get; init; }

    /// <summary>Objeto JSON con los metadatos (texto o lista de textos).</summary>
    public required string Metadatos { get; init; }

    /// <summary>Lista JSON de enlaces https con su título.</summary>
    public required string Enlaces { get; init; }

    /// <summary>Nombre exacto del Deployment o StatefulSet: la llave del drift (plan 01 §8.4).</summary>
    public string? K8sWorkload { get; init; }
}

/// <summary>Una vista del modelo en una publicación.</summary>
public sealed class VistaArquitectura : Entidad
{
    public required Guid PublicacionId { get; init; }

    public required string VistaId { get; init; }

    /// <summary><c>estatica</c>, <c>dinamica</c> o <c>despliegue</c>.</summary>
    public required string Tipo { get; init; }

    /// <summary><c>landscape</c>, <c>contexto</c>, <c>contenedores</c>, <c>componentes</c>, <c>dinamica</c>, <c>despliegue</c> u <c>otra</c>.</summary>
    public required string Categoria { get; init; }

    public required string Titulo { get; init; }
}

/// <summary>Un hallazgo de drift. Se abre, se alerta una vez y se resuelve solo cuando una lectura con datos ya no lo encuentra.</summary>
public sealed class HallazgoDrift : Entidad
{
    /// <summary>Identidad estable: capa, ambiente, clúster, código y sujeto. Reabrir usa la misma fila.</summary>
    public required string Clave { get; init; }

    public required CapaDrift Capa { get; init; }

    public required string Ambiente { get; init; }

    public string? Cluster { get; init; }

    /// <summary><c>ausente_en_gitops</c>, <c>sin_modelo</c>, <c>fuera_de_ambiente</c>, <c>sin_sincronizar</c>, <c>sin_salud</c>, <c>sin_reconciliar</c>, <c>aplicacion_ausente</c> o <c>imagen_distinta</c>.</summary>
    public required string Codigo { get; init; }

    public string? ElementoId { get; set; }

    public string? Sujeto { get; set; }

    public required string Descripcion { get; set; }

    public required SeveridadDrift Severidad { get; set; }

    public required VisibilidadArquitectura Visibilidad { get; set; }

    public Guid? AplicativoId { get; set; }

    public required EstadoHallazgo Estado { get; set; }

    public required DateTimeOffset DetectadoEn { get; set; }

    public DateTimeOffset? ResueltoEn { get; set; }

    /// <summary>Cuándo salió su alerta; al reabrirse vuelve a <c>null</c>.</summary>
    public DateTimeOffset? AlertadoEn { get; set; }
}

/// <summary>La última lectura de Argo CD de un clúster: sin ella, la capa dice «sin datos», nunca «sin drift».</summary>
public sealed class LecturaArgo : Entidad
{
    public required string Cluster { get; init; }

    public required ResultadoLecturaArgo Resultado { get; set; }

    /// <summary>Código estable y sin detalles: <c>http_401</c>, <c>sin_respuesta</c>, <c>respuesta_invalida</c>.</summary>
    public string? Motivo { get; set; }

    public required DateTimeOffset LeidaEn { get; set; }
}

/// <summary>La última vez que se pudo comparar el modelo con gitops en un ambiente: sin ella, esa comparación dice «sin datos».</summary>
public sealed class ComparacionModeloGitops : Entidad
{
    public required string Ambiente { get; init; }

    public required DateTimeOffset ComparadaEn { get; set; }
}

/// <summary>
/// Tablas <c>arquitectura_*</c> (ATLAS-ADR-0056), con su historial <c>__ef_migraciones_arquitectura</c>. Ninguna lleva
/// <c>organizacion_id</c>: la arquitectura es de FLIT y ningún externo la ve.
/// </summary>
public sealed class ArquitecturaDbContext(DbContextOptions<ArquitecturaDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloArquitectura.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloArquitectura.IdModulo);

    public const int LargoCodigo = 60;
    public const int LargoRepositorio = 200;
    public const int LargoCanal = 10;
    public const int LargoCluster = 60;
    public const int LargoRama = 200;
    public const int LargoSha = 64;
    public const int LargoVersion = 40;
    public const int LargoTitulo = 500;
    public const int LargoElemento = 300;
    public const int LargoKind = 100;
    public const int LargoDescripcion = 2000;
    public const int LargoSujeto = 300;
    public const int LargoClave = 64;
    public const int LargoMotivo = 60;

    public DbSet<ProyectoArquitectura> Proyectos => Set<ProyectoArquitectura>();

    public DbSet<PublicacionArquitectura> Publicaciones => Set<PublicacionArquitectura>();

    public DbSet<ElementoArquitectura> Elementos => Set<ElementoArquitectura>();

    public DbSet<VistaArquitectura> Vistas => Set<VistaArquitectura>();

    public DbSet<HallazgoDrift> Hallazgos => Set<HallazgoDrift>();

    public DbSet<LecturaArgo> LecturasArgo => Set<LecturaArgo>();

    public DbSet<ComparacionModeloGitops> Comparaciones => Set<ComparacionModeloGitops>();

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<ProyectoArquitectura>(e =>
        {
            e.ToTable(Prefijo + "proyecto", t => t.HasComment("Proyectos LikeC4 y fuentes de snapshots; cada uno lo publica su repo (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Codigo).HasMaxLength(LargoCodigo);
            e.Property(p => p.Repositorio).HasMaxLength(LargoRepositorio);
            e.HasIndex(p => p.Codigo).IsUnique();
        });

        modelBuilder.Entity<PublicacionArquitectura>(e =>
        {
            e.ToTable(Prefijo + "publicacion", t =>
            {
                t.HasComment("Versiones inmutables por sha y canal: modelos LikeC4 y snapshots de despliegue (ATLAS-ADR-0056).");
                t.HasCheckConstraint("ck_arquitectura_publicacion_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_arquitectura_publicacion_cluster", "(tipo = 'modelo') = (cluster = '')");
            });
            e.Property(p => p.Id).ValueGeneratedNever();
            Enum(e.Property(p => p.Tipo), 20);
            e.Property(p => p.Canal).HasMaxLength(LargoCanal);
            e.Property(p => p.Cluster).HasMaxLength(LargoCluster);
            e.Property(p => p.Rama).HasMaxLength(LargoRama);
            e.Property(p => p.CommitSha).HasMaxLength(LargoSha);
            e.Property(p => p.VersionPropuesta).HasMaxLength(LargoVersion);
            Enum(e.Property(p => p.Estado), 20);
            Enum(e.Property(p => p.Visibilidad), 20);
            e.Property(p => p.HerramientaVersion).HasMaxLength(LargoVersion);
            e.Property(p => p.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(p => p.Workflow).HasMaxLength(LargoRama);
            e.Property(p => p.WorkflowDelJob).HasMaxLength(LargoRama);
            e.Property(p => p.Ejecucion).HasMaxLength(LargoVersion);
            e.Property(p => p.PublicadoPor).HasMaxLength(LargoRama);
            e.HasOne<ProyectoArquitectura>().WithMany().HasForeignKey(p => p.ProyectoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.ProyectoId, p.Tipo, p.Canal, p.Cluster, p.CommitSha }).IsUnique();
            e.HasIndex(p => new { p.Tipo, p.Canal, p.Cluster, p.PublicadoEn });
        });

        modelBuilder.Entity<ElementoArquitectura>(e =>
        {
            e.ToTable(Prefijo + "elemento", t => t.HasComment("Elementos por publicación: proyección del JSON para la ficha y el drift (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.ElementoId).HasMaxLength(LargoElemento);
            e.Property(p => p.Kind).HasMaxLength(LargoKind);
            e.Property(p => p.Titulo).HasMaxLength(LargoTitulo);
            e.Property(p => p.Descripcion).HasMaxLength(LargoDescripcion);
            e.Property(p => p.Tecnologia).HasMaxLength(LargoTitulo);
            e.Property(p => p.Metadatos).HasColumnType("jsonb");
            e.Property(p => p.Enlaces).HasColumnType("jsonb");
            e.Property(p => p.K8sWorkload).HasColumnName("k8s_workload").HasMaxLength(LargoSujeto);
            e.HasOne<PublicacionArquitectura>().WithMany().HasForeignKey(p => p.PublicacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.PublicacionId, p.ElementoId }).IsUnique();
        });

        modelBuilder.Entity<VistaArquitectura>(e =>
        {
            e.ToTable(Prefijo + "vista", t => t.HasComment("Vistas por publicación (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.VistaId).HasMaxLength(LargoElemento);
            e.Property(p => p.Tipo).HasMaxLength(20);
            e.Property(p => p.Categoria).HasMaxLength(20);
            e.Property(p => p.Titulo).HasMaxLength(LargoTitulo);
            e.HasOne<PublicacionArquitectura>().WithMany().HasForeignKey(p => p.PublicacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.PublicacionId, p.VistaId }).IsUnique();
        });

        modelBuilder.Entity<HallazgoDrift>(e =>
        {
            e.ToTable(Prefijo + "hallazgo_drift", t => t.HasComment("Hallazgos de drift entre modelo, gitops y Argo CD; sin hosts ni IP (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Clave).HasMaxLength(LargoClave);
            Enum(e.Property(p => p.Capa), 20);
            e.Property(p => p.Ambiente).HasMaxLength(LargoCanal);
            e.Property(p => p.Cluster).HasMaxLength(LargoCluster);
            e.Property(p => p.Codigo).HasMaxLength(30);
            e.Property(p => p.ElementoId).HasMaxLength(LargoElemento);
            e.Property(p => p.Sujeto).HasMaxLength(LargoSujeto);
            e.Property(p => p.Descripcion).HasMaxLength(LargoDescripcion);
            Enum(e.Property(p => p.Severidad), 10);
            Enum(e.Property(p => p.Visibilidad), 20);
            Enum(e.Property(p => p.Estado), 10);
            e.HasIndex(p => p.Clave).IsUnique();
            e.HasIndex(p => new { p.Ambiente, p.Estado });
        });

        modelBuilder.Entity<LecturaArgo>(e =>
        {
            e.ToTable(Prefijo + "lectura_argo", t => t.HasComment("Última lectura de Argo CD por clúster: resultado, nunca credenciales (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Cluster).HasMaxLength(LargoCluster);
            Enum(e.Property(p => p.Resultado), 20);
            e.Property(p => p.Motivo).HasMaxLength(LargoMotivo);
            e.HasIndex(p => p.Cluster).IsUnique();
        });

        modelBuilder.Entity<ComparacionModeloGitops>(e =>
        {
            e.ToTable(Prefijo + "comparacion", t => t.HasComment("Última comparación del modelo con gitops por ambiente (ATLAS-ADR-0056)."));
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Ambiente).HasMaxLength(LargoCanal);
            e.HasIndex(p => p.Ambiente).IsUnique();
        });
    }

    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad, int largo) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(largo);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Arquitectura -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<ArquitecturaDbContext>
{
    public ArquitecturaDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<ArquitecturaDbContext>();
        ArquitecturaDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new ArquitecturaDbContext(opciones.Options);
    }
}
