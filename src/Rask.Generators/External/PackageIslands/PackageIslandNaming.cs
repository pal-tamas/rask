using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.External.PackageIslands;

/// <summary>
///     Turns names from a package's TypeScript into C# identifiers, and its prose into safe doc comments.
/// </summary>
/// <remarks>
///     Everything a snapshot carries is untrusted input — it was extracted from whatever <c>.d.ts</c> an
///     npm package shipped — so nothing here pastes it into generated code verbatim. An identifier is
///     rebuilt from its letters and digits; a doc comment is escaped and split so a line break cannot end
///     the comment and start code.
/// </remarks>
internal static class PackageIslandNaming
{
    /// <summary>
    ///     Every character C# ends a line at — not only <c>\r</c> and <c>\n</c>. A comment ends at NEL (U+0085),
    ///     LINE SEPARATOR (U+2028) and PARAGRAPH SEPARATOR (U+2029) too, so a doc string carrying one of those
    ///     would close its <c>///</c> and let whatever follows compile as code.
    /// </summary>
    private static readonly char[] LineTerminators = { '\r', '\n', '\u0085', '\u2028', '\u2029' };

    /// <summary>
    ///     A PascalCase identifier for <paramref name="raw" />: <c>small</c> → <c>Small</c>,
    ///     <c>x-large</c> → <c>XLarge</c>, <c>2xl</c> → <c>N2Xl</c>, <c>aria-label</c> → <c>AriaLabel</c>.
    /// </summary>
    /// <returns>The identifier, or <paramref name="fallback" /> when nothing usable is left.</returns>
    public static string Identifier(string raw, string fallback)
    {
        var sb = new StringBuilder(raw.Length);
        var startOfWord = true;
        var previous = '\0';

        foreach (var c in raw)
        {
            if (!char.IsLetterOrDigit(c))
            {
                startOfWord = true;
                previous = c;
                continue;
            }

            // A new word starts at a lower→Upper step (onClick) and at a letter↔digit step (h1, 2xl), so
            // the capital lands where a reader expects it rather than only after punctuation.
            var boundary = startOfWord
                           || (char.IsLower(previous) && char.IsUpper(c))
                           || (char.IsLetter(previous) && char.IsDigit(c))
                           || (char.IsDigit(previous) && char.IsLetter(c));

            sb.Append(boundary ? char.ToUpperInvariant(c) : c);
            startOfWord = false;
            previous = c;
        }

        if (sb.Length == 0)
        {
            return fallback;
        }

        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, 'N');
        }

        var result = sb.ToString();
        return SyntaxFacts.IsValidIdentifier(result) ? result : fallback;
    }

    /// <summary>The enum member name for a literal: a string by its words, a number by its digits.</summary>
    /// <param name="text">The literal's text — the string value, the number as written, or true/false.</param>
    /// <param name="isNumber">Whether the literal is a number.</param>
    /// <param name="fallback">What to use when nothing usable is left.</param>
    public static string EnumMember(string text, bool isNumber, string fallback)
    {
        if (text.Length == 0)
        {
            return "Empty";
        }

        if (!isNumber)
        {
            return Identifier(text, fallback);
        }

        // -1 → Minus1, 0.5 → N0Point5, 1e3 → N1E3. Spelled out so two different numbers cannot collapse
        // onto one member name the way stripping the punctuation would make 1.5 and 15 collide.
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '-' => "Minus",
                '+' => "Plus",
                '.' => "Point",
                _ => c.ToString(),
            });
        }

        return Identifier(sb.ToString(), fallback);
    }

    /// <summary>
    ///     <paramref name="candidate" />, or it with the lowest numeric suffix not yet in
    ///     <paramref name="taken" />. Records the result as taken.
    /// </summary>
    public static string Unique(string candidate, HashSet<string> taken)
    {
        if (taken.Add(candidate))
        {
            return candidate;
        }

        for (var i = 2; ; i++)
        {
            var next = candidate + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (taken.Add(next))
            {
                return next;
            }
        }
    }

    /// <summary>
    ///     The <c>///</c> lines of a summary for a generated member, indented by <paramref name="indent" />.
    /// </summary>
    /// <param name="doc">The package's own documentation, when it has any.</param>
    /// <param name="fallback">Plain text used when it has none. Escaped like everything else.</param>
    /// <param name="default">The package's documented default, appended when present.</param>
    /// <param name="indent">The indentation in front of each <c>///</c>.</param>
    public static string Summary(string? doc, string fallback, string? @default, string indent)
    {
        var text = string.IsNullOrWhiteSpace(doc) ? fallback : doc!;
        var sb = new StringBuilder();
        sb.Append(indent).AppendLine("/// <summary>");

        foreach (var line in text.Split(LineTerminators))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            sb.Append(indent).Append("///     ").AppendLine(Code(Escape(trimmed)));
        }

        if (!string.IsNullOrWhiteSpace(@default))
        {
            sb.Append(indent).Append("///     Defaults to <c>")
                .Append(Escape(SingleLine(@default!)))
                .AppendLine("</c> in the package.");
        }

        sb.Append(indent).AppendLine("/// </summary>");
        return sb.ToString();
    }

    /// <summary>
    ///     The inner XML of a one-line summary: the package's documentation when it has any, otherwise
    ///     <paramref name="fallback" />, escaped, with <c>`code`</c> spans kept.
    /// </summary>
    /// <remarks>
    ///     For the chain step's tooltip, which the factory generator writes on a single line. Line breaks in
    ///     the package's prose are folded to spaces rather than kept, so they cannot end the comment.
    /// </remarks>
    public static string SummaryText(string? doc, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(doc) ? fallback : doc!;
        return Code(Escape(SingleLine(text)));
    }

    /// <summary>
    ///     One line of text, safe inside a <c>//</c> or <c>///</c> comment: every line terminator C# honours is
    ///     folded to a single space.
    /// </summary>
    public static string SingleLine(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var part in text.Split(LineTerminators))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (folded.Length > 0)
            {
                folded.Append(' ');
            }

            folded.Append(trimmed);
        }

        return folded.ToString();
    }

    /// <summary>Escapes the three characters XML documentation cannot carry as text.</summary>
    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // `code` spans become <c>code</c>. Applied after escaping, so a backtick pair can only ever wrap
    // text that is already safe.
    private static string Code(string escaped)
    {
        var sb = new StringBuilder(escaped.Length);
        var open = false;
        foreach (var c in escaped)
        {
            if (c == '`')
            {
                sb.Append(open ? "</c>" : "<c>");
                open = !open;
                continue;
            }

            sb.Append(c);
        }

        if (open)
        {
            sb.Append("</c>");
        }

        return sb.ToString();
    }
}
