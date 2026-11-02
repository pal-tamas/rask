using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators.Analyzers;

// RASK022 — a Rask factory call (an HTML element or a custom component) produced in a sibling-list
// context without a Key. Two idioms are flagged:
//   * a `.Select(...)`/`.SelectMany(...)` projection whose body becomes a sibling component, and
//   * a component added to a component collection (`List<Component>.Add(...)`) inside a loop.
// Keyless list items reconcile by POSITION: an insert/remove/reorder falls back to a full-HTML
// morph and loses DOM identity (focus, input state) on surviving nodes. A stable `Key` lets
// the diff codec match by identity and ship trusted keyed structural ops instead.
//
// A COMPONENT row (a type that renders itself — it overrides Render, so it has its own state and its own
// handlers) is an ERROR (#1243): the parent keeps its instances by position, so an event already on its
// way to the instance in place N runs on whichever record sits in place N now — a delete reaches the
// wrong row, and the stale-event check cannot see it because nothing about the handler moved. A plain
// element row has no instance of its own to misdirect, so it stays a warning. Suppressible either way.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingKeyAnalyzer : DiagnosticAnalyzer
{
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";

    private static readonly DiagnosticDescriptor Rask022 = new(
        "RASK022",
        "List item is missing a Key",
        "'{0}' is rendered in a list without a Key — add '.Key(row.Id)' (a stable id of the item, never its "
        + "index) so the parent keeps each row's instance, state and events with its record rather than with "
        + "its position, and the diff codec reconciles it by identity",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "A component produced in a .Select(...) projection or added to a component collection in a loop "
        + "must carry a stable Key (Blazor @key parity). Without it the parent reuses its instances by "
        + "position, so an event already sent to the row in place N runs on whichever record sits there "
        + "now — a delete reaches the wrong row. A plain element row is reported as a warning: it has no "
        + "instance of its own, but insert/remove/reorder still falls back to a positional full-HTML morph "
        + "and loses DOM identity (focus, input state) on surviving nodes.",
        DiagnosticHelp.Link("RASK022"));

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
            if (component is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component),
                SyntaxKind.InvocationExpression,
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var node = (ExpressionSyntax)context.Node;
        var model = context.SemanticModel;
        if (!TryReadUnkeyedConstruction(
                node, model, component, context.CancellationToken, out var name, out var location, out var built))
        {
            return;
        }

        // 3) The produced "child expression": the call plus any `[...]` children indexer, paren,
        //    and `(Component)` cast wrapping it. Its (converted) type tells us it becomes a sibling
        //    component.
        var outer = ClimbToChildExpression(node);
        var typeInfo = model.GetTypeInfo(outer, context.CancellationToken);
        var isChildLike =
            InheritsFrom(typeInfo.Type as INamedTypeSymbol, component)
            || InheritsFrom(typeInfo.ConvertedType as INamedTypeSymbol, component);
        if (!isChildLike)
        {
            return;
        }

        // 4) Sibling-LIST context: a Select/SelectMany projection, or an Add to a component
        //    collection inside a loop. A single static child (e.g. Div()[ Span() ]) is not flagged.
        if (!IsInSelectProjection(outer)
            && !IsCollectionAddInLoop(outer, model, component, context.CancellationToken))
        {
            return;
        }

        // A row that renders itself keeps its instance by position, which is what misdirects an event; a
        // plain element does not, so it is told apart here rather than by a second id.
        var severity = RendersItself(built ?? RowType(node, model, component, context.CancellationToken), component)
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;
        context.ReportDiagnostic(Diagnostic.Create(Rask022, location, severity, null, null, name));
    }

    // The row's own type when the construction did not name it (a seed-opened chain): the chain's type, read
    // beneath any children indexer, which hands back `Component` rather than the row.
    private static ITypeSymbol? RowType(ExpressionSyntax node, SemanticModel model, INamedTypeSymbol component,
        CancellationToken cancellationToken)
    {
        for (var current = node; ; )
        {
            var type = model.GetTypeInfo(current, cancellationToken).Type;
            if (type is not null && !SymbolEqualityComparer.Default.Equals(type, component))
            {
                return type;
            }

            switch (current)
            {
                case ElementAccessExpressionSyntax indexer:
                    current = indexer.Expression;
                    continue;
                case ParenthesizedExpressionSyntax paren:
                    current = paren.Expression;
                    continue;
                default:
                    return null;
            }
        }
    }

    // Whether the type draws its own subtree — some type between it and Component overrides Render — which is
    // what gives it state and handlers of its own. A generated tag never does: it renders itself as its tag.
    private static bool RendersItself(ITypeSymbol? type, INamedTypeSymbol component)
    {
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, component))
            {
                return false;
            }

            if (t.GetMembers("Render").Any(m => m is IMethodSymbol { IsOverride: true, Parameters.Length: 0 }))
            {
                return true;
            }
        }

        return false;
    }

    // 1) Is this a Rask construction that COULD carry a key, and 2) does it still lack one? Two spellings.
    //
    //    The factory is a static method on a class named `Generated` (namespace varies per
    //    component) returning a Component-derived type.
    //
    //    A CHAIN is not a method call at all, which is why this analyzer used to miss it entirely:
    //    `Li[…]` is a property reference plus a children indexer, and `Li.Class("c")[…]` puts
    //    extension setters in between. The outermost invocation's symbol is the last SETTER, never
    //    the component — so matching on the method name found nothing and the keyless-list check
    //    silently stopped firing on the only spelling the docs teach (#704).
    //
    //    The distinction still matters: an expression merely TYPED as a component is not enough. A
    //    static helper returning markup (`Ui.Badge(x)`) yields a Component and cannot take a key, so
    //    flagging it would be noise. Only a factory call or a chain can be keyed.
    private static bool TryReadUnkeyedConstruction(
        ExpressionSyntax node,
        SemanticModel model,
        INamedTypeSymbol component,
        CancellationToken cancellationToken,
        out string name,
        out Location location,
        out ITypeSymbol? built)
    {
        name = string.Empty;
        location = Location.None;
        built = null;
        if (node is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            && method.IsStatic
            && string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
            && InheritsFrom(method.ReturnType as INamedTypeSymbol, component))
        {
            // Already keyed — a Key: argument, or a Data argument carrying rask-key (back-compat).
            name = method.Name;
            location = NameLocation(invocation);
            built = method.ReturnType;
            return !HasKeyArgument(invocation);
        }

        if (BuilderEntry.TryReadChain(node, model, cancellationToken, out var entry, out built, out var steps))
        {
            // Key names the identity; a Data step carrying rask-key is the VirtualizePage-style equivalent.
            name = entry.Identifier.ValueText;
            location = entry.GetLocation();
            return !(steps.Contains("Key") || steps.Contains("Data") && node.ToString().Contains("rask-key"));
        }

        return false;
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

    // Climb past the children indexer (`[...]`), parens, and `(Component)` casts to reach the
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
        var lambda = outer.Parent switch
        {
            LambdaExpressionSyntax direct => direct,
            ReturnStatementSyntax ret => EnclosingLambda(ret),
            _ => null
        };

        return lambda?.Parent is ArgumentSyntax
        {
            Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv }
        }
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
        INamedTypeSymbol component, CancellationToken ct)
    {
        // outer must be the single argument to a `.Add(<Component>)` call ...
        if (outer.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } }
            || inv.Expression is not MemberAccessExpressionSyntax ma
            || !string.Equals(ma.Name.Identifier.ValueText, "Add", StringComparison.Ordinal)
            || model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol add
            || add.Parameters.Length != 1
            || !InheritsFrom(add.Parameters[0].Type as INamedTypeSymbol, component))
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
