using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Microsoft365;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma;

public static class ExtensionesPlataforma
{
    /// <summary>
    /// Registra el <see cref="PlataformaDbContext"/>, el publicador de la outbox, el centro de notificaciones con Web
    /// Push y los permisos (manifiestos del núcleo, catálogo, políticas por permiso y calculadora de permisos efectivos,
    /// ATLAS-ADR-0023). Requiere un <c>IConnection</c> de RabbitMQ en el contenedor (lo registra <c>AddRabbitMQClient</c>
    /// en atlas-api).
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

        // Correo (HU 1.5, ATLAS-ADR-0025): SMTP si el ambiente lo configura; Graph lo registra la identidad (usa la
        // credencial de Entra). Sin proveedor, no se envía nada.
        servicios.AddOptions<OpcionesCorreo>().BindConfiguration(OpcionesCorreo.Seccion);
        servicios.TryAddSingleton<IEnviadorCorreo>(sp =>
            string.Equals(sp.GetRequiredService<IConfiguration>()[$"{OpcionesCorreo.Seccion}:Proveedor"], OpcionesCorreo.ProveedorSmtp, StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<EnviadorSmtp>(sp)
                : ActivatorUtilities.CreateInstance<EnviadorDesactivado>(sp));

        // Microsoft 365 para los módulos (ATLAS-ADR-0033): el token de aplicación de Graph lo pone la identidad cuando hay
        // Entra (sin Entra, nada llama a Graph) y los avisos de Teams salen por el webhook de un flujo de Workflows. El
        // cliente de Teams no registra sus peticiones: la URL del flujo es la credencial.
        servicios.TryAddScoped<IAccesoGraph, SinAccesoGraph>();
        servicios.AddHttpClient(ClienteGraph.Nombre, c => c.BaseAddress = ClienteGraph.Base);
        servicios.AddOptions<OpcionesTeams>().BindConfiguration(OpcionesTeams.Seccion);
        servicios.AddHttpClient(NotificadorTeamsWorkflows.NombreCliente).RemoveAllLoggers();
        servicios.TryAddSingleton<INotificadorTeams, NotificadorTeamsWorkflows>();

        // Lecturas entre módulos (ATLAS-ADR-0031): por defecto vacías; el catálogo y el soporte registran las suyas.
        servicios.TryAddScoped<Modulos.ILectorAplicativos, Modulos.SinAplicativos>();
        servicios.TryAddScoped<Modulos.ISoporteDeTurno, Modulos.SinSoporteDeTurno>();

        // Roles y permisos (HU 1.4).
        servicios.AgregarPermisos();
        return servicios;
    }
}
