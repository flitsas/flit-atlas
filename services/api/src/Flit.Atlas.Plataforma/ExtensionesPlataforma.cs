using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma;

public static class ExtensionesPlataforma
{
    /// <summary>
    /// Registra el <see cref="PlataformaDbContext"/>, el publicador de la outbox y el centro de notificaciones con Web
    /// Push. Requiere un <c>IConnection</c> de RabbitMQ en el contenedor (lo registra <c>AddRabbitMQClient</c> en
    /// atlas-api).
    /// </summary>
    public static IServiceCollection AgregarPlataforma(
        this IServiceCollection servicios, string cadenaConexion, Action<OpcionesMensajeria>? configurar = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenaConexion);

        servicios.AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, cadenaConexion));

        var opciones = new OpcionesMensajeria();
        configurar?.Invoke(opciones);
        servicios.AddSingleton(opciones);
        servicios.TryAddSingleton(TimeProvider.System);
        servicios.TryAddSingleton<ICanalEventos, CanalRabbitMq>();
        servicios.AddHostedService<PublicadorOutbox>();

        // Centro de notificaciones y Web Push (HU 1.6).
        servicios.TryAddSingleton(sp => LlavesVapid.Resolver(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Flit.Atlas.Plataforma.Push")));
        servicios.TryAddSingleton(sp =>
        {
            var hosts = sp.GetRequiredService<IConfiguration>()[OpcionesPush.ClaveHostsPermitidos];
            return string.IsNullOrWhiteSpace(hosts)
                ? new OpcionesPush()
                : new OpcionesPush { HostsPermitidos = hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        });
        // Tiempos y reintentos ante 5xx: los del manejador de resiliencia de ServiceDefaults. Un reintento que duplique
        // el envío no duplica el aviso: el service worker usa el id de la notificación como tag.
        servicios.AddHttpClient<EnviadorPush>();
        servicios.AddScoped<ServicioNotificaciones>();
        return servicios;
    }
}
