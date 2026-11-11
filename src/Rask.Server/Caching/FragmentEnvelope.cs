using System.Globalization;

namespace Rask.Server.Caching;

/// <summary>The markup and when it lapses, as one string — the shape every <c>HybridCache</c> serializer carries as is.</summary>
internal readonly record struct FragmentEnvelope(string Html, DateTimeOffset AbsoluteEnd, TimeSpan? Sliding, DateTimeOffset ReadAt, string[] Tags)
{
    // absoluteEnd ticks, sliding ticks (0 for none), read-at ticks, tag count, then each tag and the markup, each after its length.
    internal string Seal()
    {
        var head = string.Join(
            ',',
            AbsoluteEnd.UtcTicks.ToString(CultureInfo.InvariantCulture),
            (Sliding?.Ticks ?? 0).ToString(CultureInfo.InvariantCulture),
            ReadAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            string.Join(',', Tags.Select(static t => t.Length.ToString(CultureInfo.InvariantCulture) + ":" + t)));
        return head.Length.ToString(CultureInfo.InvariantCulture) + ";" + head + Html;
    }

    internal static bool TryOpen(string sealedText, out FragmentEnvelope envelope)
    {
        envelope = default;
        var semicolon = sealedText.IndexOf(';', StringComparison.Ordinal);
        if (semicolon < 1 || !int.TryParse(sealedText.AsSpan(0, semicolon), NumberStyles.None, CultureInfo.InvariantCulture, out var headLength)
                          || semicolon + 1 + headLength > sealedText.Length)
        {
            return false;
        }

        var head = sealedText.AsSpan(semicolon + 1, headLength);
        Span<Range> fields = stackalloc Range[4];
        if (head.Split(fields, ',', StringSplitOptions.None) < 3
            || !long.TryParse(head[fields[0]], NumberStyles.None, CultureInfo.InvariantCulture, out var absolute)
            || !long.TryParse(head[fields[1]], NumberStyles.None, CultureInfo.InvariantCulture, out var sliding)
            || !long.TryParse(head[fields[2]], NumberStyles.None, CultureInfo.InvariantCulture, out var readAt))
        {
            return false;
        }

        envelope = new FragmentEnvelope(
            sealedText[(semicolon + 1 + headLength)..],
            new DateTimeOffset(absolute, TimeSpan.Zero),
            sliding == 0 ? null : TimeSpan.FromTicks(sliding),
            new DateTimeOffset(readAt, TimeSpan.Zero),
            TagsIn(head[fields[3]].ToString()));
        return true;
    }

    private static string[] TagsIn(string list)
    {
        var tags = new List<string>();
        var at = 0;
        while (at < list.Length)
        {
            var colon = list.IndexOf(':', at);
            if (colon < 0 || !int.TryParse(list.AsSpan(at, colon - at), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                          || colon + 1 + length > list.Length)
            {
                break;
            }

            tags.Add(list.Substring(colon + 1, length));
            at = colon + 1 + length + 1;
        }

        return [.. tags];
    }
}
