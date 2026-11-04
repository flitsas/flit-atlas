using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Qué organizaciones puede ver quien usa la base (prompt maestro §7 y §12, ATLAS-ADR-0023). Se aplica en dos capas:
/// el <b>predicado obligatorio</b> (filtro global de EF Core en toda entidad con <c>organizacion_id</c>) y <b>RLS</b> de
/// PostgreSQL con las variables de sesión <c>atlas.alcance_total</c> y <c>atlas.organizaciones</c>, que fija
/// <see cref="InterceptorAlcanceDatos"/> al abrir cada conexión.
/// </summary>
/// <param name="Total">Ve todas las organizaciones (empleados y los procesos del sistema que lo piden).</param>
/// <param name="Organizaciones">Organizaciones visibles cuando no es total (un externo: la suya).</param>
public sealed record AlcanceDatos(bool Total, IReadOnlyList<Guid> Organizaciones)
{
    /// <summary>
    /// Procesos del sistema: migrador, trabajos en segundo plano (sin petición HTTP) y lo que lo pide explícitamente con
    /// <see cref="AlcanceDelSistema"/> o <see cref="ExtensionesAlcanceDatos.ConAlcanceDelSistemaSinSesion"/> (ingreso antes de
    /// tener sesión, proxy del sandbox, feed .ics). Una petición sin sesión recibe <see cref="Ninguno"/> (ATLAS-ADR-0050).
    /// </summary>
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
/// organización y una petición sin sesión no ve nada, salvo que pida el del sistema (ATLAS-ADR-0050). Sin implementación
/// registrada (migrador, pruebas, diseño), el alcance es <see cref="AlcanceDatos.Sistema"/>.
/// </summary>
public interface IAlcanceDatos
{
    AlcanceDatos Actual { get; }
}

/// <summary>
/// Un contexto de EF Core que fija las variables de RLS al abrir su conexión (<see cref="InterceptorAlcanceDatos"/>). Lo
/// implementan <see cref="ContextoAtlas"/> y los contextos que comparten conexión con ellos (el de la identidad, en la
/// transacción compartida): si el que abre la conexión no las fijara, el otro consultaría sin ellas (ATLAS-ADR-0050).
/// </summary>
public interface IContextoConAlcance
{
    AlcanceDatos Alcance { get; }
}

/// <summary>
/// Pedido explícito del alcance del sistema para un tramo de código (ATLAS-ADR-0050): <c>using var _ =
/// AlcanceDelSistema.Abrir();</c>. Vale para ese flujo asíncrono y lo que llame; al cerrarlo vuelve el anterior. Es para
/// lo que corre dentro de una petición sin sesión pero con su propia autenticación (la devolución de Entra antes de crear
/// la sesión); en un endpoint se prefiere <see cref="ExtensionesAlcanceDatos.ConAlcanceDelSistemaSinSesion"/>.
/// </summary>
public static class AlcanceDelSistema
{
    private static readonly AsyncLocal<bool> Pedido = new();

    /// <summary>Si el tramo en curso pidió el alcance del sistema.</summary>
    public static bool Activo => Pedido.Value;

    public static IDisposable Abrir()
    {
        var anterior = Pedido.Value;
        Pedido.Value = true;
        return new Cierre(anterior);
    }

    private sealed class Cierre(bool anterior) : IDisposable
    {
        public void Dispose() => Pedido.Value = anterior;
    }
}

/// <summary>
/// Metadata de un endpoint sin sesión que se autentica por su cuenta (credenciales, enlace de un solo uso, token de la URL,
/// llave del sandbox) y necesita el alcance del sistema para encontrar a quién pertenece lo que trae (ATLAS-ADR-0050).
/// Con sesión no cambia nada: manda la de la persona.
/// </summary>
public sealed class AlcanceDelSistemaSinSesion
{
    public static AlcanceDelSistemaSinSesion Instancia { get; } = new();

    private AlcanceDelSistemaSinSesion()
    {
    }
}

public static class ExtensionesAlcanceDatos
{
    /// <summary>
    /// Este endpoint (o grupo), llamado sin sesión, usa el alcance del sistema. Agregarlo es una decisión de seguridad:
    /// el endpoint debe autenticar lo que recibe antes de devolver o cambiar datos (ATLAS-ADR-0050).
    /// </summary>
    public static TBuilder ConAlcanceDelSistemaSinSesion<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(e => e.Metadata.Add(AlcanceDelSistemaSinSesion.Instancia));
        return builder;
    }
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
        var alcance = (datos.Context as IContextoConAlcance)?.Alcance ?? AlcanceDatos.Ninguno;
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
