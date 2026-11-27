using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK148 — an entity's <c>Id</c> is set to <c>Guid.CreateVersion7()</c> or <c>Guid.NewGuid()</c>, directly or inside a
///     typed id (<c>new OrderId(Guid.CreateVersion7())</c>).
/// </summary>
/// <remarks>
///     <c>Guid.CreateVersion7()</c> fills the 12 bits after the millisecond at random, so two rows made in one millisecond
///     sort either way, and <c>Guid.NewGuid()</c> is not time-ordered at all: the key's index stops being append-only and
///     "newest first" by the key is wrong. <c>OrderedGuid.New()</c> — what a typed id's generated <c>New()</c> returns — is
///     strictly increasing. A suggestion by default; an error under <c>&lt;RaskStrict&gt;</c>. Generated code is not analyzed.
///     The quick-fix writes the typed id's <c>New()</c>, or <c>OrderedGuid.New()</c> — <c>OrderedGuid.New(time)</c> for
///     <c>Guid.CreateVersion7(time.GetUtcNow())</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrderedKeyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask148 = new(
        "RASK148",
        "An entity is keyed by a Guid that is not strictly increasing",
        "'{0}' keys '{1}' with '{2}', which two rows made in one millisecond do not sort by — use '{3}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Guid.CreateVersion7() fills the 12 bits after the millisecond at random, so ids made in one "
                     + "millisecond sort either way, and Guid.NewGuid() is not time-ordered at all. A typed id's New() "
                     + "(OrderId.New()) and OrderedGuid.New() for a plain Guid key are version-7 Guids that are strictly "
                     + "increasing, so the key's index stays append-only and ordering by the key is the order the rows "
                     + "were made. A suggestion by default; an error under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK148"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask148);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.OrderedGuid") is null ||
                start.Compilation.GetTypeByMetadataName("Rask.Data.Entity`1") is not { } entity)
            {
                return;
            }

            start.RegisterOperationAction(operation => Analyze(operation, entity), OperationKind.SimpleAssignment);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol entityBase)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation { Property: { Name: "Id", IsStatic: false } property } ||
            !SymbolEqualityComparer.Default.Equals(property.OriginalDefinition.ContainingType, entityBase) ||
            property.ContainingType.TypeArguments.Length != 1)
        {
            return;
        }

        var idType = property.ContainingType.TypeArguments[0];

        var value = Unwrapped(assignment.Value);
        var creation = value as IObjectCreationOperation;
        if (creation is { Arguments.Length: 1 } && SymbolEqualityComparer.Default.Equals(creation.Type, idType))
        {
            value = Unwrapped(creation.Arguments[0].Value);
        }
        else
        {
            creation = null;
        }

        if (value is not IInvocationOperation { TargetMethod: { IsStatic: true, Name: "CreateVersion7" or "NewGuid" } made } call ||
            !string.Equals(made.ContainingType.ToDisplayString(), "System.Guid", System.StringComparison.Ordinal))
        {
            return;
        }

        // The node the fix replaces, and what with: a typed id that makes its own key is the id's New() as a whole; any
        // other key is the Guid alone, a TimeProvider's now staying its time source.
        var (at, replacement) = call.Arguments.Length == 0 && creation is not null && TypedIdNew.Has(idType)
            ? (creation.Syntax, idType.Name + ".New()")
            : (call.Syntax, "OrderedGuid.New(" + TimeSource(call) + ")");
        var keyed = context.ContainingSymbol.ContainingType?.Name ?? property.ContainingType.Name;
        context.ReportDiagnostic(Diagnostic.Create(
            Rask148, at.GetLocation(), keyed, assignment.Target.Syntax.ToString(), "Guid." + made.Name + "()", replacement));
    }

    // `time.GetUtcNow()` passed to CreateVersion7: `time`, so the id keeps that clock. Any other moment: `timeProvider`,
    // naming what to pass, as there is nothing the fix could keep.
    private static string TimeSource(IInvocationOperation call)
    {
        if (call.Arguments.Length == 0)
        {
            return "";
        }

        return Unwrapped(call.Arguments[0].Value) is IInvocationOperation
        {
            TargetMethod: { Name: "GetUtcNow", Parameters.Length: 0 } getUtcNow, Instance: { } time,
        } && IsTimeProvider(getUtcNow.ContainingType)
            ? time.Syntax.ToString()
            : "timeProvider";
    }

    private static bool IsTimeProvider(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.ToDisplayString(), "System.TimeProvider", System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation? Unwrapped(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
