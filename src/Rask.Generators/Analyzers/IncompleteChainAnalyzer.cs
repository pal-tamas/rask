using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

// RASK095 — a chain used before its required steps are taken.
//
// A required property is a step the component does not exist until you take: the entry hands back a
// seed struct, each required step a state struct still owing the rest, and only the last one hands back
// the component. That makes a skipped step a compile error, but the compiler's own words for it are
// about generated types nobody wrote — "'RaskSeed_Card' does not contain a definition for 'Note'",
// "cannot convert 'RaskPending_Card_Title' to 'Component'" — and a half-built chain used as a CHILD is
// not an error at all: the `params object?[]` children indexer takes it and it throws while rendering.
// This says what is missing, in the chain's own words, at the same span.
//
// A state is recognised by the name the generator gives it (RaskSeed_/RaskPending_), as BuilderEntry
// recognises a seed, and what it still owes is read off it: its steps ARE the outstanding required
// properties, in declaration order.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IncompleteChainAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The missing steps for the quick-fix, each with the placeholder it can take (<c>Title:"";Body:default!</c>).</summary>
    public const string StepsKey = "Steps";

    private const string SeedPrefix = "RaskSeed_";

    private const string PendingPrefix = "RaskPending_";

    private static readonly DiagnosticDescriptor Rask095 = new(
        "RASK095",
        "Chain skips a required step",
        "'{0}' needs {1}{2} — write {3}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "A non-nullable property with no initializer is a required step (RASK001): the chain is not the "
        + "component until every one is taken. Take the missing steps first — they can come in any order.",
        DiagnosticHelp.Link("RASK095"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask095);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // What a state owes is a property of its TYPE, and an app references the same few states at
            // thousands of call sites — an element seed alone has hundreds of steps. Worked out once per type
            // per compilation, not once per reference: measured on the site, the uncached walk turned a
            // one-minute compile into one that did not finish.
            var owed = new ConcurrentDictionary<INamedTypeSymbol, List<IMethodSymbol>?>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(
                ctx => Analyze(ctx, owed),
                OperationKind.PropertyReference,
                OperationKind.Invocation,
                OperationKind.LocalReference,
                OperationKind.FieldReference,
                OperationKind.ParameterReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context, ConcurrentDictionary<INamedTypeSymbol, List<IMethodSymbol>?> owed)
    {
        var operation = context.Operation;
        if (!IsChainState(operation.Type) || operation.Syntax is not ExpressionSyntax expression
            || operation.Parent is INameOfOperation)
        {
            return;
        }

        var state = (INamedTypeSymbol)operation.Type!;
        if (owed.GetOrAdd(state, Outstanding) is not { Count: > 0 } missing)
        {
            return;
        }

        var outer = expression;
        while (outer.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            outer = parenthesized;
        }

        string? next = null;
        if (outer.Parent is MemberAccessExpressionSyntax access && access.Expression == outer)
        {
            // A step of this very state — `Card.Title(…)` — is how a chain moves on, not a mistake.
            // Read off the invocation the operation tree already bound; asking the semantic model again binds
            // the member access a second time, at every step of every chain in the compilation.
            var step = operation.Parent is IInvocationOperation invocation && invocation.Instance == operation
                ? invocation.TargetMethod
                : operation.SemanticModel?.GetSymbolInfo(access, context.CancellationToken).Symbol as IMethodSymbol;
            if (step is not null
                && SymbolEqualityComparer.Default.Equals(step.ContainingType?.OriginalDefinition, state.OriginalDefinition))
            {
                return;
            }

            next = access.Name.Identifier.ValueText;
        }
        else if (!LeavesAsSomethingElse(operation, outer, state, context))
        {
            // Stored as itself (`var card = Card;`) — the rest of the chain can still follow.
            return;
        }

        var names = missing.Select(static m => m.Name).ToList();
        var written = new StringBuilder(Spell(outer));
        foreach (var name in names)
        {
            written.Append('.').Append(name).Append("(…)");
        }

        if (next is not null)
        {
            written.Append('.').Append(next).Append("(…)");
        }

        var properties = ImmutableDictionary<string, string?>.Empty.Add(StepsKey, FixSteps(missing));

        context.ReportDiagnostic(Diagnostic.Create(
            Rask095, outer.GetLocation(), properties,
            Subject(outer, state, operation.SemanticModel, context), Join(names),
            next is null ? string.Empty : " before anything else", written.ToString()));
    }

    // Whether the half-built chain is handed on as some OTHER type here — a child (boxed to object), a
    // return value, an argument — which is where the compiler either refuses it or, for a child, lets it
    // through to throw at render.
    private static bool LeavesAsSomethingElse(
        IOperation operation, ExpressionSyntax outer, INamedTypeSymbol state, OperationAnalysisContext context)
    {
        if (operation.Parent is IConversionOperation conversion)
        {
            return !SymbolEqualityComparer.Default.Equals(conversion.Type, state);
        }

        if (operation.Parent is IInvalidOperation or IArgumentOperation { Parameter: null })
        {
            return true;
        }

        var converted = operation.SemanticModel?.GetTypeInfo(outer, context.CancellationToken).ConvertedType;
        return converted is not null && converted.TypeKind != TypeKind.Error
               && !SymbolEqualityComparer.Default.Equals(converted, state);
    }

    private static bool IsChainState(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsValueType: true } named
        && (named.Name.StartsWith(SeedPrefix, StringComparison.Ordinal)
            || named.Name.StartsWith(PendingPrefix, StringComparison.Ordinal));

    // The steps a state offers, one per name, in declaration order: `Key` (which keeps the state) and a seed's
    // `Of<T>()` (a type opening, never a property) aside.
    private static List<IMethodSymbol> Steps(INamedTypeSymbol state)
    {
        var steps = new List<IMethodSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in state.GetMembers())
        {
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary, IsStatic: false, DeclaredAccessibility: Accessibility.Public,
                } method
                && method.Name is not ("Key" or "Of")
                && seen.Add(method.Name))
            {
                steps.Add(method);
            }
        }

        return steps;
    }

    // What the chain still owes, or null when it cannot be said as a list of steps that ALL have to be taken.
    // A pending state's steps are exactly that. A seed's are its openings, which is the same thing for a
    // component whose chain opens on its required properties (each opening leads to a state owing the
    // others) but not for a form control, whose openings — `Bind` or `Value` — are alternatives.
    private static List<IMethodSymbol>? Outstanding(INamedTypeSymbol state)
    {
        var steps = Steps(state);
        if (state.Name.StartsWith(PendingPrefix, StringComparison.Ordinal))
        {
            return steps;
        }

        var all = new HashSet<string>(steps.Select(static s => s.Name), StringComparer.Ordinal);
        foreach (var step in steps)
        {
            var after = new HashSet<string>(StringComparer.Ordinal) { step.Name };
            if (step.ReturnType is INamedTypeSymbol next && IsChainState(next))
            {
                if (!next.Name.StartsWith(PendingPrefix, StringComparison.Ordinal))
                {
                    return null;
                }

                after.UnionWith(Steps(next).Select(static s => s.Name));
            }

            if (!after.SetEquals(all))
            {
                return null;
            }
        }

        return steps;
    }

    // `Title:"";Body:default!` — each missing step with a placeholder that compiles, or with none where no
    // single spelling exists (an overloaded or generic step, where `default!` would be ambiguous). The fix
    // needs a placeholder only for a step the chain does not name further along.
    private static string FixSteps(List<IMethodSymbol> missing) =>
        string.Join(";", missing.Select(static step => step.Name + ":" + Placeholder(step)));

    private static string Placeholder(IMethodSymbol step)
    {
        var overloads = step.ContainingType.GetMembers(step.Name).OfType<IMethodSymbol>().Count();
        if (step.IsGenericMethod || step.Parameters.Length != 1 || overloads != 1)
        {
            return string.Empty;
        }

        var type = step.Parameters[0].Type;
        return type.SpecialType == SpecialType.System_String ? "\"\""
            : type.IsValueType ? "default"
            : "default!";
    }

    // The chain as written, each step's arguments elided: `Card.Title(…)`.
    private static string Spell(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Spell(parenthesized.Expression),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } =>
                Spell(member.Expression) + "." + member.Name.Identifier.ValueText + "(…)",
            _ => expression.WithoutTrivia().ToString(),
        };

    // What the chain builds, as its entry spells it (`Card`, `Ui.Stat`); for a chain held in a local, the
    // component it builds.
    private static string Subject(
        ExpressionSyntax outer, INamedTypeSymbol state, SemanticModel? model, OperationAnalysisContext context)
    {
        var root = outer;
        while (true)
        {
            switch (root)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    root = parenthesized.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }:
                    root = member.Expression;
                    continue;
            }

            break;
        }

        if (model?.GetSymbolInfo(root, context.CancellationToken).Symbol is IPropertySymbol { IsStatic: true })
        {
            return root.WithoutTrivia().ToString();
        }

        return ComponentOf(state)?.Name ?? root.WithoutTrivia().ToString();
    }

    // A pending state holds the component it is building; a seed leads to one through its steps.
    private static ITypeSymbol? ComponentOf(INamedTypeSymbol state)
    {
        var current = state;
        for (var depth = 0; depth < 16; depth++)
        {
            if (current.GetMembers("Component").OfType<IPropertySymbol>().FirstOrDefault() is { } held)
            {
                return held.Type;
            }

            var step = Steps(current).FirstOrDefault();
            if (step?.ReturnType is not INamedTypeSymbol next)
            {
                return null;
            }

            if (!IsChainState(next))
            {
                return next;
            }

            current = next;
        }

        return null;
    }

    private static string Join(List<string> names)
    {
        var quoted = names.Select(static n => "'" + n + "'").ToList();
        return quoted.Count == 1
            ? quoted[0]
            : string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[quoted.Count - 1];
    }
}
