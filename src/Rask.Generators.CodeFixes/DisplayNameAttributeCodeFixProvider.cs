using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK131 ([DisplayName] on a property): rewrite [DisplayName("X")] as [Display(Name = "X")], the one
// attribute Rask reads a field's name from.
//
// The file gains `using System.ComponentModel.DataAnnotations;` where its short name does not bind yet (no global using
// brings it), and the new name is written fully qualified and handed to Roslyn's simplifier, so it reduces to a bare
// Display wherever that binds unambiguously. `using System.ComponentModel;` goes only when the compiler reports it
// unused once the attribute is gone (CS8019); anything else in the file still using it keeps it.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DisplayNameAttributeCodeFixProvider))]
[Shared]
public sealed class DisplayNameAttributeCodeFixProvider : RaskCodeFixProvider<AttributeSyntax>
{
    private const string DataAnnotations = "System.ComponentModel.DataAnnotations";
    private const string DisplayAttribute = "DisplayAttribute";
    private const string DisplayNameAttribute = "System.ComponentModel.DisplayNameAttribute";
    private const string ComponentModel = "System.ComponentModel";
    private const string UnusedUsing = "CS8019";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK131");

    protected override string Title => "Use [Display(Name = …)]";

    protected override string EquivalenceKey => "RASK131_UseDisplay";

    // Only .NET's own attribute with the one name it takes: a subclass of it may build its name any way it likes,
    // which no mechanical rewrite can carry over.
    protected override async Task<bool> CanFixAsync(CodeFixContext context, AttributeSyntax node)
    {
        if (node.ArgumentList?.Arguments is not { Count: 1 } arguments || arguments[0].NameEquals is not null)
        {
            return false;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var constructor = model?.GetSymbolInfo(node, context.CancellationToken).Symbol;
        return string.Equals(constructor?.ContainingType.ToDisplayString(), DisplayNameAttribute, StringComparison.Ordinal);
    }

    protected override async Task<Document> FixAsync(Document document, AttributeSyntax node, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax unit || model is null)
        {
            return document;
        }

        var replaced = unit.ReplaceNode(node, Display(node));
        if (!Imported(model, node.SpanStart))
        {
            replaced = WithUsing(replaced);
        }

        return await WithoutUnusedComponentModelAsync(document.WithSyntaxRoot(replaced), cancellationToken).ConfigureAwait(false);
    }

    // Whether the attribute's short name already binds there: through a using of this file, or a global one elsewhere.
    private static bool Imported(SemanticModel model, int position) =>
        model.GetSpeculativeSymbolInfo(position, SyntaxFactory.ParseName(DisplayAttribute), SpeculativeBindingOption.BindAsTypeOrNamespace)
            .Symbol is INamedTypeSymbol found
        && string.Equals(found.ToDisplayString(), DataAnnotations + "." + DisplayAttribute, StringComparison.Ordinal);

    // The file's usings with System.ComponentModel.DataAnnotations in its ordinal place among them.
    private static CompilationUnitSyntax WithUsing(CompilationUnitSyntax unit)
    {
        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(DataAnnotations))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var at = 0;
        while (at < unit.Usings.Count
               && string.CompareOrdinal(unit.Usings[at].Name?.ToString(), DataAnnotations) < 0
               && unit.Usings[at].GlobalKeyword.RawKind == 0)
        {
            at++;
        }

        return unit.WithUsings(unit.Usings.Insert(at, directive));
    }

    // [Display(Name = <the argument as written>)], keeping the attribute's trivia.
    private static AttributeSyntax Display(AttributeSyntax node)
    {
        var name = SyntaxFactory.ParseName(DataAnnotations + ".Display").WithAdditionalAnnotations(Simplifier.Annotation);
        var argument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Name")), null, node.ArgumentList!.Arguments[0].Expression);

        return SyntaxFactory.Attribute(name, SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(argument)))
            .WithTriviaFrom(node);
    }

    private static async Task<Document> WithoutUnusedComponentModelAsync(Document document, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var unused = model.GetDiagnostics(cancellationToken: cancellationToken)
            .Where(static d => string.Equals(d.Id, UnusedUsing, StringComparison.Ordinal))
            .Select(d => root.FindNode(d.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>())
            .FirstOrDefault(static u => u is { Alias: null, StaticKeyword.RawKind: 0 }
                                        && string.Equals(u.Name?.ToString(), ComponentModel, StringComparison.Ordinal));

        return unused is null
            ? document
            : document.WithSyntaxRoot(root.RemoveNode(unused, SyntaxRemoveOptions.KeepNoTrivia)!);
    }
}
