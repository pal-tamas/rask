using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK045 — a component a chain produced is assigned to afterwards.
///     <para>
///         A chain states everything a component was given, in one expression, where the reader of the
///         call site can see it. An assignment after the chain has ended is invisible from there, and
///         nothing reconciles the two: a chain step and a later write to the same property simply
///         disagree, and the write wins.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         Documented in <c>docs/diagnostics.md</c> since the chain landed and never implemented, so it
///         never fired — the docs promised a rule the compiler did not have, which is worse than an
///         undocumented gap: a reader cannot tell it from a codebase with no violations. That is exactly
///         how RASK034 spent its whole life.
///     </para>
///     <para>
///         Only a component a CHAIN produced is held to this. One built any other way — a factory, a
///         field a component assigned itself — is not reported: the surface it came through is what
///         decides, and only a chain promises to be the whole story. (<c>new</c> is RASK014's business.)
///     </para>
///     <para>
///         It has to be an analyzer rather than a property of the type. A chain hands back the component
///         itself, so once the expression has ended the result is an ordinary component with ordinary
///         settable properties, and nothing in the type system is left to forbid the write.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChainAssignedAfterwardsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask045 = new(
        "RASK045",
        "Component built by a chain is assigned to afterwards",
        "'{0}' was built by a chain, so the chain is supposed to say everything it was given — this later write to '{1}' is invisible from the call site, and it wins. Move it into the chain: .{1}(…).",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A chain states everything a component was given, in one expression, where the reader "
                     + "of the call site can see it. An assignment after the chain has ended is invisible "
                     + "from there and nothing reconciles the two -- the chain step and the later write "
                     + "simply disagree, and the write wins. Every property a chain can reach has a step of "
                     + "the same name, so the fix is always to move the assignment into the chain. Suppress "
                     + "it where a component genuinely has to be completed later.",
        helpLinkUri: DiagnosticHelp.Link("RASK045"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask045);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.SimpleAssignment);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        // `c.Note = "b"` — a property on something held in a local. A write to a property of `this`, or
        // of a field a component owns, is a component managing its own state and none of this rule's
        // business.
        //
        // The instance is unwrapped because a cast is still the same local: `((Card)c).Note = "b"` is
        // the shape a chain closed by its children indexer produces, since that hands back Component.
        if (assignment.Target is not IPropertyReferenceOperation { Property: { } property } target
            || UnwrapCasts(target.Instance) is not ILocalReferenceOperation local)
        {
            return;
        }

        if (!ProducedByChain(local, context.Operation.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask045, assignment.Syntax.GetLocation(), local.Local.Name, property.Name));
    }

    // Whether the local was initialised by a chain. Read off the declaration rather than tracked through
    // the method: a local reassigned from something else later is a shape this deliberately does not
    // chase, because reporting it would need flow analysis to be right and a wrong answer here is a
    // warning on correct code.
    private static bool ProducedByChain(ILocalReferenceOperation local, SemanticModel? model)
    {
        if (model is null)
        {
            return false;
        }

        var declarator = local.Local.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();

        if (declarator?.Initializer?.Value is not { } initializer)
        {
            return false;
        }

        return IsChainExpression(model.GetOperation(initializer));
    }

    private static bool IsChainExpression(IOperation? operation)
    {
        operation = Unwrap(operation);

        return operation switch
        {
            // A step: `Card.Note("a")`. Every one is an extension method on the generated setters class.
            IInvocationOperation invocation =>
                BuilderEntry.IsChainStep(invocation.TargetMethod)
                || IsChainExpression(Receiver(invocation)),

            // The children indexer closes a chain — `Card.Note("a")[…]` — and a qualified entry names one
            // outright: `RaskEntriesRask_Core.Div`.
            IPropertyReferenceOperation reference =>
                IsEntry(reference.Property) || IsChainExpression(reference.Instance),

            _ => false,
        };
    }

    // The generated entry class, spelled the way ComponentFactoryGenerator spells it ("RaskEntries" plus
    // the assembly's identifier-safe name). Keyed on the generated surface rather than on the property's
    // type, so an ordinary method or property that happens to return a component is not mistaken for one.
    //
    // This catches the QUALIFIED entry only. An unqualified `Card` inside a markup host binds to a
    // per-host forwarder, which carries nothing at the symbol level to tell it from a hand-written
    // property returning a component — and guessing there would put a warning on correct code, which is
    // worse than the miss. So `var c = Card; c.Note = "b";` is not reported while
    // `var c = Card.Note("a"); c.Note = "b";` is: a chain that named a step is the shape the rule is
    // actually about, because that is where the two answers disagree.
    private static bool IsEntry(ISymbol property) =>
        property.ContainingType?.Name.StartsWith("RaskEntries", System.StringComparison.Ordinal) == true;

    // As Unwrap, but an EXPLICIT cast counts too: a chain closed by its children indexer hands back
    // Component, so reaching a property on it needs one, and ((Card)c).Note = "b" is still a write to
    // the same local.
    private static IOperation? UnwrapCasts(IOperation? operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation)
        {
            operation = operation is IParenthesizedOperation p
                ? p.Operand
                : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }

    private static IOperation? Receiver(IInvocationOperation invocation) =>
        invocation.Instance
        ?? (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length != 0
            ? invocation.Arguments[0].Value
            : null);

    // Parentheses, an implicit conversion and a null-forgiving `!` are all part of the same expression.
    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            operation = operation is IParenthesizedOperation p
                ? p.Operand
                : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }
}
