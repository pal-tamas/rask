using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK106 — an aggregate or a child entity is made with <c>new</c> outside the type that guards it.
/// </summary>
/// <remarks>
///     <para>
///         An aggregate keeps its invariants by being made in one place: its own factory, or the generated
///         <c>X.Create(…)</c>. A private parameterless constructor used to say so; this says it instead, so the
///         constructor need not be written. A child is made by its own type or by the entity that holds it.
///     </para>
///     <para>
///         Generated code is not analyzed: the generated writes start from <c>new X()</c> on purpose. EF Core builds
///         a row through reflection, not through <c>new</c>, so it never meets this rule.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityCreationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask106 = new(
        "RASK106",
        "An entity is created outside the type that guards it",
        "'{0}' is {1}, so it is made only {2} — call its factory or the generated '{0}.Create(…)' instead of 'new'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An aggregate keeps its invariants by being made in one place: a member of its own type (a "
                     + "static factory) or the generated Create. A child entity is made by its own type or by the "
                     + "entity that holds it. EF Core builds rows through reflection and is not affected.",
        helpLinkUri: DiagnosticHelp.Link("RASK106"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask106);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.ObjectCreation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol created || !AggregateShape.IsMappedEntity(created))
        {
            return;
        }

        var aggregate = AggregateShape.IsAggregate(created);
        for (var container = context.ContainingSymbol.ContainingType; container is not null; container = container.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(container, created) || (!aggregate && Holds(container, created)))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask106,
            creation.Syntax.GetLocation(),
            created.Name,
            aggregate ? "an aggregate" : "a child entity",
            aggregate ? "inside '" + created.Name + "'" : "inside '" + created.Name + "' or the entity that holds it"));
    }

    // Whether the entity holds the child as a collection or a single child of its own.
    private static bool Holds(INamedTypeSymbol holder, INamedTypeSymbol child) =>
        AggregateShape.IsEntity(holder) &&
        holder.GetMembers().OfType<IPropertySymbol>().Any(p =>
            GeneratedModelShape.DescribeChild(holder, p) is { } held &&
            SymbolEqualityComparer.Default.Equals(held.ChildType, child));
}
