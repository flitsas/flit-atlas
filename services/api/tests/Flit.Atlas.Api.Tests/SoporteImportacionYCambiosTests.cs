using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Pruebas.Soporte;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.7 (importador del Excel con revisión) y HU 2.9 (cambios de turno y compensaciones) montados por el host
/// (ATLAS-ADR-0029 y ATLAS-ADR-0032). El Excel se genera en la prueba con datos ficticios. Cada prueba usa su base.
/// </summary>
public class SoporteImportacionYCambiosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private static readonly Guid Desarrollo = SoporteDbContext.EquipoDesarrolloTi;

    private static DateOnly Hoy => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime);

    private static DateOnly ProximoSabado
    {
        get
        {
            var manana = Hoy.AddDays(1);
            return manana.AddDays(((int)DayOfWeek.Saturday - (int)manana.DayOfWeek + 7) % 7);
        }
    }

    private static string Iso(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Escenario(string Base, WebApplicationFactory<Program> Api, Navegador Lider, Guid LiderId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Lider.Dispose();
            await Api.DisposeAsync();
        }
    }

    private async Task<(Navegador Navegador, Guid Persona)> EmpleadoAsync(WebApplicationFactory<Program> api, string baseDeDatos, string nombre, params Guid[] grupos)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::27:{Random.Shared.Next(1, 0xffff):x}";
        var correo = $"soporte.{Guid.NewGuid():N}@ejemplo.com";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, nombre, grupos)).StatusCode);
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        return (navegador, await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct));
    }

    private async Task<Escenario> EscenarioAsync()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var api = entorno.Api(baseDeDatos: baseDeDatos);
        var (lider, id) = await EmpleadoAsync(api, baseDeDatos, "Líder Demo", await entorno.GrupoConRolAsync(RolesAtlas.LiderTecnico, baseDeDatos));
        return new Escenario(baseDeDatos, api, lider, id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static Task<HttpResponseMessage> Enviar(Navegador n, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        n.EnviarAsync(metodo, ruta, cuerpo is null ? null : JsonContent.Create(cuerpo));

    // --- HU 2.7 ---

    private async Task<Dictionary<string, Guid>> PersonasDelExcelAsync(Escenario e)
    {
        await using var plataforma = entorno.Plataforma(e.Base);
        var organizacion = await plataforma.Personas.Where(p => p.Id == e.LiderId).Select(p => p.OrganizacionId).SingleAsync(Ct);
        var personas = new[] { ExcelFicticio.Personas.Ana, ExcelFicticio.Personas.Bruno, ExcelFicticio.Personas.Carla, ExcelFicticio.Personas.Diego }
            .Select(n => new Persona { Tipo = TipoPersona.Empleado, OrganizacionId = organizacion, Nombre = n, Correo = $"p.{Guid.NewGuid():N}@ejemplo.com" })
            .ToList();
        plataforma.Personas.AddRange(personas);
        await plataforma.SaveChangesAsync(Ct);
        return personas.ToDictionary(p => p.Nombre, p => p.Id);
    }

    private static object Pedido(byte[] archivo, IEnumerable<string>? crear = null, string? huella = null) => new
    {
        contenido = Convert.ToBase64String(archivo),
        nombreArchivo = "rotacion-ficticia.xlsx",
        crearInactivas = crear?.ToArray() ?? [],
        huella,
    };

    [Fact]
    public async Task Importa_el_excel_con_vista_previa_informe_persona_inactiva_y_reimportar_no_duplica()
    {
        await using var e = await EscenarioAsync();
        var personas = await PersonasDelExcelAsync(e);
        var archivo = ExcelFicticio.HojaDeEjemplo(ProximoSabado.AddDays(7));

        // Primera vista previa: la persona que no existe en Atlas se propone, pero su fila no entra sin confirmarla.
        var previa = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", Pedido(archivo)));
        Assert.Equal("Desarrollo TI", previa.GetProperty("equipo").GetProperty("nombre").GetString());
        var nueva = Assert.Single(previa.GetProperty("personasNuevas").EnumerateArray());
        Assert.False(nueva.GetProperty("confirmada").GetBoolean());
        Assert.Equal(6, previa.GetProperty("resumen").GetProperty("importadas").GetInt32());
        Assert.NotEmpty(previa.GetProperty("avisos").EnumerateArray());
        Assert.Empty(await TurnosAsync(e));

        var clave = nueva.GetProperty("clave").GetString()!;
        previa = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", Pedido(archivo, [clave])));
        var resumen = previa.GetProperty("resumen");
        Assert.Equal((7, 7, 0, 0), (resumen.GetProperty("filas").GetInt32(), resumen.GetProperty("importadas").GetInt32(),
            resumen.GetProperty("yaImportadas").GetInt32(), resumen.GetProperty("omitidas").GetInt32()));
        Assert.Equal((9, 2, 1, 5), (resumen.GetProperty("turnos").GetInt32(), resumen.GetProperty("compensaciones").GetInt32(),
            resumen.GetProperty("personasInactivas").GetInt32(), resumen.GetProperty("participantes").GetInt32()));
        Assert.Equal((12, 2), (resumen.GetProperty("interpretaciones").GetInt32(), resumen.GetProperty("ambiguos").GetInt32()));
        // El informe: cada interpretación y cada caso ambiguo, por fila.
        var filas = previa.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal(12, filas.Sum(f => f.GetProperty("interpretaciones").GetArrayLength()));
        Assert.Equal(2, filas.Count(f => f.GetProperty("requiereRevision").GetBoolean()));
        Assert.Equal("parcial", filas[6].GetProperty("responsable").GetProperty("coincidencia").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/confirmar", Pedido(archivo, [clave], new string('0', 64)))).StatusCode);
        var confirmada = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/confirmar", Pedido(archivo, [clave], previa.GetProperty("huella").GetString()));
        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);
        Assert.Equal(7, (await JsonAsync(confirmada)).GetProperty("informe").GetProperty("resumen").GetProperty("importadas").GetInt32());

        var turnos = await TurnosAsync(e);
        Assert.Equal(9, turnos.Count);
        Assert.All(turnos, t => Assert.Equal(OrigenTurno.Importado, t.Origen));
        Assert.All(turnos, t => Assert.Equal((new TimeOnly(8, 0), new TimeOnly(12, 0)), (t.HoraInicio!.Value, t.HoraFin!.Value)));
        // «Diego reemplazo y compensa»: el turno de Carla queda reemplazado, Diego lo tomó y tiene su compensación.
        var reemplazado = turnos.Single(t => t.Fecha == new DateOnly(2026, 6, 27) && t.PersonaId == personas[ExcelFicticio.Personas.Carla]);
        Assert.Equal(EstadoTurno.Reemplazado, reemplazado.Estado);
        var deDiego = turnos.Single(t => t.TurnoReemplazadoId == reemplazado.Id);
        Assert.Equal((personas[ExcelFicticio.Personas.Diego], EstadoTurno.Aplicado), (deDiego.PersonaId, deDiego.Estado));
        await using (var soporte = Soporte(e))
        {
            var compensaciones = await soporte.Compensaciones.AsNoTracking().ToListAsync(Ct);
            Assert.Equal(2, compensaciones.Count);
            Assert.Contains(compensaciones, c => c.TurnoId == deDiego.Id && c.PersonaId == deDiego.PersonaId && c.FechaCompensada is null);
            Assert.Contains(compensaciones, c => c.PersonaId == personas[ExcelFicticio.Personas.Bruno] && c.FechaCompensada == new DateOnly(2026, 11, 8));
            Assert.Equal(3, turnos.Count(t => t.RequiereRevision));
            Assert.Contains(turnos, t => t.Observacion == "Compensado: lo cubrió alguien del equipo" && t.RequiereRevision);
        }

        // La persona que no existía quedó inactiva, en la rotación como inactiva, y la generación no la usa.
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            var creada = await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Nombre == ExcelFicticio.Personas.Externa, Ct);
            Assert.Equal(EstadoPersona.Inactiva, creada.Estado);
            await using var soporte = Soporte(e);
            var participantes = await soporte.Participantes.AsNoTracking().Where(p => p.EquipoId == Desarrollo).OrderBy(p => p.Orden).ToListAsync(Ct);
            Assert.Equal(5, participantes.Count);
            Assert.False(participantes.Single(p => p.PersonaId == creada.Id).Activo);
            var generacion = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/generacion/vista-previa", new { equipoId = Desarrollo, meses = 3 }));
            Assert.DoesNotContain(generacion.GetProperty("turnosPorPersona").EnumerateArray(), c => c.GetProperty("personaId").GetGuid() == creada.Id);
            var auditoria = await plataforma.Auditoria.AsNoTracking().SingleAsync(a => a.Accion == "soporte.importacion_confirmada", Ct);
            Assert.DoesNotContain("Elena", auditoria.Datos, StringComparison.Ordinal);
        }

        // Reimportar el mismo archivo no duplica nada: la persona ya existe y cada fila está importada.
        var otraVez = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", Pedido(archivo, [clave])));
        Assert.Equal((0, 7, 0), (otraVez.GetProperty("resumen").GetProperty("importadas").GetInt32(),
            otraVez.GetProperty("resumen").GetProperty("yaImportadas").GetInt32(), otraVez.GetProperty("resumen").GetProperty("turnos").GetInt32()));
        Assert.Empty(otraVez.GetProperty("personasNuevas").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/confirmar",
            Pedido(archivo, [clave], otraVez.GetProperty("huella").GetString()))).StatusCode);
        Assert.Equal(9, (await TurnosAsync(e)).Count);
        await using (var plataforma = entorno.Plataforma(e.Base))
        {
            Assert.Equal(1, await plataforma.Personas.CountAsync(p => p.Nombre == ExcelFicticio.Personas.Externa, Ct));
        }
    }

    [Fact]
    public async Task El_importador_exige_gestionar_el_equipo_y_valida_el_archivo()
    {
        await using var e = await EscenarioAsync();
        var (desarrollador, _) = await EmpleadoAsync(e.Api, e.Base, "Desarrolladora Demo", await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, e.Base));
        var archivo = ExcelFicticio.HojaDeEjemplo(ProximoSabado);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Enviar(desarrollador, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", Pedido(archivo))).StatusCode);
        var malo = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", new { contenido = "%%%" });
        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        var noXlsx = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa", new { contenido = Convert.ToBase64String("hola"u8.ToArray()) });
        Assert.Equal(HttpStatusCode.BadRequest, noXlsx.StatusCode);
        Assert.Contains("xlsx", (await noXlsx.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        var otroEquipo = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/importacion/vista-previa",
            new { contenido = Convert.ToBase64String(archivo), equipoId = SoporteDbContext.EquipoOperaciones });
        Assert.Equal("Operaciones", (await JsonAsync(otroEquipo)).GetProperty("equipo").GetProperty("nombre").GetString());
        desarrollador.Dispose();
    }

    private static SoporteDbContext Soporte(Escenario e)
    {
        var opciones = new DbContextOptionsBuilder<SoporteDbContext>();
        SoporteDbContext.Configurar(opciones, e.Base);
        return new SoporteDbContext(opciones.Options);
    }

    private static async Task<List<Turno>> TurnosAsync(Escenario e)
    {
        await using var soporte = Soporte(e);
        return await soporte.Turnos.AsNoTracking().OrderBy(t => t.Fecha).ThenBy(t => t.CreadoEn).ToListAsync(Ct);
    }

    // --- HU 2.9 ---

    private sealed record Participantes(Navegador Ana, Guid AnaId, Navegador Bruno, Guid BrunoId, Guid TurnoId) : IDisposable
    {
        public void Dispose()
        {
            Ana.Dispose();
            Bruno.Dispose();
        }
    }

    /// <summary>Dos desarrolladores de Desarrollo TI y un turno manual de la primera el próximo sábado.</summary>
    private async Task<Participantes> ParticipantesAsync(Escenario e)
    {
        var grupo = await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, e.Base);
        var (ana, anaId) = await EmpleadoAsync(e.Api, e.Base, "Ana Demo", grupo);
        var (bruno, brunoId) = await EmpleadoAsync(e.Api, e.Base, "Bruno Demo", grupo);
        foreach (var persona in new[] { anaId, brunoId })
        {
            Assert.Equal(HttpStatusCode.Created, (await Enviar(e.Lider, HttpMethod.Post, $"/api/v1/soporte/equipos/{Desarrollo}/participantes", new { personaId = persona })).StatusCode);
        }
        var turno = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/turnos", new { equipoId = Desarrollo, fecha = ProximoSabado, personaId = anaId }));
        return new Participantes(ana, anaId, bruno, brunoId, turno.GetProperty("id").GetGuid());
    }

    private async Task<List<Notificacion>> AvisosAsync(Escenario e, Guid persona)
    {
        await using var plataforma = entorno.Plataforma(e.Base);
        return await plataforma.Notificaciones.AsNoTracking().Where(n => n.PersonaId == persona && n.Tipo == "soporte.cambio_turno").ToListAsync(Ct);
    }

    [Fact]
    public async Task Solicitud_aceptacion_y_aprobacion_con_historial_avisos_e_idempotencia()
    {
        await using var e = await EscenarioAsync();
        using var p = await ParticipantesAsync(e);
        var llave = Guid.NewGuid();
        var pedido = new { turnoId = p.TurnoId, reemplazoPersonaId = p.BrunoId, motivo = "Cita médica", llaveIdempotencia = llave, hechaEn = DateTimeOffset.UtcNow.AddMinutes(-30) };

        // Solo la persona de turno pide el cambio; reenviar la misma acción (cola sin conexión) no la duplica.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(p.Bruno, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio",
            new { turnoId = p.TurnoId, reemplazoPersonaId = p.AnaId, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        var creada = await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio", pedido);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var solicitud = await JsonAsync(creada);
        var id = solicitud.GetProperty("id").GetGuid();
        Assert.Equal("pendiente_reemplazo", solicitud.GetProperty("estado").GetString());
        var repetida = await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio", pedido);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(id, (await JsonAsync(repetida)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio",
            new { turnoId = p.TurnoId, reemplazoPersonaId = p.BrunoId, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        Assert.Single(await AvisosAsync(e, p.BrunoId));

        // El reemplazo acepta (solo él); quien aprueba no puede adelantarse.
        var ruta = $"/api/v1/soporte/solicitudes-cambio/{id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(p.Ana, HttpMethod.Post, $"{ruta}/respuesta", new { acepta = true, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(e.Lider, HttpMethod.Post, $"{ruta}/resolucion", new { aprueba = true, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/resolucion", new { aprueba = true, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        var respuesta = new { acepta = true, nota = "Con gusto", llaveIdempotencia = Guid.NewGuid() };
        var aceptada = await JsonAsync(await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/respuesta", respuesta));
        Assert.Equal("pendiente_aprobacion", aceptada.GetProperty("estado").GetString());
        Assert.Equal("pendiente_aprobacion", (await JsonAsync(await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/respuesta", respuesta))).GetProperty("estado").GetString());
        Assert.Single(await AvisosAsync(e, p.AnaId));
        Assert.Single(await AvisosAsync(e, e.LiderId));
        var bandeja = (await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/solicitudes-cambio"))).EnumerateArray().ToList();
        Assert.True(Assert.Single(bandeja).GetProperty("puedeResolver").GetBoolean());

        // Aprobar: el original queda reemplazado y Bruno tiene su turno; reenviar la aprobación no crea otro.
        var resolucion = new { aprueba = true, nota = "Aprobado", llaveIdempotencia = Guid.NewGuid() };
        var aprobada = await JsonAsync(await Enviar(e.Lider, HttpMethod.Post, $"{ruta}/resolucion", resolucion));
        Assert.Equal("aprobada", aprobada.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Enviar(e.Lider, HttpMethod.Post, $"{ruta}/resolucion", resolucion)).StatusCode);
        var historial = aprobada.GetProperty("historial").EnumerateArray().ToList();
        Assert.Equal(["solicitada", "aceptada", "aprobada"], historial.Select(h => h.GetProperty("accion").GetString()));
        Assert.Equal([p.AnaId, p.BrunoId, e.LiderId], historial.Select(h => h.GetProperty("persona").GetProperty("id").GetGuid()));
        Assert.Equal(["Cita médica", "Con gusto", "Aprobado"], historial.Select(h => h.GetProperty("nota").GetString()));
        Assert.NotEqual(JsonValueKind.Null, historial[0].GetProperty("hechaEn").ValueKind);
        var turnos = await TurnosAsync(e);
        Assert.Equal(2, turnos.Count);
        Assert.Equal(EstadoTurno.Reemplazado, turnos.Single(t => t.Id == p.TurnoId).Estado);
        var nuevo = turnos.Single(t => t.TurnoReemplazadoId == p.TurnoId);
        Assert.Equal((p.BrunoId, EstadoTurno.Programado), (nuevo.PersonaId, nuevo.Estado));
        Assert.Equal(nuevo.Id, aprobada.GetProperty("turnoNuevoId").GetGuid());
        Assert.Equal(2, (await AvisosAsync(e, p.AnaId)).Count);
        Assert.Equal(2, (await AvisosAsync(e, p.BrunoId)).Count);
        // La llave de una acción no sirve para otra.
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(p.Ana, HttpMethod.Post, $"{ruta}/cancelacion", new { llaveIdempotencia = resolucion.llaveIdempotencia })).StatusCode);
        await using var plataforma = entorno.Plataforma(e.Base);
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "soporte.solicitud_cambio_aprobada", Ct));
        Assert.True(await plataforma.Auditoria.AnyAsync(a => a.Accion == "soporte.turno_reemplazado", Ct));
    }

    [Fact]
    public async Task El_reemplazo_puede_rechazar_y_quien_pide_puede_cancelar()
    {
        await using var e = await EscenarioAsync();
        using var p = await ParticipantesAsync(e);
        var primera = await JsonAsync(await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio",
            new { turnoId = p.TurnoId, reemplazoPersonaId = p.BrunoId, llaveIdempotencia = Guid.NewGuid() }));
        var ruta = $"/api/v1/soporte/solicitudes-cambio/{primera.GetProperty("id").GetGuid()}";
        var rechazada = await JsonAsync(await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/respuesta", new { acepta = false, llaveIdempotencia = Guid.NewGuid() }));
        Assert.Equal("rechazada", rechazada.GetProperty("estado").GetString());
        Assert.Equal("rechazada_por_reemplazo", rechazada.GetProperty("historial")[1].GetProperty("accion").GetString());

        var segunda = await JsonAsync(await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/solicitudes-cambio",
            new { turnoId = p.TurnoId, reemplazoPersonaId = p.BrunoId, llaveIdempotencia = Guid.NewGuid() }));
        ruta = $"/api/v1/soporte/solicitudes-cambio/{segunda.GetProperty("id").GetGuid()}";
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/cancelacion", new { llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        var cancelada = await JsonAsync(await Enviar(p.Ana, HttpMethod.Post, $"{ruta}/cancelacion", new { nota = "Ya puedo", llaveIdempotencia = Guid.NewGuid() }));
        Assert.Equal("cancelada", cancelada.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(p.Bruno, HttpMethod.Post, $"{ruta}/respuesta", new { acepta = true, llaveIdempotencia = Guid.NewGuid() })).StatusCode);
        Assert.Equal(2, (await JsonAsync(await p.Ana.GetAsync("/api/v1/soporte/solicitudes-cambio?estado=cerradas"))).GetArrayLength());
        Assert.Empty((await JsonAsync(await p.Ana.GetAsync("/api/v1/soporte/solicitudes-cambio"))).EnumerateArray());
        // Quien no participa ni aprueba no la ve.
        var (otro, _) = await EmpleadoAsync(e.Api, e.Base, "Otra Persona Demo");
        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync(ruta)).StatusCode);
        otro.Dispose();
        Assert.Single(await TurnosAsync(e));
    }

    [Fact]
    public async Task Compensaciones_se_registran_con_quien_aprueba_y_salen_en_el_reporte_de_pendientes()
    {
        await using var e = await EscenarioAsync();
        using var p = await ParticipantesAsync(e);

        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(p.Ana, HttpMethod.Post, "/api/v1/soporte/compensaciones", new { turnoId = p.TurnoId })).StatusCode);
        var creada = await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/compensaciones", new { turnoId = p.TurnoId, nota = "Por el cierre" });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var compensacion = await JsonAsync(creada);
        Assert.Equal(p.AnaId, compensacion.GetProperty("persona").GetProperty("id").GetGuid());
        Assert.Equal(e.LiderId, compensacion.GetProperty("aprobadaPor").GetProperty("id").GetGuid());
        Assert.Equal("pendiente", compensacion.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(e.Lider, HttpMethod.Post, "/api/v1/soporte/compensaciones", new { turnoId = p.TurnoId })).StatusCode);
        Assert.Single(await AvisosAsync(e, p.AnaId));

        var reporte = await JsonAsync(await p.Bruno.GetAsync("/api/v1/soporte/compensaciones/pendientes"));
        Assert.Equal(1, reporte.GetProperty("total").GetInt32());
        var fila = Assert.Single(reporte.GetProperty("personas").EnumerateArray());
        Assert.Equal((p.AnaId, 1, 1), (fila.GetProperty("persona").GetProperty("id").GetGuid(), fila.GetProperty("pendientes").GetInt32(), fila.GetProperty("sinFecha").GetInt32()));
        // La nota solo la ven la persona compensada y quien aprueba.
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("compensaciones")[0].GetProperty("nota").ValueKind);
        Assert.Equal("Por el cierre", (await JsonAsync(await p.Ana.GetAsync("/api/v1/soporte/compensaciones?estado=pendiente")))[0].GetProperty("nota").GetString());

        var ruta = $"/api/v1/soporte/compensaciones/{compensacion.GetProperty("id").GetGuid()}";
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(e.Lider, HttpMethod.Put, ruta, new { estado = "tomada" })).StatusCode);
        var tomada = await JsonAsync(await Enviar(e.Lider, HttpMethod.Put, ruta, new { estado = "tomada", fechaCompensada = Iso(Hoy) }));
        Assert.Equal("tomada", tomada.GetProperty("estado").GetString());
        Assert.Equal(0, (await JsonAsync(await e.Lider.GetAsync("/api/v1/soporte/compensaciones/pendientes"))).GetProperty("total").GetInt32());
        var indicadores = await JsonAsync(await e.Lider.GetAsync($"/api/v1/soporte/indicadores?desde={Iso(ProximoSabado)}&hasta={Iso(ProximoSabado)}"));
        Assert.Equal(0, indicadores.GetProperty("compensacionesPendientes").GetInt32());
    }
}
