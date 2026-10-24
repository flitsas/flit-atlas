using System.Security.Claims;
using Flit.Atlas.Modulos.Contratos.Poda;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

/// <summary>Exportaciones de contratos (sección <c>Contratos:Exportaciones</c>, HU 4.5, ATLAS-ADR-0042).</summary>
public sealed class OpcionesExportaciones
{
    public const string Seccion = "Contratos:Exportaciones";

    /// <summary>Tope del plan 00 §6.3 para la URL firmada de descarga.</summary>
    public static readonly TimeSpan MaximoUrl = TimeSpan.FromMinutes(15);

    /// <summary>Vida de la URL firmada de cada descarga (como máximo <see cref="MaximoUrl"/>).</summary>
    public TimeSpan DuracionUrl { get; set; } = MaximoUrl;

    /// <summary>Cuánto quedan los archivos en el S3 después de terminar; luego se borran y la exportación queda vencida.</summary>
    public TimeSpan Retencion { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Cada cuánto corre el trabajo que vence exportaciones y corta las que no terminan.</summary>
    public TimeSpan IntervaloVencimiento { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Una exportación pendiente o en proceso más vieja que esto queda fallida (<c>tiempo_agotado</c>).</summary>
    public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Ambiente al que se podan los specs de un paquete (<c>sandbox</c>, <c>qa</c> o <c>pdn</c>, ATLAS-ADR-0038).</summary>
    public string Ambiente { get; set; } = AmbientesPoda.Qa;

    /// <summary>
    /// URL públicas por código de contrato (o <c>*</c> para todos) que van en <c>servers</c> del spec podado. Sin URL, el
    /// spec sale sin <c>servers</c>: nunca con los internos (ATLAS-ADR-0038).
    /// </summary>
    public Dictionary<string, List<Uri>> Servidores { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Uri> ServidoresDe(string contrato) =>
        Servidores.TryGetValue(contrato, out var propios) ? propios
        : Servidores.TryGetValue("*", out var todos) ? todos
        : [];
}

internal sealed class ValidadorOpcionesExportaciones : IValidateOptions<OpcionesExportaciones>
{
    public ValidateOptionsResult Validate(string? name, OpcionesExportaciones options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errores = new List<string>();
        if (options.DuracionUrl <= TimeSpan.Zero || options.DuracionUrl > OpcionesExportaciones.MaximoUrl)
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:DuracionUrl debe estar entre 1 segundo y {OpcionesExportaciones.MaximoUrl} (plan 00 §6.3).");
        }
        if (options.Retencion < TimeSpan.FromMinutes(1))
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:Retencion debe ser de al menos 1 minuto.");
        }
        if (options.IntervaloVencimiento <= TimeSpan.Zero || options.TiempoMaximo <= TimeSpan.Zero)
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:IntervaloVencimiento y TiempoMaximo deben ser positivos.");
        }
        if (!AmbientesPoda.Todos.Contains(options.Ambiente))
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:Ambiente debe ser {string.Join(", ", AmbientesPoda.Todos.Order(StringComparer.Ordinal))}.");
        }
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}

/// <summary>¿Puede esta persona del portal exportar este paquete, y en qué revisión?</summary>
/// <param name="Permitida">Compartición vigente con su organización, con <c>puede_exportar</c>, y el permiso de la persona.</param>
/// <param name="Revision">La revisión que ve su organización (la fijada en la compartición o la vigente del paquete).</param>
/// <param name="Motivo">Si no: por qué, en una frase para la persona.</param>
public sealed record DecisionExportacionPortal(bool Permitida, int? Revision, string? Motivo)
{
    public static DecisionExportacionPortal Negada(string motivo) => new(false, null, motivo);
}

/// <summary>
/// Consulta de las comparticiones (HU 4.3) para exportar desde el portal (HU 4.4 y 4.5): ¿la organización de la persona
/// tiene el paquete compartido, vigente y con <c>puede_exportar</c>, y la persona el permiso? La implementa el módulo de
/// comparticiones; mientras no exista, <see cref="SinExportacionPortal"/> niega todo (falla cerrado). Se evalúa al
/// solicitar y otra vez al descargar.
/// </summary>
public interface IAutorizacionExportacionPortal
{
    Task<DecisionExportacionPortal> EvaluarAsync(ClaimsPrincipal usuario, Guid paqueteId, CancellationToken ct);
}

/// <summary>Sin comparticiones todavía: nadie exporta desde el portal.</summary>
internal sealed class SinExportacionPortal : IAutorizacionExportacionPortal
{
    public Task<DecisionExportacionPortal> EvaluarAsync(ClaimsPrincipal usuario, Guid paqueteId, CancellationToken ct) =>
        Task.FromResult(DecisionExportacionPortal.Negada("Este paquete no está compartido con tu organización para exportarlo."));
}
