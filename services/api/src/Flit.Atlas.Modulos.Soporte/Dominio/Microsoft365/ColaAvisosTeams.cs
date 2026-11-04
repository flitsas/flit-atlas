using System.Text.Json;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Microsoft365;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;

/// <summary>
/// Encola avisos para el canal de Teams del soporte (ATLAS-ADR-0033) en la transacción de la acción que los origina; el
/// trabajo de Microsoft 365 los publica con <see cref="INotificadorTeams"/> y reintenta si el flujo no responde. Sin el
/// canal configurado no encola nada. La usan la generación y los cambios de turno (HU 2.10) y los recordatorios del
/// sábado (HU 2.11, <see cref="TipoAvisoTeams.Recordatorio"/>).
/// </summary>
public sealed class ColaAvisosTeams(INotificadorTeams teams, IOptions<OpcionesM365Soporte> opciones, IConfiguration configuracion, TimeProvider reloj)
{
    /// <summary>El canal del soporte tiene URL: los avisos salen.</summary>
    public bool Activa => teams.Configurado(opciones.Value.CanalTeams);

    /// <summary>Enlace absoluto a una ruta de la PWA con <c>Atlas:UrlPublica</c>; sin ella, sin botón.</summary>
    public Uri? Enlace(string ruta) =>
        Uri.TryCreate(configuracion["Atlas:UrlPublica"]?.Trim(), UriKind.Absolute, out var raiz) ? new Uri(raiz, ruta) : null;

    /// <summary>Agrega el aviso al contexto (se publica cuando la transacción del llamador confirma). <c>false</c> si el canal no está configurado.</summary>
    public bool Encolar(SoporteDbContext soporte, TipoAvisoTeams tipo, MensajeTeams mensaje)
    {
        ArgumentNullException.ThrowIfNull(soporte);
        ArgumentNullException.ThrowIfNull(mensaje);
        if (!Activa)
        {
            return false;
        }
        var contenido = JsonSerializer.Serialize(Recortar(mensaje), SobreEvento.OpcionesJson);
        soporte.AvisosTeams.Add(new AvisoTeams
        {
            Tipo = tipo,
            Canal = opciones.Value.CanalTeams,
            Contenido = contenido.Length <= SoporteDbContext.LargoContenidoAviso ? contenido : JsonSerializer.Serialize(mensaje with { Hechos = null }, SobreEvento.OpcionesJson),
            ProximoIntentoEn = reloj.GetUtcNow(),
        });
        return true;
    }

    public static MensajeTeams Leer(string contenido) =>
        JsonSerializer.Deserialize<MensajeTeams>(contenido, SobreEvento.OpcionesJson) ?? throw new JsonException("El aviso no trae un mensaje.");

    /// <summary>A lo sumo 15 datos en la tarjeta; el resto se resume.</summary>
    private static MensajeTeams Recortar(MensajeTeams mensaje) =>
        mensaje.Hechos is { Count: > 15 } hechos
            ? mensaje with { Hechos = [.. hechos.Take(14), new HechoTeams("…", $"y {hechos.Count - 14} más en Atlas")] }
            : mensaje;
}

/// <summary>
/// Despierta el trabajo de Microsoft 365 de esta réplica cuando una acción deja algo pendiente, para no esperar al
/// siguiente intervalo. Las demás réplicas lo ven en su próxima revisión.
/// </summary>
public sealed class DespertadorM365 : IDisposable
{
    private readonly SemaphoreSlim _senal = new(0, 1);

    public void Dispose() => _senal.Dispose();

    public void Despertar()
    {
        try
        {
            _senal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya estaba despierto.
        }
    }

    /// <summary>Espera el intervalo (con el reloj de la aplicación) o una señal, lo que llegue primero.</summary>
    public async Task EsperarAsync(TimeSpan intervalo, TimeProvider reloj, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var senal = _senal.WaitAsync(limite.Token);
        var plazo = Task.Delay(intervalo, reloj, limite.Token);
        await Task.WhenAny(senal, plazo).ConfigureAwait(false);
        await limite.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(senal, plazo).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Una de las dos se canceló a propósito.
        }
        ct.ThrowIfCancellationRequested();
    }
}
