using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rask.Generators.CodeFixes;

// The arguments RASK130's fix hands a value object's base record, by parameter name, as written: `MaxLength` = `200`.
// Two rules that give one parameter different values conflict, and the fix is then not offered.
internal sealed class BaseArguments
{
    private readonly Dictionary<string, string> _written = new(StringComparer.Ordinal);

    // Whether two rules disagree on one parameter.
    public bool Conflicts { get; private set; }

    // Whether a rule says the text must be filled: [Required] or NotEmpty().
    public bool Required { get; set; }

    public string this[string parameter] => _written[parameter];

    public bool Has(string parameter) => _written.ContainsKey(parameter);

    public void Set(string parameter, string literal)
    {
        if (_written.TryGetValue(parameter, out var already) && !string.Equals(already, literal, StringComparison.Ordinal))
        {
            Conflicts = true;
        }

        _written[parameter] = literal;
    }

    // A whole-number argument's value, or null when there is none.
    public long? Number(string parameter) =>
        _written.TryGetValue(parameter, out var literal) &&
        long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // A numeric constant as the base's parameter takes it — `200`, `0.5m` — or null when it is none, or a fraction a
    // whole number cannot take.
    public static string? Literal(object? value, bool whole)
    {
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
        {
            return null;
        }

        decimal number;
        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }

        if (number == decimal.Truncate(number))
        {
            return number.ToString("0", CultureInfo.InvariantCulture);
        }

        return whole ? null : number.ToString(CultureInfo.InvariantCulture) + "m";
    }
}
