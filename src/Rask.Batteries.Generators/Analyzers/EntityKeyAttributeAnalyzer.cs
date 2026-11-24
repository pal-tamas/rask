using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK143 — <c>[Key]</c> on a member of an aggregate or an entity, whose key is the <c>Id</c> it inherits from
///     <c>Entity&lt;TId&gt;</c>.
/// </summary>
/// <remarks>
///     EF Core honours the attribute and keys the table on the marked member as well, so the row is no longer found by
///     the <c>Id</c> every generated read, write and route uses. A value that may not repeat is a unique index in the
///     aggregate's <c>Configure</c>; a child keyed by the aggregates it joins is <c>Entity&lt;(A, B)&gt;</c>. A suggestion
///     by default; a warning under <c>&lt;RaskStrict&gt;</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityKeyAttributeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask143 = new(
        "RASK143",
        "[Key] on an aggregate or an entity",
        "'{0}.{1}' is marked [Key], but '{0}' is keyed by the Id it inherits — remove the attribute, and give a value that may not repeat a unique index in Configure",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every aggregate and entity is keyed by the Id it inherits from Entity<TId>, and every generated "
                     + "read, write and route finds a row by it. A [Key] on another member makes EF Core key the "
                     + "table on that member too. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK143"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask143);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.KeyAttribute") is not { } key)
            {
                return;
            }

            start.RegisterSymbolAction(context => Analyze(context, key), SymbolKind.Property, SymbolKind.Field);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol key)
    {
        var member = context.Symbol;
        if (member.IsStatic || !AggregateShape.IsEntity(member.ContainingType) ||
            member.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, key)) is not { } marked)
        {
            return;
        }

        var at = marked.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? member.Locations.FirstOrDefault();
        context.ReportDiagnostic(Diagnostic.Create(Rask143, at, member.ContainingType.Name, member.Name));
    }
}
