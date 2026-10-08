using System.Text;

namespace Rask;

/// <summary>
///     What an avatar shows for a person with no picture: their initials, and the hue those initials pick.
/// </summary>
/// <remarks>
///     Both rules are Flux's, read off its docs page rather than its source. Initials: the first letter of the
///     first word and of the last, or the first two letters of a lone word, the first of them capitalised.
///     Hue: the CRC-32 of the seed, modulo the seventeen chromatic hues in Tailwind's order — which is what
///     gives CP emerald, MJ sky, KC red, KN violet, KS fuchsia, BP lime and AB pink on that page.
/// </remarks>
internal static class UiAvatarInitials
{
    private const int Hues = 17;

    internal static string From(string? name, bool single)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "";
        }

        var first = char.ToUpperInvariant(words[0][0]).ToString();
        if (single)
        {
            return first;
        }

        if (words.Length > 1)
        {
            return first + char.ToUpperInvariant(words[^1][0]);
        }

        return words[0].Length > 1 ? first + char.ToLowerInvariant(words[0][1]) : first;
    }

    /// <summary>The same seed is the same hue, on every machine and in every run.</summary>
    internal static Ui.Color Hue(string seed) => (Ui.Color)(Crc32(Encoding.UTF8.GetBytes(seed)) % Hues);

    // The IEEE CRC-32 (the one zlib and PHP's crc32() compute), bit by bit: an avatar hashes a few bytes, and
    // the BCL's own implementation ships in a separate package the kit would have to take for this alone.
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        const uint polynomial = 0xEDB88320u;
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ polynomial;
            }
        }

        return ~crc;
    }
}
