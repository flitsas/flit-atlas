using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Base de los <see cref="DbContext"/> de Atlas (núcleo y módulos). Todos viven en el esquema <c>atlas</c>
/// (prompt maestro A-06) y cada uno tiene su propio historial de migraciones (ATLAS-ADR-0016).
/// </summary>
/// <param name="alcance">Alcance de datos de la petición; sin él (migrador, pruebas, diseño) es <see cref="AlcanceDatos.Sistema"/>.</param>
public abstract class ContextoAtlas(DbContextOptions opciones, IAlcanceDatos? alcance = null) : DbContext(opciones), IContextoConAlcance
{
    public const string Esquema = "atlas";

    /// <summary>Columna que marca los datos de una organización: predicado obligatorio y RLS (ATLAS-ADR-0023).</summary>
    public const string PropiedadOrganizacion = "OrganizacionId";

    /// <summary>Hora actual en UTC; las pruebas la pueden fijar.</summary>
    protected internal TimeProvider Reloj { get; set; } = TimeProvider.System;

    /// <summary>
    /// Organizaciones que puede ver quien usa este contexto; se lee en cada consulta y al abrir la conexión. Un tramo que
    /// abrió <see cref="AlcanceDelSistema"/> ve todo (ATLAS-ADR-0050).
    /// </summary>
    public AlcanceDatos Alcance => AlcanceDelSistema.Activo ? AlcanceDatos.Sistema : alcance?.Actual ?? AlcanceDatos.Sistema;

    /// <summary>
    /// Opciones comunes de los contextos de los módulos (ATLAS-ADR-0026): Npgsql con su historial de migraciones propio en
    /// el esquema <c>atlas</c>, snake_case y el interceptor que fija el alcance de datos de RLS en cada conexión. Es lo
    /// mismo que hace <see cref="PlataformaDbContext.Configurar"/> para el núcleo.
    /// </summary>
    /// <param name="tablaHistorial"><c>__ef_migraciones_&lt;id&gt;</c>.</param>
    public static DbContextOptionsBuilder ConfigurarOpciones(DbContextOptionsBuilder opciones, string cadenaConexion, string tablaHistorial)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentException.ThrowIfNullOrWhiteSpace(tablaHistorial);
        return opciones
            .UseNpgsql(cadenaConexion, n => n.MigrationsHistoryTable(tablaHistorial, Esquema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(InterceptorAlcanceDatos.Instancia);
    }

    // EF Core evalúa estos miembros con el contexto de cada consulta (los filtros globales los parametrizan).
    private bool AlcanceTotal => Alcance.Total;

    private Guid[] OrganizacionesVisibles => Alcance.ComoArreglo();

    /// <summary>
    /// Predicado obligatorio: toda entidad con <see cref="PropiedadOrganizacion"/> (y la propia organización, por su id)
    /// solo devuelve filas de las organizaciones del <see cref="Alcance"/>. Las filas sin organización solo las ve un
    /// alcance total. Es la primera capa; RLS en PostgreSQL es la segunda (defensa en profundidad).
    /// </summary>
    protected void AplicarPredicadoDeOrganizacion(ModelBuilder modelo, params Type[] organizacionPorId)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(organizacionPorId);
        var contexto = Expression.Constant(this);
        var total = Expression.Property(contexto, typeof(ContextoAtlas).GetProperty(nameof(AlcanceTotal), BindingFlags.NonPublic | BindingFlags.Instance)!);
        var visibles = Expression.Property(contexto, typeof(ContextoAtlas).GetProperty(nameof(OrganizacionesVisibles), BindingFlags.NonPublic | BindingFlags.Instance)!);
        var contiene = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(Guid));

        foreach (var entidad in modelo.Model.GetEntityTypes().Where(e => e.BaseType is null && !e.IsOwned()))
        {
            var nombre = organizacionPorId.Contains(entidad.ClrType) ? "Id" : PropiedadOrganizacion;
            var propiedad = entidad.ClrType.GetProperty(nombre);
            if (propiedad is null || (propiedad.PropertyType != typeof(Guid) && propiedad.PropertyType != typeof(Guid?)))
            {
                continue;
            }
            var fila = Expression.Parameter(entidad.ClrType, "fila");
            var columna = Expression.Property(fila, propiedad);
            Expression visible = propiedad.PropertyType == typeof(Guid)
                ? Expression.Call(contiene, visibles, columna)
                : Expression.AndAlso(
                    Expression.Property(columna, nameof(Nullable<Guid>.HasValue)),
                    Expression.Call(contiene, visibles, Expression.Property(columna, nameof(Nullable<Guid>.Value))));
            modelo.Entity(entidad.ClrType).HasQueryFilter(Expression.Lambda(Expression.OrElse(total, visible), fila));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SellarFechas();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SellarFechas();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SellarFechas()
    {
        var ahora = Reloj.GetUtcNow();
        foreach (var entrada in ChangeTracker.Entries<Entidad>())
        {
            if (entrada.State == EntityState.Added)
            {
                entrada.Entity.CreadoEn = ahora;
                entrada.Entity.ActualizadoEn = ahora;
            }
            else if (entrada.State == EntityState.Modified)
            {
                entrada.Entity.ActualizadoEn = ahora;
                entrada.Property(e => e.CreadoEn).IsModified = false;
            }
        }
    }
}
