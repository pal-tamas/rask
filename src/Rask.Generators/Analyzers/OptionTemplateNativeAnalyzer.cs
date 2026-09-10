using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK075 — an option template on a select that was also told to use the platform's own control.
///     <para>
///         An <c>&lt;option&gt;</c>'s content model is text. There is nowhere inside the platform's
///         control for a template's markup to go, so the template is simply not called — the list
///         renders its plain words and nothing reports it. That is the quiet kind of failure: the build
///         is green, the control works, and the icons someone wrote are missing with no clue why.
///     </para>
///     <para>
///         Leaving <c>Native</c> unset is the fix in nearly every case: a template implies the drawn
///         list, so the control already draws it. This fires only on an EXPLICIT <c>Native(true)</c>,
///         which is a statement that contradicts the template rather than an omission.
///     </para>
/// </summary>
/// <remarks>
///     Deliberately walks the chain's invocation operations rather than going through
///     <see cref="BuilderEntry.TryReadChain" />. That helper requires the entry to be a
///     <c>Build&lt;T&gt;</c> property, and a form control's entry is its mode-opening seed
///     (<c>RaskSeed_UiSelect</c>) instead — so reading the chain that way finds nothing here and the
///     diagnostic would be silently dead, which is how RASK022/023/025/038/044 each spent a while.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptionTemplateNativeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask075 = new(
        "RASK075",
        "Option template on a native select",
        "This chain sets '{0}' and also Native(true). An <option> holds text only, so the template never renders — drop Native(true) to draw the list, or drop the template.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A template hands the control markup to draw per option, and the platform's <select> has "
                     + "nowhere to put it: its options are text nodes. The template is therefore never called, "
                     + "the list renders its plain words, and the build stays green — so the mistake is only "
                     + "found by looking at the running page and wondering where the icons went. Setting a "
                     + "template with Native unset already chooses the drawn list; this reports only the "
                     + "explicit Native(true) that contradicts it.",
        helpLinkUri: DiagnosticHelp.Link("RASK075"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask075);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var operation = (IInvocationOperation)context.Operation;

        // Only the OUTERMOST call of a chain, so one chain is reported once rather than once per link.
        if (operation.Syntax.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return;
        }

        // A step hands back the component itself, so the return type IS what is being built — this used
        // to have to unwrap a `Build<T>` first.
        if (!IsSelect(operation.TargetMethod.ReturnType))
        {
            return;
        }

        string? template = null;
        IInvocationOperation? native = null;

        for (var current = operation; current is not null; current = Next(current))
        {
            switch (current.TargetMethod.Name)
            {
                case "OptionTemplate":
                case "ChipTemplate":
                    template ??= current.TargetMethod.Name;
                    break;
                case "Native" when IsTrue(current):
                    native ??= current;
                    break;
            }
        }

        if (template is null || native is null)
        {
            return;
        }

        // Reported on the Native(true) call rather than the whole chain: that is the shorter span, and
        // it is the one of the two the author is far more likely to have added without thinking.
        context.ReportDiagnostic(Diagnostic.Create(Rask075, native.Syntax.GetLocation(), template));
    }

    // Only the kit's two selects. A component of somebody else's with a property spelled the same way is
    // none of this analyzer's business, and a name match alone would make it so.
    private static bool IsSelect(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.ConstructedFrom.ToDisplayString() is "Rask.Ui.UiSelect<T>" or "Rask.Ui.UiMultiSelect<T>";

    // A literal `true`, and nothing else. Native(false) is agreement, Native(null) is silence, and
    // Native(someFlag) is not knowable here — reporting any of them would be a guess.
    private static bool IsTrue(IInvocationOperation operation)
    {
        if (operation.Arguments.Length == 0)
        {
            return false;
        }

        var value = Unwrap(operation.Arguments[operation.Arguments.Length - 1].Value);
        return value?.ConstantValue is { HasValue: true, Value: true };
    }

    // The next link DOWN the chain: a setter's receiver is the invocation that produced it, whether it
    // was written as an extension (argument 0) or as an instance call.
    private static IInvocationOperation? Next(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        return Unwrap(receiver) as IInvocationOperation;
    }

    // Parentheses, an implicit conversion (bool to bool?) and a null-forgiving `!` are all part of the
    // same expression, so they pass straight through.
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
