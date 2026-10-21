namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Base de las entidades con identidad propia: clave uuid v7 generada en la aplicación (ordenable por tiempo) y
/// marcas <c>creado_en</c> / <c>actualizado_en</c> que llena <see cref="ContextoAtlas"/> al guardar.
/// </summary>
public abstract class Entidad
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset CreadoEn { get; internal set; }

    public DateTimeOffset ActualizadoEn { get; internal set; }
}
