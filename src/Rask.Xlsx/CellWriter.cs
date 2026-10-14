using System.Globalization;

namespace Rask.Workbooks;

/// <summary>
///     One cell's XML. A text is ALWAYS an inline string, so one that begins with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is
///     shown as text and never worked out; only an <see cref="Xlsx.Formula" /> is a formula.
/// </summary>
internal static class CellWriter
{
    // How many characters a cell shows, as Excel will write it.
    public static int Wide(object? value) => CellValue.Normalize(value) switch
    {
        null => 0,
        Xlsx.Strong strong => Wide(strong.Value) + 1,
        Xlsx.Formula formula => Wide(formula.Now) + 1,
        Xlsx.Sum => 14,
        string text => text.Length,
        Xlsx.Moment => 16,
        Xlsx.Day => 10,
        bool => 5,
        decimal number => number.ToString("#,##0.00##", CultureInfo.InvariantCulture).Length,
        var other when CellValue.IsNumber(other) => CellValue.Invariant(other).Length,
        _ => 0,
    };

    public static string Cell(object? raw, string at) => CellValue.Normalize(raw) switch
    {
        null or "" or Xlsx.Strong { Value: null or "" } => "",
        Xlsx.Formula formula => $"<c r=\"{at}\" s=\"{(formula.Whole ? 3 : 5)}\"><f>{XmlText.Escape(formula.Text)}</f><v>{CellValue.Invariant(formula.Now)}</v></c>",
        Xlsx.Strong { Value: string text } => $"<c r=\"{at}\" s=\"3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText.Escape(text)}</t></is></c>",
        Xlsx.Strong { Value: decimal number } => $"<c r=\"{at}\" s=\"5\"><v>{CellValue.Invariant(number)}</v></c>",
        Xlsx.Strong { Value: { } number } when CellValue.IsNumber(number) => $"<c r=\"{at}\" s=\"3\"><v>{CellValue.Invariant(number)}</v></c>",
        string text => $"<c r=\"{at}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText.Escape(text)}</t></is></c>",
        Xlsx.Moment moment => $"<c r=\"{at}\" s=\"1\"><v>{CellValue.Serial(moment.WallClock)}</v></c>",
        Xlsx.Day day => $"<c r=\"{at}\" s=\"2\"><v>{CellValue.Serial(day.WallClock)}</v></c>",
        bool flag => $"<c r=\"{at}\" t=\"b\"><v>{(flag ? 1 : 0)}</v></c>",
        decimal number => $"<c r=\"{at}\" s=\"4\"><v>{CellValue.Invariant(number)}</v></c>",
        { } number when CellValue.IsNumber(number) => $"<c r=\"{at}\"><v>{CellValue.Invariant(number)}</v></c>",
        { } other => throw CellValue.Refuse(other),
    };
}
