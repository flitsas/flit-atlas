using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Modulos.Equipo;
using Flit.Atlas.Modulos.Equipo.GitHub;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.3, 2.4 y 2.5 (ATLAS-ADR-0031): el módulo de equipo montado por el host con el catálogo sembrado. Importación del
/// documento ficticio con vista previa (CA: las tablas 2, 3.1 y 3.2 salen iguales; cambiar crea la 6; restaurar la 5 crea
/// la 7 igual a la 5), rol implícito de editor en el catálogo, traspaso acompañado, revisión con recordatorio, CODEOWNERS
/// con un GitHub simulado y «¿A quién llamo?» un sábado. Cada prueba usa su base.
/// </summary>
public class ModuloEquipoTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private static string Documento => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "responsables-ficticio.md"));

    private static readonly string[] Nombres =
    [
        "Persona Demo Uno", "Persona Demo Dos", "Persona Demo Tres", "Persona Demo Cuatro", "Persona Demo Cinco", "Persona Demo Seis", "Persona Demo Séptima",
    ];

    private sealed record Escenario(
        string Base, WebApplicationFactory<Program> Api, Navegador Lider, Guid LiderId, string CorreoLider, Guid GrupoLider,
        Dictionary<string, (Guid Id, string Correo)> Personas)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Lider.Dispose();
            await Api.DisposeAsync();
            // Cada prueba usa su base: se sueltan sus conexiones libres para no agotar las del contenedor compartido.
            using var conexion = new NpgsqlConnection(Base);
            NpgsqlConnection.ClearPool(conexion);
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta) => await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static Task<HttpResponseMessage> Enviar(Navegador n, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        n.EnviarAsync(metodo, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    private static async Task<Navegador> EntrarAsync(WebApplicationFactory<Program> api, string correo, string nombre, params Guid[] grupos)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::31:{Random.Shared.Next(1, 0xffff):x}";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, nombre, grupos)).StatusCode);
        return navegador;
    }

    /// <summary>
    /// Sesión de una persona ya creada en la base (como las del reparto): le crea su usuario y entra con el ingreso de
    /// prueba. El proveedor simulado no enlaza personas existentes por correo (solo Entra lo hace).
    /// </summary>
    private static async Task<Navegador> EntrarComoPersonaAsync(WebApplicationFactory<Program> api, Guid personaId, string correo)
    {
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
            var usuario = new UsuarioAtlas { PersonaId = personaId, UserName = correo, Email = correo, CreadoEn = DateTimeOffset.UtcNow };
            Assert.True((await usuarios.CreateAsync(usuario)).Succeeded);
            var navegador = Navegador.De(api);
            navegador.Ip = $"2001:db8::32:{Random.Shared.Next(1, 0xffff):x}";
            Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(usuario.Id)).StatusCode);
            return navegador;
        }
    }

    /// <summary>Base nueva (con la semilla del catálogo), el líder técnico y los siete empleados ficticios del documento.</summary>
    private async Task<Escenario> EscenarioAsync(TimeProvider? reloj = null, IReadOnlyDictionary<string, string>? configuracion = null,
        Action<IServiceCollection>? servicios = null)
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var config = new Dictionary<string, string>(configuracion ?? new Dictionary<string, string>())
        {
            ["Equipo:Revision:Habilitada"] = "false",
        };
        var api = entorno.Api(baseDeDatos: baseDeDatos, reloj: reloj, configuracion: config);
        if (servicios is not null)
        {
            api = api.WithWebHostBuilder(b => b.ConfigureTestServices(servicios));
        }
        var correoLider = $"lider.{Guid.NewGuid():N}@ejemplo.com";
        var grupo = await entorno.GrupoConRolAsync(RolesAtlas.LiderTecnico, baseDeDatos);
        var lider = await EntrarAsync(api, correoLider, "Líder Demo", grupo);
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var liderPersona = await plataforma.Personas.SingleAsync(p => p.Correo == correoLider, Ct);
        var personas = Nombres.Select((n, i) => new Persona
        {
            Tipo = TipoPersona.Empleado,
            OrganizacionId = liderPersona.OrganizacionId,
            Nombre = n,
            Correo = $"persona.{i + 1}.{Guid.NewGuid():N}@ejemplo.com",
            Telefono = "+57 300 000 0000",
        }).ToList();
        plataforma.Personas.AddRange(personas);
        await plataforma.SaveChangesAsync(Ct);
        return new Escenario(baseDeDatos, api, lider, liderPersona.Id, correoLider, grupo, personas.ToDictionary(p => p.Nombre, p => (p.Id, p.Correo)));
    }

    private static async Task<JsonElement> ImportarAsync(Navegador lider, string? documento = null, string? traspaso = null)
    {
        var vista = await JsonAsync(await Enviar(lider, HttpMethod.Post, "/api/v1/equipo/importacion/vista-previa", new { markdown = documento ?? Documento }));
        Assert.True(vista.GetProperty("puedeConfirmar").GetBoolean(), vista.ToString());
        var respuesta = await Enviar(lider, HttpMethod.Post, "/api/v1/equipo/importacion",
            new { markdown = documento ?? Documento, huella = vista.GetProperty("huella").GetString(), traspaso });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return await JsonAsync(respuesta);
    }

    private static string Tabla(string titulo)
    {
        var lineas = Documento.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inicio = Array.FindIndex(lineas, l => l.StartsWith(titulo, StringComparison.Ordinal));
        return string.Concat(lineas.Skip(inicio + 1).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Select(l => l + "\n"));
    }

    private static async Task<Guid> AplicativoAsync(Navegador n, string slug) =>
        (await JsonAsync(await n.GetAsync($"/api/v1/catalogo/aplicativos/{slug}"))).GetProperty("id").GetGuid();

    [Fact]
    public async Task CA_importa_la_version_5_y_las_tablas_2_3_1_y_3_2_se_reproducen_exactamente()
    {
        await using var e = await EscenarioAsync();

        var vista = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion/vista-previa", new { markdown = Documento }));
        Assert.Equal(5, vista.GetProperty("version").GetInt32());
        Assert.Equal(52, vista.GetProperty("filas").GetArrayLength());
        Assert.Equal(0, vista.GetProperty("pendientes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, vista.GetProperty("versionActual").ValueKind);
        // Nada se guardó todavía.
        Assert.Equal(0, (await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones"))).GetArrayLength());

        var version = await ImportarAsync(e.Lider);
        Assert.Equal(5, version.GetProperty("numero").GetInt32());
        Assert.Equal("importacion", version.GetProperty("origen").GetString());
        Assert.Equal("Persona Demo Uno (Líder técnico)", version.GetProperty("definidoPor").GetString());
        Assert.Equal("Líder Demo", version.GetProperty("autor").GetString());

        var tablas = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades/tablas"));
        Assert.Equal(Tabla("## 2."), tablas.GetProperty("personas").GetString());
        Assert.Equal(Tabla("### 3.1"), tablas.GetProperty("productos").GetString());
        Assert.Equal(Tabla("### 3.2"), tablas.GetProperty("modulos").GetString());

        var reparto = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades"));
        Assert.Equal(9, reparto.GetProperty("productos").GetArrayLength());
        Assert.Equal(17, reparto.GetProperty("modulos").GetArrayLength());
        Assert.Equal(7, reparto.GetProperty("porPersona").GetArrayLength());
        var definiciones = reparto.GetProperty("definiciones");
        Assert.Equal("Responsable principal", definiciones[0].GetProperty("titulo").GetString());
        Assert.Equal("Dedicación orientativa: ~20 % de su tiempo en ese módulo.", definiciones[1].GetProperty("puntos")[2].GetString());
        var vigia = reparto.GetProperty("productos")[0];
        Assert.Equal("vigia", vigia.GetProperty("aplicativo").GetProperty("slug").GetString());
        Assert.Equal(20, vigia.GetProperty("secundario").GetProperty("dedicacion").GetInt32());
        Assert.Equal("habilitado", vigia.GetProperty("secundario").GetProperty("estadoTraspaso").GetString());

        // La revisión trimestral del documento quedó programada.
        var revisiones = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/revisiones"));
        Assert.Equal("2027-01-20", revisiones.GetProperty("proxima").GetProperty("fecha").GetString());

        // Reimportar la misma versión no se permite: el reparto ya va en la 5.
        var otra = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion/vista-previa", new { markdown = Documento }));
        Assert.False(otra.GetProperty("puedeConfirmar").GetBoolean());
        await using var plataforma = entorno.Plataforma(e.Base);
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "equipo.reparto_importado" && a.RecursoId == "5", Ct));
    }

    [Fact]
    public async Task CA_cambiar_un_responsable_crea_la_6_y_restaurar_la_5_crea_la_7_igual_a_la_5()
    {
        await using var e = await EscenarioAsync();
        await ImportarAsync(e.Lider);
        var vigia = await AplicativoAsync(e.Lider, "vigia");

        var cambio = await Enviar(e.Lider, HttpMethod.Put, "/api/v1/equipo/responsabilidades", new
        {
            aplicativoId = vigia, tipo = "secundario", personaId = e.Personas["Persona Demo Dos"].Id, nota = "Respaldo de Vigía mientras dura el proyecto.",
        });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var seis = await JsonAsync(cambio);
        Assert.Equal(6, seis.GetProperty("numero").GetInt32());
        Assert.Equal("cambio", seis.GetProperty("origen").GetString());
        Assert.Equal("Líder Demo", seis.GetProperty("autor").GetString());
        Assert.Equal("Respaldo de Vigía mientras dura el proyecto.", seis.GetProperty("nota").GetString());

        var comparacion = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones/comparacion?desde=5&hasta=6"));
        Assert.False(comparacion.GetProperty("iguales").GetBoolean());
        var diferencia = Assert.Single(comparacion.GetProperty("cambios").EnumerateArray());
        Assert.Equal(("vigia", "secundario", "persona", "Persona Demo Uno", "Persona Demo Dos"),
            (diferencia.GetProperty("aplicativo").GetProperty("slug").GetString(), diferencia.GetProperty("tipo").GetString(),
             diferencia.GetProperty("cambio").GetString(), diferencia.GetProperty("antes").GetString(), diferencia.GetProperty("despues").GetString()));
        // El secundario nuevo empieza en acompañamiento.
        var reparto = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades"));
        Assert.Equal("en_acompanamiento", reparto.GetProperty("productos")[0].GetProperty("secundario").GetProperty("estadoTraspaso").GetString());

        var restaurar = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/versiones/5/restauracion", new { nota = "Volvemos al reparto de la versión 5." });
        Assert.Equal(HttpStatusCode.Created, restaurar.StatusCode);
        var siete = await JsonAsync(restaurar);
        Assert.Equal((7, "restauracion", 5), (siete.GetProperty("numero").GetInt32(), siete.GetProperty("origen").GetString(), siete.GetProperty("restauradaDe").GetInt32()));

        var versiones = (await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones"))).EnumerateArray().ToList();
        Assert.Equal([7, 6, 5], versiones.Select(v => v.GetProperty("numero").GetInt32()));
        Assert.Equal(versiones[2].GetProperty("huella").GetString(), versiones[0].GetProperty("huella").GetString());
        Assert.True((await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones/comparacion?desde=5&hasta=7"))).GetProperty("iguales").GetBoolean());
        var tablas = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades/tablas"));
        Assert.Equal(Tabla("## 2."), tablas.GetProperty("personas").GetString());
        Assert.Equal(Tabla("### 3.1"), tablas.GetProperty("productos").GetString());
        var detalle = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones/7"));
        Assert.Equal(Tabla("### 3.2"), detalle.GetProperty("tablas").GetProperty("modulos").GetString());
        Assert.Equal(2, detalle.GetProperty("definiciones").GetArrayLength());
    }

    [Fact]
    public async Task El_responsable_recibe_permisos_de_editor_solo_sobre_su_aplicativo_y_lo_que_cuelga_de_el()
    {
        await using var e = await EscenarioAsync();
        await ImportarAsync(e.Lider);
        // Persona Demo Siete es principal de Certify, Reportes, Ayuda y Doctor FLIT, y secundaria de Firmas.
        var (septima, correo) = e.Personas["Persona Demo Séptima"];
        using var responsable = await EntrarComoPersonaAsync(e.Api, septima, correo);

        async Task<bool> PuedeEditar(string slug) =>
            (await JsonAsync(await responsable.GetAsync($"/api/v1/catalogo/aplicativos/{slug}"))).GetProperty("permisos").GetProperty("editar").GetBoolean();

        Assert.True(await PuedeEditar("certify"));
        Assert.True(await PuedeEditar("firmas"));
        Assert.True(await PuedeEditar("flit2-reportes"));
        Assert.False(await PuedeEditar("flito"));
        Assert.False(await PuedeEditar("flit2-comparendos"));
        var yo = await JsonAsync(await responsable.GetAsync("/api/v1/plataforma/yo"));
        Assert.Contains(RolesAtlas.Responsable, yo.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // Lo que cuelga del producto también (el catálogo evalúa los ancestros): un servicio de Certify.
        var hijos = await JsonAsync(await responsable.GetAsync($"/api/v1/catalogo/aplicativos?padreId={await AplicativoAsync(responsable, "certify")}"));
        foreach (var hijo in hijos.GetProperty("elementos").EnumerateArray())
        {
            Assert.True(await PuedeEditar(hijo.GetProperty("slug").GetString()!));
        }

        // El servidor lo hace cumplir: editar Certify sí, FLITO no.
        async Task<HttpStatusCode> Editar(string slug)
        {
            var f = await JsonAsync(await responsable.GetAsync($"/api/v1/catalogo/aplicativos/{slug}"));
            var cuerpo = new
            {
                slug, nombre = f.GetProperty("nombre").GetString(), tipo = f.GetProperty("tipo").GetString(), padreId = (Guid?)null,
                grupo = f.GetProperty("grupo").GetString(), descripcion = "Editado por su responsable.", estado = f.GetProperty("estado").GetString(),
                criticidad = f.GetProperty("criticidad").GetString(), manejaDatosPersonales = f.GetProperty("manejaDatosPersonales").GetBoolean(),
                repo = f.GetProperty("repo").GetString(), rutaEnRepo = f.GetProperty("rutaEnRepo").GetString(),
                tecnologias = f.GetProperty("tecnologias").EnumerateArray().Select(t => t.GetString()).ToArray(),
                ambientes = f.GetProperty("ambientes").EnumerateArray().Select(t => t.GetString()).ToArray(),
                etiquetas = f.GetProperty("etiquetas").EnumerateArray().Select(t => t.GetString()).ToArray(),
            };
            return (await Enviar(responsable, HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{f.GetProperty("id").GetGuid()}", cuerpo)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await Editar("certify"));
        Assert.Equal(HttpStatusCode.Forbidden, await Editar("flito"));

        // Al retirarla como principal de Certify, pierde la edición en la petición siguiente.
        var reparto = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades"));
        var certify = reparto.GetProperty("productos").EnumerateArray().Single(f => f.GetProperty("aplicativo").GetProperty("slug").GetString() == "certify");
        var retiro = await e.Lider.EnviarAsync(HttpMethod.Delete,
            $"/api/v1/equipo/responsabilidades/{certify.GetProperty("principal").GetProperty("id").GetGuid()}?nota=Cambio%20de%20prueba");
        Assert.Equal(HttpStatusCode.OK, retiro.StatusCode);
        Assert.Equal(6, (await JsonAsync(retiro)).GetProperty("numero").GetInt32());
        Assert.False(await PuedeEditar("certify"));
        Assert.True(await PuedeEditar("firmas"));
    }

    [Fact]
    public async Task El_traspaso_exige_un_despliegue_y_una_guardia_antes_de_habilitar()
    {
        await using var e = await EscenarioAsync();
        await ImportarAsync(e.Lider, traspaso: "en_acompanamiento");
        var traspasos = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/traspasos"));
        Assert.Equal(26, traspasos.GetArrayLength());
        var primero = traspasos[0];
        Assert.Equal("vigia", primero.GetProperty("aplicativo").GetProperty("slug").GetString());
        var id = primero.GetProperty("responsabilidadId").GetGuid();

        var temprano = await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/equipo/responsabilidades/{id}/traspaso/habilitacion");
        Assert.Equal(HttpStatusCode.Conflict, temprano.StatusCode);
        Assert.Equal("checklist_incompleto", (await JsonAsync(temprano)).GetProperty("codigo").GetString());

        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/equipo/responsabilidades/{id}/traspaso/actividades",
            new { tipo = "despliegue", nota = "Despliegue a qa con el principal." })).StatusCode);
        var conGuardia = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/equipo/responsabilidades/{id}/traspaso/actividades", new { tipo = "guardia" }));
        Assert.True(conGuardia.GetProperty("completo").GetBoolean());
        Assert.Equal(2, conGuardia.GetProperty("actividades").GetArrayLength());

        var habilitado = await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/equipo/responsabilidades/{id}/traspaso/habilitacion");
        Assert.Equal(HttpStatusCode.OK, habilitado.StatusCode);
        Assert.Equal("habilitado", (await JsonAsync(habilitado)).GetProperty("estado").GetString());
        Assert.Equal(25, (await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/traspasos"))).GetArrayLength());
        // Un principal no tiene traspaso.
        var reparto = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/responsabilidades"));
        var principal = reparto.GetProperty("productos")[0].GetProperty("principal").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/equipo/responsabilidades/{principal}/traspaso/actividades", new { tipo = "guardia" })).StatusCode);
    }

    [Fact]
    public async Task La_revision_trimestral_avisa_al_lider_una_sola_vez_y_al_cerrarla_programa_la_siguiente()
    {
        var reloj = new RelojDePrueba();
        await using var e = await EscenarioAsync(reloj);
        await ImportarAsync(e.Lider);

        // Lejos de la fecha no avisa; a una semana, sí, y una sola vez.
        await using (var alcance = e.Api.Services.CreateAsyncScope())
        {
            Assert.Equal(0, await RecordatorioRevision.EnviarSiCorrespondeAsync(alcance.ServiceProvider, 7, Ct));
        }
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), Bogota).DateTime);
        reloj.Adelantar(TimeSpan.FromDays(new DateOnly(2027, 1, 15).DayNumber - hoy.DayNumber));
        await using (var alcance = e.Api.Services.CreateAsyncScope())
        {
            Assert.Equal(1, await RecordatorioRevision.EnviarSiCorrespondeAsync(alcance.ServiceProvider, 7, Ct));
        }
        await using (var alcance = e.Api.Services.CreateAsyncScope())
        {
            Assert.Equal(0, await RecordatorioRevision.EnviarSiCorrespondeAsync(alcance.ServiceProvider, 7, Ct));
        }
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            var aviso = await plataforma.Notificaciones.SingleAsync(n => n.Tipo == "equipo.revision_trimestral", Ct);
            Assert.Equal(e.LiderId, aviso.PersonaId);
            Assert.Contains("20/01/2027", aviso.Cuerpo, StringComparison.Ordinal);
        }

        // La sesión de antes ya venció con el reloj adelantado: el líder vuelve a entrar.
        using var lider = await EntrarAsync(e.Api, e.CorreoLider, "Líder Demo", e.GrupoLider);
        var revisiones = await JsonAsync(await lider.GetAsync("/api/v1/equipo/revisiones"));
        var proxima = revisiones.GetProperty("proxima");
        Assert.NotEqual(JsonValueKind.Null, proxima.GetProperty("recordatorioEnviadoEn").ValueKind);
        var cierre = await JsonAsync(await Enviar(lider, HttpMethod.Post, $"/api/v1/equipo/revisiones/{proxima.GetProperty("id").GetGuid()}/cierre",
            new { nota = "Reparto revisado sin cambios." }));
        Assert.Equal("2027-04-20", cierre.GetProperty("proxima").GetProperty("fecha").GetString());
        Assert.Equal("Líder Demo", cierre.GetProperty("historial")[0].GetProperty("hechaPor").GetString());
    }

    /// <summary>GitHub simulado para CODEOWNERS: un archivo en <c>.github/</c> del repo de Atlas.</summary>
    private sealed class GitHubSimulado : HttpMessageHandler
    {
        public int Peticiones { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticiones++;
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/repos/flitsas/flit-atlas/contents/.github/CODEOWNERS"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("* @demo-uno @otra-persona\n/docs/ @demo-uno\n") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task Genera_el_codeowners_sugerido_y_lo_compara_con_el_de_github_simulado()
    {
        var github = new GitHubSimulado();
        await using var e = await EscenarioAsync(
            configuracion: new Dictionary<string, string> { ["Plataforma:GitHub:Token"] = "token-de-prueba" },
            servicios: s => s.AddHttpClient<ClienteGitHub>().ConfigurePrimaryHttpMessageHandler(() => github));
        await ImportarAsync(e.Lider);
        var uno = e.Personas["Persona Demo Uno"].Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(e.Lider, HttpMethod.Put, $"/api/v1/equipo/personas/{uno}/github", new { usuario = "-mal-" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(e.Lider, HttpMethod.Put, $"/api/v1/equipo/personas/{uno}/github", new { usuario = "@demo-uno" })).StatusCode);

        var sugerido = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/codeowners"));
        Assert.True(sugerido.GetProperty("gitHubConfigurado").GetBoolean());
        var atlas = sugerido.GetProperty("repos").EnumerateArray().Single(r => r.GetProperty("repo").GetString() == "flitsas/flit-atlas");
        var raiz = atlas.GetProperty("lineas").EnumerateArray().First();
        Assert.Equal("*", raiz.GetProperty("patron").GetString());
        Assert.Equal(["@demo-uno"], raiz.GetProperty("propietarios").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(["Persona Demo Seis"], raiz.GetProperty("sinUsuario").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("* @demo-uno", atlas.GetProperty("texto").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, sugerido.GetProperty("personasSinUsuario").GetArrayLength());

        var diferencias = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/codeowners/diferencias?repo=flitsas/flit-atlas"));
        Assert.Equal(".github/CODEOWNERS", diferencias.GetProperty("archivo").GetString());
        var porPatron = diferencias.GetProperty("diferencias").EnumerateArray().ToDictionary(d => d.GetProperty("patron").GetString()!, d => d.GetProperty("estado").GetString());
        Assert.Equal("distinto", porPatron["*"]);
        Assert.Equal("solo_en_github", porPatron["/docs/"]);
        Assert.True(github.Peticiones > 0);

        var ficha = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/aplicativos/atlas/ficha-responsables"));
        Assert.Contains("| Responsable principal | Persona Demo Uno | @demo-uno |", ficha.GetProperty("markdown").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_github_configurado_la_pantalla_lo_dice_y_no_falla()
    {
        await using var e = await EscenarioAsync();
        await ImportarAsync(e.Lider);

        var diferencias = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/codeowners/diferencias?repo=flitsas/flit-atlas"));

        Assert.False(diferencias.GetProperty("configurado").GetBoolean());
        Assert.Contains("Plataforma:GitHub", diferencias.GetProperty("mensaje").GetString(), StringComparison.Ordinal);
        Assert.False((await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/codeowners"))).GetProperty("gitHubConfigurado").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Lider.GetAsync("/api/v1/equipo/codeowners/diferencias?repo=sin-barra")).StatusCode);
    }

    [Fact]
    public async Task A_quien_llamo_un_sabado_muestra_el_soporte_de_turno_y_los_responsables_con_contacto_segun_permisos()
    {
        // Próximo sábado a las 10:00 de Bogotá.
        var reloj = new RelojDePrueba();
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), Bogota);
        var dias = ((int)DayOfWeek.Saturday - (int)ahora.DayOfWeek + 7) % 7;
        var sabado = DateOnly.FromDateTime(ahora.Date.AddDays(dias));
        var objetivo = new DateTimeOffset(sabado.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-5));
        reloj.Adelantar(objetivo - reloj.GetUtcNow());
        await using var e = await EscenarioAsync(reloj);
        await ImportarAsync(e.Lider);

        var deTurno = e.Personas["Persona Demo Cuatro"].Id;
        Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{SoporteDbContext.EquipoDesarrolloTi}/participantes",
            new { personaId = deTurno })).StatusCode);
        var turno = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new
        {
            equipoId = SoporteDbContext.EquipoDesarrolloTi, fecha = sabado.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), personaId = deTurno,
        });
        Assert.Equal(HttpStatusCode.Created, turno.StatusCode);

        var llamar = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/aplicativos/flito/a-quien-llamo"));
        Assert.True(llamar.GetProperty("esSabado").GetBoolean());
        var equipos = llamar.GetProperty("soporte").GetProperty("equipos").EnumerateArray().ToList();
        var desarrollo = equipos.Single(q => q.GetProperty("id").GetGuid() == SoporteDbContext.EquipoDesarrolloTi);
        var persona = Assert.Single(desarrollo.GetProperty("personas").EnumerateArray());
        Assert.Equal("Persona Demo Cuatro", persona.GetProperty("nombre").GetString());
        Assert.Equal("+57 300 000 0000", persona.GetProperty("telefono").GetString());
        Assert.Equal("08:00", desarrollo.GetProperty("horaInicio").GetString());
        // Operaciones sin nadie ese sábado: aparece vacío (los equipos por área se muestran todos).
        Assert.Equal(0, equipos.Single(q => q.GetProperty("id").GetGuid() == SoporteDbContext.EquipoOperaciones).GetProperty("personas").GetArrayLength());
        Assert.Equal("Persona Demo Cuatro", llamar.GetProperty("principal").GetProperty("persona").GetProperty("nombre").GetString());
        Assert.Equal("Persona Demo Seis", llamar.GetProperty("secundario").GetProperty("persona").GetProperty("nombre").GetString());

        // Un empleado sin atlas.equipo.ver_contacto ve los nombres y el correo, sin teléfono.
        using var lector = await EntrarAsync(e.Api, $"lector.{Guid.NewGuid():N}@ejemplo.com", "Lector Demo");
        var sinContacto = await JsonAsync(await lector.GetAsync("/api/v1/equipo/aplicativos/flito/a-quien-llamo"));
        Assert.False(sinContacto.GetProperty("veContacto").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sinContacto.GetProperty("principal").GetProperty("persona").GetProperty("telefono").ValueKind);
        Assert.Equal(JsonValueKind.Null, sinContacto.GetProperty("soporte").GetProperty("equipos").EnumerateArray()
            .Single(q => q.GetProperty("id").GetGuid() == SoporteDbContext.EquipoDesarrolloTi).GetProperty("personas")[0].GetProperty("telefono").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, sinContacto.GetProperty("principal").GetProperty("persona").GetProperty("correo").ValueKind);

        // Un servicio sin responsables propios hereda los de su producto.
        var hijos = await JsonAsync(await e.Lider.GetAsync($"/api/v1/catalogo/aplicativos?padreId={await AplicativoAsync(e.Lider, "flito")}"));
        var hijo = hijos.GetProperty("elementos")[0].GetProperty("slug").GetString();
        var heredado = await JsonAsync(await e.Lider.GetAsync($"/api/v1/equipo/aplicativos/{hijo}/responsables"));
        Assert.Equal("flito", heredado.GetProperty("heredadoDe").GetProperty("slug").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await e.Lider.GetAsync("/api/v1/equipo/aplicativos/no-existe/a-quien-llamo")).StatusCode);
    }

    [Fact]
    public async Task Las_vistas_por_persona_y_el_mapa_de_carga_salen_del_reparto()
    {
        await using var e = await EscenarioAsync();
        await ImportarAsync(e.Lider);

        var seis = await JsonAsync(await e.Lider.GetAsync($"/api/v1/equipo/personas/{e.Personas["Persona Demo Seis"].Id}/responsabilidades"));
        Assert.Equal(["Comparendos", "Suite", "Migración", "Identidad", "Usuarios", "Notificaciones"],
            seis.GetProperty("principalDe").EnumerateArray().Select(r => r.GetProperty("aplicativo").GetProperty("nombre").GetString()));
        Assert.Equal(4, seis.GetProperty("secundarioDe").GetArrayLength());

        var carga = await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/carga"));
        Assert.Equal(4.46m, carga.GetProperty("media").GetDecimal());
        var primero = carga.GetProperty("personas")[0];
        Assert.Equal(("Persona Demo Seis", "alta"), (primero.GetProperty("persona").GetProperty("nombre").GetString(), primero.GetProperty("nivel").GetString()));

        var (idUno, correo) = e.Personas["Persona Demo Uno"];
        using var uno = await EntrarComoPersonaAsync(e.Api, idUno, correo);
        var mias = await JsonAsync(await uno.GetAsync("/api/v1/equipo/yo/responsabilidades"));
        Assert.Equal(5, mias.GetProperty("principalDe").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await e.Lider.GetAsync($"/api/v1/equipo/personas/{Guid.NewGuid()}/responsabilidades")).StatusCode);
    }

    [Fact]
    public async Task La_importacion_rechaza_una_vista_previa_vieja_y_lo_que_no_se_pudo_enlazar()
    {
        await using var e = await EscenarioAsync();
        var vista = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion/vista-previa", new { markdown = Documento }));

        var cambiado = Documento.Replace("| Vigía | Persona Demo Tres | Persona Demo Uno |", "| Vigía | Persona Demo Tres | Persona Demo Dos |", StringComparison.Ordinal);
        var vieja = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion", new { markdown = cambiado, huella = vista.GetProperty("huella").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
        Assert.Equal("vista_previa_cambio", (await JsonAsync(vieja)).GetProperty("codigo").GetString());

        var desconocida = Documento.Replace("| FLITO | Persona Demo Cuatro |", "| FLITO | Persona Inexistente |", StringComparison.Ordinal);
        var conPendiente = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion/vista-previa", new { markdown = desconocida }));
        Assert.False(conPendiente.GetProperty("puedeConfirmar").GetBoolean());
        var pendiente = Assert.Single(conPendiente.GetProperty("pendientes").EnumerateArray());
        Assert.Equal("Persona Inexistente", pendiente.GetProperty("nombre").GetString());
        var rechazada = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/equipo/importacion",
            new { markdown = desconocida, huella = conPendiente.GetProperty("huella").GetString() });
        Assert.Equal("hay_pendientes", (await JsonAsync(rechazada)).GetProperty("codigo").GetString());
        Assert.Equal(0, (await JsonAsync(await e.Lider.GetAsync("/api/v1/equipo/versiones"))).GetArrayLength());
    }
}
