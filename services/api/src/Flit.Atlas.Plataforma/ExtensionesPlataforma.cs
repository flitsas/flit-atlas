using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Plataforma;

public static class ExtensionesPlataforma
{
    /// <summary>
    /// Registra el <see cref="PlataformaDbContext"/> y el publicador de la outbox. Requiere un <c>IConnection</c> de
    /// RabbitMQ en el contenedor (lo registra <c>AddRabbitMQClient</c> en atlas-api).
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
        return servicios;
    }
}
