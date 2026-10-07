using System.Text;

namespace Rask;

/// <summary>
///     Flux's <c>mask</c> pattern (Alpine's): <c>9</c> a digit, <c>a</c> a letter, <c>*</c> either, anything else
///     written as it stands — <c>(999) 999-9999</c> turns <c>7161234567</c> into <c>(716) 123-4567</c>.
/// </summary>
/// <remarks>
///     Applied to the value the control draws and to the value it commits. Holding each keystroke to the
///     pattern as it is typed needs script in the page, which the kit does not ship.
/// </remarks>
internal static class UiInputMask
{
    /// <summary>The text laid into the pattern: what does not fit is dropped, and it stops where the text runs out.</summary>
    /// <param name="mask">The pattern.</param>
    /// <param name="text">What was typed, with or without the pattern's own characters.</param>
    internal static string Format(string mask, string? text)
    {
        var typed = Typed(mask, text ?? string.Empty);
        var formatted = new StringBuilder(mask.Length);
        var next = 0;

        foreach (var slot in mask)
        {
            if (next == typed.Length)
            {
                break;
            }

            formatted.Append(IsWildcard(slot) ? typed[next++] : slot);
        }

        return formatted.ToString();
    }

    // What the reader typed, without the pattern's own characters and without what no slot accepts.
    private static string Typed(string mask, string text)
    {
        var rest = new StringBuilder(text);
        foreach (var literal in mask.Where(slot => !IsWildcard(slot)))
        {
            RemoveFirst(rest, candidate => candidate == literal);
        }

        var typed = new StringBuilder(mask.Length);
        foreach (var slot in mask.Where(IsWildcard))
        {
            if (RemoveFirst(rest, candidate => Accepts(slot, candidate)) is not { } taken)
            {
                break;
            }

            typed.Append(taken);
        }

        return typed.ToString();
    }

    private static char? RemoveFirst(StringBuilder text, Func<char, bool> wanted)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (wanted(text[i]))
            {
                var found = text[i];
                text.Remove(i, 1);
                return found;
            }
        }

        return null;
    }

    private static bool IsWildcard(char slot) => slot is '9' or 'a' or '*';

    private static bool Accepts(char slot, char candidate) => slot switch
    {
        '9' => char.IsAsciiDigit(candidate),
        'a' => char.IsAsciiLetter(candidate),
        _ => char.IsAsciiLetterOrDigit(candidate),
    };
}
