using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK147 — the domain holds a bare <c>DateTime</c> or <c>DateTimeOffset</c>: a stored property or a field of an
///     aggregate, an entity, a value object, a domain event (an <c>IEvent</c>) or a read model (an <c>IReadModel</c>).
/// </summary>
/// <remarks>
///     <para>
///         Rask holds time one way: a <c>Moment</c> for an instant (stored as UTC on every database) and a <c>Day</c> for
///         a calendar day. A bare <c>DateTime</c> says neither which it is nor which clock it was read on — a wall-clock
///         time, a UTC one, the machine's — and PostgreSQL refuses one that is not UTC only as the row is saved. A
///         wall-clock time from outside becomes a moment where it comes in: <c>FuelingMoment.FromWall(local, zone)</c>.
///     </para>
///     <para>
///         Stored state only: an auto-property, a record's positional member, a property with a setter, a field. A
///         property computed from others, a static member and a constant are left alone, and so is the one value a
///         record declared from <c>Moment</c> hands its base. No name and no namespace is exempt: Rask's own tables hold
///         their times as a <c>Timestamp</c> (a <c>Moment</c>), as an app's do. A warning; an error under
///         <c>&lt;RaskStrict&gt;</c>. Reported only when the compilation has <c>Rask.Values.Moment</c>, so the quick-fix
///         always has somewhere to point.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainTimeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The property bag key that says the member reads as a day, so the quick-fix offers a <c>Day</c> first.</summary>
    public const string ReadsAsDay = "ReadsAsDay";

    private static readonly DiagnosticDescriptor Rask146 = new(
        "RASK147",
        "The domain holds a bare DateTime",
        "'{0}' in '{1}' is a bare {2} — hold an instant as a Moment and a calendar day as a Day",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Aggregates, entities, value objects, domain events and read models hold time one way: a Moment "
                     + "(an instant, stored as UTC on every database) or a Day (a calendar day). A bare DateTime or "
                     + "DateTimeOffset says neither which it is nor which clock it was read on, and PostgreSQL refuses a "
                     + "DateTime that is not UTC only when the row is saved. A wall-clock time from outside becomes a "
                     + "Moment where it comes in, with FromWall(local, zone). A warning; an error under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK147"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask146);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Values.Moment") is null)
            {
                return;
            }

            start.RegisterSymbolAction(Analyze, SymbolKind.Property, SymbolKind.Field);
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var member = context.Symbol;
        if (member.IsStatic || member.IsImplicitlyDeclared || member is IFieldSymbol { IsConst: true } ||
            HandsItsBase(member) ||
            TimeOf(member) is not { } time || !IsDomain(member.ContainingType) || !IsStored(member, context.CancellationToken))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (ReadsAsADay(member.Name))
        {
            properties = properties.Add(ReadsAsDay, "true");
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask146, member.Locations.FirstOrDefault(), properties, member.Name, member.ContainingType.Name, time));
    }

    // "DateTime" or "DateTimeOffset" for a member of that type, nullable or not; null for any other.
    private static string? TimeOf(ISymbol member)
    {
        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return type switch
        {
            { SpecialType: SpecialType.System_DateTime } => "DateTime",
            INamedTypeSymbol { Name: "DateTimeOffset", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } => "DateTimeOffset",
            _ => null,
        };
    }

    private static bool IsDomain(INamedTypeSymbol? type) =>
        type is not null &&
        (AggregateShape.IsEntity(type) || AggregateShape.IsValueRecord(type) ||
         type.AllInterfaces.Any(static i => IsNamed(i, "Rask.Cqrs", "IEvent") || IsNamed(i, "Rask.Data", "IReadModel")));

    // The one value a Moment holds is a DateTime by definition: Moment's own Value, and the positional member of a record
    // declared from it that feeds it (`record FueledAt(DateTime Value) : Moment(Value)`).
    private static bool HandsItsBase(ISymbol member)
    {
        if (!string.Equals(member.Name, "Value", System.StringComparison.Ordinal))
        {
            return false;
        }

        for (INamedTypeSymbol? type = member.ContainingType; type is not null; type = type.BaseType)
        {
            if (type is { Name: "Moment", ContainingNamespace: { Name: "Values", ContainingNamespace: { Name: "Rask", ContainingNamespace.IsGlobalNamespace: true } } })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNamed(INamedTypeSymbol type, string ns, string name) =>
        string.Equals(type.Name, name, System.StringComparison.Ordinal) &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), ns, System.StringComparison.Ordinal);

    // State the type keeps: a field, a record's positional member, a property with a setter, or an auto-property.
    private static bool IsStored(ISymbol member, System.Threading.CancellationToken cancellationToken)
    {
        if (member is IFieldSymbol)
        {
            return true;
        }

        var property = (IPropertySymbol)member;
        if (property.SetMethod is not null)
        {
            return true;
        }

        return property.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).Any(static syntax => syntax switch
        {
            ParameterSyntax => true,
            PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors } =>
                accessors.Accessors.All(static a => a.Body is null && a.ExpressionBody is null),
            _ => false,
        });
    }

    // A name that says a day — DueDate, PayDay, DeliveredOn — so the quick-fix offers a Day first.
    private static bool ReadsAsADay(string name) =>
        name.EndsWith("Date", System.StringComparison.Ordinal) ||
        name.EndsWith("Day", System.StringComparison.Ordinal) ||
        (name.EndsWith("On", System.StringComparison.Ordinal) && name.Length > 2);
}
