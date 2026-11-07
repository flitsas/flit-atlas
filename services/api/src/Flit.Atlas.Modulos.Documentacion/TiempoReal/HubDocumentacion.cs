using System.Collections.Concurrent;
using System.Security.Claims;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Documentacion.TiempoReal;

/// <summary>Sección <c>Documentacion:TiempoReal</c>.</summary>
public sealed class OpcionesTiempoReal
{
    public const string Seccion = "Documentacion:TiempoReal";

    /// <summary>
    /// Vida máxima de una conexión: al cumplirse se cierra y el cliente se reconecta, y al negociar se vuelven a revisar la
    /// sesión y los permisos. Una conexión abierta no vuelve a pasar por la cookie: es el retraso máximo tras suspender a
    /// alguien o quitarle el permiso, y lo único que recibe mientras tanto son nombres y números de versión.
    /// </summary>
    public TimeSpan VidaMaxima { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan IntervaloRevision { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Una persona que tiene abierto un documento.</summary>
public sealed record PersonaViendo(Guid Id, string Nombre);

/// <summary>Quién tiene abierto cada documento <b>en esta réplica</b> (ATLAS-ADR-0053 §8): vive en memoria, sin backplane.</summary>
public sealed class PresenciaDocumentos
{
    private sealed record Conexion(HubCallerContext Contexto, DateTimeOffset Desde)
    {
        public Guid? Documento { get; set; }

        public PersonaViendo? Persona { get; set; }
    }

    private readonly ConcurrentDictionary<string, Conexion> _conexiones = new(StringComparer.Ordinal);

    internal void Registrar(HubCallerContext contexto, DateTimeOffset ahora) => _conexiones[contexto.ConnectionId] = new Conexion(contexto, ahora);

    /// <summary>Deja la conexión en el documento; devuelve el que tenía antes, si era otro.</summary>
    internal Guid? Entrar(string conexion, Guid documento, PersonaViendo persona)
    {
        if (!_conexiones.TryGetValue(conexion, out var c))
        {
            return null;
        }
        var anterior = c.Documento;
        c.Documento = documento;
        c.Persona = persona;
        return anterior == documento ? null : anterior;
    }

    /// <summary>Saca la conexión del documento; devuelve en cuál estaba.</summary>
    internal Guid? Salir(string conexion)
    {
        if (!_conexiones.TryGetValue(conexion, out var c))
        {
            return null;
        }
        var anterior = c.Documento;
        c.Documento = null;
        return anterior;
    }

    /// <summary>Olvida la conexión; devuelve en qué documento estaba.</summary>
    internal Guid? Quitar(string conexion) => _conexiones.TryRemove(conexion, out var c) ? c.Documento : null;

    /// <summary>Las personas del documento, una sola vez aunque tengan varias pestañas.</summary>
    internal IReadOnlyList<PersonaViendo> Viendo(Guid documento) =>
    [
        .. _conexiones.Values.Where(c => c.Documento == documento && c.Persona is not null).Select(c => c.Persona!)
            .DistinctBy(p => p.Id).OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase),
    ];

    /// <summary>Cierra las conexiones más viejas que <paramref name="limite"/>; devuelve cuántas.</summary>
    internal int CerrarAnteriores(DateTimeOffset limite)
    {
        var cerradas = 0;
        foreach (var c in _conexiones.Values.Where(c => c.Desde <= limite))
        {
            c.Contexto.Abort();
            cerradas++;
        }
        return cerradas;
    }
}

/// <summary>
/// Primer hub de Atlas (HU 5.8, ATLAS-ADR-0053 §8): «quién está viendo» y «versión nueva». Montado en
/// <c>/api/v1/documentacion/tiempo-real</c> con la sesión y <c>atlas.docs.ver</c>; cada documento se revisa al entrar con el
/// mismo predicado de visibilidad del lector. No lleva contenido: solo nombres de quienes pueden ver el documento y números
/// de versión. Los externos no entran a ningún documento (no ven ninguno, ATLAS-ADR-0053 §6).
/// </summary>
public sealed class HubDocumentacion(
    DocumentacionDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, PresenciaDocumentos presencia,
    TimeProvider reloj) : Hub
{
    public const string Ruta = "/tiempo-real";
    public const string EventoPresencia = "Presencia";
    public const string EventoVersionNueva = "VersionNueva";

    internal static string Grupo(Guid documento) => $"documento:{documento:D}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var ct = Context.ConnectionAborted;
        // El filtro de bandera de los endpoints no aplica a un hub.
        var fila = await plataforma.Modulos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == ModuloDocumentacion.IdModulo, ct);
        var organizacion = Guid.TryParse(Context.User?.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : Guid.Empty;
        // Un WebSocket es un GET: la antifalsificación de la API no lo cubre. SameSite=Strict ya deja la cookie afuera en otro
        // sitio; esto es la segunda barrera.
        var origen = http?.Request.Headers.Origin.ToString();
        var mismoOrigen = string.IsNullOrEmpty(origen) || string.Equals(origen, $"{http!.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase);
        if (!ExtensionesModulos.BanderaActiva(fila, porDefecto: true, organizacion) || !mismoOrigen)
        {
            Context.Abort();
            return;
        }
        presencia.Registrar(Context, reloj.GetUtcNow());
        await base.OnConnectedAsync();
    }

    /// <summary>La persona abrió un documento. <c>false</c> si no existe o no puede verlo (sin decir cuál de las dos).</summary>
    public async Task<bool> Entrar(Guid documentoId)
    {
        var ct = Context.ConnectionAborted;
        var usuario = Context.User!;
        var acceso = await AccesoDocumentos.CalcularAsync(usuario, permisosDeSesion, lector, ct);
        if (!await acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking()).AnyAsync(d => d.Id == documentoId, ct)
            || !Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona), out var personaId))
        {
            return false;
        }
        var nombre = await plataforma.Personas.AsNoTracking().Where(p => p.Id == personaId).Select(p => p.Nombre).FirstOrDefaultAsync(ct) ?? "Persona no disponible";
        var anterior = presencia.Entrar(Context.ConnectionId, documentoId, new PersonaViendo(personaId, nombre));
        if (anterior is { } otro)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grupo(otro), ct);
            await AvisarPresenciaAsync(otro);
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(documentoId), ct);
        await AvisarPresenciaAsync(documentoId);
        return true;
    }

    /// <summary>La persona salió del documento.</summary>
    public async Task Salir()
    {
        if (presencia.Salir(Context.ConnectionId) is { } documento)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grupo(documento), Context.ConnectionAborted);
            await AvisarPresenciaAsync(documento);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (presencia.Quitar(Context.ConnectionId) is { } documento)
        {
            await AvisarPresenciaAsync(documento);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private Task AvisarPresenciaAsync(Guid documento) =>
        Clients.Group(Grupo(documento)).SendAsync(EventoPresencia, new { documentoId = documento, personas = presencia.Viendo(documento) });
}

