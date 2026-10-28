namespace Flit.Atlas.Modulos.Contratos.Paquetes;

/// <summary>
/// Paquetes de contratos (sección <c>Contratos:Paquetes</c>, HU 4.1, ATLAS-ADR-0041). Por defecto solo las versiones
/// estables van en paquetes de clientes. En QA se aceptan además las candidatas de <c>staging</c> (<c>rc</c>) para probar
/// el portal y las exportaciones antes de que el contrato llegue a <c>release</c> (decisión del líder del 28/10/2026).
/// En pdn la lista va vacía.
/// </summary>
public sealed class OpcionesPaquetes
{
    public const string Seccion = "Contratos:Paquetes";

    /// <summary>
    /// Prefijos de prerelease que también pueden ir en paquetes y que, como una estable, fijan la visibilidad del contrato
    /// al publicarse: <c>rc</c> acepta <c>1.2.0-rc.ab12cd3</c>. Vacía: solo estables.
    /// </summary>
    public List<string> PrereleasesAceptadas { get; set; } = [];

    /// <summary>Si una versión con esta prerelease (<c>null</c> = estable) puede ir en un paquete.</summary>
    public bool Acepta(string? prerelease) =>
        prerelease is null || PrereleasesAceptadas.Contains(prerelease.Split('.')[0], StringComparer.Ordinal);
}
