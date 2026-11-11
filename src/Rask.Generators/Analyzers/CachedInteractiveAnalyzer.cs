using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK117 — an event handler, a binding or an element reference written inside <c>Cached[…]</c>.
///     <para>
///         A cached fragment stores the markup it drew and hands it to later visitors as plain HTML, where nothing
///         would answer a click or fill a bound field. The runtime refuses to store such markup, naming it; this
///         reports the cases written straight into the fragment's children at compile time, where the fix is
///         cheapest. A handler inside a component the fragment wraps is out of its sight and left to the runtime.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CachedInteractiveAnalyzer : DiagnosticAnalyzer
{
    private const string CachedFullName = "Rask.Core.Components.Cached";
    private const string ComponentFullName = "Rask.Core.Component";

    private static readonly DiagnosticDescriptor Rask117 = new(
        "RASK117",
        "A cached fragment cannot hold anything interactive",
        "'{0}' inside Cached[ … ] would be served to later visitors as static HTML, where nothing answers it; move it outside the cached fragment",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Cached stores the markup it drew and serves it to every later request as plain HTML. An event "
                     + "handler, a binding or an element reference in it would reach the next visitor dead. Keep only "
                     + "static markup inside the fragment and put the interactive part next to it.",
        helpLinkUri: DiagnosticHelp.Link("RASK117"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask117);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var cachedType = start.Compilation.GetTypeByMetadataName(CachedFullName);
            var componentType = start.Compilation.GetTypeByMetadataName(ComponentFullName);
            if (cachedType is null || componentType is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, cachedType, componentType),
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol cachedType, INamedTypeSymbol componentType)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;
        if (!IsCached(context.SemanticModel, node, cachedType, context))
        {
            return;
        }

        foreach (var invocation in node.ArgumentList.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access
                || !IsInteractiveStep(access.Name.Identifier.ValueText)
                || NearestCached(invocation, node, context.SemanticModel, cachedType, context) != node
                || !IsChainStep(context.SemanticModel, invocation, componentType, context))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rask117, access.Name.GetLocation(), access.Name.Identifier.ValueText));
        }
    }

    // OnClick, OnInput, OnSubmit… (an event step), Bind (a two-way binding) and Ref (an element reference).
    private static bool IsInteractiveStep(string name) =>
        string.Equals(name, "Bind", StringComparison.Ordinal)
        || string.Equals(name, "Ref", StringComparison.Ordinal)
        || (name.Length > 2 && name[0] == 'O' && name[1] == 'n' && char.IsUpper(name[2]));

    private static bool IsCached(SemanticModel model, ElementAccessExpressionSyntax node, INamedTypeSymbol cachedType, SyntaxNodeAnalysisContext context) =>
        SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(node.Expression, context.CancellationToken).Type, cachedType);

    // The innermost Cached[…] around the invocation, so a nested fragment's children are reported once, by it.
    private static SyntaxNode? NearestCached(SyntaxNode invocation, SyntaxNode outer, SemanticModel model, INamedTypeSymbol cachedType, SyntaxNodeAnalysisContext context)
    {
        for (var at = invocation.Parent; at is not null; at = at.Parent)
        {
            if (at is ElementAccessExpressionSyntax access && (at == outer || IsCached(model, access, cachedType, context)))
            {
                return at;
            }
        }

        return null;
    }

    // A step of a markup chain — on a component, or on the generated seed or stage a chain passes through —
    // never a method of the same name on anything else (a LINQ OnNext, a domain object's Bind).
    private static bool IsChainStep(SemanticModel model, InvocationExpressionSyntax invocation, INamedTypeSymbol componentType, SyntaxNodeAnalysisContext context)
    {
        if (model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return IsMarkup(method.ReturnType, componentType) || IsMarkup(method.ReceiverType, componentType);
    }

    private static bool IsMarkup(ITypeSymbol? type, INamedTypeSymbol componentType)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Name.StartsWith("RaskSeed_", StringComparison.Ordinal)
            || type.Name.StartsWith("RaskStage_", StringComparison.Ordinal)
            || type.Name.StartsWith("RaskPending_", StringComparison.Ordinal))
        {
            return true;
        }

        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, componentType))
            {
                return true;
            }
        }

        return false;
    }
}
