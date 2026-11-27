using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK145 — an aggregate raises an event that reads the <c>Id</c> of an entity the code has only just made, whose key
///     the database produces on insert: raised before the first save, the event carries <c>0</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>Raise(new OrderPlaced(Id))</c> takes the value <c>Id</c> holds when it runs. An integer key (or a typed id
///         over one) is the store's identity, which EF Core keeps as a temporary value in its own entry until the insert,
///         so the entity's <c>Id</c> is still <c>0</c> — and so is the event, for good: the handlers that run after the
///         commit are handed the event as raised. A key the entity makes itself (a version-7 <c>Guid</c>, a typed id over
///         one made with <c>Create()</c>) is there from the start.
///     </para>
///     <para>
///         Reported only where the entity is certainly new, so an operation on a stored aggregate — whose <c>Id</c> is
///         its row's — never is:
///         <list type="bullet">
///             <item><c>Id</c> (or <c>this.Id</c>) read in the aggregate's constructor;</item>
///             <item>
///                 <c>x.Id</c> of a local the same method made — <c>var x = new T(…)</c>, <c>new() { … }</c>, or a static
///                 factory of <c>T</c> (<c>var line = OrderLine.For(…)</c>): the aggregate a factory builds, or a child an
///                 operation adds.
///             </item>
///         </list>
///         The key is the store's when it is an integer the entity's own <c>Configure</c> does not mark
///         <c>ValueGeneratedNever()</c>, or any key it marks <c>ValueGeneratedOnAdd()</c>. An instance method the factory
///         calls (<c>order.Open()</c> raising with <c>Id</c>) is not followed: it also runs on stored aggregates.
///     </para>
///     <para>A warning by default; an error under <c>&lt;RaskStrict&gt;</c>.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StoreKeyEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask145 = new(
        "RASK145",
        "An event reads a key the database has not produced yet",
        "The event raised in '{0}' reads '{1}', which the database produces only on insert: raised before the first save, it carries 0 — give '{2}' a client-generated key (a typed id over a Guid, made with Create())",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Raise(new X(Id)) takes the value Id holds when it runs. An integer key, or a typed id over one, is the "
                     + "store's identity: EF Core keeps a temporary value in its own entry until the insert, so the entity's "
                     + "Id is still 0, and the event the handlers are handed after the commit carries 0. Reported where the "
                     + "entity is certainly new: Id read in the aggregate's constructor, or x.Id of a local the same method "
                     + "made with new or with a static factory of its type. Key the entity on the client instead: "
                     + "a typed id over a Guid (public readonly partial record struct OrderId(Guid Value)) made with "
                     + "OrderId.Create(), a time-ordered version-7 Guid. A warning by default; an error under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK145"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask145);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Data.Aggregate`1") is not { } aggregate ||
                start.Compilation.GetTypeByMetadataName("Rask.Data.Entity`1") is not { } entity)
            {
                return;
            }

            start.RegisterOperationAction(operation => Analyze(operation, aggregate, entity), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol aggregateBase, INamedTypeSymbol entityBase)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: "Raise", Parameters.Length: 1 } raise ||
            !SymbolEqualityComparer.Default.Equals(raise.ContainingType.OriginalDefinition, aggregateBase) ||
            call.Arguments.Length != 1)
        {
            return;
        }

        foreach (var read in call.Arguments[0].Value.DescendantsAndSelf().OfType<IPropertyReferenceOperation>())
        {
            if (read.Property is { Name: "Id", IsStatic: false } property &&
                SymbolEqualityComparer.Default.Equals(property.ContainingType.OriginalDefinition, entityBase) &&
                NewEntityOf(context, read.Instance) is { } made &&
                AggregateShape.TryGetIdType(made, out var idType) && idType is not null &&
                KeyConfiguration.IsStoreGenerated(made, idType, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask145, read.Syntax.GetLocation(), context.ContainingSymbol.ContainingType?.Name, read.Syntax.ToString(), made.Name));
            }
        }
    }

    // The entity the read is of, when the code certainly made it just now; null for any other.
    private static INamedTypeSymbol? NewEntityOf(OperationAnalysisContext context, IOperation? instance)
    {
        switch (Unwrapped(instance))
        {
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } self
                when context.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor }:
                return self.Type as INamedTypeSymbol;
            case ILocalReferenceOperation local when local.Local.Type is INamedTypeSymbol type && IsMadeHere(context, local.Local, type):
                return type;
            default:
                return null;
        }
    }

    // `var x = new T(…)`, `T x = new() { … }` or `var x = T.Factory(…)`: the local starts as an entity this code made.
    private static bool IsMadeHere(OperationAnalysisContext context, ILocalSymbol local, INamedTypeSymbol type)
    {
        var semanticModel = context.Operation.SemanticModel;
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax { Initializer.Value: { } value } ||
                semanticModel is null || value.SyntaxTree != semanticModel.SyntaxTree)
            {
                continue;
            }

            var made = Unwrapped(semanticModel.GetOperation(value, context.CancellationToken));
            if (made is IObjectCreationOperation creation && SymbolEqualityComparer.Default.Equals(creation.Type, type))
            {
                return true;
            }

            if (made is IInvocationOperation { TargetMethod: { IsStatic: true } factory } &&
                SymbolEqualityComparer.Default.Equals(factory.ContainingType, type) &&
                SymbolEqualityComparer.Default.Equals(factory.ReturnType, type))
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
