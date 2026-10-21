using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Guarda un enum como texto en snake_case (<c>EnAcompanamiento</c> ↔ <c>en_acompanamiento</c>), legible en SQL y
/// estable aunque cambie el orden de los valores en C#.
/// </summary>
public sealed class ConvertidorEnumSnake<TEnum>() : ValueConverter<TEnum, string>(
    valor => ASnake(valor.ToString()),
    texto => Enum.Parse<TEnum>(DesdeSnake(texto)))
    where TEnum : struct, Enum
{
    /// <summary>Largo máximo de la columna: el valor más largo del enum.</summary>
    internal static int Largo { get; } = Enum.GetNames<TEnum>().Max(n => ASnake(n).Length);

    internal static string ASnake(string nombre)
    {
        var sb = new StringBuilder(nombre.Length + 4);
        for (var i = 0; i < nombre.Length; i++)
        {
            if (char.IsUpper(nombre[i]) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(nombre[i]));
        }
        return sb.ToString();
    }

    private static string DesdeSnake(string texto) =>
        string.Concat(texto.Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
}
