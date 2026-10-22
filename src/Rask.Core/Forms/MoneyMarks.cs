using System.Globalization;
using System.Text;

namespace Rask.Core.Forms;

// An amount as a field shows it while it is typed into: digits in groups of three, one decimal mark and at most
// so many decimals — Alpine's $money, which the runtime's data-rask-mask-money shapes as the reader types
// (rask-field.ts, maskMoney). This is the same shape for the value the server writes into the field, and the
// way back: the text read with these marks, whatever the reader's culture says a comma is.
internal sealed class MoneyMarks
{
    private readonly NumberFormatInfo _format;

    internal MoneyMarks(char decimals, char thousands, int precision)
    {
        (Decimals, Thousands, Precision) = (decimals, thousands, precision);
        _format = new NumberFormatInfo
        {
            NumberDecimalSeparator = decimals.ToString(),
            NumberGroupSeparator = thousands.ToString(),
        };
    }

    internal char Decimals { get; }

    internal char Thousands { get; }

    internal int Precision { get; }

    /// <summary>The marks as the runtime reads them: <c>".,2"</c>.</summary>
    internal string Attribute => string.Concat(Decimals.ToString(), Thousands.ToString(), Precision.ToString(CultureInfo.InvariantCulture));

    /// <summary>The value as the field shows it: <c>1234.56m</c> is <c>"1,234.56"</c>.</summary>
    internal string Show(object? value) => value switch
    {
        null => string.Empty,
        string text => Shape(text),
        _ => Shape(Plain(BindingHelpers.FormatValue(value)).Replace('.', Decimals)),
    };

    /// <summary>The text in the parsers' spelling — <c>"1 234,56"</c> is <c>"1234.56"</c> — or null when it is no amount.</summary>
    internal string? Read(string raw) => NumberText.Invariant(raw, _format);

    // A double the invariant culture writes with an exponent, written out.
    private static string Plain(string text) => NumberText.WrittenOut(text) ?? text;

    // maskMoney's rule, without the caret: digits, one decimal mark, Precision decimals, a leading minus.
    private string Shape(string text)
    {
        var whole = new StringBuilder(text.Length);
        var fraction = new StringBuilder(Precision);
        var dotted = false;
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                if (!dotted)
                {
                    whole.Append(c);
                }
                else if (fraction.Length < Precision)
                {
                    fraction.Append(c);
                }
            }
            else if (c == Decimals && !dotted && Precision > 0)
            {
                dotted = true;
            }
        }

        var shaped = new StringBuilder(text.Length + (whole.Length / 3));
        if (text.AsSpan().TrimStart().StartsWith("-", StringComparison.Ordinal))
        {
            shaped.Append('-');
        }

        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0)
            {
                shaped.Append(Thousands);
            }

            shaped.Append(whole[i]);
        }

        return dotted ? shaped.Append(Decimals).Append(fraction).ToString() : shaped.ToString();
    }
}
