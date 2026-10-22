using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Base de los <see cref="DbContext"/> de Atlas (núcleo y módulos). Todos viven en el esquema <c>atlas</c>
/// (prompt maestro A-06) y cada uno tiene su propio historial de migraciones (ATLAS-ADR-0016).
/// </summary>
public abstract class ContextoAtlas(DbContextOptions opciones) : DbContext(opciones)
{
    public const string Esquema = "atlas";

    /// <summary>Hora actual en UTC; las pruebas la pueden fijar.</summary>
    protected internal TimeProvider Reloj { get; set; } = TimeProvider.System;

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
