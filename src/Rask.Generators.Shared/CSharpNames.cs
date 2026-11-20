using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Shared;

/// <summary>Names as emitted C# spells them: an identifier a keyword would break, a namespace an assembly name would.</summary>
internal static class CSharpNames
{
    /// <summary>
    ///     A name as a valid C# identifier in emitted code. <c>ISymbol.Name</c> strips the leading <c>@</c> from a
    ///     verbatim identifier (a property declared <c>@event</c> has Name <c>event</c>), so a reserved keyword is
    ///     re-escaped wherever it is emitted as an identifier. Only for emitted identifiers: comparisons against
    ///     metadata names keep the raw name.
    /// </summary>
    public static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    ///     An assembly name as a namespace: <c>My-App</c> → <c>My_App</c>, <c>1st.Shop</c> → <c>_1st.Shop</c>. A
    ///     project's assembly name can carry characters a namespace cannot; MSBuild's own default RootNamespace maps
    ///     them to <c>_</c>, and this does the same.
    /// </summary>
    public static string Namespace(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var segments = name.Split('.').Select(static segment =>
        {
            var id = new string(segment.Select(static ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
            return id.Length == 0 || char.IsDigit(id[0]) ? "_" + id : Escape(id);
        });
        return string.Join(".", segments);
    }
}
