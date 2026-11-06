using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators;

/// <summary>Author text as it is written into generated source.</summary>
internal static class CodeText
{
    private static readonly char[] LineTerminators = { '\r', '\n', '\u0085', '\u2028', '\u2029' };

    /// <summary>
    ///     A C# string literal, quoted and escaped — escaped rather than raw, so quotes and braces in the text
    ///     (TypeScript, a doc comment, a prop name) can never break it.
    /// </summary>
    public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    ///     The author's own literal, but still text in a doc comment: one line, XML-escaped, so it cannot end
    ///     the comment.
    /// </summary>
    public static string Prose(string text) => Escape(SingleLine(text));

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
}
