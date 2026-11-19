using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Tablas <c>plataforma_*</c> del núcleo (prompt maestro §9), con su historial de migraciones
/// <c>__ef_migraciones_plataforma</c> (ATLAS-ADR-0016).
/// </summary>
public sealed class PlataformaDbContext(DbContextOptions<PlataformaDbContext> opciones) : ContextoAtlas(opciones)
{
    public const string Prefijo = "plataforma_";
    public const string TablaHistorial = "__ef_migraciones_plataforma";

    /// <summary>Comentario de las columnas con datos personales: se excluyen de los logs (prompt maestro §9).</summary>
    public const string Pii = "@pii";

    public DbSet<Organizacion> Organizaciones => Set<Organizacion>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<AsignacionRol> AsignacionesRol => Set<AsignacionRol>();
    public DbSet<MapeoGrupoEntra> MapeosGrupoEntra => Set<MapeoGrupoEntra>();
    public DbSet<Invitacion> Invitaciones => Set<Invitacion>();
    public DbSet<AceptacionTerminos> AceptacionesTerminos => Set<AceptacionTerminos>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<PreferenciaNotificacion> PreferenciasNotificacion => Set<PreferenciaNotificacion>();
    public DbSet<SuscripcionPush> SuscripcionesPush => Set<SuscripcionPush>();
    public DbSet<EventoAuditoria> Auditoria => Set<EventoAuditoria>();
    public DbSet<Modulo> Modulos => Set<Modulo>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<MensajeOutbox> Outbox => Set<MensajeOutbox>();
    public DbSet<MensajeInbox> Inbox => Set<MensajeInbox>();

    /// <summary>Opciones comunes: Npgsql, snake_case y el historial de migraciones propio.</summary>
    public static void Configurar(DbContextOptionsBuilder opciones, string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones
            .UseNpgsql(cadenaConexion, ConfigurarNpgsql)
            .UseSnakeCaseNamingConvention();
    }

