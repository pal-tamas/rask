using System.Text.RegularExpressions;

namespace Rask;

// A value object's own value has no name of its own — the field it is bound to has the label — so its rules name it
// blank (FluentDisplayNames), and what FluentValidation's own sentence says about the blank is taken out:
// "The specified condition was not met for ''." reads "The specified condition was not met.".
internal static partial class UnnamedValue
{
    internal static string Said(string message)
    {
        var said = Blank().Replace(message, "").Trim();
        if (said.Length == 0 || ReferenceEquals(said, message))
        {
            return message;
        }

        return char.ToUpper(said[0], System.Globalization.CultureInfo.CurrentCulture) + said[1..];
    }

    // The blank name in quotes, with the words around it that only named it (English "for ''", Hungarian
    // "a(z) '' mezőre"), and the space beside it.
    [GeneratedRegex(@"\s*(?:\bfor\s+|\ba\(z\)\s+)?''(?:\s+mezőre)?\s?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Blank();
}
