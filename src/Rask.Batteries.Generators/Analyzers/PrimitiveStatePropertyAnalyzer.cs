using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK130 — an aggregate or entity stores a bare text, number, moment or Guid where a value object would carry
///     its rules: <c>public string Name { get; private set; }</c> instead of <c>public DriverName Name { … }</c>.
/// </summary>
/// <remarks>
///     A suggestion by default (Info), so it never breaks a build; an app that sets <c>&lt;RaskStrict&gt;</c> gets
///     it as a warning from <c>rask-strict.globalconfig</c>. The framework's own members (<c>Id</c>, <c>Version</c>,
///     the timestamps, <c>TenantId</c>) are not reported, nor a <c>bool</c>, an enum, a strongly-typed id, a value
///     object, a collection, or a property computed from others.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrimitiveStatePropertyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask130 = new(
        "RASK130",
        "An aggregate stores a bare value where a value object would hold its rules",
        "'{0}.{1}' is a bare '{2}' — {3}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A property of an aggregate or entity typed string, a number, DateTime, DateTimeOffset, DateOnly, "
                     + "TimeOnly or Guid holds whatever it is given. A value object declared from a Rask.Data base "
                     + "record — public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100) — "
                     + "checks its rules as it is made, gives its column its length, and a form its maxlength. A "
                     + "suggestion by default; a warning under <RaskStrict>. A Guid holds no rules: it is a reference to "
                     + "another row, typed by a strongly-typed id — public readonly partial record struct AccountId(Guid Value).",
        helpLinkUri: DiagnosticHelp.Link("RASK130"));

    private const string DeclareValueObject =
        "declare a value object for it (Text, Number, Integer, Day, Range or Value), so its rules are checked wherever one is made";

    // A Guid is a reference to another row: no value-object base holds one, and a typed id is what keeps it apart.
    private const string DeclareTypedId =
        "declare a strongly-typed id for it (public readonly partial record struct …Id(Guid Value)), so it is never mistaken for another id";

    // The framework's own state on every entity: never the app's to wrap.
    private static readonly ImmutableHashSet<string> FrameworkMembers = ImmutableHashSet.Create(
        System.StringComparer.Ordinal, "Id", "Version", "CreatedAt", "UpdatedAt", "DeletedAt", AggregateShape.TenantColumn);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask130);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // A DateTime or DateTimeOffset is RASK147's where the app has Moment and Day to hold it: one report, one fix.
            var timeIsRask146 = start.Compilation.GetTypeByMetadataName("Rask.Values.Moment") is not null;
            start.RegisterSymbolAction(symbol => Analyze(symbol, timeIsRask146), SymbolKind.Property);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, bool timeIsRask146)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property is not { IsStatic: false, IsIndexer: false, IsOverride: false, DeclaredAccessibility: Accessibility.Public } ||
            FrameworkMembers.Contains(property.Name) ||
            !property.ExplicitInterfaceImplementations.IsEmpty ||
            !AggregateShape.IsEntity(property.ContainingType) ||
            Bare(property.Type) is not { } bare ||
            (timeIsRask146 && IsTime(property.Type)) ||
            !IsStored(property))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask130,
            property.Locations.FirstOrDefault(),
            property.ContainingType.Name,
            property.Name,
            bare.Name,
            bare.IsGuid ? DeclareTypedId : DeclareValueObject));
    }

    private static bool IsTime(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;
        return held.SpecialType == SpecialType.System_DateTime ||
               held is { Name: "DateTimeOffset", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
    }

    // The type's name when it is one a value object or a typed id would wrap, and whether it is a Guid; null for anything else.
    private static (string Name, bool IsGuid)? Bare(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        var bare = held.SpecialType switch
        {
            SpecialType.System_String or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                or SpecialType.System_DateTime => true,
            _ => held is { Name: "DateTimeOffset" or "DateOnly" or "TimeOnly" or "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } },
        };

        return bare
            ? (type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), held is { Name: "Guid", SpecialType: SpecialType.None })
            : null;
    }

    // A setter of any visibility, or the compiler's field behind a get-only auto-property: a computed one is not state.
    private static bool IsStored(IPropertySymbol property) =>
        property.SetMethod is not null ||
        property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
}
