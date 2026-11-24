using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Rask.Generators.CodeFixes;

// The usings a fix adds: whether a short type name already binds where the fix writes it, and the file's usings with one
// more namespace in its ordinal place among them.
internal static class UsingDirectives
{
    // Whether `shortName` binds at `position` to the type `qualifiedName`: through a using of this file, or a global one.
    public static bool Binds(SemanticModel model, int position, string shortName, string qualifiedName) =>
        model.GetSpeculativeSymbolInfo(position, SyntaxFactory.ParseName(shortName), SpeculativeBindingOption.BindAsTypeOrNamespace)
            .Symbol is INamedTypeSymbol found
        && string.Equals(found.ToDisplayString(), qualifiedName, StringComparison.Ordinal);

    // The file's usings with `using {name};` in its ordinal place among the plain ones.
    public static CompilationUnitSyntax With(CompilationUnitSyntax unit, string name)
    {
        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var at = 0;
        while (at < unit.Usings.Count
               && string.CompareOrdinal(unit.Usings[at].Name?.ToString(), name) < 0
               && unit.Usings[at].GlobalKeyword.RawKind == 0)
        {
            at++;
        }

        return unit.WithUsings(unit.Usings.Insert(at, directive));
    }
}
