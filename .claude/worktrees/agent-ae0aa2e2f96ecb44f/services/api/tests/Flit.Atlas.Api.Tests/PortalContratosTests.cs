using System.Net;
using System.Text.Json;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Api.Tests.EscenarioPortal;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 4.3 y 4.4 (ATLAS-ADR-0043) con atlas-api completo: compartir un paquete publicado con una organización (vigencia,
/// exportar, sandbox, revisión fijada), cambiar y revocar con reautenticación y auditoría; el portal del integrador
/// (consolidado, detalle, spec podado con las URL públicas, guías, cambios y avisos de deprecación) y el alcance de los
/// demás roles externos. El aislamiento entre organizaciones lo prueba <see cref="AislamientoPortalTests"/>.
/// </summary>
public sealed class PortalContratosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Ruta(Guid paquete) => $"/api/v1/contratos/paquetes/{paquete}/comparticiones";

    [Fact]
    public async Task Compartir_cambiar_revocar_y_reactivar_con_reautenticacion_reglas_y_auditoria()
    {
        var reloj = new RelojDePrueba();
        await using var escenario = new EscenarioPortal(entorno, reloj: reloj);
        var (lider, liderId) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (aplicativo, slug) = await AplicativoAsync(lider);
        var (desarrollador, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador);
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-compartida", Spec("API Compartida Demo", "compartidos", "Compartido"));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Compartido Demo", contrato, version, ["GET /compartidos/{id}"]);
        var cliente = await escenario.OrganizacionAsync("Organismo Demo Cliente");
        var proveedor = await escenario.OrganizacionAsync("Proveedor Demo", TipoOrganizacion.Proveedor);
        Guid interna;
        await using (var plataforma = entorno.Plataforma())
        {
            interna = await plataforma.Organizaciones.Where(o => o.Tipo == TipoOrganizacion.Interna).Select(o => o.Id).FirstAsync(Ct);
        }

        // Las opciones: clientes y proveedores activos, sin FLIT.
        var opciones = (await Ok(await lider.GetAsync("/api/v1/contratos/comparticiones/organizaciones"))).EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(cliente, opciones);
        Assert.Contains(proveedor, opciones);
        Assert.DoesNotContain(interna, opciones);

        // Reglas: FLIT no, un proveedor con vencimiento, una revisión que existe y un paquete publicado.
        async Task<IReadOnlyList<string>> Errores(object cuerpo, Guid? otro = null)
        {
            var respuesta = await Enviar(lider, HttpMethod.Post, Ruta(otro ?? paquete), cuerpo);
            return [.. (await Ok(respuesta, HttpStatusCode.BadRequest)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
        }
        Assert.Equal(["organizacionId"], await Errores(new { organizacionId = interna, puedeExportar = true, puedeSandbox = false }));
        Assert.Equal(["vigenteHasta"], await Errores(new { organizacionId = proveedor, puedeExportar = true, puedeSandbox = false }));
        Assert.Equal(["revisionFijada"], await Errores(new { organizacionId = cliente, revisionFijada = 9, puedeExportar = true, puedeSandbox = false }));
        Assert.Equal(["vigenteHasta"], await Errores(new { organizacionId = cliente, puedeExportar = true, puedeSandbox = false, vigenteHasta = DateTimeOffset.UtcNow.AddDays(-1) }));
        var borrador = await Ok(await Enviar(lider, HttpMethod.Post, "/api/v1/contratos/paquetes", new
        {
            nombre = "Paquete Borrador Demo", descripcion = (string?)null, nota = "Borrador.", revisionBase = (int?)null, definicion = Array.Empty<object>(),
        }), HttpStatusCode.Created);
        var noPublicado = await Enviar(lider, HttpMethod.Post, Ruta(borrador.GetProperty("paquete").GetProperty("id").GetGuid()),
            new { organizacionId = cliente, puedeExportar = true, puedeSandbox = false });
        Assert.Equal("no_publicado", (await Ok(noPublicado, HttpStatusCode.Conflict)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lider, HttpMethod.Post, Ruta(Guid.NewGuid()), new { organizacionId = cliente })).StatusCode);

        // Compartir: 201, vigente; dos veces, 409; el proveedor con vencimiento sí.
        var creada = await Ok(await Enviar(lider, HttpMethod.Post, Ruta(paquete), new { organizacionId = cliente, puedeExportar = true, puedeSandbox = false }),
            HttpStatusCode.Created);
        var id = creada.GetProperty("id").GetGuid();
        Assert.Equal(("vigente", 1, true, false), (creada.GetProperty("estado").GetString(), creada.GetProperty("revisionQueVe").GetInt32(),
            creada.GetProperty("puedeExportar").GetBoolean(), creada.GetProperty("puedeSandbox").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, creada.GetProperty("revisionFijada").ValueKind);
        Assert.Equal(cliente, creada.GetProperty("organizacion").GetProperty("id").GetGuid());
        var repetida = await Enviar(lider, HttpMethod.Post, Ruta(paquete), new { organizacionId = cliente, puedeExportar = false, puedeSandbox = false });
        Assert.Equal("ya_compartido", (await Ok(repetida, HttpStatusCode.Conflict)).GetProperty("codigo").GetString());
        await CompartirAsync(lider, paquete, proveedor, hasta: DateTimeOffset.UtcNow.AddDays(30));

        // «Compartido con» y la página de la organización; quien solo lee no comparte.
        var lista = await Ok(await lider.GetAsync(Ruta(paquete)));
        Assert.True(lista.GetProperty("puedeCompartir").GetBoolean());
        Assert.Equal(2, lista.GetProperty("elementos").GetArrayLength());
        var lectura = await Ok(await desarrollador.GetAsync(Ruta(paquete)));
        Assert.False(lectura.GetProperty("puedeCompartir").GetBoolean());
        Assert.All(lectura.GetProperty("elementos").EnumerateArray(), c => Assert.False(c.GetProperty("puedeGestionar").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(desarrollador, HttpMethod.Post, Ruta(paquete), new { organizacionId = cliente })).StatusCode);
        var deLaOrganizacion = await Ok(await lider.GetAsync($"/api/v1/contratos/organizaciones/{cliente}/comparticiones"));
        Assert.Equal(paquete, Assert.Single(deLaOrganizacion.GetProperty("elementos").EnumerateArray()).GetProperty("paqueteId").GetGuid());

        // Cambiar: exportar no, sandbox sí, vencimiento; con una revisión fijada que existe.
        var cambiada = await Ok(await Enviar(lider, HttpMethod.Put, $"{Ruta(paquete)}/{id}", new
        {
            revisionFijada = 1, puedeExportar = false, puedeSandbox = true, vigenteHasta = DateTimeOffset.UtcNow.AddDays(10),
        }));
        Assert.Equal((1, false, true), (cambiada.GetProperty("revisionFijada").GetInt32(), cambiada.GetProperty("puedeExportar").GetBoolean(),
            cambiada.GetProperty("puedeSandbox").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lider, HttpMethod.Put, $"{Ruta(paquete)}/{Guid.NewGuid()}", new { puedeExportar = true })).StatusCode);

        // Revocar: la historia queda; revocar o cambiar otra vez, 409; compartir otra vez la reactiva (la misma fila).
        var revocada = await Ok(await Enviar(lider, HttpMethod.Delete, $"{Ruta(paquete)}/{id}"));
        Assert.Equal("revocada", revocada.GetProperty("estado").GetString());
        Assert.Equal("revocada", (await Ok(await Enviar(lider, HttpMethod.Delete, $"{Ruta(paquete)}/{id}"), HttpStatusCode.Conflict)).GetProperty("codigo").GetString());
        Assert.Equal("revocada", (await Ok(await Enviar(lider, HttpMethod.Put, $"{Ruta(paquete)}/{id}", new { puedeExportar = true }), HttpStatusCode.Conflict))
            .GetProperty("codigo").GetString());
        var reactivada = await Ok(await Enviar(lider, HttpMethod.Post, Ruta(paquete), new { organizacionId = cliente, puedeExportar = true, puedeSandbox = false }),
            HttpStatusCode.Created);
        Assert.Equal((id, "vigente"), (reactivada.GetProperty("id").GetGuid(), reactivada.GetProperty("estado").GetString()));

        // Acción sensible (§6.3): con un ingreso de hace más de 10 minutos, compartir pide reautenticación.
        reloj.Adelantar(TimeSpan.FromMinutes(11));
        var vieja = await Enviar(lider, HttpMethod.Delete, $"{Ruta(paquete)}/{id}");
        Assert.Equal("reautenticacion_requerida", (await Ok(vieja, HttpStatusCode.Unauthorized)).GetProperty("codigo").GetString());

        // Auditado en la misma transacción, con la organización destino y sin nombres.
        await using var auditoria = entorno.Plataforma();
        var acciones = await auditoria.Auditoria.Where(a => a.RecursoTipo == "contratos.comparticion" && a.RecursoId == id.ToString())
            .OrderBy(a => a.OcurridoEn).ThenBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(["contratos.comparticion_creada", "contratos.comparticion_cambiada", "contratos.comparticion_revocada", "contratos.comparticion_creada"],
            acciones.Select(a => a.Accion));
        Assert.All(acciones, a => Assert.Equal((cliente, liderId.ToString()), (a.OrganizacionId!.Value, a.ActorId)));
        Assert.All(acciones, a => Assert.DoesNotContain("Paquete Compartido", a.Datos, StringComparison.Ordinal));
        Assert.True(JsonDocument.Parse(acciones[3].Datos!).RootElement.GetProperty("reactivada").GetBoolean());
        _ = aplicativo;
    }

    [Fact]
    public async Task El_integrador_ve_su_consolidado_el_spec_podado_con_las_URL_publicas_las_guias_los_cambios_y_los_avisos()
    {
        var slug = $"api-portal-int-{Guid.NewGuid().ToString("N")[..8]}";
        await using var escenario = new EscenarioPortal(entorno, new Dictionary<string, string>
        {
            ["Contratos:Portal:Servidores:0:Contrato"] = $"{slug}/api-tramites",
            ["Contratos:Portal:Servidores:0:Ambiente"] = "qa",
            ["Contratos:Portal:Servidores:0:Urls:0"] = "https://qa.api.ejemplo.com/tramites",
        });
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (aplicativo, _) = await AplicativoAsync(lider, "Trámites Demo", slug);
        var (responsable, _) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador, responsableDe: aplicativo);
        const string Changelog = """
            [{"id":"response-property-added","text":"Se agregó la propiedad estado","level":1,"operation":"GET","path":"/tramites/{id}"},
             {"id":"new-required-request-property","text":"Cambió el pago interno","level":3,"operation":"POST","path":"/tramites-pagos-internos"}]
            """;
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-tramites", Spec("API Trámites Demo", "tramites", "Tramite"), changelog: Changelog);
        var guia = await GuiaCompartibleAsync(lider, responsable, contrato, "Cómo autenticarte", "Usa `Bearer $TOKEN`.");
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Trámites Demo", contrato, version, ["GET /tramites/{id}"], guia);
        var organizacion = await escenario.OrganizacionAsync("Organismo Demo Integrador");
        await CompartirAsync(lider, paquete, organizacion, exportar: true, sandbox: false);
        var integrador = await escenario.ExternoAsync(organizacion, RolesAtlas.Integrador);
        var portal = integrador.Navegador;

        // Inicio del cliente: su aplicativo, su paquete con nivel técnico y lo que puede hacer.
        var inicio = await Ok(await portal.GetAsync("/api/v1/portal/inicio"));
        Assert.Equal(organizacion, inicio.GetProperty("organizacion").GetProperty("id").GetGuid());
        var tarjeta = Assert.Single(inicio.GetProperty("aplicativos").EnumerateArray());
        Assert.Equal(("Trámites Demo", 1), (tarjeta.GetProperty("aplicativo").GetProperty("nombre").GetString(), tarjeta.GetProperty("contratos").GetInt32()));
        Assert.Empty(tarjeta.GetProperty("documentos").EnumerateArray());
        var resumen = Assert.Single(inicio.GetProperty("paquetes").EnumerateArray());
        Assert.Equal((paquete, "tecnico", true, false), (resumen.GetProperty("id").GetGuid(), resumen.GetProperty("nivel").GetString(),
            resumen.GetProperty("puedeExportar").GetBoolean(), resumen.GetProperty("puedeSandbox").GetBoolean()));
        Assert.Empty(inicio.GetProperty("avisos").EnumerateArray());

        // Detalle: solo la operación compartida y su guía.
        var detalle = await Ok(await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}"));
        var delPaquete = Assert.Single(detalle.GetProperty("contratos").EnumerateArray());
        Assert.Equal(["GET /tramites/{id}"], delPaquete.GetProperty("operaciones").EnumerateArray().Select(o => o.GetProperty("clave").GetString()));
        Assert.Equal(guia, Assert.Single(delPaquete.GetProperty("guias").EnumerateArray()).GetProperty("id").GetGuid());

        // Spec podado: sin la operación interna, sin su esquema, sin el servidor interno; con las URL públicas del ambiente.
        var spec = await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/spec");
        Assert.Equal(HttpStatusCode.OK, spec.StatusCode);
        Assert.Equal("application/yaml", spec.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(spec.Headers.ETag);
        var texto = await spec.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/tramites/{id}", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("pagos-internos", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("PagoInterno", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("servidor-interno", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("servers", texto, StringComparison.Ordinal);
        var enQa = await (await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/spec?ambiente=qa")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("https://qa.api.ejemplo.com/tramites", enQa, StringComparison.Ordinal);
        Assert.DoesNotContain("servidor-interno", enQa, StringComparison.Ordinal);
        var invalido = await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/spec?ambiente=produccion-interna");
        Assert.Equal("ambiente_invalido", (await Ok(invalido, HttpStatusCode.UnprocessableEntity)).GetProperty("codigo").GetString());

        // La guía (la revisión que fijó el paquete) y los cambios (solo los de la operación compartida).
        var deGuia = await Ok(await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}/guias/{guia}"));
        Assert.Equal(("Cómo autenticarte", "Usa `Bearer $TOKEN`.", 1), (deGuia.GetProperty("titulo").GetString(), deGuia.GetProperty("contenido").GetString(),
            deGuia.GetProperty("revision").GetInt32()));
        var cambios = await Ok(await portal.GetAsync($"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/cambios"));
        Assert.Equal(["response-property-added"], cambios.GetProperty("elementos").EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        // Buscar dentro de lo compartido: la operación compartida sí, la interna no.
        var busqueda = (await Ok(await portal.GetAsync("/api/v1/portal/buscar?q=tramites"))).EnumerateArray().ToList();
        Assert.Contains(busqueda, r => r.GetProperty("tipo").GetString() == "operacion" && r.GetProperty("titulo").GetString() == "GET /tramites/{id}");
        Assert.DoesNotContain(busqueda, r => r.GetProperty("titulo").GetString()!.Contains("pagos", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.GetAsync("/api/v1/portal/buscar?q=a")).StatusCode);

        // Al deprecar la versión, el aviso llega al inicio.
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.Database.ExecuteSqlInterpolatedAsync($"UPDATE atlas.contratos_version SET estado = 'deprecada', retiro_en = '2027-06-30' WHERE id = {version}", Ct);
        }
        var aviso = Assert.Single((await Ok(await portal.GetAsync("/api/v1/portal/inicio"))).GetProperty("avisos").EnumerateArray());
        Assert.Equal(("deprecada", "1.0.0"), (aviso.GetProperty("tipo").GetString(), aviso.GetProperty("version").GetString()));
        Assert.Contains("30/06/2027", aviso.GetProperty("mensaje").GetString(), StringComparison.Ordinal);

        // Toda vista queda auditada con la organización y la persona.
        await using var auditoria = entorno.Plataforma();
        var vistas = await auditoria.Auditoria.Where(a => a.ActorId == integrador.PersonaId.ToString() && a.RecursoTipo == "contratos.portal")
            .Select(a => a.Accion).ToListAsync(Ct);
        foreach (var accion in new[] { "contratos.portal_inicio_visto", "contratos.portal_paquete_visto", "contratos.portal_spec_visto", "contratos.portal_guia_vista",
                     "contratos.portal_cambios_vistos", "contratos.portal_busqueda" })
        {
            Assert.Contains(accion, vistas);
        }
        Assert.All(await auditoria.Auditoria.Where(a => a.ActorId == integrador.PersonaId.ToString()).ToListAsync(Ct),
            a => Assert.Equal(organizacion, a.OrganizacionId));
        Assert.DoesNotContain(await auditoria.Auditoria.Where(a => a.Accion == "contratos.portal_busqueda" && a.ActorId == integrador.PersonaId.ToString())
            .Select(a => a.Datos).ToListAsync(Ct), d => d!.Contains("tramites", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cada_rol_externo_ve_lo_que_le_toca_segun_su_rol_y_el_alcance_de_su_invitacion()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider);
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-roles", Spec("API Roles Demo", "roles", "Rol"));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Roles Demo", contrato, version, ["GET /roles/{id}"]);
        var organizacion = await escenario.OrganizacionAsync("Organismo Demo Roles");
        await CompartirAsync(lider, paquete, organizacion, exportar: true, sandbox: true);
        var delPaquete = new[] { (TiposAlcance.Paquete, paquete) };

        async Task<(string? Nivel, bool Exportar, bool Sandbox, HttpStatusCode Spec)> Ver(PersonaExterna p)
        {
            var paquetes = (await Ok(await p.Navegador.GetAsync("/api/v1/portal/inicio"))).GetProperty("paquetes").EnumerateArray().ToList();
            var spec = (await p.Navegador.GetAsync($"/api/v1/portal/paquetes/{paquete}/contratos/{contrato}/spec")).StatusCode;
            return paquetes.Count == 0
                ? (null, false, false, spec)
                : (paquetes[0].GetProperty("nivel").GetString(), paquetes[0].GetProperty("puedeExportar").GetBoolean(), paquetes[0].GetProperty("puedeSandbox").GetBoolean(), spec);
        }

        // Integrador: técnico, exporta y sandbox (la compartición y su permiso).
        Assert.Equal(("tecnico", true, true, HttpStatusCode.OK), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.Integrador)));
        // Asesor comercial: resumen, sin detalle técnico.
        Assert.Equal(("resumen", false, false, HttpStatusCode.Forbidden), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.AsesorComercial)));
        // Cliente funcional: resumen, salvo que su invitación nombre el paquete; exporta solo con el permiso extra.
        Assert.Equal(("resumen", false, false, HttpStatusCode.Forbidden), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.ClienteFuncional)));
        Assert.Equal(("tecnico", false, false, HttpStatusCode.OK), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.ClienteFuncional, delPaquete)));
        Assert.Equal(("tecnico", true, false, HttpStatusCode.OK),
            await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.ClienteFuncional, delPaquete, [AccesoExtra.Exportar])));
        // Proveedor: solo lo concedido explícitamente.
        Assert.Equal((null, false, false, HttpStatusCode.NotFound), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.Proveedor)));
        Assert.Equal(("tecnico", false, true, HttpStatusCode.OK),
            await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.Proveedor, delPaquete, [AccesoExtra.Sandbox])));
        // Administrador de la organización: resumen (invita dentro de ese alcance).
        Assert.Equal(("resumen", false, false, HttpStatusCode.Forbidden), await Ver(await escenario.ExternoAsync(organizacion, RolesAtlas.AdminOrganizacion)));

        // Un empleado con atlas.portal.ver ve el portal de su organización (FLIT no tiene comparticiones): vacío.
        var (soporte, _) = await escenario.EmpleadoAsync(RolesAtlas.Soporte);
        Assert.Empty((await Ok(await soporte.GetAsync("/api/v1/portal/inicio"))).GetProperty("paquetes").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await soporte.GetAsync($"/api/v1/portal/paquetes/{paquete}")).StatusCode);
    }

    /// <summary>Los permisos extra de una invitación (§6.2).</summary>
    private static class AccesoExtra
    {
        public const string Exportar = "exportar";
        public const string Sandbox = "sandbox";
    }
}
