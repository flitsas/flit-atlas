using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Flit.Atlas.Modulos.Documentacion.Datos;

/// <summary>De dónde sale el contenido (ATLAS-ADR-0053 §2).</summary>
public enum OrigenDocumento
{
    /// <summary>Llega por la CI: no se edita en Atlas.</summary>
    Repo,

    /// <summary>Se escribe en Atlas, con revisiones y aprobación.</summary>
    Atlas,
}

/// <summary>Niveles de visibilidad del prompt maestro §7.1.</summary>
public enum VisibilidadDocumento
{
    PublicoClientes,
    InternoNegocio,
    InternoTi,
    Restringido,
}

/// <summary>Estados del documento (prompt maestro §8.5).</summary>
public enum EstadoDocumento
{
    Borrador,
    EnRevision,
    Publicado,
    Archivado,
}

/// <summary>Cómo nació una revisión.</summary>
public enum OrigenRevisionDocumento
{
    Creacion,
    Edicion,
    Restauracion,
}

/// <summary>
/// Identidad estable de un documento (ATLAS-ADR-0053 §2). Título, tipo, visibilidad, audiencias y responsable son la foto
/// de la <see cref="VersionVigente"/>: lo que se lista es siempre lo que se puede leer. Sin versiones, la de la última
/// revisión.
/// </summary>
public sealed class Documento : Entidad
{
    /// <summary>Aplicativo del catálogo (no se mapea: un módulo no lee las tablas de otro, ATLAS-ADR-0031).</summary>
    public required Guid AplicativoId { get; init; }

    public required OrigenDocumento Origen { get; init; }

    /// <summary>Ruta en el repo: junto con el aplicativo es la clave natural de los de origen repo.</summary>
    public string? Ruta { get; init; }

    /// <summary><c>dueño/nombre</c> del repo que lo publica.</summary>
    public string? Repositorio { get; init; }

    public required string Tipo { get; set; }

    public required string Titulo { get; set; }

    public required VisibilidadDocumento Visibilidad { get; set; }

    public List<string> Audiencias { get; set; } = [];

    /// <summary>Responsable funcional: texto del front-matter o del formulario.</summary>
    public required string Responsable { get; set; }

    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Borrador;

    /// <summary>Número de la última versión; 0 si no tiene.</summary>
    public int VersionActual { get; set; }

    /// <summary>La versión que se lee por defecto: la del canal pdn o, sin él, la última.</summary>
    public int? VersionVigente { get; set; }

    /// <summary>Número de la última revisión; 0 en los de origen repo.</summary>
    public int RevisionActual { get; set; }

    /// <summary>Revisión congelada en la versión publicada más reciente.</summary>
    public int? RevisionPublicada { get; set; }
}

/// <summary>
/// Una versión <b>inmutable</b> (ATLAS-ADR-0053 §2): lo que llega por la CI o lo que se congela al publicar una revisión.
/// Guarda su propia foto y su visibilidad: una versión vieja más restringida no se vuelve visible si el documento se abre
/// después. Un disparador en la base rechaza cualquier cambio.
/// </summary>
public sealed class VersionDocumento : Entidad
{
    public required Guid DocumentoId { get; init; }

    /// <summary>1, 2, 3… dentro del documento.</summary>
    public required int Numero { get; init; }

    public required string Tipo { get; init; }

    public required string Titulo { get; init; }

    /// <summary>Cuerpo en MDX, sin el front-matter.</summary>
    public required string Contenido { get; init; }

    public required VisibilidadDocumento Visibilidad { get; init; }

    public List<string> Audiencias { get; init; } = [];

    public required string Responsable { get; init; }

    /// <summary>
    /// De origen repo: el front-matter no la da por aprobada (plan 03 §18.3): solo la ven los roles técnicos y los
    /// responsables del aplicativo.
    /// </summary>
    public required bool EsBorrador { get; init; }

    /// <summary>SHA-256 del archivo completo (repo) o de la foto de la revisión (Atlas). Decide la idempotencia.</summary>
    public required string Sha256 { get; init; }

    public string? Ruta { get; init; }

    public string? Repositorio { get; init; }

    public string? Rama { get; init; }

    public string? CommitSha { get; init; }

    public string? Workflow { get; init; }

    public string? WorkflowDelJob { get; init; }

    public string? Ejecucion { get; init; }

    /// <summary>Revisión congelada, si se escribió en Atlas.</summary>
    public int? RevisionOrigen { get; init; }

    /// <summary><c>github:dueño/repo@rama</c> en los de repo; en los de Atlas, vacío (ver <see cref="AprobadaPorPersonaId"/>).</summary>
    public string? PublicadoPor { get; init; }

