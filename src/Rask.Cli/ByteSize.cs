using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Cli;

/// <summary>
/// A size as people write it in <c>.rask/deploy.json</c> — <c>"20GB"</c>, <c>"3GiB"</c>, <c>"500MB"</c>, <c>"1.5TB"</c> —
/// read the way docker reads the same text in <c>builder prune --reserved-space 20GB</c>: <c>k</c>, <c>M</c>, <c>G</c>,
/// <c>T</c> are powers of 1024, with or without the <c>i</c>. One reading for every size a deploy is given, so the
/// build cache it keeps and the free space it guards are counted alike.
/// </summary>
internal static partial class ByteSize
{
    private const long Kibibyte = 1024;

    /// <summary>The largest size any setting takes: a petabyte, far past any one box's disk.</summary>
    internal const long Maximum = Kibibyte * Kibibyte * Kibibyte * Kibibyte * Kibibyte;

    /// <summary>Read <paramref name="text"/>, the value of <paramref name="key"/>, as a byte count from 0 to <see cref="Maximum"/>.</summary>
    internal static bool TryParse(string key, string text, out long bytes, out string? error)
    {
        bytes = 0;
        var match = Size().Match(text.Trim());
        if (!match.Success || !decimal.TryParse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            error = $"\"{key}\": \"{text}\" isn't a size — write it like \"20GB\", \"500MB\" or \"3GiB\".";
            return false;
        }

        var scaled = amount * Multiplier(match.Groups["unit"].Value);
        if (scaled > Maximum)
        {
            error = $"\"{key}\": \"{text}\" is out of range — at most {Format(Maximum)}, written like \"20GB\".";
            return false;
        }

        bytes = (long)decimal.Floor(scaled);
        error = null;
        return true;
    }

    /// <summary>
    /// <paramref name="bytes"/> in the largest unit that holds it whole — <c>20GB</c>, <c>1536MB</c>, <c>0B</c> — which
    /// <see cref="TryParse"/> and docker both read back as exactly <paramref name="bytes"/>.
    /// </summary>
    internal static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var unit = 0;
        var value = bytes;
        while (value != 0 && value % Kibibyte == 0 && unit < units.Length - 1)
        {
            value /= Kibibyte;
            unit++;
        }

        return value.ToString(CultureInfo.InvariantCulture) + units[unit];
    }

    private static decimal Multiplier(string unit) =>
        char.ToUpperInvariant(unit.Length > 0 ? unit[0] : 'B') switch
        {
            'K' => Kibibyte,
            'M' => Kibibyte * Kibibyte,
            'G' => Kibibyte * Kibibyte * Kibibyte,
            'T' => Kibibyte * Kibibyte * Kibibyte * Kibibyte,
            'P' => Maximum,
            _ => 1,
        };

    [GeneratedRegex(@"^(?<n>\d{1,12}(?:\.\d{1,6})?) ?(?<unit>(?:[kKmMgGtTpP][iI]?)?[bB]?)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Size();
}
