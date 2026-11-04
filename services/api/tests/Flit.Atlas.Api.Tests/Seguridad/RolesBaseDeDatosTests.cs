using Flit.Atlas.Plataforma.Datos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// F4-SEC-10 (ATLAS-ADR-0050): atlas-api corre con un rol sin superusuario, sin BYPASSRLS y sin ser dueño de las tablas,
/// así RLS aplica siempre y una inyección SQL no puede apagarla; el migrador concede lo justo.
/// </summary>
public class RolesBaseDeDatosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> AbrirComoAplicacionAsync()
    {
        var conexion = new NpgsqlConnection(entorno.ComoAplicacion());
        await conexion.OpenAsync(Ct);
        return conexion;
    }

    private static async Task<PostgresException> FallaAsync(NpgsqlConnection conexion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        return await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(Ct));
    }

    [Fact]
    public async Task El_rol_de_la_aplicacion_no_tiene_privilegios_y_no_puede_apagar_rls_ni_cambiar_la_auditoria()
    {
        await using var conexion = await AbrirComoAplicacionAsync();

        var estado = await RolesBaseDeDatos.RevisarAsync(conexion, Ct);

        Assert.Equal(EntornoApi.RolAplicacion, estado.Rol);
        Assert.True(estado.SinPrivilegios, estado.ToString());
        // 42501: insufficient_privilege (no es dueño de la tabla ni tiene permiso).
        Assert.Equal("42501", (await FallaAsync(conexion, "ALTER TABLE atlas.plataforma_persona NO FORCE ROW LEVEL SECURITY")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "ALTER TABLE atlas.contratos_sandbox DISABLE ROW LEVEL SECURITY")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "DROP POLICY organizacion_visible ON atlas.plataforma_persona")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "UPDATE atlas.plataforma_auditoria SET accion = accion")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "DELETE FROM atlas.identidad_evento_seguridad")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "TRUNCATE atlas.plataforma_organizacion")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "DELETE FROM atlas.__ef_migraciones_plataforma")).SqlState);
        Assert.Equal("42501", (await FallaAsync(conexion, "CREATE TABLE atlas.intrusa (id int)")).SqlState);
    }

    [Fact]
    public async Task Sin_variables_de_alcance_el_rol_de_la_aplicacion_no_ve_filas_con_organizacion()
    {
        await using var conexion = await AbrirComoAplicacionAsync();
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM atlas.plataforma_organizacion", conexion);

        Assert.Equal(0L, await comando.ExecuteScalarAsync(Ct));
    }

    [Theory]
    [InlineData("qa", HealthStatus.Unhealthy)]
    [InlineData("pdn", HealthStatus.Unhealthy)]
    [InlineData("local", HealthStatus.Degraded)]
    public async Task La_sonda_de_preparacion_no_queda_lista_fuera_de_local_con_un_rol_con_privilegios(string ambiente, HealthStatus esperado)
    {
        // El superusuario del contenedor (dueño de las tablas): lo que pasaría si atlas-api usara la cadena del migrador.
        Assert.Equal(esperado, (await ChequearAsync(entorno.Postgres.GetConnectionString(), ambiente)).Status);
        Assert.Equal(HealthStatus.Healthy, (await ChequearAsync(entorno.ComoAplicacion(), ambiente)).Status);
    }

    private static async Task<HealthCheckResult> ChequearAsync(string cadena, string ambiente)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ChequeoRolBaseDeDatos.ClaveAmbiente] = ambiente })
            .Build();
        await using var servicios = new ServiceCollection()
            .AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, cadena))
            .BuildServiceProvider();
        var chequeo = new ChequeoRolBaseDeDatos(
            servicios.GetRequiredService<IServiceScopeFactory>(), configuracion, NullLogger<ChequeoRolBaseDeDatos>.Instance);
        return await chequeo.CheckHealthAsync(new HealthCheckContext(), Ct);
    }

    [Fact]
    public void El_nombre_del_rol_de_la_aplicacion_no_admite_inyeccion()
    {
        Assert.Throws<ArgumentException>(() => RolesBaseDeDatos.Sql("flit_atlas; DROP SCHEMA atlas"));
        Assert.Throws<ArgumentException>(() => RolesBaseDeDatos.Sql("\"flit_atlas\""));
        Assert.Contains("TO flit_atlas;", RolesBaseDeDatos.Sql("flit_atlas"), StringComparison.Ordinal);
    }
}
