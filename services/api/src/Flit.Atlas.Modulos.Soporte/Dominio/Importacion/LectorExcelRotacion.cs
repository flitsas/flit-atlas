using System.Globalization;
using System.IO.Packaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Importacion;

/// <summary>
/// Valor de una celda tal como viene: el texto (cadenas, compartidas o en línea) o el número (fechas seriales de Excel
/// incluidas) y si su formato es de fecha.
/// </summary>
public sealed record CeldaExcel(string? Texto, double? Numero, bool FormatoFecha)
{
    public static CeldaExcel Vacia { get; } = new(null, null, false);

    public bool EstaVacia => string.IsNullOrWhiteSpace(Texto) && Numero is null;

    /// <summary>El valor para mostrarlo en el informe: el texto, o el número en formato invariante.</summary>
    public string? Original => !string.IsNullOrWhiteSpace(Texto) ? Texto.Trim() : Numero?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Una fila de datos de la hoja «Rotacion Soporte» (columnas Fecha, Responsable, Estado, Compensado y Observación).</summary>
/// <param name="Numero">Número de la fila en Excel (desde 1), para que el informe la ubique.</param>
public sealed record FilaExcel(int Numero, CeldaExcel Fecha, CeldaExcel Responsable, CeldaExcel Estado, CeldaExcel Compensado, CeldaExcel Observacion);

/// <summary>Las filas de datos de la hoja y su nombre.</summary>
public sealed record HojaRotacion(string Hoja, IReadOnlyList<FilaExcel> Filas);

/// <summary>El archivo no se puede leer como la hoja de la rotación; <see cref="Codigo"/> es estable para la API.</summary>
public sealed class LecturaExcelException(string codigo, string mensaje) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
}

/// <summary>
/// Lee el .xlsx de la rotación de soporte con el SDK de Open XML (HU 2.7), con límites de tamaño, de caracteres por
/// parte (contra archivos comprimidos maliciosos) y de filas. Busca la hoja «Rotacion Soporte» (o la primera) y la fila
/// de encabezados con «Fecha» y «Responsable»; las demás columnas son opcionales. No interpreta valores: eso lo hace
/// <see cref="InterpreteRotacion"/>.
/// </summary>
public static class LectorExcelRotacion
{
    /// <summary>Tamaño máximo del archivo: la hoja real pesa unos 10 KB.</summary>
    public const int TamanoMaximo = 1024 * 1024;

    /// <summary>Filas de datos como máximo (la hoja real trae 26).</summary>
    public const int FilasMaximas = 1000;

    /// <summary>Caracteres máximos por parte del paquete: corta los archivos que se inflan al descomprimir.</summary>
    public const long CaracteresMaximosPorParte = 10_000_000;

    public const string NombreHoja = "Rotacion Soporte";

    /// <summary>Filas que se revisan buscando los encabezados.</summary>
    private const int FilasParaEncabezados = 20;

    private static readonly Dictionary<string, string> Columnas = new(StringComparer.Ordinal)
    {
        ["fecha"] = "fecha",
        ["responsable"] = "responsable",
        ["estado"] = "estado",
        ["compensado"] = "compensado",
        ["compensacion"] = "compensado",
        ["observacion"] = "observacion",
        ["observaciones"] = "observacion",
    };

    /// <summary>Formatos de número integrados de Excel que son fechas u horas (ECMA-376, parte 1, §18.8.30).</summary>
    private static readonly HashSet<uint> FormatosFechaIntegrados = [14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47];

