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

/// <summary>Un documento funcional (manual, guía funcional, ficha comercial) que el portal muestra a una persona externa.</summary>
/// <param name="AplicativoId">Aplicativo del catálogo al que pertenece; el portal lo pinta en su tarjeta.</param>
/// <param name="Tipo"><c>manual_usuario</c>, <c>guia_funcional</c>, <c>ficha_comercial</c>…, como los defina la documentación (F5).</param>
/// <param name="Ruta">Ruta de la PWA donde se lee.</param>
public sealed record DocumentoDelPortal(Guid Id, Guid AplicativoId, string Titulo, string Tipo, string Ruta);

/// <summary>
/// Punto de extensión del portal de clientes para la documentación funcional (prompt maestro §7.3 y §8.5,
/// ATLAS-ADR-0043): lo que <c>ext.cliente_funcional</c> y <c>ext.asesor_comercial</c> ven de los productos que tiene su
/// organización. Lo implementará el módulo de documentación (F5) con sus niveles de visibilidad (<c>publico_clientes</c>
/// dentro del alcance); hasta entonces no hay documentos.
/// </summary>
public interface IDocumentacionDelPortal
{
    /// <summary>Documentos visibles para la persona de la sesión, de los aplicativos dados, siempre de su organización.</summary>
    Task<IReadOnlyList<DocumentoDelPortal>> DeAplicativosAsync(
        System.Security.Claims.ClaimsPrincipal usuario, Guid organizacionId, IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default);
}

/// <summary>Sin módulo de documentación (hasta F5) el portal no muestra documentos.</summary>
internal sealed class SinDocumentacionDelPortal : IDocumentacionDelPortal
{
    public Task<IReadOnlyList<DocumentoDelPortal>> DeAplicativosAsync(
        System.Security.Claims.ClaimsPrincipal usuario, Guid organizacionId, IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DocumentoDelPortal>>([]);
}

/// <summary>
/// Responsables vigentes (principal o secundario) de los aplicativos, para los demás módulos (HU 4.8, ATLAS-ADR-0046):
/// los avisos de deprecación de un contrato llegan también a quienes responden por su aplicativo. Lo implementa el módulo
/// de equipo con su reparto vigente (hoy en Bogotá), sin que nadie más lea sus tablas.
/// </summary>
public interface ILectorResponsables
{
    /// <summary>Personas responsables vigentes de cada aplicativo pedido; los que no tienen, no aparecen.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default);
}

/// <summary>Sin módulo de equipo no hay responsables.</summary>
internal sealed class SinResponsables : ILectorResponsables
{
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(new Dictionary<Guid, IReadOnlyList<Guid>>());
}
