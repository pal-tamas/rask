using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK146 — a trait the class cannot carry: an aggregate's on a child entity, two that contradict each other, or a
///     tenant that is not the app's.
/// </summary>
/// <remarks>
///     <para>
///         The traits are marker interfaces read like a sentence — <c>Company : Aggregate&lt;CompanyId&gt;,
///         IBelongsTo&lt;Tenant&gt;, ISoftDeletes, IHasForm</c> — so the compiler takes any of them on any class. The rules
///         they had as type arguments of the base (an aggregate's trait refused on <c>Entity&lt;TId, …&gt;</c>) are
///         this analyzer's: an error, because the model would otherwise quietly ignore what the class says.
///     </para>
///     <para>
///         <c>IBelongsTo&lt;T&gt;</c> names the tenant aggregate, and the one that says <c>ITenant</c> is the app's: a
///         table belonging to another aggregate would be filtered by a key that names no tenant.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TraitShapeAnalyzer : DiagnosticAnalyzer
{
    private const string RaskData = "Rask.Data";
    private const string BelongsTo = "IBelongsTo";
    private const string BelongsThrough = "IBelongsToTenantThrough";

    // The traits only an aggregate root carries; a child or a plain entity has the timestamps and ISoftDeletes.
    private static readonly ImmutableHashSet<string> AggregateOnly = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "IVersioned", "IHasForm", "IHasCreateForm", "IHasUpdateForm", "IDeletable", BelongsTo, BelongsThrough, "IBroadcasts");

    private static readonly DiagnosticDescriptor Rask146 = new(
        "RASK146",
        "A trait the class cannot carry",
        "'{0}' says {1}, but {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A trait is a marker interface on the class, read by the build. An aggregate's trait on a child entity, "
                     + "IBelongsTo beside IBelongsToTenantThrough, IDeletable beside ISoftDeletes, and IBelongsTo<T> naming "
                     + "an aggregate other than the one that says ITenant are each a table the model would build otherwise "
                     + "than the class reads.",
        helpLinkUri: DiagnosticHelp.Link("RASK146"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask146);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // The app's tenant is looked for only when a table names an aggregate that does not say ITenant: an app's
            // tables name the tenant, so the walk is a test assembly's, once.
            var compilation = start.Compilation;
            var theTenant = new Lazy<INamedTypeSymbol?>(() => TheTenant(compilation.Assembly.GlobalNamespace));
            start.RegisterSymbolAction(context => Analyze(context, theTenant), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, Lazy<INamedTypeSymbol?> theTenant)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type)
        {
            return;
        }

        var traits = type.AllInterfaces.Where(static face => IsRaskData(face)).ToList();
        var names = new HashSet<string>(traits.Select(static face => face.Name), StringComparer.Ordinal);
        var location = type.Locations.FirstOrDefault();

        if (AggregateShape.IsEntity(type) && !AggregateShape.IsAggregate(type) &&
            names.FirstOrDefault(AggregateOnly.Contains) is { } aggregateOnly)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask146, location, type.Name, aggregateOnly,
                "it is a child entity, and only an aggregate root carries that; say it on its aggregate"));
        }

        if (names.Contains(BelongsTo) && names.Contains(BelongsThrough))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask146, location, type.Name, BelongsTo,
                "it says IBelongsToTenantThrough as well; a table keeps its own tenant or reads its parent's, so say one"));
        }

        if (names.Contains("IDeletable") && names.Contains("ISoftDeletes"))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask146, location, type.Name, "IDeletable",
                "it says ISoftDeletes as well, which is the generated Delete already; drop IDeletable"));
        }

        if (traits.FirstOrDefault(static face => IsNamed(face, BelongsTo) && face.TypeArguments.Length == 1)?.TypeArguments[0] is INamedTypeSymbol tenant &&
            !SaysTenant(tenant) &&
            theTenant.Value is { } appTenant)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask146, location, type.Name, $"IBelongsTo<{tenant.Name}>",
                $"'{appTenant.Name}' is the app's tenant, the aggregate that says ITenant; say IBelongsTo<{appTenant.Name}>"));
        }
    }

    // The one aggregate of this assembly that says ITenant, or null for none or several (RASK113 says that).
    private static INamedTypeSymbol? TheTenant(INamespaceSymbol root)
    {
        INamedTypeSymbol? found = null;
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var member in pending.Pop().GetMembers())
            {
                if (member is INamespaceSymbol space)
                {
                    pending.Push(space);
                }
                else if (member is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } candidate &&
                         SaysTenant(candidate) && AggregateShape.IsAggregate(candidate))
                {
                    if (found is not null)
                    {
                        return null;
                    }

                    found = candidate;
                }
            }
        }

        return found;
    }

    private static bool SaysTenant(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(static face => IsNamed(face, "ITenant") && IsRaskData(face));

    private static bool IsNamed(INamedTypeSymbol face, string name) => string.Equals(face.Name, name, StringComparison.Ordinal);

    private static bool IsRaskData(INamedTypeSymbol face) =>
        string.Equals(face.ContainingNamespace?.ToDisplayString(), RaskData, StringComparison.Ordinal);
}
