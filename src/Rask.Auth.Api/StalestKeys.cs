namespace Rask.Auth;

/// <summary>
///     The keys an in-memory count of attempts lets go of once it holds as many as it may: the stalest, a tenth of the
///     bound at a time.
/// </summary>
/// <remarks>
///     A spray of distinct keys inside one window — a list of addresses, or a guesser spread over many clients — fails
///     every one of them now, so dropping only the keys whose failures have expired frees nothing and the table grows
///     without bound. The bound is what keeps the process's memory its own. Letting go of a tenth at once keeps the sort
///     off the path of every new key: a table at its bound sorts once per that many arrivals.
/// </remarks>
internal static class StalestKeys
{
    /// <summary>The keys to let go of so <paramref name="entries" /> stands at nine tenths of <paramref name="most" />.</summary>
    /// <param name="entries">The table's entries, read once.</param>
    /// <param name="most">How many keys the table may hold.</param>
    /// <param name="lastSeen">When an entry last counted an attempt.</param>
    /// <typeparam name="T">What the table keeps per key.</typeparam>
    public static IEnumerable<KeyValuePair<string, T>> Over<T>(
        IReadOnlyCollection<KeyValuePair<string, T>> entries, int most, Func<T, DateTimeOffset> lastSeen)
    {
        var excess = entries.Count - (most - Math.Max(1, most / 10));
        return excess <= 0 ? [] : entries.OrderBy(entry => lastSeen(entry.Value)).Take(excess).ToList();
    }
}
