using System.Globalization;

namespace Rask.Sync;

/// <summary>
///     A point on a hybrid logical clock: wall-clock milliseconds, a counter that breaks ties within the
///     same millisecond, and the node that issued it.
/// </summary>
/// <remarks>
///     <para>
///         Ordering is <see cref="PhysicalMs" />, then <see cref="Counter" />, then
///         <see cref="NodeId" />. The node id is not decoration — it is what makes the order
///         <b>total</b>. Two devices can otherwise produce byte-identical stamps, and a merge whose
///         outcome depends on which op happened to arrive first is not a merge, it is a race.
///     </para>
///     <para>
///         <see cref="ToString" /> is fixed-width hex, so <b>lexicographic order on the string equals
///         <see cref="CompareTo(HlcTimestamp)" /></b>. That is what lets a log be ordered by object key
///         alone, with no parsing and no index.
///     </para>
/// </remarks>
/// <param name="PhysicalMs">Milliseconds since the Unix epoch, as the issuing node believed them to be.</param>
/// <param name="Counter">Disambiguates events within one millisecond, and lets a node stay monotonic when its wall clock stalls or moves backwards.</param>
/// <param name="NodeId">The issuing node — the final tie-break, so no two distinct stamps ever compare equal.</param>
public readonly record struct HlcTimestamp(long PhysicalMs, int Counter, string NodeId)
    : IComparable<HlcTimestamp>
{
    /// <summary>The lowest possible stamp — before anything. Useful as a "never synced" watermark.</summary>
    public static HlcTimestamp MinValue => new(0, 0, string.Empty);

    /// <inheritdoc />
    public int CompareTo(HlcTimestamp other)
    {
        var byPhysical = PhysicalMs.CompareTo(other.PhysicalMs);
        if (byPhysical != 0)
        {
            return byPhysical;
        }

        var byCounter = Counter.CompareTo(other.Counter);
        return byCounter != 0
            ? byCounter
            // Ordinal, to match the lexicographic ordering of ToString(). A culture-aware comparison here
            // would make the merge outcome depend on the device's locale.
            : string.CompareOrdinal(NodeId, other.NodeId);
    }

    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;

    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;

    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;

    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    /// <summary>
    ///     The sortable wire form: <c>{physical:X12}-{counter:X4}-{node}</c>. Fixed-width hex so that
    ///     sorting these as strings gives the same order as comparing them as values — 12 hex digits of
    ///     milliseconds runs to the year 10889, which is enough.
    /// </summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{PhysicalMs:X12}-{Counter:X4}-{NodeId}");

    /// <summary>Parses a stamp produced by <see cref="ToString" />.</summary>
    /// <exception cref="FormatException">The text is not a stamp.</exception>
    public static HlcTimestamp Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse(text, out var stamp)
            ? stamp
            : throw new FormatException($"'{text}' is not a hybrid logical clock timestamp.");
    }

    /// <summary>Parses a stamp produced by <see cref="ToString" />, returning whether it was well-formed.</summary>
    public static bool TryParse(string? text, out HlcTimestamp stamp)
    {
        stamp = default;

        // {12 hex}-{4 hex}-{node}: the node may itself contain '-' (a Guid does), so the split is by
        // position rather than by counting separators.
        if (text is null || text.Length < 18 || text[12] != '-' || text[17] != '-')
        {
            return false;
        }

        if (!long.TryParse(text.AsSpan(0, 12), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var physical) ||
            !int.TryParse(text.AsSpan(13, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var counter))
        {
            return false;
        }

        stamp = new HlcTimestamp(physical, counter, text[18..]);
        return true;
    }
}