/// <summary>Avisa a quien tiene abierto un documento que hay otra versión. De mejor esfuerzo: la publicación ya quedó guardada.</summary>
public sealed partial class AvisosDocumentos(IHubContext<HubDocumentacion> hub, ILogger<AvisosDocumentos> log)
{
    public async Task VersionNuevaAsync(Guid documento, int numero)
    {
        try
        {
            await hub.Clients.Group(HubDocumentacion.Grupo(documento))
                .SendAsync(HubDocumentacion.EventoVersionNueva, new { documentoId = documento, numero }).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Quien tenga el documento abierto verá la versión al recargar.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AvisoFallido(log, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo avisar de una versión nueva por el hub")]
    private static partial void AvisoFallido(ILogger log, Exception ex);
}

/// <summary>Cierra las conexiones que cumplieron su vida máxima (<see cref="OpcionesTiempoReal.VidaMaxima"/>).</summary>
internal sealed class RevisorConexiones(PresenciaDocumentos presencia, IOptions<OpcionesTiempoReal> opciones, TimeProvider reloj) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(opciones.Value.IntervaloRevision, reloj);
        try
        {
            while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                presencia.CerrarAnteriores(reloj.GetUtcNow() - opciones.Value.VidaMaxima);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Al apagar.
        }
    }
}
