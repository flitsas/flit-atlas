using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Qué cambió al aplicar el catálogo a la base.</summary>
public sealed record ResultadoSembrado(int RolesCreados, int RolesActualizados, int PermisosCreados, int PermisosRetirados, int ConcesionesCreadas, int ConcesionesRetiradas)
{
    public bool SinCambios => RolesCreados + RolesActualizados + PermisosCreados + PermisosRetirados + ConcesionesCreadas + ConcesionesRetiradas == 0;
}

/// <summary>
/// Aplica a la base, de forma idempotente, los roles de <see cref="RolesAtlas"/> y la matriz rol × permiso del
/// <see cref="CatalogoPermisos"/> (ATLAS-ADR-0023). Lo corre el migrador después de las migraciones: la base queda
/// igual al código. Los roles que ya no estén en el código no se borran (pueden tener asignaciones e historia); pierden
/// sus permisos.
/// </summary>
public static class SembradorPermisos
{
    public static async Task<ResultadoSembrado> SembrarAsync(PlataformaDbContext plataforma, CatalogoPermisos catalogo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plataforma);
        ArgumentNullException.ThrowIfNull(catalogo);
        var ahora = TimeProvider.System.GetUtcNow();

        await using var transaccion = await plataforma.Database.BeginTransactionAsync(ct);
        // Dos migradores a la vez (p. ej. dos despliegues) no se pisan.
        await plataforma.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended('atlas.plataforma.sembrado_permisos', 0))", ct);

        var roles = await plataforma.Roles.ToDictionaryAsync(r => r.Codigo, StringComparer.Ordinal, ct);
        int rolesCreados = 0, rolesActualizados = 0;
        foreach (var definicion in catalogo.Roles)
        {
            if (!roles.TryGetValue(definicion.Codigo, out var rol))
            {
                rol = new Rol { Codigo = definicion.Codigo, Nombre = definicion.Nombre, Tipo = definicion.Tipo, Descripcion = definicion.Descripcion };
                plataforma.Roles.Add(rol);
                roles[rol.Codigo] = rol;
                rolesCreados++;
            }
            else if (rol.Nombre != definicion.Nombre || rol.Tipo != definicion.Tipo || rol.Descripcion != definicion.Descripcion)
            {
                rol.Nombre = definicion.Nombre;
                rol.Tipo = definicion.Tipo;
                rol.Descripcion = definicion.Descripcion;
                rolesActualizados++;
            }
        }

        var permisos = await plataforma.Permisos.ToDictionaryAsync(p => p.Codigo, StringComparer.Ordinal, ct);
        int permisosCreados = 0, permisosRetirados = 0;
        foreach (var registrado in catalogo.Permisos)
        {
            var codigo = registrado.Permiso.Codigo;
            if (!permisos.TryGetValue(codigo, out var permiso))
            {
                plataforma.Permisos.Add(new Permiso
                {
                    Codigo = codigo, Modulo = registrado.Manifiesto.Modulo, Descripcion = registrado.Permiso.Descripcion, ActualizadoEn = ahora,
                });
                permisosCreados++;
            }
            else if (permiso.Modulo != registrado.Manifiesto.Modulo || permiso.Descripcion != registrado.Permiso.Descripcion)
            {
                permiso.Modulo = registrado.Manifiesto.Modulo;
                permiso.Descripcion = registrado.Permiso.Descripcion;
                permiso.ActualizadoEn = ahora;
            }
        }
        foreach (var sobrante in permisos.Values.Where(p => !catalogo.Existe(p.Codigo)))
        {
            plataforma.Permisos.Remove(sobrante);
            permisosRetirados++;
        }
        await plataforma.SaveChangesAsync(ct);

        var deseadas = roles.Values
            .SelectMany(r => catalogo.PermisosDe(r.Codigo).Select(p => (r.Id, p)))
            .ToHashSet();
        var actuales = await plataforma.RolesPermisos.ToListAsync(ct);
        var retiradas = actuales.Where(rp => !deseadas.Contains((rp.RolId, rp.PermisoCodigo))).ToList();
        plataforma.RolesPermisos.RemoveRange(retiradas);
        var existentes = actuales.Select(rp => (rp.RolId, rp.PermisoCodigo)).ToHashSet();
        var nuevas = deseadas.Where(d => !existentes.Contains(d)).ToList();
        plataforma.RolesPermisos.AddRange(nuevas.Select(d => new RolPermiso { RolId = d.Id, PermisoCodigo = d.p }));
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return new ResultadoSembrado(rolesCreados, rolesActualizados, permisosCreados, permisosRetirados, nuevas.Count, retiradas.Count);
    }
}
