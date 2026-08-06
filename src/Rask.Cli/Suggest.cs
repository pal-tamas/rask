namespace Rask.Cli;

/// <summary>
/// Finds the nearest spelling of a word the user got wrong — the "did you mean 'generate'?" behind every
/// unknown command, option, action, and off-list option value. Deliberately conservative: it offers a
/// correction only when one candidate is clearly closest, because a confidently wrong suggestion costs
/// the reader more than no suggestion at all.
/// </summary>
internal static class Suggest
{
    /// <summary>
    /// The nearest candidate to <paramref name="input"/>, or <c>null</c> when nothing is close enough.
    /// Matching is case-insensitive and tolerates a transposition ("srever" → "server"); an unambiguous
    /// prefix ("gen" → "generate") also counts, since an abbreviation is a guess, not a typo.
    /// </summary>
    public static string? Closest(string? input, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        // A short word can only afford one edit before the "correction" is really a different word;
        // longer ones can absorb two. Anything further apart is left uncorrected on purpose.
        var budget = input.Length <= 3 ? 1 : 2;

        string? best = null;
        var bestDistance = int.MaxValue;
        string? onlyPrefixMatch = null;
        var prefixMatches = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Equals(input, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            // Two or more candidates share the prefix, so it identifies nothing — drop the prefix route.
            if (input.Length >= 3 && candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                prefixMatches++;
                onlyPrefixMatch = candidate;
            }

            var distance = Distance(input, candidate, budget);
            if (distance <= budget && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        // A lone prefix match wins over the nearest edit: "dep" is one substitution from "dev", but
        // someone who typed it was spelling "deploy" and stopped.
        return prefixMatches == 1 ? onlyPrefixMatch : best;
    }

    /// <summary>
    /// Optimal string alignment distance (Levenshtein plus adjacent transposition), case-insensitive and
    /// abandoned early once every cell in a row exceeds <paramref name="budget"/> — the answer is only ever
    /// compared against that budget, so the exact value beyond it is never needed.
    /// </summary>
    private static int Distance(string a, string b, int budget)
    {
        if (Math.Abs(a.Length - b.Length) > budget)
        {
            return int.MaxValue;
        }

        var previousPrevious = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowBest = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                var value = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);

                if (i > 1 && j > 1
                    && char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 2])
                    && char.ToLowerInvariant(a[i - 2]) == char.ToLowerInvariant(b[j - 1]))
                {
                    value = Math.Min(value, previousPrevious[j - 2] + 1);
                }

                current[j] = value;
                rowBest = Math.Min(rowBest, value);
            }

            if (rowBest > budget)
            {
                return int.MaxValue;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[b.Length];
    }
}