    public static HojaRotacion Leer(byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (contenido.Length == 0)
        {
            throw new LecturaExcelException("archivo_vacio", "El archivo está vacío.");
        }
        if (contenido.Length > TamanoMaximo)
        {
            throw new LecturaExcelException("archivo_grande", $"El archivo pesa más de {TamanoMaximo / 1024} KB.");
        }
        try
        {
            using var flujo = new MemoryStream(contenido, writable: false);
            using var documento = SpreadsheetDocument.Open(flujo, false, new OpenSettings { MaxCharactersInPart = CaracteresMaximosPorParte });
            return LeerDocumento(documento);
        }
        catch (LecturaExcelException)
        {
            throw;
        }
        catch (Exception error) when (error is OpenXmlPackageException or FileFormatException or InvalidDataException or InvalidOperationException
            or System.Xml.XmlException or IOException or ArgumentException or NotSupportedException)
        {
            throw new LecturaExcelException("archivo_invalido", "El archivo no es un libro de Excel (.xlsx) válido.");
        }
    }

    private static HojaRotacion LeerDocumento(SpreadsheetDocument documento)
    {
        var libro = documento.WorkbookPart ?? throw new LecturaExcelException("archivo_invalido", "El libro no tiene hojas.");
        var hojas = libro.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
        var hoja = hojas.FirstOrDefault(h => NombresPersona.Normalizar(h.Name?.Value) == NombresPersona.Normalizar(NombreHoja)) ?? hojas.FirstOrDefault();
        if (hoja?.Id?.Value is not { } idHoja || libro.GetPartById(idHoja) is not WorksheetPart parte)
        {
            throw new LecturaExcelException("archivo_invalido", "El libro no tiene hojas.");
        }
        var compartidas = libro.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
        var formatosFecha = FormatosDeFecha(libro.WorkbookStylesPart?.Stylesheet);

        var filas = parte.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? [];
        Dictionary<string, int>? encabezados = null;
        var datos = new List<FilaExcel>();
        var revisadas = 0;
        foreach (var fila in filas)
        {
            revisadas++;
            var celdas = Celdas(fila, compartidas, formatosFecha);
            var numero = (int)(fila.RowIndex?.Value ?? (uint)revisadas);
            if (encabezados is null)
            {
                encabezados = Encabezados(celdas);
                if (encabezados is null && revisadas >= FilasParaEncabezados)
                {
                    break;
                }
                continue;
            }
            CeldaExcel De(string columna) =>
                encabezados.TryGetValue(columna, out var indice) && celdas.TryGetValue(indice, out var celda) ? celda : CeldaExcel.Vacia;
            var leida = new FilaExcel(numero, De("fecha"), De("responsable"), De("estado"), De("compensado"), De("observacion"));
            if (leida is { Fecha.EstaVacia: true, Responsable.EstaVacia: true, Estado.EstaVacia: true, Compensado.EstaVacia: true, Observacion.EstaVacia: true })
            {
                continue;
            }
            if (datos.Count == FilasMaximas)
            {
                throw new LecturaExcelException("demasiadas_filas", $"La hoja tiene más de {FilasMaximas} filas de datos.");
            }
            datos.Add(leida);
        }
        if (encabezados is null)
        {
            throw new LecturaExcelException("sin_encabezados", "No se encontró la fila de encabezados con «Fecha» y «Responsable».");
        }
        return new HojaRotacion(hoja.Name?.Value ?? NombreHoja, datos);
    }

