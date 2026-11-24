using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Batteries.Generators;

/// <summary>
///     Whether a generator can add a part to an aggregate's own class — a module initializer that registers one of its
///     private conventions (<c>On(E)</c>, <c>OnDelete()</c>): a partial, non-generic class declared in a namespace.
/// </summary>
internal static class AggregatePart
{
    /// <summary>Why a part cannot be added to <paramref name="aggregate" />, or null when it can.</summary>
    public static string? Problem(INamedTypeSymbol aggregate, CancellationToken token)
    {
        if (aggregate.ContainingType is not null)
        {
            return "it is nested in another type — declare it directly in a namespace";
        }

        if (aggregate.IsGenericType)
        {
            return "it is generic";
        }

        var partial = aggregate.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(token))
            .OfType<ClassDeclarationSyntax>()
            .Any(static c => c.Modifiers.Any(SyntaxKind.PartialKeyword));
        return partial ? null : "it is not partial — declare it 'partial class'";
    }
}
