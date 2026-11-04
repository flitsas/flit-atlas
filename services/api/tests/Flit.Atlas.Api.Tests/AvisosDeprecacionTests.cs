using System.Net;
using Flit.Atlas.Modulos.Contratos.Deprecacion;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Flit.Atlas.Api.Tests.EscenarioPortal;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 4.8 (ATLAS-ADR-0046): deprecar una versión avisa por la bandeja, push y correo a las personas de las organizaciones
/// que la ven en el portal (comparticiones vigentes, mismas reglas de <c>AccesoPortal</c>) y a los responsables del
/// aplicativo; no a otras organizaciones, ni con la compartición revocada o vencida, ni fuera del alcance de la persona.
/// Idempotente, auditado y con recordatorios 30 y 7 días antes según el reloj (un <see cref="FakeTimeProvider"/>). Corre
/// <see cref="AvisosDeprecacion"/> por versión (el trabajo programado está apagado en las pruebas: la base es compartida).
/// </summary>
public sealed class AvisosDeprecacionTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Tipo = "contratos.aviso_deprecacion";

    private sealed record Base(
        EscenarioPortal Escenario, FakeTimeProvider Reloj, Navegador Lider, Guid PersonaResponsable, Guid Contrato, Guid Version, Guid Paquete, string Slug) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Escenario.DisposeAsync();
    }

    private async Task<Base> PrepararAsync(string nombre)
    {
        var reloj = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var escenario = new EscenarioPortal(entorno, reloj: reloj);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (aplicativo, slug) = await AplicativoAsync(lider, $"Aplicativo {nombre}");
        var (_, responsable) = await escenario.EmpleadoAsync(RolesAtlas.Desarrollador, responsableDe: aplicativo);
        var codigo = $"api-{nombre.ToLowerInvariant()}";
        var (contrato, version) = await escenario.PublicarAsync(slug, codigo, Spec($"API {nombre}", nombre.ToLowerInvariant(), nombre));
        var paquete = await PaquetePublicadoAsync(lider, $"Paquete {nombre}", contrato, version, [$"GET /{nombre.ToLowerInvariant()}/{{id}}"]);
        return new Base(escenario, reloj, lider, responsable, contrato, version, paquete, slug);
    }

    private static async Task<ResultadoAvisos> CorrerAsync(Base b)
    {
        await using var alcance = b.Escenario.Api.Services.CreateAsyncScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<AvisosDeprecacion>().CorrerAsync(b.Version, Ct);
        Assert.NotNull(resultado);
        return resultado;
    }

    private static Task<HttpResponseMessage> DeprecarAsync(Base b, DateOnly retiro, string version = "1.0.0") =>
        Enviar(b.Lider, HttpMethod.Post, $"/api/v1/contratos/{b.Contrato}/versiones/{version}/deprecar", new { retiroEn = retiro.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });

    private async Task<(int Bandeja, IReadOnlyList<Flit.Atlas.Plataforma.Correo.MensajeCorreo> Correos)> AvisosDeAsync(Base b, Guid persona)
    {
        await using var plataforma = entorno.Plataforma();
        var correo = await plataforma.Personas.AsNoTracking().Where(p => p.Id == persona).Select(p => p.Correo).SingleAsync(Ct);
        var bandeja = await plataforma.Notificaciones.AsNoTracking().CountAsync(n => n.PersonaId == persona && n.Tipo == Tipo, Ct);
        return (bandeja, b.Escenario.Api.Services.GetRequiredService<BuzonDePrueba>().Para(correo));
    }

    [Fact]
    public async Task Al_deprecar_avisa_solo_a_quien_ve_la_version_en_el_portal_y_a_los_responsables_sin_repetir()
    {
        await using var b = await PrepararAsync("Retiro");
        var e = b.Escenario;
        // La 2.0.0 del mismo contrato es la que reemplaza a la 1.0.0.
        var (_, v2) = await e.PublicarAsync(b.Slug, "api-retiro", Spec("API Retiro", "retiro", "Retiro", "2.0.0"), version: "2.0.0");
        var soloV2 = await PaquetePublicadoAsync(b.Lider, "Paquete Retiro Nuevo", b.Contrato, v2, ["GET /retiro/{id}"]);

        var orgA = await e.OrganizacionAsync("Organismo Demo Aviso A");
        var orgRevocada = await e.OrganizacionAsync("Organismo Demo Aviso Revocada");
        var orgVencida = await e.OrganizacionAsync("Organismo Demo Aviso Vencida");
        var orgOtra = await e.OrganizacionAsync("Organismo Demo Aviso Otra");
        await CompartirAsync(b.Lider, b.Paquete, orgA);
        await CompartirAsync(b.Lider, soloV2, orgA);
        var revocada = await CompartirAsync(b.Lider, b.Paquete, orgRevocada);
        await Ok(await Enviar(b.Lider, HttpMethod.Delete, $"/api/v1/contratos/paquetes/{b.Paquete}/comparticiones/{revocada}"));
        await CompartirAsync(b.Lider, b.Paquete, orgVencida);
        await CompartirAsync(b.Lider, soloV2, orgOtra);

        var a1 = await e.ExternoAsync(orgA, RolesAtlas.Integrador);
        var a2 = await e.ExternoAsync(orgA, RolesAtlas.ClienteFuncional);
        // De la misma organización, pero su invitación solo nombra el paquete que no trae la 1.0.0.
        var aFuera = await e.ExternoAsync(orgA, RolesAtlas.Integrador, [(TiposAlcance.Paquete, soloV2)]);
        var deRevocada = await e.ExternoAsync(orgRevocada, RolesAtlas.Integrador);
        var deVencida = await e.ExternoAsync(orgVencida, RolesAtlas.Integrador);
        var deOtra = await e.ExternoAsync(orgOtra, RolesAtlas.Integrador);
        await using (var plataforma = entorno.Plataforma())
        {
            // Con la hora del reloj de atlas-api (que no avanza solo).
            var desde = b.Reloj.GetUtcNow().AddDays(-2);
            var hasta = b.Reloj.GetUtcNow().AddSeconds(-1);
            await plataforma.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE atlas.contratos_comparticion SET vigente_desde = {desde}, vigente_hasta = {hasta} WHERE organizacion_id = {orgVencida}", Ct);
        }
        // Las asignaciones de las personas empiezan con la hora real: el reloj se pone al día (solo avanza).
        b.Reloj.SetUtcNow(DateTimeOffset.UtcNow);

        var hoy = ReglasDeprecacion.Hoy(b.Reloj);
        var retiro = hoy.AddDays(60);
        // La fecha debe ser futura y la versión debe existir.
        Assert.Equal(HttpStatusCode.BadRequest, (await DeprecarAsync(b, hoy)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeprecarAsync(b, retiro, "9.9.9")).StatusCode);
        // Sin publicar_manual, no se depreca.
        var (desarrollador, _) = await e.EmpleadoAsync(RolesAtlas.Desarrollador);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(desarrollador, HttpMethod.Post, $"/api/v1/contratos/{b.Contrato}/versiones/1.0.0/deprecar",
            new { retiroEn = retiro.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) })).StatusCode);

        var deprecada = await Ok(await DeprecarAsync(b, retiro));
        Assert.True(deprecada.GetProperty("cambio").GetBoolean());
        Assert.Equal("2.0.0", deprecada.GetProperty("reemplazo").GetString());
        Assert.Equal("deprecada", deprecada.GetProperty("version").GetProperty("estado").GetString());

        var primera = await CorrerAsync(b);
        // a1, a2 y el responsable: bandeja y correo (push no, porque nadie tiene dispositivos suscritos).
        Assert.Equal(3, primera.Personas);
        var fecha = ReglasDeprecacion.Texto(retiro);
        foreach (var persona in new[] { a1.PersonaId, a2.PersonaId })
        {
            var (bandeja, correos) = await AvisosDeAsync(b, persona);
            Assert.Equal(1, bandeja);
            var correo = Assert.Single(correos);
            Assert.Contains(fecha, correo.Texto, StringComparison.Ordinal);
            Assert.Contains("La reemplaza la versión 2.0.0", correo.Texto, StringComparison.Ordinal);
            Assert.Contains($"/portal/paquetes/{b.Paquete}/contratos/{b.Contrato}", correo.Texto, StringComparison.Ordinal);
            Assert.Contains("Paquete Retiro", correo.Texto, StringComparison.Ordinal);
            // Nada de otras organizaciones ni del paquete que no la trae.
            foreach (var ajeno in new[] { "Revocada", "Vencida", "Otra", "Paquete Retiro Nuevo", orgRevocada.ToString(), orgOtra.ToString() })
            {
                Assert.DoesNotContain(ajeno, correo.Texto, StringComparison.Ordinal);
                Assert.DoesNotContain(ajeno, correo.Html, StringComparison.Ordinal);
            }
        }
        var (bandejaResponsable, correosResponsable) = await AvisosDeAsync(b, b.PersonaResponsable);
        Assert.Equal(1, bandejaResponsable);
        var alResponsable = Assert.Single(correosResponsable);
        Assert.Contains("Atlas le avisa a 1 organización", alResponsable.Texto, StringComparison.Ordinal);
        Assert.Contains($"/contratos/{b.Contrato}", alResponsable.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Organismo Demo", alResponsable.Texto, StringComparison.Ordinal);
        foreach (var persona in new[] { aFuera.PersonaId, deRevocada.PersonaId, deVencida.PersonaId, deOtra.PersonaId })
        {
            var (bandeja, correos) = await AvisosDeAsync(b, persona);
            Assert.Equal(0, bandeja);
            Assert.Empty(correos);
        }

        // Idempotente: otra corrida y deprecar otra vez con la misma fecha no repiten nada.
        var segunda = await CorrerAsync(b);
        Assert.Equal(0, segunda.Enviados);
        Assert.False((await Ok(await DeprecarAsync(b, retiro))).GetProperty("cambio").GetBoolean());
        Assert.Equal(0, (await CorrerAsync(b)).Enviados);
        Assert.Equal(1, (await AvisosDeAsync(b, a1.PersonaId)).Bandeja);

        // Auditado: la deprecación y cada aviso con la organización de la persona (sin el correo).
        await using (var plataforma = entorno.Plataforma())
        {
            Assert.Equal(1, await plataforma.Auditoria.CountAsync(a => a.Accion == EndpointsDeprecacion.AccionDeprecada && a.RecursoId == b.Version.ToString(), Ct));
            var avisos = await plataforma.Auditoria.AsNoTracking()
                .Where(a => a.Accion == AvisosDeprecacion.AccionAviso && a.RecursoId == b.Version.ToString()).ToListAsync(Ct);
            Assert.Equal(3, avisos.Count);
            Assert.Equal(2, avisos.Count(a => a.OrganizacionId == orgA));
            Assert.All(avisos, a => Assert.DoesNotContain("@", a.Datos ?? string.Empty, StringComparison.Ordinal));
        }

        // El Inicio del portal muestra el mismo aviso: fecha dd/mm/aaaa y su reemplazo.
        var inicio = await Ok(await a1.Navegador.GetAsync("/api/v1/portal/inicio"));
        var aviso = Assert.Single(inicio.GetProperty("avisos").EnumerateArray(), x => x.GetProperty("contratoId").GetGuid() == b.Contrato);
        Assert.Equal("deprecada", aviso.GetProperty("tipo").GetString());
        Assert.Equal("2.0.0", aviso.GetProperty("reemplazo").GetString());
        Assert.Contains(fecha, aviso.GetProperty("mensaje").GetString(), StringComparison.Ordinal);

        // Cambiar la fecha de retiro es otro aviso.
        Assert.True((await Ok(await DeprecarAsync(b, retiro.AddDays(10)))).GetProperty("cambio").GetBoolean());
        Assert.Equal(3, (await CorrerAsync(b)).Personas);
        Assert.Equal(2, (await AvisosDeAsync(b, a1.PersonaId)).Bandeja);
    }

    [Fact]
    public async Task Los_recordatorios_salen_30_y_7_dias_antes_del_retiro_una_sola_vez()
    {
        await using var b = await PrepararAsync("Recordatorio");
        var e = b.Escenario;
        var org = await e.OrganizacionAsync("Organismo Demo Recordatorio");
        await CompartirAsync(b.Lider, b.Paquete, org);
        var externa = await e.ExternoAsync(org, RolesAtlas.Integrador);
        b.Reloj.SetUtcNow(DateTimeOffset.UtcNow);

        var retiro = ReglasDeprecacion.Hoy(b.Reloj).AddDays(45);
        await Ok(await DeprecarAsync(b, retiro));
        await CorrerAsync(b);
        Assert.Equal(1, (await AvisosDeAsync(b, externa.PersonaId)).Bandeja);

        // Faltan 31 días: nada. Faltan 30: el primer recordatorio, una sola vez.
        b.Reloj.Advance(TimeSpan.FromDays(14));
        await CorrerAsync(b);
        Assert.Equal(1, (await AvisosDeAsync(b, externa.PersonaId)).Bandeja);
        b.Reloj.Advance(TimeSpan.FromDays(1));
        await CorrerAsync(b);
        await CorrerAsync(b);
        var (bandeja, correos) = await AvisosDeAsync(b, externa.PersonaId);
        Assert.Equal(2, bandeja);
        Assert.Contains("faltan 30 días", correos[^1].Texto, StringComparison.Ordinal);
        Assert.StartsWith("Recordatorio:", correos[^1].Asunto, StringComparison.Ordinal);

        // Una persona que llega después recibe el aviso de la deprecación, no los recordatorios que ya pasaron.
        b.Reloj.Advance(TimeSpan.FromDays(23));
        var nueva = await e.ExternoAsync(org, RolesAtlas.Integrador);
        await CorrerAsync(b);
        Assert.Equal(3, (await AvisosDeAsync(b, externa.PersonaId)).Bandeja);
        var (bandejaNueva, correosNueva) = await AvisosDeAsync(b, nueva.PersonaId);
        Assert.Equal(1, bandejaNueva);
        Assert.Contains("faltan 7 días", Assert.Single(correosNueva).Texto, StringComparison.Ordinal);
        await CorrerAsync(b);
        Assert.Equal(1, (await AvisosDeAsync(b, nueva.PersonaId)).Bandeja);

        // Retirada la versión (pasó la fecha), ya no se avisa.
        b.Reloj.Advance(TimeSpan.FromDays(8));
        Assert.Equal(0, (await CorrerAsync(b)).Versiones);
    }

    [Fact]
    public async Task Una_version_retirada_no_se_depreca()
    {
        await using var b = await PrepararAsync("Retirada");
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.Database.ExecuteSqlInterpolatedAsync($"UPDATE atlas.contratos_version SET estado = 'retirada' WHERE id = {b.Version}", Ct);
        }
        var respuesta = await DeprecarAsync(b, ReglasDeprecacion.Hoy(b.Reloj).AddDays(30));
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("retirada", (await Json(respuesta)).GetProperty("codigo").GetString());
    }
}