    /// <summary>Índice de cada columna conocida si la fila es la de encabezados (tiene Fecha y Responsable).</summary>
    private static Dictionary<string, int>? Encabezados(Dictionary<int, CeldaExcel> celdas)
    {
        var mapa = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (indice, celda) in celdas)
        {
            if (Columnas.TryGetValue(NombresPersona.Normalizar(celda.Texto), out var columna))
            {
                mapa.TryAdd(columna, indice);
            }
        }
        return mapa.ContainsKey("fecha") && mapa.ContainsKey("responsable") ? mapa : null;
    }

    private static Dictionary<int, CeldaExcel> Celdas(Row fila, List<string> compartidas, HashSet<uint> formatosFecha)
    {
        var celdas = new Dictionary<int, CeldaExcel>();
        var siguiente = 0;
        foreach (var celda in fila.Elements<Cell>())
        {
            var indice = Columna(celda.CellReference?.Value) ?? siguiente;
            siguiente = indice + 1;
            var valor = Valor(celda, compartidas, formatosFecha);
            if (!valor.EstaVacia)
            {
                celdas[indice] = valor;
            }
        }
        return celdas;
    }

    private static CeldaExcel Valor(Cell celda, List<string> compartidas, HashSet<uint> formatosFecha)
    {
        var texto = celda.CellValue?.Text;
        var tipo = celda.DataType?.Value;
        if (tipo == CellValues.SharedString)
        {
            return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < compartidas.Count
                ? new CeldaExcel(compartidas[i], null, false)
                : CeldaExcel.Vacia;
        }
        if (tipo == CellValues.InlineString)
        {
            return new CeldaExcel(celda.InlineString?.InnerText, null, false);
        }
        if (tipo == CellValues.String || tipo == CellValues.Boolean || tipo == CellValues.Error)
        {
            return new CeldaExcel(texto, null, false);
        }
        if (tipo == CellValues.Date)
        {
            // Fecha ISO 8601 guardada como texto (poco común): se deja como texto con formato de fecha.
            return new CeldaExcel(texto, null, true);
        }
        if (texto is null)
        {
            return CeldaExcel.Vacia;
        }
        var esFecha = celda.StyleIndex?.Value is { } estilo && formatosFecha.Contains(estilo);
        return double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero)
            ? new CeldaExcel(null, numero, esFecha)
            : new CeldaExcel(texto, null, false);
    }

    /// <summary>Índices de los estilos de celda (cellXfs) cuyo formato de número es una fecha.</summary>
    private static HashSet<uint> FormatosDeFecha(Stylesheet? estilos)
    {
        var propios = estilos?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.NumberFormatId?.Value is not null)
            .ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode?.Value ?? string.Empty) ?? [];
        var resultado = new HashSet<uint>();
        var indice = 0u;
        foreach (var formato in estilos?.CellFormats?.Elements<CellFormat>() ?? [])
        {
            var id = formato.NumberFormatId?.Value ?? 0;
            if (FormatosFechaIntegrados.Contains(id) || (propios.TryGetValue(id, out var codigo) && EsCodigoDeFecha(codigo)))
            {
                resultado.Add(indice);
            }
            indice++;
        }
        return resultado;
    }

    /// <summary>¿El código de formato tiene día o año (sin contar texto entre comillas, escapes ni colores)?</summary>
    internal static bool EsCodigoDeFecha(string codigo)
    {
        var limpio = new System.Text.StringBuilder(codigo.Length);
        var entreComillas = false;
        var entreCorchetes = false;
        for (var i = 0; i < codigo.Length; i++)
        {
            var c = codigo[i];
            if (c == '"')
            {
                entreComillas = !entreComillas;
            }
            else if (entreComillas)
            {
            }
            else if (c == '\\')
            {
                i++;
            }
            else if (c == '[')
            {
                entreCorchetes = true;
            }
            else if (c == ']')
            {
                entreCorchetes = false;
            }
            else if (!entreCorchetes)
            {
                limpio.Append(char.ToLowerInvariant(c));
            }
        }
        var texto = limpio.ToString();
        return texto.Contains('d', StringComparison.Ordinal) || texto.Contains('y', StringComparison.Ordinal);
    }

    /// <summary>«B12» → 1 (columnas desde 0); <c>null</c> si la referencia no es válida.</summary>
    private static int? Columna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia))
        {
            return null;
        }
        var indice = 0;
        var letras = 0;
        foreach (var c in referencia)
        {
            if (c is >= 'A' and <= 'Z')
            {
                indice = (indice * 26) + (c - 'A' + 1);
                letras++;
            }
            else if (c is >= 'a' and <= 'z')
            {
                indice = (indice * 26) + (c - 'a' + 1);
                letras++;
            }
            else
            {
                break;
            }
            if (letras > 3)
            {
                return null;
            }
        }
        return letras == 0 ? null : indice - 1;
    }
}
