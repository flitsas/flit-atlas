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

/// <summary>Quién puede ver una guía (HU 3.7, prompt maestro §7.1 y §7.2).</summary>
public enum VisibilidadGuia
{
    /// <summary>Solo empleados con <c>atlas.contratos.ver_internos</c>.</summary>
    Interna,

    /// <summary>Puede ir en paquetes para clientes (F4); publicarla exige la aprobación del principal o del secundario.</summary>
    Compartible,
}

/// <summary>Estado de una guía: <c>publicada</c> si su última revisión es la publicada.</summary>
public enum EstadoGuia
{
    Borrador,
    Publicada,
}

/// <summary>De dónde salió una revisión.</summary>
public enum OrigenRevision
{
    Creacion,
    Edicion,
    Restauracion,
}

/// <summary>
/// Guía de integración de un contrato (plan 00 §8.1 y §8.4, HU 3.7, ATLAS-ADR-0040): Markdown editable en Atlas, con
/// revisiones inmutables. Esta fila guarda la foto de la última revisión (para listar sin leer los cuerpos), el estado y
/// qué revisión está publicada. Solo se escribe junto con una revisión o una publicación, en la misma transacción que su
/// auditoría.
/// </summary>
public sealed class GuiaContrato : Entidad
{
    public required Guid ContratoId { get; init; }

    public required string Titulo { get; set; }

    /// <summary>Clave del índice de operaciones (<c>GET /tramites/{id}</c>) si la guía es de una operación; vacío = del contrato.</summary>
    public string? Operacion { get; set; }

    /// <summary>Primera versión del contrato a la que aplica (semver); vacío = desde la primera.</summary>
    public string? AplicaDesde { get; set; }

    /// <summary>Última versión del contrato a la que aplica (semver); vacío = hasta la última.</summary>
    public string? AplicaHasta { get; set; }

    public VisibilidadGuia Visibilidad { get; set; } = VisibilidadGuia.Interna;

    public EstadoGuia Estado { get; set; } = EstadoGuia.Borrador;

    /// <summary>Número de la última revisión.</summary>
    public int RevisionActual { get; set; }

    /// <summary>Revisión publicada (la que verá el portal en F4); vacío si nunca se publicó.</summary>
    public int? RevisionPublicada { get; set; }

    public Guid? PublicadaPor { get; set; }

    public DateTimeOffset? PublicadaEn { get; set; }

    /// <summary>Principal o secundario que aprobó la revisión publicada, si es compartible (§7.2).</summary>
    public Guid? AprobadaPor { get; set; }
}

/// <summary>
/// Una revisión <b>inmutable</b> de una guía (plan 00 §8.2, regla 6): la foto completa de lo guardado (título, cuerpo,
/// operación, versiones y visibilidad), su autor, su fecha y su nota. Restaurar la N crea otra igual a la N, con la misma
/// huella. Un trigger de la base rechaza cualquier <c>UPDATE</c>.
/// </summary>
public sealed class RevisionGuia : Entidad
{
    public required Guid GuiaId { get; init; }

    /// <summary>1, 2, 3… dentro de la guía.</summary>
    public required int Numero { get; init; }

    public required OrigenRevision Origen { get; init; }

    /// <summary>Si es una restauración, la revisión que se restauró.</summary>
    public int? RestauradaDe { get; init; }

    public required string Titulo { get; init; }

    /// <summary>El cuerpo en Markdown, tal cual.</summary>
    public required string Contenido { get; init; }

    public string? Operacion { get; init; }

    public string? AplicaDesde { get; init; }

    public string? AplicaHasta { get; init; }

    public required VisibilidadGuia Visibilidad { get; init; }

    /// <summary>Mensaje del cambio (texto libre: puede traer datos personales; nunca va a la auditoría ni a los logs).</summary>
    public required string Nota { get; init; }

    /// <summary>Persona de Atlas que guardó.</summary>
    public required Guid AutorPersonaId { get; init; }

    public required DateTimeOffset GuardadaEn { get; init; }

    /// <summary>SHA-256 (hexadecimal) de la foto sin autor, fecha ni nota: dos revisiones con la misma huella son iguales.</summary>
    public required string Huella { get; init; }
}

