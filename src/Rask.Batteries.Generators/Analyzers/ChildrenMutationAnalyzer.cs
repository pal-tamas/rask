using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK180 — <c>Children&lt;TEntity, TSpec&gt;.Add</c> or <c>.Remove</c> is called outside the type that declares the
///     property holding them.
/// </summary>
/// <remarks>
///     <para>
///         The children are the aggregate's: everyone reads them, and only the aggregate changes them, through its own
///         methods, where its rules are. <c>Add</c> and <c>Remove</c> are public because C# has no "only my holder" access;
///         this says it instead. A call is allowed when its receiver is a property or field of <c>Children</c> declared by
///         the calling type, or by a type that type is nested in — <c>Parts.Add(part)</c>, <c>this.Parts.Remove(p)</c>,
///         <c>other.Parts.Add(p)</c> between two of the same aggregate. A local, a parameter or a method's result holding
///         the children is no receiver this can trace to its declaring type, so it is refused too.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChildrenMutationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask180 = new(
        "RASK180",
        "Children are added or removed outside their aggregate",
        "'{0}' changes the children of {1} from outside it — add and remove them in a method of the aggregate, "
        + "which keeps its rules",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Children<TEntity, TSpec>.Add and Remove are the aggregate's own: call them only from the type that "
                     + "declares the Children property (or a type nested in it). Everywhere else, call the aggregate's "
                     + "method that changes them.",
        helpLinkUri: DiagnosticHelp.Link("RASK180"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask180);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeCall, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeReference, OperationKind.MethodReference);
    }

    private static void AnalyzeCall(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        Check(context, call.TargetMethod, call.Instance, call.Syntax);
    }

    // Parts.Add handed over as a delegate — specs.ForEach(Parts.Add) — is the same call.
    private static void AnalyzeReference(OperationAnalysisContext context)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        Check(context, reference.Method, reference.Instance, reference.Syntax);
    }

    private static void Check(OperationAnalysisContext context, IMethodSymbol method, IOperation? receiver, SyntaxNode at)
    {
        if (method.Name is not ("Add" or "Remove") || !IsChildren(method.ContainingType))
        {
            return;
        }

        var declaring = DeclaringTypeOf(receiver);
        for (var container = context.ContainingSymbol.ContainingType; container is not null; container = container.ContainingType)
        {
            if (declaring is not null && SymbolEqualityComparer.Default.Equals(container.OriginalDefinition, declaring.OriginalDefinition))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask180,
            at.GetLocation(),
            method.Name,
            declaring is null ? "an aggregate, read through a local or a call that is no member of it," : "'" + declaring.Name + "'"));
    }

    // The type that declares the property or field the children are read from; null for any other receiver.
    private static INamedTypeSymbol? DeclaringTypeOf(IOperation? receiver) =>
        (receiver is IConversionOperation { IsImplicit: true } conversion ? conversion.Operand : receiver) switch
        {
            IPropertyReferenceOperation property => property.Property.ContainingType,
            IFieldReferenceOperation field => field.Field.ContainingType,
            _ => null,
        };

    private static bool IsChildren(INamedTypeSymbol? type) =>
        type is { Name: "Children", Arity: 2 } &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);
}
