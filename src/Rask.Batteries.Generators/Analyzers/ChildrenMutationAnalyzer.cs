using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK180 — a <c>HasMany&lt;TChild&gt;</c> is changed (<c>Add</c>, <c>Remove</c>, <c>RemoveAll</c>, <c>Clear</c>,
///     <c>Sync</c>) or cast to <c>ICollection&lt;T&gt;</c> outside the type that declares the property holding it.
/// </summary>
/// <remarks>
///     <para>
///         The children are the aggregate's: everyone reads them, and only the aggregate changes them, through its own
///         methods, where its rules are. The mutators are public because C# has no "only my holder" access; this says it
///         instead. A call is allowed when its receiver is a property or field of <c>HasMany</c> declared by the calling
///         type, or by a type that type is nested in — <c>Parts.Add(part)</c>, <c>this.Parts.Remove(p)</c>,
///         <c>other.Parts.Add(p)</c> between two of the same aggregate. A local, a parameter or a method's result holding
///         the children is no receiver this can trace to its declaring type, so it is refused too. A cast to
///         <c>ICollection&lt;T&gt;</c> is the same door by another name, and is held to the same rule.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChildrenMutationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask180 = new(
        "RASK180",
        "Children are changed outside their aggregate",
        "'{0}' changes the children of {1} from outside it — change them in a method of the aggregate, which keeps "
        + "its rules",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HasMany<TChild>.Add, Remove, RemoveAll, Clear and Sync are the aggregate's own, and so is the "
                     + "ICollection<T> it is: call them, or cast to it, only from the type that declares the HasMany "
                     + "property (or a type nested in it). Everywhere else, call the aggregate's method that changes them.",
        helpLinkUri: DiagnosticHelp.Link("RASK180"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask180);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeCall, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeReference, OperationKind.MethodReference);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    private static void AnalyzeCall(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (IsMutator(call.TargetMethod))
        {
            Check(context, call.TargetMethod.Name, call.Instance, call.Syntax);
        }
    }

    // Parts.Add handed over as a delegate — specs.ForEach(Parts.Add) — is the same call.
    private static void AnalyzeReference(OperationAnalysisContext context)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        if (IsMutator(reference.Method))
        {
            Check(context, reference.Method.Name, reference.Instance, reference.Syntax);
        }
    }

    // (ICollection<Part>)s.Parts, or s.Parts handed to an ICollection<Part> parameter: its Add is the HasMany's.
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (IsHasMany(conversion.Operand.Type as INamedTypeSymbol) &&
            conversion.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_ICollection_T } target)
        {
            Check(context, target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), conversion.Operand, conversion.Syntax);
        }
    }

    private static void Check(OperationAnalysisContext context, string what, IOperation? receiver, SyntaxNode at)
    {
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
            what,
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

    private static bool IsMutator(IMethodSymbol method) =>
        method.Name is "Add" or "Remove" or "RemoveAll" or "Clear" or "Sync" && IsHasMany(method.ContainingType);

    private static bool IsHasMany(INamedTypeSymbol? type) =>
        type is { Name: "HasMany", Arity: 1 } &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);
}
