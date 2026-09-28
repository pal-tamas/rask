using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.ScopedScripts;

internal static class Names
{
    /// <summary><c>width</c> → <c>Width</c>; null when the name has no C# spelling.</summary>
    public static string? Pascal(string name)
    {
        var sb = new StringBuilder(name.Length);
        var upper = true;
        foreach (var c in name)
        {
            if (c is '-' or ' ' or '.')
            {
                upper = true;
                continue;
            }

            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return null;
            }

            if (sb.Length == 0 && c == '_')
            {
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.Length == 0 || char.IsDigit(sb[0]) ? null : sb.ToString();
    }

    /// <summary>A TypeScript parameter name as a C# one — a keyword is escaped (<c>@event</c>).</summary>
    public static string Parameter(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (clean.Length == 0 || char.IsDigit(clean[0]))
        {
            clean = "_" + clean;
        }

        return SyntaxFacts.GetKeywordKind(clean) != SyntaxKind.None ? "@" + clean : clean;
    }
}
