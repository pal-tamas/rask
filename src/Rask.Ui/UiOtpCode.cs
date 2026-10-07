using System.Text;

namespace Rask;

/// <summary>
///     What a code becomes when one of its cells changes — Flux's rules, run where the value lives.
/// </summary>
/// <remarks>
///     <para>
///     A code has no gaps: deleting a character closes the row up, and a character typed past the end joins the
///     end.
///     </para>
///     <para>
///     A cell is a text input, and nothing moves focus on from it when a character lands, so it can come to hold
///     several: a code typed straight through without leaving the cell, a paste, a code the phone offered. Then
///     the cell <em>runs on</em> — its text is the code from that cell to the end — until focus leaves it.
///     </para>
/// </remarks>
internal static class UiOtpCode
{
    /// <summary>The characters of <paramref name="text" /> the mode takes, letters upper-cased, no more than fit.</summary>
    internal static string Filter(string? text, Ui.OtpMode mode, int length)
    {
        var kept = new StringBuilder();
        foreach (var character in text ?? "")
        {
            if (kept.Length < length && Takes(mode, character))
            {
                kept.Append(char.ToUpperInvariant(character));
            }
        }

        return kept.ToString();
    }

    /// <summary>The code after cell <paramref name="cell" /> reported <paramref name="raw" /> as its text.</summary>
    /// <param name="code">The code so far.</param>
    /// <param name="cell">Which cell changed, from 0.</param>
    /// <param name="raw">The cell's whole text.</param>
    /// <param name="mode">Which characters the code takes.</param>
    /// <param name="length">How many cells there are.</param>
    /// <param name="runsOn">Whether the cell already held the rest of the code.</param>
    /// <returns>The new code, and whether the cell now holds the rest of it.</returns>
    internal static (string Code, bool RunsOn) Typed(string code, int cell, string raw, Ui.OtpMode mode, int length, bool runsOn)
    {
        var kept = Filter(raw, mode, int.MaxValue);
        if (!runsOn && kept.Length < 2)
        {
            return (One(code, cell, raw, kept), false);
        }

        // A character typed beside the one a cell in the middle held: the other one is the new one.
        if (!runsOn && cell < code.Length - 1 && kept.Length == 2 && kept.Contains(code[cell], StringComparison.Ordinal))
        {
            return (Put(code, cell, kept[0] == code[cell] ? kept[1] : kept[0]), false);
        }

        var ran = string.Concat(code.AsSpan(0, Math.Min(cell, code.Length)), kept);

        return (ran.Length > length ? ran[..length] : ran, true);
    }

    private static string One(string code, int cell, string raw, string kept)
    {
        if (raw.Length == 0)
        {
            return cell < code.Length ? code.Remove(cell, 1) : code;
        }

        return kept.Length == 0 ? code : Put(code, cell, kept[0]);
    }

    private static string Put(string code, int cell, char character) =>
        cell < code.Length
            ? string.Concat(code.AsSpan(0, cell), [character], code.AsSpan(cell + 1))
            : code + character;

    private static bool Takes(Ui.OtpMode mode, char character) => mode switch
    {
        Ui.OtpMode.Alpha => char.IsAsciiLetter(character),
        Ui.OtpMode.Alphanumeric => char.IsAsciiLetterOrDigit(character),
        _ => char.IsAsciiDigit(character),
    };
}
