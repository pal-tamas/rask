using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.External.PackageIslands;

namespace Rask.Generators;

/// <summary>Author text as it is written into generated source.</summary>
internal static class CodeText
{
    /// <summary>
    ///     A C# string literal, quoted and escaped — escaped rather than raw, so quotes and braces in the text
    ///     (TypeScript, a doc comment, a prop name) can never break it.
    /// </summary>
    public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    ///     The author's own literal, but still text in a doc comment: one line, XML-escaped, so it cannot end
    ///     the comment.
    /// </summary>
    public static string Prose(string text) => PackageIslandNaming.Escape(PackageIslandNaming.SingleLine(text));
}
