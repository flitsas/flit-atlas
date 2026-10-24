using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Atlas.Pruebas.Soporte;

/// <summary>
/// Genera en memoria un .xlsx con las mismas formas que la hoja real de la rotación (HU 2.7), con datos ficticios: textos
/// como cadenas compartidas, fechas seriales de Excel con el formato propio <c>yyyy\-mm\-dd;@</c> o con <c>mm-dd-yy</c>
/// y números sueltos. Nunca se copia el Excel real a las pruebas. Lo usan las pruebas del módulo y las de la API.
/// </summary>
public static class ExcelFicticio
{
    public static readonly string[] Encabezados = ["Fecha", "Responsable", "Estado", "Compensado", "Observacion"];

    /// <summary>Fecha serial con el formato corto <c>mm-dd-yy</c> (como la columna Compensado del Excel real).</summary>
    public sealed record FechaCorta(DateOnly Fecha);

    /// <summary>
    /// Celdas: <see cref="string"/> → texto (cadena compartida); <see cref="DateOnly"/> → serial con formato
    /// <c>yyyy\-mm\-dd;@</c>; <see cref="FechaCorta"/> → serial con <c>mm-dd-yy</c>; <see cref="double"/> → número;
    /// <c>null</c> → vacía.
    /// </summary>
    public static byte[] Crear(IEnumerable<object?[]> filas, string hoja = "Rotacion Soporte", string[]? encabezados = null)
    {
        using var flujo = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook();
            var estilos = libro.AddNewPart<WorkbookStylesPart>();
            estilos.Stylesheet = new Stylesheet(
                new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = @"yyyy\-mm\-dd;@" }) { Count = 1 },
                new Fonts(new Font()) { Count = 1 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })) { Count = 1 },
                new Borders(new Border()) { Count = 1 },
                new CellFormats(
                    new CellFormat(),
                    new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
                    new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }) { Count = 3 });
            var compartidas = libro.AddNewPart<SharedStringTablePart>();
            compartidas.SharedStringTable = new SharedStringTable();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);

            var parte = libro.AddNewPart<WorksheetPart>();
            var datos = new SheetData();
            parte.Worksheet = new Worksheet(datos);
            var numero = 1u;
            foreach (var fila in new[] { (encabezados ?? Encabezados).Cast<object?>().ToArray() }.Concat(filas))
            {
                var row = new Row { RowIndex = numero };
                for (var c = 0; c < fila.Length; c++)
                {
                    var referencia = $"{(char)('A' + c)}{numero}";
                    switch (fila[c])
                    {
                        case null:
                            break;
                        case string texto:
                            if (!indices.TryGetValue(texto, out var i))
                            {
                                i = indices.Count;
                                indices[texto] = i;
                                compartidas.SharedStringTable.AppendChild(new SharedStringItem(new Text(texto)));
                            }
                            row.AppendChild(new Cell { CellReference = referencia, DataType = CellValues.SharedString, CellValue = new CellValue(i) });
                            break;
                        case DateOnly fecha:
                            row.AppendChild(new Cell { CellReference = referencia, StyleIndex = 1, CellValue = new CellValue(Serial(fecha)) });
                            break;
                        case FechaCorta corta:
                            row.AppendChild(new Cell { CellReference = referencia, StyleIndex = 2, CellValue = new CellValue(Serial(corta.Fecha)) });
                            break;
                        case double valor:
                            row.AppendChild(new Cell { CellReference = referencia, CellValue = new CellValue(valor) });
                            break;
                        default:
                            throw new ArgumentException($"Celda no soportada: {fila[c]!.GetType().Name}");
                    }
                }
                datos.AppendChild(row);
                numero++;
            }
            libro.Workbook.AppendChild(new Sheets(new Sheet { Id = libro.GetIdOfPart(parte), SheetId = 1, Name = hoja }));
            libro.Workbook.Save();
        }
        return flujo.ToArray();
    }

    public static double Serial(DateOnly fecha) => fecha.ToDateTime(TimeOnly.MinValue).ToOADate();

    /// <summary>
    /// Nombres ficticios de la hoja de ejemplo (en Atlas van con tildes; en el Excel, a veces sin ellas o solo el nombre).
    /// </summary>
    public static class Personas
    {
        public const string Ana = "Ana Pérez Demo";
        public const string Bruno = "Bruno Gómez Demo";
        public const string Carla = "Carla Núñez Demo";
        public const string Diego = "Diego Ruiz Demo";

        /// <summary>Aparece en el Excel y no existe en Atlas: se propone crearla inactiva.</summary>
        public const string Externa = "Elena Ficticia";
    }

    /// <summary>
    /// Una hoja como la real, con datos ficticios: fechas en texto y seriales, nombres sin tildes, Compensado con fecha,
    /// con «reemplazo», con «reemplazo y compensa» y ambiguo, una persona que no existe en Atlas, una fila sin estado y
    /// dos sábados futuros. Siete filas.
    /// </summary>
    public static byte[] HojaDeEjemplo(DateOnly sabadoFuturo) => Crear(
    [
        ["2026-06-13", "Ana Perez Demo", "Aplicado", null, null],
        ["2026-06-20", "bruno gomez demo", "Aplicado", new FechaCorta(new DateOnly(2026, 11, 8)), "Hizo el cierre de mes"],
        [new DateOnly(2026, 6, 27), "Carla Nunez Demo", "Aplicado", "Diego reemplazo y compensa", null],
        [new DateOnly(2026, 7, 4), "Ana Pérez Demo", null, "Bruno Reemplazo", null],
        ["2026-07-11", Personas.Externa, "Aplicado", null, null],
        [new DateOnly(2026, 7, 18), "Carla Núñez Demo", "Aplicado", "lo cubrió alguien del equipo", null],
        [sabadoFuturo, "Diego", null, null, null],
    ]);
}
