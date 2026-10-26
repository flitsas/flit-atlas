using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Dos roles de PostgreSQL por ambiente (ATLAS-ADR-0050, hallazgo F4-SEC-10): el del <b>migrador</b>, dueño del esquema
/// <c>atlas</c> y de sus tablas, y el de la <b>aplicación</b> (<c>flit_atlas</c>), sin propiedad, sin superusuario y sin
/// <c>BYPASSRLS</c>, para que RLS aplique siempre y una inyección SQL no pueda hacer <c>ALTER TABLE … NO FORCE</c>. El
/// migrador concede al de la aplicación lo justo después de migrar (<see cref="ConcederAsync"/>) y la sonda de
/// preparación de atlas-api revisa con qué rol quedó (<see cref="ChequeoRolBaseDeDatos"/>).
/// </summary>
public static partial class RolesBaseDeDatos
{
    /// <summary>Nombre del rol de la aplicación al que el migrador concede permisos (<c>Atlas__BaseDeDatos__RolAplicacion</c>).</summary>
    public const string ClaveRolAplicacion = "Atlas:BaseDeDatos:RolAplicacion";

    /// <summary>Rol de la aplicación por defecto, el que crea el script de flit.</summary>
    public const string RolAplicacionPorDefecto = "flit_atlas";

    /// <summary>Tablas de solo inserción: la aplicación las lee y escribe, pero no las cambia ni las borra.</summary>
    public static IReadOnlyList<string> TablasSoloInsercion { get; } = ["plataforma_auditoria", "identidad_evento_seguridad"];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex NombreRol();

    /// <summary>
    /// Sentencias que concede al rol de la aplicación: uso del esquema, lectura y escritura de las tablas (sin
    /// <c>TRUNCATE</c>, sin el historial de migraciones y sin cambiar ni borrar las de solo inserción), secuencias,
    /// funciones (las de RLS) y los mismos privilegios por defecto para lo que creen las migraciones siguientes.
    /// </summary>
    public static string Sql(string rolAplicacion)
    {
        // El nombre va dentro del SQL (GRANT no admite parámetros): solo minúsculas, dígitos y guion bajo.
        if (string.IsNullOrEmpty(rolAplicacion) || !NombreRol().IsMatch(rolAplicacion))
        {
            throw new ArgumentException("El rol de la aplicación debe ser un identificador simple de PostgreSQL (minúsculas, dígitos y _).", nameof(rolAplicacion));
        }
        var soloInsercion = string.Join(", ", TablasSoloInsercion.Select(t => $"'{t}'"));
        return $"""
            GRANT USAGE ON SCHEMA atlas TO {rolAplicacion};
            DO $permisos$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'atlas' LOOP
                    IF t.tablename LIKE '\_\_ef\_migraciones%' THEN
                        EXECUTE format('REVOKE ALL ON atlas.%I FROM {rolAplicacion}', t.tablename);
                    ELSIF t.tablename IN ({soloInsercion}) THEN
                        EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON atlas.%I FROM {rolAplicacion}', t.tablename);
                        EXECUTE format('GRANT SELECT, INSERT ON atlas.%I TO {rolAplicacion}', t.tablename);
                    ELSE
                        EXECUTE format('REVOKE TRUNCATE ON atlas.%I FROM {rolAplicacion}', t.tablename);
                        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON atlas.%I TO {rolAplicacion}', t.tablename);
                    END IF;
                END LOOP;
            END
            $permisos$;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA atlas TO {rolAplicacion};
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA atlas TO {rolAplicacion};
            ALTER DEFAULT PRIVILEGES IN SCHEMA atlas GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {rolAplicacion};
            ALTER DEFAULT PRIVILEGES IN SCHEMA atlas GRANT USAGE, SELECT ON SEQUENCES TO {rolAplicacion};
            ALTER DEFAULT PRIVILEGES IN SCHEMA atlas GRANT EXECUTE ON FUNCTIONS TO {rolAplicacion};
            """;
    }

