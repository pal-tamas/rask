namespace Rask.Core.Forms;

// How a typed-into field that edits a number spells it: what the field shows of the value, and the way back from what
// the reader typed to the one spelling the parsers read (digits, a dot for the fraction). An amount field spells it
// with its own marks (MoneyMarks); any other text field over a number, in the reader's culture (ReaderNumbers). A
// number input needs neither: the browser always sends it in the parsers' spelling.
internal interface IFieldNumbers
{
    /// <summary>The value as the field shows it.</summary>
    string Show(object? value);

    /// <summary>What the reader typed, in the parsers' spelling, or null when it is no number.</summary>
    string? Read(string raw);
}
