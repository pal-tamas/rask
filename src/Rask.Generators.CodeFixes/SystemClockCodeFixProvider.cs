using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK140 — the machine's clock becomes Rask's: <c>DateTime.Now</c> → <c>Clock.Wall</c>, <c>DateTime.Today</c> →
///     <c>Clock.Wall.Date</c> (and <c>DateOnly.FromDateTime(DateTime.Today)</c> → <c>Clock.Today</c>),
///     <c>DateTime.UtcNow</c> → <c>Clock.Now.UtcDateTime</c>, <c>DateTimeOffset.Now</c>/<c>UtcNow</c> →
///     <c>Clock.Now</c>, and <c>x.ToLocalTime()</c> → <c>TimeZoneInfo.ConvertTimeFromUtc(x, Clock.Zone)</c> (a
///     <c>DateTimeOffset</c>: <c>TimeZoneInfo.ConvertTime(x, Clock.Zone)</c>).
/// </summary>
/// <remarks>
///     Every replacement keeps the expression's type, so the code around it compiles unchanged. <c>Clock</c> is written
///     bare where it already names <c>Rask.Clock</c>, and qualified where it does not. A conditional call
///     (<c>x?.ToLocalTime()</c>) gets no fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SystemClockCodeFixProvider))]
[Shared]
public sealed class SystemClockCodeFixProvider : RaskCodeFixProvider<ExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK140"];

    protected override string Title => "Read Rask's Clock";

    protected override string EquivalenceKey => "RASK140_UseClock";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, ExpressionSyntax node) =>
        await ReplacementAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false) is not null;

    protected override async Task<Document> FixAsync(Document document, ExpressionSyntax node, CancellationToken cancellationToken) =>
        await ReplacementAsync(document, node, cancellationToken).ConfigureAwait(false) is { } replacement
            ? await ReplaceNodeAsync(document, replacement.Old, replacement.New.WithTriviaFrom(replacement.Old), cancellationToken).ConfigureAwait(false)
            : document;

    private static async Task<(ExpressionSyntax Old, ExpressionSyntax New)?> ReplacementAsync(
        Document document, ExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return null;
        }

        var clock = TypeName(model, node.SpanStart, "Rask", "Clock");
        return model.GetSymbolInfo(node, cancellationToken).Symbol switch
        {
            IPropertySymbol { IsStatic: true } read when SystemTime(read.ContainingType) is { } type => Read(model, node, clock, type, read.Name, cancellationToken),
            IMethodSymbol { Name: "ToLocalTime" } call when SystemTime(call.ContainingType) is { } type &&
                node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access } =>
                (node, Converted(
                    TypeName(model, node.SpanStart, "System", "TimeZoneInfo"), clock, access.Expression.WithoutTrivia(),
                    string.Equals(type, "DateTime", System.StringComparison.Ordinal) ? "ConvertTimeFromUtc" : "ConvertTime")),
            _ => null,
        };
    }

    private static (ExpressionSyntax, ExpressionSyntax)? Read(
        SemanticModel model, ExpressionSyntax node, ExpressionSyntax clock, string type, string name, CancellationToken cancellationToken) =>
        (type, name) switch
        {
            ("DateTime", "Now") => (node, Member(clock, "Wall")),
            ("DateTime", "Today") when TodayAsDay(model, node, cancellationToken) is { } day => (day, Member(clock, "Today")),
            ("DateTime", "Today") => (node, Member(Member(clock, "Wall"), "Date")),
            ("DateTime", "UtcNow") => (node, Member(Member(clock, "Now"), "UtcDateTime")),
            ("DateTimeOffset", "Now" or "UtcNow") => (node, Member(clock, "Now")),
            _ => null,
        };

    // `DateOnly.FromDateTime(DateTime.Today)`: the whole call is `Clock.Today`.
    private static InvocationExpressionSyntax? TodayAsDay(SemanticModel model, ExpressionSyntax node, CancellationToken cancellationToken) =>
        node.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Arguments.Count: 1, Parent: InvocationExpressionSyntax call } } &&
        model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol
        {
            Name: "FromDateTime", ContainingType: { Name: "DateOnly", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } },
        }
            ? call
            : null;

    private static InvocationExpressionSyntax Converted(ExpressionSyntax zones, ExpressionSyntax clock, ExpressionSyntax moment, string conversion) =>
        InvocationExpression(
            Member(zones, conversion),
            ArgumentList(SeparatedList([Argument(moment), Argument(Member(clock, "Zone"))])));

    // `Clock` where the bare name binds to Rask.Clock there; `global::Rask.Clock` where it does not.
    private static ExpressionSyntax TypeName(SemanticModel model, int position, string ns, string name)
    {
        var bound = model.GetSpeculativeSymbolInfo(position, IdentifierName(name), SpeculativeBindingOption.BindAsExpression).Symbol;
        return bound is INamedTypeSymbol { ContainingNamespace: { ContainingNamespace.IsGlobalNamespace: true } named } type &&
               string.Equals(type.Name, name, System.StringComparison.Ordinal) && string.Equals(named.Name, ns, System.StringComparison.Ordinal)
            ? IdentifierName(name)
            : ParseExpression("global::" + ns + "." + name);
    }

    private static MemberAccessExpressionSyntax Member(ExpressionSyntax on, string name) =>
        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, on, IdentifierName(name));

    private static string? SystemTime(INamedTypeSymbol? type) =>
        type is { Name: "DateTime" or "DateTimeOffset", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
            ? type.Name
            : null;
}
