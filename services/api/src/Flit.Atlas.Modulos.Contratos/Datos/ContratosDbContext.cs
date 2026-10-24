using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Contratos.Datos;

/// <summary>Tipo de contrato (plan 00 §8.1).</summary>
public enum TipoContrato
{
    Openapi,
    Asyncapi,
    Proto,
}

/// <summary>Solo los <c>compartible</c> entran a paquetes de clientes (F4).</summary>
public enum VisibilidadContrato
{
    Interna,
    Compartible,
}

/// <summary>Estado de una versión (plan 00 §8.2, regla 7): la deprecación llega con <c>publicar_manual</c>.</summary>
public enum EstadoVersion
{
    Vigente,
    Deprecada,
    Retirada,
}

/// <summary>Formato en que llegó el spec (se guarda tal cual).</summary>
public enum FormatoSpec
{
    Json,
    Yaml,
    Proto,
}

/// <summary>
/// Una interfaz de un aplicativo del catálogo, de un tipo (plan 00 §8.1): <c>external-api</c> (openapi) de
/// <c>flit2-core-api</c>. Lo crea la primera publicación. El aplicativo se guarda por id, sin llave foránea: es de otro
/// módulo (ATLAS-ADR-0026); se lee con <c>ILectorAplicativos</c>.
/// </summary>
public sealed class Contrato : Entidad
{
    public required Guid AplicativoId { get; set; }

    /// <summary>Clave del contrato dentro del aplicativo: minúsculas, números y guiones (<c>external-api</c>).</summary>
    public required string Codigo { get; set; }

    public required TipoContrato Tipo { get; set; }

    /// <summary>De la última versión publicada (<c>info.title</c>, o el paquete en proto).</summary>
    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public VisibilidadContrato Visibilidad { get; set; } = VisibilidadContrato.Interna;

    /// <summary>Repositorio de la última publicación (<c>dueño/nombre</c>).</summary>
    public required string Repositorio { get; set; }
}

/// <summary>
/// Una publicación <b>inmutable</b> de un contrato (plan 00 §8.2 y ATLAS-ADR-0035): versión semántica, hash SHA-256 del
/// spec tal como llegó, el spec mismo y de dónde salió (repo, rama, workflow, commit y ejecución del token de GitHub). Un
/// trigger de la base rechaza cualquier cambio a esos campos; solo cambian el estado y la fecha de retiro.
/// </summary>
public sealed class VersionContrato : Entidad
{
    public required Guid ContratoId { get; init; }

    /// <summary>Versión semántica sin metadatos de compilación: <c>3.3.0</c>, <c>3.4.0-dev.828c9fe</c>.</summary>
    public required string Version { get; init; }

    public required int Mayor { get; init; }

    public required int Menor { get; init; }

    public required int Parche { get; init; }

    /// <summary>Prerelease (<c>dev.828c9fe</c>, <c>rc.1a2b3c4</c>); vacío en una estable.</summary>
    public string? Prerelease { get; init; }

    /// <summary>Solo las estables pueden ir en paquetes de clientes (plan 00 §8.2, regla 3).</summary>
    public required bool EsEstable { get; init; }

    /// <summary>SHA-256 en hexadecimal (minúsculas) de los bytes UTF-8 del spec tal como llegó.</summary>
    public required string Sha256 { get; init; }

    public required FormatoSpec Formato { get; init; }

    /// <summary>El spec completo, tal como llegó.</summary>
    public required string Contenido { get; init; }

    /// <summary>Bytes UTF-8 del spec.</summary>
    public required int Tamano { get; init; }

    public required string Repositorio { get; init; }

    public required string Rama { get; init; }

    public required string CommitSha { get; init; }

    /// <summary>Workflow que llamó (<c>.github/workflows/ci.yml</c>), si el token lo trae.</summary>
    public string? Workflow { get; init; }

    /// <summary><c>job_workflow_ref</c>: el workflow reutilizable que publicó, con su referencia fijada.</summary>
    public required string WorkflowDelJob { get; init; }

