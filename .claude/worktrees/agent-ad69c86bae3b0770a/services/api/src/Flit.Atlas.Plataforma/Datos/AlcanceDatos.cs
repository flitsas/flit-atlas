using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Qué organizaciones puede ver quien usa la base (prompt maestro §7 y §12, ATLAS-ADR-0023). Se aplica en dos capas:
/// el <b>predicado obligatorio</b> (filtro global de EF Core en toda entidad con <c>organizacion_id</c>) y <b>RLS</b> de
/// PostgreSQL con las variables de sesión <c>atlas.alcance_total</c> y <c>atlas.organizaciones</c>, que fija
/// <see cref="InterceptorAlcanceDatos"/> al abrir cada conexión.
/// </summary>
/// <param name="Total">Ve todas las organizaciones (empleados, el sistema y los procesos sin sesión).</param>
/// <param name="Organizaciones">Organizaciones visibles cuando no es total (un externo: la suya).</param>
public sealed record AlcanceDatos(bool Total, IReadOnlyList<Guid> Organizaciones)
{
    /// <summary>Procesos del sistema (migrador, trabajos en segundo plano, ingreso antes de tener sesión).</summary>
    public static AlcanceDatos Sistema { get; } = new(true, []);

    /// <summary>Un empleado de FLIT: ve todas las organizaciones (§7.2); sus permisos limitan el resto.</summary>
    public static AlcanceDatos Interno { get; } = new(true, []);

    /// <summary>Nada: es lo que ve una conexión sin variables (RLS niega por defecto).</summary>
    public static AlcanceDatos Ninguno { get; } = new(false, []);

    public static AlcanceDatos DeOrganizaciones(params Guid[] organizaciones) => new(false, organizaciones);

    internal Guid[] ComoArreglo() => [.. Organizaciones];
}

/// <summary>
/// Alcance de datos de la petición en curso. La identidad lo implementa con la sesión: un externo ve solo su
/// organización. Sin implementación registrada, el alcance es <see cref="AlcanceDatos.Sistema"/>.
/// </summary>
public interface IAlcanceDatos
{
    AlcanceDatos Actual { get; }
}

/// <summary>
/// Fija <c>atlas.alcance_total</c> y <c>atlas.organizaciones</c> en cada conexión que abre un <see cref="ContextoAtlas"/>,
/// para que las políticas RLS filtren con el alcance de quien consulta. Las variables son de la sesión de PostgreSQL y
/// se escriben siempre al abrir: el pool de Npgsql las limpia al devolver la conexión (DISCARD ALL) y, aunque no lo
/// hiciera, la siguiente apertura las sobrescribe.
/// </summary>
public sealed class InterceptorAlcanceDatos : DbConnectionInterceptor
{
    public static InterceptorAlcanceDatos Instancia { get; } = new();

    public const string VariableTotal = "atlas.alcance_total";
    public const string VariableOrganizaciones = "atlas.organizaciones";

    private const string Sql =
        $"SELECT set_config('{VariableTotal}', @total, false), set_config('{VariableOrganizaciones}', @organizaciones, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var comando = Comando(connection, eventData);
        comando.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var comando = Comando(connection, eventData);
        await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand Comando(DbConnection conexion, ConnectionEndEventData datos)
    {
        var alcance = (datos.Context as ContextoAtlas)?.Alcance ?? AlcanceDatos.Ninguno;
        var comando = conexion.CreateCommand();
        comando.CommandText = Sql;
        Agregar(comando, "total", alcance.Total ? "on" : "off");
        Agregar(comando, "organizaciones", string.Join(',', alcance.Organizaciones.Select(o => o.ToString("D", CultureInfo.InvariantCulture))));
        return comando;
    }

    private static void Agregar(DbCommand comando, string nombre, string valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }
}
