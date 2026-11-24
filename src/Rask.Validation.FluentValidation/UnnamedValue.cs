using System.Globalization;
using System.Text;

namespace Rask;

// A value object's own value has no name of its own — the field it is bound to has the label — so its rules name it
// blank (FluentDisplayNames), and what FluentValidation's own sentence says about the blank is taken out:
// "The specified condition was not met for ''." reads "The specified condition was not met.".
// Plain string work, not a regex: a wall-clock match timeout fires on a starved CI runner, and there is no pattern
// here that needs one.
internal static class UnnamedValue
{
    private const string Blank = "''";

    // The words around the blank that only named it: English "for ''", Hungarian "a(z) '' mezőre".
    private static readonly string[] NamingBefore = ["for", "a(z)"];
    private const string NamingAfter = "mezőre";

    internal static string Said(string message)
    {
        if (!message.Contains(Blank, StringComparison.Ordinal))
        {
            return message;
        }

        var said = WithoutBlanks(message).Trim();
        if (said.Length == 0)
        {
            return message;
        }

        return char.ToUpper(said[0], CultureInfo.CurrentCulture) + said[1..];
    }

    private static string WithoutBlanks(string message)
    {
        var said = new StringBuilder(message.Length);
        var from = 0;
        for (var at = message.IndexOf(Blank, StringComparison.Ordinal); at >= 0; at = message.IndexOf(Blank, from, StringComparison.Ordinal))
        {
            said.Append(message, from, StartOfNaming(message, from, at) - from);
            from = EndOfNaming(message, at + Blank.Length);
        }

        return said.Append(message, from, message.Length - from).ToString();
    }

    // Back over the whitespace before the blank, the one naming word before it (a whole word), and the whitespace
    // before that word — never back past `floor`, where the text already kept ends.
    private static int StartOfNaming(string message, int floor, int at)
    {
        var start = SkipWhitespaceBack(message, floor, at);
        foreach (var word in NamingBefore)
        {
            var wordStart = start - word.Length;
            if (start < at && wordStart >= floor
                && message.AsSpan(wordStart, word.Length).SequenceEqual(word)
                && (wordStart == 0 || !char.IsLetterOrDigit(message[wordStart - 1])))
            {
                return SkipWhitespaceBack(message, floor, wordStart);
            }
        }

        return start;
    }

    // On past the naming word after the blank (after whitespace) and the one space beside it.
    private static int EndOfNaming(string message, int end)
    {
        var word = SkipWhitespace(message, end);
        if (word > end && message.AsSpan(word).StartsWith(NamingAfter, StringComparison.Ordinal))
        {
            end = word + NamingAfter.Length;
        }

        return end < message.Length && char.IsWhiteSpace(message[end]) ? end + 1 : end;
    }

    private static int SkipWhitespaceBack(string message, int floor, int at)
    {
        while (at > floor && char.IsWhiteSpace(message[at - 1]))
        {
            at--;
        }

        return at;
    }

    private static int SkipWhitespace(string message, int at)
    {
        while (at < message.Length && char.IsWhiteSpace(message[at]))
        {
            at++;
        }

        return at;
    }
}
