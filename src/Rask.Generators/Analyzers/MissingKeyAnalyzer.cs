using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK022 — warn when a Rask factory call (an HTML element or a custom component) is produced
// in a sibling-list context without a Key. Two idioms are flagged:
//   * a `.Select(...)`/`.SelectMany(...)` projection whose body becomes a Child, and
//   * an element/component added to a Child collection (`List<Child>.Add(...)`) inside a loop.
// Keyless list items reconcile by POSITION: an insert/remove/reorder falls back to a full-HTML
// morph and loses DOM identity (focus, input state) on surviving nodes. A stable `Key:` lets
// the diff codec match by identity and ship trusted keyed structural ops instead. Best-effort:
// the heuristics aim for the common idioms; the warning is suppressible.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingKeyAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string ChildFullName = "Rask.Core.Child";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";

    private static readonly DiagnosticDescriptor Rask022 = new(
        "RASK022",
        "List item is missing a Key",
        "'{0}' is rendered in a list without a Key — set Key: so the diff codec reconciles it by "
        + "identity (trusted keyed structural ops) instead of by position",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Elements/components produced in a .Select(...) projection or added to a Child collection in "
        + "a loop should carry a stable Key (Blazor @key parity). Without it, insert/remove/reorder "
        + "falls back to a positional full-HTML morph and loses DOM identity (focus, input state) on "
        + "surviving nodes.",
        helpLinkUri: DiagnosticHelp.Link("RASK022"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask022);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var component = start.Compilation.GetTypeByMetadataName(ComponentFullName);
            var child = start.Compilation.GetTypeByMetadataName(ChildFullName);
            if (component is null || child is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component, child),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol component,
        INamedTypeSymbol child)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model = context.SemanticModel;

        // 1) Is this a Rask factory call? Generated factories are static methods on a class named
        //    `Generated` (namespace varies per component) returning a Component-derived type.
        if (model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !method.IsStatic
            || !string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
            || !InheritsFrom(method.ReturnType as INamedTypeSymbol, component))
        {
            return;
        }

        // 2) Already keyed — a Key: argument, or a Data argument carrying rask-key (back-compat).
        if (HasKeyArgument(invocation))
        {
            return;
        }

        // 3) The produced "child expression": the call plus any `[...]` children indexer, paren,
        //    and `(Child)` cast wrapping it. Its converted type tells us it becomes a sibling Child.
        var outer = ClimbToChildExpression(invocation);
        var typeInfo = model.GetTypeInfo(outer, context.CancellationToken);
        var isChildLike =
            IsChild(typeInfo.Type, child) || IsChild(typeInfo.ConvertedType, child)
            || InheritsFrom(typeInfo.Type as INamedTypeSymbol, component)
            || InheritsFrom(typeInfo.ConvertedType as INamedTypeSymbol, component);
        if (!isChildLike)
        {
            return;
        }

        // 4) Sibling-LIST context: a Select/SelectMany projection, or an Add to a Child collection
        //    inside a loop. A single static child (e.g. Div()[ Span() ]) is not flagged.
        if (!IsInSelectProjection(outer)
            && !IsCollectionAddInLoop(outer, model, child, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask022, NameLocation(invocation), method.Name));
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol target)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsChild(ITypeSymbol? type, INamedTypeSymbol child) =>
        type is not null && SymbolEqualityComparer.Default.Equals(type, child);

    private static bool HasKeyArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (string.Equals(arg.NameColon?.Name.Identifier.ValueText, "Key", StringComparison.Ordinal))
            {
                return true;
            }

            // A Data: argument that mentions rask-key keeps VirtualizePage-style keying quiet.
            if (arg.ToString().IndexOf("rask-key", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // Climb past the children indexer (`[...]`), parens, and `(Child)` casts to reach the
    // expression that actually flows into the surrounding list/projection context.
    private static ExpressionSyntax ClimbToChildExpression(ExpressionSyntax node)
    {
        while (true)
        {
            switch (node.Parent)
            {
                case ElementAccessExpressionSyntax eae when eae.Expression == node:
                    node = eae;
                    continue;
                case ParenthesizedExpressionSyntax pe:
                    node = pe;
                    continue;
                case CastExpressionSyntax ce when ce.Expression == node:
                    node = ce;
                    continue;
                default:
                    return node;
            }
        }
    }

    private static bool IsInSelectProjection(ExpressionSyntax outer)
    {
        // `outer` must be the DIRECTLY projected value — the lambda's expression body or a
        // returned expression — not a nested descendant element (e.g. a Code inside a projected
        // Li). Only the top-level projected item is the reconciled sibling that needs a Key.
        LambdaExpressionSyntax? lambda = outer.Parent switch
        {
            LambdaExpressionSyntax direct => direct,
            ReturnStatementSyntax ret => EnclosingLambda(ret),
            _ => null
        };

        return lambda?.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } }
               && IsLinqProjection(inv);
    }

    private static LambdaExpressionSyntax? EnclosingLambda(SyntaxNode node)
    {
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            switch (n)
            {
                case LambdaExpressionSyntax lambda:
                    return lambda;
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AccessorDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }

    private static bool IsLinqProjection(InvocationExpressionSyntax inv)
    {
        var name = inv.Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.ValueText : null;
        return string.Equals(name, "Select", StringComparison.Ordinal)
               || string.Equals(name, "SelectMany", StringComparison.Ordinal);
    }

    private static bool IsCollectionAddInLoop(SyntaxNode outer, SemanticModel model,
        INamedTypeSymbol child, CancellationToken ct)
    {
        // outer must be the single argument to a `.Add(<Child>)` call ...
        if (outer.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } }
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || !string.Equals(ma.Name.Identifier.ValueText, "Add", StringComparison.Ordinal)
            || model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol add
            || add.Parameters.Length != 1
            || !IsChild(add.Parameters[0].Type, child))
        {
            return false;
        }

        // ... lexically inside a loop (the foreach/for/while list-building idiom).
        return outer.Ancestors().Any(a =>
            a is ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax);
    }

    private static Location NameLocation(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => inv.Expression.GetLocation()
    };
}
