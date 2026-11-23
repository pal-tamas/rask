using System.Globalization;

namespace Rask.SQLite;

/// <summary>
///     SQLite's <c>LIKE</c> — <c>%</c> for any run of characters, <c>_</c> for one, an optional escape character — matched
///     under a culture's comparison rather than SQLite's ASCII-only case folding.
/// </summary>
/// <remarks>
///     Matched position by position over the set of places the text could have reached, so a pattern of many <c>%</c>
///     costs its length times the text's, never the exponential walk a backtracking matcher can take. Total by
///     construction: it runs inside a native SQLite callback.
/// </remarks>
internal static class SqliteLikePattern
{
    /// <summary>Whether <paramref name="text" /> is like <paramref name="pattern" />.</summary>
    /// <param name="text">The text being tested.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="escape">The character that makes the next one literal, or null.</param>
    /// <param name="compare">The culture's comparison.</param>
    /// <param name="options">What the comparison ignores — <see cref="CompareOptions.Ordinal" /> for a binary one.</param>
    internal static bool IsMatch(string text, string pattern, char? escape, CompareInfo compare, CompareOptions options)
    {
        var reached = new bool[text.Length + 1];
        reached[0] = true;
        var literal = new System.Text.StringBuilder();

        var escaped = false;
        foreach (var c in pattern)
        {
            if (escaped || c is not ('%' or '_') && c != escape)
            {
                literal.Append(c);
                escaped = false;
                continue;
            }

            if (c == escape)
            {
                escaped = true;
                continue;
            }

            reached = Literal(text, reached, literal, compare, options);
            literal.Clear();
            reached = c == '%' ? AnyRun(reached) : AnyOne(text, reached);
        }

        reached = Literal(text, reached, literal, compare, options);
        return reached[text.Length];
    }

    private static bool[] Literal(string text, bool[] reached, System.Text.StringBuilder literal, CompareInfo compare, CompareOptions options)
    {
        if (literal.Length == 0)
        {
            return reached;
        }

        var value = literal.ToString();
        var next = new bool[reached.Length];
        for (var at = 0; at < reached.Length; at++)
        {
            if (!reached[at])
            {
                continue;
            }

            if (compare.IsPrefix(text.AsSpan(at), value, options, out var length))
            {
                next[at + length] = true;
            }
        }

        return next;
    }

    private static bool[] AnyRun(bool[] reached)
    {
        var next = new bool[reached.Length];
        var from = Array.IndexOf(reached, true);
        if (from >= 0)
        {
            Array.Fill(next, true, from, next.Length - from);
        }

        return next;
    }

    // One character, as SQLite counts them: a surrogate pair is one.
    private static bool[] AnyOne(string text, bool[] reached)
    {
        var next = new bool[reached.Length];
        for (var at = 0; at < text.Length; at++)
        {
            if (reached[at])
            {
                next[at + (char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]) ? 2 : 1)] = true;
            }
        }

        return next;
    }
}
