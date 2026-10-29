using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK113 — a child entity implements a trait only an aggregate root carries.
/// </summary>
/// <remarks>
///     <para>
///         A child entity — <c>OrderLine : Entity&lt;Guid&gt;</c> — may keep its own timestamps (<c>ICreated</c>,
///         <c>IUpdated</c>, <c>ITimestamps</c>). Everything else is the aggregate's: a form model, a delete, the
///         tenant, a broadcast. <c>IVersioned</c> and <c>ISoftDeletable</c> need no rule here — a child has no
///         <c>Version</c> or <c>DeletedAt</c> to implement them with, so the compiler refuses them first.
///     </para>
///     <para>
///         Reported where the trait is written: on the base that writes it, not on every type that inherits it.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChildEntityTraitAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask113 = new(
        "RASK113",
        "A child entity implements a trait of an aggregate root",
        "'{0}' is a child entity, so it cannot implement {1} — move it to the aggregate root, or derive '{0}' from 'Aggregate<TId>'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Only the timestamps (ICreated, IUpdated, ITimestamps) belong on a child entity. A form model, a "
                     + "delete, the tenant and a broadcast are the aggregate root's, and the child takes them from it.",
        helpLinkUri: DiagnosticHelp.Link("RASK113"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask113);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!AggregateShape.IsEntity(type) || AggregateShape.IsAggregate(type))
        {
            return;
        }

        var traits = AggregateShape.AggregateOnlyTraitsDeclaredOn(type).ToArray();
        if (traits.Length == 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask113,
            type.Locations.FirstOrDefault(),
            type.Name,
            string.Join(", ", traits.Select(static t => "'" + t + "'"))));
    }
}
