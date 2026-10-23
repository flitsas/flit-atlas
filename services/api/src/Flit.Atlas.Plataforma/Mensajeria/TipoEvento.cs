using System.Text.RegularExpressions;

namespace Flit.Atlas.Plataforma.Mensajeria;

/// <summary>
/// Un tipo de evento que Atlas publica en <c>flit.atlas</c>: <c>atlas.&lt;modulo&gt;.&lt;evento&gt;</c> y la versión del
/// esquema de <c>data</c> (ATLAS-ADR-0018). Cada módulo lo declara como campo <c>public static readonly</c> (p. ej. en
/// <c>EventosSoporte</c>) y lo encola con <see cref="MensajeriaModelBuilderExtensions.EncolarEvento{TDatos}(Datos.ContextoAtlas, TipoEvento, TDatos, Guid?)"/>.
/// Una prueba verifica que todo tipo declarado esté en <c>contracts/asyncapi/atlas-events.v1.yaml</c> y al revés
/// (ATLAS-ADR-0021).
/// </summary>
public sealed partial record TipoEvento
{
    public TipoEvento(string nombre, int version)
    {
        if (!Formato().IsMatch(nombre))
        {
            throw new ArgumentException($"«{nombre}» no sigue el formato atlas.<modulo>.<evento> en snake_case.", nameof(nombre));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Nombre = nombre;
        Version = version;
    }

    /// <summary><c>atlas.&lt;modulo&gt;.&lt;evento&gt;</c>; es también la llave de ruteo.</summary>
    public string Nombre { get; }

    /// <summary>Versión del esquema de <c>data</c>. Un cambio incompatible sube la versión y conserva la anterior.</summary>
    public int Version { get; }

    public override string ToString() => $"{Nombre} v{Version}";

    [GeneratedRegex("^atlas\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$")]
    private static partial Regex Formato();
}
