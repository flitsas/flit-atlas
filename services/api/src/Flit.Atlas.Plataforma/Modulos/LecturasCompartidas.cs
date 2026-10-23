namespace Flit.Atlas.Plataforma.Modulos;

/// <summary>
/// Aplicativo del catálogo tal como lo leen los demás módulos (ATLAS-ADR-0031): un módulo no referencia a otro
/// (ATLAS-ADR-0026), así que el catálogo publica esta lectura y los demás la consumen por inyección de dependencias, sin
/// tocar sus tablas.
/// </summary>
/// <param name="Tipo"><c>producto</c>, <c>modulo</c> o <c>servicio</c>.</param>
/// <param name="Grupo"><c>flit2</c>, <c>flito</c>, <c>satelite</c>, <c>legado</c> o <c>infraestructura</c>.</param>
/// <param name="Estado"><c>planeado</c>, <c>activo</c>, <c>en_retiro</c> o <c>retirado</c>.</param>
/// <param name="Repo">Repositorio de GitHub como <c>dueño/nombre</c>, si lo tiene.</param>
/// <param name="RutaEnRepo">Carpeta dentro del repo, si la tiene.</param>
public sealed record AplicativoLeido(
    Guid Id, string Slug, string Nombre, string Tipo, Guid? PadreId, string Grupo, string Estado, string? Repo, string? RutaEnRepo);

/// <summary>Lectura del catálogo para los demás módulos; la implementa el módulo del catálogo.</summary>
public interface ILectorAplicativos
{
    /// <summary>Todos los aplicativos (son unos cientos), sin orden garantizado.</summary>
    Task<IReadOnlyList<AplicativoLeido>> ListarAsync(CancellationToken ct = default);
}

/// <summary>Sin módulo de catálogo registrado no hay aplicativos.</summary>
internal sealed class SinAplicativos : ILectorAplicativos
{
    public Task<IReadOnlyList<AplicativoLeido>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AplicativoLeido>>([]);
}

/// <summary>Un equipo de soporte con las personas de turno en una fecha.</summary>
/// <param name="Area">Área del equipo («TI», «Operaciones»), si la tiene.</param>
/// <param name="Personas">Personas con turno vigente ese día (programado, confirmado o aplicado).</param>
/// <param name="HoraInicio">HH:mm, hora de Bogotá, si se conoce.</param>
/// <param name="HoraFin">HH:mm, hora de Bogotá, si se conoce.</param>
public sealed record EquipoDeTurno(Guid Id, string Nombre, string? Area, IReadOnlyList<Guid> Personas, string? HoraInicio, string? HoraFin);

/// <summary>Soporte de turno de una fecha, por equipo.</summary>
/// <param name="Festivo">Nombre del festivo, si la fecha lo es.</param>
public sealed record SoporteDelDia(DateOnly Fecha, string? Festivo, IReadOnlyList<EquipoDeTurno> Equipos);

/// <summary>
/// «¿Quién está de turno?» para los demás módulos (HU 2.5, ATLAS-ADR-0031): lo implementa el módulo de soporte con sus
/// propias reglas (turnos vigentes, equipos activos, festivos), sin que nadie más lea sus tablas.
/// </summary>
public interface ISoporteDeTurno
{
    /// <summary>Equipos activos con sus personas de turno en <paramref name="fecha"/>; <c>null</c> si no hay módulo de soporte.</summary>
    Task<SoporteDelDia?> DelDiaAsync(DateOnly fecha, CancellationToken ct = default);
}

/// <summary>Sin módulo de soporte registrado no hay turnos.</summary>
internal sealed class SinSoporteDeTurno : ISoporteDeTurno
{
    public Task<SoporteDelDia?> DelDiaAsync(DateOnly fecha, CancellationToken ct = default) => Task.FromResult<SoporteDelDia?>(null);
}
