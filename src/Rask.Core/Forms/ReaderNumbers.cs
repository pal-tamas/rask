using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// A number in a TEXT field (`type=text`, `inputmode=decimal`): shown and read in the reader's culture, so it comes back
// as it went out. It used to be shown in the parsers' spelling ("1234.5") and read by guessing — text of digits and a
// dot was taken as the parsers' spelling in every culture — so a German reader's "1.234" (one thousand two hundred and
// thirty-four) was 1.234. Now a German reader sees "1234,5" and types "1.234,5"; a Hungarian one sees "1234,5" and
// types "1 234,5". A number input (`type=number`) is not this: the browser sends it in the parsers' spelling whatever
// the language, and it stays so.
internal sealed class ReaderNumbers : IFieldNumbers
{
    internal static readonly ReaderNumbers Instance = new();

    private ReaderNumbers()
    {
    }

    /// <summary>The value with the reader's decimal mark and no groups: <c>1234.5</c> is <c>"1234,5"</c> in de-DE.</summary>
    public string Show(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var text = BindingHelpers.FormatValue(value);
        text = NumberText.WrittenOut(text) ?? text;
        var mark = RaskCulture.Current.NumberFormat.NumberDecimalSeparator;
        return string.Equals(mark, ".", StringComparison.Ordinal) ? text : text.Replace(".", mark, StringComparison.Ordinal);
    }

    /// <summary>The reader's text in the parsers' spelling, read with their culture's decimal mark and groups.</summary>
    public string? Read(string raw) => NumberText.Invariant(raw, RaskCulture.Current.NumberFormat);
}
