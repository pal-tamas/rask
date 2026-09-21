using System.Text;

namespace Rask.Data;

/// <summary>
/// Turns what someone typed into a search box into a full-text query that means "rows containing all of these
/// words", and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// FTS5 has a query language — <c>OR</c>, <c>NOT</c>, <c>NEAR(…)</c>, <c>column:</c> filters, <c>^</c>, <c>*</c> — and
/// passing typed text straight through makes all of it live: a stray <c>"</c> is a syntax error the user sees
/// as a crash, and <c>title:x OR y</c> is a query they did not mean to write. So every word is sent as a quoted
/// phrase, which the tokenizer then splits exactly as it split the indexed text.
/// </para>
/// <para>
/// A word with no letter or digit in it is dropped: it would tokenize to nothing, and an empty phrase matches
/// nothing, so <c>"C# - intro"</c> would otherwise return no rows at all. The last word is a prefix match, so
/// results narrow as the user types.
/// </para>
/// </remarks>
internal static class FullTextQuery
{
    /// <summary>
    /// Enough for any real search box, and a bound on the work one request can ask of the index.
    /// </summary>
    internal const int MaxTerms = 16;

    /// <summary>
    /// The FTS5 query for <paramref name="text"/>, or <see langword="null"/> when it holds no searchable word.
    /// </summary>
    public static string? Compile(string? text)
    {
        if (Terms(text) is not { } terms)
        {
            return null;
        }

        var query = new StringBuilder();
        for (var i = 0; i < terms.Count; i++)
        {
            if (i > 0)
            {
                query.Append(' ');
            }

            query.Append('"').Append(terms[i].Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        return query.Append(" *").ToString();
    }

    /// <summary>
    /// The same search in PostgreSQL's <c>tsquery</c> syntax (#1109): every word required, the last one as a prefix —
    /// <c>'first' &amp; 'second':*</c>. Each word is a quoted lexeme, so nothing a reader types is read as an operator;
    /// <c>to_tsquery</c> still normalises it through the text search configuration, as it does the indexed text.
    /// </summary>
    public static string? CompileTsQuery(string? text)
    {
        if (Terms(text) is not { } terms)
        {
            return null;
        }

        var query = new StringBuilder();
        for (var i = 0; i < terms.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" & ");
            }

            query.Append('\'')
                .Append(terms[i].Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal))
                .Append('\'');
        }

        return query.Append(":*").ToString();
    }

    // The words a search is made of, shared by every provider's syntax so "what counts as a word" cannot drift
    // between them: runs of non-whitespace carrying at least one letter or digit, at most MaxTerms of them.
    private static List<string>? Terms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var terms = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(static term => term.Any(char.IsLetterOrDigit))
            .Take(MaxTerms)
            .ToList();

        return terms.Count == 0 ? null : terms;
    }
}
