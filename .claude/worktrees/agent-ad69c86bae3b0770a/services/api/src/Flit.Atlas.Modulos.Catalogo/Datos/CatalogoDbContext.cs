using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Atlas.Modulos.Catalogo.Datos;

/// <summary>Nivel en la jerarquía producto → módulo → servicio/componente (prompt maestro §8.1).</summary>
public enum TipoAplicativo
{
    Producto,
    Modulo,
    Servicio,
}

/// <summary>Grupo del inventario del plan 00 §2.1 (FLIT 2.0, FLITO, satélites, legados e infraestructura).</summary>
public enum GrupoAplicativo
{
    Flit2,
    Flito,
    Satelite,
    Legado,
    Infraestructura,
}

public enum EstadoAplicativo
{
    Planeado,
    Activo,
    EnRetiro,
    Retirado,
}

public enum Criticidad
{
    Alta,
    Media,
    Baja,
}

/// <summary>
/// Un producto, módulo o servicio de FLIT. La jerarquía va en <see cref="PadreId"/>: un producto no tiene padre, un
/// módulo cuelga de un producto y un servicio de un producto o de un módulo. No tiene <c>organizacion_id</c>: el catálogo
/// es de FLIT y solo lo ven los empleados (<c>atlas.catalogo.ver</c>); lo que se comparta con externos irá por paquetes y
/// documentos (F4).
/// </summary>
public sealed class Aplicativo : Entidad
{
    /// <summary>Clave natural, estable y legible (<c>flit2-comparendos</c>): la usan la URL de la ficha y la semilla.</summary>
    public required string Slug { get; set; }

    public required string Nombre { get; set; }

    public required TipoAplicativo Tipo { get; set; }

    public Guid? PadreId { get; set; }

    public required GrupoAplicativo Grupo { get; set; }

    /// <summary>Lo que se sabe del aplicativo; lo que falta confirmar lleva la marca «[por verificar]».</summary>
    public string? Descripcion { get; set; }

    public EstadoAplicativo Estado { get; set; } = EstadoAplicativo.Activo;

    public Criticidad Criticidad { get; set; } = Criticidad.Media;

    /// <summary>Si trata datos personales (Ley 1581): lo usan el asistente y el gobierno de IA para sus filtros.</summary>
    public bool ManejaDatosPersonales { get; set; }

    /// <summary>Repositorio de GitHub como <c>dueño/nombre</c> (<c>flitsas/flit</c>).</summary>
    public string? Repo { get; set; }

    /// <summary>Carpeta dentro del repo (<c>services/core-api</c>); vacía si es el repo completo.</summary>
    public string? RutaEnRepo { get; set; }

    public List<string> Tecnologias { get; set; } = [];

    /// <summary>Ambientes donde corre: <c>dev</c>, <c>qa</c>, <c>pdn</c>.</summary>
    public List<string> Ambientes { get; set; } = [];

    public List<string> Etiquetas { get; set; } = [];
}

/// <summary>
/// Una fila de la lista blanca de publicación desde la CI (HU 3.1, ATLAS-ADR-0035): el repo que puede publicar fuentes de
/// un aplicativo con el token OIDC de GitHub Actions, desde qué ramas, con qué workflow y de qué tipos. Revocar es borrar
/// la fila. La leen los módulos que reciben publicaciones (contratos, documentación, arquitectura) con
/// <c>IFuentesPublicacion</c>.
/// </summary>
public sealed class FuentePublicacion : Entidad
{
    public required Guid AplicativoId { get; set; }

    /// <summary><c>dueño/nombre</c>, en minúsculas (GitHub no distingue mayúsculas).</summary>
    public required string Repositorio { get; set; }

    /// <summary>Ramas exactas o con <c>*</c> (<c>release</c>, <c>hotfix/*</c>).</summary>
    public List<string> Ramas { get; set; } = [];

    /// <summary>Ruta del workflow que llama (<c>.github/workflows/ci.yml</c>); vacío: cualquiera del repo.</summary>
    public string? Workflow { get; set; }

    /// <summary><c>openapi</c>, <c>asyncapi</c>, <c>proto</c>, <c>likec4</c> o <c>docs</c>.</summary>
    public List<string> Tipos { get; set; } = [];

    /// <summary>Por qué existe la fila (quién la pidió, para qué).</summary>
    public string? Nota { get; set; }
}

/// <summary>
/// Registro de las entradas de la semilla ya aplicadas, por su slug. La semilla solo crea las que nunca aplicó: así
/// correrla otra vez no duplica nada, no pisa lo que se editó en Atlas y no revive lo que alguien eliminó (ATLAS-ADR-0028).
/// </summary>
public sealed class SemillaAplicada : Entidad
{
    public required string Slug { get; init; }

    /// <summary>Versión del archivo de la semilla que la aplicó.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Tablas <c>catalogo_*</c> del módulo con su historial <c>__ef_migraciones_catalogo</c> (ATLAS-ADR-0016, ATLAS-ADR-0026
/// y ATLAS-ADR-0028). Solo conoce sus entidades: nunca mapea tablas del núcleo ni de otro módulo.
/// </summary>
/// <param name="alcance">Alcance de datos de la petición (lo registra la identidad); sin él, el del sistema.</param>
public sealed class CatalogoDbContext(DbContextOptions<CatalogoDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloCatalogo.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloCatalogo.IdModulo);

