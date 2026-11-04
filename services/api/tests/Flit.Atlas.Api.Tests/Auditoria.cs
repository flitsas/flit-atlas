using Flit.Atlas.Identidad.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests;

internal static class Auditoria
{
    /// <summary>
    /// ¿El evento de seguridad quedó en <c>identidad_evento_seguridad</c> y en <c>plataforma_auditoria</c>? Con
    /// <paramref name="id"/> (usuario o persona) solo cuentan los eventos de ese sujeto.
    /// </summary>
    public static async Task<bool> RegistradoAsync(EntornoApi entorno, string tipo, string? id = null, string? baseDeDatos = null)
    {
        var ct = TestContext.Current.CancellationToken;
        Guid? sujeto = id is null ? null : Guid.Parse(id);
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        await using var identidad = entorno.Identidad(baseDeDatos);
        var accion = TiposEventoSeguridad.PrefijoAuditoria + tipo;
        var enAuditoria = await plataforma.Auditoria.AnyAsync(
            a => a.Accion == accion && (id == null || a.RecursoId == id || a.ActorId == id), ct);
        var enEventos = await identidad.EventosSeguridad.AnyAsync(
            e => e.Tipo == tipo && (sujeto == null || e.UsuarioId == sujeto || e.PersonaId == sujeto), ct);
        return enAuditoria && enEventos;
    }
}
