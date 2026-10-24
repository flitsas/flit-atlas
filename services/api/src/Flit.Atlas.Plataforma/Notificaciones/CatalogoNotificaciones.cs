using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Todos los tipos de notificación: los del núcleo (<see cref="TiposNotificacion.Todos"/>) y los que declaran los módulos
/// registrados (<see cref="IModuloAtlas.TiposNotificacion"/>, HU 2.11). Es lo que lista y valida el centro de
/// notificaciones en las preferencias de cada persona.
/// </summary>
public sealed class CatalogoNotificaciones(IEnumerable<IModuloAtlas> modulos)
{
    public IReadOnlyList<TipoNotificacion> Todos { get; } =
        [.. TiposNotificacion.Todos, .. modulos.SelectMany(m => m.TiposNotificacion).DistinctBy(t => t.Codigo)];

    public TipoNotificacion? Buscar(string codigo) => Todos.FirstOrDefault(t => t.Codigo == codigo);

    /// <summary>
    /// Si el canal existe en este ambiente: la bandeja y push siempre (sin llaves VAPID push no llega a ningún
    /// dispositivo, pero la preferencia se guarda); correo si hay proveedor. Teams nunca: Atlas publica en canales de
    /// Teams con un flujo de Workflows (ATLAS-ADR-0033), que no llega a una persona; no hay aviso personal por Teams y el
    /// canal se muestra no disponible en las preferencias.
    /// </summary>
    public static bool CanalDelAmbiente(CanalNotificacion canal, IEnviadorCorreo correo)
    {
        ArgumentNullException.ThrowIfNull(correo);
        return canal switch
        {
            CanalNotificacion.App or CanalNotificacion.Push => true,
            CanalNotificacion.Correo => correo.Habilitado,
            _ => false,
        };
    }

    /// <summary>El tipo admite el canal y el ambiente lo tiene.</summary>
    public static bool Disponible(TipoNotificacion tipo, CanalNotificacion canal, IEnviadorCorreo correo)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        return tipo.CanalesPosibles.Contains(canal) && CanalDelAmbiente(canal, correo);
    }
}