    /// <summary>Quien aprobó y publicó, si se escribió en Atlas.</summary>
    public Guid? AprobadaPorPersonaId { get; init; }

    public required DateTimeOffset PublicadoEn { get; init; }
}

/// <summary>Una revisión <b>inmutable</b> de lo escrito en Atlas, con el modelo de las guías (ATLAS-ADR-0040).</summary>
public sealed class RevisionDocumento : Entidad
{
    public required Guid DocumentoId { get; init; }

    public required int Numero { get; init; }

    public required OrigenRevisionDocumento Origen { get; init; }

    public int? RestauradaDe { get; init; }

    public required string Tipo { get; init; }

    public required string Titulo { get; init; }

    public required string Contenido { get; init; }

    public required VisibilidadDocumento Visibilidad { get; init; }

    public List<string> Audiencias { get; init; } = [];

    public required string Responsable { get; init; }

    /// <summary>Mensaje del cambio (texto libre: nunca va a la auditoría ni a los logs).</summary>
    public required string Nota { get; init; }

    public required Guid AutorPersonaId { get; init; }

    public required DateTimeOffset GuardadaEn { get; init; }

    /// <summary>SHA-256 de la foto, sin autor, fecha ni nota.</summary>
    public required string Huella { get; init; }
}

/// <summary>Estado de una exportación (ATLAS-ADR-0053 §7).</summary>
public enum EstadoExportacionDocumentos
{
    Pendiente,
    Procesando,
    Lista,
    Fallida,
    Vencida,
}

/// <summary>
/// Una exportación de un documento o de una colección (HU 5.6): quién la pidió (la única persona que la ve y la descarga),
/// qué entró, su estado y sus objetos en el S3. Sin <c>organizacion_id</c>: hasta que exista compartir, solo exportan empleados.
/// </summary>
public sealed class ExportacionDocumentos : Entidad
{
    public required Guid SolicitadaPor { get; init; }

    public required string Nombre { get; init; }

    /// <summary>El documento, si es de uno solo (para el enlace del aviso).</summary>
    public Guid? DocumentoId { get; init; }

    /// <summary>Qué se exportó (<c>jsonb</c>): <c>[{documentoId, version, sha256, visibilidad}]</c>.</summary>
    public required string Documentos { get; init; }

    public List<string> Formatos { get; init; } = [];

    public EstadoExportacionDocumentos Estado { get; set; } = EstadoExportacionDocumentos.Pendiente;

    public required string EntradaClave { get; init; }

    public required string EntradaSha256 { get; init; }

    public string? Objeto { get; set; }

    public string? HashContenido { get; set; }

    /// <summary>Archivos en el S3 (<c>jsonb</c>): <c>[{clave, ruta, formato, bytes, sha256, tipo}]</c>.</summary>
    public string Archivos { get; set; } = "[]";

    public string? ErrorCodigo { get; set; }

    public string? Error { get; set; }

    public required DateTimeOffset SolicitadaEn { get; init; }

    public DateTimeOffset? IniciadaEn { get; set; }

    public DateTimeOffset? TerminadaEn { get; set; }

    public DateTimeOffset? VenceEn { get; set; }

    public DateTimeOffset? VencidaEn { get; set; }
}

/// <summary>Un documento que una persona marcó para leer sin conexión (HU 5.4, ATLAS-ADR-0034).</summary>
public sealed class FavoritoDocumento : Entidad
{
    public required Guid PersonaId { get; init; }

    public required Guid DocumentoId { get; init; }
}

/// <summary>Qué versión de un documento corre en cada ambiente (ATLAS-ADR-0053 §2).</summary>
public sealed class CanalDocumento : Entidad
{
    public required Guid DocumentoId { get; init; }

    /// <summary><c>dev</c>, <c>qa</c> o <c>pdn</c>.</summary>
    public required string Ambiente { get; init; }

    public required int VersionNumero { get; set; }
}