    public static void ConfigurarNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);
        npgsql.MigrationsHistoryTable(TablaHistorial, Esquema);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Organizacion>(e =>
        {
            e.ToTable(Prefijo + "organizacion");
            e.Property(o => o.Nombre).HasMaxLength(200);
            e.HasIndex(o => o.Nombre).IsUnique();
            Enum(e.Property(o => o.Tipo));
            Enum(e.Property(o => o.Estado));
        });

        modelBuilder.Entity<Persona>(e =>
        {
            e.ToTable(Prefijo + "persona");
            Enum(e.Property(p => p.Tipo));
            Enum(e.Property(p => p.Estado));
            e.Property(p => p.Nombre).HasMaxLength(200).HasComment(Pii);
            e.Property(p => p.Correo).HasMaxLength(320).HasComment(Pii);
            e.Property(p => p.Telefono).HasMaxLength(50).HasComment(Pii);
            e.Property(p => p.Cargo).HasMaxLength(200);
            e.Property(p => p.Area).HasMaxLength(200);
            e.HasIndex(p => p.Correo).IsUnique();
            e.HasIndex(p => p.EntraOid).IsUnique().HasFilter("entra_oid IS NOT NULL");
            e.HasIndex(p => p.OrganizacionId);
            e.HasOne<Organizacion>().WithMany().HasForeignKey(p => p.OrganizacionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Rol>(e =>
        {
            e.ToTable(Prefijo + "rol");
            e.Property(r => r.Codigo).HasMaxLength(100);
            e.Property(r => r.Nombre).HasMaxLength(200);
            e.Property(r => r.Descripcion).HasMaxLength(1000);
            e.HasIndex(r => r.Codigo).IsUnique();
            Enum(e.Property(r => r.Tipo));
        });

        modelBuilder.Entity<AsignacionRol>(e =>
        {
            e.ToTable(Prefijo + "asignacion_rol");
            Enum(e.Property(a => a.AlcanceTipo));
            Enum(e.Property(a => a.Origen));
            e.Property(a => a.AlcanceId).HasMaxLength(200);
            e.HasIndex(a => a.PersonaId);
            e.HasIndex(a => a.OrganizacionId);
            e.HasOne<Persona>().WithMany().HasForeignKey(a => a.PersonaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Rol>().WithMany().HasForeignKey(a => a.RolId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organizacion>().WithMany().HasForeignKey(a => a.OrganizacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Persona>().WithMany().HasForeignKey(a => a.AsignadoPor).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MapeoGrupoEntra>(e =>
        {
            e.ToTable(Prefijo + "mapeo_grupo_entra");
            e.Property(m => m.GrupoNombre).HasMaxLength(200);
            e.HasIndex(m => new { m.GrupoId, m.RolId }).IsUnique();
            e.HasOne<Rol>().WithMany().HasForeignKey(m => m.RolId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invitacion>(e =>
        {
            e.ToTable(Prefijo + "invitacion");
            Enum(e.Property(i => i.Estado));
            e.Property(i => i.Correo).HasMaxLength(320).HasComment(Pii);
            e.Property(i => i.Alcance).HasColumnType("jsonb");
            e.Property(i => i.PermisosExtra).HasColumnType("jsonb");
            e.Property(i => i.TokenHash).HasMaxLength(32);
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.HasIndex(i => new { i.OrganizacionId, i.Estado });
            e.HasOne<Organizacion>().WithMany().HasForeignKey(i => i.OrganizacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Rol>().WithMany().HasForeignKey(i => i.RolId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Persona>().WithMany().HasForeignKey(i => i.InvitadoPor).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Persona>().WithMany().HasForeignKey(i => i.PersonaId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AceptacionTerminos>(e =>
        {
            e.ToTable(Prefijo + "aceptacion_terminos");
            Enum(e.Property(a => a.Documento));
            e.Property(a => a.Version).HasMaxLength(50);
            e.Property(a => a.Ip).HasComment(Pii);
            e.Property(a => a.AgenteUsuario).HasMaxLength(500);
            e.HasIndex(a => new { a.PersonaId, a.Documento, a.Version }).IsUnique();
            e.HasOne<Persona>().WithMany().HasForeignKey(a => a.PersonaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notificacion>(e =>
        {
            e.ToTable(Prefijo + "notificacion");
            e.Property(n => n.Tipo).HasMaxLength(100);
            e.Property(n => n.Titulo).HasMaxLength(200);
            e.Property(n => n.Cuerpo).HasMaxLength(2000);
            e.Property(n => n.Enlace).HasMaxLength(500);
            e.HasIndex(n => new { n.PersonaId, n.LeidaEn });
            // La bandeja se lista por persona, de la más reciente a la más vieja (HU 1.6).
            e.HasIndex(n => new { n.PersonaId, n.CreadoEn });
            e.HasOne<Persona>().WithMany().HasForeignKey(n => n.PersonaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PreferenciaNotificacion>(e =>
        {
            e.ToTable(Prefijo + "preferencia_notificacion");
            e.HasKey(p => new { p.PersonaId, p.Tipo, p.Canal });
            e.Property(p => p.Tipo).HasMaxLength(100);
            Enum(e.Property(p => p.Canal));
            e.HasOne<Persona>().WithMany().HasForeignKey(p => p.PersonaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SuscripcionPush>(e =>
        {
            e.ToTable(Prefijo + "suscripcion_push");
            e.Property(s => s.Endpoint).HasMaxLength(2000).HasComment(Pii);
            e.Property(s => s.P256dh).HasMaxLength(200);
            e.Property(s => s.Auth).HasMaxLength(100);
            e.Property(s => s.Dispositivo).HasMaxLength(200);
            e.HasIndex(s => s.Endpoint).IsUnique();
            e.HasIndex(s => s.PersonaId);
            e.HasOne<Persona>().WithMany().HasForeignKey(s => s.PersonaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventoAuditoria>(e =>
        {
            e.ToTable(TablaAuditoria, t => t.HasComment("Solo inserción: UPDATE, DELETE y TRUNCATE rechazados."));
            e.Property(a => a.Id).ValueGeneratedNever();
            Enum(e.Property(a => a.ActorTipo));
            e.Property(a => a.ActorId).HasMaxLength(200);
            e.Property(a => a.Accion).HasMaxLength(100);
            e.Property(a => a.RecursoTipo).HasMaxLength(100);
            e.Property(a => a.RecursoId).HasMaxLength(200);
            e.Property(a => a.Version).HasMaxLength(100);
            e.Property(a => a.Ip).HasComment(Pii);
            e.Property(a => a.Datos).HasColumnType("jsonb");
            e.Property(a => a.CorrelacionId).HasMaxLength(100);
            e.HasIndex(a => a.OcurridoEn);
            e.HasIndex(a => new { a.RecursoTipo, a.RecursoId });
            e.HasIndex(a => new { a.OrganizacionId, a.OcurridoEn });
        });

        modelBuilder.Entity<Modulo>(e =>
        {
            e.ToTable(Prefijo + "modulo");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasMaxLength(50);
        });

        modelBuilder.Entity<Configuracion>(e =>
        {
            e.ToTable(Prefijo + "configuracion");
            e.HasKey(c => c.Clave);
            e.Property(c => c.Clave).HasMaxLength(200);
            e.Property(c => c.Valor).HasColumnType("jsonb");
        });

        modelBuilder.AgregarMensajeria(excluirDeMigraciones: false);

        // Las claves uuid v7 las genera la aplicación (Entidad.Id); la base no las inventa.
        foreach (var entidad in modelBuilder.Model.GetEntityTypes())
        {
            var id = entidad.FindProperty("Id");
            if (id?.ClrType == typeof(Guid))
            {
                id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    public const string TablaAuditoria = Prefijo + "auditoria";

    /// <summary>Enum como texto snake_case, con el largo de su valor más largo.</summary>
    private static void Enum<TEnum>(PropertyBuilder<TEnum> propiedad) where TEnum : struct, System.Enum =>
        propiedad.HasConversion(new ConvertidorEnumSnake<TEnum>()).HasMaxLength(ConvertidorEnumSnake<TEnum>.Largo);
}
