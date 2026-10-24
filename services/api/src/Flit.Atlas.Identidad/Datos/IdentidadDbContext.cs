using Flit.Atlas.Plataforma.Datos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;

namespace Flit.Atlas.Identidad.Datos;

/// <summary>
/// Tablas <c>identidad_*</c>: las de ASP.NET Core Identity (esquema v3, con passkeys), la sesión del BFF, los eventos de
/// seguridad y las de OpenIddict (<c>identidad_oidc_*</c>, HU 1.3c). Historial propio <c>__ef_migraciones_identidad</c>
/// (ATLAS-ADR-0016).
/// </summary>
public sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> opciones, IAlcanceDatos? alcance = null)
    : IdentityDbContext<UsuarioAtlas, RolIdentity, Guid>(opciones), IContextoConAlcance
{
    /// <summary>
    /// Sus tablas no tienen RLS, pero comparte la conexión con el de la plataforma en <c>TransaccionCompartida</c>: si es
    /// este el que la vuelve a abrir, fija las mismas variables (ATLAS-ADR-0050).
    /// </summary>
    public AlcanceDatos Alcance => AlcanceDelSistema.Activo ? AlcanceDatos.Sistema : alcance?.Actual ?? AlcanceDatos.Sistema;

    public const string Prefijo = "identidad_";
    public const string TablaHistorial = "__ef_migraciones_identidad";

    public DbSet<Sesion> Sesiones => Set<Sesion>();

    public DbSet<EventoSeguridad> EventosSeguridad => Set<EventoSeguridad>();

    public const string TablaEventoSeguridad = Prefijo + "evento_seguridad";

    /// <summary>Intentos fallidos seguidos que bloquean una cuenta local (CA de la HU 1.3b).</summary>
    public const int IntentosAntesDelBloqueo = 10;

    /// <summary>Primer bloqueo; los siguientes duplican la duración (ATLAS-ADR-0022).</summary>
    public static readonly TimeSpan BloqueoInicial = TimeSpan.FromMinutes(15);

    /// <summary>Esquema v3 de Identity, con passkeys (ATLAS-ADR-0004 §1).</summary>
    public static void ConfigurarIdentity(IdentityOptions opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        opciones.User.RequireUniqueEmail = true;
        // Bloqueo de ASP.NET Core Identity para las cuentas locales (HU 1.5); el retroceso progresivo lo aplica
        // VerificadorContrasena sobre este bloqueo base.
        opciones.Lockout.AllowedForNewUsers = true;
        opciones.Lockout.MaxFailedAccessAttempts = IntentosAntesDelBloqueo;
        opciones.Lockout.DefaultLockoutTimeSpan = BloqueoInicial;
        // Contraseñas de externos (HU 1.5, §6.2, NIST SP 800-63B): largo mínimo y revisión contra filtraciones
        // (ValidadorContrasenaFiltrada), sin reglas de composición que empujan a contraseñas previsibles.
        opciones.Password.RequiredLength = LargoMinimoContrasena;
        opciones.Password.RequiredUniqueChars = 4;
        opciones.Password.RequireDigit = false;
        opciones.Password.RequireLowercase = false;
        opciones.Password.RequireUppercase = false;
        opciones.Password.RequireNonAlphanumeric = false;
    }

    /// <summary>Largo mínimo de la contraseña de una cuenta local (§6.2).</summary>
    public const int LargoMinimoContrasena = 12;

    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones
            .UseNpgsql(cadenaConexion, n => n.MigrationsHistoryTable(TablaHistorial, ContextoAtlas.Esquema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(InterceptorAlcanceDatos.Instancia);
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
            e.Property(s => s.Ip).HasComment(PlataformaDbContext.Pii);
            e.HasIndex(s => new { s.UsuarioId, s.RevocadaEn });
            e.HasOne<UsuarioAtlas>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        // OpenIddict con llaves uuid (ATLAS-ADR-0004 §5 y ATLAS-ADR-0024). Las columnas quedan en snake_case.
        builder.UseOpenIddict<Guid>();
        builder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable(Prefijo + "oidc_aplicacion");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable(Prefijo + "oidc_autorizacion");
        builder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable(Prefijo + "oidc_scope");
        builder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable(Prefijo + "oidc_token");

        builder.Entity<EventoSeguridad>(e =>
        {
            e.ToTable(TablaEventoSeguridad, t => t.HasComment("Solo inserción: UPDATE, DELETE y TRUNCATE rechazados."));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Tipo).HasMaxLength(100);
            e.Property(x => x.Ip).HasComment(PlataformaDbContext.Pii);
            e.Property(x => x.AgenteResumen).HasMaxLength(200);
            e.Property(x => x.Datos).HasColumnType("jsonb");
            e.HasIndex(x => new { x.UsuarioId, x.Tipo, x.OcurridoEn });
            e.HasIndex(x => x.OcurridoEn);
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
