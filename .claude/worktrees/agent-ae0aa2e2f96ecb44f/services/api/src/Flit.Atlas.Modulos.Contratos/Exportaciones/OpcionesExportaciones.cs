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

    /// <summary>
    /// Ambiente al que se podan los specs de una exportación interna (<c>sandbox</c>, <c>qa</c> o <c>pdn</c>,
    /// ATLAS-ADR-0038), con las URL públicas de <c>Contratos:Portal:Servidores</c> (las mismas del portal). Las del portal
    /// usan el ambiente del portal (<c>Contratos:Portal:Ambiente</c>).
    /// </summary>
    public string Ambiente { get; set; } = AmbientesPoda.Qa;

    /// <summary>
    /// Exportaciones pendientes o en proceso que puede tener a la vez una organización externa desde el portal (HU 4.10,
    /// ATLAS-ADR-0048). atlas-docs genera de a una por pod (ATLAS-ADR-0042): sin este tope, una organización llenaría la
    /// cola de todos. La siguiente responde 429 con <c>Retry-After</c>.
    /// </summary>
    public int MaximoEnCursoPorOrganizacion { get; set; } = 2;

    /// <summary>Lo mismo por persona (interna o externa).</summary>
    public int MaximoEnCursoPorPersona { get; set; } = 2;

    /// <summary>Lo que se le pide esperar (<c>Retry-After</c>) a quien llega al tope de exportaciones en curso.</summary>
    public TimeSpan EsperaEnCurso { get; set; } = TimeSpan.FromSeconds(30);
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
        if (options.MaximoEnCursoPorOrganizacion < 1 || options.MaximoEnCursoPorPersona < 1)
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:MaximoEnCursoPorOrganizacion y MaximoEnCursoPorPersona deben ser de al menos 1.");
        }
        if (options.EsperaEnCurso < TimeSpan.FromSeconds(1))
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:EsperaEnCurso debe ser de al menos 1 segundo.");
        }
        if (!AmbientesPoda.Todos.Contains(options.Ambiente))
        {
            errores.Add($"{OpcionesExportaciones.Seccion}:Ambiente debe ser {string.Join(", ", AmbientesPoda.Todos.Order(StringComparer.Ordinal))}.");
        }
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