/// <summary>Estado de un paquete (plan 00 §8.4, HU 4.1, ATLAS-ADR-0041).</summary>
public enum EstadoPaquete
{
    /// <summary>Nunca se publicó: ningún cliente lo ve.</summary>
    Borrador,

    /// <summary>Tiene una revisión vigente (la que verán las comparticiones de la HU 4.3).</summary>
    Publicado,

    /// <summary>Ya no se usa: no admite revisiones ni publicaciones; la historia queda.</summary>
    Archivado,
}

/// <summary>
/// Paquete de contratos para compartir con clientes (plan 00 §8.1 y §8.4, HU 4.1, ATLAS-ADR-0041): una selección curada
/// de contratos compartibles en versiones estables, sus operaciones y sus guías compartibles. Esta fila guarda la foto de
/// la última revisión (nombre y descripción), el estado y qué revisión está vigente. Solo se escribe junto con una
/// revisión, una publicación, una fijación o el archivo, en la misma transacción que su auditoría.
/// </summary>
public sealed class PaqueteContratos : Entidad
{
    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public EstadoPaquete Estado { get; set; } = EstadoPaquete.Borrador;

    /// <summary>Número de la última revisión.</summary>
    public int RevisionActual { get; set; }

    /// <summary>
    /// Revisión vigente (publicada o fijada): la que leerán las comparticiones que no fijen otra (HU 4.3). Vacío si nunca
    /// se publicó.
    /// </summary>
    public int? RevisionPublicada { get; set; }

    public Guid? PublicadaPor { get; set; }

    public DateTimeOffset? PublicadaEn { get; set; }

    /// <summary>Persona que lo creó: la única, además de los gestores globales, que puede tocarlo mientras no tenga contratos.</summary>
    public required Guid CreadoPor { get; init; }

    public Guid? ArchivadoPor { get; set; }

    public DateTimeOffset? ArchivadoEn { get; set; }
}

/// <summary>
/// Una revisión <b>inmutable</b> de un paquete (plan 00 §8.2, regla 6, y §8.4): la foto completa de lo guardado (nombre,
/// descripción y definición), su autor, su fecha, su nota y su huella. La definición es
/// <c>[{contratoId, versionId, operaciones:[clave…], guias:[{guiaId, revision}]}]</c>, normalizada (ordenada) para que
/// la huella no dependa del orden en que llegó. Un trigger de la base rechaza cualquier <c>UPDATE</c>. La unicidad
/// <c>(paquete_id, numero)</c> es la llave a la que apuntará una compartición que fije una revisión (HU 4.3).
/// </summary>
public sealed class RevisionPaquete : Entidad
{
    public required Guid PaqueteId { get; init; }

    /// <summary>1, 2, 3… dentro del paquete.</summary>
    public required int Numero { get; init; }

    public required string Nombre { get; init; }

    public string? Descripcion { get; init; }

    /// <summary>La definición en JSON (<c>jsonb</c>).</summary>
    public required string Definicion { get; init; }

    /// <summary>Mensaje del cambio (texto libre: puede traer datos personales; nunca va a la auditoría ni a los logs).</summary>
    public required string Nota { get; init; }

    public required Guid AutorPersonaId { get; init; }

    public required DateTimeOffset GuardadaEn { get; init; }

    /// <summary>SHA-256 (hexadecimal) de la foto sin autor, fecha ni nota: dos revisiones con la misma huella son iguales.</summary>
    public required string Huella { get; init; }
}

/// <summary>
/// Un paquete compartido con una organización cliente o proveedora (plan 00 §6.2 y §8.4, HU 4.3, ATLAS-ADR-0043): con
/// qué revisión (fijada o la vigente del paquete), si puede exportar y usar el sandbox, y su vigencia. Tiene
/// <c>organizacion_id</c>: la filtra el predicado obligatorio y, en la base, RLS (ATLAS-ADR-0023). Es la única entrada del
/// portal a los paquetes: un externo solo llega a un paquete por una fila de su organización. Una por paquete y
/// organización; revocar la deja con <see cref="RevocadoEn"/> (la historia queda) y compartir otra vez la reactiva.
/// </summary>
public sealed class ComparticionPaquete : Entidad
{
    public required Guid PaqueteId { get; init; }

    public required Guid OrganizacionId { get; init; }

    /// <summary>Revisión del paquete que ve la organización; vacío = sigue la vigente (publicada o fijada) del paquete.</summary>
    public int? RevisionFijada { get; set; }

