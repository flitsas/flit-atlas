using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// Reglas de los paquetes (HU 4.1, ATLAS-ADR-0041): forma de lo que llega, huella independiente del orden, filtro del
/// asistente, quién arma un paquete (gestor global o responsable de los aplicativos de todos sus contratos), la
/// visibilidad que declara el spec y la inmutabilidad de las revisiones en la base.
/// </summary>
public class PaquetesTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EndpointsPaquetes.EntradaSolicitud Entrada(Guid contrato, Guid version, string[]? operaciones = null, params EndpointsPaquetes.GuiaSolicitud[] guias) =>
        new(contrato, version, operaciones, guias);

    [Fact]
    public void Normaliza_la_definicion_y_la_huella_no_depende_del_orden_ni_de_la_nota()
    {
        var (a, b, va, vb) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (g1, g2) = (Guid.NewGuid(), Guid.NewGuid());

        var uno = ReglasPaquetes.Leer("  Paquete Demo  ", "  Para Organismo Demo. ",
            [Entrada(a, va, [" POST /tramites ", "GET /tramites/{id}", "GET /tramites/{id}"], new(g2, 3), new(g1, null)), Entrada(b, vb, [])], " Primera. ");
        var dos = ReglasPaquetes.Leer("Paquete Demo", "Para Organismo Demo.",
            [Entrada(b, vb), Entrada(a, va, ["GET /tramites/{id}", "POST /tramites"], new(g1, null), new(g2, 3))], "Otra nota");

        Assert.Empty(uno.Errores);
        Assert.Equal(("Paquete Demo", "Para Organismo Demo.", "Primera."), (uno.Nombre, uno.Descripcion, uno.Nota));
        var entradaA = Assert.Single(uno.Definicion!, e => e.ContratoId == a);
        Assert.Equal(["GET /tramites/{id}", "POST /tramites"], entradaA.Operaciones);
        Assert.Equal(new[] { g1, g2 }.Select(g => g.ToString("D")).Order(StringComparer.Ordinal), entradaA.Guias.Select(g => g.GuiaId.ToString("D")));

        FotoPaquete Foto((string? Nombre, string? Descripcion, IReadOnlyList<EntradaSolicitada>? Definicion, string? Nota, Dictionary<string, string[]> _) l) =>
            new(l.Nombre!, l.Descripcion, [.. l.Definicion!.Select(e => new EntradaPaquete(e.ContratoId, e.VersionId, e.Operaciones,
                [.. e.Guias.Select(g => new GuiaEnPaquete(g.GuiaId, g.Revision ?? 1))]))]);
        Assert.Equal(Foto(uno).Huella(), Foto(dos).Huella());
        Assert.Matches("^[0-9a-f]{64}$", Foto(uno).Huella());
        Assert.NotEqual(Foto(uno).Huella(), (Foto(uno) with { Nombre = "Otro" }).Huella());

        // Ida y vuelta por el JSON de la columna.
        var json = Foto(uno).DefinicionJson();
        Assert.Contains("\"contratoId\"", json, StringComparison.Ordinal);
        Assert.Equal(Foto(uno).Huella(), new FotoPaquete("Paquete Demo", "Para Organismo Demo.", FotoPaquete.LeerDefinicion(json)).Huella());
    }

    [Fact]
    public void Rechaza_campos_vacios_contratos_repetidos_operaciones_vacias_y_guias_repetidas()
    {
        var (a, v) = (Guid.NewGuid(), Guid.NewGuid());
        var g = Guid.NewGuid();

        var vacio = ReglasPaquetes.Leer(" ", null, null, "");
        Assert.Equal(["nombre", "nota"], vacio.Errores.Keys.Order(StringComparer.Ordinal));

        var malos = ReglasPaquetes.Leer("P", null,
            [Entrada(a, v), Entrada(a, v), Entrada(Guid.NewGuid(), Guid.Empty), Entrada(Guid.NewGuid(), Guid.NewGuid(), ["  "]),
             Entrada(Guid.NewGuid(), Guid.NewGuid(), null, new(g, null), new(g, 2)), Entrada(Guid.NewGuid(), Guid.NewGuid(), null, new EndpointsPaquetes.GuiaSolicitud(Guid.NewGuid(), 0)), null],
            "n");
        Assert.Null(malos.Definicion);
        Assert.Equal(["definicion[1].contratoId", "definicion[2]", "definicion[3].operaciones", "definicion[4].guias", "definicion[5].guias", "definicion[6]"],
            malos.Errores.Keys.Order(StringComparer.Ordinal));

        var muchos = ReglasPaquetes.Leer("P", null, [.. Enumerable.Range(0, ReglasPaquetes.MaximoContratos + 1).Select(_ => Entrada(Guid.NewGuid(), Guid.NewGuid()))], "n");
        Assert.True(muchos.Errores.ContainsKey("definicion"));

        // Un borrador puede ir vacío; publicar exige al menos un contrato con una operación.
        Assert.Empty(ReglasPaquetes.Leer("P", null, [], "n").Errores);
        Assert.False(ReglasPaquetes.TieneAlgoQueCompartir([new EntradaPaquete(a, v, [], [])]));
        Assert.True(ReglasPaquetes.TieneAlgoQueCompartir([new EntradaPaquete(a, v, [], []), new EntradaPaquete(Guid.NewGuid(), v, ["GET /x"], [])]));
    }

    [Fact]
    public void El_filtro_del_asistente_es_por_etiqueta_sin_mayusculas_y_por_x_public()
    {
        var version = Guid.NewGuid();
        OperacionContrato Op(string clave, bool publica, params string[] etiquetas) =>
            new() { VersionId = version, Clave = clave, Clase = "operacion", Etiquetas = [.. etiquetas], Publica = publica };
        var operaciones = new[] { Op("GET /a", true, "Tramites", "public"), Op("POST /a", false, "tramites"), Op("GET /b", true, "placas"), Op("DELETE /a", false) };

        Assert.Equal(4, ReglasPaquetes.Filtrar(operaciones, null, false).Count());
        Assert.Equal(["GET /a", "POST /a"], ReglasPaquetes.Filtrar(operaciones, " TRAMITES ", false).Select(o => o.Clave));
        Assert.Equal(["GET /a", "GET /b"], ReglasPaquetes.Filtrar(operaciones, "", true).Select(o => o.Clave));
        Assert.Equal(["GET /a"], ReglasPaquetes.Filtrar(operaciones, "tramites", true).Select(o => o.Clave));
    }

    [Fact]
    public void Arma_el_gestor_global_o_el_responsable_de_los_aplicativos_de_todos_sus_contratos()
    {
        var producto = new AplicativoLeido(Guid.CreateVersion7(), "producto-demo", "Producto Demo", "producto", null, "satelite", "activo", null, null);
        var servicio = new AplicativoLeido(Guid.CreateVersion7(), "servicio-demo", "Servicio Demo", "servicio", producto.Id, "satelite", "activo", null, null);
        var otro = new AplicativoLeido(Guid.CreateVersion7(), "otro-demo", "Otro Demo", "producto", null, "satelite", "activo", null, null);
        var aplicativos = new[] { producto, servicio, otro }.ToDictionary(a => a.Id);
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, new ModuloContratos().Permisos]);

        PermisosEfectivos Con(params ConcesionRol[] concesiones) => PermisosEfectivos.Desde(catalogo, concesiones);
        ConcesionRol Global(string rol) => new(rol, null, AlcanceAsignacion.Global, null, "manual");
        ConcesionRol ResponsableDe(Guid id) => new(RolesAtlas.Responsable, null, new AlcanceAsignacion(TiposAlcance.Aplicativo, id.ToString("D")), null, "implicito");

        var lider = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.LiderTecnico));
        var desarrollador = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.Desarrollador));
        var delProducto = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.Desarrollador), ResponsableDe(producto.Id));
        var (yo, otraPersona) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.True(ReglasPaquetes.GestionaTodo(lider));
        Assert.False(ReglasPaquetes.GestionaTodo(delProducto));
        // El responsable del producto usa los contratos de su servicio (ancestro), no los de otro producto.
        Assert.True(ReglasPaquetes.PuedeUsar(delProducto, servicio.Id, aplicativos));
        Assert.False(ReglasPaquetes.PuedeUsar(delProducto, otro.Id, aplicativos));
        Assert.False(ReglasPaquetes.PuedeUsar(desarrollador, servicio.Id, aplicativos));
        Assert.True(ReglasPaquetes.PuedeGestionar(delProducto, [servicio.Id, producto.Id], otraPersona, yo, aplicativos));
        Assert.False(ReglasPaquetes.PuedeGestionar(delProducto, [servicio.Id, otro.Id], yo, yo, aplicativos));
        // Sin contratos, solo quien lo creó (o el gestor global).
        Assert.True(ReglasPaquetes.PuedeGestionar(delProducto, [], yo, yo, aplicativos));
        Assert.False(ReglasPaquetes.PuedeGestionar(delProducto, [], otraPersona, yo, aplicativos));
        Assert.True(ReglasPaquetes.PuedeGestionar(lider, [otro.Id], otraPersona, yo, aplicativos));
        Assert.False(ReglasPaquetes.PuedeGestionar(desarrollador, [], yo, yo, aplicativos));
    }

    [Theory]
    [InlineData("  x-flit-visibilidad: compartible\n", VisibilidadContrato.Compartible)]
    [InlineData("  x-flit-visibilidad: interna\n", VisibilidadContrato.Interna)]
    [InlineData("  x-flit-visibilidad: publica\n", null)]
    [InlineData("", null)]
    public void El_spec_declara_su_visibilidad_en_info(string marca, VisibilidadContrato? esperada)
    {
        var openapi = SpecsDeEjemplo.OpenApiYaml.Replace("  version: 1.2.0\n", "  version: 1.2.0\n" + marca, StringComparison.Ordinal);
        var asyncapi = SpecsDeEjemplo.AsyncApi3.Replace("  version: 2.0.0\n", "  version: 2.0.0\n" + marca, StringComparison.Ordinal);

        Assert.Equal(esperada, AnalizadorSpec.Analizar(TipoContrato.Openapi, openapi).VisibilidadDeclarada);
        Assert.Equal(esperada, AnalizadorSpec.Analizar(TipoContrato.Asyncapi, asyncapi).VisibilidadDeclarada);
    }

    [Fact]
    public async Task La_base_rechaza_cambiar_una_revision_un_numero_repetido_y_estados_incoherentes()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Contratos();
        var paquete = new PaqueteContratos { Nombre = "Paquete Demo", RevisionActual = 1, CreadoPor = Guid.NewGuid() };
        contexto.AddRange(paquete, Revision(paquete.Id, 1));
        await contexto.SaveChangesAsync(Ct);

        foreach (var cambio in new[] { "nombre = 'otro'", "nota = 'otra'", "definicion = '[{}]'", "huella = repeat('b', 64)", "numero = 9" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_paquete_revision SET {cambio} WHERE paquete_id = '{paquete.Id}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }

        contexto.RevisionesPaquete.Add(Revision(paquete.Id, 1));
        var repetida = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (repetida.InnerException as PostgresException)?.SqlState);

        // La vigente no puede ser una que no existe; publicado exige vigente; archivado exige fecha.
        foreach (var cambio in new[] { "revision_publicada = 2, estado = 'publicado'", "estado = 'publicado'", "estado = 'archivado'", "revision_publicada = 1" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_paquete SET {cambio} WHERE id = '{paquete.Id}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        var noEsArreglo = await Assert.ThrowsAsync<PostgresException>(() => Contenedores.EjecutarAsync(bd.Servicio, $"""
            INSERT INTO atlas.contratos_paquete_revision (id, paquete_id, numero, nombre, definicion, nota, autor_persona_id, guardada_en, huella, creado_en, actualizado_en)
            VALUES ('{Guid.NewGuid()}', '{paquete.Id}', 2, 'P', '{"{}"}', 'n', '{Guid.NewGuid()}', now(), repeat('a', 64), now(), now())
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, noEsArreglo.SqlState);
    }

    private static RevisionPaquete Revision(Guid paquete, int numero)
    {
        var foto = new FotoPaquete("Paquete Demo", null, []);
        return new RevisionPaquete
        {
            PaqueteId = paquete, Numero = numero, Nombre = foto.Nombre, Definicion = foto.DefinicionJson(), Nota = "Creación",
            AutorPersonaId = Guid.NewGuid(), GuardadaEn = DateTimeOffset.UtcNow, Huella = foto.Huella(),
        };
    }
}
