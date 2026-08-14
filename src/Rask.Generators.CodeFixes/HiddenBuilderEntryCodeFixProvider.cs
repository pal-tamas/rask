using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     Quick-fix for <b>CS0108</b> inside a Rask component: add the <c>new</c> modifier the compiler
///     asks for.
///     <para>
///         Every component type contributes a builder entry named after itself, inherited by every
///         component, so a member that shares a tag's or component's name now hides one: a
///         <c>Component? Footer</c> property, a private <c>Section(…)</c> helper, a nested
///         <c>record Line</c>, a test field. All of them are deliberate, all of them want the same
///         one-word edit, and there are enough of them that doing it by hand is the wrong tool.
///     </para>
///     <para>
///         A <c>DiagnosticSuppressor</c> would be the obvious alternative and is deliberately not used:
///         the compiler honours one, but <c>dotnet format</c> does not, so it re-applies the fix on
///         every run and the format gate never settles.
///     </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HiddenBuilderEntryCodeFixProvider))]
[Shared]
public sealed class HiddenBuilderEntryCodeFixProvider : RaskCodeFixProvider<MemberDeclarationSyntax>
{
    private const string ComponentFullName = "Rask.Core.Component";

    // Everything csharp_preferred_modifier_order puts BEFORE `new`, so the inserted token lands where
    // the formatter would leave it and the fix does not fight IDE0036 on the next run.
    private static readonly SyntaxKind[] PrecedeNew =
    {
        SyntaxKind.PublicKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.InternalKeyword,
        SyntaxKind.FileKeyword,
        SyntaxKind.StaticKeyword,
        SyntaxKind.ExternKeyword,
    };

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CS0108");

    protected override string Title => "Add 'new' to hide the inherited Rask builder entry";

    protected override string EquivalenceKey => "RaskBuilderEntry_AddNew";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, MemberDeclarationSyntax member)
    {
        if (member.Modifiers.Any(SyntaxKind.NewKeyword))
        {
            return false;
        }

        // Only inside a component: CS0108 elsewhere is an ordinary hiding warning that the user's own
        // base type caused, and answering it with `new` is a decision the framework has no part in.
        if (member.Parent is not TypeDeclarationSyntax owner)
        {
            return false;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (model?.Compilation.GetTypeByMetadataName(ComponentFullName) is not { } component)
        {
            return false;
        }

        var declared = model.GetDeclaredSymbol(owner, context.CancellationToken);
        for (var current = declared as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, component))
            {
                return true;
            }
        }

        return false;
    }

    protected override Task<Document> FixAsync(
        Document document, MemberDeclarationSyntax member, CancellationToken cancellationToken)
    {
        var keyword = SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        var modifiers = member.Modifiers;

        var index = 0;
        while (index < modifiers.Count && Array.IndexOf(PrecedeNew, modifiers[index].Kind()) >= 0)
        {
            index++;
        }

        MemberDeclarationSyntax updated;
        if (index > 0)
        {
            updated = member.WithModifiers(modifiers.Insert(index, keyword));
        }
        else if (modifiers.Count > 0)
        {
            // `new` becomes the declaration's first token, so it inherits the indentation and any
            // doc comment that the old first modifier was carrying.
            var first = modifiers[0];
            updated = member.WithModifiers(modifiers
                .Replace(first, first.WithLeadingTrivia(SyntaxTriviaList.Empty))
                .Insert(0, keyword.WithLeadingTrivia(first.LeadingTrivia)));
        }
        else
        {
            var leading = member.GetLeadingTrivia();
            updated = member
                .WithLeadingTrivia(SyntaxTriviaList.Empty)
                .AddModifiers(keyword.WithLeadingTrivia(leading));
        }

        return ReplaceNodeAsync(document, member, updated, cancellationToken);
    }
}
