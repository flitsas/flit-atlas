using System.Net;
using System.Threading.Channels;
using Flit.Atlas.Api.Tests.Exportaciones;
using Flit.Atlas.Identidad;
using Flit.Atlas.Modulos.Documentacion.TiempoReal;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Api.Tests.Exportaciones.HttpExportaciones;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 5.8 (ATLAS-ADR-0053 §8) y los favoritos de la HU 5.4: el hub con la cookie del BFF y el cliente real de SignalR
/// (long polling sobre el servidor de pruebas, que no da WebSockets con cookie), con el permiso y la visibilidad al entrar.
/// </summary>
public sealed class TiempoRealDocumentosTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string RutaHub = "/api/v1/documentacion/tiempo-real";

    private sealed record Presencia(Guid DocumentoId, PersonaViendo[] Personas);

    private sealed record VersionNueva(Guid DocumentoId, int Numero);

    private static HubConnection Conexion(WebApplicationFactory<Program> api, Navegador navegador, bool csrf = true) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri("https://localhost"), RutaHub), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                if (navegador.Cookie is not null)
                {
                    o.Headers["Cookie"] = navegador.Cookie;
                }
                if (csrf)
                {
                    o.Headers[EndpointsIdentidad.CabeceraCsrf] = "1";
                }
            })
            .Build();

    private static async Task<T> SiguienteAsync<T>(ChannelReader<T> canal, Func<T, bool> condicion)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limite.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            var valor = await canal.ReadAsync(limite.Token);
            if (condicion(valor))
            {
                return valor;
            }
        }
    }

    private static ChannelReader<Presencia> OirPresencia(HubConnection conexion)
    {
        var canal = Channel.CreateUnbounded<Presencia>();
        conexion.On<Presencia>(HubDocumentacion.EventoPresencia, p => canal.Writer.TryWrite(p));
        return canal.Reader;
    }

    [Fact]
    public async Task Quien_abre_un_documento_ve_a_los_demas_y_al_salir_desaparece()
    {
        await using var api = entorno.Api();
        var (lider, personaLider) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var (lector, personaLector) = await EmpleadoAsync(entorno, api, RolesAtlas.Lector);

        await using var a = Conexion(api, lider);
        await using var b = Conexion(api, lector);
        var delLider = OirPresencia(a);
        await a.StartAsync(Ct);
        await b.StartAsync(Ct);

        Assert.True(await a.InvokeAsync<bool>("Entrar", documento, Ct));
        Assert.True(await b.InvokeAsync<bool>("Entrar", documento, Ct));
        var ambos = await SiguienteAsync(delLider, p => p.Personas.Length == 2);
        Assert.Equal(documento, ambos.DocumentoId);
        Assert.Contains(ambos.Personas, p => p.Id == personaLider);
        Assert.Contains(ambos.Personas, p => p.Id == personaLector);

        await b.InvokeAsync("Salir", Ct);
        var solo = await SiguienteAsync(delLider, p => p.Personas.Length == 1);
        Assert.Equal(personaLider, solo.Personas[0].Id);
    }

    [Fact]
    public async Task Lo_que_la_persona_no_puede_ver_no_la_deja_entrar()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var aplicativo = await DocumentosDePrueba.AplicativoAsync(lider);
        var tecnico = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo, titulo: "Runbook", visibilidad: "interno_ti");
        var (lector, _) = await EmpleadoAsync(entorno, api, RolesAtlas.Lector);

        await using var b = Conexion(api, lector);
        await b.StartAsync(Ct);

        Assert.False(await b.InvokeAsync<bool>("Entrar", tecnico, Ct));
        Assert.False(await b.InvokeAsync<bool>("Entrar", Guid.CreateVersion7(), Ct));
    }

    [Fact]
    public async Task Publicar_avisa_a_quien_tiene_el_documento_abierto_sin_mandar_el_contenido()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var otro = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider), titulo: "Otro");

        await using var a = Conexion(api, lider);
        var canal = Channel.CreateUnbounded<VersionNueva>();
        a.On<VersionNueva>(HubDocumentacion.EventoVersionNueva, v => canal.Writer.TryWrite(v));
        await a.StartAsync(Ct);
        Assert.True(await a.InvokeAsync<bool>("Entrar", documento, Ct));

        async Task PublicarOtraVersionAsync(Guid id)
        {
            var ruta = $"{DocumentosDePrueba.Raiz}/{id}";
            Assert.Equal(HttpStatusCode.OK, (await lider.EnviarAsync(HttpMethod.Put, ruta, System.Net.Http.Json.JsonContent.Create(new
            {
                tipo = "guia_funcional", titulo = "Radicar", contenido = "Otra versión.", visibilidad = "interno_negocio", audiencias = new[] { "empleados" },
                responsable = "Persona Demo", nota = "Segunda.", revisionBase = 1,
            }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(lider, $"{ruta}/envio-revision", new { revision = 2 })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(lider, $"{ruta}/publicacion", new { revision = 2 })).StatusCode);
        }

        // La de otro documento no llega a este grupo.
        await PublicarOtraVersionAsync(otro);
        await PublicarOtraVersionAsync(documento);

        var aviso = await SiguienteAsync(canal.Reader, _ => true);
        Assert.Equal(documento, aviso.DocumentoId);
        Assert.Equal(2, aviso.Numero);
    }

    [Fact]
    public async Task Sin_sesion_o_sin_la_cabecera_antifalsificacion_no_negocia()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);

        await using var anonima = Conexion(api, Navegador.De(api));
        var sinSesion = await Assert.ThrowsAsync<HttpRequestException>(() => anonima.StartAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);

        await using var sinCsrf = Conexion(api, lider, csrf: false);
        var rechazada = await Assert.ThrowsAsync<HttpRequestException>(() => sinCsrf.StartAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, rechazada.StatusCode);
    }

    [Fact]
    public async Task Con_la_bandera_apagada_el_hub_cierra_la_conexion()
    {
        var baseNueva = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseNueva);
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico, baseNueva);
        await using (var bd = entorno.Plataforma(baseNueva))
        {
            (await bd.Modulos.SingleAsync(m => m.Id == "documentacion", Ct)).Activo = false;
            await bd.SaveChangesAsync(Ct);
        }

        await using var a = Conexion(api, lider);
        var cerrada = new TaskCompletionSource();
        a.Closed += _ =>
        {
            cerrada.TrySetResult();
            return Task.CompletedTask;
        };
        try
        {
            await a.StartAsync(Ct);
        }
