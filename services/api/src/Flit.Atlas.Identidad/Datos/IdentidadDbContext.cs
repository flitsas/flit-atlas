using Flit.Atlas.Plataforma.Datos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Datos;

/// <summary>
/// Tablas <c>identidad_*</c>: las de ASP.NET Core Identity (esquema v3, con passkeys) y la sesión del BFF.
/// Historial propio <c>__ef_migraciones_identidad</c> (ATLAS-ADR-0016). Las tablas de OpenIddict llegan en la HU 1.3c.
/// </summary>
public sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> opciones)
    : IdentityDbContext<UsuarioAtlas, RolIdentity, Guid>(opciones)
{
    public const string Prefijo = "identidad_";
    public const string TablaHistorial = "__ef_migraciones_identidad";

    public DbSet<Sesion> Sesiones => Set<Sesion>();


    /// <summary>Esquema v3 de Identity, con passkeys (ATLAS-ADR-0004 §1).</summary>
    public static void ConfigurarIdentity(IdentityOptions opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        opciones.User.RequireUniqueEmail = true;
    }

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones
            .UseNpgsql(cadenaConexion, n => n.MigrationsHistoryTable(TablaHistorial, ContextoAtlas.Esquema))
            .UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity arma su modelo según IdentityOptions.Stores.SchemaVersion del contenedor: todo host que use este
        // contexto llama a ConfigurarIdentity (API, migrador, pruebas y la fábrica de diseño).
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(ContextoAtlas.Esquema);

        builder.Entity<UsuarioAtlas>(e =>
        {
            e.ToTable(Prefijo + "usuario");
            e.Property(u => u.Id).ValueGeneratedNever();
            e.Property(u => u.Email).HasComment(PlataformaDbContext.Pii);
            e.Property(u => u.NormalizedEmail).HasComment(PlataformaDbContext.Pii);
            e.Property(u => u.UserName).HasComment(PlataformaDbContext.Pii);
            e.Property(u => u.NormalizedUserName).HasComment(PlataformaDbContext.Pii);
            e.Property(u => u.PhoneNumber).HasComment(PlataformaDbContext.Pii);
            e.HasIndex(u => u.PersonaId).IsUnique();
        });
        builder.Entity<RolIdentity>().ToTable(Prefijo + "rol_identity");
        builder.Entity<IdentityUserRole<Guid>>().ToTable(Prefijo + "usuario_rol_identity");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable(Prefijo + "usuario_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable(Prefijo + "usuario_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable(Prefijo + "usuario_token");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable(Prefijo + "rol_claim");
        builder.Entity<IdentityUserPasskey<Guid>>().ToTable(Prefijo + "usuario_passkey");

        builder.Entity<Sesion>(e =>
        {
            e.ToTable(Prefijo + "sesion");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.AgenteResumen).HasMaxLength(200);
            e.Property(s => s.MotivoRevocacion).HasMaxLength(200);
            e.HasIndex(s => new { s.UsuarioId, s.RevocadaEn });
            e.HasOne<UsuarioAtlas>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Solo para <c>dotnet ef migrations add</c>.</summary>
internal sealed class FabricaDeDisenoIdentidad : IDesignTimeDbContextFactory<IdentidadDbContext>
{
    public IdentidadDbContext CreateDbContext(string[] args)
    {
        var servicios = new ServiceCollection().Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity).BuildServiceProvider();
        var opciones = new DbContextOptionsBuilder<IdentidadDbContext>().UseApplicationServiceProvider(servicios);
        IdentidadDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new IdentidadDbContext(opciones.Options);
    }
}
