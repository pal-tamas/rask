using System.Text;

namespace Rask.Storage.Upload;

/// <summary>
/// Reduces a client-supplied file name to a display leaf safe to store and to put in a
/// <c>Content-Disposition</c> header.
/// </summary>
/// <remarks>
/// The same rules as the server's upload staging (<c>SanitizeUploadFileName</c>) and the CQRS download
/// header (<c>SafeLeaf</c>), plus two a stored name needs: bidirectional overrides are removed, so
/// <c>invoice&#x202E;fdp.exe</c> cannot display as <c>invoiceexe.pdf</c>, and the result is NFC so one name
/// has one spelling. The name never reaches a path or a key — keys come from the id — so this is about what a
/// person reads, and what a header carries.
/// </remarks>
internal static class SafeFileName
{
    internal const int MaxLength = 255;
    private const string Fallback = "file";

    internal static string Clean(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return Fallback;
        }

        var separator = fileName.AsSpan().LastIndexOfAny('/', '\\');
        var leaf = separator >= 0 ? fileName.AsSpan(separator + 1) : fileName.AsSpan();

        var sb = new StringBuilder(Math.Min(leaf.Length, MaxLength));
        for (var i = 0; i < leaf.Length; i++)
        {
            var c = leaf[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < leaf.Length && char.IsLowSurrogate(leaf[i + 1]))
                {
                    sb.Append(c).Append(leaf[i + 1]);
                    i++;
                }

                continue;
            }

            if (char.IsLowSurrogate(c) || char.IsControl(c) || c == '"' || IsBidiControl(c))
            {
                continue;
            }

            sb.Append(c);
        }

        var normalized = sb.ToString().Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length > MaxLength)
        {
            var cut = char.IsHighSurrogate(normalized[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            normalized = normalized[..cut].TrimEnd();
        }

        return normalized.Length == 0 || normalized is "." or ".." ? Fallback : normalized;
    }

    // LRM/RLM, the embedding and override controls, and the isolates.
    private static bool IsBidiControl(char c) =>
        c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
