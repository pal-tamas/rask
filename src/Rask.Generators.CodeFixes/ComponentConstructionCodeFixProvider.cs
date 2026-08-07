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
///     RASK014 — rewrite <c>new Widget()</c> into the generated <c>Widget()</c> factory call.
/// </summary>
/// <remarks>
///     <para>
///         The highest-value fix in the set: RASK014 is an <b>Error</b>, so it stops the build, and it is
///         the first thing a Blazor or plain-C# migrant hits, because <c>new</c> is simply what you reach
///         for. The diagnostic message already computes the exact replacement, so nothing is inferred here.
///     </para>
///     <para>
///         <b>Deliberately withheld for anything but an argument-free, initializer-free construction.</b>
///         A constructor call and a factory call are not the same shape: the factory's parameters are
///         generated from the component's public properties in a defined order, which is not the
///         constructor's, so carrying positional arguments across would compile and mean something else.
///         And an object initializer is only legal after <c>new</c>, so it cannot ride along either. In
///         both cases the error stands with its message, which names the factory to call — a quick fix
///         that silently changes meaning is worse than none.
///     </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ComponentConstructionCodeFixProvider))]
[Shared]
public sealed class ComponentConstructionCodeFixProvider : RaskCodeFixProvider<ObjectCreationExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK014"];

    protected override string Title => "Use the generated factory";

    protected override string EquivalenceKey => "RASK014_UseFactory";

    protected override Task<bool> CanFixAsync(CodeFixContext context, ObjectCreationExpressionSyntax node) =>
        Task.FromResult(
            node.Initializer is null
            && (node.ArgumentList is null || node.ArgumentList.Arguments.Count == 0)
            && FactoryName(node) is not null);

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
            InvocationExpression(IdentifierName(name)).WithTriviaFrom(node),
            cancellationToken).ConfigureAwait(false);
    }

    // The factory is a static method named after the type, in scope project-wide through the generator's
    // `global using static …Generated`. So the bare type name IS the call: carrying a qualified name over
    // would name a type where a method has to go.
    private static string? FactoryName(ObjectCreationExpressionSyntax node) => node.Type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => null,
    };
}