    public const int LargoSlug = 80;
    public const int LargoNombre = 120;
    public const int LargoDescripcion = 2000;
    public const int LargoRepo = 200;
    public const int LargoRuta = 300;
    public const int LargoEtiqueta = 50;
    public const int MaximoElementos = 20;

    public DbSet<Aplicativo> Aplicativos => Set<Aplicativo>();

    public DbSet<SemillaAplicada> SemillaAplicada => Set<SemillaAplicada>();

    public DbSet<FuentePublicacion> FuentesPublicacion => Set<FuentePublicacion>();

    public const int LargoWorkflow = 200;
    public const int LargoRama = 100;
    public const int LargoNota = 500;
    public const int MaximoRamas = 10;

    /// <summary>Tipos de fuente que se publican desde la CI (prompt maestro A-08): contratos, LikeC4 y documentación.</summary>
    public static readonly IReadOnlyList<string> TiposFuente = ["openapi", "asyncapi", "proto", "likec4", "docs"];

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Aplicativo>(e =>
        {
            e.ToTable(Prefijo + "aplicativo", t =>
            {
                t.HasComment("Catálogo de aplicativos: producto → módulo → servicio (ATLAS-ADR-0028).");
                // La regla completa de la jerarquía la valida la API; la base garantiza lo mínimo.
                t.HasCheckConstraint("ck_catalogo_aplicativo_producto_sin_padre", "(tipo = 'producto') = (padre_id IS NULL)");
                t.HasCheckConstraint("ck_catalogo_aplicativo_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            });
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Slug).HasMaxLength(LargoSlug);
            e.HasIndex(a => a.Slug).IsUnique();
            e.Property(a => a.Nombre).HasMaxLength(LargoNombre);
            e.Property(a => a.Descripcion).HasMaxLength(LargoDescripcion);
            e.Property(a => a.Repo).HasMaxLength(LargoRepo);
            e.Property(a => a.RutaEnRepo).HasMaxLength(LargoRuta);
            Enum(e.Property(a => a.Tipo), 20);
            Enum(e.Property(a => a.Grupo), 20);
            Enum(e.Property(a => a.Estado), 20);
            Enum(e.Property(a => a.Criticidad), 10);
            // Un padre no se borra mientras tenga hijos: la API responde 409 antes de llegar aquí.
            e.HasOne<Aplicativo>().WithMany().HasForeignKey(a => a.PadreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.PadreId);
            e.HasIndex(a => new { a.Grupo, a.Estado });
        });

        modelBuilder.Entity<SemillaAplicada>(e =>
        {
            e.ToTable(Prefijo + "semilla", t => t.HasComment("Entradas de la semilla del catálogo ya aplicadas (no se vuelven a crear)."));
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Slug).HasMaxLength(LargoSlug);
            e.HasIndex(s => s.Slug).IsUnique();
        });

        modelBuilder.Entity<FuentePublicacion>(e =>
        {
            e.ToTable(Prefijo + "fuente_publicacion", t =>
            {
                t.HasComment("Lista blanca de publicación desde la CI con el OIDC de GitHub Actions (ATLAS-ADR-0035).");
                t.HasCheckConstraint("ck_catalogo_fuente_publicacion_repositorio", "repositorio ~ '^[a-z0-9][a-z0-9_.-]*/[a-z0-9][a-z0-9_.-]*$'");
                t.HasCheckConstraint("ck_catalogo_fuente_publicacion_ramas", "cardinality(ramas) > 0");
                t.HasCheckConstraint("ck_catalogo_fuente_publicacion_tipos",
                    $"cardinality(tipos) > 0 AND tipos <@ ARRAY[{string.Join(", ", TiposFuente.Select(t => $"'{t}'"))}]::text[]");
            });
            e.Property(f => f.Id).ValueGeneratedNever();
            e.Property(f => f.Repositorio).HasMaxLength(LargoRepo);
            e.Property(f => f.Workflow).HasMaxLength(LargoWorkflow);
            e.Property(f => f.Nota).HasMaxLength(LargoNota);
            // Borrar un aplicativo revoca sus fuentes.
            e.HasOne<Aplicativo>().WithMany().HasForeignKey(f => f.AplicativoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.AplicativoId, f.Repositorio, f.Workflow }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(f => f.Repositorio);
        });

        // translate() y lower() para la búsqueda sin tildes (no cambian el esquema), como en el núcleo.
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Traducir))!).HasName("translate").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Minusculas))!).HasName("lower").IsBuiltIn();

        // Ninguna entidad tiene OrganizacionId, pero se aplica igual: si una tabla futura lo lleva, queda filtrada.
        AplicarPredicadoDeOrganizacion(modelBuilder);
    }

    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad, int largo) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(largo);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>: generar una migración no necesita una base real.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Catalogo -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<CatalogoDbContext>
{
    public CatalogoDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<CatalogoDbContext>();
        CatalogoDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new CatalogoDbContext(opciones.Options);
    }
}
