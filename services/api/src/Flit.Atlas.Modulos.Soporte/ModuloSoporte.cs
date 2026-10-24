using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Endpoints;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Plataforma.Permisos.RolesAtlas;

namespace Flit.Atlas.Modulos.Soporte;

/// <summary>
/// Permisos de Soporte de los sábados (ATLAS-ADR-0023 y ATLAS-ADR-0029). Los cuatro de la lista base del §7.4 vienen de
/// <see cref="ManifiestosNucleo.SoporteSabados"/> (son contrato desde F1) y este módulo suma
/// <see cref="VerCanal"/>.
/// </summary>
public static class PermisosSoporte
{
    /// <summary>Calendario, participantes, festivos, indicadores y el turno de este sábado (todo empleado).</summary>
    public const string Ver = "atlas.soporte.ver";

    /// <summary>Solicitar un cambio de turno propio y responder cuando a uno lo proponen como reemplazo (HU 2.9).</summary>
    public const string SolicitarCambio = "atlas.soporte.solicitar_cambio";

    /// <summary>
    /// Publicar la generación de un equipo, aprobar cambios de turno y registrar compensaciones (HU 2.8 y 2.9). Aprueba
    /// quien tenga el permiso, global o con alcance al equipo: no hay aprobadores fijos.
    /// </summary>
    public const string Aprobar = "atlas.soporte.aprobar";

    /// <summary>
    /// Equipos, participantes, turnos a mano, vista previa e importación del Excel (global o con alcance al equipo);
    /// configuración global, equipos nuevos, ausencias y festivos adicionales (solo global).
    /// </summary>
    public const string Gestionar = "atlas.soporte.gestionar";

    /// <summary>
    /// Agregado en la HU 2.6: el canal de soporte del sábado, sin nombres de personas. Es lo único de soporte que ve un
    /// externo (§7.3 y §8.3), y solo si se configura.
    /// </summary>
    public const string VerCanal = "atlas.soporte.ver_canal";

    /// <summary>
    /// Tipo de alcance que agrega el módulo: <c>equipo_soporte:&lt;id&gt;</c>. Una asignación de rol con este alcance da
    /// <see cref="Gestionar"/> o <see cref="Aprobar"/> solo sobre ese equipo (p. ej. quien maneja Operaciones).
    /// </summary>
    public const string AlcanceEquipo = Endpoints.Comunes.AlcanceEquipo;

    public static ManifiestoPermisos Manifiesto { get; } = new(ModuloSoporte.IdModulo, ManifiestosNucleo.SoporteSabados.Nombre,
    [
        .. ManifiestosNucleo.SoporteSabados.Permisos,
        new(VerCanal, "Ver el canal de soporte del sábado configurado, sin nombres de personas.", [.. Externos]),
    ], [AlcanceEquipo]);
}

/// <summary>
/// Módulo Soporte de los sábados (HU 2.6 a 2.9, prompt maestro §8.3, ATLAS-ADR-0029): configuración, participantes y su
/// orden, turnos con horario propio, estados y origen, ausencias, festivos de Colombia calculados localmente
/// (<see cref="Dominio.FestivosColombia"/>), generación equitativa con vista previa y confirmación, indicadores, el
/// importador del Excel con revisión y los cambios de turno y compensaciones.
/// Viene encendido (bandera <c>modulo.soporte</c>), como su entrada en la PWA desde F1.
/// </summary>
public sealed class ModuloSoporte : IModuloAtlas
{
    public const string IdModulo = "soporte";

    public string Id => IdModulo;

    public string Nombre => "Soporte";

    public string Descripcion =>
        "Soporte de los sábados por equipos de cada área: configuración, equipos, participantes, turnos con estados y horario, ausencias, festivos de Colombia, generación automática con vista previa, indicadores, importador del Excel con revisión, cambios de turno y compensaciones.";

    public bool ActivoPorDefecto => true;

    public ManifiestoPermisos Permisos => PermisosSoporte.Manifiesto;

    public Type? ContextoDatos => typeof(SoporteDbContext);

    /// <summary>
    /// Avisos de los cambios de turno y las compensaciones (HU 2.9) y los recordatorios de turno por app, push, correo y
    /// Teams (HU 2.11), con sus preferencias en el centro de notificaciones.
    /// </summary>
    public IReadOnlyList<Plataforma.Notificaciones.TipoNotificacion> TiposNotificacion =>
        [EndpointsCambios.TipoCambios, Recordatorios.RecordatoriosSoporte.Tipo];

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<SoporteDbContext>(o => SoporteDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        // Los endpoints usan los contextos, TimeProvider y los permisos de la sesión. Para los demás módulos, solo la
        // lectura del soporte de turno de una fecha («¿A quién llamo?», ATLAS-ADR-0031).
        servicios.AddScoped<Plataforma.Modulos.ISoporteDeTurno, Dominio.SoporteDeTurno>();

        // Recordatorios de turno (HU 2.11): el trabajo diario y «Enviar ahora» (el endpoint usa la misma instancia).
        servicios.AddOptions<Recordatorios.OpcionesRecordatorios>().BindConfiguration(Recordatorios.OpcionesRecordatorios.Seccion);
        servicios.AddScoped<Recordatorios.RecordatoriosSoporte>();
        servicios.AddSingleton<Recordatorios.TrabajoRecordatorios>();
        servicios.AddHostedService(sp => sp.GetRequiredService<Recordatorios.TrabajoRecordatorios>());
    }

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsSoporte.Mapear(grupo);
}
