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

/// <summary>
/// Una fila de la lista blanca de publicación desde la CI (<c>catalogo_fuente_publicacion</c>, ATLAS-ADR-0035): qué repo,
/// desde qué ramas y con qué workflow puede publicar qué tipos de fuente de un aplicativo.
/// </summary>
/// <param name="Repositorio"><c>dueño/nombre</c> en minúsculas.</param>
/// <param name="Ramas">Ramas exactas o con <c>*</c> (<c>release</c>, <c>hotfix/*</c>).</param>
/// <param name="Workflow">Ruta del workflow que llama (<c>.github/workflows/ci.yml</c>); <c>null</c> = cualquiera del repo.</param>
/// <param name="Tipos"><c>openapi</c>, <c>asyncapi</c>, <c>proto</c>, <c>likec4</c> o <c>docs</c>.</param>
public sealed record FuentePublicacionLeida(
    Guid Id, Guid AplicativoId, string AplicativoSlug, string Repositorio, IReadOnlyList<string> Ramas, string? Workflow, IReadOnlyList<string> Tipos);

/// <summary>
/// La lista blanca de publicación para los módulos que reciben fuentes desde la CI (contratos, documentación,
/// arquitectura); la implementa el catálogo, dueño de los aplicativos (ATLAS-ADR-0035).
/// </summary>
public interface IFuentesPublicacion
{
    /// <summary>Las fuentes de un repo (<c>dueño/nombre</c>, sin distinguir mayúsculas).</summary>
    Task<IReadOnlyList<FuentePublicacionLeida>> DelRepositorioAsync(string repositorio, CancellationToken ct = default);
}

/// <summary>Sin módulo de catálogo no hay lista blanca: nadie publica.</summary>
internal sealed class SinFuentesPublicacion : IFuentesPublicacion
{
    public Task<IReadOnlyList<FuentePublicacionLeida>> DelRepositorioAsync(string repositorio, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FuentePublicacionLeida>>([]);
}
