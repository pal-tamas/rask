using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK046 — <c>Key</c> must be the first step of a COMPONENT's chain.
///     <para>
///         Since #685 a keyed child is identified by its key rather than by its position among its
///         siblings, which is what stops a keyed row's own state — private fields, an <c>OnMount</c>
///         subscription — following its POSITION when an item is inserted above it. Settling that identity
///         means handing back the instance the key owns and discarding the one the entry just built, so a
///         step written BEFORE <c>Key</c> lands on the discarded instance and is silently lost.
///     </para>
///     <para>
///         Elements are exempt and that is not a carve-out: an element is fully re-specified by its chain
///         every render (whatever the chain does not name, the deferred reset puts back), so it is never
///         claimed and nothing can be lost. <c>Div.Class("row").Key(i)</c> stays exactly as it reads.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KeyOpensChainAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string ElementFullName = "Rask.Core.Element";

    private static readonly DiagnosticDescriptor Rask046 = new(
        "RASK046",
        "Key must open a component's chain",
        "'Key' comes after '{0}' on this chain. Key decides WHICH instance is being built, so '{0}' is written to the "
        + "instance the key then discards — move '.Key(…)' to the front of the chain.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A keyed child is identified by its key rather than by its position among its siblings, so that "
                     + "the state a row holds itself moves with the item rather than with the slot. Settling that "
                     + "identity hands back the instance the key owns and discards the one the entry built — so any "
                     + "step written before Key is applied to a component that is about to be thrown away. It "
                     + "compiles and it renders; the value simply goes missing, and only when the list changes shape. "
                     + "Elements are exempt: they are re-specified in full every render and are never claimed.",
        helpLinkUri: DiagnosticHelp.Link("RASK046"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask046);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(ComponentFullName);
            if (component is null)
            {
                return;
            }

            var element = start.Compilation.GetTypeByMetadataName(ElementFullName);
            start.RegisterOperationAction(ctx => Analyze(ctx, component, element), OperationKind.Invocation);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        INamedTypeSymbol component,
        INamedTypeSymbol? element)
    {
        var operation = (IInvocationOperation)context.Operation;
        if (operation.TargetMethod.Name != "Key")
        {
            return;
        }

        // What is being built. The setter's receiver is Build<T>, so T is the component the chain owns.
        var built = BuiltType(operation);
        if (built is null || !DerivesFrom(built, component))
        {
            return;
        }

        // An element is never claimed, so nothing before its Key can be lost — see the type doc.
        if (element is not null && DerivesFrom(built, element))
        {
            return;
        }

        // The step immediately below this one. An entry is a PROPERTY, not an invocation, so a chain that
        // opens with Key has no invocation underneath it and is exactly what this wants to see.
        if (Receiver(operation) is not IInvocationOperation previous)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask046, operation.Syntax.GetLocation(), previous.TargetMethod.Name));
    }

    // The T of the Build<T> this call hands back.
    private static ITypeSymbol? BuiltType(IInvocationOperation operation) =>
        operation.TargetMethod.ReturnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } build
            ? build.TypeArguments[0]
            : null;

    // The next link DOWN the chain, matching DuplicateChainCallAnalyzer: a setter's receiver is whatever
    // produced it, written as an extension (argument 0) or as an instance call.
    private static IOperation? Receiver(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        return Unwrap(receiver);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            operation = operation is IParenthesizedOperation p ? p.Operand : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol target)
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
}
