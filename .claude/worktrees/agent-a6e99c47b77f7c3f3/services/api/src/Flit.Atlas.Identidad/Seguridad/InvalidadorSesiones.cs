using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>Motivos de <see cref="IInvalidadorSesiones.InvalidarSesionesAsync"/>; quedan en la sesión y en la auditoría.</summary>
public static class MotivosInvalidacion
{
    /// <summary>Se agregaron o quitaron roles de la persona (HU 1.4).</summary>
    public const string CambioDeRoles = "cambio_de_roles";

    public const string Suspension = "suspension";

    /// <summary>Venció una asignación de rol o el acceso de un externo (HU 1.4 y 1.5).</summary>
    public const string Vencimiento = "vencimiento";

    /// <summary>Un administrador cerró todas las sesiones de la persona.</summary>
    public const string CierreAdministrativo = "cierre_administrativo";

    /// <summary>Restablecimiento de la cuenta o cambio de factor (HU 1.5).</summary>
    public const string CambioDeCredenciales = "cambio_de_credenciales";
}

/// <summary>
/// Punto de extensión para invalidar las sesiones de una persona (ATLAS-ADR-0022). Lo usan la suspensión, el cierre
/// administrativo y, desde la HU 1.4, el cambio de roles y el vencimiento de asignaciones: <b>llamarlo después de
/// guardar el cambio</b>, nunca antes, para que un ingreso simultáneo no conserve el estado anterior.
/// </summary>
public interface IInvalidadorSesiones
{
    /// <summary>
    /// Cambia el sello de seguridad del usuario de la persona (toda sesión con el sello anterior deja de valer en su
    /// siguiente petición), revoca sus sesiones activas y sus tokens de OpenIddict (HU 1.3c: los Bearer dejan de valer
    /// en la siguiente petición y los refresh no se pueden canjear) y registra <c>identidad.sesiones_invalidadas</c>.
    /// </summary>
    /// <param name="personaId">Persona de <c>plataforma_persona</c>. Si aún no tiene usuario, no hay nada que invalidar.</param>
    /// <param name="motivo">Uno de <see cref="MotivosInvalidacion"/>.</param>
    /// <param name="actorPersonaId">Quién hizo el cambio; <c>null</c> si fue el sistema (p. ej. un vencimiento).</param>
    /// <returns>Cuántas sesiones activas se revocaron.</returns>
    Task<int> InvalidarSesionesAsync(Guid personaId, string motivo, Guid? actorPersonaId = null, CancellationToken ct = default);
}

internal sealed class InvalidadorSesiones(
    UserManager<UsuarioAtlas> usuarios,
    IdentidadDbContext contexto,
    AlmacenSesiones sesiones,
    RegistroSeguridad registro,
    IOpenIddictAuthorizationManager autorizaciones,
    IOpenIddictTokenManager tokens) : IInvalidadorSesiones
{
    public async Task<int> InvalidarSesionesAsync(Guid personaId, string motivo, Guid? actorPersonaId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        var usuario = await usuarios.Users.FirstOrDefaultAsync(u => u.PersonaId == personaId, ct);
        if (usuario is null)
        {
            return 0;
        }

        var resultado = await usuarios.UpdateSecurityStampAsync(usuario);
        if (!resultado.Succeeded)
        {
            // Otro cambio del usuario ganó la carrera (sello de concurrencia): se relee y se intenta de nuevo.
            await contexto.Entry(usuario).ReloadAsync(ct);
            resultado = await usuarios.UpdateSecurityStampAsync(usuario);
            if (!resultado.Succeeded)
            {
                throw new InvalidOperationException(
                    "No se pudo cambiar el sello de seguridad: " + string.Join("; ", resultado.Errors.Select(e => e.Code)));
            }
        }

        var revocadas = await sesiones.RevocarTodasAsync(usuario.Id, motivo, ct: ct);
        var sub = usuario.Id.ToString();
        await autorizaciones.RevokeBySubjectAsync(sub, ct);
        var tokensRevocados = await tokens.RevokeBySubjectAsync(sub, ct);
        await registro.RegistrarAsync(TiposEventoSeguridad.SesionesInvalidadas, new DatosEventoSeguridad
        {
            UsuarioId = usuario.Id,
            PersonaId = personaId,
            ActorPersonaId = actorPersonaId,
            Detalle = new { motivo, sesiones = revocadas, tokens = tokensRevocados },
        }, ct);
        return revocadas;
    }
}
