using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Una sola transacción de PostgreSQL para el contexto de la plataforma y el de la identidad (ATLAS-ADR-0025): aceptar
/// una invitación crea la persona, el usuario de Identity, sus factores, la evidencia de los términos, las asignaciones
/// de rol y la auditoría, y todo queda o nada queda. Los dos contextos usan la misma conexión (la de la plataforma, que
/// fija las variables de RLS al abrirse).
/// </summary>
internal static class TransaccionCompartida
{
    /// <param name="trabajo">Devuelve <c>Confirmar = false</c> para deshacer todo (p. ej. un código TOTP que no coincide).</param>
    public static async Task<T> EjecutarAsync<T>(
        PlataformaDbContext plataforma, IdentidadDbContext identidad, Func<Task<(bool Confirmar, T Valor)>> trabajo, CancellationToken ct)
    {
        await plataforma.Database.OpenConnectionAsync(ct);
        try
        {
            var conexion = plataforma.Database.GetDbConnection();
            if (!ReferenceEquals(identidad.Database.GetDbConnection(), conexion))
            {
                identidad.Database.SetDbConnection(conexion, contextOwnsConnection: false);
            }
            await using var transaccion = await plataforma.Database.BeginTransactionAsync(ct);
            await identidad.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
            try
            {
                var (confirmar, valor) = await trabajo();
                if (confirmar)
                {
                    await transaccion.CommitAsync(ct);
                }
                else
                {
                    await transaccion.RollbackAsync(ct);
                    // Lo que se «guardó» dentro de la transacción ya no existe: los contextos no lo recuerdan.
                    plataforma.ChangeTracker.Clear();
                    identidad.ChangeTracker.Clear();
                }
                return valor;
            }
            finally
            {
                await identidad.Database.UseTransactionAsync(null, CancellationToken.None);
            }
        }
        finally
        {
            await plataforma.Database.CloseConnectionAsync();
        }
    }
}
