using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// El módulo de contratos cumple el contrato de módulo (ATLAS-ADR-0016 y ATLAS-ADR-0026), se migra con el usuario del
/// servicio después del núcleo y la base hace cumplir la inmutabilidad de las versiones (ATLAS-ADR-0035).
/// </summary>
public class ModuloContratosTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_usa_los_permisos_de_la_lista_base()
    {
        var modulo = new ModuloContratos();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("contratos", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(ContratosDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_contratos", ContratosDbContext.TablaHistorial);
        Assert.Equal([EventosContratos.VersionPublicada, EventosContratos.CanalActualizado, EventosContratos.ExportacionSolicitada, EventosContratos.VersionDeprecada], modulo.EventosPublicados);
        Assert.Equal([EventosContratos.ExportacionIniciada, EventosContratos.ExportacionLista, EventosContratos.ExportacionFallida], modulo.EventosConsumidos);
        // Los mismos códigos y concesiones de la lista base del §7.4 (son contrato).
        Assert.Equal(
            ManifiestosNucleo.Contratos.Permisos.Select(p => (p.Codigo, string.Join(',', p.ConcedidoA))),
            modulo.Permisos.Permisos.Select(p => (p.Codigo, string.Join(',', p.ConcedidoA))));
        Assert.Equal(["atlas.contratos.ver_internos", "atlas.contratos.publicar_manual", "atlas.contratos.guias.editar"],
            modulo.Permisos.Permisos.Select(p => p.Codigo));
    }

    [Fact]
    public void El_nucleo_ya_no_declara_contratos_y_conserva_paquetes_portal_exportaciones_y_sandbox()
    {
        Assert.DoesNotContain(ManifiestosNucleo.Todos, m => m.Modulo == ModuloContratos.IdModulo);
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, new ModuloContratos().Permisos]);
        foreach (var codigo in new[] { "atlas.paquetes.gestionar", "atlas.portal.ver", "atlas.exportaciones.solicitar", "atlas.sandbox.usar" })
        {
            Assert.True(catalogo.Existe(codigo), codigo);
        }
        Assert.Contains(PermisosContratos.VerInternos, catalogo.PermisosDe(RolesAtlas.Desarrollador));
        Assert.DoesNotContain(PermisosContratos.VerInternos, catalogo.PermisosDe(RolesAtlas.Lector));
        Assert.All(RolesAtlas.Externos, r => Assert.DoesNotContain(catalogo.PermisosDe(r), p => p.StartsWith("atlas.contratos.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Contratos();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Contratos -o Datos/Migraciones");
    }

    [Fact]
    public async Task Se_migra_despues_del_nucleo_con_el_usuario_del_servicio_y_repetirlo_no_cambia_nada()
    {
        var bd = await contenedores.CrearBaseAsync(migrar: false);
        await using (var plataforma = bd.Plataforma())
        {
            await plataforma.Database.MigrateAsync(Ct);
        }
        var delNucleo = await Tablas(bd);

        await using var contratos = bd.Contratos();
        await contratos.Database.MigrateAsync(Ct);
        await contratos.Database.MigrateAsync(Ct);

        Assert.Empty(await contratos.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(["__ef_migraciones_contratos", "contratos_aviso_deprecacion", "contratos_canal", "contratos_canal_historial", "contratos_comparticion", "contratos_contrato", "contratos_exportacion", "contratos_favorito", "contratos_guia", "contratos_guia_revision", "contratos_operacion", "contratos_paquete", "contratos_paquete_revision", "contratos_sandbox", "contratos_version"],
            (await Tablas(bd)).Except(delNucleo).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task La_base_rechaza_cambiar_una_version_publicada_o_su_indice_pero_deja_deprecarla()
    {
        var bd = await contenedores.CrearBaseAsync();
        var (contrato, version) = await PublicarAsync(bd, "1.0.0");

        // El estado y la fecha de retiro sí cambian (deprecación, plan 00 §8.2 regla 7).
        await Contenedores.EjecutarAsync(bd.Servicio,
            $"UPDATE atlas.contratos_version SET estado = 'deprecada', retiro_en = '2027-06-30', actualizado_en = now() WHERE id = '{version}'");

        foreach (var cambio in new[] { "contenido = 'otro'", "sha256 = repeat('b', 64)", "version = '1.0.1'", "commit_sha = repeat('c', 40)", "changelog = '[{}]'::jsonb", "changelog_base = '0.9.0'" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_version SET {cambio} WHERE id = '{version}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        var operacion = await Assert.ThrowsAsync<PostgresException>(() =>
            Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_operacion SET resumen = 'otro' WHERE version_id = '{version}'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, operacion.SqlState);

        // La misma versión dos veces en un contrato tampoco entra.
        await using var contexto = bd.Contratos();
        contexto.Versiones.Add(Version(contrato, "1.0.0", new string('d', 64)));
        var repetida = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (repetida.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task El_canal_se_mueve_pero_su_historial_es_solo_insercion_y_hay_uno_por_contrato_y_ambiente()
    {
        var bd = await contenedores.CrearBaseAsync();
        var (contrato, version) = await PublicarAsync(bd, "1.0.0");
        await using var contexto = bd.Contratos();
        var canal = new CanalContrato
        {
            ContratoId = contrato, Ambiente = "pdn", VersionId = version, DesplegadoEn = DateTimeOffset.UtcNow, DesplegadoPor = "github:flitsas-demo/api-demo@release",
            CommitSha = new string('f', 40),
        };
        contexto.Canales.Add(canal);
        contexto.HistorialCanales.Add(new HistorialCanal
        {
            ContratoId = contrato, Ambiente = "pdn", VersionId = version, DesplegadoEn = DateTimeOffset.UtcNow, DesplegadoPor = "github:flitsas-demo/api-demo@release",
            Repositorio = "flitsas-demo/api-demo", Rama = "release", CommitSha = new string('f', 40),
            WorkflowDelJob = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v1",
        });
        await contexto.SaveChangesAsync(Ct);

        // El canal sí cambia (otro despliegue); el historial no.
        await Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_canal SET desplegado_en = now() WHERE id = '{canal.Id}'");
        var historial = await Assert.ThrowsAsync<PostgresException>(() =>
            Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_canal_historial SET rama = 'otra' WHERE contrato_id = '{contrato}'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, historial.SqlState);

        contexto.ChangeTracker.Clear();
        contexto.Canales.Add(new CanalContrato
        {
            ContratoId = contrato, Ambiente = "pdn", VersionId = version, DesplegadoEn = DateTimeOffset.UtcNow, DesplegadoPor = "x", CommitSha = "x",
        });
        var repetido = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (repetido.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task La_base_rechaza_un_hash_que_no_es_sha256_y_una_estable_con_prerelease()
    {
        var bd = await contenedores.CrearBaseAsync();
        var (contrato, _) = await PublicarAsync(bd, "2.0.0");

        await using var contexto = bd.Contratos();
        contexto.Versiones.Add(Version(contrato, "2.0.1", "no-es-un-hash"));
        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        contexto.ChangeTracker.Clear();
        contexto.Versiones.Add(Version(contrato, "2.0.2-dev.1", new string('e', 64), prerelease: "dev.1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
    }

    private static async Task<(Guid Contrato, Guid Version)> PublicarAsync(BaseDePrueba bd, string version)
    {
        await using var contexto = bd.Contratos();
        var contrato = new Contrato
        {
            AplicativoId = Guid.NewGuid(), Codigo = "api-demo", Tipo = TipoContrato.Openapi, Nombre = "API Demo", Repositorio = "flitsas-demo/api-demo",
        };
        var nueva = Version(contrato.Id, version, new string('a', 64));
        contexto.Contratos.Add(contrato);
        contexto.Versiones.Add(nueva);
        contexto.Operaciones.Add(new OperacionContrato { VersionId = nueva.Id, Clave = "GET /demo", Clase = "operacion", Metodo = "GET", Ruta = "/demo" });
        await contexto.SaveChangesAsync(Ct);
        return (contrato.Id, nueva.Id);
    }

    /// <param name="prerelease">Con prerelease pero marcada estable: la base debe rechazarla.</param>
    private static VersionContrato Version(Guid contrato, string version, string sha, string? prerelease = null) => new()
    {
        ContratoId = contrato,
        Version = version,
        Mayor = int.Parse(version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture),
        Menor = 0,
        Parche = 0,
        Prerelease = prerelease,
        EsEstable = true,
        Sha256 = sha,
        Formato = FormatoSpec.Yaml,
        Contenido = "openapi: 3.1.0",
        Tamano = 14,
        Repositorio = "flitsas-demo/api-demo",
        Rama = "release",
        CommitSha = new string('f', 40),
        WorkflowDelJob = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v1",
        PublicadoPor = "github:flitsas-demo/api-demo@release",
        PublicadoEn = DateTimeOffset.UtcNow,
    };

    private static Task<List<string>> Tablas(BaseDePrueba bd) =>
        Contenedores.ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");
}
