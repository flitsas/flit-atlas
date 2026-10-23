using System.ComponentModel;
using System.Globalization;
using System.Text;
using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Un permiso con el módulo que lo declara.</summary>
public sealed record PermisoRegistrado(PermisoAtlas Permiso, ManifiestoPermisos Manifiesto);

/// <summary>
/// Todos los manifiestos de permisos registrados, validados, y la matriz rol × permiso que sale de ellos
/// (ATLAS-ADR-0023). Es la referencia en tiempo de ejecución: las políticas, los permisos efectivos, la matriz que se
/// publica y lo que el migrador aplica a la base salen de aquí.
/// </summary>
public sealed class CatalogoPermisos
{
    private readonly Dictionary<string, PermisoRegistrado> _permisos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _porRol = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DefinicionRol> _roles;

    private CatalogoPermisos(IReadOnlyList<ManifiestoPermisos> manifiestos)
    {
        Manifiestos = manifiestos;
        var roles = RolesAtlas.Todos.ToDictionary(r => r.Codigo, StringComparer.Ordinal);
        foreach (var rol in roles.Keys)
        {
            _porRol[rol] = new SortedSet<string>(StringComparer.Ordinal);
        }

        var modulos = new HashSet<string>(StringComparer.Ordinal);
        var tipos = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var manifiesto in manifiestos)
        {
            if (!modulos.Add(manifiesto.Modulo))
            {
                throw new InvalidOperationException($"El módulo «{manifiesto.Modulo}» registró su manifiesto de permisos dos veces.");
            }
            tipos.UnionWith(manifiesto.TiposAlcance);
            foreach (var permiso in manifiesto.Permisos)
            {
                if (!_permisos.TryAdd(permiso.Codigo, new PermisoRegistrado(permiso, manifiesto)))
                {
                    throw new InvalidOperationException(
                        $"El permiso «{permiso.Codigo}» está en los manifiestos de «{_permisos[permiso.Codigo].Manifiesto.Modulo}» y de «{manifiesto.Modulo}».");
                }
                foreach (var rol in permiso.ConcedidoA)
                {
                    if (!roles.ContainsKey(rol))
                    {
                        throw new InvalidOperationException($"El permiso «{permiso.Codigo}» se concede a «{rol}», que no es un rol de Atlas.");
                    }
                    if (rol == RolesAtlas.Superadmin)
                    {
                        throw new InvalidOperationException(
                            $"El permiso «{permiso.Codigo}» se concede a {RolesAtlas.Superadmin}, que ya los recibe todos: quítalo.");
                    }
                    _porRol[rol].Add(permiso.Codigo);
                }
                _porRol[RolesAtlas.Superadmin].Add(permiso.Codigo);
            }
        }
        tipos.Add(global::Flit.Atlas.Plataforma.Permisos.TiposAlcance.Global);
        TiposAlcance = tipos;
        _roles = roles;
    }

    /// <summary>Valida los manifiestos (ids, códigos únicos, roles existentes) y arma la matriz.</summary>
    public static CatalogoPermisos Crear(IEnumerable<ManifiestoPermisos> manifiestos)
    {
        ArgumentNullException.ThrowIfNull(manifiestos);
        return new CatalogoPermisos([.. manifiestos]);
    }

    public IReadOnlyList<ManifiestoPermisos> Manifiestos { get; }

    /// <summary>Roles en el orden de la matriz.</summary>
    public IReadOnlyList<DefinicionRol> Roles => [.. RolesAtlas.Todos.Where(r => _roles.ContainsKey(r.Codigo))];

    /// <summary>Tipos de alcance que aceptan las asignaciones (los del núcleo y los de los manifiestos).</summary>
    public IReadOnlySet<string> TiposAlcance { get; }

    public IReadOnlyCollection<PermisoRegistrado> Permisos => _permisos.Values;

    public bool Existe(string permiso) => permiso is not null && _permisos.ContainsKey(permiso);

    public DefinicionRol? Rol(string codigo) => codigo is not null && _roles.TryGetValue(codigo, out var rol) ? rol : null;

    /// <summary>Permisos que concede un rol (vacío si el rol no existe).</summary>
    public IReadOnlySet<string> PermisosDe(string rol) =>
        rol is not null && _porRol.TryGetValue(rol, out var permisos) ? permisos : (IReadOnlySet<string>)new HashSet<string>();

    /// <summary>La matriz en la forma que publica <c>GET /api/v1/plataforma/permisos/matriz</c>.</summary>
    public MatrizPermisos Matriz() => new(
        [.. RolesAtlas.Todos.Select(r => new RolMatriz(r.Codigo, r.Nombre, Texto(r.Tipo), r.Descripcion, r.Tipo != TipoRol.Implicito))],
        [.. Manifiestos.Select(m => new ModuloMatriz(m.Modulo, m.Nombre, [.. m.Permisos.Select(p => new PermisoMatriz(
            p.Codigo, p.Descripcion, [.. RolesAtlas.Todos.Select(r => r.Codigo).Where(r => _porRol[r].Contains(p.Codigo))]))]))]);

    internal static string Texto(TipoRol tipo) => tipo switch
    {
        TipoRol.Interno => "interno",
        TipoRol.Externo => "externo",
        _ => "implicito",
    };

    /// <summary>
    /// La matriz en Markdown para <c>docs/permisos/matriz-rol-permiso.md</c>. Una prueba compara el archivo versionado con
    /// esta salida, como hace el contrato OpenAPI.
    /// </summary>
    public string MatrizEnMarkdown()
    {
        var matriz = Matriz();
        var md = new StringBuilder();
        md.Append("# Matriz rol × permiso\n\n");
        md.Append("> **Generado desde el código** (`CatalogoPermisos.MatrizEnMarkdown`, ATLAS-ADR-0023): no se edita a mano. ");
        md.Append("Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests ");
        md.Append("--filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests`. En Atlas se consulta en `GET /api/v1/plataforma/permisos/matriz`.\n\n");
        md.Append("Todo empleado activo tiene además `atlas.lector`; `atlas.superadmin` recibe todos los permisos; ");
        md.Append("`atlas.responsable` es implícito (responsable del aplicativo, solo con alcance a ese aplicativo, F2).\n\n");

        md.Append("## Roles\n\n| Código | Nombre | Tipo | Descripción |\n|---|---|---|---|\n");
        foreach (var rol in matriz.Roles)
        {
            md.Append(CultureInfo.InvariantCulture, $"| `{rol.Codigo}` | {rol.Nombre} | {rol.Tipo} | {rol.Descripcion} |\n");
        }

        var columnas = matriz.Roles.Select(r => r.Codigo).ToList();
        foreach (var modulo in matriz.Modulos)
        {
            md.Append(CultureInfo.InvariantCulture, $"\n## {modulo.Nombre} (`{modulo.Id}`)\n\n");
            md.Append("| Permiso | Descripción | ").Append(string.Join(" | ", columnas.Select(c => $"`{c}`"))).Append(" |\n");
            md.Append("|---|---|").Append(string.Concat(columnas.Select(_ => ":-:|"))).Append('\n');
            foreach (var permiso in modulo.Permisos)
            {
                md.Append(CultureInfo.InvariantCulture, $"| `{permiso.Codigo}` | {permiso.Descripcion} | ");
                md.Append(string.Join(" | ", columnas.Select(c => permiso.Roles.Contains(c) ? "✔" : " "))).Append(" |\n");
            }
        }
        return md.ToString();
    }
}

[Description("Matriz rol × permiso generada desde los manifiestos de permisos del código (ATLAS-ADR-0023).")]
public sealed record MatrizPermisos(IReadOnlyList<RolMatriz> Roles, IReadOnlyList<ModuloMatriz> Modulos);

/// <param name="Tipo"><c>interno</c>, <c>externo</c> o <c>implicito</c>.</param>
/// <param name="Asignable">Si se puede asignar a mano; los implícitos los da la propiedad de un recurso.</param>
[Description("Rol de Atlas: interno (§7.2), externo (§7.3) o implícito por propiedad.")]
public sealed record RolMatriz(string Codigo, string Nombre, string Tipo, string Descripcion, bool Asignable);

[Description("Módulo y los permisos que declara en su manifiesto.")]
public sealed record ModuloMatriz(string Id, string Nombre, IReadOnlyList<PermisoMatriz> Permisos);

/// <param name="Roles">Códigos de los roles que conceden el permiso.</param>
[Description("Permiso atlas.<modulo>.<accion> y los roles que lo conceden.")]
public sealed record PermisoMatriz(string Codigo, string Descripcion, IReadOnlyList<string> Roles);
