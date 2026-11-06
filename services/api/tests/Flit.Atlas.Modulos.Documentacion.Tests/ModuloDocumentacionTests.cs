using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Atlas.Modulos.Documentacion.Tests;

/// <summary>El módulo cumple el contrato de módulo y su esquema hace cumplir la inmutabilidad y la búsqueda (ATLAS-ADR-0053).</summary>
public class ModuloDocumentacionTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo()
    {
        var modulo = new ModuloDocumentacion();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("documentacion", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(DocumentacionDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_documentacion", DocumentacionDbContext.TablaHistorial);
        Assert.Empty(modulo.Permisos.Permisos);
        Assert.Equal(["atlas.documentacion.version_publicada"], modulo.EventosPublicados.Select(e => e.Nombre));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Documentacion();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Documentacion -o Datos/Migraciones");
    }

    [Fact]
    public async Task Migra_sin_unaccent_con_el_usuario_del_servicio_y_repetirlo_no_cambia_nada()
    {
        var bd = await contenedores.CrearBaseAsync();

        var extensiones = await Contenedores.ConsultarAsync(bd.Admin, "SELECT extname::text FROM pg_extension WHERE extname = 'unaccent'");
        await using var contexto = bd.Documentacion();
        await contexto.Database.MigrateAsync(Ct);

        Assert.Empty(extensiones);
        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
        var tablas = await Contenedores.ConsultarAsync(bd.Servicio,
            "SELECT table_name::text FROM information_schema.tables WHERE table_schema = 'atlas' AND table_name LIKE 'documentacion%' ORDER BY 1");
        Assert.Equal(["documentacion_canal", "documentacion_documento", "documentacion_revision", "documentacion_version"], tablas);
    }

    [Fact]
    public async Task Marca_los_datos_personales()
    {
        var bd = await contenedores.CrearBaseAsync();

        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || '.' || a.attname FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname LIKE 'documentacion%' AND col_description(a.attrelid, a.attnum) = '@pii' ORDER BY 1
            """);

        Assert.Equal(["documentacion_documento.responsable", "documentacion_revision.nota", "documentacion_revision.responsable", "documentacion_version.responsable"], pii);
    }

    private static Documento Documento() => new()
    {
        AplicativoId = Guid.CreateVersion7(), Origen = OrigenDocumento.Atlas, Tipo = "guia_funcional", Titulo = "Guía", Visibilidad = VisibilidadDocumento.InternoNegocio,
        Responsable = "Persona Demo",
    };

    private static VersionDocumento Version(Guid documentoId, string titulo, string contenido) => new()
    {
        DocumentoId = documentoId, Numero = 1, Tipo = "guia_funcional", Titulo = titulo, Contenido = contenido, Visibilidad = VisibilidadDocumento.InternoNegocio,
        Responsable = "Persona Demo", EsBorrador = false, Sha256 = new string('a', 64), PublicadoEn = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Una_version_y_una_revision_no_se_pueden_cambiar_ni_con_SQL()
    {
        var bd = await contenedores.CrearBaseAsync();
        var documento = Documento();
        await using (var contexto = bd.Documentacion())
        {
            contexto.Documentos.Add(documento);
            contexto.Versiones.Add(Version(documento.Id, "Guía", "Cuerpo"));
            contexto.Revisiones.Add(new RevisionDocumento
            {
                DocumentoId = documento.Id, Numero = 1, Origen = OrigenRevisionDocumento.Creacion, Tipo = "guia_funcional", Titulo = "Guía", Contenido = "Cuerpo",
                Visibilidad = VisibilidadDocumento.InternoNegocio, Responsable = "Persona Demo", Nota = "Primera", AutorPersonaId = Guid.CreateVersion7(),
                GuardadaEn = DateTimeOffset.UtcNow, Huella = new string('b', 64),
            });
            await contexto.SaveChangesAsync(Ct);
        }

        var version = await Assert.ThrowsAsync<PostgresException>(() =>
            Contenedores.EjecutarAsync(bd.Servicio, "UPDATE atlas.documentacion_version SET contenido = 'Otro'"));
        var revision = await Assert.ThrowsAsync<PostgresException>(() =>
            Contenedores.EjecutarAsync(bd.Servicio, "UPDATE atlas.documentacion_revision SET contenido = 'Otro'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, version.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, revision.SqlState);
    }

    [Theory]
    [InlineData("documentación", "documentacion")]
    [InlineData("documentacion", "Documentación")]
    [InlineData("Trámites", "tramite")]
    public async Task La_busqueda_es_en_espanol_y_no_distingue_tildes(string enElTexto, string buscado)
    {
        var bd = await contenedores.CrearBaseAsync();
        var documento = Documento();
        await using var contexto = bd.Documentacion();
        contexto.Documentos.Add(documento);
        contexto.Versiones.Add(Version(documento.Id, "Otro título", $"Aquí se habla de {enElTexto} con detalle."));
        await contexto.SaveChangesAsync(Ct);

        var termino = FuncionesSql.NormalizarBusqueda(buscado);
        var hallados = await contexto.Versiones
            .CountAsync(v => EF.Property<NpgsqlTsVector>(v, DocumentacionDbContext.ColumnaBusqueda).Matches(EF.Functions.PlainToTsQuery("spanish", termino)), Ct);
        var otros = await contexto.Versiones
            .CountAsync(v => EF.Property<NpgsqlTsVector>(v, DocumentacionDbContext.ColumnaBusqueda).Matches(EF.Functions.PlainToTsQuery("spanish", "inexistente")), Ct);

        Assert.Equal(1, hallados);
        Assert.Equal(0, otros);
    }
}
