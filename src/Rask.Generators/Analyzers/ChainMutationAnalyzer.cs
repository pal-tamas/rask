using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK045 — a component built by a chain is written to afterwards.
///     <para>
///         A chain is meant to be the ONE way a component's properties are set, so that what a component
///         was given is the sequence of steps at its call site and nothing else. A later assignment is a
///         second source of truth the reader of that call site cannot see, and the two can disagree —
///         which is exactly the shape the chain surface exists to remove.
///     </para>
///     <para>
///         It has to be an analyzer, because nothing in the type system can forbid it. <c>Build&lt;T&gt;</c>
///         converts implicitly to the component it built — that conversion is what keeps the chain out of
///         the way at every call site that wants the component itself — and once it has converted, the
///         result is an ordinary component with ordinary settable properties.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChainMutationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask045 = new(
        "RASK045",
        "Component built by a chain is assigned to afterwards",
        "'{0}' was built by a chain, so setting '{1}' on it afterwards is a second source of truth — move it into the chain as '.{1}(…)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A chain states everything a component was given, in one expression, where the reader of the "
                     + "call site can see it. An assignment after the chain has ended is invisible from there, and "
                     + "nothing reconciles the two: a chain step and a later write to the same property simply "
                     + "disagree, and the write wins. Every property a chain can reach has a step, so the "
                     + "assignment always has a place to go.",
        helpLinkUri: DiagnosticHelp.Link("RASK045"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask045);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentFullName);
            if (component is null)
            {
                return;
            }

            start.RegisterOperationAction(ctx => Analyze(ctx, component), OperationKind.SimpleAssignment);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation
            {
                Instance: { } instance, Property: { } property,
            })
        {
            return;
        }

        if (!BuilderEntry.DerivesFromComponent(instance.Type, component))
        {
            return;
        }

        // Only when the instance is provably a chain's product. A local holding a factory-built component,
        // or a field the component assigned itself, is not this rule's business — the surface it was built
        // through is what decides, and only a chain promises to be the whole story.
        if (!CameFromAChain(instance))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask045, assignment.Syntax.GetLocation(), instance.Syntax.ToString(), property.Name));
    }

    // Whether this instance traces back to a chain. Two shapes reach here: the expression IS the chain
    // (`((Div)Div.Class("a")).Id = …` — the cast's operand is the `Build<Div>`), or it is a local whose
    // initializer was one.
    private static bool CameFromAChain(IOperation instance)
    {
        var unwrapped = Unwrap(instance);

        if (ProducesChain(unwrapped))
        {
            return true;
        }

        // A local declared from a chain: `Card c = Card.Note("a"); c.Note = "b";`. The declaration is the
        // only place a chain could have produced it, so a local reassigned later is deliberately NOT
        // followed — a rule that guesses across assignments would report the wrong line.
        if (unwrapped is not ILocalReferenceOperation { Local: { } local })
        {
            return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } initializer }
                && instance.SemanticModel?.GetOperation(initializer) is { } init
                && ProducesChain(init))
            {
                return true;
            }
        }

        return false;
    }

    // Whether this expression is, or converts from, a chain.
    //
    // The test is the TYPE — `Build<T>` — because that is what a chain is; the conversion to the
    // component is the last thing that happens to it. Both spellings reach here: the conversion node
    // itself (a written cast, `((Card)Card.Note("a")).Note = …`) and the bare chain the semantic model
    // hands back for a declaration's initializer, where the implicit conversion sits on the declarator.
    //
    // NOT "an invocation that returned a component", which was the first rule tried and is wrong: a
    // generated FACTORY call returns a component too, so `var c = Generated.Input<string>();
    // c.Validate = rule;` was reported — and a factory-built component is exactly what this must leave
    // alone. Caught by the repo's own build, on a test that does precisely that.
    private static bool ProducesChain(IOperation operation) =>
        IsChain(operation.Type)
        || (operation is IConversionOperation conversion && IsChain(Unwrap(conversion.Operand).Type));

    private static bool IsChain(ITypeSymbol? type) =>
        type is not null && !SymbolEqualityComparer.Default.Equals(BuilderEntry.ChainedComponent(type), type);

    // Parentheses and a null-forgiving `!` are part of the same expression. A CONVERSION is unwrapped one
    // step at a time by the caller instead, because the operand's type is the thing being asked about.
    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IParenthesizedOperation p)
        {
            operation = p.Operand;
        }

        return operation;
    }
}
