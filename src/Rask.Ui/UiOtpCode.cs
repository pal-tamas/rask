using System.Text;

namespace Rask;

/// <summary>What a one-time code keeps of a text: the characters its mode takes, as Flux writes them.</summary>
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

    private static bool Takes(Ui.OtpMode mode, char character) => mode switch
    {
        Ui.OtpMode.Alpha => char.IsAsciiLetter(character),
        Ui.OtpMode.Alphanumeric => char.IsAsciiLetterOrDigit(character),
        _ => char.IsAsciiDigit(character),
    };
}