#pragma warning disable CA1031 // Cerrarse en el saludo también es rechazarla.
        catch (Exception)
#pragma warning restore CA1031
        {
            cerrada.TrySetResult();
        }
        await cerrada.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    [Fact]
    public async Task Una_conexion_que_cumple_su_vida_maxima_se_cierra_para_volver_a_revisar_la_sesion()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);

        await using var a = Conexion(api, lider);
        var cerrada = new TaskCompletionSource();
        a.Closed += _ =>
        {
            cerrada.TrySetResult();
            return Task.CompletedTask;
        };
        await a.StartAsync(Ct);

        // Una invocación corre después de OnConnectedAsync, que va tras el saludo.
        await a.InvokeAsync("Salir", Ct);
        var presencia = api.Services.GetRequiredService<PresenciaDocumentos>();
        // Antes de cumplirla no pasa nada.
        Assert.Equal(0, presencia.CerrarAnteriores(DateTimeOffset.UtcNow - TimeSpan.FromHours(1)));
        Assert.Equal(1, presencia.CerrarAnteriores(DateTimeOffset.UtcNow + TimeSpan.FromHours(1)));
        await cerrada.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    [Fact]
    public async Task Los_favoritos_son_de_cada_persona_y_solo_traen_lo_que_ve()
    {
        const string Ruta = "/api/v1/documentacion/favoritos";
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var aplicativo = await DocumentosDePrueba.AplicativoAsync(lider);
        var visible = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo);
        var tecnico = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo, titulo: "Runbook", visibilidad: "interno_ti");
        var (lector, _) = await EmpleadoAsync(entorno, api, RolesAtlas.Lector);

        Assert.Equal(HttpStatusCode.NotFound, (await lector.EnviarAsync(HttpMethod.Put, $"{Ruta}/{tecnico}")).StatusCode);
        var marcado = await lector.EnviarAsync(HttpMethod.Put, $"{Ruta}/{visible}");
        Assert.Equal(HttpStatusCode.OK, marcado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lector.EnviarAsync(HttpMethod.Put, $"{Ruta}/{visible}")).StatusCode);

        var lista = await JsonAsync(await lector.GetAsync(Ruta));
        Assert.Equal(1, lista.GetArrayLength());
        Assert.Equal(visible, lista[0].GetProperty("documentoId").GetGuid());
        Assert.Equal($"/documentacion/{visible}", lista[0].GetProperty("ruta").GetString());
        Assert.Equal(1, lista[0].GetProperty("versionVigente").GetInt32());
        Assert.Equal(0, (await JsonAsync(await lider.GetAsync(Ruta))).GetArrayLength());

        // Si el documento deja de verse, no aparece, aunque la fila siga.
        await using (var bd = entorno.Plataforma())
        {
            await bd.Database.ExecuteSqlAsync($"UPDATE atlas.documentacion_documento SET estado = 'archivado' WHERE id = {visible}", Ct);
        }
        Assert.Equal(0, (await JsonAsync(await lector.GetAsync(Ruta))).GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await lector.EnviarAsync(HttpMethod.Delete, $"{Ruta}/{visible}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lector.EnviarAsync(HttpMethod.Delete, $"{Ruta}/{visible}")).StatusCode);
    }
}