    public bool PuedeExportar { get; set; }

    public bool PuedeSandbox { get; set; }

    public required DateTimeOffset VigenteDesde { get; set; }

    /// <summary>Al pasar, el acceso se corta sin intervención (se calcula en cada petición); vacío = sin vencimiento.</summary>
    public DateTimeOffset? VigenteHasta { get; set; }

    /// <summary>Persona que compartió (o reactivó) el paquete.</summary>
    public required Guid OtorgadoPor { get; set; }

    public DateTimeOffset? RevocadoEn { get; set; }

    public Guid? RevocadoPor { get; set; }
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
    public const int LargoTituloGuia = 200;
    public const int LargoNotaGuia = 500;
    public const int LargoNombrePaquete = 200;
    public const int LargoNotaPaquete = 500;

    /// <summary>Tope del cuerpo de una guía, en caracteres.</summary>
    public const int LargoContenidoGuia = 200_000;

    public DbSet<Contrato> Contratos => Set<Contrato>();

    public DbSet<VersionContrato> Versiones => Set<VersionContrato>();

    public DbSet<OperacionContrato> Operaciones => Set<OperacionContrato>();

    public DbSet<GuiaContrato> Guias => Set<GuiaContrato>();

    public DbSet<RevisionGuia> RevisionesGuia => Set<RevisionGuia>();

    public DbSet<CanalContrato> Canales => Set<CanalContrato>();

    public DbSet<HistorialCanal> HistorialCanales => Set<HistorialCanal>();

    public DbSet<PaqueteContratos> Paquetes => Set<PaqueteContratos>();

    public DbSet<RevisionPaquete> RevisionesPaquete => Set<RevisionPaquete>();

    public DbSet<ComparticionPaquete> Comparticiones => Set<ComparticionPaquete>();

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