/// <summary>
/// Tablas <c>documentacion_*</c> (ATLAS-ADR-0053), con su historial <c>__ef_migraciones_documentacion</c>. Ninguna lleva
/// <c>organizacion_id</c>: los documentos son de FLIT y las tablas por organización llegan con compartir (HU 5.6).
/// </summary>
public sealed class DocumentacionDbContext(DbContextOptions<DocumentacionDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloDocumentacion.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloDocumentacion.IdModulo);

    public const int LargoTitulo = 200;
    public const int LargoTipo = 40;
    public const int LargoResponsable = 200;
    public const int LargoRuta = 500;
    public const int LargoRepo = 200;
    public const int LargoRama = 200;
    public const int LargoWorkflow = 500;
    public const int LargoNota = 500;
    public const int LargoAudiencia = 60;
    public const int MaximoAudiencias = 20;
    public const int LargoClaveObjeto = 500;
    public const int LargoErrorExportacion = 500;

    /// <summary>
    /// Tope del cuerpo. Mantiene el <c>tsvector</c> por debajo de su límite de 1 MB aun con un texto de palabras cortas y
    /// todas distintas.
    /// </summary>
    public const int LargoContenido = 300_000;

    public DbSet<Documento> Documentos => Set<Documento>();

    public DbSet<VersionDocumento> Versiones => Set<VersionDocumento>();

    public DbSet<RevisionDocumento> Revisiones => Set<RevisionDocumento>();

    public DbSet<CanalDocumento> Canales => Set<CanalDocumento>();

    public DbSet<ExportacionDocumentos> Exportaciones => Set<ExportacionDocumentos>();

    public DbSet<FavoritoDocumento> Favoritos => Set<FavoritoDocumento>();

    /// <summary>Propiedad sombra con el <c>tsvector</c> de la versión.</summary>
    public const string ColumnaBusqueda = "Busqueda";

    /// <summary>
    /// <c>translate</c> y no <c>unaccent</c> (ATLAS-ADR-0053 §7): una columna generada solo admite funciones inmutables,
    /// y <c>unaccent</c> la crea el administrador en cada ambiente, así que la migración dependería de ella.
    /// </summary>
    internal static string SqlBusqueda()
    {
        static string Normalizado(string columna) =>
            $"lower(translate({columna}, '{FuncionesSql.ConTildes}', '{FuncionesSql.SinTildes}'))";
        return $"setweight(to_tsvector('spanish'::regconfig, {Normalizado("titulo")}), 'A') || "
            + $"setweight(to_tsvector('spanish'::regconfig, {Normalizado("contenido")}), 'B')";
    }

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Documento>(e =>
        {
            e.ToTable(Prefijo + "documento", t =>
            {
                t.HasComment("Identidad estable de cada documento, con la foto de su versión vigente (ATLAS-ADR-0053).");
                t.HasCheckConstraint("ck_documentacion_documento_origen",
                    "(origen = 'repo' AND ruta IS NOT NULL AND repositorio IS NOT NULL) OR (origen = 'atlas' AND ruta IS NULL AND repositorio IS NULL)");
            });
            e.Property(d => d.Id).ValueGeneratedNever();
            Enum(e.Property(d => d.Origen), 10);
            Enum(e.Property(d => d.Visibilidad), 20);
            Enum(e.Property(d => d.Estado), 20);
            e.Property(d => d.Ruta).HasMaxLength(LargoRuta);
            e.Property(d => d.Repositorio).HasMaxLength(LargoRepo);
            e.Property(d => d.Tipo).HasMaxLength(LargoTipo);
            e.Property(d => d.Titulo).HasMaxLength(LargoTitulo);
            e.Property(d => d.Responsable).HasMaxLength(LargoResponsable).HasComment(PlataformaDbContext.Pii);
            e.HasIndex(d => new { d.AplicativoId, d.Ruta }).IsUnique().HasFilter("ruta IS NOT NULL");
            e.HasIndex(d => d.AplicativoId);
            e.HasIndex(d => d.Repositorio);
        });

        modelBuilder.Entity<VersionDocumento>(e =>
        {
            e.ToTable(Prefijo + "version", t =>
            {
                t.HasComment("Versiones inmutables de cada documento: foto, sha256 y procedencia (ATLAS-ADR-0053).");
                t.HasCheckConstraint("ck_documentacion_version_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
            });
            e.Property(v => v.Id).ValueGeneratedNever();
            e.Property(v => v.Tipo).HasMaxLength(LargoTipo);
            e.Property(v => v.Titulo).HasMaxLength(LargoTitulo);
            Enum(e.Property(v => v.Visibilidad), 20);
            e.Property(v => v.Responsable).HasMaxLength(LargoResponsable).HasComment(PlataformaDbContext.Pii);
            e.Property(v => v.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(v => v.Ruta).HasMaxLength(LargoRuta);
            e.Property(v => v.Repositorio).HasMaxLength(LargoRepo);
            e.Property(v => v.Rama).HasMaxLength(LargoRama);
            e.Property(v => v.CommitSha).HasMaxLength(64);
            e.Property(v => v.Workflow).HasMaxLength(LargoWorkflow);
            e.Property(v => v.WorkflowDelJob).HasMaxLength(LargoWorkflow);
            e.Property(v => v.Ejecucion).HasMaxLength(40);
            e.Property(v => v.PublicadoPor).HasMaxLength(LargoRepo + LargoRama + 10);
            e.Property<NpgsqlTsVector>(ColumnaBusqueda).HasColumnType("tsvector").HasComputedColumnSql(SqlBusqueda(), stored: true);
            e.HasOne<Documento>().WithMany().HasForeignKey(v => v.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            // La clave alterna (documento, número) la crea la llave foránea del canal.
            e.HasIndex(v => new { v.DocumentoId, v.Sha256 }).IsUnique();
            e.HasIndex(ColumnaBusqueda).HasMethod("gin");
        });

        modelBuilder.Entity<RevisionDocumento>(e =>
        {
            e.ToTable(Prefijo + "revision", t =>
            {
                t.HasComment("Revisiones inmutables de lo escrito en Atlas: foto, autor, fecha, nota y huella (ATLAS-ADR-0053).");
                t.HasCheckConstraint("ck_documentacion_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
            });
            e.Property(r => r.Id).ValueGeneratedNever();
            Enum(e.Property(r => r.Origen), 20);
            e.Property(r => r.Tipo).HasMaxLength(LargoTipo);
            e.Property(r => r.Titulo).HasMaxLength(LargoTitulo);
            Enum(e.Property(r => r.Visibilidad), 20);
            e.Property(r => r.Responsable).HasMaxLength(LargoResponsable).HasComment(PlataformaDbContext.Pii);
            e.Property(r => r.Nota).HasMaxLength(LargoNota).HasComment(PlataformaDbContext.Pii);
            e.Property(r => r.Huella).HasMaxLength(64).IsFixedLength();
            e.HasOne<Documento>().WithMany().HasForeignKey(r => r.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => new { r.DocumentoId, r.Numero }).IsUnique();
        });

        modelBuilder.Entity<CanalDocumento>(e =>
        {
            e.ToTable(Prefijo + "canal", t => t.HasComment("Versión de cada documento por ambiente (ATLAS-ADR-0053)."));
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Ambiente).HasMaxLength(20);
            e.HasOne<Documento>().WithMany().HasForeignKey(c => c.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VersionDocumento>().WithMany().HasPrincipalKey(v => new { v.DocumentoId, v.Numero })
                .HasForeignKey(c => new { c.DocumentoId, c.VersionNumero }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.DocumentoId, c.Ambiente }).IsUnique();
        });

        modelBuilder.Entity<ExportacionDocumentos>(e =>
        {
            e.ToTable(Prefijo + "exportacion", t =>
            {
                t.HasComment("Exportaciones de documentos y colecciones: quién, qué, formatos, estado y objetos en el S3 (ATLAS-ADR-0053).");
                t.HasCheckConstraint("ck_documentacion_exportacion_lista",
                    "estado <> 'lista' OR (objeto IS NOT NULL AND hash_contenido IS NOT NULL AND terminada_en IS NOT NULL AND vence_en IS NOT NULL)");
                t.HasCheckConstraint("ck_documentacion_exportacion_hash", "hash_contenido IS NULL OR hash_contenido ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_documentacion_exportacion_documentos", "jsonb_typeof(documentos) = 'array'");
                t.HasCheckConstraint("ck_documentacion_exportacion_archivos", "jsonb_typeof(archivos) = 'array'");
            });
            e.Property(x => x.Id).ValueGeneratedNever();
            Enum(e.Property(x => x.Estado), 20);
            e.Property(x => x.Nombre).HasMaxLength(LargoTitulo + 100);
            e.Property(x => x.Documentos).HasColumnType("jsonb");
            e.Property(x => x.EntradaClave).HasMaxLength(LargoClaveObjeto);
            e.Property(x => x.EntradaSha256).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Objeto).HasMaxLength(LargoClaveObjeto);
            e.Property(x => x.HashContenido).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Archivos).HasColumnType("jsonb");
            e.Property(x => x.ErrorCodigo).HasMaxLength(40);
            e.Property(x => x.Error).HasMaxLength(LargoErrorExportacion);
            e.HasOne<Documento>().WithMany().HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SolicitadaPor, x.SolicitadaEn });
            // El trabajo de retención busca las que vencen y las pendientes viejas.
            e.HasIndex(x => new { x.Estado, x.VenceEn });
        });

        modelBuilder.Entity<FavoritoDocumento>(e =>
        {
            e.ToTable(Prefijo + "favorito", t => t.HasComment("Documentos que cada persona marcó para leer sin conexión (ATLAS-ADR-0034)."));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Documento>().WithMany().HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PersonaId, x.DocumentoId }).IsUnique();
        });
    }

    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad, int largo) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(largo);
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Documentacion -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<DocumentacionDbContext>
{
    public DocumentacionDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<DocumentacionDbContext>();
        DocumentacionDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new DocumentacionDbContext(opciones.Options);
    }
}
