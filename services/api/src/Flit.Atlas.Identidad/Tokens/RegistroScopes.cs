using Flit.Atlas.Plataforma.Permisos;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Scope de un módulo para los tokens de OpenIddict (prompt maestro §6.3, ATLAS-ADR-0024): <c>atlas.&lt;modulo&gt;</c> o
/// <c>atlas.&lt;modulo&gt;.&lt;accion&gt;</c>. Un scope dice <b>qué cliente</b> puede llamar a qué parte de la API; los
/// permisos de la persona (HU 1.4) se siguen evaluando en el servidor en cada petición.
/// </summary>
/// <param name="Nombre">Nombre en el token (claim <c>scope</c>), p. ej. <c>atlas.publicar</c>.</param>
/// <param name="Modulo">
/// Id del módulo dueño: el de su <see cref="ManifiestoPermisos"/> (ATLAS-ADR-0023), p. ej. <c>contratos</c>. El arranque
/// falla si ese módulo no tiene manifiesto.
/// </param>
/// <param name="Descripcion">Qué permite, en español; queda en <c>identidad_oidc_scope</c>.</param>
public sealed record ScopeAtlas(string Nombre, string Modulo, string Descripcion);

/// <summary>
/// Punto de extensión: cada módulo declara sus scopes registrando un proveedor con
/// <see cref="ExtensionesScopes.AgregarScopes"/>, junto a su <see cref="ManifiestoPermisos"/>. Scopes y permisos no se
/// duplican: el permiso dice qué puede hacer la <b>persona</b>; el scope, qué parte de la API puede usar el
/// <b>cliente</b> con su token. Un endpoint con Bearer exige el scope y, si actúa por una persona, también su permiso.
/// </summary>
public interface IProveedorScopes
{
    IEnumerable<ScopeAtlas> Scopes { get; }
}

/// <summary>Scopes base del prompt maestro §6.3. Los demás llegan con su módulo.</summary>
public static class ScopesAtlas
{
    /// <summary>Servidor MCP del asistente (F7): consultar contratos, documentos y diagramas con los permisos del usuario.</summary>
    public const string Mcp = "atlas.mcp";

    /// <summary>Publicar contratos, documentación y modelos desde fuera del navegador (CLI y servicios).</summary>
    public const string Publicar = "atlas.publicar";

    /// <summary>Widget del asistente dentro de otras apps de la Suite (F7, intercambio de tokens).</summary>
    public const string Widget = "atlas.widget";

    /// <summary>Prefijo obligatorio de los scopes de Atlas.</summary>
    public const string Prefijo = "atlas.";

    internal sealed class Base : IProveedorScopes
    {
        public IEnumerable<ScopeAtlas> Scopes { get; } =
        [
            new(Mcp, ManifiestosNucleo.Asistente.Modulo, "Usar el servidor MCP de Atlas con los permisos de la persona."),
            new(Publicar, ManifiestosNucleo.Contratos.Modulo, "Publicar contratos, documentación y modelos en Atlas."),
            new(Widget, ManifiestosNucleo.Asistente.Modulo, "Usar el asistente de Atlas embebido en otra aplicación de FLIT."),
        ];
    }
}

/// <summary>Todos los scopes de Atlas declarados por los módulos, sin repetidos.</summary>
public sealed class RegistroScopes
{
    private readonly Dictionary<string, ScopeAtlas> _scopes = new(StringComparer.Ordinal);

    public RegistroScopes(IEnumerable<IProveedorScopes> proveedores)
    {
        ArgumentNullException.ThrowIfNull(proveedores);
        foreach (var scope in proveedores.SelectMany(p => p.Scopes))
        {
            if (!EsNombreValido(scope.Nombre))
            {
                throw new InvalidOperationException(
                    $"Scope inválido «{scope.Nombre}» (módulo {scope.Modulo}): debe empezar por «{ScopesAtlas.Prefijo}» y " +
                    "usar solo minúsculas, dígitos, guion bajo y puntos.");
            }
            if (_scopes.TryGetValue(scope.Nombre, out var previo) && previo != scope)
            {
                throw new InvalidOperationException(
                    $"El scope «{scope.Nombre}» lo declaran dos módulos: {previo.Modulo} y {scope.Modulo}.");
            }
            _scopes[scope.Nombre] = scope;
        }
    }

    public IReadOnlyCollection<ScopeAtlas> Todos => _scopes.Values;

    public IReadOnlyCollection<string> Nombres => _scopes.Keys;

    public bool Existe(string nombre) => _scopes.ContainsKey(nombre);

    internal static bool EsNombreValido(string nombre) =>
        nombre.StartsWith(ScopesAtlas.Prefijo, StringComparison.Ordinal)
        && nombre.Length > ScopesAtlas.Prefijo.Length
        && nombre.Length <= 100
        && nombre.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.')
        && !nombre.EndsWith('.')
        && !nombre.Contains("..", StringComparison.Ordinal);
}

public static class ExtensionesScopes
{
    /// <summary>Registra los scopes de un módulo. Se puede llamar desde cualquier módulo, en cualquier orden.</summary>
    public static IServiceCollection AgregarScopes(this IServiceCollection servicios, IProveedorScopes proveedor)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(proveedor);
        return servicios.AddSingleton(proveedor);
    }
}
