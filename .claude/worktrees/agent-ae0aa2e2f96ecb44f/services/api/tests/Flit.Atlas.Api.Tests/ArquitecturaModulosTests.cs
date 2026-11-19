using System.Reflection;
using System.Xml.Linq;
using Flit.Atlas.Composicion;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 1.9 (ATLAS-ADR-0016 y ATLAS-ADR-0026): un módulo se agrega <b>sin tocar otros módulos</b>. Estas pruebas lo hacen
/// cumplir para todos los módulos de <see cref="ModulosAtlas.Registrados"/>: un módulo solo depende del núcleo
/// (<c>Flit.Atlas.Plataforma</c>), nadie fuera de <c>Flit.Atlas.Composicion</c> lo referencia, sus tablas llevan su
/// prefijo y su historial, sus endpoints viven bajo <c>/api/v1/&lt;id&gt;</c> y toda tabla con <c>organizacion_id</c>
/// tiene RLS forzada.
/// </summary>
public class ArquitecturaModulosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Lo único de Atlas que un módulo puede referenciar.</summary>
    private static readonly string[] Permitidas = ["Flit.Atlas.Plataforma"];

    private const string Composicion = "Flit.Atlas.Composicion";

    private static string CarpetaSrc => Path.Combine(Repositorio.Raiz, "services", "api", "src");

    public static TheoryData<string> Modulos() => [.. ModulosAtlas.Registrados.Select(m => m.Id)];

    private static IModuloAtlas Modulo(string id) => ModulosAtlas.Registrados.Single(m => m.Id == id);

    [Fact]
    public void Hay_al_menos_un_modulo_registrado_y_vive_en_su_propio_ensamblado()
    {
        Assert.NotEmpty(ModulosAtlas.Registrados);
        ExtensionesModulos.Validar(ModulosAtlas.Registrados);
        Assert.All(ModulosAtlas.Registrados, m =>
            Assert.StartsWith("Flit.Atlas.Modulos.", m.GetType().Assembly.GetName().Name, StringComparison.Ordinal));
        Assert.Equal(ModulosAtlas.Registrados.Count, ModulosAtlas.Registrados.Select(m => m.GetType().Assembly).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void El_ensamblado_del_modulo_solo_depende_del_nucleo(string id)
    {
        var ensamblado = Modulo(id).GetType().Assembly;

        var deAtlas = ensamblado.GetReferencedAssemblies().Select(r => r.Name!)
            .Where(n => n.StartsWith("Flit.Atlas.", StringComparison.Ordinal)).ToList();

        Assert.True(deAtlas.All(Permitidas.Contains),
            $"{ensamblado.GetName().Name} referencia {string.Join(", ", deAtlas.Except(Permitidas))}; un módulo solo puede depender de {string.Join(", ", Permitidas)}.");
    }

    [Fact]
    public void Ningun_ensamblado_de_atlas_fuera_de_la_composicion_referencia_un_modulo()
    {
        var modulos = ModulosAtlas.Registrados.Select(m => m.GetType().Assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        var cargados = new[] { typeof(Program).Assembly, typeof(ModulosAtlas).Assembly }
            .SelectMany(a => a.GetReferencedAssemblies().Select(Assembly.Load).Append(a))
            .Concat(ModulosAtlas.Registrados.Select(m => m.GetType().Assembly))
            .Where(a => a.GetName().Name!.StartsWith("Flit.Atlas.", StringComparison.Ordinal))
            .DistinctBy(a => a.GetName().Name)
            .Where(a => a.GetName().Name != Composicion);

        var culpables = cargados
            .SelectMany(a => a.GetReferencedAssemblies().Where(r => modulos.Contains(r.Name!)).Select(r => $"{a.GetName().Name} → {r.Name}"))
            .ToList();

        Assert.True(culpables.Count == 0, "Solo Flit.Atlas.Composicion registra módulos: " + string.Join(", ", culpables));
    }

    /// <summary>Lo mismo en los <c>.csproj</c>: atrapa referencias que aún no se usan en el código (y el migrador).</summary>
    [Fact]
    public void En_los_proyectos_solo_la_composicion_referencia_modulos_y_los_modulos_solo_el_nucleo()
    {
        var problemas = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(CarpetaSrc, "*.csproj", SearchOption.AllDirectories))
        {
            var proyecto = Path.GetFileNameWithoutExtension(csproj);
            var referencias = XDocument.Load(csproj).Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
                .ToList();
            if (proyecto.StartsWith("Flit.Atlas.Modulos.", StringComparison.Ordinal))
            {
                problemas.AddRange(referencias.Except(Permitidas).Select(r => $"{proyecto} → {r} (un módulo solo referencia {string.Join(", ", Permitidas)})"));
            }
            else if (proyecto != Composicion)
            {
                problemas.AddRange(referencias.Where(r => r.StartsWith("Flit.Atlas.Modulos.", StringComparison.Ordinal))
                    .Select(r => $"{proyecto} → {r} (los módulos se registran solo en {Composicion})"));
            }
        }

        Assert.True(problemas.Count == 0, string.Join('\n', problemas));
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void El_contexto_del_modulo_solo_mapea_tablas_con_su_prefijo_y_tiene_su_historial(string id)
    {
        var modulo = Modulo(id);
        if (modulo.ContextoDatos is null)
        {
            return;
        }
        var servicios = new ServiceCollection();
        modulo.RegistrarDatos(servicios, "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x");
        using var proveedor = servicios.BuildServiceProvider();
        using var contexto = ExtensionesModulos.ContextoDatosDe(proveedor, modulo)!;

        var tablas = contexto.Model.GetEntityTypes().Select(e => (e.GetSchema(), e.GetTableName())).ToList();
        var historial = contexto.GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>();

        Assert.NotEmpty(tablas);
        Assert.All(tablas, t =>
        {
            Assert.Equal("atlas", t.Item1);
            Assert.StartsWith($"{id}_", t.Item2, StringComparison.Ordinal);
        });
        Assert.Contains(ExtensionesModulos.TablaHistorial(id), historial.GetCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Los_endpoints_de_cada_modulo_viven_bajo_su_ruta_con_su_etiqueta()
    {
        await using var api = ContratoOpenApiTests.ApiComoQa();
        using var _ = api.CreateClient();
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        foreach (var modulo in ModulosAtlas.Registrados)
        {
            var propios = endpoints.Where(e => e.Metadata.GetMetadata<MetadatoModulo>()?.Id == modulo.Id).ToList();
            Assert.NotEmpty(propios);
            Assert.All(propios, e =>
            {
                var etiquetas = e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>()?.Tags ?? [];
                // Los del portal de clientes (ATLAS-ADR-0043) viven en /api/v1/portal con su etiqueta; los demás, bajo el id.
                if (e.RoutePattern.RawText?.StartsWith(ExtensionesModulos.RutaPortal + "/", StringComparison.Ordinal) == true)
                {
                    Assert.Contains(ExtensionesModulos.EtiquetaPortal, etiquetas);
                    return;
                }
                Assert.StartsWith($"/api/v1/{modulo.Id}", e.RoutePattern.RawText, StringComparison.Ordinal);
                Assert.Contains(modulo.Nombre, etiquetas);
            });
            // Nadie más monta rutas en el espacio del módulo.
            Assert.All(endpoints.Where(e => e.RoutePattern.RawText?.StartsWith($"/api/v1/{modulo.Id}/", StringComparison.Ordinal) == true), e =>
                Assert.Equal(modulo.Id, e.Metadata.GetMetadata<MetadatoModulo>()?.Id));
        }
    }

    /// <summary>Módulo mínimo para probar el registro sin crear un proyecto.</summary>
    private sealed class ModuloDePrueba(string id, params ScopeModulo[] scopes) : IModuloAtlas
    {
        public string Id => id;
        public string Nombre => "Prueba";
        public string Descripcion => "Módulo de prueba.";
        public bool ActivoPorDefecto => false;
        public Plataforma.Permisos.ManifiestoPermisos Permisos { get; } = new(id, "Prueba", [new($"atlas.{id}.ver", "Ver.")]);
        public Type? ContextoDatos => null;
        public IReadOnlyList<ScopeModulo> Scopes => scopes;
        public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) { }
        public void RegistrarServicios(IServiceCollection servicios, Microsoft.Extensions.Configuration.IConfiguration configuracion) { }
        public void MapearEndpoints(Microsoft.AspNetCore.Routing.RouteGroupBuilder grupo) { }
    }

    [Fact]
    public void Los_scopes_de_un_modulo_llevan_su_prefijo_y_quedan_a_su_nombre()
    {
        var modulo = new ModuloDePrueba("prueba", new ScopeModulo("atlas.prueba", "Todo."), new ScopeModulo("atlas.prueba.leer", "Leer."));

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal(
            [("atlas.prueba", "prueba"), ("atlas.prueba.leer", "prueba")],
            new ScopesDeModulos([modulo]).Scopes.Select(s => (s.Nombre, s.Modulo)));
        Assert.Throws<InvalidOperationException>(() => ExtensionesModulos.Validar([new ModuloDePrueba("prueba", new ScopeModulo("atlas.otro", "Ajeno."))]));
        Assert.Throws<InvalidOperationException>(() => ExtensionesModulos.Validar([new ModuloDePrueba("plataforma")]));
    }

    /// <summary>La base con el núcleo y todos los módulos migrados: RLS forzada y política en toda tabla con organizacion_id.</summary>
    [Fact]
    public async Task Toda_tabla_con_organizacion_id_del_nucleo_y_de_los_modulos_tiene_RLS_forzada()
    {
        await using var conexion = new NpgsqlConnection(entorno.Postgres.GetConnectionString());
        await conexion.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("""
            SELECT c.relname FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            WHERE c.relkind = 'r'
              AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organizacion_id' AND NOT a.attisdropped)
              AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
            """, conexion);
        var sinRls = new List<string>();
        await using (var lector = await comando.ExecuteReaderAsync(Ct))
        {
            while (await lector.ReadAsync(Ct))
            {
                sinRls.Add(lector.GetString(0));
            }
        }

        Assert.Empty(sinRls);
    }
}
