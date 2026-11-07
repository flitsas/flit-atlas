using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Gobierno de IA (ATLAS-ADR-0055) con atlas-api completo: bandera apagada, semilla, aprobación según el nivel, EIPD, políticas
/// con la marca de pendiente, incidentes y tablero. <c>AutorizacionTests</c> recorre además estos endpoints con todos los roles.
/// </summary>
public sealed class ModuloGiaTests(EntornoApi entorno)
{
    private const string Ruta = "/api/v1/gia";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> BaseEncendidaAsync()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        (await plataforma.Modulos.SingleAsync(m => m.Id == "gia", Ct)).Activo = true;
        await plataforma.SaveChangesAsync(Ct);
        return baseDeDatos;
    }

    private async Task<(Navegador Navegador, string PersonaId)> EmpleadoAsync(WebApplicationFactory<Program> api, string baseDeDatos, string? rol = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::55:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol, baseDeDatos) };
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync($"gia.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Gobierno", grupos)).StatusCode);
        var yo = await Json(await navegador.GetAsync("/api/v1/plataforma/yo"));
        return (navegador, yo.GetProperty("personaId").GetString()!);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Enviar(Navegador n, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        n.EnviarAsync(metodo, Ruta + ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    private static object Ficha(string dueno, string nombre = "Caso de prueba") => new
    {
        nombre, aplicacion = "App de prueba", aplicativoId = (Guid?)null, repo = (string?)null, duenoPersonaId = dueno, ambientes = new[] { "qa" }, proposito = "Probar el flujo.",
        tipo = "chat", usuariosAfectados = new[] { "interno" }, decisionSobrePersonas = false, decisionDescripcion = (string?)null, humanoEnBucle = "revision_posterior",
        categoriasDato = new[] { "personal" }, tiposPii = new[] { "correo" }, volumenMensualEstimado = 100, baseLegal = "Autorización", baseLegalRef = (string?)null,
        minimizacion = "Solo lo necesario", presidioActivo = true, presidioEntidades = new[] { "correo" }, aliasUsados = new[] { "flit-rapido" }, proveedorActual = (string?)null,
        transferenciaInternacional = "EE. UU.", metricasCalidad = "Exactitud 95 %", evalsRuta = "evals/caso", promptsUbicacion = "repo", controles = "Revisión humana",
        avisoUsuario = "Generado con IA", planRollback = "Volver al prompt anterior", enlaces = Array.Empty<string>(),
    };

    private static object Evaluacion(int p, int i) => new
    {
        dimensiones = new[] { "D1", "D2", "D3", "D4", "D5", "D6" }.Select(d => new { dimension = d, escenario = "Escenario de prueba", p, i, controlesExistentes = (string?)null, controlesNuevos = (string?)null, p2 = (int?)null, i2 = (int?)null }),
        principios = new { idoneidad = "Sí" },
        transferenciaInternacional = "EE. UU.",
    };

    private static async Task<string> CrearCasoAsync(Navegador n, string dueno, string codigo)
    {
        var respuesta = await Enviar(n, HttpMethod.Post, "/casos", new { codigo, ficha = Ficha(dueno) });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Json(respuesta)).GetProperty("caso").GetProperty("id").GetString()!;
    }

    private static async Task DarRolAsync(Navegador n, string persona, string rol) =>
        Assert.Equal(HttpStatusCode.Created, (await Enviar(n, HttpMethod.Post, "/roles-gobierno", new { personaId = persona, rol })).StatusCode);

    private static async Task<string> CodigoErrorAsync(HttpResponseMessage r) => (await Json(r)).GetProperty("codigo").GetString()!;

    [Fact]
    public async Task Apagado_por_defecto_responde_404()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;

        var banderas = await (await n.GetAsync("/api/v1/plataforma/banderas")).Content.ReadFromJsonAsync<Dictionary<string, bool>>(Ct);

        Assert.False(banderas!["modulo.gia"]);
        Assert.Equal(HttpStatusCode.NotFound, (await n.GetAsync(Ruta + "/casos")).StatusCode);
    }

    [Fact]
    public async Task Encendido_trae_lo_sembrado_en_borrador_y_un_lector_no_entra()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (gobierno, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        var (lector, _) = await EmpleadoAsync(api, baseDeDatos);
        using var g = gobierno;
        using var l = lector;

        var casos = await Json(await gobierno.GetAsync(Ruta + "/casos"));
        var politicas = await Json(await gobierno.GetAsync(Ruta + "/politicas"));

        Assert.Equal(13, casos.GetProperty("elementos").GetArrayLength());
        Assert.All(casos.GetProperty("elementos").EnumerateArray(), c => Assert.Equal("borrador", c.GetProperty("estado").GetString()));
        Assert.Equal(7, politicas.GetArrayLength());
        Assert.All(politicas.EnumerateArray(), p => Assert.True(p.GetProperty("pendienteDeRedaccion").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync(Ruta + "/casos")).StatusCode);
    }

    [Fact]
    public async Task Un_auditor_lee_pero_no_gestiona()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (auditor, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.Auditor);
        using var a = auditor;

        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync(Ruta + "/tablero")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync(Ruta + "/asientos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(auditor, HttpMethod.Post, "/casos", new { codigo = "no-entra", ficha = Ficha(persona) })).StatusCode);
    }

    [Fact]
    public async Task Sin_rol_de_gobierno_nadie_aprueba()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        var caso = await CrearCasoAsync(n, persona, "sin-rol");
        Assert.Equal(HttpStatusCode.Created, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision")).StatusCode);

        var voto = await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "responsable_ia", voto = "aprobar" });

        Assert.Equal(HttpStatusCode.Forbidden, voto.StatusCode);
        Assert.Equal("sin_rol_gobierno", await CodigoErrorAsync(voto));
    }

    [Fact]
    public async Task Riesgo_bajo_lo_aprueba_el_responsable_de_ia_y_queda_para_12_meses()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        await DarRolAsync(n, persona, "responsable_ia");
        var caso = await CrearCasoAsync(n, persona, "riesgo-bajo");
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(1, 1));
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision");

        var evaluacion = await Json(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "responsable_ia", voto = "aprobar" }));
        var detalle = await Json(await n.GetAsync($"{Ruta}/casos/{caso}"));

        Assert.Equal("aprobado", evaluacion.GetProperty("decision").GetString());
        Assert.Equal("aprobado", detalle.GetProperty("caso").GetProperty("estado").GetString());
        Assert.Equal("bajo", detalle.GetProperty("caso").GetProperty("nivelRiesgo").GetString());
        Assert.True(DateOnly.Parse(detalle.GetProperty("caso").GetProperty("proximaRevision").GetString()!, System.Globalization.CultureInfo.InvariantCulture) > DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(11)));
    }

    [Fact]
    public async Task Riesgo_alto_exige_comite_y_eipd_aprobada_por_el_opd()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        await DarRolAsync(n, persona, "responsable_ia");
        await DarRolAsync(n, persona, "comite");
        var caso = await CrearCasoAsync(n, persona, "riesgo-alto");
        var evaluacion = await Json(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(3, 4)));
        Assert.Equal("alto", evaluacion.GetProperty("nivelInherente").GetString());
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision");

        var responsable = await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "responsable_ia", voto = "aprobar" });
        Assert.Equal("rol_no_aplica", await CodigoErrorAsync(responsable));

        var sinEipd = await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "comite", voto = "aprobar" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinEipd.StatusCode);
        Assert.Equal("requisitos_pendientes", await CodigoErrorAsync(sinEipd));

        var secciones = new { descripcion = "D", necesidad = "N", riesgos = "R", medidas = "M", opinionOpd = (string?)null, planAccion = "P" };
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Put, $"/casos/{caso}/eipd", secciones)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/eipd/envio")).StatusCode);
        // Sin el rol OPD no se decide.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/eipd/decision", new { decision = "aprobada" })).StatusCode);
        await DarRolAsync(n, persona, "opd");
        Assert.Equal("ficha_incompleta", await CodigoErrorAsync(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/eipd/decision", new { decision = "aprobada" })));
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Put, $"/casos/{caso}/eipd", new { opinionOpd = "Favorable" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/eipd/decision", new { decision = "aprobada" })).StatusCode);

        var aprobada = await Json(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "comite", voto = "aprobar", condiciones = "Revisar en 3 meses" }));

        Assert.Equal("aprobado_condiciones", aprobada.GetProperty("decision").GetString());
        var eipd = await Json(await n.GetAsync($"{Ruta}/casos/{caso}/eipd"));
        Assert.Equal("D", eipd.GetProperty("descripcion").GetString());
        Assert.Equal("Favorable", eipd.GetProperty("opinionOpd").GetString());
    }

    [Fact]
    public async Task Con_residual_critico_no_se_aprueba_aunque_vote_el_comite()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        await DarRolAsync(n, persona, "comite");
        var caso = await CrearCasoAsync(n, persona, "riesgo-critico");
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(5, 5));
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision");

        var voto = await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "comite", voto = "aprobar" });

        Assert.Equal("riesgo_critico", await CodigoErrorAsync(voto));
    }

    [Fact]
    public async Task Un_rechazo_devuelve_a_borrador_y_lo_aprobado_no_se_edita_sin_reabrir()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        await DarRolAsync(n, persona, "responsable_ia");
        var caso = await CrearCasoAsync(n, persona, "ciclo");
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(1, 1));
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision");
        var enRevision = await Enviar(n, HttpMethod.Put, $"/casos/{caso}", new { ficha = Ficha(persona, "Otro"), motivo = "Cambio", versionBase = 1 });
        Assert.Equal("estado_invalido", await CodigoErrorAsync(enRevision));

        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/1/votos", new { rol = "responsable_ia", voto = "rechazar" });
        Assert.Equal("borrador", (await Json(await n.GetAsync($"{Ruta}/casos/{caso}"))).GetProperty("caso").GetProperty("estado").GetString());
        // Sin evaluación pendiente no se vuelve a enviar.
        Assert.Equal("sin_evaluacion", await CodigoErrorAsync(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision")));

        var guardado = await Enviar(n, HttpMethod.Put, $"/casos/{caso}", new { ficha = Ficha(persona, "Otro"), motivo = "Cambio", versionBase = 1 });
        Assert.Equal(HttpStatusCode.OK, guardado.StatusCode);
        Assert.Equal("version_desactualizada", await CodigoErrorAsync(await Enviar(n, HttpMethod.Put, $"/casos/{caso}", new { ficha = Ficha(persona), motivo = "Viejo", versionBase = 1 })));
        Assert.Equal(2, (await Json(await n.GetAsync($"{Ruta}/casos/{caso}/versiones"))).GetArrayLength());

        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones", Evaluacion(1, 1));
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/envio-revision");
        await Enviar(n, HttpMethod.Post, $"/casos/{caso}/evaluaciones/2/votos", new { rol = "responsable_ia", voto = "aprobar" });
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/casos/{caso}/estado", new { estado = "en_produccion", motivo = "Sale" })).StatusCode);
        var reabierto = await Json(await Enviar(n, HttpMethod.Post, $"/casos/{caso}/estado", new { estado = "borrador", motivo = "Cambia el modelo" }));

        Assert.Equal("borrador", reabierto.GetProperty("caso").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, reabierto.GetProperty("caso").GetProperty("nivelRiesgo").ValueKind);
        Assert.Equal(JsonValueKind.Null, reabierto.GetProperty("aprobadoPor").ValueKind);
    }

    [Fact]
    public async Task Una_ficha_sembrada_no_llega_a_revision_incompleta()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        var casos = await Json(await n.GetAsync(Ruta + "/casos"));
        var id = casos.GetProperty("elementos")[0].GetProperty("id").GetString()!;

        var envio = await Enviar(n, HttpMethod.Post, $"/casos/{id}/envio-revision");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, envio.StatusCode);
        Assert.Contains((await Json(envio)).GetProperty("detalle").EnumerateArray(), f => f.GetString() == "dueño del caso");
    }

    [Fact]
    public async Task Una_politica_pendiente_no_se_publica_y_una_redactada_reemplaza_a_la_anterior()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;
        var sembrada = (await Json(await n.GetAsync(Ruta + "/politicas")))[0].GetProperty("id").GetString()!;

        Assert.Equal("sin_rol_gobierno", await CodigoErrorAsync(await Enviar(n, HttpMethod.Post, $"/politicas/{sembrada}/publicacion")));
        await DarRolAsync(n, persona, "comite");
        var pendiente = await Enviar(n, HttpMethod.Post, $"/politicas/{sembrada}/publicacion");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pendiente.StatusCode);
        Assert.Equal("politica_pendiente", await CodigoErrorAsync(pendiente));
        Assert.Equal("estado_invalido", await CodigoErrorAsync(await Enviar(n, HttpMethod.Post, $"/politicas/{sembrada}/aceptacion")));

        async Task<string> NuevaAsync(string version)
        {
            var r = await Enviar(n, HttpMethod.Post, "/politicas", new { codigo = "POL-PRUEBA", version, titulo = "Prueba", contenido = "Texto aprobado.", requiereAceptacion = true });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await Json(r)).GetProperty("politica").GetProperty("id").GetString()!;
        }

        var v1 = await NuevaAsync("1.0");
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/politicas/{v1}/publicacion")).StatusCode);
        Assert.Equal("estado_invalido", await CodigoErrorAsync(await Enviar(n, HttpMethod.Put, $"/politicas/{v1}", new { titulo = "X", contenido = "Reescrita", requiereAceptacion = true })));
        Assert.Equal(HttpStatusCode.NoContent, (await Enviar(n, HttpMethod.Post, $"/politicas/{v1}/aceptacion")).StatusCode);
        Assert.Equal("ya_aceptada", await CodigoErrorAsync(await Enviar(n, HttpMethod.Post, $"/politicas/{v1}/aceptacion")));
        Assert.Equal(1, (await Json(await n.GetAsync($"{Ruta}/politicas/{v1}/aceptaciones"))).GetProperty("aceptaciones").GetArrayLength());

        var v2 = await NuevaAsync("2.0");
        Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"/politicas/{v2}/publicacion")).StatusCode);

        Assert.Equal("reemplazada", (await Json(await n.GetAsync($"{Ruta}/politicas/{v1}"))).GetProperty("politica").GetProperty("estado").GetString());
        Assert.Equal("vigente", (await Json(await n.GetAsync($"{Ruta}/politicas/{v2}"))).GetProperty("politica").GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Un_empleado_lee_y_acepta_solo_las_politicas_vigentes_y_no_ve_lo_demas()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (gobierno, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var navGobierno = gobierno;
        await DarRolAsync(gobierno, persona, "comite");
        var (lector, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.Lector);
        using var navLector = lector;
        var borrador = (await Json(await gobierno.GetAsync(Ruta + "/politicas")))[0].GetProperty("id").GetString()!;
        Assert.Equal(0, (await Json(await lector.GetAsync(Ruta + "/politicas"))).GetArrayLength());

        async Task<string> PublicarAsync(string version)
        {
            var r = await Enviar(gobierno, HttpMethod.Post, "/politicas", new { codigo = "POL-LECTURA", version, titulo = "Lectura", contenido = "Texto aprobado.", requiereAceptacion = true });
            var id = (await Json(r)).GetProperty("politica").GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await Enviar(gobierno, HttpMethod.Post, $"/politicas/{id}/publicacion")).StatusCode);
            return id;
        }

        var v1 = await PublicarAsync("1.0");
        var v2 = await PublicarAsync("2.0");

        var lista = await Json(await lector.GetAsync(Ruta + "/politicas"));
        Assert.Equal(v2, Assert.Single(lista.EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"{Ruta}/politicas/{v2}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Enviar(lector, HttpMethod.Post, $"/politicas/{v2}/aceptacion")).StatusCode);

        // Lo que no está vigente no existe para el lector, y lo demás del módulo sigue cerrado.
        foreach (var id in new[] { v1, borrador })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{Ruta}/politicas/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lector, HttpMethod.Post, $"/politicas/{id}/aceptacion")).StatusCode);
        }
        foreach (var ruta in new[] { "/casos", "/tablero", $"/politicas/{v2}/aceptaciones" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync(Ruta + ruta)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(lector, HttpMethod.Put, $"/politicas/{borrador}", new { titulo = "X", contenido = "Y", requiereAceptacion = false })).StatusCode);
    }

    [Fact]
    public async Task Herramientas_asientos_e_incidentes_quedan_registrados_y_auditados()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, persona) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;

        var herramienta = await Enviar(n, HttpMethod.Post, "/herramientas", new { codigo = "herramienta-demo", nombre = "Demo", estado = "en_evaluacion", nivelDatoMaximo = "interno" });
        Assert.Equal(HttpStatusCode.Created, herramienta.StatusCode);
        var hid = (await Json(herramienta)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await Enviar(n, HttpMethod.Post, "/asientos", new { herramientaId = hid, usuarioNombre = "Persona Demo", correo = "demo@ejemplo.com", tipoCuenta = "corporativa", asignadoEn = "2026-11-01" })).StatusCode);
        Assert.Equal(1, (await Json(await n.GetAsync(Ruta + "/herramientas")))[0].GetProperty("asientosActivos").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(n, HttpMethod.Post, "/herramientas", new { codigo = "otra", nombre = "Otra", estado = "inventado", nivelDatoMaximo = "interno" })).StatusCode);

        object Incidente(string descripcion) => new { tipo = "INC-IA-02", severidad = "S2", descripcion, detectadoEn = "2026-11-01T10:00:00-05:00", reportadoSic = false, responsablePersonaId = persona, acciones = new[] { "Revocada" } };
        var primero = await Json(await Enviar(n, HttpMethod.Post, "/incidentes", Incidente("Llave en un log")));
        var segundo = await Json(await Enviar(n, HttpMethod.Post, "/incidentes", Incidente("Otra")));

        var anio = DateTime.UtcNow.AddHours(-5).Year;
        Assert.Equal($"INC-IA-{anio}-001", primero.GetProperty("codigo").GetString());
        Assert.Equal($"INC-IA-{anio}-002", segundo.GetProperty("codigo").GetString());
        var catalogo = await Json(await n.GetAsync(Ruta + "/incidentes/catalogo"));
        Assert.Equal(7, catalogo.GetProperty("tipos").GetArrayLength());
        Assert.Equal(4, catalogo.GetProperty("severidades").GetArrayLength());

        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var auditoria = await plataforma.Auditoria.AsNoTracking().Where(a => a.Accion == "gia.incidente_creado").ToListAsync(Ct);
        Assert.Equal(2, auditoria.Count);
        Assert.All(auditoria, a => Assert.DoesNotContain("Llave en un log", a.Datos ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_tablero_no_muestra_como_cero_lo_que_no_se_puede_medir()
    {
        var baseDeDatos = await BaseEncendidaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (n, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.GobiernoIa);
        using var nav = n;

        var tablero = await Json(await n.GetAsync(Ruta + "/tablero"));
        var kpis = tablero.GetProperty("kpis").EnumerateArray().ToDictionary(k => k.GetProperty("codigo").GetString()!);

        Assert.False(kpis["costo"].GetProperty("disponible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, kpis["costo"].GetProperty("valor").ValueKind);
        Assert.Equal("Sin datos hasta tener el gateway.", kpis["trafico_gateway"].GetProperty("nota").GetString());
        Assert.False(kpis["politica_ia01"].GetProperty("disponible").GetBoolean());
        Assert.False(kpis["configuracion_verificada"].GetProperty("disponible").GetBoolean());
        Assert.Equal(0, kpis["casos_aprobados"].GetProperty("valor").GetInt32());
        Assert.Equal(13, kpis["casos_aprobados"].GetProperty("total").GetInt32());
        var riesgos = tablero.GetProperty("casosPorRiesgo").EnumerateArray().ToDictionary(c => c.GetProperty("clave").GetString()!, c => c.GetProperty("total").GetInt32());
        Assert.Equal(5, riesgos["alto"]);
        Assert.Equal(2, riesgos["medio_alto"]);
        Assert.Equal(5, riesgos["medio"]);
        Assert.Equal(1, riesgos["bajo"]);
    }
}
