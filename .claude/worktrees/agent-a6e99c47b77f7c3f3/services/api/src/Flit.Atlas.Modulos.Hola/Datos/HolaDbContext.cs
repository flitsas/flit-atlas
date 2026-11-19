using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Atlas.Modulos.Hola.Datos;

/// <summary>
/// Un saludo de una organización. Tiene <c>organizacion_id</c>: lo filtra el predicado obligatorio y, en la base, RLS
/// (ATLAS-ADR-0023).
/// </summary>
public sealed class Saludo : Entidad
{
    public required Guid OrganizacionId { get; init; }

    /// <summary>Texto libre: puede traer datos personales, por eso es <c>@pii</c> y nunca va a los logs.</summary>
    public required string Texto { get; set; }

    /// <summary>Persona que lo escribió (id de <c>plataforma_persona</c>).</summary>
    public required Guid AutorPersonaId { get; init; }
}

/// <summary>
/// Tablas <c>hola_*</c> del módulo de ejemplo, con su historial <c>__ef_migraciones_hola</c> (ATLAS-ADR-0016 y
/// ATLAS-ADR-0026). Solo conoce sus entidades: nunca mapea tablas del núcleo ni de otro módulo.
/// </summary>
/// <param name="alcance">Alcance de datos de la petición (lo registra la identidad); sin él, el del sistema.</param>
public sealed class HolaDbContext(DbContextOptions<HolaDbContext> opciones, IAlcanceDatos? alcance = null)
    : ContextoAtlas(opciones, alcance)
{
    public const string Prefijo = ModuloHola.IdModulo + "_";
    public static readonly string TablaHistorial = Plataforma.Modulos.ExtensionesModulos.TablaHistorial(ModuloHola.IdModulo);

    public const int LargoTexto = 280;

    /// <summary>Comentario de las columnas con datos personales (prompt maestro §9).</summary>
    public const string Pii = PlataformaDbContext.Pii;

    public DbSet<Saludo> Saludos => Set<Saludo>();

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion) =>
        ConfigurarOpciones(opciones, cadenaConexion, TablaHistorial);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Saludo>(e =>
        {
            e.ToTable(Prefijo + "saludo", t => t.HasComment("Módulo de ejemplo «hola» (docs/como-crear-un-modulo.md)."));
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Texto).HasMaxLength(LargoTexto).HasComment(Pii);
            // El listado es por organización, del más reciente al más viejo.
            e.HasIndex(s => new { s.OrganizacionId, s.CreadoEn });
        });

        // Predicado obligatorio: toda entidad con OrganizacionId solo devuelve filas del alcance de la petición.
        AplicarPredicadoDeOrganizacion(modelBuilder);
    }
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>: generar una migración no necesita una base real.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Modulos.Hola -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<HolaDbContext>
{
    public HolaDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<HolaDbContext>();
        HolaDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new HolaDbContext(opciones.Options);
    }
}