    /// <summary><c>run_id</c> de GitHub Actions, para llegar a la ejecución.</summary>
    public string? Ejecucion { get; init; }

    /// <summary><c>github:dueño/repo@rama</c> (o la persona, en una publicación manual).</summary>
    public required string PublicadoPor { get; init; }

    public required DateTimeOffset PublicadoEn { get; init; }

    /// <summary>Changelog de la CI (salida JSON de <c>oasdiff changelog</c>), o <c>[]</c>.</summary>
    public string Changelog { get; init; } = "[]";

    /// <summary>Si el changelog trae algún cambio de nivel 3 (error de oasdiff: rompe compatibilidad).</summary>
    public bool TieneBreaking { get; init; }

    /// <summary>
    /// Versión contra la que la CI calculó el changelog (la de pdn que le dio Atlas, HU 3.3), si la informó. El Consolidado
    /// usa el changelog guardado cuando coincide con la versión que hoy está en pdn (HU 3.4).
    /// </summary>
    public string? ChangelogBase { get; init; }

    public EstadoVersion Estado { get; set; } = EstadoVersion.Vigente;

    public DateOnly? RetiroEn { get; set; }
}

/// <summary>
/// Índice de operaciones y canales de una versión (plan 00 §8.4), para buscar, armar paquetes y el asistente:
/// <c>GET /tramites/{id}</c> (OpenAPI), <c>send tramites.radicado</c> (AsyncAPI) o
/// <c>rpc:flit.tramites.v1.TramitesService/Get</c> (proto). Inmutable como su versión.
/// </summary>
public sealed class OperacionContrato : Entidad
{
    public required Guid VersionId { get; init; }

    /// <summary>Única en la versión.</summary>
    public required string Clave { get; init; }

    /// <summary><c>operacion</c>, <c>webhook</c>, <c>canal</c> o <c>rpc</c>.</summary>
    public required string Clase { get; init; }

    /// <summary>Método HTTP, acción de AsyncAPI (<c>send</c>, <c>receive</c>, <c>publish</c>, <c>subscribe</c>) o <c>rpc</c>.</summary>
    public string? Metodo { get; init; }

    /// <summary>Ruta HTTP, nombre del webhook, dirección del canal o servicio de proto.</summary>
    public string? Ruta { get; init; }

    public string? OperationId { get; init; }

    public string? Resumen { get; init; }

    public List<string> Etiquetas { get; init; } = [];

    /// <summary><c>x-public: true</c> o la etiqueta <c>public</c> (plan 00 §8.4).</summary>
    public bool Publica { get; init; }
}

/// <summary>
/// Qué versión de un contrato corre hoy en un ambiente (plan 00 §8.1 y §8.2, regla 5; HU 3.4 y ATLAS-ADR-0035 §8): una
/// fila por contrato y ambiente, que mueve el CD con <c>PUT /api/v1/contratos/canales/{ambiente}</c>. Cada cambio deja
/// una fila en <see cref="HistorialCanal"/>.
/// </summary>
public sealed class CanalContrato : Entidad
{
    public required Guid ContratoId { get; init; }

    /// <summary><c>dev</c>, <c>qa</c> o <c>pdn</c> (los de <c>Contratos:Canales:Ambientes</c>).</summary>
    public required string Ambiente { get; init; }

    public required Guid VersionId { get; set; }

    public required DateTimeOffset DesplegadoEn { get; set; }

    /// <summary><c>github:dueño/repo@rama</c> del CD que avisó.</summary>
    public required string DesplegadoPor { get; set; }

    public required string CommitSha { get; set; }

    /// <summary><c>run_id</c> de GitHub Actions.</summary>
    public string? Ejecucion { get; set; }
}

/// <summary>
/// Historial de los canales (solo inserción; un trigger rechaza <c>UPDATE</c>): de qué versión a cuál pasó cada ambiente,
/// cuándo y desde qué ejecución del CD.
/// </summary>
public sealed class HistorialCanal : Entidad
{
    public required Guid ContratoId { get; init; }

    public required string Ambiente { get; init; }

    public required Guid VersionId { get; init; }