    /// <summary>Concede los permisos al rol de la aplicación con la conexión del migrador (idempotente).</summary>
    public static async Task ConcederAsync(DbContext migrador, string rolAplicacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(migrador);
        await migrador.Database.ExecuteSqlRawAsync(Sql(rolAplicacion), ct).ConfigureAwait(false);
    }

    /// <summary>Si existe el rol <paramref name="rol"/> en el clúster.</summary>
    public static async Task<bool> ExisteAsync(DbConnection conexion, string rol, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @rol)";
        var parametro = comando.CreateParameter();
        parametro.ParameterName = "rol";
        parametro.Value = rol;
        comando.Parameters.Add(parametro);
        return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    /// <summary>Lo que importa del rol con que se conectó <paramref name="conexion"/>.</summary>
    public static async Task<EstadoRolBaseDeDatos> RevisarAsync(DbConnection conexion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = """
            SELECT current_user::text, r.rolsuper, r.rolbypassrls,
                   (SELECT count(*) FROM pg_tables t WHERE t.schemaname = 'atlas' AND t.tableowner = current_user)::int
            FROM pg_roles r WHERE r.rolname = current_user
            """;
        await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await lector.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("No se pudo leer el rol de la conexión a la base.");
        }
        return new EstadoRolBaseDeDatos(lector.GetString(0), lector.GetBoolean(1), lector.GetBoolean(2), lector.GetInt32(3));
    }
}

/// <param name="Rol">Nombre del rol (no es secreto).</param>
/// <param name="TablasPropias">Tablas del esquema <c>atlas</c> de las que es dueño.</param>
public sealed record EstadoRolBaseDeDatos(string Rol, bool Superusuario, bool SaltaRls, int TablasPropias)
{
    /// <summary>RLS aplica a este rol sin excepciones y no puede apagarla.</summary>
    public bool SinPrivilegios => !Superusuario && !SaltaRls && TablasPropias == 0;
}

/// <summary>
/// Chequeo de <c>/health/ready</c> (ATLAS-ADR-0050): en dev, qa y pdn atlas-api no queda lista si su rol de base es
/// superusuario, tiene <c>BYPASSRLS</c> o es dueño de tablas de <c>atlas</c>; en local (el AppHost usa el superusuario del
/// contenedor) queda lista con un aviso en el log. Va en la sonda de preparación y no en el arranque para que la de vida y
/// la generación del contrato no dependan de la base.
/// </summary>
public sealed partial class ChequeoRolBaseDeDatos(IServiceScopeFactory alcances, IConfiguration configuracion, ILogger<ChequeoRolBaseDeDatos> log)
    : IHealthCheck
{
    public const string Nombre = "rol-base-de-datos";

    public const string ClaveAmbiente = "Atlas:Ambiente";

    private EstadoRolBaseDeDatos? _estado;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var estado = _estado;
        if (estado is null)
        {
            await using var alcance = alcances.CreateAsyncScope();
            var plataforma = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            await plataforma.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                estado = await RolesBaseDeDatos.RevisarAsync(plataforma.Database.GetDbConnection(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await plataforma.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
            _estado = estado;
            if (!estado.SinPrivilegios)
            {
                RolConPrivilegios(log, estado.Rol, estado.Superusuario, estado.SaltaRls, estado.TablasPropias);
            }
        }
        if (estado.SinPrivilegios)
        {
            return HealthCheckResult.Healthy();
        }
        var ambiente = (configuracion[ClaveAmbiente] ?? string.Empty).Trim().ToLowerInvariant();
        var mensaje = $"Rol de base «{estado.Rol}» con privilegios: debe ser el de la aplicación, sin superusuario, sin BYPASSRLS y sin ser dueño de tablas de atlas (ATLAS-ADR-0050).";
        return ambiente is "local" or "" ? HealthCheckResult.Degraded(mensaje) : HealthCheckResult.Unhealthy(mensaje);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rol de base {Rol} con privilegios (superusuario {Superusuario}, BYPASSRLS {SaltaRls}, dueño de {Tablas} tablas): RLS no es segunda barrera. Fuera de local, atlas-api no queda lista (ATLAS-ADR-0050).")]
    private static partial void RolConPrivilegios(ILogger log, string rol, bool superusuario, bool saltaRls, int tablas);
}
