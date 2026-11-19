using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Exportaciones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// HU 4.5 (ATLAS-ADR-0042): <c>contratos_exportacion</c> lleva <c>organizacion_id</c>, así que tiene el predicado
/// obligatorio y RLS forzada como las tablas de organizaciones del núcleo (prueba con el usuario del servicio, dueño de
/// las tablas). Además, las reglas puras de los formatos y del changelog.
/// </summary>
public sealed class ExportacionesTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExportacionContratos Exportacion(Guid paqueteId, Guid? organizacion) => new()
    {
        SolicitadaPor = Guid.NewGuid(),
        OrganizacionId = organizacion,
        DesdePortal = organizacion is not null,
        Origen = OrigenExportacion.Paquete,
        PaqueteId = paqueteId,
        Revision = 1,
        Nombre = "Integración Demo · revisión 1",
        Formatos = ["pdf", "zip"],
        EntradaClave = "exportaciones/x/entrada.json",
        EntradaSha256 = new string('a', 64),
        SolicitadaEn = DateTimeOffset.UtcNow,
    };

    private static async Task<(BaseDePrueba Bd, Guid OrgA, Guid OrgB, Guid PaqueteId)> EscenarioAsync(Contenedores contenedores)
    {
        var bd = await contenedores.CrearBaseAsync();
        var a = new Organizacion { Nombre = "Organismo Demo A", Tipo = TipoOrganizacion.Cliente };
        var b = new Organizacion { Nombre = "Organismo Demo B", Tipo = TipoOrganizacion.Cliente };
        await using (var plataforma = bd.Plataforma())
        {
            plataforma.Organizaciones.AddRange(a, b);
            await plataforma.SaveChangesAsync(Ct);
        }
        var paquete = new PaqueteContratos { Nombre = "Integración Demo", RevisionActual = 1, CreadoPor = Guid.NewGuid() };
        await using (var sistema = bd.Contratos())
        {
            sistema.Paquetes.Add(paquete);
            sistema.RevisionesPaquete.Add(new RevisionPaquete
            {
                PaqueteId = paquete.Id, Numero = 1, Nombre = paquete.Nombre, Definicion = "[]", Nota = "Primera.", AutorPersonaId = paquete.CreadoPor,
                GuardadaEn = DateTimeOffset.UtcNow, Huella = new string('f', 64),
            });
            sistema.Exportaciones.AddRange(Exportacion(paquete.Id, a.Id), Exportacion(paquete.Id, b.Id), Exportacion(paquete.Id, null));
            await sistema.SaveChangesAsync(Ct);
        }
        return (bd, a.Id, b.Id, paquete.Id);
    }

    [Fact]
    public async Task Un_externo_solo_ve_las_exportaciones_de_su_organizacion_aun_sin_el_predicado()
    {
        var e = await EscenarioAsync(contenedores);
        await using var deA = e.Bd.Contratos(AlcanceDatos.DeOrganizaciones(e.OrgA));

        Assert.Equal([e.OrgA], await deA.Exportaciones.Select(x => x.OrganizacionId).ToListAsync(Ct));
        // Sin la primera capa (IgnoreQueryFilters), RLS oculta igual las de B y las internas (sin organización).
        Assert.Equal([e.OrgA], await deA.Exportaciones.IgnoreQueryFilters().Select(x => x.OrganizacionId).ToListAsync(Ct));
        await using var interno = e.Bd.Contratos(AlcanceDatos.Interno);
        Assert.Equal(3, await interno.Exportaciones.CountAsync(Ct));
    }

    [Fact]
    public async Task RLS_impide_crear_una_exportacion_de_otra_organizacion()
    {
        var e = await EscenarioAsync(contenedores);
        await using var deA = e.Bd.Contratos(AlcanceDatos.DeOrganizaciones(e.OrgA));
        deA.Exportaciones.Add(Exportacion(e.PaqueteId, e.OrgB));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => deA.SaveChangesAsync(Ct));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
    }

    [Fact]
    public async Task Una_exportacion_externa_solo_puede_ser_de_un_paquete_desde_el_portal()
    {
        var e = await EscenarioAsync(contenedores);
        await using var sistema = e.Bd.Contratos();
        var fuera = Exportacion(e.PaqueteId, e.OrgA);
        sistema.Exportaciones.Add(new ExportacionContratos
        {
            SolicitadaPor = fuera.SolicitadaPor, OrganizacionId = e.OrgA, DesdePortal = false, Origen = OrigenExportacion.Paquete, PaqueteId = e.PaqueteId,
            Revision = 1, Nombre = fuera.Nombre, EntradaClave = fuera.EntradaClave, EntradaSha256 = fuera.EntradaSha256, SolicitadaEn = fuera.SolicitadaEn,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => sistema.SaveChangesAsync(Ct));

        Assert.Contains("ck_contratos_exportacion_externa", error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "PDF", "excel", "pdf" }, new[] { "pdf", "excel", "zip" })]
    [InlineData(new[] { "zip" }, new[] { "zip" })]
    [InlineData(new[] { "excel", "openapi", "word", "html", "postman", "pdf" }, new[] { "openapi", "postman", "html", "pdf", "word", "excel", "zip" })]
    public void Los_formatos_se_normalizan_en_su_orden_y_siempre_con_el_ZIP(string[] pedidos, string[] esperados) =>
        Assert.Equal(esperados, FormatosExportacion.Normalizar(pedidos));

    [Theory]
    [InlineData("")]
    [InlineData("pdf,docx")]
    public void Un_formato_desconocido_o_ninguno_no_vale(string pedidos) =>
        Assert.Null(FormatosExportacion.Normalizar(pedidos.Split(',', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void El_changelog_de_una_version_sale_del_texto_de_oasdiff_y_si_no_de_la_publicacion()
    {
        static VersionContrato Version(string changelog) => new()
        {
            ContratoId = Guid.NewGuid(), Version = "1.2.0", Mayor = 1, Menor = 2, Parche = 0, EsEstable = true, Sha256 = new string('a', 64),
            Formato = FormatoSpec.Yaml, Contenido = "x", Tamano = 1, Repositorio = "flitsas-demo/api-demo", Rama = "release", CommitSha = new string('b', 40),
            WorkflowDelJob = "w", PublicadoPor = "github:flitsas-demo/api-demo@release", PublicadoEn = new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero),
            Changelog = changelog,
        };
        var conCambios = ArmadorEntrada.ChangelogVersion(Version(JsonSerializer.Serialize(new[] { new { id = "x", text = "se agregó GET /demo" }, new { id = "y", text = "" } })));
        var sinCambios = ArmadorEntrada.ChangelogVersion(Version("no es json"));

        var cambio = Assert.Single(conCambios);
        Assert.Equal("se agregó GET /demo", cambio.Cambio);
        // 03:00 UTC del 24 es el 23 en Bogotá.
        Assert.Equal(new DateOnly(2026, 10, 23), cambio.Fecha);
        Assert.StartsWith("Versión 1.2.0 publicada", Assert.Single(sinCambios).Cambio, StringComparison.Ordinal);
    }
}