    /// <summary>La que estaba antes; vacío la primera vez.</summary>
    public Guid? VersionAnteriorId { get; init; }

    public required DateTimeOffset DesplegadoEn { get; init; }

    public required string DesplegadoPor { get; init; }

    public required string Repositorio { get; init; }

    public required string Rama { get; init; }

    public required string CommitSha { get; init; }

    public required string WorkflowDelJob { get; init; }

    public string? Ejecucion { get; init; }
}

/// <summary>
/// Tablas <c>contratos_*</c> del módulo con su historial <c>__ef_migraciones_contratos</c> (ATLAS-ADR-0026 y
/// ATLAS-ADR-0035). El prefijo es el id del módulo (<c>contratos</c>), como en los demás módulos; el plan 00 §8.4 las
/// llamaba <c>contrato_*</c>.
/// </summary>
/// <param name="alcance">Alcance de datos de la petición (lo registra la identidad); sin él, el del sistema.</param>
public sealed class ContratosDbContext(DbContextOptions<ContratosDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloContratos.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloContratos.IdModulo);

    public const int LargoCodigo = 80;
    public const int LargoNombre = 200;
    public const int LargoDescripcion = 2000;
    public const int LargoVersion = 100;
    public const int LargoRepo = 200;
    public const int LargoRama = 100;
    public const int LargoWorkflow = 300;
    public const int LargoClave = 600;
    public const int LargoRuta = 500;
    public const int LargoResumen = 500;
    public const int LargoEtiqueta = 100;

    public DbSet<Contrato> Contratos => Set<Contrato>();

    public DbSet<VersionContrato> Versiones => Set<VersionContrato>();

    public DbSet<OperacionContrato> Operaciones => Set<OperacionContrato>();

    public DbSet<CanalContrato> Canales => Set<CanalContrato>();

    public DbSet<HistorialCanal> HistorialCanales => Set<HistorialCanal>();

    public const int LargoAmbiente = 20;

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Contrato>(e =>
        {
            e.ToTable(Prefijo + "contrato", t =>
            {
                t.HasComment("Contratos de integración de cada aplicativo (plan 00 §8.4, ATLAS-ADR-0035).");
                t.HasCheckConstraint("ck_contratos_contrato_codigo", "codigo ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Codigo).HasMaxLength(LargoCodigo);
            e.Property(c => c.Nombre).HasMaxLength(LargoNombre);
            e.Property(c => c.Descripcion).HasMaxLength(LargoDescripcion);
            e.Property(c => c.Repositorio).HasMaxLength(LargoRepo);
            Enum(e.Property(c => c.Tipo), 20);
            Enum(e.Property(c => c.Visibilidad), 20);
            e.HasIndex(c => new { c.AplicativoId, c.Codigo }).IsUnique();
        });

        modelBuilder.Entity<VersionContrato>(e =>
        {
            e.ToTable(Prefijo + "version", t =>
            {
                t.HasComment("Versiones inmutables de cada contrato: semver, sha256, spec y procedencia (ATLAS-ADR-0035).");
                t.HasCheckConstraint("ck_contratos_version_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_contratos_version_estable", "es_estable = (prerelease IS NULL)");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Version).HasMaxLength(LargoVersion);
            e.Property(v => v.Prerelease).HasMaxLength(LargoVersion);
            e.Property(v => v.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(v => v.Repositorio).HasMaxLength(LargoRepo);
            e.Property(v => v.Rama).HasMaxLength(LargoRama);
            e.Property(v => v.CommitSha).HasMaxLength(64);
            e.Property(v => v.Workflow).HasMaxLength(LargoWorkflow);
            e.Property(v => v.WorkflowDelJob).HasMaxLength(LargoWorkflow);
            e.Property(v => v.Ejecucion).HasMaxLength(40);
            e.Property(v => v.PublicadoPor).HasMaxLength(LargoRepo + LargoRama + 10);
            e.Property(v => v.Changelog).HasColumnType("jsonb");
            e.Property(v => v.ChangelogBase).HasMaxLength(LargoVersion);
            Enum(e.Property(v => v.Formato), 10);
            Enum(e.Property(v => v.Estado), 20);
            e.HasOne<Contrato>().WithMany().HasForeignKey(v => v.ContratoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.ContratoId, v.Version }).IsUnique();
            e.HasIndex(v => new { v.ContratoId, v.PublicadoEn });
        });

        modelBuilder.Entity<OperacionContrato>(e =>
        {
            e.ToTable(Prefijo + "operacion", t => t.HasComment("Índice de operaciones, webhooks, canales y rpc de cada versión (plan 00 §8.4)."));
            e.Property(o => o.Id).ValueGeneratedNever();
            e.Property(o => o.Clave).HasMaxLength(LargoClave);
            e.Property(o => o.Clase).HasMaxLength(20);
            e.Property(o => o.Metodo).HasMaxLength(20);
            e.Property(o => o.Ruta).HasMaxLength(LargoRuta);
            e.Property(o => o.OperationId).HasMaxLength(LargoResumen);
            e.Property(o => o.Resumen).HasMaxLength(LargoResumen);
            // Purgar una versión (decisión explícita, sin endpoint) se lleva su índice.
            e.HasOne<VersionContrato>().WithMany().HasForeignKey(o => o.VersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => new { o.VersionId, o.Clave }).IsUnique();
            e.HasIndex(o => o.OperationId);
        });

        modelBuilder.Entity<CanalContrato>(e =>
        {
            e.ToTable(Prefijo + "canal", t =>
            {
                t.HasComment("Versión de cada contrato que corre en cada ambiente (plan 00 §8.2, regla 5; HU 3.4).");
                t.HasCheckConstraint("ck_contratos_canal_ambiente", "ambiente ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Ambiente).HasMaxLength(LargoAmbiente);
            e.Property(c => c.DesplegadoPor).HasMaxLength(LargoRepo + LargoRama + 10);
            e.Property(c => c.CommitSha).HasMaxLength(64);
            e.Property(c => c.Ejecucion).HasMaxLength(40);
            e.HasOne<Contrato>().WithMany().HasForeignKey(c => c.ContratoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VersionContrato>().WithMany().HasForeignKey(c => c.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.ContratoId, c.Ambiente }).IsUnique();
        });

        modelBuilder.Entity<HistorialCanal>(e =>
        {
            e.ToTable(Prefijo + "canal_historial", t => t.HasComment("Historial de los canales por ambiente, solo inserción (HU 3.4)."));
            e.Property(h => h.Id).ValueGeneratedNever();
            e.Property(h => h.Ambiente).HasMaxLength(LargoAmbiente);
            e.Property(h => h.DesplegadoPor).HasMaxLength(LargoRepo + LargoRama + 10);
            e.Property(h => h.Repositorio).HasMaxLength(LargoRepo);
            e.Property(h => h.Rama).HasMaxLength(LargoRama);
            e.Property(h => h.CommitSha).HasMaxLength(64);
            e.Property(h => h.WorkflowDelJob).HasMaxLength(LargoWorkflow);
            e.Property(h => h.Ejecucion).HasMaxLength(40);
            e.HasOne<Contrato>().WithMany().HasForeignKey(h => h.ContratoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VersionContrato>().WithMany().HasForeignKey(h => h.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VersionContrato>().WithMany().HasForeignKey(h => h.VersionAnteriorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(h => new { h.ContratoId, h.Ambiente, h.DesplegadoEn });
        });

        // translate() y lower() para la búsqueda sin tildes, como en el catálogo.
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Traducir))!).HasName("translate").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Minusculas))!).HasName("lower").IsBuiltIn();

        // Ninguna entidad tiene OrganizacionId (los contratos son internos; lo compartido irá por paquetes, F4), pero se
        // aplica igual: si una tabla futura lo lleva, queda filtrada.
        AplicarPredicadoDeOrganizacion(modelBuilder);
    }

    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad, int largo) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(largo);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Contratos -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<ContratosDbContext>
{
    public ContratosDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<ContratosDbContext>();
        ContratosDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new ContratosDbContext(opciones.Options);
    }
}