        modelBuilder.Entity<GuiaContrato>(e =>
        {
            e.ToTable(Prefijo + "guia", t =>
            {
                t.HasComment("Guías de integración de cada contrato, con la foto de su última revisión (HU 3.7, ATLAS-ADR-0040).");
                t.HasCheckConstraint("ck_contratos_guia_revisiones",
                    "revision_actual >= 1 AND (revision_publicada IS NULL OR revision_publicada BETWEEN 1 AND revision_actual)");
            });
            e.Property(g => g.Id).ValueGeneratedNever();
            e.Property(g => g.Titulo).HasMaxLength(LargoTituloGuia);
            e.Property(g => g.Operacion).HasMaxLength(LargoClave);
            e.Property(g => g.AplicaDesde).HasMaxLength(LargoVersion);
            e.Property(g => g.AplicaHasta).HasMaxLength(LargoVersion);
            Enum(e.Property(g => g.Visibilidad), 20);
            Enum(e.Property(g => g.Estado), 20);
            e.HasOne<Contrato>().WithMany().HasForeignKey(g => g.ContratoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(g => g.ContratoId);
        });

        modelBuilder.Entity<RevisionGuia>(e =>
        {
            e.ToTable(Prefijo + "guia_revision", t =>
            {
                t.HasComment("Revisiones inmutables de cada guía: foto completa, autor, fecha, nota y huella (HU 3.7, ATLAS-ADR-0040).");
                t.HasCheckConstraint("ck_contratos_guia_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_contratos_guia_revision_restaurada", "(origen = 'restauracion') = (restaurada_de IS NOT NULL)");
            });
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Titulo).HasMaxLength(LargoTituloGuia);
            e.Property(r => r.Contenido).HasMaxLength(LargoContenidoGuia);
            e.Property(r => r.Operacion).HasMaxLength(LargoClave);
            e.Property(r => r.AplicaDesde).HasMaxLength(LargoVersion);
            e.Property(r => r.AplicaHasta).HasMaxLength(LargoVersion);
            e.Property(r => r.Nota).HasMaxLength(LargoNotaGuia).HasComment("@pii texto libre del autor");
            e.Property(r => r.Huella).HasMaxLength(64).IsFixedLength();
            Enum(e.Property(r => r.Origen), 20);
            Enum(e.Property(r => r.Visibilidad), 20);
            e.HasOne<GuiaContrato>().WithMany().HasForeignKey(r => r.GuiaId).OnDelete(DeleteBehavior.Restrict);
            // Dos guardados a la vez de la misma guía: el segundo choca aquí y responde 409.
            e.HasIndex(r => new { r.GuiaId, r.Numero }).IsUnique();
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

        modelBuilder.Entity<PaqueteContratos>(e =>
        {
            e.ToTable(Prefijo + "paquete", t =>
            {
                t.HasComment("Paquetes de contratos para clientes, con la foto de su última revisión (HU 4.1, ATLAS-ADR-0041).");
                t.HasCheckConstraint("ck_contratos_paquete_revisiones",
                    "revision_actual >= 1 AND (revision_publicada IS NULL OR revision_publicada BETWEEN 1 AND revision_actual)");
                t.HasCheckConstraint("ck_contratos_paquete_estado",
                    "(estado <> 'publicado' OR revision_publicada IS NOT NULL) AND (estado <> 'borrador' OR revision_publicada IS NULL) "
                    + "AND ((estado = 'archivado') = (archivado_en IS NOT NULL))");
            });
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Nombre).HasMaxLength(LargoNombrePaquete);
            e.Property(p => p.Descripcion).HasMaxLength(LargoDescripcion);
            Enum(e.Property(p => p.Estado), 20);
            e.HasIndex(p => p.Estado);
        });

        modelBuilder.Entity<RevisionPaquete>(e =>
        {
            e.ToTable(Prefijo + "paquete_revision", t =>
            {
                t.HasComment("Revisiones inmutables de cada paquete: nombre, descripción, definición, autor, fecha, nota y huella (HU 4.1, ATLAS-ADR-0041).");
                t.HasCheckConstraint("ck_contratos_paquete_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_contratos_paquete_revision_definicion", "jsonb_typeof(definicion) = 'array'");
            });
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Nombre).HasMaxLength(LargoNombrePaquete);
            e.Property(r => r.Descripcion).HasMaxLength(LargoDescripcion);
            e.Property(r => r.Definicion).HasColumnType("jsonb");
            e.Property(r => r.Nota).HasMaxLength(LargoNotaPaquete).HasComment("@pii texto libre del autor");
            e.Property(r => r.Huella).HasMaxLength(64).IsFixedLength();
            e.HasOne<PaqueteContratos>().WithMany().HasForeignKey(r => r.PaqueteId).OnDelete(DeleteBehavior.Restrict);
            // Dos guardados a la vez del mismo paquete: el segundo choca aquí y responde 409. Es también la llave que usará
            // una compartición que fije una revisión (HU 4.3).
            e.HasIndex(r => new { r.PaqueteId, r.Numero }).IsUnique();
        });

        modelBuilder.Entity<ComparticionPaquete>(e =>
        {
            e.ToTable(Prefijo + "comparticion", t =>
            {
                t.HasComment("Paquetes compartidos con organizaciones: revisión fijada, exportar, sandbox y vigencia (HU 4.3, ATLAS-ADR-0043).");
                t.HasCheckConstraint("ck_contratos_comparticion_revision", "revision_fijada IS NULL OR revision_fijada >= 1");
                t.HasCheckConstraint("ck_contratos_comparticion_vigencia", "vigente_hasta IS NULL OR vigente_hasta > vigente_desde");
                t.HasCheckConstraint("ck_contratos_comparticion_revocada", "(revocado_en IS NULL) = (revocado_por IS NULL)");
            });
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasOne<PaqueteContratos>().WithMany().HasForeignKey(c => c.PaqueteId).OnDelete(DeleteBehavior.Restrict);
            // Una por paquete y organización: compartir otra vez una revocada la reactiva.
            e.HasIndex(c => new { c.PaqueteId, c.OrganizacionId }).IsUnique();
            // El portal parte siempre de las comparticiones de la organización de la sesión.
            e.HasIndex(c => c.OrganizacionId);
        });

        // translate() y lower() para la búsqueda sin tildes, como en el catálogo.
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Traducir))!).HasName("translate").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(FuncionesSql.Minusculas))!).HasName("lower").IsBuiltIn();

        // Predicado obligatorio: contratos_comparticion (HU 4.3) solo devuelve filas de las organizaciones del alcance de la
        // petición. Los contratos, versiones, guías y paquetes son internos: el portal llega a ellos solo desde una
        // compartición de la organización de la sesión (ATLAS-ADR-0043).
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
