using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Centro de notificaciones (prompt maestro §8.9): toda notificación queda en la bandeja de la app y, si la persona
/// no lo apagó para ese tipo, sale por push a sus dispositivos. Los módulos notifican solo a través de este servicio.
/// </summary>
public sealed class ServicioNotificaciones(PlataformaDbContext contexto, EnviadorPush push)
{
    public async Task<(Notificacion Notificacion, int Dispositivos)> NotificarAsync(
        Guid personaId, TipoNotificacion tipo, string titulo, string cuerpo, string? enlace, CancellationToken ct)
    {
        var notificacion = await RegistrarEnBandejaAsync(personaId, tipo, titulo, cuerpo, enlace, ct);
        // En línea: lo dispara la persona (notificación de prueba) o un trabajo en segundo plano (recordatorios de soporte,
        // HU 2.11), nunca una petición de otra persona.
        var dispositivos = await CanalActivoAsync(personaId, tipo, CanalNotificacion.Push, ct)
            ? await EnviarPushAsync(personaId, new CargaPush(notificacion.Id, tipo.Codigo, titulo, cuerpo, enlace), ct)
            : 0;
        return (notificacion, dispositivos);
    }

    /// <summary>Solo la bandeja de la app (canal <c>app</c>, que no se puede apagar).</summary>
    public async Task<Notificacion> RegistrarEnBandejaAsync(
        Guid personaId, TipoNotificacion tipo, string titulo, string cuerpo, string? enlace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        var notificacion = new Notificacion
        {
            PersonaId = personaId,
            Tipo = tipo.Codigo,
            Titulo = titulo,
            Cuerpo = cuerpo,
            Enlace = enlace,
        };
        contexto.Notificaciones.Add(notificacion);
        await contexto.SaveChangesAsync(ct);
        return notificacion;
    }

    /// <summary>Solo push, sin mirar la preferencia (quien llama ya la revisó); devuelve a cuántos dispositivos llegó.</summary>
    public Task<int> EnviarPushAsync(Guid personaId, CargaPush carga, CancellationToken ct) => push.EnviarAsync(contexto, personaId, carga, ct);

    public async Task<bool> CanalActivoAsync(Guid personaId, TipoNotificacion tipo, CanalNotificacion canal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        var preferencia = await contexto.PreferenciasNotificacion.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PersonaId == personaId && p.Tipo == tipo.Codigo && p.Canal == canal, ct);
        return preferencia?.Activa ?? tipo.PorDefecto.Contains(canal);
    }
}
