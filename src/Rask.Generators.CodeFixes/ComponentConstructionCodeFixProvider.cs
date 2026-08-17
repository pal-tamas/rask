using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK014 — rewrite <c>new Widget()</c> into the chain that builds it: the bare entry <c>Widget</c>.
/// </summary>
/// <remarks>
///     <para>
///         The highest-value fix in the set: RASK014 is an <b>Error</b>, so it stops the build, and it is
///         the first thing a Blazor or plain-C# migrant hits, because <c>new</c> is simply what you reach
///         for. The diagnostic message already computes the exact replacement, so nothing is inferred here.
///     </para>
///     <para>
///         <b>Deliberately withheld for anything but an argument-free, initializer-free construction
///         inside a markup host.</b> A constructor call and a chain are not the same shape: a chain sets
///         each property by name in its own step, so carrying positional constructor arguments across
///         would compile and mean something else. An object initializer is only legal after <c>new</c>,
///         so it cannot ride along either. And the bare entry only binds where entries live — outside a
///         markup host the rewrite is <c>CS0119</c>, a worse error than the one it replaces. In every
///         such case the error stands with its message, which spells out the chain to write; a quick fix
///         that silently changes meaning, or trades one error for a worse one, is worse than none.
///     </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ComponentConstructionCodeFixProvider))]
[Shared]
public sealed class ComponentConstructionCodeFixProvider : RaskCodeFixProvider<ObjectCreationExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK014"];

    protected override string Title => "Build it with the chain";

    protected override string EquivalenceKey => "RASK014_UseChain";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, ObjectCreationExpressionSyntax node)
    {
        if (node.Initializer is not null
            || (node.ArgumentList is not null && node.ArgumentList.Arguments.Count != 0)
            || FactoryName(node) is null)
        {
            return false;
        }

        // The bare entry only binds inside a MARKUP HOST: entries are protected static members on
        // RaskMarkup, or injected into a host's own partial. In a service, a Program.cs, a DI factory or
        // any plain class, `Widget` names the TYPE and the rewrite is CS0119 — worse than the error it
        // replaces, since RASK014 fires there too. The old factory call had no such limit (it rode a
        // project-wide `global using static`), which is why this gate arrived with the bare entry.
        if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration)
        {
            return false;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        return model?.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type
               && IsMarkupHost(type);
    }

    // Mirrors BuilderEntry.IsEntryHost, which lives in the analyzer assembly and is not visible here:
    // a type deriving from RaskMarkup (every Component does), or one carrying [RaskMarkup] because it
    // could not spend its base slot.
    private static bool IsMarkupHost(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    "Rask.Core.RaskMarkupAttribute",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), "Rask.Core.RaskMarkup", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    protected override async Task<Document> FixAsync(
        Document document,
        ObjectCreationExpressionSyntax node,
        CancellationToken cancellationToken)
    {
        if (FactoryName(node) is not { } name)
        {
            return document;
        }

        return await ReplaceNodeAsync(
            document,
            node,
            IdentifierName(name).WithTriviaFrom(node),
            cancellationToken).ConfigureAwait(false);
    }

    // The entry is a property named after the type, injected into every markup host by the generator. So
    // the bare simple name IS the chain: carrying a qualified name over would name the TYPE, which is
    // exactly what RASK014 is complaining about.
    private static string? FactoryName(ObjectCreationExpressionSyntax node) => node.Type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => null,
    };
}
